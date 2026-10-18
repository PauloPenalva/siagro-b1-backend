using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Funrural da linha na entrada: o modo vem da natureza, o enquadramento do cadastro do parceiro e as alíquotas da
/// tabela vigente (spec 2026-10-18 §4.4). Tabela semeada: comercialização 1,32/0,11/0,20 e folha 0/0/0,20.
/// </summary>
public class PurchaseInvoicesFunruralTests
{
    /// <summary>O cenário da NF-e de entrada, com o modo da contribuição social na natureza "COMPRA DE MERCADORIA".</summary>
    private static async Task<(UnitOfWork Db, int Usage)> SeedAsync(RuralContributionMode mode)
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var code = await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db);
        var usage = await db.Context.Usages.SingleAsync(u => u.Code == code);
        usage.RuralContributionMode = mode;
        await db.SaveChangesAsync();
        return (db, code);
    }

    private static PurchaseInvoice OwnEntry(int usage)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = PurchaseNfeTestSeed.Supplier,
            IssuerType = DocumentIssuerType.Own, InvoiceType = PurchaseInvoiceType.Normal, IssueDate = new DateTime(2026, 10, 9),
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 1000m, UnitPrice = 1.5m, UsageCode = usage,
        });
        return invoice;
    }

    private static PurchaseInvoice ThirdPartyEntry(int usage)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = PurchaseNfeTestSeed.Supplier,
            IssuerType = DocumentIssuerType.ThirdParty, InvoiceType = PurchaseInvoiceType.Normal,
            TaxDocumentKind = TaxDocumentKind.Nfe, ChaveNFe = SupplierNfeXml.AccessKey,
            TaxDocumentNumber = "456", TaxDocumentSeries = "1", IssueDate = new DateTime(2026, 10, 1),
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 1000m, UnitPrice = 1.5m, UsageCode = usage,
        });
        return invoice;
    }

    /// <summary>Criação da entrada; sem <paramref name="partners"/>, o F-SP é produtor pessoa física não optante.</summary>
    private static PurchaseInvoicesCreateService Create(
        UnitOfWork db, string erp = "STANDALONE", FakeBusinessPartnerService? partners = null)
    {
        partners ??= PurchaseNfeTestSeed.Partners();
        return new PurchaseInvoicesCreateService(db, partners, new FakeItemService(),
            TaxTestServices.PurchaseApply(db, partners, erp), new FakeDocNumberSequenceService());
    }

    private static Task<PurchaseInvoiceItem> LineAsync(UnitOfWork db, Guid invoiceKey) =>
        db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.PurchaseInvoiceKey == invoiceKey);

    [Fact]
    public async Task Own_entry_from_individual_producer_withholds_standard_rates()
    {
        var (db, usage) = await SeedAsync(RuralContributionMode.Full);
        var invoice = OwnEntry(usage);

        await Create(db).ExecuteAsync(invoice, "tester");

        var line = await LineAsync(db, invoice.Key);
        Assert.Equal((1.32m, 0.11m, 0.20m), (line.FunruralInssRate, line.FunruralRatRate, line.FunruralSenarRate));
        Assert.Equal((1500m, 19.80m, 1.65m, 3.00m),
            (line.FunruralBase, line.FunruralInssValue, line.FunruralRatValue, line.FunruralSenarValue));
        var saved = await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == invoice.Key);
        Assert.Equal(24.45m, saved.TotalFunrural);
        Assert.Equal(1475.55m, saved.NetPayable);
    }

    [Fact]
    public async Task Special_insured_producer_withholds_standard_rates()
    {
        var (db, usage) = await SeedAsync(RuralContributionMode.Full);
        var invoice = OwnEntry(usage);

        await Create(db, partners: PurchaseNfeTestSeed.PartnersAs(RuralProducerType.SpecialInsured)).ExecuteAsync(invoice, "tester");

        var line = await LineAsync(db, invoice.Key);
        Assert.Equal((19.80m, 1.65m, 3.00m), (line.FunruralInssValue, line.FunruralRatValue, line.FunruralSenarValue));
    }

    [Fact]
    public async Task Senar_only_nature_withholds_only_senar()
    {
        var (db, usage) = await SeedAsync(RuralContributionMode.SenarOnly);
        var invoice = OwnEntry(usage);

        await Create(db).ExecuteAsync(invoice, "tester");

        var line = await LineAsync(db, invoice.Key);
        Assert.Equal((0m, 0m, 0.20m), (line.FunruralInssRate, line.FunruralRatRate, line.FunruralSenarRate));
        Assert.Equal((0m, 0m, 3.00m), (line.FunruralInssValue, line.FunruralRatValue, line.FunruralSenarValue));
    }

    [Fact]
    public async Task Payroll_option_producer_withholds_only_senar()
    {
        var (db, usage) = await SeedAsync(RuralContributionMode.Full);
        var invoice = OwnEntry(usage);

        await Create(db, partners: PurchaseNfeTestSeed.PartnersAs(RuralProducerType.Individual, payroll: true))
            .ExecuteAsync(invoice, "tester");

        var line = await LineAsync(db, invoice.Key);
        Assert.Equal((0m, 0m, 0.20m), (line.FunruralInssRate, line.FunruralRatRate, line.FunruralSenarRate));
        Assert.Equal((0m, 0m, 3.00m), (line.FunruralInssValue, line.FunruralRatValue, line.FunruralSenarValue));
    }

    [Fact]
    public async Task Legal_entity_producer_is_not_withheld()
    {
        var (db, usage) = await SeedAsync(RuralContributionMode.Full);
        var invoice = OwnEntry(usage);

        await Create(db, partners: PurchaseNfeTestSeed.PartnersAs(RuralProducerType.LegalEntity)).ExecuteAsync(invoice, "tester");

        var line = await LineAsync(db, invoice.Key);
        Assert.Equal((0m, 0m), (line.FunruralBase, line.FunruralValue));
    }

    [Fact]
    public async Task Supplier_not_marked_as_producer_is_not_withheld()
    {
        var (db, usage) = await SeedAsync(RuralContributionMode.Full);
        var invoice = OwnEntry(usage);

        await Create(db, partners: PurchaseNfeTestSeed.PartnersAs(null)).ExecuteAsync(invoice, "tester");

        var line = await LineAsync(db, invoice.Key);
        Assert.Equal((0m, 0m), (line.FunruralBase, line.FunruralValue));
    }

    [Fact]
    public async Task Nature_none_leaves_the_line_without_funrural()
    {
        var (db, usage) = await SeedAsync(RuralContributionMode.None);
        var invoice = OwnEntry(usage);
        var body = invoice.Items.Single();
        body.FunruralBase = 1500m;
        body.FunruralInssRate = 9m;
        body.FunruralInssValue = 999m;

        await Create(db).ExecuteAsync(invoice, "tester");

        var line = await LineAsync(db, invoice.Key);
        Assert.Equal((0m, 0m, 0m), (line.FunruralBase, line.FunruralInssRate, line.FunruralValue));
    }

    [Fact]
    public async Task Missing_effective_rates_refuse_the_entry()
    {
        var (db, usage) = await SeedAsync(RuralContributionMode.Full);
        db.Context.RuralContributionRates.RemoveRange(db.Context.RuralContributionRates);
        await db.SaveChangesAsync();
        var invoice = OwnEntry(usage);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Create(db).ExecuteAsync(invoice, "tester"));

        Assert.Equal("Cadastre as alíquotas de contribuição social vigentes em 09/10/2026.", ex.Message);
    }

    [Fact]
    public async Task Rates_effective_on_the_issue_date_are_used()
    {
        var (db, usage) = await SeedAsync(RuralContributionMode.Full);
        db.Context.RuralContributionRates.Add(new RuralContributionRate
        {
            StartDate = new DateOnly(2026, 10, 10), Scheme = RuralContributionScheme.Standard,
            InssRate = 1.5m, RatRate = 0.1m, SenarRate = 0.2m,
        });
        await db.SaveChangesAsync();
        var invoice = OwnEntry(usage);

        await Create(db).ExecuteAsync(invoice, "tester");

        // Emissão em 09/10: a vigência de 10/10 ainda não vale.
        var line = await LineAsync(db, invoice.Key);
        Assert.Equal((1.32m, 0.11m, 0.20m), (line.FunruralInssRate, line.FunruralRatRate, line.FunruralSenarRate));
    }

    [Fact]
    public async Task Third_party_entry_is_withheld_too()
    {
        var (db, usage) = await SeedAsync(RuralContributionMode.Full);
        var invoice = ThirdPartyEntry(usage);

        await Create(db).ExecuteAsync(invoice, "tester");

        var line = await LineAsync(db, invoice.Key);
        Assert.Equal((19.80m, 1.65m, 3.00m), (line.FunruralInssValue, line.FunruralRatValue, line.FunruralSenarValue));
    }

    [Fact]
    public async Task Funrural_in_the_body_is_discarded_when_the_rule_is_inactive()
    {
        var (db, usage) = await SeedAsync(RuralContributionMode.Full);
        var invoice = OwnEntry(usage);
        var body = invoice.Items.Single();
        body.FunruralBase = 1500m;
        body.FunruralInssRate = 9m;
        body.FunruralInssValue = 999m;

        await Create(db, erp: "SAPB1").ExecuteAsync(invoice, "tester");

        var line = await LineAsync(db, invoice.Key);
        Assert.Equal((0m, 0m, 0m), (line.FunruralBase, line.FunruralInssRate, line.FunruralValue));
    }

    [Fact]
    public async Task Confirmed_document_keeps_its_funrural()
    {
        // Natureza e parceiro diriam "não retém"; o documento confirmado não se recalcula.
        var (db, usage) = await SeedAsync(RuralContributionMode.Full);
        var invoice = OwnEntry(usage);
        invoice.InvoiceStatus = InvoiceStatus.Confirmed;
        var line = invoice.Items.Single();
        PurchaseInvoiceFunrural.Apply(line, 1.32m, 0.11m, 0.20m);

        await TaxTestServices.PurchaseApply(db, PurchaseNfeTestSeed.PartnersAs(RuralProducerType.LegalEntity))
            .ApplyAsync(invoice, invoice.Items);

        Assert.Equal((1.32m, 24.45m), (line.FunruralInssRate, line.FunruralValue));
    }

    [Fact]
    public async Task Changing_the_quantity_recalculates_the_funrural()
    {
        var (db, usage) = await SeedAsync(RuralContributionMode.Full);
        var invoice = OwnEntry(usage);
        await Create(db).ExecuteAsync(invoice, "tester");
        var key = invoice.Items.Single().Key!.Value;
        db.Context.ChangeTracker.Clear();

        var patch = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == key);
        patch.Quantity = 2000m;
        await new PurchaseInvoicesItemsUpdateService(db, new FakeItemService(),
            TaxTestServices.PurchaseApply(db, PurchaseNfeTestSeed.Partners())).ExecuteAsync(key, patch, "tester");

        var line = await LineAsync(db, invoice.Key);
        Assert.Equal((3000m, 39.60m, 3.30m, 6.00m),
            (line.FunruralBase, line.FunruralInssValue, line.FunruralRatValue, line.FunruralSenarValue));
    }

    [Fact]
    public async Task Moving_the_document_out_of_the_rule_clears_the_funrural()
    {
        // Revisão final F1: Normal -> Devolução ao cliente tira o documento da regra; a linha não pode ficar com Funrural.
        var (db, usage) = await SeedAsync(RuralContributionMode.Full);
        var invoice = ThirdPartyEntry(usage);
        await Create(db).ExecuteAsync(invoice, "tester");
        Assert.Equal(19.80m, (await LineAsync(db, invoice.Key)).FunruralInssValue);
        db.Context.ChangeTracker.Clear();

        var changed = await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == invoice.Key);
        changed.InvoiceType = PurchaseInvoiceType.Return;
        await new PurchaseInvoicesUpdateService(db, PurchaseNfeTestSeed.Partners(), new FakeItemService(),
            TaxTestServices.PurchaseApply(db, PurchaseNfeTestSeed.Partners())).ExecuteAsync(changed.Key, changed, "tester");

        var line = await LineAsync(db, invoice.Key);
        Assert.Equal((0m, 0m, 0m, 0m),
            (line.FunruralBase, line.FunruralInssValue, line.FunruralRatValue, line.FunruralSenarValue));
    }
}
