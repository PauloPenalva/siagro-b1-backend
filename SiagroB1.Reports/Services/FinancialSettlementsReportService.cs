using System.Globalization;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Baixas de Recebíveis / Pagáveis: as linhas EFETIVAS do calculador de posição com data no período. Uma baixa distribuída
/// (pagamento do provisório) aparece no título firme composto, com a conta do pagamento original. Estorno sai negativo.
/// </summary>
public class FinancialSettlementsReportService(IUnitOfWork db, IFastReportService reportService)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string? Validate(FinancialSettlementsRequest request)
    {
        if (request.DateFrom == default || request.DateTo == default)
            return "Informe o período.";

        if (request.DateFrom.Date > request.DateTo.Date)
            return "A data inicial deve ser menor ou igual à final.";

        return null;
    }

    public async Task<byte[]> ExecuteAsync(FinancialSettlementsRequest request)
    {
        if (Validate(request) is { } error)
            throw new ApplicationException(error);

        var from = request.DateFrom.Date;
        var baseDate = request.DateTo.Date;
        var cardCode = string.IsNullOrWhiteSpace(request.CardCode) ? null : request.CardCode.Trim();
        var accountCode = string.IsNullOrWhiteSpace(request.AccountCode) ? null : request.AccountCode.Trim();

        var position = await FinancialPositionQuery.LoadAsync(db.Context,
            new FinancialPositionFilter(request.Direction, cardCode, baseDate, SettlementDateFrom: from),
            request.Mode, CancellationToken.None);

        // Filtros repetidos de propósito: a expansão do grupo traz documentos de outro parceiro/direção.
        var slices = position.Settlements
            .Where(s => s.Document.Direction == request.Direction
                        && (cardCode is null || s.Document.CardCode == cardCode)
                        && s.Date >= from && s.Date <= baseDate
                        // Abatimento de devolução não tem conta (não é dinheiro): o filtro por conta não o inclui.
                        && (accountCode is null || (!s.IsReturnNetting && s.Source.FinancialAccountCode == accountCode)))
            .OrderBy(s => s.Date)
            .ThenBy(s => s.Document.Currency)
            .ThenBy(s => PartnerName(s.Document), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(s => s.Document.Code, StringComparer.Ordinal)
            .ToList();

        // Baixa distribuída: s.Document já é o firme composto, então a nota é a do firme.
        var invoices = await FinancialDocumentInvoiceLookup.LoadAsync(db.Context, slices.Select(s => s.Document), CancellationToken.None);

        var rows = slices.Select(s =>
        {
            var currency = CurrencyCode(s.Document.Currency);
            var invoice = invoices.For(s.Document);
            return new FinancialSettlementRowDto
            {
                GroupKey = $"{s.Date:yyyyMMdd} · {currency}",
                Date = s.Date.ToString("dd/MM/yyyy"),
                Partner = PartnerName(s.Document),
                Document = s.Document.Code ?? string.Empty,
                NfeNumber = invoice.Number,
                NfeSeries = invoice.Series,
                Account = s.IsReturnNetting ? string.Empty : s.Source.FinancialAccountCode ?? string.Empty,
                Method = s.IsReturnNetting ? "Abatimento de devolução" : MethodLabel(s.Source.Origin),
                Currency = currency,
                // Devolução: o calculador usa sinal negativo; o relatório mostra o sinal gravado no ledger.
                Amount = IsReturn(s.Document) ? -s.Amount : s.Amount,
                Interest = s.Interest,
                Fine = s.Fine,
                Discount = s.Discount
            };
        }).ToList();

        var totals = rows.GroupBy(r => r.Currency)
            .OrderBy(g => g.Key == "BRL" ? 0 : 1).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => string.Create(PtBr,
                $"Total {g.Key}: valor {g.Sum(r => r.Amount):N2} · juros {g.Sum(r => r.Interest):N2} · multa {g.Sum(r => r.Fine):N2} · desconto {g.Sum(r => r.Discount):N2}"))
            .ToList();

        var parameters = new Dictionary<string, object>
        {
            ["pTitle"] = request.Direction == FinancialDirection.Receivable ? "Baixas de Recebíveis" : "Baixas de Pagáveis",
            ["pPeriod"] = $"Baixas de {from:dd/MM/yyyy} a {baseDate:dd/MM/yyyy}",
            ["pMode"] = request.Mode == FinancialPositionMode.Firm ? "Títulos: Firmes" : "Títulos: Provisórios",
            ["pTotals"] = string.Join("\n", totals),
            ["pRowCount"] = rows.Count
        };

        return await reportService.GeneratePdfAsync(
            "FinancialSettlements.frx", rows, "SettlementRows", "SettlementRows", parameters);
    }

    internal static string MethodLabel(FinancialSettlementOrigin origin) => origin switch
    {
        FinancialSettlementOrigin.Manual => "Baixa",
        FinancialSettlementOrigin.Reversal => "Estorno",
        FinancialSettlementOrigin.InvoiceOffset => "Abatimento",
        FinancialSettlementOrigin.AdvanceApplication => "Aplicação de adiantamento",
        FinancialSettlementOrigin.Netting => "Encontro de contas",
        FinancialSettlementOrigin.AdvanceRefund => "Devolução de adiantamento",
        FinancialSettlementOrigin.Dation => "Dação",
        FinancialSettlementOrigin.AdvanceTransfer => "Transferência de adiantamento",
        _ => origin.ToString()
    };

    private static bool IsReturn(FinancialDocument d) =>
        d.Nature is FinancialDocumentNature.PurchaseReturn or FinancialDocumentNature.SalesReturn;

    private static string PartnerName(FinancialDocument d) =>
        string.IsNullOrWhiteSpace(d.CardName) ? d.CardCode : d.CardName;

    private static string CurrencyCode(CurrencyType currency) => currency == CurrencyType.Usd ? "USD" : "BRL";
}
