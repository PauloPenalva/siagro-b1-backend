using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Estorna uma baixa gravando a linha NEGATIVA espelho. O ledger é insert-only — é dinheiro,
/// nunca se apaga. O índice único filtrado em ReversedSettlementKey impede estornar a mesma
/// baixa duas vezes; a consulta abaixo é a camada amigável do mesmo impedimento.
/// </summary>
public class FinancialDocumentsReverseSettlementService(IUnitOfWork db)
{
    public async Task ExecuteAsync(
        Guid settlementKey, string reason, string userName, CommitMode commitMode = CommitMode.Auto)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ApplicationException("Informe o motivo do estorno.");

        var settlement = await db.Context.FinancialSettlements
                             .FirstOrDefaultAsync(x => x.Key == settlementKey)
                         ?? throw new NotFoundException("Baixa não encontrada.");

        if (settlement.Origin == FinancialSettlementOrigin.Reversal)
            throw new ApplicationException("Não é possível estornar um estorno.");

        if (await db.Context.FinancialSettlements.AnyAsync(x => x.ReversedSettlementKey == settlementKey))
            throw new ApplicationException("Esta baixa já foi estornada.");

        var reversal = new FinancialSettlement
        {
            Key = Guid.NewGuid(),
            FinancialDocumentKey = settlement.FinancialDocumentKey,
            FinancialAccountCode = settlement.FinancialAccountCode,
            SettlementDate = DateTime.Today,
            Amount = -settlement.Amount,
            InterestAmount = -settlement.InterestAmount,
            FineAmount = -settlement.FineAmount,
            DiscountAmount = -settlement.DiscountAmount,
            Origin = FinancialSettlementOrigin.Reversal,
            ReversedSettlementKey = settlementKey,
            DocumentReference = settlement.DocumentReference,
            Notes = reason,
            CreatedAt = DateTime.Now,
            CreatedBy = userName,
            UpdatedAt = DateTime.Now,
            UpdatedBy = userName
        };

        try
        {
            if (commitMode == CommitMode.Auto) await db.BeginTransactionAsync();

            db.Context.FinancialSettlements.Add(reversal);
            await db.Context.SaveChangesAsync();

            await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(
                db.Context, settlement.FinancialDocumentKey);
            await db.Context.SaveChangesAsync();

            if (commitMode == CommitMode.Auto) await db.CommitAsync();
        }
        catch
        {
            if (commitMode == CommitMode.Auto) await db.RollbackAsync();
            throw;
        }
    }
}
