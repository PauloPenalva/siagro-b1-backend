using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Entrada de TERCEIRO confirmada do fornecedor F-SP (SP → filial SP), com a chave e, por padrão, o XML guardado:
/// TRIGO no item 1 e MILHO no item 2, ICMS 51 18% diferido 100% com cBenef SP053521 — a mesma tributação que a
/// natureza de devolução do <see cref="PurchaseNfeTestSeed"/> produz dentro do estado (a conferência passa).
/// </summary>
public static class ThirdPartyPurchaseSeed
{
    public static async Task<(PurchaseNfeScenario Scenario, PurchaseInvoice Origin)> SeedAsync(
        bool withXml = true, bool configureUsage = true, string? icms = null)
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var context = scenario.Db.Context;

        if (configureUsage)
        {
            var branch = await context.Branchs.SingleAsync(b => b.Code == "01");
            branch.ThirdPartyPurchaseReturnUsageCode = scenario.ReturnUsage;
        }

        var xml = SupplierNfeXml.Build(
            SupplierNfeXml.Det(1, "TRIGO", "TRIGO EM GRAOS", 1000m, 1.5m, icms ?? SupplierNfeXml.Icms51(1500m)),
            SupplierNfeXml.Det(2, "MILHO", "MILHO EM GRAOS", 500m, 1m, SupplierNfeXml.Icms51(500m)));

        var origin = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = PurchaseNfeTestSeed.Supplier, CardName = "PRODUTOR RURAL TESTE",
            IssuerType = DocumentIssuerType.ThirdParty, InvoiceType = PurchaseInvoiceType.Normal, InvoiceStatus = InvoiceStatus.Confirmed,
            IssueDate = new DateTime(2026, 10, 1), PostingDate = new DateTime(2026, 10, 2), ChaveNFe = SupplierNfeXml.AccessKey,
            TaxDocumentNumber = "456", TaxDocumentSeries = "1", GrossWeight = 1500m, NetWeight = 1500m, FreightTerms = FreightTerms.None,
            XmlData = withXml ? SupplierNfeXml.Bytes(xml) : null, XmlFileName = withXml ? "nota.xml" : null,
        };
        origin.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", ItemName = "TRIGO EM GRAOS", UnitOfMeasureCode = "KG", Quantity = 1000m,
            UnitPrice = 1.5m, NfeItemNumber = withXml ? 1 : null,
        });
        origin.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "MILHO", ItemName = "MILHO EM GRAOS", UnitOfMeasureCode = "KG", Quantity = 500m,
            UnitPrice = 1m, NfeItemNumber = withXml ? 2 : null,
        });
        PurchaseInvoiceSupplierTaxes.Apply(origin, origin.Items);

        context.PurchaseInvoices.Add(origin);
        await scenario.Db.SaveChangesAsync();
        context.ChangeTracker.Clear();

        return (scenario, await context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == origin.Key));
    }
}
