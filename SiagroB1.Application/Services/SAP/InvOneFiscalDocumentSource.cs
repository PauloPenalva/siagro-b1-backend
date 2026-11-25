using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SAP;

/// <summary>Lê chave de acesso e status da NF-e no banco do add-on InvOne.</summary>
/// <remarks>
/// Como a linha AUTORIZADA vence o desempate, um cancelamento na SEFAZ gravado pelo add-on como NOVA linha de
/// <c>Process</c> ao lado da autorizada original não é visto por aqui — para esse caso a sincronização conta com
/// <c>OINV.CANCELED = 'Y'</c> no SAP.
/// </remarks>
public class InvOneFiscalDocumentSource(InvOneDbContext db) : ISapFiscalDocumentSource
{
    private const int ArInvoiceDocType = 13;

    /// <summary>
    /// DocType do A/R Credit Memo em <c>Process</c>: confirmado na produção da Yokotobi em 24/11/2026.
    /// </summary>
    internal const int ArCreditMemoDocType = 14;
    private const int AuthorizedStatus = 4;
    private const int CancelledStatus = 10;
    private const int BatchSize = 1000;

    public bool IsAvailable => true;

    /// <remarks>
    /// O resultado é indexado por DocEntry: se o mesmo DocEntry for pedido para duas filiais, as
    /// linhas das duas filiais concorrem e vale a melhor delas pela ordem abaixo (autorizada primeiro). Quem chama não deve misturar filiais com o mesmo DocEntry (no SAP o
    /// DocEntry é único por base).
    /// </remarks>
    public Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForInvoicesAsync(
        IReadOnlyCollection<SapInvoiceRef> invoices, CancellationToken ct = default) =>
        GetAsync(invoices, BuildQuery, ct);

    /// <summary>Mesma leitura para as NF de devolução (A/R Credit Memo): só muda o DocType.</summary>
    public Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForCreditMemosAsync(
        IReadOnlyCollection<SapInvoiceRef> creditMemos, CancellationToken ct = default) =>
        GetAsync(creditMemos, BuildCreditMemoQuery, ct);

