using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

/// <summary>
/// ZIP para o contador com as NF-e do período: emitidas pelo Siagro (saída e entrada própria) com procNFe,
/// evento de cancelamento, CC-e, inutilizações e DANFE; e os XMLs de terceiros da Distribuição de DF-e.
/// O período segue a data de autorização (inutilização: data do retorno; terceiros: data de emissão).
/// Um <c>resumo.csv</c> lista tudo o que foi exportado para conferência.
/// </summary>
public partial class NfeExportService(IUnitOfWork db, DanfeReportService danfe)
{
    public const int MaxDays = 366;
    public const string NothingFound = "Nenhuma NF-e encontrada no período.";
    public const string InvalidPeriod = "Informe um período válido (data inicial até a final, no máximo 366 dias).";

    private sealed record Doc(Guid Key, string? Branch, string? Number, string? Series, string? AccessKey,
        DateTime? AuthorizedAt, string CardCode, string? CardName, NfeStatus Status);

    private sealed record XmlRow(Guid DocKey, NfeXmlKind Kind, string Xml, DateTime CreatedAt);

    private sealed record CceRow(Guid DocKey, int Sequence, string Xml);

    private sealed record VoidRow(string? Branch, string? Number, string? Series, string Xml, DateTime CreatedAt);

    private sealed class Source
    {
        public required string Folder { get; init; }
        public required string Label { get; init; }
        public required Func<DateTime, DateTime, string?, Task<List<Doc>>> Docs { get; init; }
        public required Func<List<Guid>, Task<List<XmlRow>>> Xmls { get; init; }
        public required Func<List<Guid>, Task<List<CceRow>>> Corrections { get; init; }
        public required Func<DateTime, DateTime, string?, Task<List<VoidRow>>> Voids { get; init; }
        public required Func<Guid, Task<(byte[] Pdf, string FileName)>> Danfe { get; init; }
    }

    public async Task<(byte[] Zip, string FileName)> BuildAsync(NfeExportRequest request)
    {
        var from = request.StartDate.Date;
        var to = request.EndDate.Date.AddDays(1);
        if (request.StartDate == default || request.EndDate == default || from >= to || (to - from).TotalDays > MaxDays)
            throw new DefaultException(InvalidPeriod);

        var branch = string.IsNullOrWhiteSpace(request.BranchCode) ? null : request.BranchCode.Trim();
        _names.Clear();
        var summary = new StringBuilder("Tipo;Filial;Série;Número;Chave;Data;Parceiro;Nome;Valor;Situação;Observação\r\n");
        var count = 0;

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var source in new[] { Sales(), Purchases() })
                count += await AddEmittedAsync(zip, source, from, to, branch, request.IncludeDanfe, summary);

            if (request.IncludeReceived)
                count += await AddReceivedAsync(zip, from, to, branch, summary);

            if (count == 0)
                throw new DefaultException(NothingFound);

