using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SAP;

/// <summary>
/// Lista as NF de devolução de venda do SAP (A/R Credit Memo, ORIN) que podem ser vinculadas a um documento de
/// devolução da carga (GAC-1226). Somente leitura no SAP. <see cref="FindAsync"/> aplica as MESMAS regras a um
/// único documento e é o que a ação de vínculo usa para revalidar no servidor.
/// </summary>
/// <remarks>
/// Serviço próprio, e não uma variação de <see cref="SapLinkableInvoicesService"/>: as regras são outras (cliente
/// e quantidade vêm do documento de devolução; não há saldo de carga, liberação nem transporte) e o fluxo de venda
/// não muda. A consulta ao SAP fica em <see cref="BuildSapQuery"/> para o teste de tradução rodá-la no provider
/// SqlServer.
/// </remarks>
public class SapLinkableCreditMemosService(
    AppDbContext context,
    SapErpDbContext sap,
    ISapFiscalDocumentSource fiscal)
{
    public const string NotReturnDocumentMessage = "O documento informado não é uma devolução confirmada desta carga.";

    /// <summary>Modelo '39' do SAP = NF-e (modelo 55 na tabela ONFM).</summary>
    private const string NfeModel = "39";

    /// <summary>RIN1.BaseType da linha gerada a partir de uma A/R Invoice (OINV).</summary>
    private const int ArInvoiceObjectType = 13;

    /// <summary>Tolerância de um milésimo na comparação com a quantidade da devolução.</summary>
    public const decimal QuantityTolerance = 0.001m;

    /// <summary>Máximo de linhas devolvidas.</summary>
    private const int MaxRows = 200;

    /// <summary>Tamanho do lote de ids na consulta ao banco do Siagro (limite de parâmetros).</summary>
    private const int ChunkSize = 1_000;

    private sealed record ReturnInfo(
        Guid Key, string CardCode, string? BranchCode, string ItemCode, decimal Quantity, int? OriginSapDocEntry);

    /// <summary>Notas de devolução vinculáveis no período (<paramref name="dateTo"/> inclui o dia inteiro).</summary>
    public Task<List<SapLinkableCreditMemoDto>> ListAsync(
        Guid loadKey, Guid returnInvoiceKey, DateTime dateFrom, DateTime dateTo, CancellationToken ct = default) =>
        QueryAsync(loadKey, returnInvoiceKey, dateFrom.Date, dateTo.Date.AddDays(1), null, ct);

    /// <summary>Mesmas regras da listagem para uma única nota, sem filtro de período.</summary>
    public async Task<SapLinkableCreditMemoDto?> FindAsync(
        Guid loadKey, Guid returnInvoiceKey, int docEntry, CancellationToken ct = default) =>
        (await QueryAsync(loadKey, returnInvoiceKey, null, null, docEntry, ct)).FirstOrDefault();

    /// <summary>
    /// Filtros que o SQL do SAP consegue aplicar. "Exatamente uma linha" e "item da linha" ficam traduzíveis
    /// (<c>Count()</c> e <c>Any</c>) e só depois vem a projeção.
    /// </summary>
    public static IQueryable<SapCreditMemoProjection> BuildSapQuery(
        SapErpDbContext sap, string itemCode, int branchId, string cardCode,
        DateTime? from, DateTime? toExclusive, int? docEntry)
    {
        var query = sap.SapCreditMemos.AsNoTracking()
            .Where(c => c.Canceled == "N"
                        && c.Model == NfeModel
                        && c.Serial != null
                        && c.BPLId == branchId
                        && c.CardCode == cardCode
                        && c.Lines.Count() == 1
                        && c.Lines.Any(l => l.ItemCode == itemCode)
                        // Sem filtro de quantidade igual: a nota de quantidade diferente aparece (ajuda a achar
                        // erro de digitação); quem barra é o vínculo.
                        && c.Lines.Any(l => l.Quantity > 0));

        if (from is { } f) query = query.Where(c => c.DocDate >= f);
        if (toExclusive is { } t) query = query.Where(c => c.DocDate < t);
        if (docEntry is { } d) query = query.Where(c => c.DocEntry == d);

        return query
            .OrderByDescending(c => c.DocDate)
            .ThenByDescending(c => c.DocEntry)
            .Select(c => new SapCreditMemoProjection
            {
                DocEntry = c.DocEntry,
                DocNum = c.DocNum,
                DocDate = c.DocDate,
                CardCode = c.CardCode,
                CardName = c.CardName,
                BranchId = c.BPLId,
                Serial = c.Serial!.Value,
                SeriesStr = c.SeriesStr,
                Quantity = c.Lines.Select(l => l.Quantity).FirstOrDefault(),
                Price = c.Lines.Select(l => l.Price).FirstOrDefault(),
                LineTotal = c.Lines.Select(l => l.LineTotal).FirstOrDefault(),
                Cfop = c.Lines.Select(l => l.CfopCode).FirstOrDefault(),
                UsageCode = c.Lines.Select(l => l.Usage).FirstOrDefault(),
                BaseType = c.Lines.Select(l => l.BaseType).FirstOrDefault(),
                BaseEntry = c.Lines.Select(l => l.BaseEntry).FirstOrDefault(),
            });
    }

    /// <summary>Linha lida do SAP; classe própria (e não record posicional) para a projeção traduzir.</summary>
    public class SapCreditMemoProjection
    {
        public int DocEntry { get; set; }
        public int DocNum { get; set; }
        public DateTime DocDate { get; set; }
        public string? CardCode { get; set; }
        public string? CardName { get; set; }
        public int? BranchId { get; set; }
        public int Serial { get; set; }
        public string? SeriesStr { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal LineTotal { get; set; }
        public string? Cfop { get; set; }
        public int? UsageCode { get; set; }
        public int? BaseType { get; set; }
        public int? BaseEntry { get; set; }
    }

    /// <summary>
    /// Executa a consulta ao SAP. Costura de teste: permite simular o SAP fora do ar
    /// (<c>DbException</c>/<c>TimeoutException</c>) sem rede, e provar que vira erro de negócio.
    /// </summary>
    protected virtual Task<List<SapCreditMemoProjection>> ExecuteSapQueryAsync(
        IQueryable<SapCreditMemoProjection> query, CancellationToken ct) =>
        query.ToListAsync(ct);

    private async Task<List<SapLinkableCreditMemoDto>> QueryAsync(
        Guid loadKey, Guid returnInvoiceKey, DateTime? from, DateTime? toExclusive, int? docEntry,
        CancellationToken ct)
    {
        var info = await LoadReturnAsync(loadKey, returnInvoiceKey, ct);

        // A filial do Siagro é o BPLId do SAP escrito como texto ("3"); "03" não é a mesma filial.
        if (!int.TryParse(info.BranchCode, out var branchId) || branchId.ToString() != info.BranchCode)
            return [];

        List<SapCreditMemoProjection> rows;
        Dictionary<int, string> usageNames;
        try
        {
            rows = await ExecuteSapQueryAsync(
                BuildSapQuery(sap, info.ItemCode, branchId, info.CardCode, from, toExclusive, docEntry), ct);
            usageNames = await LoadUsageNamesAsync(rows, ct);
        }
        catch (Exception e) when (e is DbException or TimeoutException)
        {
            throw new BusinessException(
                "Não foi possível consultar as notas fiscais no SAP. Tente novamente em instantes.");
        }

        rows = await ExcludeAlreadyLinkedAsync(rows, info.Key, ct);

        IReadOnlyDictionary<int, SapFiscalDocumentInfo> infos = new Dictionary<int, SapFiscalDocumentInfo>();
        if (fiscal.IsAvailable && rows.Count > 0)
        {
            try
            {
                infos = await fiscal.GetForCreditMemosAsync(
                    rows.Select(r => new SapInvoiceRef(r.DocEntry, r.BranchId)).ToList(), ct);
            }
            catch (Exception e) when (e is DbException or TimeoutException)
            {
                throw new BusinessException(
                    "Não foi possível consultar a situação das notas fiscais no add-on fiscal. Tente novamente em instantes.");
            }

            // Sem informação no add-on = não autorizada.
            rows = rows.Where(r => infos.TryGetValue(r.DocEntry, out var fiscalInfo)
                                   && fiscalInfo.Authorized && !fiscalInfo.Cancelled).ToList();
        }

        // A nota que referencia a NF de origem vem primeiro; depois as de quantidade igual; depois as mais recentes.
        return rows
            .Select(r => ToDto(r, info, infos, usageNames))
            .OrderByDescending(d => d.MatchesOrigin)
            .ThenByDescending(d => d.QuantityMatches)
            .ThenByDescending(d => d.DocDate)
            .ThenByDescending(d => d.DocEntry)
            .Take(MaxRows)
            .ToList();
    }

    /// <summary>
    /// O documento de devolução que recebe a nota: da carga informada, tipo Devolução, Confirmado e sem NF-e própria
    /// do Siagro. Qualquer outra coisa é recusada com a mesma mensagem.
    /// </summary>
    private async Task<ReturnInfo> LoadReturnAsync(Guid loadKey, Guid returnInvoiceKey, CancellationToken ct)
    {
        var invoice = await context.SalesInvoices.AsNoTracking()
            .Where(x => x.Key == returnInvoiceKey)
            .Select(x => new
            {
                x.Key, x.ShipmentLoadKey, x.InvoiceType, x.InvoiceStatus, x.IsNfeReturn, x.CardCode, x.BranchCode,
                x.SalesInvoiceOriginKey,
                Items = x.Items.Select(i => new { i.ItemCode, i.Quantity }).ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (invoice is null
            || invoice.ShipmentLoadKey != loadKey
            || invoice.InvoiceType != SalesInvoiceType.Return
            || invoice.InvoiceStatus != InvoiceStatus.Confirmed
            || invoice.IsNfeReturn)
            throw new BusinessException(NotReturnDocumentMessage);

        var items = invoice.Items.Select(i => i.ItemCode).Distinct().ToList();
        if (items.Count != 1)
            throw new BusinessException(NotReturnDocumentMessage);

        int? originSapDocEntry = null;
        if (invoice.SalesInvoiceOriginKey is { } originKey)
            originSapDocEntry = await context.SalesInvoices.AsNoTracking()
                .Where(x => x.Key == originKey)
                .Select(x => x.SapDocEntry)
                .FirstOrDefaultAsync(ct);

        return new ReturnInfo(
            invoice.Key, invoice.CardCode, invoice.BranchCode, items[0], invoice.Items.Sum(i => i.Quantity),
            originSapDocEntry);
    }

    /// <summary>Nomes das naturezas (OUSG) das linhas lidas. Consulta separada: a projeção fica simples.</summary>
    private async Task<Dictionary<int, string>> LoadUsageNamesAsync(
        List<SapCreditMemoProjection> rows, CancellationToken ct)
    {
        var codes = rows.Where(r => r.UsageCode.HasValue).Select(r => r.UsageCode!.Value).Distinct().ToList();
        if (codes.Count == 0)
            return [];

        return await sap.Usages.AsNoTracking()
            .Where(u => codes.Contains(u.Code))
            .ToDictionaryAsync(u => u.Code, u => u.Name, ct);
    }

    /// <summary>
    /// Tira as notas já ligadas a OUTRO documento de saída que não esteja Cancelado nem Devolvido. Dois bancos,
    /// sem join: consulta o Siagro em lotes de ids e remove em memória.
    /// </summary>
    private async Task<List<SapCreditMemoProjection>> ExcludeAlreadyLinkedAsync(
        List<SapCreditMemoProjection> rows, Guid returnInvoiceKey, CancellationToken ct)
    {
        var linked = new HashSet<int>();

        foreach (var chunk in rows.Select(r => r.DocEntry).Chunk(ChunkSize))
        {
            var ids = chunk.Select(id => (int?)id).ToList();

            var found = await context.SalesInvoices
                .AsNoTracking()
                .Where(s => s.SapCreditMemoDocEntry != null && ids.Contains(s.SapCreditMemoDocEntry)
                            && s.Key != returnInvoiceKey
                            && s.InvoiceStatus != InvoiceStatus.Cancelled
                            && s.InvoiceStatus != InvoiceStatus.Returned)
                .Select(s => s.SapCreditMemoDocEntry!.Value)
                .ToListAsync(ct);

            linked.UnionWith(found);
        }

        return rows.Where(r => !linked.Contains(r.DocEntry)).ToList();
    }

    private static SapLinkableCreditMemoDto ToDto(
        SapCreditMemoProjection r, ReturnInfo info, IReadOnlyDictionary<int, SapFiscalDocumentInfo> infos,
        IReadOnlyDictionary<int, string> usageNames)
    {
        var baseInvoice = r.BaseType == ArInvoiceObjectType ? r.BaseEntry : null;

        return new SapLinkableCreditMemoDto
        {
            DocEntry = r.DocEntry,
            DocNum = r.DocNum,
            DocDate = r.DocDate,
            CardCode = r.CardCode,
            CardName = r.CardName,
            TaxDocumentNumber = SapTaxDocumentFormat.Number(r.Serial),
            TaxDocumentSeries = SapTaxDocumentFormat.Series(r.SeriesStr),
            Quantity = r.Quantity,
            UnitPrice = r.Price,
            LineTotal = r.LineTotal,
            Cfop = r.Cfop,
            UsageCode = r.UsageCode,
            UsageName = r.UsageCode is { } usageCode && usageNames.TryGetValue(usageCode, out var usageName)
                ? usageName.Trim()
                : null,
            AccessKey = infos.TryGetValue(r.DocEntry, out var fiscalInfo) ? fiscalInfo.AccessKey : null,
            BaseInvoiceDocEntry = baseInvoice,
            MatchesOrigin = baseInvoice is not null && baseInvoice == info.OriginSapDocEntry,
            QuantityMatches = Math.Abs(r.Quantity - info.Quantity) <= QuantityTolerance,
        };
    }
}
