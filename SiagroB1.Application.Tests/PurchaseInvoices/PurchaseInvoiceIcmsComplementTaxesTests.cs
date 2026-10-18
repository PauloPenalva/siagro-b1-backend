using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Spec 2026-10-17 D4 / "Motor de tributos": no complemento de ICMS o motor só resolve o CFOP e zera os demais tributos;
/// base e valor do ICMS são os informados, a alíquota é derivada (4 casas) e o CST segue a linha original.
/// </summary>
public class PurchaseInvoiceIcmsComplementTaxesTests
{
    private static PurchaseInvoice ThirdPartyIcms(int usage, decimal icmsBase = 1_000m, decimal icmsValue = 120m)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = PurchaseNfeTestSeed.Supplier,
            IssuerType = DocumentIssuerType.ThirdParty, InvoiceType = PurchaseInvoiceType.IcmsComplement,
            TaxDocumentKind = TaxDocumentKind.Other, TaxDocumentNumber = "8001", TaxDocumentSeries = "1",
            IssueDate = new DateTime(2026, 10, 17),
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", ItemName = "TRIGO EM GRAOS", UnitOfMeasureCode = "KG",
            IcmsBase = icmsBase, IcmsValue = icmsValue, UsageCode = usage,
            // O corpo pode trazer lixo nos demais tributos: o motor zera.
            PisBase = 10m, PisRate = 1.65m, PisValue = 0.17m, FunruralInssRate = 1.2m,
        });
        return invoice;
    }

    private static PurchaseInvoicesTaxApplyService Apply(UnitOfWork db) =>
        TaxTestServices.PurchaseApply(db, PurchaseNfeTestSeed.Partners());

    private static PurchaseInvoicesCreateService Create(UnitOfWork db) =>
        new(db, PurchaseNfeTestSeed.Partners(), new FakeItemService(), Apply(db), new FakeDocNumberSequenceService());

    private static Task<PurchaseInvoiceItem> LineAsync(UnitOfWork db, Guid invoiceKey)
    {
        db.Context.ChangeTracker.Clear();
        return db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.PurchaseInvoiceKey == invoiceKey);
    }

    private static void AssertOnlyIcms(PurchaseInvoiceItem line)
    {
        Assert.Equal((0m, 0m, 0m, 0m), (line.IcmsBaseReduction, line.IcmsDeferral, line.IcmsOperationValue, line.IcmsDeferredValue));
        Assert.Equal((0m, 0m, 0m), (line.PisBase, line.PisRate, line.PisValue));
        Assert.Equal((0m, 0m, 0m), (line.CofinsBase, line.CofinsRate, line.CofinsValue));
        Assert.Equal((0m, 0m, 0m), (line.IbsCbsBase, line.CbsRate, line.CbsValue));
        Assert.Equal((0m, 0m, 0m, 0m), (line.IbsStateRate, line.IbsMunicipalRate, line.IbsStateValue, line.IbsMunicipalValue));
        Assert.Equal((0m, 0m, 0m, 0m), (line.FunruralBase, line.FunruralInssRate, line.FunruralInssValue, line.FunruralSenarValue));
    }

    [Theory]
    [InlineData("00", "00")]
    [InlineData("20", "20")]
    [InlineData("90", "90")]
    [InlineData("51", "90")]
    [InlineData("41", "90")]
    [InlineData("101", "900")]
    [InlineData("900", "900")]
    [InlineData(null, "90")]
    [InlineData("", "90")]
    public void Cst_follows_the_original_line(string? original, string expected) =>
        Assert.Equal(expected, PurchaseInvoiceIcmsComplementTaxes.CstFor(original));

    [Fact]
    public void Icms_complement_keeps_the_ibs_cbs_rates_and_reductions_of_the_nature()
    {
        // Homologação SEFAZ-SP 17/10 (DE000050, série 9 nº 57): cClassTrib 200036 sem gRed → rejeição 1033 "Não informado o
        // grupo de redução de alíquota Estadual". O complemento não tem IBS/CBS (base e valores 0), mas o grupo precisa sair
        // com as alíquotas e a redução que o motor resolveu pela natureza.
        var line = new PurchaseInvoiceItem
        {
            IbsCbsCst = "200", IbsCbsClassCode = "200036", IbsCbsBase = 10m,
            IbsStateRate = 0.1m, IbsMunicipalRate = 0m, IbsRateReduction = 60m, IbsStateValue = 0.01m, IbsMunicipalValue = 0.02m,
            CbsRate = 0.9m, CbsRateReduction = 60m, CbsValue = 0.03m,
        };

        PurchaseInvoiceIcmsComplementTaxes.Apply(line, "90", 1_000m, 180m);

        Assert.Equal(("200", "200036"), (line.IbsCbsCst, line.IbsCbsClassCode));
        Assert.Equal((0.1m, 0m, 60m), (line.IbsStateRate, line.IbsMunicipalRate, line.IbsRateReduction));
        Assert.Equal((0.9m, 60m), (line.CbsRate, line.CbsRateReduction));
        Assert.Equal((0m, 0m, 0m, 0m), (line.IbsCbsBase, line.IbsStateValue, line.IbsMunicipalValue, line.CbsValue));
    }

    [Fact]
    public async Task Icms_complement_keeps_typed_base_and_value_after_recalculation()
    {
        // Review Focus 3: a natureza COMPRA DE MERCADORIA dá CST 51 com 18% e diferimento 100% — nada disso vale aqui.
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var invoice = ThirdPartyIcms(await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db));

        await Create(db).ExecuteAsync(invoice, "ana");

        var line = await LineAsync(db, invoice.Key);
        Assert.Equal((1_000m, 120m, 12.0000m), (line.IcmsBase, line.IcmsValue, line.IcmsRate));
        Assert.Equal(("90", "1102"), (line.CstIcms, line.Cfop));
        Assert.Equal((0m, 0m, 0m), (line.Quantity, line.UnitPrice, line.GrandTotal));
        AssertOnlyIcms(line);
    }

    [Fact]
    public async Task Icms_complement_rate_is_rounded_to_four_places()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var invoice = ThirdPartyIcms(await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db), 333.33m, 40m);

        await Create(db).ExecuteAsync(invoice, "ana");

        Assert.Equal(12.0001m, (await LineAsync(db, invoice.Key)).IcmsRate);
    }

    [Fact]
    public async Task Icms_complement_outside_the_rule_still_gets_rate_and_cst()
    {
        // Filial STANDALONE que não emite NF-e pelo Siagro: o motor não roda, mas a alíquota e o CST existem.
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        (await db.Context.Branchs.SingleAsync(b => b.Code == "01")).IssuesNfe = false;
        await db.SaveChangesAsync();
        var invoice = ThirdPartyIcms(await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db));

        await Create(db).ExecuteAsync(invoice, "ana");

        var line = await LineAsync(db, invoice.Key);
        Assert.Equal((1_000m, 120m, 12m, "90"), (line.IcmsBase, line.IcmsValue, line.IcmsRate, line.CstIcms));
        AssertOnlyIcms(line);
    }

    [Theory]
    [InlineData("00", "1102", "00", "1102")]
    [InlineData("51", "1101", "90", "1101")]
    [InlineData("101", "1102", "900", "1102")]
    public async Task Own_icms_complement_uses_original_cst_and_cfop(
        string originalCst, string originalCfop, string expectedCst, string expectedCfop)
    {
        var s = await ValueComplementNfeSeed.SeedAsync();
        await ValueComplementNfeSeed.ChangeAsync(s.Db, s.OriginalKey, original =>
        {
            var item = original.Items.Single();
            item.CstIcms = originalCst;
            item.Cfop = originalCfop;
        });
        s.Db.Context.ChangeTracker.Clear();
        var invoice = s.Complement((s.OriginalItemKey, 0m));
        invoice.InvoiceType = PurchaseInvoiceType.IcmsComplement;
        var typed = invoice.Items.Single();
        typed.IcmsBase = 1_000m;
        typed.IcmsValue = 120m;

        await ValueComplementNfeSeed.Create(s.Db).ExecuteAsync(invoice, "ana");

        var line = await LineAsync(s.Db, invoice.Key);
        Assert.Equal((expectedCst, expectedCfop), (line.CstIcms, line.Cfop));
        Assert.Equal((1_000m, 120m, 12m), (line.IcmsBase, line.IcmsValue, line.IcmsRate));
        AssertOnlyIcms(line);
    }

    [Fact]
    public async Task Icms_complement_has_no_difal()
    {
        // Fixture de PurchaseInvoiceDifalTests: fornecedor do PR, filial de SP contribuinte, natureza de uso e consumo.
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "FILIAL", ShortName = "FILIAL", TaxId = "12345678000195", StateCode = "SP",
            StateRegistration = "111222333444", TaxRegime = TaxRegime.Normal, IssuesNfe = true,
        });
        db.Context.Items.Add(new Item { ItemCode = "BOMBA", ItemName = "BOMBA D'AGUA", Ncm = "84137010", GoodsOrigin = 0 });
        db.Context.IcmsStateSettings.Add(new IcmsStateSetting
            { StateCode = "SP", InternalRate = 18m, FcpRate = 2m, DifalBaseRule = DifalBaseRule.Double });
        var consumption = (await new UsageService(db, NullLogger<UsageService>.Instance).CreateAsync(new UsageModel
        {
            Name = "Uso e consumo", Direction = UsageDirection.Incoming, CfopIncomingInState = "1556",
            CfopIncomingOutState = "2556", IcmsOutStateCst = "41", PisCst = "74", CofinsCst = "74",
            PurchasePurpose = PurchasePurpose.UseAndConsumption,
        })).Code;
        await db.SaveChangesAsync();
        var partners = new FakeBusinessPartnerService(
            names: new Dictionary<string, string> { ["F-PR"] = "FORNECEDOR" },
            states: new Dictionary<string, string> { ["F-PR"] = "PR" });
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = "F-PR", IssuerType = DocumentIssuerType.ThirdParty,
            InvoiceType = PurchaseInvoiceType.IcmsComplement, TaxDocumentKind = TaxDocumentKind.Other,
            TaxDocumentNumber = "9001", TaxDocumentSeries = "1", IssueDate = new DateTime(2026, 10, 12),
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "BOMBA", ItemName = "BOMBA D'AGUA", UnitOfMeasureCode = "UN",
            IcmsBase = 1_000m, IcmsValue = 120m, UsageCode = consumption,
        });

        await new PurchaseInvoicesCreateService(db, partners, new FakeItemService(),
            TaxTestServices.PurchaseApply(db, partners), new FakeDocNumberSequenceService()).ExecuteAsync(invoice, "ana");

        var line = await LineAsync(db, invoice.Key);
        Assert.Equal((1_000m, 120m, "2556"), (line.IcmsBase, line.IcmsValue, line.Cfop));
        Assert.Equal((0m, 0m, 0m), (line.DifalBase, line.DifalValue, line.DifalInternalRate));
        Assert.Equal((0m, 0m), (line.FcpRate, line.FcpValue));
    }

    [Fact]
    public async Task Pending_icms_complement_line_patch_saves_the_new_typed_values()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var invoice = ThirdPartyIcms(await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db));
        await Create(db).ExecuteAsync(invoice, "ana");
        var patch = await LineAsync(db, invoice.Key);
        patch.IcmsBase = 2_000m;
        patch.IcmsValue = 300m;

        await new PurchaseInvoicesItemsUpdateService(db, new FakeItemService(), Apply(db))
            .ExecuteAsync(patch.Key!.Value, patch, "ana");

        var line = await LineAsync(db, invoice.Key);
        Assert.Equal((2_000m, 300m, 15m, "90"), (line.IcmsBase, line.IcmsValue, line.IcmsRate, line.CstIcms));
    }

    [Fact]
    public async Task Pending_icms_complement_header_patch_saves_the_new_typed_values()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var invoice = ThirdPartyIcms(await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db));
        await Create(db).ExecuteAsync(invoice, "ana");
        db.Context.ChangeTracker.Clear();
        var incoming = await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items)
            .SingleAsync(i => i.Key == invoice.Key);
        incoming.Items.Single().IcmsBase = 500m;
        incoming.Items.Single().IcmsValue = 60m;

        await new PurchaseInvoicesUpdateService(db, PurchaseNfeTestSeed.Partners(), new FakeItemService(), Apply(db))
            .ExecuteAsync(invoice.Key, incoming, "ana");

        var line = await LineAsync(db, invoice.Key);
        Assert.Equal((500m, 60m, 12m), (line.IcmsBase, line.IcmsValue, line.IcmsRate));
    }

    [Fact]
    public async Task Icms_complement_typed_values_are_kept_off_pending()
    {
        // Pre-flight: o PATCH em documento Confirmado pode ser recusado ou ter os campos restaurados — o que importa é que
        // os valores confirmados nunca se movem.
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var invoice = ThirdPartyIcms(await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db));
        await Create(db).ExecuteAsync(invoice, "ana");
        await ValueComplementNfeSeed.ChangeAsync(db, invoice.Key, i => i.InvoiceStatus = InvoiceStatus.Confirmed);
        var patch = await LineAsync(db, invoice.Key);
        patch.IcmsBase = 2_000m;
        patch.IcmsValue = 300m;

        try
        {
            await new PurchaseInvoicesItemsUpdateService(db, new FakeItemService(), Apply(db))
                .ExecuteAsync(patch.Key!.Value, patch, "ana");
        }
        catch (DefaultException)
        {
            // Recusado também serve: o requisito é só que os valores não se movam.
        }

        var line = await LineAsync(db, invoice.Key);
        Assert.Equal((1_000m, 120m, 12m, "90"), (line.IcmsBase, line.IcmsValue, line.IcmsRate, line.CstIcms));
    }
}
