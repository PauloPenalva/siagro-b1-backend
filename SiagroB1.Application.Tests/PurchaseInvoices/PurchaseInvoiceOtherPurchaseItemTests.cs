using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.PurchaseFiscalReconciliations;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Spec 2026-10-17 §2: a linha de "outra compra" (natureza sem contrato que gera financeiro) aceita qualquer produto ativo;
/// a linha de contrato segue só o grupo de grãos (105).
/// </summary>
public class PurchaseInvoiceOtherPurchaseItemTests
{
    private const string Service = "SERV-MANUT";
    private const string Inactive = "SERV-INATIVO";

    private sealed record Scenario(UnitOfWork Db, int OtherPurchaseUsage, int ContractUsage);

    private static async Task<Scenario> SeedAsync()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        seed.Db.Context.Items.Add(new Item { ItemCode = Service, ItemName = "MANUTENCAO DE MAQUINAS", ItmsGrpCod = 200, Enabled = "SIM" });
        seed.Db.Context.Items.Add(new Item { ItemCode = Inactive, ItemName = "SERVICO INATIVO", ItmsGrpCod = 200, Enabled = "NAO" });
        await seed.Db.Context.SaveChangesAsync();

        var usages = new UsageService(seed.Db, NullLogger<UsageService>.Instance);
        var other = (await usages.CreateAsync(Usage("Serviço tomado", requiresContract: false))).Code;
        var contract = (await usages.CreateAsync(Usage("Compra de grãos", requiresContract: true))).Code;
        return new Scenario(seed.Db, other, contract);
    }

    private static UsageModel Usage(string name, bool requiresContract) => new()
    {
        Name = name, Direction = UsageDirection.Incoming, CfopIncomingInState = "1102", CfopIncomingOutState = "2102",
        CreatesFinancialDocument = true, RequiresContract = requiresContract,
    };

    private static PurchaseInvoice Invoice() => new()
    {
        Key = Guid.NewGuid(), BranchCode = "01", CardCode = FiscalReconciliationSeed.Supplier, InvoiceType = PurchaseInvoiceType.Normal,
        IssuerType = DocumentIssuerType.ThirdParty, TaxDocumentNumber = "7001", TaxDocumentSeries = "1",
        IssueDate = new DateTime(2026, 10, 12),
    };

    private static PurchaseInvoiceItem Line(string itemCode, int usage) => new()
    {
        Key = Guid.NewGuid(), ItemCode = itemCode, ItemName = "", UnitOfMeasureCode = "UN", Quantity = 1m, UnitPrice = 10m, UsageCode = usage,
    };

    private static async Task<string?> ResolveAsync(Scenario s, PurchaseInvoiceItem line)
    {
        var items = new ItemService(s.Db, NullLogger<ItemService>.Instance, TaxTestServices.Config("STANDALONE"));
        var taxApply = TaxTestServices.PurchaseApply(s.Db, new FakeBusinessPartnerService());
        var other = await taxApply.IsOtherPurchaseLineAsync(Invoice(), line);

        return await PurchaseInvoiceLineGuard.ResolveItemNameAsync(items, line.ItemCode, line.ItemName, otherPurchase: other);
    }

    [Fact]
    public async Task Other_purchase_line_accepts_item_outside_grain_group()
    {
        var s = await SeedAsync();

        Assert.Equal("MANUTENCAO DE MAQUINAS", await ResolveAsync(s, Line(Service, s.OtherPurchaseUsage)));
    }

    [Fact]
    public async Task Contract_line_keeps_grain_group_only()
    {
        var s = await SeedAsync();

        Assert.Null(await ResolveAsync(s, Line(Service, s.ContractUsage)));
    }

    [Fact]
    public async Task Other_purchase_line_does_not_accept_inactive_item()
    {
        var s = await SeedAsync();

        Assert.Null(await ResolveAsync(s, Line(Inactive, s.OtherPurchaseUsage)));
    }

    [Fact]
    public async Task Other_purchase_line_in_sapb1_keeps_grain_group_only()
    {
        var s = await SeedAsync();
        var items = new ItemService(s.Db, NullLogger<ItemService>.Instance, TaxTestServices.Config("SAPB1"));
        var line = Line(Service, s.OtherPurchaseUsage);
        var taxApply = TaxTestServices.PurchaseApply(s.Db, new FakeBusinessPartnerService(), "SAPB1");

        var other = await taxApply.IsOtherPurchaseLineAsync(Invoice(), line);

        Assert.False(other);
        Assert.Null(await PurchaseInvoiceLineGuard.ResolveItemNameAsync(items, line.ItemCode, line.ItemName, otherPurchase: other));
    }

    [Fact]
    public async Task Create_resolves_the_description_of_an_other_purchase_line()
    {
        var s = await SeedAsync();
        var items = new ItemService(s.Db, NullLogger<ItemService>.Instance, TaxTestServices.Config("STANDALONE"));
        var create = new PurchaseInvoicesCreateService(s.Db, new FakeBusinessPartnerService(), items,
            TaxTestServices.PurchaseApply(s.Db, new FakeBusinessPartnerService()), new FakeDocNumberSequenceService());
        var invoice = Invoice();
        invoice.AddItem(Line(Service, s.OtherPurchaseUsage));

        await create.ExecuteAsync(invoice, "ana");

        Assert.Equal("MANUTENCAO DE MAQUINAS", invoice.Items.Single().ItemName);
    }

    [Fact]
    public async Task Active_item_queries_return_every_group_and_skip_inactive()
    {
        var s = await SeedAsync();
        var items = new ItemService(s.Db, NullLogger<ItemService>.Instance, TaxTestServices.Config("STANDALONE"));

        Assert.Contains(items.QueryAllActive().ToList(), x => x.ItemCode == Service);
        Assert.DoesNotContain(items.QueryAllActive().ToList(), x => x.ItemCode == Inactive);
        Assert.DoesNotContain(items.QueryAll().ToList(), x => x.ItemCode == Service);
        Assert.Null(await items.GetByIdAsync(Service));
        Assert.NotNull(await items.GetActiveByIdAsync(Service));
    }
}
