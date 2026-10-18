using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Spec 2026-10-18 S5/S6: no complemento de ICMS de saída o motor só resolve o CFOP e zera o resto; base e valor do ICMS
/// são os informados, a alíquota é derivada, CST e CFOP vêm da linha original. As linhas não se movem — só Base/ICMS, e só
/// em Pendente — e não se incluem nem removem (V7).
/// </summary>
public class SalesInvoiceIcmsComplementTaxesTests
{
    private const string V7 = "O complemento de ICMS não aceita inclusão nem remoção de itens.";

    private static FakeBusinessPartnerService Partners() => new(
        names: new() { [NfeTestSeed.CardCode] = "CLIENTE BA LTDA", ["C2"] = "OUTRO" },
        states: new() { [NfeTestSeed.CardCode] = "BA", ["C2"] = "SP" });

    private static SalesInvoicesTaxApplyService TaxApply(UnitOfWork db) => TaxTestServices.Apply(db, Partners());

    private static SalesInvoicesItemsUpdateService ItemUpdate(UnitOfWork db) =>
        new(db, new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS", ["MILHO"] = "MILHO" }),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            TaxApply(db), TaxTestServices.InactiveFiscalComplement(db), PurchaseDischargeTestServices.Sync(db),
            NullLogger<SalesInvoicesUpdateService>.Instance);

    private static SalesInvoicesUpdateService HeaderUpdate(UnitOfWork db) =>
        new(db, Partners(), TaxApply(db), TaxTestServices.InactiveFiscalComplement(db),
            NullLogger<SalesInvoicesUpdateService>.Instance);

    /// <summary>
    /// A venda autorizada do <see cref="NfeReturnTestSeed"/> (CST da linha parametrizado, CFOP 6101) com a natureza
    /// tributada de um jeito que o complemento NÃO pode seguir (CST 20 com redução fora do estado, PIS/COFINS, IBS/CBS) e
    /// o complemento Pendente dela, montado à mão (a ação de criação é a Task 3).
    /// </summary>
    private static async Task<(UnitOfWork Db, SalesInvoice Complement, SalesInvoiceItem Line)> SeedAsync(
        string originalCst = "00", decimal icmsBase = 1_000m, decimal icmsValue = 120m)
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var db = s.Sale.Db;
        var usage = await db.Context.Usages.SingleAsync(u => u.Name == "Venda de grãos");
        usage.IcmsOutStateCst = "20";
        usage.IcmsOutStateBaseReduction = 10m;
        usage.PisCst = "01";
        usage.PisRate = 1.65m;
        usage.CofinsCst = "01";
        usage.CofinsRate = 7.6m;
        usage.IbsCbsCst = "000";
        usage.IbsCbsClassCode = "000001";
        var original = await db.Context.SalesInvoicesItems.SingleAsync(i => i.Key == s.SaleItemKey);
        original.CstIcms = originalCst;
        original.Cfop = "6101";
        await db.SaveChangesAsync();

