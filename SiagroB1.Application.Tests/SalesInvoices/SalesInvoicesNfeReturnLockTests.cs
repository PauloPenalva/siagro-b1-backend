using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>Travas da devolução própria (spec §7 e §9.4).</summary>
public class SalesInvoicesNfeReturnLockTests
{
    private static FakeBusinessPartnerService Partners() => new(
        names: new() { [NfeTestSeed.CardCode] = "CLIENTE BA LTDA", ["C2"] = "OUTRO" },
        states: new() { [NfeTestSeed.CardCode] = "BA", ["C2"] = "SP" });

    private static SalesInvoicesUpdateService HeaderUpdate(UnitOfWork db) =>
        new(db, Partners(), TaxTestServices.Apply(db, Partners()), NullLogger<SalesInvoicesUpdateService>.Instance);

    private static SalesInvoicesItemsUpdateService ItemUpdate(UnitOfWork db) =>
        new(db, new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            TaxTestServices.Apply(db, Partners()), NullLogger<SalesInvoicesUpdateService>.Instance);

    [Fact]
    public async Task Informar_nota_fiscal_is_refused_for_an_own_return()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            new SalesInvoicesSetDocumentNumberService(s.Sale.Db, new SalesInvoicesChangeLogService(s.Sale.Db.Context))
                .ExecuteAsync(created.Key, "000000010", "1", null, "tester"));

        Assert.Equal("Número, série e chave da devolução vêm da emissão da NF-e pelo Siagro.", ex.Message);
    }

    [Fact]
    public async Task Patch_cannot_turn_a_sale_into_an_own_return()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var sale = await s.Sale.Db.Context.SalesInvoices.SingleAsync(i => i.Key == s.Sale.InvoiceKey);
        sale.IsNfeReturn = true;

        await HeaderUpdate(s.Sale.Db).ExecuteAsync(sale.Key, sale, "tester");

        Assert.False((await s.Sale.Db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == sale.Key)).IsNfeReturn);
    }

    [Fact]
    public async Task Own_return_header_keeps_customer_and_branch()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);
        created.CardCode = "C2";
        created.BranchCode = "99";

        await HeaderUpdate(s.Sale.Db).ExecuteAsync(created.Key, created, "tester");

        var saved = await s.Sale.Db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == created.Key);
        Assert.Equal(NfeTestSeed.CardCode, saved.CardCode);
        Assert.Equal("01", saved.BranchCode);
    }

    [Fact]
    public async Task Own_return_line_keeps_price_and_usage_and_recalculates_the_quantity()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);
        var line = await s.Sale.Db.Context.SalesInvoicesItems.SingleAsync(i => i.SalesInvoiceKey == created.Key);
        line.Quantity = 10000m;
        line.UnitPrice = 9m;
        line.UsageCode = 999;

        await ItemUpdate(s.Sale.Db).ExecuteAsync(line.Key!.Value, line, "tester");

        var saved = await s.Sale.Db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == line.Key);
        Assert.Equal(10000m, saved.Quantity);
        Assert.Equal(2m, saved.UnitPrice);
        Assert.Equal(s.ReturnUsageCode, saved.UsageCode);
        Assert.Equal(20000m, saved.IcmsBase);
    }

    [Fact]
    public async Task Own_return_line_above_the_balance_is_refused()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);
        var line = await s.Sale.Db.Context.SalesInvoicesItems.SingleAsync(i => i.SalesInvoiceKey == created.Key);
        line.Quantity = 30001m;

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ItemUpdate(s.Sale.Db).ExecuteAsync(line.Key!.Value, line, "tester"));

        Assert.Contains("passa do saldo devolvível da venda", ex.Message);
    }

    [Fact]
    public async Task Own_return_line_with_zero_quantity_is_refused()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);
        var line = await s.Sale.Db.Context.SalesInvoicesItems.SingleAsync(i => i.SalesInvoiceKey == created.Key);
        line.Quantity = 0m;

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ItemUpdate(s.Sale.Db).ExecuteAsync(line.Key!.Value, line, "tester"));

        Assert.Equal("Item SOJA: informe a quantidade a devolver.", ex.Message);
    }

    [Fact]
    public async Task Own_return_refuses_new_lines()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 10000m);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            new SalesInvoicesItemsCreateService(s.Sale.Db, new FakeItemService(), TaxTestServices.Apply(s.Sale.Db, Partners()),
                    NullLogger<SalesInvoicesItemsCreateService>.Instance)
                .ExecuteAsync(new SalesInvoiceItem { SalesInvoiceKey = created.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 2m }, "tester"));

        Assert.StartsWith("Na devolução com NF-e, os itens vêm da venda", ex.Message);
    }

    [Fact]
    public async Task Retornar_is_refused_for_a_sale_authorized_by_the_siagro()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var service = new SalesInvoicesReturnService(s.Sale.Db, null!, null!, null!, null!, null!, null!,
            NullLogger<SalesInvoicesReturnService>.Instance, TaxTestServices.Gate(s.Sale.Db, "STANDALONE"));

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(
            new SalesInvoiceReturnRequest(s.Sale.InvoiceKey, [], RefusalDestination.Rebilling, null, "Recusa"), "tester"));

        Assert.Equal("Na filial que emite NF-e pelo Siagro, a devolução de documento com romaneio ou carga ainda não é suportada.", ex.Message);
    }

    [Fact]
    public async Task Retornar_outside_the_rule_keeps_the_old_validation()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var service = new SalesInvoicesReturnService(s.Sale.Db, null!, null!, null!, null!, null!, null!,
            NullLogger<SalesInvoicesReturnService>.Instance, TaxTestServices.Gate(s.Sale.Db, "SAPB1"));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(
            new SalesInvoiceReturnRequest(s.Sale.InvoiceKey, [], RefusalDestination.Rebilling, null, "Recusa"), "tester"));

        Assert.Equal("Selecione ao menos um romaneio a devolver.", ex.Message);
    }

    [Fact]
    public async Task Load_refusal_is_refused_for_a_sale_authorized_by_the_siagro()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var service = new ShipmentLoadsRefuseService(s.Sale.Db, null!, null!, null!, null!, null!, null!, null!,
            NullLogger<ShipmentLoadsRefuseService>.Instance, TaxTestServices.Gate(s.Sale.Db, "STANDALONE"));

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(
            new RefusalRequest(Guid.NewGuid(), [new RefusalLine(s.Sale.InvoiceKey, 1m)], RefusalDestination.Rebilling, null, "Recusa"), "tester"));

        Assert.Equal("Na filial que emite NF-e pelo Siagro, a devolução de documento com romaneio ou carga ainda não é suportada.", ex.Message);
    }
}
