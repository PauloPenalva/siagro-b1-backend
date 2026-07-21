using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsPriceFixationsRejectService(
    AppDbContext context,
    PurchaseContractsFixedVolumeService fixedVolumeService)
{
    public async Task ExecuteAsync(Guid fixationKey, string? comments, string rejectedBy)
    {
        var fixation = await context.PurchaseContractsPriceFixations
                           .Include(x => x.PurchaseContract)
                           .FirstOrDefaultAsync(x => x.Key == fixationKey)
                       ?? throw new NotFoundException("Fixação de preço não encontrada.");

        if (fixation.Status != PriceFixationStatus.InApproval)
            throw new ApplicationException(
                $"Só é possível rejeitar fixação em aprovação. Status atual: {fixation.Status}.");

        var contract = fixation.PurchaseContract
                       ?? throw new NotFoundException("Contrato de compra não encontrado.");

        if (contract.Status != ContractStatus.Approved)
            throw new ApplicationException(
                "Contrato precisa estar aprovado para movimentar fixações.");

        await using var transaction = await context.Database.BeginTransactionAsync();

        fixation.Status = PriceFixationStatus.Rejected;
        fixation.ApprovalComments = comments;
        fixation.UpdatedAt = DateTime.Now;
        fixation.UpdatedBy = rejectedBy;

        // Salva o status ANTES de recalcular. RecalculateAsync consulta o banco e
        // não enxerga mudanças apenas rastreadas em memória — recalcular antes deste
        // SaveChanges somaria a fixação rejeitada de volta, como se nada tivesse mudado.
        await context.SaveChangesAsync();

        // Rejeitada deixa de reservar volume.
        await fixedVolumeService.RecalculateAsync(contract);
        await context.SaveChangesAsync();

        await transaction.CommitAsync();
    }
}
