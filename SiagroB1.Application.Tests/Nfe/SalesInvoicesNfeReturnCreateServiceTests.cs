using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>"Devolver" (spec §6): cria a devolução própria Pendente a partir da venda autorizada.</summary>
public class SalesInvoicesNfeReturnCreateServiceTests
{
    private static async Task<string> Rejects(NfeReturnScenario s, decimal quantity = 30000m, string reason = "Carga recusada")
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() => NfeReturnTestSeed.CreateReturnAsync(s, quantity, reason));
        return ex.Message;
    }

    [Fact]
    public async Task Total_return_creates_a_pending_own_return_from_the_sale()
    {
        var s = await NfeReturnTestSeed.SeedAsync();

        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);

        var saved = await s.Sale.Db.Context.SalesInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == created.Key);
        Assert.Equal(SalesInvoiceType.Return, saved.InvoiceType);
        Assert.Equal(InvoiceStatus.Pending, saved.InvoiceStatus);
        Assert.True(saved.IsNfeReturn);
        Assert.Equal(s.Sale.InvoiceKey, saved.SalesInvoiceOriginKey);
        Assert.Equal(new DateTime(2026, 10, 2), saved.InvoiceDate!.Value.Date);
        Assert.Equal(30000m, saved.GrossWeight);
        Assert.Equal(30000m, saved.NetWeight);
        Assert.Null(saved.PaymentConditionCode);
        Assert.Null(saved.TaxPayerComments);
        Assert.StartsWith("Devolução da NF-e 1 série 1", saved.Comments);
        Assert.Contains("Motivo: Carga recusada", saved.Comments);

        var line = Assert.Single(saved.Items);
        Assert.Equal(s.ReturnUsageCode, line.UsageCode);
        Assert.Equal(s.SaleItemKey, line.SalesInvoiceItemOriginKey);
        Assert.Equal("2202", line.Cfop);
        Assert.Equal("00", line.CstIcms);
        Assert.Equal(7m, line.IcmsRate);

        var sale = await s.Sale.Db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == s.Sale.InvoiceKey);
        Assert.Equal(InvoiceStatus.Confirmed, sale.InvoiceStatus);
    }

    [Fact]
    public async Task Partial_returns_are_accepted_until_the_sold_quantity()
    {
        var s = await NfeReturnTestSeed.SeedAsync();

        await NfeReturnTestSeed.CreateReturnAsync(s, 10000m);
        await NfeReturnTestSeed.CreateReturnAsync(s, 20000m);

        Assert.Contains("passa do saldo devolvível (0,000)", await Rejects(s, 1m));
    }

    [Fact]
    public async Task Cancelled_return_frees_the_balance()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var first = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);
        first.InvoiceStatus = InvoiceStatus.Cancelled;
        await s.Sale.Db.SaveChangesAsync();

        var second = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);

        Assert.True(second.IsNfeReturn);
    }

    [Fact]
    public async Task Branch_that_does_not_issue_nfe_is_refused()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        (await s.Sale.Db.Context.Branchs.SingleAsync()).IssuesNfe = false;
        await s.Sale.Db.SaveChangesAsync();

        Assert.Equal("A filial 01 não emite NF-e pelo Siagro.", await Rejects(s));
    }

    [Fact]
    public async Task Sale_without_authorized_nfe_is_refused()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        (await s.Sale.Db.Context.SalesInvoices.SingleAsync()).NfeStatus = NfeStatus.None;
        await s.Sale.Db.SaveChangesAsync();

        Assert.Contains("não tem NF-e autorizada pelo Siagro", await Rejects(s));
    }

    [Fact]
    public async Task Sale_of_a_load_is_refused()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        (await s.Sale.Db.Context.SalesInvoices.SingleAsync()).ShipmentLoadKey = Guid.NewGuid();
        await s.Sale.Db.SaveChangesAsync();

        Assert.Equal("Documento com romaneio/carga: a devolução com NF-e ainda não é suportada.", await Rejects(s));
    }

    [Fact]
    public async Task Reason_is_required()
    {
        var s = await NfeReturnTestSeed.SeedAsync();

        Assert.Equal("Informe o motivo da devolução.", await Rejects(s, reason: " "));
    }

    [Fact]
    public async Task At_least_one_quantity_is_required()
    {
        var s = await NfeReturnTestSeed.SeedAsync();

        Assert.Equal("Informe a quantidade a devolver de ao menos um item.", await Rejects(s, 0m));
    }

    [Fact]
    public async Task Sale_usage_without_return_usage_is_refused()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        (await s.Sale.Db.Context.Usages.SingleAsync(u => u.Name == "Venda de grãos")).ReturnUsageCode = null;
        await s.Sale.Db.SaveChangesAsync();

        Assert.Contains("da venda não tem natureza de devolução cadastrada", await Rejects(s));
    }

    [Fact]
    public async Task Legacy_single_item_sale_without_number_is_accepted()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        (await s.Sale.Db.Context.SalesInvoicesItems.SingleAsync(i => i.Key == s.SaleItemKey)).NfeItemNumber = null;
        await s.Sale.Db.SaveChangesAsync();

        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);

        Assert.True(created.IsNfeReturn);
    }

    [Fact]
    public async Task Legacy_multi_item_sale_without_numbers_is_refused()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var sale = await s.Sale.Db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == s.Sale.InvoiceKey);
        sale.Items.Single().NfeItemNumber = null;
        var extra = sale.Items.Single();
        // Chave preenchida à mão: o EF trata como "existente" se entrar pela coleção rastreada.
        s.Sale.Db.Context.SalesInvoicesItems.Add(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = sale.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            Quantity = 1m, UnitPrice = 2m, UsageCode = extra.UsageCode,
        });
        await s.Sale.Db.SaveChangesAsync();

        Assert.Contains("antes da numeração dos itens", await Rejects(s));
    }

    [Fact]
    public async Task Api_body_cannot_mark_a_return_as_nfe_return()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var db = s.Sale.Db;
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);
        var partners = new FakeBusinessPartnerService(
            names: new() { [NfeTestSeed.CardCode] = "CLIENTE BA LTDA" }, states: new() { [NfeTestSeed.CardCode] = "BA" });

        // O caminho da API (OData POST) chega ao SalesInvoicesCreateService sem o parâmetro nfeReturn.
        var body = new SalesInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = NfeTestSeed.CardCode, InvoiceType = SalesInvoiceType.Return,
            SalesInvoiceOriginKey = s.Sale.InvoiceKey, IsNfeReturn = true, InvoiceDate = new DateTime(2026, 10, 2),
            Items = [new SalesInvoiceItem { ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 2m }],
        };

        await new SalesInvoicesCreateService(
                db, partners, new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA" }),
                new FakeDocNumberSequenceService(), new SalesInvoicesUsageGuardService(usages),
                new SalesInvoicesCfopResolveService(db, usages, partners), TaxTestServices.InactiveApply(db),
                TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesCreateService>.Instance)
            .ExecuteAsync(body, "tester");

        Assert.False((await db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == body.Key)).IsNfeReturn);
    }

    [Fact]
    public async Task Returnable_items_show_sold_returned_and_balance()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        await NfeReturnTestSeed.CreateReturnAsync(s, 10000m);

        var row = Assert.Single(await new SalesInvoicesNfeReturnableItemsService(s.Sale.Db).ExecuteAsync(s.Sale.InvoiceKey));

        Assert.Equal(s.SaleItemKey.ToString(), row.OriginItemKey);
        Assert.Equal(30000d, row.SoldQuantity);
        Assert.Equal(10000d, row.ReturnedQuantity);
        Assert.Equal(20000d, row.Returnable);
    }

    // --- Frete, seguro, desconto e outras despesas na proporção do que volta (spec 2026-10-05 D4) ---

    private static async Task SetSaleChargesAsync(NfeReturnScenario s, decimal freight, decimal insurance, decimal discount, decimal other)
    {
        var item = await s.Sale.Db.Context.SalesInvoicesItems.SingleAsync(i => i.Key == s.SaleItemKey);
        (item.FreightValue, item.InsuranceValue, item.DiscountValue, item.OtherExpensesValue) = (freight, insurance, discount, other);
        await s.Sale.Db.SaveChangesAsync();
    }

    private static Task<SalesInvoiceItem> ReturnLineAsync(NfeReturnScenario s, Guid returnKey) =>
        s.Sale.Db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync(i => i.SalesInvoiceKey == returnKey);

    [Fact]
    public async Task Partial_return_brings_the_charges_in_proportion_and_taxes_them()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        await SetSaleChargesAsync(s, 100m, 10m, 50m, 0.05m);

        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 10000m);

        var line = await ReturnLineAsync(s, created.Key);
        Assert.Equal((33.33m, 3.33m, 16.67m, 0.02m), (line.FreightValue, line.InsuranceValue, line.DiscountValue, line.OtherExpensesValue));
        // Base = 20.000,00 + 33,33 + 3,33 + 0,02 − 16,67 (D3 na devolução).
        Assert.Equal(20020.01m, line.IcmsBase);
    }

    [Fact]
    public async Task Proportional_discount_never_passes_the_ceiling_of_the_return_line()
    {
        // Revisão final I1: 3 x 150,125 = 450,38 (ToEven) e desconto 450,38 (bonificação) passa na origem; devolvendo 1,
        // a linha vale 150,12 (ToEven) e 450,38 / 3 = 150,1266 arredondaria para 150,13, acima do valor do produto da própria linha.
        var s = await NfeReturnTestSeed.SeedAsync();
        var item = await s.Sale.Db.Context.SalesInvoicesItems.SingleAsync(i => i.Key == s.SaleItemKey);
        (item.Quantity, item.UnitPrice, item.DiscountValue) = (3m, 150.125m, 450.38m);
        await s.Sale.Db.SaveChangesAsync();

        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 1m);

        var line = await ReturnLineAsync(s, created.Key);
        Assert.Equal(150.12m, line.Total);
        Assert.Equal(150.12m, line.DiscountValue);
    }

    [Fact]
    public async Task Half_cent_of_the_proportion_rounds_away_from_zero()
    {
        // Review Focus 2: 0,05 na metade = 0,025 → 0,03 (e não 0,02); 0,01 na metade = 0,005 → 0,01.
        var s = await NfeReturnTestSeed.SeedAsync();
        await SetSaleChargesAsync(s, 0m, 0m, 0.01m, 0.05m);

        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 15000m);

        var line = await ReturnLineAsync(s, created.Key);
        Assert.Equal((0.01m, 0.03m), (line.DiscountValue, line.OtherExpensesValue));
    }

    [Fact]
    public async Task Total_return_brings_exactly_the_charges_of_the_sale()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        await SetSaleChargesAsync(s, 100m, 10m, 50m, 0.05m);

        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);

        var line = await ReturnLineAsync(s, created.Key);
        Assert.Equal((100m, 10m, 50m, 0.05m), (line.FreightValue, line.InsuranceValue, line.DiscountValue, line.OtherExpensesValue));
    }
}
