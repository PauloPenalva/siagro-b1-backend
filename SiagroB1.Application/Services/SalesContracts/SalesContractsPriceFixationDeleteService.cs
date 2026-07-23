using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Exclusão de fixação de preço EM APROVAÇÃO, exposta como OData action
/// (<c>SalesContractsPriceFixationDelete</c>). Excluir devolve o volume ao saldo a fixar;
/// fixação confirmada não se exclui — estorna-se primeiro. Espelha
/// <c>PurchaseContractsPriceFixationDeleteService</c>.
/// </summary>
public class SalesContractsPriceFixationDeleteService(
    AppDbContext context,
    SalesContractsFixedVolumeService fixedVolumeService,
    ILogger<SalesContractsPriceFixationDeleteService> logger)
{
    public async Task ExecuteAsync(Guid fixationKey)
    {
        try
        {
            var fixation = await context.SalesContractsPriceFixations
                               .Include(x => x.SalesContract)
                               .FirstOrDefaultAsync(x => x.Key == fixationKey)
                           ?? throw new NotFoundException("Fixação de preço não encontrada.");

            if (fixation.Status != PriceFixationStatus.InApproval)
                throw new ApplicationException(
                    $"Fixação {fixation.Status} não pode ser excluída. " +
                    "Para desfazer, estorne a fixação — o histórico é preservado.");

            var contract = fixation.SalesContract;

            context.SalesContractsPriceFixations.Remove(fixation);

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
