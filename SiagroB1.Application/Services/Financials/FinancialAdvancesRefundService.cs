using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Devolução do valor adiantado: o dinheiro saiu e voltou.
///
/// Uma das três saídas para um adiantamento pago cujo contrato alguém quer cancelar (as outras
/// são estornar a baixa e vincular o adiantamento a outro contrato). Sempre pelo valor INTEIRO:
/// devolução parcial exigiria partir o título em dois e encosta na amortização da Fase 2.
///
/// AvailableAdvanceAmount é [NotMapped] derivado de SettledAmount, então o crédito desaparece
/// sozinho — não há segunda fonte de verdade para sincronizar.
/// </summary>
public class FinancialAdvancesRefundService(IUnitOfWork db)
{
    public async Task ExecuteAsync(
        Guid documentKey,
        string financialAccountCode,
        DateTime refundDate,
        string? documentReference,
        string reason,
        string userName,
        CommitMode commitMode = CommitMode.Auto)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ApplicationException("Informe o motivo da devolução.");

        var document = await db.Context.FinancialDocuments
                           .FirstOrDefaultAsync(x => x.Key == documentKey)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (document.Nature != FinancialDocumentNature.Advance)
            throw new ApplicationException(
                $"O documento {document.Code} não é um adiantamento. Só adiantamento se devolve.");

        if (document.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException($"O documento {document.Code} já está cancelado.");

        if (document.SettledAmount == 0m)
            throw new ApplicationException(
                $"O adiantamento {document.Code} não tem valor pago: não há o que devolver.");

        // Obrigatória: o dinheiro volta para algum lugar concreto. FinancialAccountCode ser
        // anulável no modelo existe para as origens não-caixa da Fase 2/3, não para esta.
        var account = await db.Context.FinancialAccounts
            .FirstOrDefaultAsync(x => x.Code == financialAccountCode);

        if (account is null)
            throw new ApplicationException("Informe a conta financeira que recebeu a devolução.");

        if (account.Inactive)
            throw new ApplicationException($"A conta financeira {account.Code} está inativa.");

        if (account.Currency != document.Currency)
            throw new ApplicationException(
                $"A conta {account.Code} é em {account.Currency} e o adiantamento em " +
                $"{document.Currency}: a moeda precisa ser a mesma.");

        var refund = new FinancialSettlement
        {
            Key = Guid.NewGuid(),
            FinancialDocumentKey = documentKey,
            FinancialAccountCode = financialAccountCode,
            SettlementDate = refundDate,
            Amount = -document.SettledAmount,
            InterestAmount = 0m,
            FineAmount = 0m,
            DiscountAmount = 0m,
            Origin = FinancialSettlementOrigin.AdvanceRefund,
            ReversedSettlementKey = null,
            DocumentReference = documentReference,
            Notes = reason,
            CreatedAt = DateTime.Now,
            CreatedBy = userName,
            UpdatedAt = DateTime.Now,
            UpdatedBy = userName
        };

        try
        {
            if (commitMode == CommitMode.Auto) await db.BeginTransactionAsync();

            db.Context.FinancialSettlements.Add(refund);
            await db.Context.SaveChangesAsync();

            // SumAsync não enxerga entidade rastreada ainda não salva: recalcular só DEPOIS do save
            // acima. RecalculateAsync (sobrecarga estática) só ENFILEIRA a mudança no MESMO
            // `document` rastreado por este DbContext — não salva —, então o save abaixo, feito
            // depois do cancelamento, persiste as duas coisas de uma vez.
            await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(db.Context, documentKey);

            // O recálculo escreve Status; o cancelamento vem DEPOIS para prevalecer sobre ele.
            document.Status = FinancialDocumentStatus.Canceled;
            document.CancellationReason = $"Adiantamento devolvido: {reason}";
            document.CanceledAt = DateTime.Now;
            document.CanceledBy = userName;
            document.UpdatedAt = DateTime.Now;
            document.UpdatedBy = userName;
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
