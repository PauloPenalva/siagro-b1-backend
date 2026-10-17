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

        if (invoice.IssueDate is not { } issue)
            throw new DefaultException("Informe a data de emissão do documento antes de confirmar.");
        var issueDate = issue.Date;

        // NetPayable depende de Items, que a confirmação não carrega: calcula pelas linhas recebidas. O retido é a SOMA DOS
        // VALORES ARREDONDADOS de cada tributo (os mesmos dos títulos), para parcelas + títulos de retenção fecharem no bruto.
        var withheldTotal = WithheldOf(invoice).Sum(w => w.Amount);
        var net = decimal.Round(
            lines.Sum(l => l.GrandTotal) - lines.Sum(l => l.FunruralValue) - withheldTotal, 2, MidpointRounding.ToEven);
        if (net <= 0m)
            throw new DefaultException(
                $"O total das retenções ({withheldTotal.ToString("N2", PtBr)}) consome o valor da nota: revise os valores retidos.");

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

    /// <summary>Motivo gravado no cancelamento dos títulos quando o documento de entrada é estornado ou cancelado.</summary>
    public const string UndoneReason = "Documento de entrada estornado ou cancelado";

    private IQueryable<FinancialDocument> ActiveTitlesOf(Guid invoiceKey) =>
        db.Context.FinancialDocuments.Where(d =>
            d.OriginType == FinancialDocumentOrigin.OtherPurchaseInvoice && d.OriginKey == invoiceKey &&
            d.Status != FinancialDocumentStatus.Canceled);

    /// <summary>
    /// Ensaio do desfazer: recusa se algum título ativo da origem já tem baixa (SettledAmount, que já soma dação e
    /// encontro de contas, ambos gravados no ledger de baixas). Composição não se aplica: a origem 9 não é firme de contrato.
    /// Nada é alterado. Sem títulos desta origem (grão, SAPB1, documento sem financeiro) não faz nada.
    /// </summary>
    public async Task EnsureCanUndoAsync(Guid invoiceKey)
    {
        var titles = await ActiveTitlesOf(invoiceKey).AsNoTracking().OrderBy(d => d.Code).ToListAsync();
        foreach (var title in titles)
            if (title.SettledAmount > 0m)
                throw new DefaultException(
                    $"O título {title.Code} já tem baixa: estorne a baixa no Financeiro antes de desfazer o documento.");
    }

    /// <summary>
    /// Cancela (nunca apaga) os títulos ativos da outra compra, com os mesmos carimbos de
    /// PurchaseFirmPayableRules.CancelLooseFirms, que não grava change log de status (não há campo de status no log).
    /// ENQUEUE-ONLY: o chamador salva no mesmo SaveChanges do estorno/cancelamento. Recusa antes de qualquer mudança.
    /// Leitura síncrona de propósito, como as regras de firme: a chamada é por chave e cabe no fluxo do chamador.
    /// </summary>
    public void EnqueueUndo(Guid invoiceKey, string userName)
    {
        var titles = ActiveTitlesOf(invoiceKey).ToList()
            // Reconfere em memória: o mesmo SaveChanges pode já ter cancelado o título no tracker (consulta traz o valor do banco).
            .Where(d => d.Status != FinancialDocumentStatus.Canceled).ToList();
        foreach (var title in titles)
            if (title.SettledAmount > 0m)
                throw new DefaultException(
                    $"O título {title.Code} já tem baixa: estorne a baixa no Financeiro antes de desfazer o documento.");

        var now = DateTime.Now;
        foreach (var title in titles)
        {
            title.Status = FinancialDocumentStatus.Canceled;
            title.CancellationReason = UndoneReason;
            title.CanceledAt = now;
            title.CanceledBy = userName;
            title.UpdatedAt = now;
            title.UpdatedBy = userName;
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
        PaymentCondition? condition = null;
        if (invoice.PaymentConditionCode is { } code)
            condition = await db.Context.PaymentConditions.AsNoTracking().FirstOrDefaultAsync(c => c.Code == code)
                        ?? throw new DefaultException(
                            $"A condição de pagamento {code} do documento não existe mais: escolha outra antes de confirmar.");

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

    /// <summary>Valores retidos por tributo, arredondados e só os maiores que zero.</summary>
    private static List<(WithholdingTaxType Type, decimal Amount)> WithheldOf(PurchaseInvoice invoice) =>
        new[]
            {
                (WithholdingTaxType.Iss, invoice.WithheldIss), (WithholdingTaxType.Irrf, invoice.WithheldIrrf),
                (WithholdingTaxType.Inss, invoice.WithheldInss), (WithholdingTaxType.Pis, invoice.WithheldPis),
                (WithholdingTaxType.Cofins, invoice.WithheldCofins), (WithholdingTaxType.Csll, invoice.WithheldCsll),
            }
            .Select(w => (w.Item1, Amount: decimal.Round(w.Item2, 2, MidpointRounding.ToEven)))
            .Where(w => w.Amount > 0m)
            .ToList();

    private sealed record TaxTitle(WithholdingTaxType Type, string CardCode, string? CardName, decimal Amount, DateTime DueDate);

    /// <summary>Um título por tributo com valor > 0. Recusa se faltar o credor ou se o parceiro configurado não existir mais.</summary>
    private async Task<List<TaxTitle>> ResolveWithholdingsAsync(PurchaseInvoice invoice, DateTime issueDate)
    {
        var result = new List<TaxTitle>();
        var nextMonth = new DateTime(issueDate.Year, issueDate.Month, 1).AddMonths(1);

        foreach (var (type, rounded) in WithheldOf(invoice))
        {

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