        var line = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "SOJA", ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "KG",
            Quantity = 0m, UnitPrice = 0m, UsageCode = usage.Code, UsageName = usage.Name, Cfop = "6101",
            Ncm = "12019000", GoodsOrigin = 0, ComplementedItemKey = s.SaleItemKey,
            IcmsBase = icmsBase, IcmsValue = icmsValue,
            // Lixo no corpo: o aplicador zera.
            PisBase = 10m, PisRate = 1.65m, PisValue = 0.17m,
        };
        var complement = new SalesInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = NfeTestSeed.CardCode, CardName = "CLIENTE BA LTDA",
            InvoiceType = SalesInvoiceType.IcmsComplement, InvoiceStatus = InvoiceStatus.Pending,
            InvoiceNumber = "000002400", InvoiceDate = new DateTime(2026, 10, 2), TaxDocumentKind = TaxDocumentKind.Nfe,
            Items = [line],
        };
        db.Context.SalesInvoices.Add(complement);
        await TaxApply(db).ApplyAsync(complement, complement.Items);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        return (db, complement, line);
    }

    private static Task<SalesInvoiceItem> SavedLineAsync(UnitOfWork db, Guid key)
    {
        db.Context.ChangeTracker.Clear();
        return db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == key);
    }

    private static void AssertOnlyIcms(SalesInvoiceItem line)
    {
        Assert.Equal((0m, 0m, 0m, 0m), (line.IcmsBaseReduction, line.IcmsDeferral, line.IcmsOperationValue, line.IcmsDeferredValue));
        Assert.Equal((0m, 0m, 0m), (line.PisBase, line.PisRate, line.PisValue));
        Assert.Equal((0m, 0m, 0m), (line.CofinsBase, line.CofinsRate, line.CofinsValue));
        Assert.Equal((0m, 0m, 0m, 0m), (line.IbsCbsBase, line.CbsValue, line.IbsStateValue, line.IbsMunicipalValue));
    }

    [Fact]
    public async Task Sales_icms_complement_keeps_typed_values_after_recalculation()
    {
        var (db, _, created) = await SeedAsync();

        var line = await SavedLineAsync(db, created.Key!.Value);

        Assert.Equal((1_000m, 120m, 12.0000m), (line.IcmsBase, line.IcmsValue, line.IcmsRate));
        Assert.Equal(("00", "6101"), (line.CstIcms, line.Cfop));
        Assert.Equal((0m, 0m, 0m), (line.Quantity, line.UnitPrice, line.GrandTotal));
        AssertOnlyIcms(line);
        // IBS/CBS: alíquotas da vigência mantidas para o grupo do XML (rejeição 1033 sem elas), valores 0.
        Assert.Equal(("000", "000001"), (line.IbsCbsCst, line.IbsCbsClassCode));
        Assert.Equal((0.9m, 0.1m), (line.CbsRate, line.IbsStateRate));
    }

    [Theory]
    [InlineData("00", "00")]
    [InlineData("20", "20")]
    [InlineData("90", "90")]
    [InlineData("51", "90")]
    [InlineData("101", "900")]
    public async Task Sales_icms_complement_cst_follows_original(string originalCst, string expected)
    {
        var (db, _, created) = await SeedAsync(originalCst);

        Assert.Equal(expected, (await SavedLineAsync(db, created.Key!.Value)).CstIcms);
    }

    [Fact]
    public async Task Patch_changing_item_quantity_or_price_is_restored_or_refused()
    {
        // Review Focus 4: produto, quantidade, preço, natureza, valores da linha e vínculo não se movem pelo PATCH.
        var (db, _, created) = await SeedAsync();
        var patch = await db.Context.SalesInvoicesItems.SingleAsync(i => i.Key == created.Key);
        var originalKey = patch.ComplementedItemKey;
        var usage = patch.UsageCode;
        patch.ItemCode = "MILHO";
        patch.Quantity = 10m;
        patch.UnitPrice = 5m;
        patch.FreightValue = 7m;
        patch.UsageCode = 999;
        patch.ComplementedItemKey = Guid.NewGuid();
        patch.SalesContractKey = Guid.NewGuid();
        patch.CstIcms = "41";
        patch.Cfop = "5102";

        try
        {
            await ItemUpdate(db).ExecuteAsync(patch.Key!.Value, patch, "tester");
        }
        catch (DefaultException)
        {
            // Recusar também atende: o requisito é que a linha não se mova.
        }

        var line = await SavedLineAsync(db, created.Key!.Value);
        Assert.Equal(("SOJA", 0m, 0m, 0m), (line.ItemCode, line.Quantity, line.UnitPrice, line.FreightValue));
        Assert.Equal((usage, originalKey, (Guid?)null), (line.UsageCode, line.ComplementedItemKey, line.SalesContractKey));
        Assert.Equal(("00", "6101"), (line.CstIcms, line.Cfop));
        Assert.Equal((1_000m, 120m, 12m), (line.IcmsBase, line.IcmsValue, line.IcmsRate));
    }

    [Fact]
    public async Task Patch_icms_values_on_pending_complement_is_kept()
    {
        var (db, _, created) = await SeedAsync();
        var patch = await db.Context.SalesInvoicesItems.SingleAsync(i => i.Key == created.Key);
        patch.IcmsBase = 2_000m;
        patch.IcmsValue = 300m;
        patch.IcmsRate = 99m;

        await ItemUpdate(db).ExecuteAsync(patch.Key!.Value, patch, "tester");

        var line = await SavedLineAsync(db, created.Key!.Value);
        Assert.Equal((2_000m, 300m, 15m, "00"), (line.IcmsBase, line.IcmsValue, line.IcmsRate, line.CstIcms));
        AssertOnlyIcms(line);
    }

    [Fact]
    public async Task Put_icms_values_on_pending_complement_is_kept()
    {
        // Caminho PUT: a entidade chega desanexada.
        var (db, _, created) = await SeedAsync();
        var put = await SavedLineAsync(db, created.Key!.Value);
        put.IcmsBase = 500m;
        put.IcmsValue = 60m;
        put.Quantity = 3m;

        await ItemUpdate(db).ExecuteAsync(put.Key!.Value, put, "tester");

        var line = await SavedLineAsync(db, created.Key!.Value);
        Assert.Equal((500m, 60m, 12m, 0m), (line.IcmsBase, line.IcmsValue, line.IcmsRate, line.Quantity));
    }

    [Theory]
    [InlineData(InvoiceStatus.Confirmed, NfeStatus.Authorized)]
    [InlineData(InvoiceStatus.Pending, NfeStatus.Authorized)]
    [InlineData(InvoiceStatus.Pending, NfeStatus.Processing)]
    public async Task Patch_icms_values_off_pending_does_not_move_them(InvoiceStatus status, NfeStatus nfe)
    {
        var (db, complement, created) = await SeedAsync();
        var stored = await db.Context.SalesInvoices.SingleAsync(i => i.Key == complement.Key);
        stored.InvoiceStatus = status;
        stored.NfeStatus = nfe;
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        var patch = await db.Context.SalesInvoicesItems.SingleAsync(i => i.Key == created.Key);
        patch.IcmsBase = 2_000m;
        patch.IcmsValue = 300m;

        try
        {
            await ItemUpdate(db).ExecuteAsync(patch.Key!.Value, patch, "tester");
        }
        catch (DefaultException)
        {
            // Recusar também atende.
        }

        var line = await SavedLineAsync(db, created.Key!.Value);
        Assert.Equal((1_000m, 120m, 12m), (line.IcmsBase, line.IcmsValue, line.IcmsRate));
    }

    [Fact]
    public async Task Adding_or_removing_lines_is_refused()
    {
        var (db, complement, created) = await SeedAsync();

        var add = await Assert.ThrowsAsync<DefaultException>(() =>
            new SalesInvoicesItemsCreateService(db, new FakeItemService(), TaxApply(db),
                    TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesItemsCreateService>.Instance)
                .ExecuteAsync(new SalesInvoiceItem
                {
                    SalesInvoiceKey = complement.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 2m,
                }, "tester"));
        var remove = await Assert.ThrowsAsync<DefaultException>(() =>
            new SalesInvoicesItemsDeleteService(db, TaxApply(db), NullLogger<SalesInvoicesItemsDeleteService>.Instance)
                .ExecuteAsync(created.Key!.Value));

        Assert.Equal(V7, add.Message);
        Assert.Equal(V7, remove.Message);
        db.Context.ChangeTracker.Clear();
        Assert.Equal(1, await db.Context.SalesInvoicesItems.CountAsync(i => i.SalesInvoiceKey == complement.Key));
    }

    [Fact]
    public async Task Complement_header_keeps_customer_branch_and_type()
    {
        var (db, complement, _) = await SeedAsync();
        var patch = await db.Context.SalesInvoices.SingleAsync(i => i.Key == complement.Key);
        patch.CardCode = "C2";
        patch.BranchCode = "99";
        patch.InvoiceType = SalesInvoiceType.Normal;

        await HeaderUpdate(db).ExecuteAsync(patch.Key, patch, "tester");

        db.Context.ChangeTracker.Clear();
        var saved = await db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == complement.Key);
        Assert.Equal((NfeTestSeed.CardCode, "01", SalesInvoiceType.IcmsComplement),
            (saved.CardCode, saved.BranchCode, saved.InvoiceType));
        Assert.Equal("CLIENTE BA LTDA", saved.CardName);
    }

    [Fact]
    public async Task Patch_cannot_turn_a_sale_into_an_icms_complement()
    {
        var s = await NfeTestSeed.SeedAsync();
        var sale = await s.Db.Context.SalesInvoices.SingleAsync(i => i.Key == s.InvoiceKey);
        sale.InvoiceType = SalesInvoiceType.IcmsComplement;

        await HeaderUpdate(s.Db).ExecuteAsync(sale.Key, sale, "tester");

        s.Db.Context.ChangeTracker.Clear();
        Assert.Equal(SalesInvoiceType.Normal,
            (await s.Db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == s.InvoiceKey)).InvoiceType);
    }

    [Fact]
    public void Purchase_icms_complement_still_passes()
    {
        // O aplicador comum não leva o Funrural: o invólucro da entrada continua limpando-o. A suíte da entrada
        // (PurchaseInvoiceIcmsComplement*) cobre o resto.
        var line = new PurchaseInvoiceItem
        {
            FunruralBase = 10m, FunruralInssRate = 1.2m, FunruralInssValue = 0.12m, PisBase = 5m, CbsRate = 0.9m,
        };

        PurchaseInvoiceIcmsComplementTaxes.Apply(line, IcmsComplementTaxes.CstFor("51"), 1_000m, 120m);

        Assert.Equal(("90", 1_000m, 120m, 12m), (line.CstIcms, line.IcmsBase, line.IcmsValue, line.IcmsRate));
        Assert.Equal((0m, 0m, 0m, 0m), (line.FunruralBase, line.FunruralInssRate, line.FunruralInssValue, line.PisBase));
        Assert.Equal(0.9m, line.CbsRate);
    }
}
