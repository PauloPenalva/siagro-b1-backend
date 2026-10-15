using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using static SiagroB1.Application.Tests.Support.ValueComplementNfeSeed;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Spec 2026-10-14, "Serviços e API": itens que o complemento próprio pode apontar (regras 2 e 3), com ou sem contrato (D5).</summary>
public class PurchaseInvoicesGetComplementCandidatesTests
{
    private static PurchaseInvoicesGetComplementCandidatesService Service(ValueComplementScenario s) => new(s.Db);

    [Fact]
    public async Task Authorized_own_entry_items_with_contract_are_offered()
    {
        var s = await SeedAsync();

        var row = Assert.Single(await Service(s).ExecuteAsync(PurchaseNfeTestSeed.Supplier, "01"));

        Assert.Equal(s.OriginalItemKey, row.ItemKey);
        Assert.Equal(s.OriginalKey, row.InvoiceKey);
        Assert.Equal(OriginalNumber, row.InvoiceNumber);
        Assert.Equal(new DateTime(2026, 10, 1), row.InvoiceDate);
        Assert.Equal(AccessKey(5), row.AccessKey);
        Assert.Equal(1, row.LineNumber);
        Assert.Equal("TRIGO", row.ItemCode);
        Assert.Equal("TRIGO EM GRAOS", row.ItemName);
        Assert.Equal("KG", row.UnitOfMeasure);
        Assert.Equal(1000d, row.Quantity);
        Assert.Equal(1.5d, row.UnitPrice);
        Assert.Equal(1500d, row.Total);
        Assert.Equal(s.PurchaseUsage, row.UsageCode);
        Assert.Equal("COMPRA DE MERCADORIA", row.UsageName);
        Assert.Equal(s.Contract.Key, row.PurchaseContractKey);
        Assert.Equal("PC-TRIGO", row.PurchaseContractCode);
    }

    [Fact]
    public async Task Ineligible_documents_and_items_are_not_offered()
    {
        var s = await SeedAsync();
        // O cenário já tem a entrada própria PENDENTE do PurchaseNfeTestSeed (F-SP): não aparece.
        foreach (var (number, change) in new (int, Action<SiagroB1.Domain.Entities.PurchaseInvoice>)[]
                 {
                     (10, i => i.IssuerType = DocumentIssuerType.ThirdParty),
                     (11, i => i.NfeStatus = NfeStatus.Cancelled),
                     (12, i => i.InvoiceStatus = InvoiceStatus.Cancelled),
                     (13, i => i.CardCode = "F-OUTRO"),
                     (14, i => i.InvoiceType = PurchaseInvoiceType.ValueComplement),
                 })
        {
            var (key, _) = await AddOriginalAsync(s.Db, s.Contract, s.PurchaseUsage, $"DE0000{number}", number);
            await ChangeAsync(s.Db, key, change);
        }

        var rows = await Service(s).ExecuteAsync(PurchaseNfeTestSeed.Supplier, "01");

        Assert.Equal([OriginalNumber], rows.Select(r => r.InvoiceNumber).ToArray());
        Assert.Empty(await Service(s).ExecuteAsync(PurchaseNfeTestSeed.Supplier, "02"));
        Assert.Empty(await Service(s).ExecuteAsync("", "01"));
        Assert.Empty(await Service(s).ExecuteAsync(PurchaseNfeTestSeed.Supplier, null));
    }

    [Fact]
    public async Task Items_without_contract_are_offered_with_an_empty_contract()
    {
        var s = await SeedAsync();
        await ChangeAsync(s.Db, s.OriginalKey, o => o.Items.Single().PurchaseContractKey = null);

        var row = Assert.Single(await Service(s).ExecuteAsync(PurchaseNfeTestSeed.Supplier, "01"));

        Assert.Equal(s.OriginalItemKey, row.ItemKey);
        Assert.Null(row.PurchaseContractKey);
        Assert.Null(row.PurchaseContractCode);
    }

    [Fact]
    public async Task Counter_note_is_offered()
    {
        var s = await SeedAsync();
        await ChangeAsync(s.Db, s.OriginalKey, o => o.ProducerInvoiceKey = s.Purchase.InvoiceKey);

        Assert.Single(await Service(s).ExecuteAsync(PurchaseNfeTestSeed.Supplier, "01"));
    }

    [Fact]
    public async Task Items_come_newest_note_first_in_item_order()
    {
        var s = await SeedAsync();
        var (newer, items) = await AddOriginalAsync(s.Db, s.Contract, s.PurchaseUsage, SecondNumber, 7, items: 2);
        await ChangeAsync(s.Db, newer, i => i.IssueDate = new DateTime(2026, 10, 2));

        var rows = await Service(s).ExecuteAsync(PurchaseNfeTestSeed.Supplier, "01");

        Assert.Equal([items[0], items[1], s.OriginalItemKey], rows.Select(r => r.ItemKey).ToArray());
        Assert.Equal([1, 2, 1], rows.Select(r => r.LineNumber ?? 0).ToArray());
    }
}
