using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Exclusão de fixação de preço EM APROVAÇÃO, exposta como OData action
/// (<c>PurchaseContractsPriceFixationDelete</c>) para que o frontend a invoque pelo
/// ODataModel e a linha suma da tela sem recarregar a rota. Excluir devolve o volume
/// ao saldo a fixar; fixação confirmada não se exclui — estorna-se primeiro.
/// </summary>
public class PurchaseContractsPriceFixationDeleteService(
    AppDbContext context,
    PurchaseContractsFixedVolumeService fixedVolumeService,
    PurchaseContractsChangeLogService changeLog,
    ILogger<PurchaseContractsPriceFixationDeleteService> logger)
{
    public async Task ExecuteAsync(Guid fixationKey, string deletedBy)
    {
        try
        {
            var fixation = await context.PurchaseContractsPriceFixations
                               .Include(x => x.PurchaseContract)
                               .FirstOrDefaultAsync(x => x.Key == fixationKey)
                           ?? throw new NotFoundException("Fixação de preço não encontrada.");

            if (fixation.Status != PriceFixationStatus.InApproval)
                throw new ApplicationException(
                    $"Fixação {fixation.Status} não pode ser excluída. " +
                    "Para desfazer, estorne a fixação — o histórico é preservado.");

            // A FK washout → fixação é Restrict para washout de QUALQUER status (mesmo rejeitado
            // ou estornado). O InMemory dos testes não vê essa violação, então o SQL 547 só
            // aparece em banco real — a trava tem de vir daqui.
            var hasWashout = await context.PurchaseContractsWashouts
                .AnyAsync(w => w.PriceFixationKey == fixation.Key);

            if (hasWashout)
                throw new ApplicationException(
                    "Fixação possui washout registrado (mesmo rejeitado ou estornado) e não pode ser excluída. " +
                    "Rejeite a fixação em vez de excluí-la.");

            var contract = fixation.PurchaseContract;

            context.PurchaseContractsPriceFixations.Remove(fixation);

            if (contract != null)
            {
                changeLog.Register(
                    contract.Key,
                    ContractChangeLogFields.PriceFixation,
                    ContractChangeLogFields.DescribePriceFixation(
                        fixation.FixationVolume, fixation.FixationPrice, fixation.Status,
                        contract.UnitOfMeasureCode),
                    null,
                    deletedBy);
            }

            // Persiste a remoção ANTES de recalcular: RecalculateAsync consulta o banco
            // e ainda enxergaria a fixação removida se recalculássemos agora.
            await context.SaveChangesAsync();

            if (contract != null)
            {
                await fixedVolumeService.RecalculateAsync(contract);
                await context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw;
        }
    }
}
