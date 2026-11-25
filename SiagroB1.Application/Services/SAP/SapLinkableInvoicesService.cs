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

    private sealed record LoadInfo(
        string ItemCode, string? BranchCode, string? CardCode, string? CarrierCardCode, string? TruckCode);

    /// <summary>
    /// Placa comparável: maiúsculas, sem hífen e sem espaço ("abc-1d23" e "ABC 1D23" viram "ABC1D23");
    /// vazia vira "". A coluna do SAP recebe a mesma normalização no SQL.
    /// </summary>
    public static string NormalizePlate(string? plate) =>
        (plate ?? string.Empty).Replace("-", "").Replace(" ", "").ToUpperInvariant();

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
            return serial is { } number ? SapTaxDocumentFormat.Number(number) : null;
        }
        catch (Exception e) when (e is DbException or TimeoutException)
        {
            return null;
        }
    }

    /// <summary>
    /// Filtros que o SQL do SAP consegue aplicar. "Exatamente uma linha" e "item da linha" ficam
    /// traduzíveis (<c>Count()</c> e <c>Any</c>) e só depois vem a projeção. <paramref name="plate"/>
    /// já vem normalizada por <see cref="NormalizePlate"/>.
    /// </summary>
    public static IQueryable<SapInvoiceProjection> BuildSapQuery(
        SapErpDbContext sap, string itemCode, int branchId, string cardCode, string carrierCode, string plate,
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
                        && i.Lines.Any(l => l.Quantity > 0)
                        // GAC-1226 rodada 3: só NF do cliente, da transportadora e da placa da carga, em reais
                        // e em KG (a linha única é a de KG). A finalidade (finNFe = 1) vem do add-on, depois.
                        && i.CardCode == cardCode
                        && i.Transport.Carrier == carrierCode
                        && i.Transport.Vehicle != null
                        && i.Transport.Vehicle.Replace("-", "").Replace(" ", "").ToUpper() == plate
                        && i.DocCur == "R$"
                        && i.Lines.Any(l => l.UnitMsr == "KG"));

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
                UsageCode = i.Lines.Select(l => l.Usage).FirstOrDefault(),
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
        public int? UsageCode { get; set; }
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

    /// <summary>Nomes das naturezas (OUSG) das linhas lidas. Consulta separada: a projeção fica simples.</summary>
    private async Task<Dictionary<int, string>> LoadUsageNamesAsync(
        List<SapInvoiceProjection> rows, CancellationToken ct)
    {
        var codes = rows.Where(r => r.UsageCode.HasValue).Select(r => r.UsageCode!.Value).Distinct().ToList();
        if (codes.Count == 0)
            return [];

        return await sap.Usages.AsNoTracking()
            .Where(u => codes.Contains(u.Code))
            .ToDictionaryAsync(u => u.Code, u => u.Name, ct);
    }

    private async Task<List<SapLinkableInvoiceDto>> QueryAsync(
        Guid loadKey, DateTime? from, DateTime? toExclusive, int? docEntry, CancellationToken ct)
    {
        var load = await context.ShipmentLoads
            .AsNoTracking()
            .Where(x => x.Key == loadKey)
            .Select(x => new LoadInfo(x.ItemCode, x.BranchCode, x.CardCode, x.CarrierCardCode, x.TruckCode))
            .FirstOrDefaultAsync(ct);

        if (load == null)
            throw new BusinessException("Carga não encontrada.");

        // Sem cliente, transportadora ou placa não há o que casar com a NF: avisa o que falta na carga.
        if (string.IsNullOrWhiteSpace(load.CardCode))
            throw new BusinessException("Informe o cliente da carga para vincular a NF do SAP.");
        if (string.IsNullOrWhiteSpace(load.CarrierCardCode))
            throw new BusinessException("Informe a transportadora da carga para vincular a NF do SAP.");
        var plate = NormalizePlate(load.TruckCode);
        if (plate.Length == 0)
            throw new BusinessException("Informe a placa da carga para vincular a NF do SAP.");

        // A filial do Siagro é o BPLId do SAP escrito como texto ("3"); "03" não é a mesma filial.
        if (!int.TryParse(load.BranchCode, out var branchId) || branchId.ToString() != load.BranchCode)
            return [];

        var available = await loadGuard.CalculateAvailableAsync(loadKey);
        if (available <= QuantityTolerance)
            return [];

        List<SapInvoiceProjection> rows;
        Dictionary<int, string> usageNames;
        try
        {
            rows = await ExecuteSapQueryAsync(
                BuildSapQuery(sap, load.ItemCode, branchId, load.CardCode.Trim(), load.CarrierCardCode.Trim(),
                    plate, from, toExclusive, docEntry), ct);
            usageNames = await LoadUsageNamesAsync(rows, ct);
        }
        catch (Exception e) when (e is DbException or TimeoutException)
        {
            throw new BusinessException(
                "Não foi possível consultar as notas fiscais no SAP. Tente novamente em instantes.");
        }

        rows = await ExcludeAlreadyLinkedAsync(rows, ct);

        IReadOnlyDictionary<int, SapFiscalDocumentInfo> infos = new Dictionary<int, SapFiscalDocumentInfo>();
        IReadOnlyDictionary<int, string> purposes = new Dictionary<int, string>();
        if (fiscal.IsAvailable && rows.Count > 0)
        {
            try
            {
                var refs = rows.Select(r => new SapInvoiceRef(r.DocEntry, r.BranchId)).ToList();
                infos = await fiscal.GetForInvoicesAsync(refs, ct);

                // Só a NF-e normal (finNFe = 1); complemento, ajuste e devolução ficam de fora, e a que não
                // tem XML legível (ausente do dicionário) também. Lê só as já autorizadas.
                var authorized = refs.Where(r => infos.TryGetValue(r.DocEntry, out var info)
                                                 && info.Authorized && !info.Cancelled).ToList();
                purposes = authorized.Count == 0
                    ? new Dictionary<int, string>()
                    : await fiscal.GetInvoicePurposesAsync(authorized, ct);
            }
            catch (Exception e) when (e is DbException or TimeoutException)
            {
                // Banco do add-on fora do ar: erro de negócio (4xx), e não 500 genérico.
                throw new BusinessException(
                    "Não foi possível consultar a situação das notas fiscais no add-on fiscal. Tente novamente em instantes.");
            }

            // Sem informação no add-on = não autorizada.
            rows = rows.Where(r => infos.TryGetValue(r.DocEntry, out var info)
                                   && info.Authorized && !info.Cancelled
                                   && purposes.TryGetValue(r.DocEntry, out var purpose) && purpose == "1").ToList();
        }

        return rows
            .Take(MaxRows)
            .Select(r => ToDto(r, infos, usageNames))
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
        SapInvoiceProjection r, IReadOnlyDictionary<int, SapFiscalDocumentInfo> infos,
        IReadOnlyDictionary<int, string> usageNames)
    {
        var carrier = r.Carrier?.Trim();

        return new SapLinkableInvoiceDto
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
            CarrierCode = string.IsNullOrEmpty(carrier) ? null : carrier,
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
