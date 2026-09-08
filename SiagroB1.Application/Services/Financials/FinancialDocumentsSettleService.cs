using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Grava UMA linha do ledger de baixas e recalcula o saldo do documento.
///
/// Aceita CommitMode porque é ponto de entrada de tela HOJE e será composto pelo pagamento em
/// lote da Fase 4: chamado em Auto de dentro de uma transação alheia, o CommitAsync do
/// UnitOfWork comitaria a transação DO CHAMADOR — ele comita e zera _transaction
/// incondicionalmente.
/// </summary>
public class FinancialDocumentsSettleService(IUnitOfWork db)
{
    public async Task<FinancialSettlement> ExecuteAsync(
        Guid documentKey,
        string financialAccountCode,
        decimal amount,
        DateTime settlementDate,
        decimal interest,
        decimal fine,
        decimal discount,
        string? documentReference,
        string? notes,
        string userName,
        CommitMode commitMode = CommitMode.Auto)
    {
        var document = await db.Context.FinancialDocuments
                           .FirstOrDefaultAsync(x => x.Key == documentKey)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        var account = await db.Context.FinancialAccounts
            .FirstOrDefaultAsync(x => x.Code == financialAccountCode);

        // Validação ANTES da transação.
        FinancialDocumentsSettlementGuardService.EnsureCanSettle(document, account, amount);

        var settlement = new FinancialSettlement
        {
            Key = Guid.NewGuid(),
            FinancialDocumentKey = documentKey,
            FinancialAccountCode = financialAccountCode,
            SettlementDate = settlementDate,
            Amount = decimal.Round(amount, 2, MidpointRounding.ToEven),
            InterestAmount = decimal.Round(interest, 2, MidpointRounding.ToEven),
            FineAmount = decimal.Round(fine, 2, MidpointRounding.ToEven),
            DiscountAmount = decimal.Round(discount, 2, MidpointRounding.ToEven),
            Origin = FinancialSettlementOrigin.Manual,
            DocumentReference = documentReference,
            Notes = notes,
            CreatedAt = DateTime.Now,
            CreatedBy = userName,
            UpdatedAt = DateTime.Now,
            UpdatedBy = userName
        };

        try
        {
            if (commitMode == CommitMode.Auto) await db.BeginTransactionAsync();

            db.Context.FinancialSettlements.Add(settlement);
            await db.Context.SaveChangesAsync();

            await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(db.Context, documentKey);
            await db.Context.SaveChangesAsync();

            if (commitMode == CommitMode.Auto) await db.CommitAsync();
        }
        catch
        {
            if (commitMode == CommitMode.Auto) await db.RollbackAsync();
            throw;
        }

        return settlement;
    }
}
