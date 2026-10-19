using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using R = SiagroB1.Infra.Queries.PurchaseFiscalReconciliationRules;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Títulos a receber das linhas SEM contrato do documento de saída (spec 2026-10-19 parte 1): quem decide é a natureza
/// ("gera financeiro"), não o contrato — venda de ativo, sacaria e serviço de armazenagem não têm contrato. As linhas com
/// contrato continuam no firme por linha × contrato (<see cref="FinancialDocumentsSalesFirmSyncService"/>).
///
/// Um conjunto de parcelas por documento (origem SalesInvoiceNoContract, OriginKey = documento), pela condição de pagamento
/// do documento, sobre a soma do GrandTotal das linhas sem contrato. Vale nos dois modos (em SAPB1 toda natureza conta como
/// "gera", decisão do usuário em 19/10) e só para documentos com data a partir de <see cref="CutoverDateKey"/>.
///
/// ENQUEUE-ONLY: quem chama grava. Devolução fica de fora (sem regra de compensação para título sem contrato).
/// </summary>
public class FinancialDocumentsSalesNoContractService(
    IUnitOfWork db,
    DocNumberSequenceService docNumberSequence,
    FinancialDocumentsFirmSyncService firmSync,
    IConfiguration configuration,
    ILogger<FinancialDocumentsSalesNoContractService> logger)
{
    /// <summary>Data de corte (yyyy-MM-dd): documento de saída com data anterior não gera título sem contrato. Ausente = sem limite.</summary>
    public const string CutoverDateKey = "Financial:SalesNoContractReceivablesCutoverDate";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task EnqueueAsync(SalesInvoice invoice, string userName)
    {
        if (!Applies(invoice, Cutover())) return;

        var context = db.Context;
        if (await ActiveTitlesOf(invoice.Key).AnyAsync()) return;

        var amount = 0m;
        foreach (var line in invoice.Items.Where(l => l.SalesContractKey is null))
            if (await firmSync.CreatesFinancialDocumentAsync(line.UsageCode))
                amount += line.GrandTotal;
        amount = decimal.Round(amount, 2, MidpointRounding.ToEven);
        if (amount <= 0m) return;

        var issueDate = (invoice.InvoiceDate ?? DateTime.Today).Date;
        var installments = await PaymentInstallmentPlanner.PlanAsync(context, invoice.PaymentConditionCode, amount, issueDate);
        var taxDocument = R.TaxDocument(invoice.TaxDocumentNumber, invoice.TaxDocumentSeries, invoice.InvoiceNumber);
        var now = DateTime.Now;

        foreach (var (number, dueDate, value) in installments)
        {
            var assignment = await docNumberSequence.AssignAsync(new DocNumberRequest(
                TransactionCode.FinancialDocument, null, null, 50,
                c => context.FinancialDocuments.AnyAsync(x => x.Code == c)));

            var document = new FinancialDocument
            {
                Key = Guid.NewGuid(),
                Code = assignment.Code,
                DocNumberKey = assignment.DocNumberKey,
                BranchCode = invoice.BranchCode,
                Direction = FinancialDirection.Receivable,
                Nature = FinancialDocumentNature.Firm,
                Status = FinancialDocumentStatus.Open,
                CardCode = invoice.CardCode,
                CardName = invoice.CardName,
                DocumentDate = issueDate,
                DueDate = dueDate,
                Currency = CurrencyType.Brl,
                NetAmount = value,
                InstallmentNumber = number,
                OriginType = FinancialDocumentOrigin.SalesInvoiceNoContract,
                OriginKey = invoice.Key,
                OriginDocNumber = invoice.InvoiceNumber,
                SalesInvoiceKey = invoice.Key,
                TaxDocumentNumber = taxDocument,
                CreatedAt = now,
                CreatedBy = userName,
                UpdatedAt = now,
                UpdatedBy = userName,
            };
            context.FinancialDocuments.Add(document);
            await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, document.Key);
        }
    }

    /// <summary>G1–G3 do firme (confirmado, sem complemento de ICMS, custódia nem liberação), sem devolução, e a data de corte.</summary>
    internal static bool Applies(SalesInvoice invoice, DateTime? cutover) =>
        invoice.InvoiceType == SalesInvoiceType.Normal &&
        invoice.InvoiceStatus == InvoiceStatus.Confirmed &&
        invoice.CustodyRole == StorageCustodyRole.None &&
        invoice.ShipmentReleaseKey == null &&
        (cutover is not { } date || (invoice.InvoiceDate ?? DateTime.Today).Date >= date);

    private DateTime? Cutover()
    {
        var raw = configuration[CutoverDateKey];
        if (string.IsNullOrWhiteSpace(raw))
        {
            logger.LogWarning("{Key} ausente: títulos sem contrato do documento de saída valem sem data de corte.", CutoverDateKey);
            return null;
        }

        if (DateTime.TryParseExact(raw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var cutover))
            return cutover;

        logger.LogWarning("{Key} inválida ({Value}): títulos sem contrato valem sem data de corte.", CutoverDateKey, raw);
        return null;
    }

    private IQueryable<FinancialDocument> ActiveTitlesOf(Guid invoiceKey) =>
        db.Context.FinancialDocuments.Where(d =>
            d.OriginType == FinancialDocumentOrigin.SalesInvoiceNoContract && d.OriginKey == invoiceKey &&
            d.Status != FinancialDocumentStatus.Canceled);

    /// <summary>Ensaio do desfazer: recusa se algum título ativo da origem já tem baixa. Nada é alterado.</summary>
    public async Task EnsureCanUndoAsync(Guid salesInvoiceKey)
    {
        var titles = await ActiveTitlesOf(salesInvoiceKey).AsNoTracking().OrderBy(d => d.Code).ToListAsync();
        foreach (var title in titles)
            EnsureLoose(title);
    }

    /// <summary>Cancela (nunca apaga) os títulos ativos sem contrato do documento. ENQUEUE-ONLY; recusa antes de qualquer mudança.</summary>
    public void EnqueueUndo(Guid salesInvoiceKey, string userName)
    {
        var titles = ActiveTitlesOf(salesInvoiceKey).ToList()
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
