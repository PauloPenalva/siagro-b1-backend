using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Relatório de contratos de compra por produto e período (conferência diária dos negócios).
/// A consulta e toda a formatação de texto ficam aqui; o .frx só desenha.
/// </summary>
public class PurchaseContractsByItemReportService(
    IUnitOfWork db,
    IFastReportService reportService)
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<List<PurchaseContractsByItemRowDto>> BuildRowsAsync(
        PurchaseContractsByItemRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);

        // Branch é tabela local (BRANCHS) e a FK é opcional -> LEFT JOIN, seguro em modo SAPB1.
        var query = db.Context.PurchaseContracts
            .AsNoTracking()
            .Include(x => x.Brokers)
            .Include(x => x.Branch)
            .Where(x => x.Status != ContractStatus.Canceled)
            .Where(x => x.CreationDate >= from && x.CreationDate < toExclusive);

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(x => x.ItemCode == request.ItemCode);

        if (!string.IsNullOrWhiteSpace(request.HarvestSeasonCode))
            query = query.Where(x => x.HarvestSeasonCode == request.HarvestSeasonCode);

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(x => x.BranchCode == request.BranchCode);

        if (!string.IsNullOrWhiteSpace(request.DeliveryLocationCode))
            query = query.Where(x => x.DeliveryLocationCode == request.DeliveryLocationCode);

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

    public async Task<byte[]> ExecuteAsync(PurchaseContractsByItemRequest request)
    {
        var rows = await BuildRowsAsync(request);

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = BuildFiltersDescription(request, rows)
        };

        return await reportService.GeneratePdfAsync(
            "PurchaseContractsByItem.frx",
            rows,
            "PurchaseContractsByItem",
            "PurchaseContractsByItem",
            parameters);
    }

    /// <summary>
    /// Linha de filtros impressa no cabeçalho. As descrições saem das próprias linhas do
    /// resultado — resolver código -> nome consultando ITEMS/WAREHOUSES/BUSINESS_PARTNERS
    /// devolveria vazio em modo SAPB1. Sem resultado, imprime só o código.
    /// </summary>
    public static string BuildFiltersDescription(
        PurchaseContractsByItemRequest request,
        IReadOnlyList<PurchaseContractsByItemRowDto> rows)
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

        if (!string.IsNullOrWhiteSpace(request.DeliveryLocationCode))
            parts.Add($"Local de entrega: {Describe(first?.DeliveryLocation, request.DeliveryLocationCode)}");

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            parts.Add($"Fornecedor: {Describe(first?.Supplier, request.CardCode)}");

        if (request.DeliveryFromDate is { } deliveryFrom && request.DeliveryToDate is { } deliveryTo)
            parts.Add($"Entrega: {Date(deliveryFrom)} a {Date(deliveryTo)}");
        else if (request.DeliveryFromDate is { } onlyFrom)
            parts.Add($"Entrega a partir de: {Date(onlyFrom)}");
        else if (request.DeliveryToDate is { } onlyTo)
            parts.Add($"Entrega até: {Date(onlyTo)}");

        return string.Join(" | ", parts);
    }

    private static PurchaseContractsByItemRowDto ToRow(PurchaseContract contract) => new()
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
        DeliveryLocation = NameOrCode(contract.DeliveryLocationName, contract.DeliveryLocationCode),
        Supplier = string.IsNullOrWhiteSpace(contract.CardName) ? contract.CardCode : contract.CardName,
        Quantity = contract.TotalVolume,
        UnitOfMeasure = contract.UnitOfMeasureCode,
        Price = contract.StandardPrice,
        Funrural = DescribeFunrural(contract.FunruralType),
        PaymentForecast = contract.StandardCashFlowDate is { } forecast ? Date(forecast) : "",
        Commission = DescribeCommission(contract.Brokers),
        Freight = DescribeFreight(contract.FreightTerms, contract.FreightCostStandard),
        Buyer = contract.AgentName ?? "",
    };

    private static string DescribeCommission(IEnumerable<PurchaseContractBroker> brokers) =>
        string.Join("; ", brokers.Select(broker =>
        {
            var name = string.IsNullOrWhiteSpace(broker.CardName) ? broker.CardCode : broker.CardName;
            var unit = string.IsNullOrWhiteSpace(broker.ComissionUmCode) ? "" : $" {broker.ComissionUmCode}";
            return $"{name} - {broker.Commission.ToString("N2", Culture)}{unit}";
        }));

    private static string DescribeFreight(FreightTerms terms, decimal cost) => terms switch
    {
        FreightTerms.Cif => $"CIF - {cost.ToString("N2", Culture)}",
        FreightTerms.Fob => $"FOB - {cost.ToString("N2", Culture)}",
        _ => "Sem frete",
    };

    private static string DescribeFunrural(FunruralType? type) => type switch
    {
        FunruralType.Livre => "Livre",
        FunruralType.Bruto => "Bruto",
        _ => "",
    };

    /// <summary>Imprime o nome; sem nome cadastrado, resta o código.</summary>
    private static string NameOrCode(string? name, string? code) =>
        string.IsNullOrWhiteSpace(name) ? code ?? "" : name;

    private static string Describe(string? description, string? fallback) =>
        string.IsNullOrWhiteSpace(description) ? fallback ?? "" : description;

    private static string Date(DateTime value) => value.ToString("dd/MM/yyyy", Culture);
}