            // BOM: o Excel abre o CSV em UTF-8 com os acentos certos.
            Write(zip, "resumo.csv", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(summary.ToString())).ToArray());
        }

        var fileName = $"NFe-{from:yyyyMMdd}-{to.AddDays(-1):yyyyMMdd}{(branch == null ? "" : "-" + branch)}.zip";
        return (stream.ToArray(), fileName);
    }

    private async Task<int> AddEmittedAsync(ZipArchive zip, Source source, DateTime from, DateTime to, string? branch,
        bool includeDanfe, StringBuilder summary)
    {
        var docs = await source.Docs(from, to, branch);
        var keys = docs.Select(d => d.Key).ToList();
        var xmls = keys.Count == 0 ? [] : await source.Xmls(keys);
        var corrections = keys.Count == 0 ? [] : await source.Corrections(keys);
        var count = 0;

        foreach (var doc in docs.OrderBy(d => d.AuthorizedAt))
        {
            var mine = xmls.Where(x => x.DocKey == doc.Key).OrderByDescending(x => x.CreatedAt).ToList();
            var authorized = mine.FirstOrDefault(x => x.Kind == NfeXmlKind.Authorized)?.Xml;
            var accessKey = doc.AccessKey ?? doc.Key.ToString();
            var notes = new List<string>();

            if (authorized == null)
                notes.Add("XML autorizado não encontrado");
            else
                Write(zip, $"{source.Folder}/XML/{accessKey}-procNFe.xml", authorized);

            var cancellation = mine.FirstOrDefault(x => x.Kind == NfeXmlKind.CancellationEvent)?.Xml;
            if (cancellation != null)
                Write(zip, $"{source.Folder}/Cancelamentos/{accessKey}-cancelamento.xml", cancellation);

            foreach (var cce in corrections.Where(c => c.DocKey == doc.Key).OrderBy(c => c.Sequence))
                Write(zip, $"{source.Folder}/CCe/{accessKey}-cce-{cce.Sequence}.xml", cce.Xml);

            if (includeDanfe && authorized != null)
            {
                try
                {
                    var (pdf, _) = await source.Danfe(doc.Key);
                    Write(zip, $"{source.Folder}/DANFE/{accessKey}-danfe.pdf", pdf);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    // Um DANFE com problema não derruba a exportação: o XML vai e o resumo avisa.
                    notes.Add("DANFE não gerado: " + e.Message);
                }
            }

            summary.AppendLine(Csv(source.Label, doc.Branch, doc.Series, doc.Number, doc.AccessKey, doc.AuthorizedAt,
                doc.CardCode, doc.CardName, authorized == null ? null : TotalFromXml(authorized),
                doc.Status == NfeStatus.Cancelled ? "Cancelada" : "Autorizada", string.Join(" | ", notes)));
            count++;
        }

        foreach (var row in await source.Voids(from, to, branch))
        {
            Write(zip, $"{source.Folder}/Inutilizacoes/inutilizacao-serie{row.Series}-{row.Number}.xml", row.Xml);
            summary.AppendLine(Csv(source.Label, row.Branch, row.Series, row.Number, null, row.CreatedAt,
                null, null, null, "Inutilizada", null));
            count++;
        }

        return count;
    }

    private async Task<int> AddReceivedAsync(ZipArchive zip, DateTime from, DateTime to, string? branch, StringBuilder summary)
    {
        var start = new DateTimeOffset(from);
        var end = new DateTimeOffset(to);
        var received = await db.Context.ReceivedDfes.AsNoTracking()
            .Where(x => x.Xml != null && x.IssueDate >= start && x.IssueDate < end && (branch == null || x.BranchCode == branch))
            .OrderBy(x => x.IssueDate)
            .Select(x => new { x.BranchCode, x.AccessKey, x.IssueDate, x.IssuerTaxId, x.IssuerName, x.TotalValue, x.Situation, x.Xml })
            .ToListAsync();

        foreach (var x in received)
        {
            Write(zip, $"Terceiros/XML/{x.AccessKey}.xml", x.Xml!);
            summary.AppendLine(Csv("Terceiros", x.BranchCode, null, null, x.AccessKey, x.IssueDate?.LocalDateTime,
                x.IssuerTaxId, x.IssuerName, x.TotalValue, x.Situation.ToString(), null));
        }

        return received.Count;
    }

    private Source Sales() => new()
    {
        Folder = "Saidas",
        Label = "Saída",
        Docs = (from, to, branch) => db.Context.SalesInvoices.AsNoTracking()
            .Where(i => (i.NfeStatus == NfeStatus.Authorized || i.NfeStatus == NfeStatus.Cancelled)
                        && i.NfeAuthorizedAt >= from && i.NfeAuthorizedAt < to && (branch == null || i.BranchCode == branch))
            .Select(i => new Doc(i.Key, i.BranchCode, i.TaxDocumentNumber, i.TaxDocumentSeries, i.ChaveNFe,
                i.NfeAuthorizedAt, i.CardCode, i.CardName, i.NfeStatus))
            .ToListAsync(),
        Xmls = keys => db.Context.SalesInvoiceNfeXmls.AsNoTracking()
            .Where(x => keys.Contains(x.SalesInvoiceKey) && (x.Kind == NfeXmlKind.Authorized || x.Kind == NfeXmlKind.CancellationEvent))
            .Select(x => new XmlRow(x.SalesInvoiceKey, x.Kind, x.Xml, x.CreatedAt))
            .ToListAsync(),
        Corrections = keys => db.Context.SalesInvoiceNfeCorrections.AsNoTracking()
            .Where(x => keys.Contains(x.SalesInvoiceKey) && x.ProcEventXml != null)
            .Select(x => new CceRow(x.SalesInvoiceKey, x.Sequence, x.ProcEventXml!))
            .ToListAsync(),
        Voids = (start, end, branch) => (
                from x in db.Context.SalesInvoiceNfeXmls.AsNoTracking()
                join i in db.Context.SalesInvoices.AsNoTracking() on x.SalesInvoiceKey equals i.Key
                where x.Kind == NfeXmlKind.NumberVoid && x.CreatedAt >= start && x.CreatedAt < end && (branch == null || i.BranchCode == branch)
                select new VoidRow(i.BranchCode, i.TaxDocumentNumber, i.TaxDocumentSeries, x.Xml, x.CreatedAt))
            .ToListAsync(),
        Danfe = danfe.GeneratePdfAsync,
    };

    private Source Purchases() => new()
    {
        Folder = "Entradas",
        Label = "Entrada própria",
        Docs = (from, to, branch) => db.Context.PurchaseInvoices.AsNoTracking()
            .Where(i => (i.NfeStatus == NfeStatus.Authorized || i.NfeStatus == NfeStatus.Cancelled)
                        && i.NfeAuthorizedAt >= from && i.NfeAuthorizedAt < to && (branch == null || i.BranchCode == branch))
            .Select(i => new Doc(i.Key, i.BranchCode, i.TaxDocumentNumber, i.TaxDocumentSeries, i.ChaveNFe,
                i.NfeAuthorizedAt, i.CardCode, i.CardName, i.NfeStatus))
            .ToListAsync(),
        Xmls = keys => db.Context.PurchaseInvoiceNfeXmls.AsNoTracking()
            .Where(x => keys.Contains(x.PurchaseInvoiceKey) && (x.Kind == NfeXmlKind.Authorized || x.Kind == NfeXmlKind.CancellationEvent))
            .Select(x => new XmlRow(x.PurchaseInvoiceKey, x.Kind, x.Xml, x.CreatedAt))
            .ToListAsync(),
        Corrections = keys => db.Context.PurchaseInvoiceNfeCorrections.AsNoTracking()
            .Where(x => keys.Contains(x.PurchaseInvoiceKey) && x.ProcEventXml != null)
            .Select(x => new CceRow(x.PurchaseInvoiceKey, x.Sequence, x.ProcEventXml!))
            .ToListAsync(),
        Voids = (start, end, branch) => (
                from x in db.Context.PurchaseInvoiceNfeXmls.AsNoTracking()
                join i in db.Context.PurchaseInvoices.AsNoTracking() on x.PurchaseInvoiceKey equals i.Key
                where x.Kind == NfeXmlKind.NumberVoid && x.CreatedAt >= start && x.CreatedAt < end && (branch == null || i.BranchCode == branch)
                select new VoidRow(i.BranchCode, i.TaxDocumentNumber, i.TaxDocumentSeries, x.Xml, x.CreatedAt))
            .ToListAsync(),
        Danfe = danfe.GeneratePurchasePdfAsync,
    };

    // ZipArchive em modo Create não deixa consultar entradas: os nomes já usados ficam aqui (serviço é scoped, um pedido por vez).
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    private void Write(ZipArchive zip, string name, string content) => Write(zip, name, Encoding.UTF8.GetBytes(content));

    private void Write(ZipArchive zip, string name, byte[] content)
    {
        // Mesmo nome duas vezes (chave repetida em documentos diferentes) não sobrescreve: ganha sufixo.
        var path = name;
        for (var n = 2; !_names.Add(path); n++)
            path = Path.ChangeExtension(name, null) + $"-{n}" + Path.GetExtension(name);

        using var entry = zip.CreateEntry(path, CompressionLevel.Optimal).Open();
        entry.Write(content, 0, content.Length);
    }

    private static decimal? TotalFromXml(string xml)
    {
        var match = TotalRegex().Match(xml);
        return match.Success && decimal.TryParse(match.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var total)
            ? total
            : null;
    }

    private static string Csv(string type, string? branch, string? series, string? number, string? accessKey, DateTime? date,
        string? partner, string? name, decimal? total, string status, string? notes)
    {
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        string[] cells =
        [
            type, branch ?? "", series ?? "", number ?? "",
            // Aspas + "=" impedem o Excel de mostrar a chave de 44 dígitos em notação científica.
            string.IsNullOrEmpty(accessKey) ? "" : $"=\"{accessKey}\"",
            date?.ToString("dd/MM/yyyy HH:mm", pt) ?? "", partner ?? "", name ?? "",
            total?.ToString("N2", pt) ?? "", status, notes ?? "",
        ];
        return string.Join(';', cells.Select(c => c.Contains(';') || c.Contains('\n') ? $"\"{c.Replace("\"", "\"\"")}\"" : c));
    }

    [GeneratedRegex(@"<vNF>([\d.]+)</vNF>")]
    private static partial Regex TotalRegex();
}
