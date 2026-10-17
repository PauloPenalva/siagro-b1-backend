using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Fiscal.Payments;
using SiagroB1.Infra;
using R = SiagroB1.Infra.Queries.PurchaseFiscalReconciliationRules;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Títulos da "outra compra" (spec 2026-10-17 §3): ao confirmar o documento de entrada STANDALONE sem contrato e com natureza
/// que gera financeiro, nascem (a) as parcelas a pagar do fornecedor sobre o líquido (NetPayable) pela condição de pagamento
/// do documento e (b) um título por tributo retido, para o credor configurado em WITHHOLDING_TAX_SETTINGS.
///
/// As parcelas são Firm de origem OtherPurchaseInvoice, que NÃO é firme de contrato (FinancialDocument.IsContractFirmDocument):
/// são baixadas direto, sem composição. Por serem N títulos com o mesmo OriginKey, o índice IX_FINANCIAL_DOCUMENTS_FirmOrigin
/// exclui esta origem e a idempotência é daqui.
///
/// ENQUEUE-ONLY: o chamador (PurchaseInvoicesConfirmService) grava os títulos no MESMO SaveChanges da confirmação.
/// ⚠️ Chame FORA de transação: DocNumberSequenceService roda por Dapper numa conexão própria, com UPDLOCK. Toda recusa
/// acontece ANTES de consumir numeração.
/// </summary>
public class FinancialDocumentsOtherPurchaseService(
    IUnitOfWork db,
    DocNumberSequenceService docNumberSequence,
    IUsage usageService,
    IConfiguration configuration,
    IBusinessPartnerService businessPartners)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task EnqueueAsync(PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines, string userName)
    {
        if (!ErpMode.IsStandalone(configuration) ||
            !await PurchaseInvoiceLineGuard.IsOtherPurchaseDocumentAsync(usageService, configuration, invoice, lines))
            return;

        var context = db.Context;

        // Idempotente: título ativo desta origem = já gerado. (Cancelar o documento cancela os títulos; a reabertura regenera.)
        if (await context.FinancialDocuments.AnyAsync(d =>
                d.OriginType == FinancialDocumentOrigin.OtherPurchaseInvoice && d.OriginKey == invoice.Key &&
                d.Status != FinancialDocumentStatus.Canceled))
            return;

        // NetPayable depende de Items, que a confirmação não carrega: calcula pelas linhas recebidas.
        var net = decimal.Round(
            lines.Sum(l => l.GrandTotal) - lines.Sum(l => l.FunruralValue) - invoice.TotalWithheld, 2, MidpointRounding.ToEven);
        if (net <= 0m)
            throw new DefaultException(
                $"O total das retenções ({invoice.TotalWithheld.ToString("N2", PtBr)}) consome o valor da nota: revise os valores retidos.");

        var issueDate = (invoice.IssueDate ?? DateTime.Today).Date;
        var taxes = await ResolveWithholdingsAsync(invoice, issueDate);
        var installments = await PlanInstallmentsAsync(invoice, net, issueDate);
        var taxDocument = R.TaxDocument(invoice.TaxDocumentNumber, invoice.TaxDocumentSeries, invoice.InvoiceNumber);

        foreach (var (number, dueDate, amount) in installments)
        {
            var document = await NewDocumentAsync(invoice, userName, issueDate, taxDocument, FinancialDocumentNature.Firm,
                invoice.CardCode, invoice.CardName, dueDate, amount);
            document.InstallmentNumber = number;
            context.FinancialDocuments.Add(document);
            await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, document.Key);
        }

        foreach (var tax in taxes)
        {
            var document = await NewDocumentAsync(invoice, userName, issueDate, taxDocument,
                FinancialDocumentNature.TaxWithholding, tax.CardCode, tax.CardName, tax.DueDate, tax.Amount);
            document.WithholdingTaxType = tax.Type;
            context.FinancialDocuments.Add(document);
            await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, document.Key);
        }
    }

    private async Task<FinancialDocument> NewDocumentAsync(
        PurchaseInvoice invoice, string userName, DateTime issueDate, string taxDocument,
        FinancialDocumentNature nature, string cardCode, string? cardName, DateTime dueDate, decimal amount)
    {
        var docNumberKey = await docNumberSequence.GetKeyByTransactionCode(TransactionCode.FinancialDocument);
        var code = await docNumberSequence.GetDocNumber(docNumberKey);
        var now = DateTime.Now;

        return new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = code,
            DocNumberKey = docNumberKey,
            BranchCode = invoice.BranchCode,
            Direction = FinancialDirection.Payable,
            Nature = nature,
            Status = FinancialDocumentStatus.Open,
            CardCode = cardCode,
            CardName = cardName,
            DocumentDate = issueDate,
            DueDate = dueDate,
            Currency = CurrencyType.Brl,
            NetAmount = amount,
            OriginType = FinancialDocumentOrigin.OtherPurchaseInvoice,
            OriginKey = invoice.Key,
            OriginDocNumber = invoice.InvoiceNumber,
            PurchaseInvoiceKey = invoice.Key,
            TaxDocumentNumber = taxDocument,
            CreatedAt = now,
            CreatedBy = userName,
            UpdatedAt = now,
            UpdatedBy = userName,
        };
    }

    /// <summary>Parcelas pela condição do documento; sem condição, ou sem parcelas (meio 90), é 1 título vencendo na emissão.</summary>
    private async Task<List<(int Number, DateTime DueDate, decimal Amount)>> PlanInstallmentsAsync(
        PurchaseInvoice invoice, decimal net, DateTime issueDate)
    {
        var condition = invoice.PaymentConditionCode is { } code
            ? await db.Context.PaymentConditions.AsNoTracking().FirstOrDefaultAsync(c => c.Code == code)
            : null;

        if (condition is not null)
        {
            var plan = PaymentInstallmentCalculator.Calculate(
                condition.Days, condition.StartRule, condition.PaymentMeans, net, DateOnly.FromDateTime(issueDate));
            if (plan.Installments.Count > 0)
                return plan.Installments
                    .Select(i => (i.Number, i.DueDate.ToDateTime(TimeOnly.MinValue), decimal.Round(i.Amount, 2, MidpointRounding.ToEven)))
                    .ToList();
        }

        return [(1, issueDate, net)];
    }

    private sealed record TaxTitle(WithholdingTaxType Type, string CardCode, string? CardName, decimal Amount, DateTime DueDate);

    /// <summary>Um título por tributo com valor > 0. Recusa se faltar o credor ou se o parceiro configurado não existir mais.</summary>
    private async Task<List<TaxTitle>> ResolveWithholdingsAsync(PurchaseInvoice invoice, DateTime issueDate)
    {
        var withheld = new (WithholdingTaxType Type, decimal Amount)[]
        {
            (WithholdingTaxType.Iss, invoice.WithheldIss), (WithholdingTaxType.Irrf, invoice.WithheldIrrf),
            (WithholdingTaxType.Inss, invoice.WithheldInss), (WithholdingTaxType.Pis, invoice.WithheldPis),
            (WithholdingTaxType.Cofins, invoice.WithheldCofins), (WithholdingTaxType.Csll, invoice.WithheldCsll),
        };

        var result = new List<TaxTitle>();
        var nextMonth = new DateTime(issueDate.Year, issueDate.Month, 1).AddMonths(1);

        foreach (var (type, amount) in withheld)
        {
            var rounded = decimal.Round(amount, 2, MidpointRounding.ToEven);
            if (rounded <= 0m)
                continue;

            var label = type.ToString().ToUpperInvariant();
            var key = (int)type;
            var setting = await db.Context.WithholdingTaxSettings.AsNoTracking().FirstOrDefaultAsync(s => s.TaxType == key)
                          ?? throw new DefaultException($"Configure o credor da retenção de {label} em Configuração de Retenções.");

            var partner = await businessPartners.GetByIdAsync(setting.CardCode)
                          ?? throw new DefaultException(
                              $"O credor {setting.CardCode} da retenção de {label} não existe mais: revise Configuração de Retenções.");

            var day = Math.Min(setting.DueDay, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month));
            result.Add(new TaxTitle(type, setting.CardCode, partner.CardName ?? setting.CardName, rounded,
                new DateTime(nextMonth.Year, nextMonth.Month, day)));
        }

        return result;
    }
}
