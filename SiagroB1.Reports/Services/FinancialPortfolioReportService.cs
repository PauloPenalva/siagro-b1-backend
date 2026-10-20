using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Carteira de Contas a Receber / a Pagar na data-base (fim do período de vencimento), agrupada por parceiro e moeda.
/// A posição vem de <see cref="FinancialPositionQuery"/> (grupo de composição inteiro); o filtro de parceiro, vencimento
/// e saldo é aplicado aqui, depois do cálculo. Devolução de contrato aparece negativa (R7).
/// </summary>
public class FinancialPortfolioReportService(IUnitOfWork db, IFastReportService reportService)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string? Validate(FinancialPortfolioRequest request)
    {
        if (request.DueDateFrom == default || request.DueDateTo == default)
            return "Informe o período.";

        if (request.DueDateFrom.Date > request.DueDateTo.Date)
            return "A data inicial deve ser menor ou igual à final.";

        return null;
    }

    public async Task<byte[]> ExecuteAsync(FinancialPortfolioRequest request)
    {
        if (Validate(request) is { } error)
            throw new ApplicationException(error);

        var from = request.DueDateFrom.Date;
        var baseDate = request.DueDateTo.Date;
        var cardCode = string.IsNullOrWhiteSpace(request.CardCode) ? null : request.CardCode.Trim();

        var position = await FinancialPositionQuery.LoadAsync(db.Context,
            new FinancialPositionFilter(request.Direction, cardCode, baseDate, from, baseDate), request.Mode, CancellationToken.None);

        // Filtros repetidos de propósito: a expansão do grupo de composição traz documentos de outro parceiro, de outra
        // direção ou fora do período, que entram no cálculo mas não na carteira.
        var titles = position.Titles
            .Where(t => t.Document.Direction == request.Direction
                        && (cardCode is null || t.Document.CardCode == cardCode)
                        && t.Document.DueDate.Date >= from && t.Document.DueDate.Date <= baseDate
                        && t.OpenAtBase != 0m)
            .OrderBy(t => PartnerName(t.Document), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.Document.CardCode, StringComparer.Ordinal)
            .ThenBy(t => t.Document.Currency)
            .ThenBy(t => t.Document.DueDate.Date)
            .ThenBy(t => t.Document.Code, StringComparer.Ordinal)
            .ToList();

        var contractCodes = await ContractCodesAsync(titles.Select(t => t.Document).ToList());
        var invoices = await FinancialDocumentInvoiceLookup.LoadAsync(db.Context, titles.Select(t => t.Document), CancellationToken.None);

        var rows = titles.Select(t =>
        {
            var d = t.Document;
            var currency = CurrencyCode(d.Currency);
            var overdue = (baseDate - d.DueDate.Date).Days;
            var invoice = invoices.For(d);
            return new FinancialPortfolioRowDto
            {
                GroupKey = $"{d.CardCode} · {currency}",
                Partner = PartnerName(d),
                Document = d.Code ?? string.Empty,
                Origin = Origin(d, contractCodes),
                NfeNumber = invoice.Number,
                NfeSeries = invoice.Series,
                DocumentDate = d.DocumentDate.ToString("dd/MM/yyyy"),
                DueDate = d.DueDate.ToString("dd/MM/yyyy"),
                Currency = currency,
                Amount = t.Amount,
                Settled = t.SettledAtBase,
                Open = t.OpenAtBase,
                DaysOverdue = overdue > 0 ? overdue : 0
            };
        }).ToList();

        var totals = rows.GroupBy(r => r.Currency)
            .OrderBy(g => g.Key == "BRL" ? 0 : 1).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => string.Create(PtBr,
                $"Total {g.Key}: valor {g.Sum(r => r.Amount):N2} · baixado {g.Sum(r => r.Settled):N2} · saldo {g.Sum(r => r.Open):N2}"))
            .ToList();

        var parameters = new Dictionary<string, object>
        {
            ["pTitle"] = request.Direction == FinancialDirection.Receivable ? "Contas a Receber" : "Contas a Pagar",
            ["pPeriod"] = $"Vencimento: {from:dd/MM/yyyy} a {baseDate:dd/MM/yyyy} · Posição em {baseDate:dd/MM/yyyy} (data-base)",
            ["pMode"] = request.Mode == FinancialPositionMode.Firm ? "Títulos: Firmes" : "Títulos: Provisórios",
            ["pTotals"] = string.Join("\n", totals),
            ["pRowCount"] = rows.Count
        };

        return await reportService.GeneratePdfAsync(
            "FinancialPortfolio.frx", rows, "PortfolioRows", "PortfolioRows", parameters);
    }

    private async Task<Dictionary<Guid, string?>> ContractCodesAsync(List<FinancialDocument> documents)
    {
        var purchaseKeys = documents.Select(d => d.PurchaseContractKey).OfType<Guid>().Distinct().ToList();
        var salesKeys = documents.Select(d => d.SalesContractKey).OfType<Guid>().Distinct().ToList();
        var codes = new Dictionary<Guid, string?>();
        if (purchaseKeys.Count > 0)
            foreach (var c in await db.Context.PurchaseContracts.AsNoTracking().Where(c => purchaseKeys.Contains(c.Key))
                         .Select(c => new { c.Key, c.Code }).ToListAsync())
                codes[c.Key] = c.Code;
        if (salesKeys.Count > 0)
            foreach (var c in await db.Context.SalesContracts.AsNoTracking().Where(c => salesKeys.Contains(c.Key))
                         .Select(c => new { c.Key, c.Code }).ToListAsync())
                codes[c.Key] = c.Code;
        return codes;
    }

    // Origem: número do documento de origem gravado no título, senão o código do contrato, senão "Manual".
    private static string Origin(FinancialDocument d, Dictionary<Guid, string?> contractCodes)
    {
        if (!string.IsNullOrWhiteSpace(d.OriginDocNumber)) return d.OriginDocNumber;
        var contractKey = d.PurchaseContractKey ?? d.SalesContractKey;
        if (contractKey is { } key && contractCodes.GetValueOrDefault(key) is { Length: > 0 } code) return code;
        return "Manual";
    }

    private static string PartnerName(FinancialDocument d) =>
        string.IsNullOrWhiteSpace(d.CardName) ? d.CardCode : d.CardName;

    private static string CurrencyCode(CurrencyType currency) => currency == CurrencyType.Usd ? "USD" : "BRL";
}