    /// <summary>
    /// finNFe das invoices autorizadas (DocType 13, StatusId 4), lido do XML assinado. Mesmos lotes e filtro de
    /// filial de <see cref="GetAsync"/>. A sincronização não chama este método: o XML pesa ~4 KB por documento.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, string>> GetInvoicePurposesAsync(
        IReadOnlyCollection<SapInvoiceRef> invoices, CancellationToken ct = default)
    {
        var result = new Dictionary<int, string>();

        var wanted = invoices.Where(i => i.BranchId.HasValue)
            .Select(i => (i.DocEntry, Branch: i.BranchId!.Value))
            .Distinct()
            .ToList();
        var branches = wanted.Select(w => w.Branch).Distinct().ToList();
        var pairs = wanted.ToHashSet();

        foreach (var batch in wanted.Select(w => w.DocEntry).Distinct().Chunk(BatchSize))
        {
            var rows = await BuildQuery(db, batch, branches)
                .Where(p => p.StatusId == AuthorizedStatus)
                .Select(p => new { p.DocEntry, p.CompanyId, p.XmlEnvAssinado })
                .ToListAsync(ct);

            foreach (var row in rows.Where(r => r.DocEntry.HasValue && r.CompanyId.HasValue
                                           && pairs.Contains((r.DocEntry.Value, r.CompanyId.Value))))
            {
                var purpose = ReadPurpose(row.XmlEnvAssinado);
                if (purpose != null) result[row.DocEntry!.Value] = purpose;
            }
        }

        return result;
    }

    private static readonly Regex PurposeRegex = new(@"<finNFe>(\d)</finNFe>", RegexOptions.Compiled);

    /// <summary>
    /// Descomprime o XML (deflate sem cabeçalho; UTF-16LE se o segundo byte for 0, senão UTF-8) e devolve o
    /// finNFe. Qualquer falha devolve null.
    /// </summary>
    internal static string? ReadPurpose(byte[]? compressedXml)
    {
        if (compressedXml is not { Length: > 0 }) return null;
        try
        {
            using var input = new MemoryStream(compressedXml);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            var bytes = output.ToArray();
            if (bytes.Length == 0) return null;

            // BOM primeiro (FF FE = UTF-16LE, EF BB BF = UTF-8); sem BOM vale a heurística do segundo byte.
            var text = bytes is [0xFF, 0xFE, ..] ? Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2)
                : bytes is [0xEF, 0xBB, 0xBF, ..] ? Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
                : bytes.Length > 1 && bytes[1] == 0 ? Encoding.Unicode.GetString(bytes)
                : Encoding.UTF8.GetString(bytes);
            var match = PurposeRegex.Match(text);
            return match.Success ? match.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetAsync(
        IReadOnlyCollection<SapInvoiceRef> invoices,
        Func<InvOneDbContext, IReadOnlyCollection<int>, IReadOnlyCollection<int>, IQueryable<InvOneProcess>> buildQuery,
        CancellationToken ct)
    {
        var result = new Dictionary<int, SapFiscalDocumentInfo>();

        // Sem filial não há como saber de qual base é a linha do add-on: não casa com nada.
        var wanted = invoices.Where(i => i.BranchId.HasValue)
            .Select(i => (i.DocEntry, Branch: i.BranchId!.Value))
            .Distinct()
            .ToList();
        var branches = wanted.Select(w => w.Branch).Distinct().ToList();
        var pairs = wanted.ToHashSet();

        // Lotes: um IN com milhares de itens estoura o limite de parâmetros do SQL Server.
        foreach (var batch in wanted.Select(w => w.DocEntry).Distinct().Chunk(BatchSize))
        {
            // A tabela guarda várias bases (CompanyId = OINV.BPLId): filtra por filial também.
            var rows = await buildQuery(db, batch, branches).ToListAsync(ct);

            foreach (var group in rows.Where(p => p.DocEntry.HasValue && p.CompanyId.HasValue
                                             && pairs.Contains((p.DocEntry.Value, p.CompanyId.Value)))
                         .GroupBy(p => p.DocEntry!.Value))
            {
                // Reenvios geram várias linhas. Ordem explícita (o SQL não garante nenhuma): autorizada,
                // depois cancelada, depois as demais; empate resolvido por protocolo e chave.
                var chosen = group
                    .OrderByDescending(IsAuthorized)
                    .ThenByDescending(IsCancelled)
                    .ThenByDescending(p => p.ProtocolNumber, StringComparer.Ordinal)
                    .ThenByDescending(p => p.KeyNfe, StringComparer.Ordinal)
                    .First();

                result[group.Key] = new SapFiscalDocumentInfo(
                    group.Key,
                    NormalizeKey(chosen.KeyNfe),
                    IsAuthorized(chosen),
                    IsCancelled(chosen),
                    chosen.ProtocolNumber);
            }
        }

        return result;
    }

    /// <summary>Consulta de um lote; separada para o teste provar que traduz para SQL.</summary>
    internal static IQueryable<InvOneProcess> BuildQuery(
        InvOneDbContext db, IReadOnlyCollection<int> docEntries, IReadOnlyCollection<int> branches) =>
        BuildBatchQuery(db, docEntries, branches).Where(p => p.DocType == ArInvoiceDocType);

    /// <summary>O lote das NF de devolução (A/R Credit Memo).</summary>
    internal static IQueryable<InvOneProcess> BuildCreditMemoQuery(
        InvOneDbContext db, IReadOnlyCollection<int> docEntries, IReadOnlyCollection<int> branches) =>
        BuildBatchQuery(db, docEntries, branches).Where(p => p.DocType == ArCreditMemoDocType);

    private static IQueryable<InvOneProcess> BuildBatchQuery(
        InvOneDbContext db, IReadOnlyCollection<int> docEntries, IReadOnlyCollection<int> branches)
    {
        var entries = docEntries.Select(d => (int?)d).ToList();
        var branchList = branches.Select(b => (int?)b).ToList();
        return db.Processes.AsNoTracking()
            .Where(p => entries.Contains(p.DocEntry) && branchList.Contains(p.CompanyId));
    }

    private static bool IsAuthorized(InvOneProcess p) => p.StatusId == AuthorizedStatus;

    private static bool IsCancelled(InvOneProcess p) => p.StatusId == CancelledStatus || p.IsCanceled == true;

    private static string? NormalizeKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length == 44 ? digits : null;
    }
}
