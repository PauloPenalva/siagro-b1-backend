using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SAP;

/// <summary>
/// Lista as A/R Invoices do SAP que podem ser vinculadas a uma carga (GAC-1226). Somente leitura
/// no SAP. <see cref="FindAsync"/> aplica as MESMAS regras a um único documento e é o que a ação
/// de vínculo usa para revalidar no servidor.
/// </summary>
/// <remarks>
/// A consulta ao SAP é montada em <see cref="BuildSapQuery"/> (um <see cref="IQueryable"/> puro)
/// para o teste de tradução poder rodá-la no provider SqlServer: o InMemory aceita LINQ que o SQL
/// Server recusa. A exclusão das já vinculadas é em dois passos, sem join entre bancos.
/// </remarks>
public class SapLinkableInvoicesService(
    AppDbContext context,
    SapErpDbContext sap,
    ISapFiscalDocumentSource fiscal,
    ShipmentLoadsBillingGuardService loadGuard)
{
    /// <summary>Modelo '39' do SAP = NF-e (modelo 55 na tabela ONFM).</summary>
    private const string NfeModel = "39";

    /// <summary>Tolerância de um milésimo: carga com saldo até este valor não tem o que vincular.</summary>
    private const decimal QuantityTolerance = 0.001m;

    /// <summary>Máximo de linhas devolvidas, as mais recentes primeiro.</summary>
    private const int MaxRows = 200;

    /// <summary>Tamanho do lote de ids na consulta ao banco do Siagro (limite de parâmetros).</summary>
    private const int ChunkSize = 1_000;

    private sealed record LoadInfo(string ItemCode, string? BranchCode, string? CarrierCardCode);

    /// <summary>Invoices vinculáveis à carga no período (<paramref name="dateTo"/> inclui o dia inteiro).</summary>
    public async Task<List<SapLinkableInvoiceDto>> ListAsync(
        Guid loadKey, DateTime dateFrom, DateTime dateTo, CancellationToken ct = default)
    {
        var from = dateFrom.Date;
        var toExclusive = dateTo.Date.AddDays(1);

        return await QueryAsync(loadKey, from, toExclusive, null, ct);
    }

    /// <summary>Mesmas regras da listagem para um único documento, sem filtro de período.</summary>
    public async Task<SapLinkableInvoiceDto?> FindAsync(
        Guid loadKey, int docEntry, CancellationToken ct = default) =>
        (await QueryAsync(loadKey, null, null, docEntry, ct)).FirstOrDefault();

    /// <summary>
    /// Número da NF (9 dígitos) de uma invoice do SAP, sem regra nenhuma — só para a mensagem de recusa
    /// do vínculo. Nulo quando o documento não existe, não tem número ou o SAP não responde.
    /// </summary>
    public async Task<string?> FindTaxDocumentNumberAsync(int docEntry, CancellationToken ct = default)
    {
        try
        {
            var serial = await sap.SapInvoices.AsNoTracking()
                .Where(i => i.DocEntry == docEntry)
                .Select(i => i.Serial)
                .FirstOrDefaultAsync(ct);
            return serial?.ToString("D9");
        }
        catch (Exception e) when (e is DbException or TimeoutException)
        {
            return null;
        }
    }

    /// <summary>
    /// Filtros que o SQL do SAP consegue aplicar. "Exatamente uma linha" e "item da linha" ficam
    /// traduzíveis (<c>Count()</c> e <c>Any</c>) e só depois vem a projeção.
    /// </summary>
    public static IQueryable<SapInvoiceProjection> BuildSapQuery(
        SapErpDbContext sap, string itemCode, int branchId,
        DateTime? from, DateTime? toExclusive, int? docEntry)
    {
        var query = sap.SapInvoices.AsNoTracking()
            .Where(i => i.Canceled == "N"
                        && i.Model == NfeModel
                        && i.Serial != null
                        && i.BPLId == branchId
                        && i.Transport != null
                        && (i.Transport.Incoterms == "0" || i.Transport.Incoterms == "1"
                            || i.Transport.Incoterms == "2" || i.Transport.Incoterms == "9")
                        && i.Lines.Count() == 1
                        && i.Lines.Any(l => l.ItemCode == itemCode)
                        // GAC-1226 rodada 2: sem teto — a NF maior que o saldo aparece (ajuda a achar a
                        // quantidade de carga digitada errada); quem barra é a regra da soma no vínculo.
                        && i.Lines.Any(l => l.Quantity > 0));

        if (from is { } f) query = query.Where(i => i.DocDate >= f);
        if (toExclusive is { } t) query = query.Where(i => i.DocDate < t);
        if (docEntry is { } d) query = query.Where(i => i.DocEntry == d);

        return query
            .OrderByDescending(i => i.DocDate)
            .ThenByDescending(i => i.DocEntry)
            .Select(i => new SapInvoiceProjection
            {
                DocEntry = i.DocEntry,
                DocNum = i.DocNum,
                DocDate = i.DocDate,
                CardCode = i.CardCode,
                CardName = i.CardName,
                BranchId = i.BPLId,
                Serial = i.Serial!.Value,
                SeriesStr = i.SeriesStr,
                Quantity = i.Lines.Select(l => l.Quantity).FirstOrDefault(),
                Price = i.Lines.Select(l => l.Price).FirstOrDefault(),
                LineTotal = i.Lines.Select(l => l.LineTotal).FirstOrDefault(),
                Cfop = i.Lines.Select(l => l.CfopCode).FirstOrDefault(),
                Carrier = i.Transport!.Carrier,
                Incoterms = i.Transport.Incoterms,
            });
    }

    /// <summary>Linha lida do SAP; classe própria (e não record posicional) para a projeção traduzir.</summary>
    public class SapInvoiceProjection
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
        public string? Carrier { get; set; }
        public string? Incoterms { get; set; }
    }

    /// <summary>
    /// Executa a consulta ao SAP. Existe como costura de teste: permite simular o SAP fora do ar
    /// (<c>DbException</c>/<c>TimeoutException</c>) sem rede, e provar que vira erro de negócio.
    /// </summary>
    protected virtual Task<List<SapInvoiceProjection>> ExecuteSapQueryAsync(
        IQueryable<SapInvoiceProjection> query, CancellationToken ct) =>
        query.ToListAsync(ct);

    private async Task<List<SapLinkableInvoiceDto>> QueryAsync(
        Guid loadKey, DateTime? from, DateTime? toExclusive, int? docEntry, CancellationToken ct)
    {
        var load = await context.ShipmentLoads
            .AsNoTracking()
            .Where(x => x.Key == loadKey)
            .Select(x => new LoadInfo(x.ItemCode, x.BranchCode, x.CarrierCardCode))
            .FirstOrDefaultAsync(ct);

        if (load == null)
            throw new BusinessException("Carga não encontrada.");

        // A filial do Siagro é o BPLId do SAP escrito como texto ("3"); "03" não é a mesma filial.
        if (!int.TryParse(load.BranchCode, out var branchId) || branchId.ToString() != load.BranchCode)
            return [];

        var available = await loadGuard.CalculateAvailableAsync(loadKey);
        if (available <= QuantityTolerance)
            return [];

        List<SapInvoiceProjection> rows;
        try
        {
            rows = await ExecuteSapQueryAsync(
                BuildSapQuery(sap, load.ItemCode, branchId, from, toExclusive, docEntry), ct);
        }
        catch (Exception e) when (e is DbException or TimeoutException)
        {
            throw new BusinessException(
                "Não foi possível consultar as notas fiscais no SAP. Tente novamente em instantes.");
        }

        rows = await ExcludeAlreadyLinkedAsync(rows, ct);

        IReadOnlyDictionary<int, SapFiscalDocumentInfo> infos = new Dictionary<int, SapFiscalDocumentInfo>();
        if (fiscal.IsAvailable && rows.Count > 0)
        {
            try
            {
                infos = await fiscal.GetForInvoicesAsync(
                    rows.Select(r => new SapInvoiceRef(r.DocEntry, r.BranchId)).ToList(), ct);
            }
            catch (Exception e) when (e is DbException or TimeoutException)
            {
                // Banco do add-on fora do ar: erro de negócio (4xx), e não 500 genérico.
                throw new BusinessException(
                    "Não foi possível consultar a situação das notas fiscais no add-on fiscal. Tente novamente em instantes.");
            }

            // Sem informação no add-on = não autorizada.
            rows = rows.Where(r => infos.TryGetValue(r.DocEntry, out var info)
                                   && info.Authorized && !info.Cancelled).ToList();
        }

        return rows
            .Take(MaxRows)
            .Select(r => ToDto(r, load, infos))
            .ToList();
    }

    /// <summary>
    /// Tira as invoices já ligadas a um documento de saída que não esteja Cancelado nem Devolvido.
    /// Dois bancos, sem join: consulta o Siagro em lotes de ids e remove em memória.
    /// </summary>
    private async Task<List<SapInvoiceProjection>> ExcludeAlreadyLinkedAsync(
        List<SapInvoiceProjection> rows, CancellationToken ct)
    {
        var linked = new HashSet<int>();

        foreach (var chunk in rows.Select(r => r.DocEntry).Chunk(ChunkSize))
        {
            var ids = chunk.Select(id => (int?)id).ToList();

            var found = await context.SalesInvoices
                .AsNoTracking()
                .Where(s => s.SapDocEntry != null && ids.Contains(s.SapDocEntry)
                            && s.InvoiceStatus != InvoiceStatus.Cancelled
                            && s.InvoiceStatus != InvoiceStatus.Returned)
                .Select(s => s.SapDocEntry!.Value)
                .ToListAsync(ct);

            linked.UnionWith(found);
        }

        return rows.Where(r => !linked.Contains(r.DocEntry)).ToList();
    }

    private static SapLinkableInvoiceDto ToDto(
        SapInvoiceProjection r, LoadInfo load, IReadOnlyDictionary<int, SapFiscalDocumentInfo> infos)
    {
        var carrier = r.Carrier?.Trim();

        return new SapLinkableInvoiceDto
        {
            DocEntry = r.DocEntry,
            DocNum = r.DocNum,
            DocDate = r.DocDate,
            CardCode = r.CardCode,
            CardName = r.CardName,
            TaxDocumentNumber = r.Serial.ToString("D9"),
            TaxDocumentSeries = (r.SeriesStr ?? string.Empty).Trim().PadLeft(3, '0'),
            Quantity = r.Quantity,
            UnitPrice = r.Price,
            LineTotal = r.LineTotal,
            Cfop = r.Cfop,
            CarrierCode = string.IsNullOrEmpty(carrier) ? null : carrier,
            CarrierMismatch = !string.IsNullOrEmpty(carrier)
                              && !string.Equals(carrier, load.CarrierCardCode?.Trim(), StringComparison.OrdinalIgnoreCase),
            FreightTerms = MapFreightTerms(r.Incoterms),
            AccessKey = infos.TryGetValue(r.DocEntry, out var info) ? info.AccessKey : null,
        };
    }

    /// <summary>INV12.Incoterms é o modFrete da NF-e; a consulta já descartou os outros valores.</summary>
    private static FreightTerms MapFreightTerms(string? incoterms) => incoterms switch
    {
        "0" => FreightTerms.Cif,
        "1" => FreightTerms.Fob,
        "2" => FreightTerms.Ter,
        _ => FreightTerms.None,
    };
}
