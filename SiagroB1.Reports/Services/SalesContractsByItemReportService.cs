using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Relatório de contratos de venda por produto e período — espelho do de compra
/// (<see cref="PurchaseContractsByItemReportService"/>). As diferenças estão no
/// contrato de venda, não na mecânica: não há Funrural nem corretores, o frete só
/// tem tipo, e o local de entrega (1:N) dá lugar à região logística.
/// </summary>
public class SalesContractsByItemReportService(
    IUnitOfWork db,
    IFastReportService reportService)
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<List<SalesContractsByItemRowDto>> BuildRowsAsync(
        SalesContractsByItemRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);

        // Branch e LogisticRegion são tabelas locais com FK opcional -> LEFT JOIN,
        // seguro em modo SAPB1.
        var query = db.Context.SalesContracts
            .AsNoTracking()
            .Include(x => x.Branch)
            .Include(x => x.LogisticRegion)
            .Where(x => x.Status != ContractStatus.Canceled)
            .Where(x => x.CreationDate >= from && x.CreationDate < toExclusive);

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(x => x.ItemCode == request.ItemCode);

        if (!string.IsNullOrWhiteSpace(request.HarvestSeasonCode))
            query = query.Where(x => x.HarvestSeasonCode == request.HarvestSeasonCode);

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(x => x.BranchCode == request.BranchCode);

        if (!string.IsNullOrWhiteSpace(request.LogisticRegionCode))
            query = query.Where(x => x.LogisticRegionCode == request.LogisticRegionCode);

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(x => x.CardCode == request.CardCode);

        // Período de entrega por SOBREPOSIÇÃO: a janela do contrato precisa cruzar a janela pedida.
        if (request.DeliveryFromDate is { } deliveryFrom)
        {
            var deliveryFromDate = deliveryFrom.Date;
            query = query.Where(x => x.DeliveryEndDate >= deliveryFromDate);
        }

        if (request.DeliveryToDate is { } deliveryTo)
        {
            var deliveryToExclusive = deliveryTo.Date.AddDays(1);
            query = query.Where(x => x.DeliveryStartDate < deliveryToExclusive);
        }

        var contracts = await query.ToListAsync();

        return contracts
            .OrderBy(x => x.ItemName ?? "", StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.CreationDate)
            .ThenBy(x => x.Code ?? "", StringComparer.Ordinal)
            .Select(ToRow)
            .ToList();
    }

    public async Task<byte[]> ExecuteAsync(SalesContractsByItemRequest request)
    {
        var rows = await BuildRowsAsync(request);

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = BuildFiltersDescription(request, rows)
        };

        return await reportService.GeneratePdfAsync(
            "SalesContractsByItem.frx",
            rows,
            "SalesContractsByItem",
            "SalesContractsByItem",
            parameters);
    }

    /// <summary>
    /// Linha de filtros impressa no cabeçalho. As descrições saem das próprias linhas do
    /// resultado — resolver código -> nome consultando ITEMS/BUSINESS_PARTNERS devolveria
    /// vazio em modo SAPB1. Sem resultado, imprime só o código.
    /// </summary>
    public static string BuildFiltersDescription(
        SalesContractsByItemRequest request,
        IReadOnlyList<SalesContractsByItemRowDto> rows)
    {
        var first = rows.Count > 0 ? rows[0] : null;

        var parts = new List<string>
        {
            $"Emissão: {Date(request.FromDate)} a {Date(request.ToDate)}"
        };

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            parts.Add($"Produto: {Describe(first?.Product, request.ItemCode)}");

        if (!string.IsNullOrWhiteSpace(request.HarvestSeasonCode))
            parts.Add($"Safra: {request.HarvestSeasonCode}");

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            parts.Add($"Filial: {Describe(first?.Branch, request.BranchCode)}");

        if (!string.IsNullOrWhiteSpace(request.LogisticRegionCode))
            parts.Add($"Região logística: {Describe(first?.LogisticRegion, request.LogisticRegionCode)}");

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            parts.Add($"Cliente: {Describe(first?.Customer, request.CardCode)}");

        if (request.DeliveryFromDate is { } deliveryFrom && request.DeliveryToDate is { } deliveryTo)
            parts.Add($"Entrega: {Date(deliveryFrom)} a {Date(deliveryTo)}");
        else if (request.DeliveryFromDate is { } onlyFrom)
            parts.Add($"Entrega a partir de: {Date(onlyFrom)}");
        else if (request.DeliveryToDate is { } onlyTo)
            parts.Add($"Entrega até: {Date(onlyTo)}");

        return string.Join(" | ", parts);
    }

    private static SalesContractsByItemRowDto ToRow(SalesContract contract) => new()
    {
        ItemCode = contract.ItemCode,
        ItemName = contract.ItemName ?? "",
        Product = string.IsNullOrWhiteSpace(contract.ItemName)
            ? contract.ItemCode
            : $"{contract.ItemName} ({contract.ItemCode})",
        ContractCode = contract.Code ?? "",
        Branch = NameOrCode(
            contract.Branch?.ShortName ?? contract.Branch?.BranchName,
            contract.BranchCode),
        LogisticRegion = NameOrCode(contract.LogisticRegion?.Name, contract.LogisticRegionCode),
        Customer = string.IsNullOrWhiteSpace(contract.CardName) ? contract.CardCode : contract.CardName,
        Quantity = contract.TotalVolume,
        UnitOfMeasure = contract.UnitOfMeasureCode,
        Price = contract.Price,
        Market = DescribeMarket(contract.MarketType),
        PaymentForecast = contract.StandardCashFlowDate is { } forecast ? Date(forecast) : "",
        Freight = DescribeFreight(contract.FreightTerms),
        Seller = contract.AgentName ?? "",
    };

    /// <remarks>Sem valor: o contrato de venda não tem campo de custo de frete.</remarks>
    private static string DescribeFreight(FreightTerms terms) => terms switch
    {
        FreightTerms.Cif => "CIF",
        FreightTerms.Fob => "FOB",
        _ => "Sem frete",
    };

    private static string DescribeMarket(MarketType? market) => market switch
    {
        MarketType.Internal => "Interno",
        MarketType.External => "Exportação",
        _ => "",
    };

    /// <summary>Imprime o nome; sem nome cadastrado, resta o código.</summary>
    private static string NameOrCode(string? name, string? code) =>
        string.IsNullOrWhiteSpace(name) ? code ?? "" : name;

    private static string Describe(string? description, string? fallback) =>
        string.IsNullOrWhiteSpace(description) ? fallback ?? "" : description;

    private static string Date(DateTime value) => value.ToString("dd/MM/yyyy", Culture);
}
