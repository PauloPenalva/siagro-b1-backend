using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;
using SiagroB1.Application.Services.MasterData;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsUpdateService(
    AppDbContext context,
    IBusinessPartnerService businessPartnerService,
    IItemService itemService,
    IAgentService agentService,
    ContractNotificationOutboxService notificationOutbox,
    ILogger<SalesContractsUpdateService> logger)
{
    public async Task<SalesContract?> ExecuteAsync(Guid key, SalesContract entity, string userName)
    {
        var existingEntity = await context.Set<SalesContract>()
            .FirstOrDefaultAsync(tc => tc.Key == key) ?? throw new KeyNotFoundException("Entity not found.");
        
        if (existingEntity.Status != ContractStatus.Draft)
        {
            throw new ApplicationException("You can only edit a sales contract if its status is draft.");
        }
        
        var entry = context.Entry(existingEntity);

        // O tipo é definido na criação e não muda mais: trocar FIX por PAF (ou o contrário)
        // desalinha o contrato da fixação de preço, que nasceu com regra diferente para cada um.
        // Tem de sair do OriginalValues: no PATCH o controller aplica o delta sobre a entidade
        // rastreada e a recarga pelo Key devolve essa mesma instância, então comparar `entity`
        // com `existingEntity` é comparar o objeto com ele mesmo e nunca acusa nada.
        var originalType = (ContractType)entry.OriginalValues[nameof(SalesContract.Type)]!;
        if (entity.Type != originalType)
        {
            throw new ApplicationException("O tipo do contrato não pode ser alterado após a criação.");
        }

        try
        {
            entry.CurrentValues.SetValues(entity);

            if (existingEntity.Type == ContractType.Fixed)
            {
                await UpdatePriceFixation(existingEntity);
            }

            // SaveAsync changes
            existingEntity.UpdatedAt = DateTime.Now;
            existingEntity.UpdatedBy = userName;
            // Uma leitura só do parceiro para as três colunas desnormalizadas.
            // GAC-1196: cadastro inativo no SAP só barra quando o código MUDOU nesta edição.
            var partner = InactiveRecordGuard.Partner(
                await businessPartnerService.GetByIdAsync(entity.CardCode),
                InactiveRecordGuard.Original<string>(entry, nameof(SalesContract.CardCode)), "O cliente");
            existingEntity.CardName = partner?.CardName;
            existingEntity.CardFName = partner?.CardFName;
            existingEntity.CardTaxId = partner?.TaxId;
            existingEntity.ItemName = InactiveRecordGuard.Item(
                await itemService.GetByIdAsync(entity.ItemCode),
                InactiveRecordGuard.Original<string>(entry, nameof(SalesContract.ItemCode)))?.ItemName;
            // Precisa ser `existingEntity`: o SetValues acima já copiou `entity`, então
            // gravar em `entity` aqui não chega ao registro rastreado e a coluna fica velha.
            existingEntity.AgentName = InactiveRecordGuard.Agent(
                await agentService.GetByIdAsync((int) entity.AgentCode),
                InactiveRecordGuard.Original<int?>(entry, nameof(SalesContract.AgentCode)))?.Name;

            // O diff tem de sair AQUI: depois do SaveChanges o EF sincroniza OriginalValues com
            // o que foi gravado e a comparação sai vazia. Sem campo notificável alterado não há
            // notificação — uma edição que só mexeu em coluna derivada não vira mensagem.
            var changes = ContractHeaderDiffBuilder.Build(
                context.Entry(existingEntity), NotificationDocumentType.SalesContract);

            if (changes.Count > 0)
                notificationOutbox.Register(
                    existingEntity, NotificationEventType.HeaderUpdated, userName, changes);

            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.Log(LogLevel.Error, "Failed to update entity.");
            throw new DefaultException("Error updating entity due to concurrency issues.");
        }

        return entity;
    }

    /// <summary>
    /// Espelho de <c>PurchaseContractsUpdateService.UpdatePriceFixation</c>: a fixação automática
    /// do contrato de preço fixo acompanha volume, preço e frete. Sem isso, editar depois de
    /// retirar a aprovação deixava a fixação (e o título provisório da reaprovação) no volume antigo.
    /// </summary>
    private async Task UpdatePriceFixation(SalesContract entity)
    {
        var fixation = await context.SalesContractsPriceFixations
                           .FirstOrDefaultAsync(pf => pf.SalesContractKey == entity.Key) ??
                       throw new KeyNotFoundException("Price fixation not found.");

        fixation.FreightCost = entity.FreightCostStandard;
        fixation.FixationVolume = entity.TotalVolume;
        fixation.FixationPrice = entity.Price;
        // Confirmed pelo mesmo motivo da criação: é o espelho do preço já acordado.
        fixation.Status = PriceFixationStatus.Confirmed;
        // A fixação automática cobre o contrato inteiro, como na criação. Não dá para usar o
        // SalesContractsFixedVolumeService aqui: ele soma pelo banco e ainda leria o volume antigo.
        entity.FixedVolume = entity.TotalVolume;
    }
}