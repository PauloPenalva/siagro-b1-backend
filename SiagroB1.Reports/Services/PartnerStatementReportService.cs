using System.Globalization;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Extrato do Parceiro (spec R6/C2): saldo anterior = posição em DateFrom − 1 dia; movimentos = o que mudou na posição a
/// cada dia do período; saldo final = posição em DateTo. Os movimentos são DIFERENÇAS entre a posição de um dia e a do
/// anterior (por título e, nas baixas, por linha efetiva do calculador), então saldo anterior + movimentos = saldo final
/// por construção, inclusive com a distribuição de R5/R7 (que redistribui dinheiro antigo quando um firme é composto) e o
/// composto de R8 (a "emissão" do provisório é cada variação do composto, na data da composição).
/// Sinal: saldo = débitos − créditos, positivo = parceiro deve à empresa. Valores só de principal (sem juros/multa/desconto).
/// </summary>
public class PartnerStatementReportService(IUnitOfWork db, IFastReportService reportService)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string? Validate(PartnerStatementRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CardCode))
            return "Informe o parceiro.";

        if (request.DateFrom == default || request.DateTo == default)
            return "Informe o período.";

        if (request.DateFrom.Date > request.DateTo.Date)
            return "A data inicial deve ser menor ou igual à final.";

        return null;
    }

    public async Task<byte[]> ExecuteAsync(PartnerStatementRequest request)
    {
        if (Validate(request) is { } error)
            throw new ApplicationException(error);

        var from = request.DateFrom.Date;
        var to = request.DateTo.Date;
        var cardCode = request.CardCode!.Trim();

        var input = await FinancialPositionQuery.LoadInputAsync(db.Context,
            new FinancialPositionFilter(request.Direction, cardCode, to), CancellationToken.None);

        Snapshot Take(DateTime day) =>
            Snapshot.Of(FinancialPositionCalculator.Compute(input, day, request.Mode), cardCode, request.Direction);

        // Só muda a posição num dia com emissão ou baixa; DateTo entra sempre para fechar no saldo final.
        var days = input.Documents.Select(d => d.DocumentDate.Date)
            .Concat(input.Settlements.Select(s => s.SettlementDate.Date))
            .Where(d => d >= from && d <= to)
            .Append(to)
            .Distinct().Order().ToList();

        var opening = Take(from.AddDays(-1));
        var previous = opening;
        var movements = new List<Movement>();
        foreach (var day in days)
        {
            var current = Take(day);
            movements.AddRange(Diff(previous, current, day));
            previous = current;
        }
        var closing = previous;

        var openingByCurrency = opening.BalanceByCurrency();
        var closingByCurrency = closing.BalanceByCurrency();

        var invoices = await FinancialDocumentInvoiceLookup.LoadAsync(db.Context,
            movements.Select(m => m.Document).DistinctBy(d => d.Key), CancellationToken.None);

        var rows = new List<PartnerStatementRowDto>();
        foreach (var group in movements.GroupBy(m => CurrencyCode(m.Document.Currency)).OrderBy(g => CurrencyOrder(g.Key)))
        {
            var balance = openingByCurrency.GetValueOrDefault(group.Key);
            foreach (var m in group.OrderBy(m => m.Date).ThenBy(m => m.IsSettlement ? 1 : 0)
                         .ThenBy(m => m.Document.Code, StringComparer.Ordinal).ThenBy(m => m.Sequence))
            {
                balance += m.Debit - m.Credit;
                var invoice = invoices.For(m.Document);
                rows.Add(new PartnerStatementRowDto
                {
                    Currency = group.Key,
                    Date = m.Date.ToString("dd/MM/yyyy"),
                    History = m.History,
                    Document = m.Document.Code ?? string.Empty,
                    NfeNumber = invoice.Number,
                    NfeSeries = invoice.Series,
                    Debit = m.Debit,
                    Credit = m.Credit,
                    Balance = balance,
                    OpeningBalance = openingByCurrency.GetValueOrDefault(group.Key),
                    ClosingBalance = closingByCurrency.GetValueOrDefault(group.Key)
                });
            }
        }

        var currencies = openingByCurrency.Keys.Concat(closingByCurrency.Keys).Concat(rows.Select(r => r.Currency))
            .DefaultIfEmpty("BRL").Distinct().OrderBy(CurrencyOrder).ToList();
        var totals = currencies.Select(c => string.Create(PtBr,
            $"Saldo anterior {c}: {openingByCurrency.GetValueOrDefault(c):N2} · Saldo final {c}: {closingByCurrency.GetValueOrDefault(c):N2}"));

        var partnerName = input.Documents.Where(d => d.CardCode == cardCode && !string.IsNullOrWhiteSpace(d.CardName))
            .Select(d => d.CardName).FirstOrDefault();

        var parameters = new Dictionary<string, object>
        {
            ["pTitle"] = "Extrato do Parceiro",
            ["pPartner"] = partnerName is null ? $"Parceiro: {cardCode}" : $"Parceiro: {cardCode} - {partnerName}",
            ["pPeriod"] = $"Período de {from:dd/MM/yyyy} a {to:dd/MM/yyyy}",
            ["pMode"] = request.Mode == FinancialPositionMode.Firm ? "Títulos: Firmes" : "Títulos: Provisórios",
            ["pDirection"] = request.Direction switch
            {
                FinancialDirection.Receivable => "Direção: A Receber",
                FinancialDirection.Payable => "Direção: A Pagar",
                _ => "Direção: Ambos"
            },
            ["pTotals"] = string.Join("\n", totals),
            ["pRowCount"] = rows.Count
        };

        return await reportService.GeneratePdfAsync(
            "PartnerStatement.frx", rows, "StatementRows", "StatementRows", parameters);
    }

    private static IEnumerable<Movement> Diff(Snapshot previous, Snapshot current, DateTime day)
    {
        var sequence = 0;
        foreach (var key in current.Titles.Keys.Union(previous.Titles.Keys))
        {
            var document = (current.Titles.GetValueOrDefault(key) ?? previous.Titles[key]).Document;
            var delta = (current.Titles.GetValueOrDefault(key)?.Amount ?? 0m) - (previous.Titles.GetValueOrDefault(key)?.Amount ?? 0m);
            if (delta != 0m)
                yield return Movement.Create(document, day, $"Emissão {document.Code}", delta, isSettlement: false, sequence++);
        }

        foreach (var key in current.Slices.Keys.Union(previous.Slices.Keys))
        {
            var slice = current.Slices.GetValueOrDefault(key) ?? previous.Slices[key];
            var delta = (current.Slices.GetValueOrDefault(key)?.Amount ?? 0m) - (previous.Slices.GetValueOrDefault(key)?.Amount ?? 0m);
            if (delta == 0m) continue;
            var method = slice.IsReturnNetting
                ? "Abatimento de devolução"
                : FinancialSettlementsReportService.MethodLabel(slice.Source.Origin);
            yield return Movement.Create(slice.Document, day, $"{method} {slice.Document.Code}", delta, isSettlement: true, sequence++);
        }
    }

    private sealed record Movement(FinancialDocument Document, DateTime Date, string History, decimal Debit, decimal Credit,
        bool IsSettlement, int Sequence)
    {
        /// <summary>
        /// C2. <paramref name="delta"/> está no sinal do calculador (valor do título negativo para devolução; baixa reduz o
        /// aberto). Coluna: a receber emissão = débito, baixa = crédito; a pagar o inverso; devolução na coluna oposta.
        /// O valor na coluna mantém o sinal (estorno sai negativo na coluna da baixa).
        /// </summary>
        public static Movement Create(FinancialDocument document, DateTime date, string history, decimal delta,
            bool isSettlement, int sequence)
        {
            var sign = document.Direction == FinancialDirection.Receivable ? 1m : -1m;
            var signed = isSettlement ? -sign * delta : sign * delta; // efeito no saldo (débito − crédito)
            var debitColumn = (document.Direction == FinancialDirection.Receivable) ^ isSettlement ^ IsReturn(document);
            return debitColumn
                ? new Movement(document, date, history, signed, 0m, isSettlement, sequence)
                : new Movement(document, date, history, 0m, -signed, isSettlement, sequence);
        }
    }

    private sealed record SliceTotal(FinancialDocument Document, FinancialSettlement Source, bool IsReturnNetting, decimal Amount);

    private sealed record Snapshot(Dictionary<Guid, PositionTitle> Titles,
        Dictionary<(Guid Document, Guid Source, bool Netting), SliceTotal> Slices)
    {
        public static Snapshot Of(FinancialPosition position, string cardCode, FinancialDirection? direction)
        {
            bool Mine(FinancialDocument d) => d.CardCode == cardCode && (direction is null || d.Direction == direction);

            var titles = position.Titles.Where(t => Mine(t.Document)).ToDictionary(t => t.Document.Key);
            var slices = position.Settlements.Where(s => Mine(s.Document))
                .GroupBy(s => (s.Document.Key, s.Source.Key, s.IsReturnNetting))
                .ToDictionary(g => g.Key, g => new SliceTotal(g.First().Document, g.First().Source, g.Key.IsReturnNetting,
                    g.Sum(s => s.Amount)));
            return new Snapshot(titles, slices);
        }

        public Dictionary<string, decimal> BalanceByCurrency() => Titles.Values
            .GroupBy(t => CurrencyCode(t.Document.Currency))
            .ToDictionary(g => g.Key, g => g.Sum(t =>
                (t.Document.Direction == FinancialDirection.Receivable ? 1m : -1m) * t.OpenAtBase));
    }

    private static bool IsReturn(FinancialDocument d) =>
        d.Nature is FinancialDocumentNature.PurchaseReturn or FinancialDocumentNature.SalesReturn;

    private static int CurrencyOrder(string currency) => currency == "BRL" ? 0 : 1;

    private static string CurrencyCode(CurrencyType currency) => currency == CurrencyType.Usd ? "USD" : "BRL";
}
