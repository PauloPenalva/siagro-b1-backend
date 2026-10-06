using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Tributos da entrada própria e da devolução de compra pela natureza (spec §6.2).</summary>
public class PurchaseInvoicesTaxApplyTests
{
    private const string Supplier = "F-SP";

    private sealed record Seed(UnitOfWork Db, int PurchaseUsage, int ReturnUsage, FakeBusinessPartnerService Partners);

    private static async Task<Seed> SeedAsync(string supplierState = "SP", bool issuesNfe = true)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "CEAGUI", ShortName = "CEAGUI", TaxId = "12345678000195", StateCode = "SP",
            TaxRegime = TaxRegime.Normal, IssuesNfe = issuesNfe,
        });
        db.Context.Items.Add(new Item { ItemCode = "TRIGO", ItemName = "TRIGO EM GRAOS", Ncm = "10019900", GoodsOrigin = 0 });
        var returnUsage = new Usage
        {
            Name = "DEVOLUCAO DE COMPRA", Direction = UsageDirection.Outgoing, CfopOutgoingInState = "5202",
            CfopOutgoingOutState = "6202", IcmsInStateCst = "51", IcmsInStateRate = 18m, IcmsInStateDeferral = 100m,
            IcmsInStateBenefitCode = "SP053521", IcmsOutStateCst = "00", PisCst = "49", CofinsCst = "49",
        };
        db.Context.Usages.Add(returnUsage);
        await db.SaveChangesAsync();
        var purchaseUsage = new Usage
        {
            Name = "COMPRA DE MERCADORIA", Direction = UsageDirection.Incoming, CfopIncomingInState = "1102",
            CfopIncomingOutState = "2102", IcmsInStateCst = "51", IcmsInStateRate = 18m, IcmsInStateDeferral = 100m,
            IcmsInStateBenefitCode = "SP053521", IcmsOutStateCst = "00", PisCst = "74", CofinsCst = "74",
            ReturnUsageCode = returnUsage.Code,
        };
        db.Context.Usages.Add(purchaseUsage);
        await db.SaveChangesAsync();

        var partners = new FakeBusinessPartnerService(
            names: new Dictionary<string, string> { [Supplier] = "PRODUTOR TESTE" },
            states: new Dictionary<string, string> { [Supplier] = supplierState },
            paymentConditions: new Dictionary<string, int> { [Supplier] = 7 });

        return new Seed(db, purchaseUsage.Code, returnUsage.Code, partners);
    }

    private static PurchaseInvoice OwnEntry(int? usageCode, string? itemCode = "TRIGO")
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = Supplier, IssuerType = DocumentIssuerType.Own,
            InvoiceType = PurchaseInvoiceType.Normal, IssueDate = new DateTime(2026, 10, 5),
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = itemCode, UnitOfMeasureCode = "KG", Quantity = 1000m, UnitPrice = 1.5m,
            UsageCode = usageCode,
        });
        return invoice;
    }

    private static PurchaseInvoicesCreateService Create(Seed seed, string erp = "STANDALONE") =>
        new(seed.Db, seed.Partners, new FakeItemService(names: new Dictionary<string, string> { ["TRIGO"] = "TRIGO EM GRAOS" }),
            TaxTestServices.PurchaseApply(seed.Db, seed.Partners, erp));

    [Fact]
    public async Task Own_entry_is_calculated_with_the_incoming_usage()
    {
        var seed = await SeedAsync();
        var invoice = OwnEntry(seed.PurchaseUsage);

        await Create(seed).ExecuteAsync(invoice, "tester");

        var line = await seed.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync();
        Assert.Equal("1102", line.Cfop);
        Assert.Equal("10019900", line.Ncm);
        Assert.Equal("51", line.CstIcms);
        Assert.Equal(1500m, line.IcmsBase);
        Assert.Equal(270m, line.IcmsOperationValue);
        Assert.Equal(0m, line.IcmsValue);
        Assert.Equal("SP053521", line.IcmsBenefitCode);
        Assert.Equal("74", line.CstPis);
        Assert.Equal("COMPRA DE MERCADORIA", line.UsageName);
    }

    [Fact]
    public async Task Own_entry_from_another_state_uses_the_out_of_state_cfop()
    {
        var seed = await SeedAsync(supplierState: "PR");

        await Create(seed).ExecuteAsync(OwnEntry(seed.PurchaseUsage), "tester");

        var line = await seed.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync();
        Assert.Equal("2102", line.Cfop);
        Assert.Equal(12m, line.IcmsRate); // PR → SP
    }

    [Fact]
    public async Task Own_entry_takes_the_supplier_payment_condition()
    {
        var seed = await SeedAsync();
        var invoice = OwnEntry(seed.PurchaseUsage);

        await Create(seed).ExecuteAsync(invoice, "tester");

        Assert.Equal(7, invoice.PaymentConditionCode);
    }

    [Fact]
    public async Task Own_entry_line_without_usage_is_refused()
    {
        var seed = await SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Create(seed).ExecuteAsync(OwnEntry(null), "tester"));

        Assert.Equal("O item TRIGO está sem natureza de operação.", e.Message);
    }

    [Fact]
    public async Task Own_entry_line_without_product_is_refused()
    {
        var seed = await SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(
            () => Create(seed).ExecuteAsync(OwnEntry(seed.PurchaseUsage, itemCode: null), "tester"));

        Assert.Equal("Informe o produto do item 1.", e.Message);
    }

    [Fact]
    public async Task Outgoing_usage_is_refused_in_the_own_entry()
    {
        var seed = await SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Create(seed).ExecuteAsync(OwnEntry(seed.ReturnUsage), "tester"));

        Assert.Equal("A natureza de operação DEVOLUCAO DE COMPRA é de saída e não pode ser usada na entrada.", e.Message);
    }

    [Fact]
    public async Task Supplier_without_state_is_refused_at_save()
    {
        var seed = await SeedAsync();
        var partners = new FakeBusinessPartnerService(names: new Dictionary<string, string> { [Supplier] = "PRODUTOR TESTE" });
        var create = new PurchaseInvoicesCreateService(seed.Db, partners, new FakeItemService(),
            TaxTestServices.PurchaseApply(seed.Db, partners));

        var e = await Assert.ThrowsAsync<DefaultException>(() => create.ExecuteAsync(OwnEntry(seed.PurchaseUsage), "tester"));

        Assert.Equal($"Parceiro {Supplier} está sem UF no endereço de faturamento.", e.Message);
    }

    [Fact]
    public async Task Third_party_normal_document_is_calculated_like_the_own()
    {
        // Antes desta mudança o terceiro nunca calculava; a spec terceiro-chave D1 o põe igual à própria.
        var seed = await SeedAsync();
        var invoice = OwnEntry(seed.PurchaseUsage);
        invoice.IssuerType = DocumentIssuerType.ThirdParty;
        invoice.TaxDocumentKind = TaxDocumentKind.Other;

        await Create(seed).ExecuteAsync(invoice, "tester");

        Assert.NotNull((await seed.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync()).Cfop);
    }

    [Fact]
    public async Task Inactive_rule_keeps_the_own_entry_as_typed()
    {
        var seed = await SeedAsync(issuesNfe: false);

        await Create(seed).ExecuteAsync(OwnEntry(null), "tester");

        Assert.Null((await seed.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync()).Cfop);
    }

    [Fact]
    public async Task Legacy_third_party_document_without_branch_still_saves()
    {
        // Review Focus 1: o BranchCode nunca era gravado na entrada; documento de terceiro não passa a exigi-lo.
        var seed = await SeedAsync();
        var invoice = OwnEntry(null);
        invoice.IssuerType = DocumentIssuerType.ThirdParty;
        invoice.BranchCode = null;
        await Create(seed, "SAPB1").ExecuteAsync(invoice, "tester");

        var update = new PurchaseInvoicesUpdateService(seed.Db, seed.Partners, new FakeItemService(),
            TaxTestServices.PurchaseApply(seed.Db, seed.Partners));
        var changed = await seed.Db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync();
        changed.Comments = "conferido";

        await update.ExecuteAsync(changed.Key, changed, "tester");

        Assert.Equal("conferido", (await seed.Db.Context.PurchaseInvoices.AsNoTracking().SingleAsync()).Comments);
    }

    [Fact]
    public async Task Changing_the_supplier_recalculates_every_line()
    {
        var seed = await SeedAsync();
        await Create(seed).ExecuteAsync(OwnEntry(seed.PurchaseUsage), "tester");
        var partners = new FakeBusinessPartnerService(
            names: new Dictionary<string, string> { [Supplier] = "PRODUTOR TESTE", ["F-PR"] = "PRODUTOR PR" },
            states: new Dictionary<string, string> { [Supplier] = "SP", ["F-PR"] = "PR" });
        var update = new PurchaseInvoicesUpdateService(seed.Db, partners, new FakeItemService(),
            TaxTestServices.PurchaseApply(seed.Db, partners));
        var changed = await seed.Db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync();
        changed.CardCode = "F-PR";

        await update.ExecuteAsync(changed.Key, changed, "tester");

        Assert.Equal("2102", (await seed.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync()).Cfop);
    }

    [Fact]
    public async Task Manual_own_return_is_refused_in_a_branch_that_issues_nfe()
    {
        var seed = await SeedAsync();
        var invoice = OwnEntry(seed.ReturnUsage);
        invoice.InvoiceType = PurchaseInvoiceType.Return;

        var e = await Assert.ThrowsAsync<DefaultException>(() => Create(seed).ExecuteAsync(invoice, "tester"));

        Assert.Equal(
            "Na filial que emite NF-e pelo Siagro, a devolução de compra é feita pelo botão Devolver, no detalhe do documento de entrada.",
            e.Message);
    }

    [Fact]
    public async Task Purchase_return_is_calculated_with_the_outgoing_usage_and_conferred()
    {
        var seed = await SeedAsync();
        var origin = OwnEntry(seed.PurchaseUsage);
        await Create(seed).ExecuteAsync(origin, "tester");
        var originItem = origin.Items.Single();

        var returned = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = Supplier, IssuerType = DocumentIssuerType.Own,
            InvoiceType = PurchaseInvoiceType.Return, IssueDate = new DateTime(2026, 10, 6),
            PurchaseInvoiceOriginKey = origin.Key,
        };
        returned.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 400m, UnitPrice = 1.5m,
            UsageCode = seed.ReturnUsage, PurchaseInvoiceItemOriginKey = originItem.Key,
        });

        await Create(seed).ExecuteAsync(returned, "tester", nfeReturn: true);

        var line = returned.Items.Single();
        Assert.Equal("5202", line.Cfop);
        Assert.Equal("51", line.CstIcms);
        Assert.Equal("49", line.CstPis);
        Assert.True(returned.IsNfeReturn);
        Assert.Null(returned.PaymentConditionCode);
    }

    [Fact]
    public async Task Purchase_return_that_does_not_reproduce_the_purchase_is_refused()
    {
        var seed = await SeedAsync();
        var origin = OwnEntry(seed.PurchaseUsage);
        await Create(seed).ExecuteAsync(origin, "tester");
        var usage = await seed.Db.Context.Usages.SingleAsync(u => u.Code == seed.ReturnUsage);
        usage.IcmsInStateBenefitCode = "SP070010";
        await seed.Db.SaveChangesAsync();

        var returned = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = Supplier, IssuerType = DocumentIssuerType.Own,
            InvoiceType = PurchaseInvoiceType.Return, IssueDate = new DateTime(2026, 10, 6), PurchaseInvoiceOriginKey = origin.Key,
        };
        returned.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 400m, UnitPrice = 1.5m,
            UsageCode = seed.ReturnUsage, PurchaseInvoiceItemOriginKey = origin.Items.Single().Key,
        });

        var e = await Assert.ThrowsAsync<DefaultException>(() => Create(seed).ExecuteAsync(returned, "tester", nfeReturn: true));

        Assert.Equal(
            "Item TRIGO: a natureza de devolução DEVOLUCAO DE COMPRA não reproduz a tributação da compra — cBenef: compra SP053521, devolução SP070010.",
            e.Message);
    }

    // --- Frete, seguro, desconto e outras despesas na base (spec 2026-10-05 D3) ---

    private static PurchaseInvoice OwnEntryWithCharges(int usageCode)
    {
        var invoice = OwnEntry(usageCode);
        var item = invoice.Items.Single();
        (item.FreightValue, item.InsuranceValue, item.DiscountValue, item.OtherExpensesValue) = (100m, 20m, 50m, 30m);
        return invoice;
    }

    [Fact]
    public async Task Line_charges_enter_the_icms_base_of_the_entry()
    {
        var seed = await SeedAsync();

        await Create(seed).ExecuteAsync(OwnEntryWithCharges(seed.PurchaseUsage), "tester");

        var line = await seed.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync();
        Assert.Equal((1600m, 288.00m, 288.00m, 0m), (line.IcmsBase, line.IcmsOperationValue, line.IcmsDeferredValue, line.IcmsValue));
    }

    [Fact]
    public async Task Branch_without_the_rule_only_keeps_the_charges()
    {
        var seed = await SeedAsync();

        await Create(seed, "SAPB1").ExecuteAsync(OwnEntryWithCharges(seed.PurchaseUsage), "tester");

        var line = await seed.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync();
        Assert.Equal((100m, (string?)null, 0m), (line.FreightValue, line.Cfop, line.IcmsBase));
    }
}
