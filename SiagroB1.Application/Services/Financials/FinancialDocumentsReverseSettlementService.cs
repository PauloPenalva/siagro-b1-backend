using System.Globalization;
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
/// Lançamentos em PAR — encontro de contas (spec 2026-10-13, regra 21), compensação de adiantamento e transferência de
/// crédito (spec 2026-10-14, regras 5 e 9): estornar uma ponta estorna o PAR no mesmo ato.
/// Abatimento de composição (regra 14): recusado — só o desfazer da composição o estorna.
/// Pagamento do adiantamento (regra 6): recusado se o pago ficaria abaixo do aplicado.
/// Transferência (regra 9): estornar o par CANCELA o adiantamento nascido do desmembramento.
/// </summary>
public class FinancialDocumentsReverseSettlementService(IUnitOfWork db)
{
    /// <summary>Motivo de cancelamento do adiantamento novo quando a transferência é estornada (spec 2026-10-14, regra 9).</summary>
    public const string SplitReversalReason = "Transferência de crédito estornada";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

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

        // A linha escolhida já estornada tem de recusar ANTES da busca do par: o par estornado não é achado
        // (CounterpartAsync o ignora) e a mensagem sairia "par não encontrado".
        if (await db.Context.FinancialSettlements.AnyAsync(x => x.ReversedSettlementKey == settlement.Key))
            throw new ApplicationException("Esta baixa já foi estornada.");

        var settlements = new List<FinancialSettlement> { settlement };

        if (settlement.Origin is FinancialSettlementOrigin.Netting
            or FinancialSettlementOrigin.AdvanceApplication
            or FinancialSettlementOrigin.AdvanceTransfer)
            settlements.Add(await CounterpartAsync(settlement));

        var documents = new Dictionary<Guid, FinancialDocument>();
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

            documents[line.FinancialDocumentKey] = document;
        }

        EnsurePaymentStillCoversTheApplied(settlement, documents[settlement.FinancialDocumentKey]);

        var splitAdvance = settlement.Origin == FinancialSettlementOrigin.AdvanceTransfer
            ? EnsureSplitAdvanceUnused(settlements, documents)
            : null;

        var reversals = settlements.Select(line => Mirror(line, reason, userName)).ToList();
        LogUndo(settlements, documents, userName);

        try
        {
            if (commitMode == CommitMode.Auto) await db.BeginTransactionAsync();

            db.Context.FinancialSettlements.AddRange(reversals);
            await db.Context.SaveChangesAsync();

            foreach (var line in settlements)
                await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(db.Context, line.FinancialDocumentKey);

            // Regra 9: o recálculo deixa o novo sem pago (Open); o cancelamento vem DEPOIS para prevalecer.
            if (splitAdvance is not null)
                FinancialDocumentsCancelService.Cancel(splitAdvance, SplitReversalReason, userName);

            await db.Context.SaveChangesAsync();

            if (commitMode == CommitMode.Auto) await db.CommitAsync();
        }
        catch
        {
            if (commitMode == CommitMode.Auto) await db.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// A outra ponta do par: lançamento entre os mesmos dois títulos (chaves cruzadas), mesma origem e valor, ainda não
    /// estornado. Qualquer uma serve — financeiramente são idênticas.
    /// </summary>
    private async Task<FinancialSettlement> CounterpartAsync(FinancialSettlement settlement)
    {
        var counterpart = await db.Context.FinancialSettlements
            .Where(x => x.FinancialDocumentKey == settlement.RelatedDocumentKey &&
                        x.RelatedDocumentKey == settlement.FinancialDocumentKey &&
                        x.Origin == settlement.Origin &&
                        x.Amount == settlement.Amount &&
                        !db.Context.FinancialSettlements.Any(r => r.ReversedSettlementKey == x.Key))
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync();

        return counterpart ?? throw new ApplicationException(settlement.Origin switch
        {
            FinancialSettlementOrigin.Netting => "Lançamento par do encontro de contas não encontrado.",
            FinancialSettlementOrigin.AdvanceApplication => "Lançamento par da compensação de adiantamento não encontrado.",
            _ => "Lançamento par da transferência de adiantamento não encontrado.",
        });
    }

    /// <summary>
    /// Regra 6 (spec 2026-10-14): estornar um pagamento do adiantamento não pode deixar o pago abaixo do aplicado — o
    /// crédito já usado ficaria sem dinheiro por trás. A transferência de saída do desmembramento conta como aplicado.
    /// </summary>
    private static void EnsurePaymentStillCoversTheApplied(FinancialSettlement settlement, FinancialDocument document)
    {
        if (settlement.Origin != FinancialSettlementOrigin.Manual ||
            document.Nature != FinancialDocumentNature.Advance ||
            settlement.Amount <= 0m)
            return;

        var paidAfter = decimal.Round(document.SettledAmount - settlement.Amount, 2, MidpointRounding.ToEven);
        if (paidAfter < document.AppliedAmount)
            throw new ApplicationException(
                $"O adiantamento {document.Code} tem {document.AppliedAmount.ToString("N2", PtBr)} aplicado: desfaça as compensações antes de estornar o pagamento.");
    }

    /// <summary>
    /// Regra 9: o adiantamento NOVO é o da transferência de ENTRADA. Só se desfaz o desmembramento se ele continua
    /// intocado — nada aplicado e o pago igual à transferência (compensação estornada se anula e não trava).
    /// </summary>
    private static FinancialDocument EnsureSplitAdvanceUnused(
        IReadOnlyList<FinancialSettlement> settlements, IReadOnlyDictionary<Guid, FinancialDocument> documents)
    {
        var incoming = settlements.FirstOrDefault(line =>
                           FinancialDocumentsRecalculateBalanceService.IsIncomingTransfer(
                               documents[line.FinancialDocumentKey], line))
                       ?? throw new ApplicationException("Lançamento par da transferência de adiantamento não encontrado.");

        var split = documents[incoming.FinancialDocumentKey];
        if (split.AppliedAmount != 0m || split.SettledAmount != incoming.Amount)
            throw new ApplicationException(
                $"O adiantamento {split.Code} já foi usado: desfaça as compensações dele antes de estornar a transferência.");

        return split;
    }

    /// <summary>Compensação e transferência logam o desfazer nos dois títulos (NewValue nulo), como a composição.</summary>
    private void LogUndo(
        IReadOnlyList<FinancialSettlement> settlements, IReadOnlyDictionary<Guid, FinancialDocument> documents,
        string userName)
    {
        var field = settlements[0].Origin switch
        {
            FinancialSettlementOrigin.AdvanceApplication => FinancialDocumentChangeLogFields.AdvanceApplication,
            FinancialSettlementOrigin.AdvanceTransfer => FinancialDocumentChangeLogFields.AdvanceTransfer,
            _ => null,
        };
        if (field is null)
            return;

        var changeLog = new FinancialDocumentChangeLogService(db.Context);
        foreach (var line in settlements)
        {
            var other = documents[line.RelatedDocumentKey!.Value];
            changeLog.Register(line.FinancialDocumentKey, field,
                FinancialDocumentsComposeService.Describe(other.Code, line.Amount), null, userName);
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
