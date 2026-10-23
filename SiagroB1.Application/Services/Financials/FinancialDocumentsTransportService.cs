using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Títulos a receber do CT-e (entrega B): nascem na AUTORIZAÇÃO, contra o TOMADOR, pelo valor a receber
/// (<see cref="TransportDocument.AmountToReceive"/>), parcelados pela condição de pagamento do documento e com as dimensões
/// contábeis dele. Só se a natureza "gera financeiro". Origem TransportDocument, OriginKey = chave do CT-e (N parcelas; fora
/// do índice FirmOrigin, como <see cref="FinancialDocumentsSalesNoContractService"/>, de onde o padrão foi copiado).
///
/// ENQUEUE-ONLY: quem chama grava.
/// </summary>
public class FinancialDocumentsTransportService(
    IUnitOfWork db,
    DocNumberSequenceService docNumberSequence,
    FinancialDocumentsFirmSyncService firmSync)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Idempotente: se o CT-e já tem título ativo, nada acontece.</summary>
    public async Task EnqueueAsync(TransportDocument doc, string userName)
    {
        var context = db.Context;
        if (await ActiveTitlesOf(doc.Key).AnyAsync()) return;
        if (!await firmSync.CreatesFinancialDocumentAsync(doc.UsageCode)) return;

        var amount = decimal.Round(doc.AmountToReceive, 2, MidpointRounding.ToEven);
        if (amount <= 0m) return;

        var taker = doc.Parties.FirstOrDefault(p => (int)p.Role == doc.TakerRole && !string.IsNullOrWhiteSpace(p.CardCode))
                    ?? throw new DefaultException("O tomador do CT-e não tem parceiro cadastrado: não é possível gerar o título a receber.");

        var issueDate = (doc.IssuedAt ?? doc.AuthorizedAt ?? DateTime.Today).Date;
        var installments = await PaymentInstallmentPlanner.PlanAsync(context, doc.PaymentConditionCode, amount, issueDate);
        var number = doc.Number?.ToString(CultureInfo.InvariantCulture);
        var now = DateTime.Now;

        foreach (var (installment, dueDate, value) in installments)
        {
            var assignment = await docNumberSequence.AssignAsync(new DocNumberRequest(
                TransactionCode.FinancialDocument, null, null, 50,
                c => context.FinancialDocuments.AnyAsync(x => x.Code == c)));

            var document = new FinancialDocument
            {
                Key = Guid.NewGuid(),
                Code = assignment.Code,
                DocNumberKey = assignment.DocNumberKey,
                BranchCode = doc.BranchCode,
                Direction = FinancialDirection.Receivable,
                Nature = FinancialDocumentNature.Firm,
                Status = FinancialDocumentStatus.Open,
                CardCode = taker.CardCode!,
                CardName = taker.Name,
                DocumentDate = issueDate,
                DueDate = dueDate,
                Currency = CurrencyType.Brl,
                NetAmount = value,
                InstallmentNumber = installment,
                OriginType = FinancialDocumentOrigin.TransportDocument,
                OriginKey = doc.Key,
                OriginDocNumber = number,
                TaxDocumentNumber = number,
                CostCenterCode = doc.CostCenterCode,
                AccountingItemCode = doc.AccountingItemCode,
                DistributionRuleCode = doc.DistributionRuleCode,
                CreatedAt = now,
                CreatedBy = userName,
                UpdatedAt = now,
                UpdatedBy = userName,
            };
            context.FinancialDocuments.Add(document);
            await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, document.Key);
        }
    }

    private IQueryable<FinancialDocument> ActiveTitlesOf(Guid docKey) =>
        db.Context.FinancialDocuments.Where(d =>
            d.OriginType == FinancialDocumentOrigin.TransportDocument && d.OriginKey == docKey &&
            d.Status != FinancialDocumentStatus.Canceled);

    /// <summary>Ensaio do desfazer: recusa se algum título ativo do CT-e já tem baixa. Nada é alterado.</summary>
    public async Task EnsureCanUndoAsync(Guid docKey)
    {
        var titles = await ActiveTitlesOf(docKey).AsNoTracking().OrderBy(d => d.Code).ToListAsync();
        foreach (var title in titles)
            EnsureLoose(title);
    }

    /// <summary>Cancela (nunca apaga) os títulos ativos do CT-e. ENQUEUE-ONLY; recusa antes de qualquer mudança.</summary>
    public void EnqueueUndo(Guid docKey, string userName)
    {
        var titles = ActiveTitlesOf(docKey).ToList()
            .Where(d => d.Status != FinancialDocumentStatus.Canceled).ToList();
        foreach (var title in titles)
            EnsureLoose(title);
        foreach (var title in titles)
            FinancialDocumentsCancelService.Cancel(title, FinancialDocumentsSalesFirmSyncService.DocumentUndoneReason, userName);
    }

    private static void EnsureLoose(FinancialDocument title)
    {
        if (title.SettledAmount > 0m || title.AppliedAmount > 0m)
            throw new DefaultException(
                $"O título {title.Code} já tem baixa ({title.SettledAmount.ToString("N2", PtBr)}): estorne a baixa no Financeiro antes de desfazer o documento.");
    }
}
