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
///
/// Encontro de contas (spec 2026-10-13, títulos firmes, regra 21): estornar uma ponta estorna o PAR no mesmo ato.
/// Abatimento de composição (regra 14): recusado — só o desfazer da composição o estorna.
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

        // Regra 14 (spec 2026-10-13, títulos firmes): o abatimento nasce da composição e só ela o desfaz (remove a
        // linha de composição junto); estornar por aqui deixaria a composição órfã.
        if (settlement.Origin == FinancialSettlementOrigin.InvoiceOffset)
            throw new ApplicationException("Abatimento de composição se desfaz pela composição.");

        // A devolução é o registro de que o dinheiro voltou por fora — não é uma baixa errada,
        // então não há o que "desfazer" nela.
        if (settlement.Origin == FinancialSettlementOrigin.AdvanceRefund)
            throw new ApplicationException(
                "Não é possível estornar uma devolução: ela é o registro de que o dinheiro voltou.");

        var settlements = new List<FinancialSettlement> { settlement };

        // Regra 21: o par do encontro de contas. Qualquer ponta ainda não estornada entre os mesmos dois títulos e
        // com o mesmo valor serve — financeiramente são idênticas.
        if (settlement.Origin == FinancialSettlementOrigin.Netting)
        {
            var counterpart = await db.Context.FinancialSettlements
                                  .Where(x => x.FinancialDocumentKey == settlement.RelatedDocumentKey &&
                                              x.RelatedDocumentKey == settlement.FinancialDocumentKey &&
                                              x.Origin == FinancialSettlementOrigin.Netting &&
                                              x.Amount == settlement.Amount &&
                                              !db.Context.FinancialSettlements.Any(r => r.ReversedSettlementKey == x.Key))
                                  .OrderBy(x => x.CreatedAt)
                                  .FirstOrDefaultAsync()
                              ?? throw new ApplicationException("Lançamento par do encontro de contas não encontrado.");
            settlements.Add(counterpart);
        }

        foreach (var line in settlements)
        {
            var document = await db.Context.FinancialDocuments
                               .FirstOrDefaultAsync(x => x.Key == line.FinancialDocumentKey)
                           ?? throw new NotFoundException("Documento financeiro não encontrado.");

            // A devolução de adiantamento é o primeiro caminho do sistema que produz um documento
            // Canceled com um ledger de baixas ainda vivo (a baixa original + a linha negativa da
            // devolução). Sem este guard, estornar a baixa original reabriria o saldo enquanto o
            // Status continua Canceled — e nem o recálculo (que não mexe em documento cancelado) nem
            // o guard de cancelamento de contrato (que já filtra Status != Canceled) percebem. É o
            // buraco que esta feature existe para fechar reabrindo sozinho, então NÃO apagar por
            // parecer redundante com o guard de Origin acima.
            if (document.Status == FinancialDocumentStatus.Canceled)
                throw new ApplicationException("Não é possível estornar a baixa de um título cancelado.");

            if (await db.Context.FinancialSettlements.AnyAsync(x => x.ReversedSettlementKey == line.Key))
                throw new ApplicationException("Esta baixa já foi estornada.");
        }

        var reversals = settlements.Select(line => Mirror(line, reason, userName)).ToList();

        try
        {
            if (commitMode == CommitMode.Auto) await db.BeginTransactionAsync();

            db.Context.FinancialSettlements.AddRange(reversals);
            await db.Context.SaveChangesAsync();

            foreach (var line in settlements)
                await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(db.Context, line.FinancialDocumentKey);
            await db.Context.SaveChangesAsync();

            if (commitMode == CommitMode.Auto) await db.CommitAsync();
        }
        catch
        {
            if (commitMode == CommitMode.Auto) await db.RollbackAsync();
            throw;
        }
    }

    private static FinancialSettlement Mirror(FinancialSettlement settlement, string reason, string userName) => new()
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
        ReversedSettlementKey = settlement.Key,
        RelatedDocumentKey = settlement.RelatedDocumentKey,
        CompositionKey = settlement.CompositionKey,
        DocumentReference = settlement.DocumentReference,
        Notes = reason,
        CreatedAt = DateTime.Now,
        CreatedBy = userName,
        UpdatedAt = DateTime.Now,
        UpdatedBy = userName
    };
}
