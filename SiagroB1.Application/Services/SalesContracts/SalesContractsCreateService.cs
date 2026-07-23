using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsCreateService(
    IUnitOfWork db, 
    IBusinessPartnerService  businessPartnerService,
    IItemService itemService,
    IAgentService  agentService,
    DocNumberSequenceService numberSequenceService,
    ILogger<SalesContractsCreateService> logger)
{
    private static readonly TransactionCode TransactionCode = TransactionCode.SalesContract;

    public async Task<SalesContract> ExecuteAsync(SalesContract entity, string createdBy)
    {
        // Validação fora do try: o catch abaixo mascara toda exceção como "Unable to create
        // sales contract.", então a mensagem de negócio precisa escapar antes de entrar nele.
        if (entity.Type == ContractType.Fixed && entity.Price <= 0)
            throw new ApplicationException("Preço é obrigatório para contrato de preço fixo.");

        try
        {
            await db.BeginTransactionAsync();

            entity.Code = await numberSequenceService.GetDocNumber((Guid) entity.DocNumberKey);
            entity.CreatedAt = DateTime.Now;
            entity.CreatedBy = createdBy;
            // Uma leitura só do parceiro para as três colunas desnormalizadas.
            var partner = await businessPartnerService.GetByIdAsync(entity.CardCode);
            entity.CardName = partner?.CardName;
            entity.CardFName = partner?.CardFName;
            entity.CardTaxId = partner?.TaxId;
            entity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;
            entity.AgentName = (await agentService.GetByIdAsync((int) entity.AgentCode))?.Name;
            entity.Status = ContractStatus.Draft;

            if (entity.Type == ContractType.Fixed)
            {
                // Preço fixo: a fixação nasce Confirmed (o preço já foi acordado na negociação;
                // esta fixação é o espelho dele, não um pedido à diretoria) e reserva todo o
                // volume. Price é MANTIDO — é a base da reconciliação de alocação de contratos
                // fixos, que continua idêntica.
                await CreatePriceFixation(entity);
                entity.FixedVolume = entity.TotalVolume;
            }
            else
            {
                // Contrato a fixar (PAF): sem preço na negociação. Zerado no servidor porque
                // TotalPrice/reconciliação passam a derivar das fixações aprovadas pela
                // diretoria — a UI desabilita o campo, mas um POST direto não passa por ela.
                entity.Price = 0;
            }

            await db.Context.SalesContracts.AddAsync(entity);
            await db.SaveChangesAsync();

            await db.CommitAsync();
            return entity;
        }
        catch (Exception ex)
        {
            await db.RollbackAsync();
            logger.LogError(ex, ex.Message);
            throw new ApplicationException("Unable to create sales contract.");
        }
    }

    private async Task CreatePriceFixation(SalesContract entity)
    {
        var fixation = new SalesContractPriceFixation
        {
            SalesContract = entity,
            FixationDate = DateTime.Now.Date,
            FixationVolume = entity.TotalVolume,
            FixationPrice = entity.Price,
            // Confirmed, não InApproval: num contrato de preço fixo o preço já foi acordado
            // na negociação. Nascer InApproval zeraria TotalPrice (que conta só Confirmed) e
            // entulharia a fila de aprovação com item inaprovável.
            Status = PriceFixationStatus.Confirmed
        };

        await db.Context.SalesContractsPriceFixations.AddAsync(fixation);
    }
}