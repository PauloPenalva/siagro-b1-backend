using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Support;

public sealed record SalesInvoiceDivergenceScenario(Guid LoadKey, Guid InvoiceKey, Guid DivergenceKey);

/// <summary>
/// Carga de 30.000 kg com um documento vinculado a uma NF do SAP de 29.800 kg e o registro de divergência de 200 kg
/// (GAC-1226), no estado que o vínculo divergente deixa: carga Faturada, saldo zero. O saldo da carga é gravado pelo
/// escritor real (<see cref="ShipmentLoadsRecalculateInvoicedService"/>), não à mão.
/// </summary>
public static class SalesInvoiceDivergenceTestSeed
{
    public const decimal LoadQuantity = 30_000m;
    public const decimal InvoiceQuantity = 29_800m;
    public const decimal QuantityDifference = 200m;

    public static async Task<SalesInvoiceDivergenceScenario> SeedAsync(
        AppDbContext context, InvoiceStatus status = InvoiceStatus.Confirmed, int sapDocEntry = 1)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000010",
            BranchCode = "3",
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            CardCode = "C0001",
            TotalQuantity = LoadQuantity,
            Status = ShipmentLoadStatus.Open,
        };

        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C0001",
            BranchCode = "3",
            InvoiceNumber = $"DS{sapDocEntry:D6}",
            InvoiceStatus = status,
            InvoiceType = SalesInvoiceType.Normal,
            ShipmentLoadKey = load.Key,
            SapDocEntry = sapDocEntry,
            SapDocNum = sapDocEntry + 1000,
            SapLinkType = SapLinkType.QuantityAndValueDivergent,
        };

        var item = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = invoice.Key,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = InvoiceQuantity,
            UnitPrice = 2m,
        };

        var divergence = new SalesInvoiceDivergence
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = invoice.Key,
            ShipmentLoadKey = load.Key,
            LinkType = SapLinkType.QuantityAndValueDivergent,
            EffectiveQuantity = LoadQuantity,
            InvoiceQuantity = InvoiceQuantity,
            QuantityDifference = QuantityDifference,
            EffectiveValue = 60_000m,
            InvoiceValue = 59_600m,
            ValueDifference = 400m,
            Status = SalesInvoiceDivergenceStatus.Open,
            CreatedAt = DateTime.Now,
            CreatedBy = "tester",
        };

        context.ShipmentLoads.Add(load);
        context.SalesInvoices.Add(invoice);
        context.SalesInvoicesItems.Add(item);
        context.SalesInvoiceDivergences.Add(divergence);
        await context.SaveChangesAsync();

        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(context, load.Key, null);
        await context.SaveChangesAsync();

        return new SalesInvoiceDivergenceScenario(load.Key, invoice.Key, divergence.Key);
    }
}
