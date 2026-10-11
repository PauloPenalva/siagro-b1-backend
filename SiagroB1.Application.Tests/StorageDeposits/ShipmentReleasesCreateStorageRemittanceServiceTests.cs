using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.StorageDeposits;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageDeposits;

public class ShipmentReleasesCreateStorageRemittanceServiceTests
{
    private static ShipmentReleasesCreateStorageRemittanceService Service(UnitOfWork db, string erp = "STANDALONE")
    {
        var partners = new FakeBusinessPartnerService(names: new Dictionary<string, string> { [StorageDepositSeed.Warehouse] = "ARMAZEM TERCEIRO" },
            states: new Dictionary<string, string> { [StorageDepositSeed.Warehouse] = "SP" });
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);
        var create = new SalesInvoicesCreateService(
            db, partners, new FakeItemService(), new FakeDocNumberSequenceService(),
            new SalesInvoicesUsageGuardService(usages), new SalesInvoicesCfopResolveService(db, usages, partners),
            TaxTestServices.InactiveApply(db), TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesCreateService>.Instance);
        return new(db, TestConfiguration.Erp(erp), partners, usages, create,
            NullLogger<ShipmentReleasesCreateStorageRemittanceService>.Instance);
    }

    [Fact]
    public async Task Creates_pending_remittance_to_the_warehouse_with_the_released_quantity()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db, released: 100_000m);

        var created = await Service(db).ExecuteAsync(new(seed.Release.Key, seed.RemittanceUsageCode, 2.5m), "tester");

        var saved = await db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == created.Key);
        Assert.Equal(StorageDepositSeed.Warehouse, saved.CardCode);
        Assert.Equal(seed.Release.Key, saved.ShipmentReleaseKey);
        Assert.Equal(InvoiceStatus.Pending, saved.InvoiceStatus);
        Assert.Equal(StorageDepositSeed.Branch, saved.BranchCode);
        var line = Assert.Single(saved.Items);
        Assert.Equal(StorageDepositSeed.Item, line.ItemCode);
        Assert.Equal(100_000m, line.Quantity);
        Assert.Equal(2.5m, line.UnitPrice);
        Assert.Equal(seed.RemittanceUsageCode, line.UsageCode);
        Assert.Null(line.SalesContractKey);
    }

    [Fact]
    public async Task Refuses_in_sap_mode()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db, "SAPB1").ExecuteAsync(new(seed.Release.Key, seed.RemittanceUsageCode, 2m), "tester"));

        Assert.Equal("Disponível somente no modo STANDALONE.", e.Message);
    }

    [Fact]
    public async Task Refuses_cancelled_release()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        seed.Release.Status = ReleaseStatus.Cancelled;
        await db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ExecuteAsync(new(seed.Release.Key, seed.RemittanceUsageCode, 2m), "tester"));

        Assert.Equal("Liberação cancelada não pode ter remessa para depósito.", e.Message);
    }

    [Fact]
    public async Task Refuses_second_active_remittance_but_allows_after_cancelling()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var first = await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Pending);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ExecuteAsync(new(seed.Release.Key, seed.RemittanceUsageCode, 2m), "tester"));
        Assert.Equal("A liberação já tem a remessa para depósito nº 000000123 ativa.", e.Message);

        first.InvoiceStatus = InvoiceStatus.Cancelled;
        await db.SaveChangesAsync();
        await Service(db).ExecuteAsync(new(seed.Release.Key, seed.RemittanceUsageCode, 2m), "tester");
    }

    [Fact]
    public async Task Refuses_warehouse_without_business_partner()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        seed.Release.DeliveryLocationCode = "INTERNO";
        await db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ExecuteAsync(new(seed.Release.Key, seed.RemittanceUsageCode, 2m), "tester"));

        Assert.Equal("O armazém INTERNO não tem parceiro de negócio cadastrado: a remessa precisa de destinatário.", e.Message);
    }

    [Fact]
    public async Task Refuses_usage_without_remittance_role()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ExecuteAsync(new(seed.Release.Key, seed.ReturnUsageCode, 2m), "tester"));

        Assert.Equal("Escolha uma natureza de remessa para depósito.", e.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Refuses_non_positive_price(decimal price)
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ExecuteAsync(new(seed.Release.Key, seed.RemittanceUsageCode, price), "tester"));

        Assert.Equal("Informe o preço unitário da remessa.", e.Message);
    }
}
