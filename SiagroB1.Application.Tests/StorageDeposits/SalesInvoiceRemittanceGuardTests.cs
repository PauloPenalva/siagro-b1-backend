using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.StorageDeposits;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageDeposits;

public class SalesInvoiceRemittanceGuardTests
{
    private static SalesInvoicesUsageGuardService Guard(UnitOfWork db) =>
        new(new UsageService(db, NullLogger<UsageService>.Instance));

    private static SalesInvoice Invoice(int usageCode, Guid? releaseKey) => new()
    {
        Key = Guid.NewGuid(), CardCode = StorageDepositSeed.Warehouse, ShipmentReleaseKey = releaseKey,
        Items = [new SalesInvoiceItem { ItemCode = StorageDepositSeed.Item, UnitOfMeasureCode = "KG", Quantity = 10m, UnitPrice = 1m, UsageCode = usageCode }],
    };

    [Fact]
    public async Task Remittance_usage_requires_the_release()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Guard(db).ValidateAsync(Invoice(seed.RemittanceUsageCode, null)));

        Assert.Equal("Natureza REMESSA P/ DEPOSITO é de remessa para depósito: emita pela liberação (Emitir remessa).", e.Message);
    }

    [Fact]
    public async Task Release_link_requires_remittance_usage()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        db.Context.Usages.Add(new Usage { Code = 1, Name = "VENDA", Direction = UsageDirection.Outgoing });
        db.Context.UsageEffects.Add(new UsageEffect { UsageCode = 1 });
        await db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Guard(db).ValidateAsync(Invoice(1, seed.Release.Key)));

        Assert.Equal("Remessa para depósito só aceita natureza de remessa para depósito.", e.Message);
    }

    [Fact]
    public async Task Return_usage_is_refused_on_outgoing_document()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Guard(db).ValidateAsync(Invoice(seed.ReturnUsageCode, null)));

        Assert.Equal("Natureza RETORNO DE DEPOSITO é de retorno de depósito e não pode ser usada no documento de saída.", e.Message);
    }

    [Fact]
    public async Task Confirm_refuses_remittance_whose_quantity_differs_from_the_release()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db, released: 100_000m);
        var remittance = await seed.AddRemittanceAsync(90_000m, InvoiceStatus.Pending);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            StorageDepositRules.EnsureRemittanceMatchesReleaseAsync(db.Context, remittance));

        Assert.Equal("A remessa para depósito precisa ter uma linha com a quantidade liberada (100.000,000) para o armazém ARM001.", e.Message);
    }

    [Fact]
    public async Task Confirm_refuses_remittance_to_another_recipient()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Pending);
        remittance.CardCode = "OUTRO";

        await Assert.ThrowsAsync<DefaultException>(() =>
            StorageDepositRules.EnsureRemittanceMatchesReleaseAsync(db.Context, remittance));
    }

    [Fact]
    public async Task Confirm_refuses_remittance_of_another_item_than_the_contract()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Pending);
        remittance.Items.Single().ItemCode = "MILHO";

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            StorageDepositRules.EnsureRemittanceMatchesReleaseAsync(db.Context, remittance));

        Assert.Equal("A remessa para depósito precisa ser do produto do contrato da liberação (SOJA).", e.Message);
    }

    [Fact]
    public async Task Matching_remittance_passes()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Pending);

        await StorageDepositRules.EnsureRemittanceMatchesReleaseAsync(db.Context, remittance);
    }
}
