using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// Liberação por departamento da liberação de pagamento (GAC-1188): cada departamento exige a
/// sua permissão, vale em qualquer status e "Loja" só se aplica a contrato com barter.
/// </summary>
public class PurchaseContractDepartmentReleaseTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsSetDepartmentReleaseService Service(PaymentReleaseDepartment? allowed) => new(
        _db.Context,
        new PurchaseContractsChangeLogService(_db.Context),
        allowed is { } d ? new FakeUserPermissions(PermissionCodes.ForDepartment(d)) : new FakeUserPermissions());

    private async Task<PurchaseContract> SeedAsync(ContractStatus status = ContractStatus.Approved, bool barter = false)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TotalVolume = 100_000m,
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            StandardPrice = 2m,
            Type = ContractType.Fixed,
            Status = status,
            IsBarter = barter,
        };

        _db.Context.PurchaseContracts.Add(contract);
        await _db.Context.SaveChangesAsync();

        return contract;
    }

    private Task<List<PurchaseContractChangeLog>> LogsAsync(Guid key) =>
        _db.Context.PurchaseContractsChangeLogs.Where(l => l.PurchaseContractKey == key).ToListAsync();

    private Task<List<PurchaseContractPaymentRelease>> RowsAsync(Guid key) =>
        _db.Context.PurchaseContractsPaymentReleases.AsNoTracking().Where(r => r.PurchaseContractKey == key).ToListAsync();

    [Fact]
    public async Task Release_WithoutPermission_Throws()
    {
        var contract = await SeedAsync();

        // Permissão de outro departamento não vale.
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(PaymentReleaseDepartment.Legal).ExecuteAsync(contract.Key, PaymentReleaseDepartment.Fiscal, true, "joao"));

        Assert.Equal("Você não tem permissão para a Lib. Fiscal.", ex.Message);
        Assert.Empty(await RowsAsync(contract.Key));
        Assert.Empty(await LogsAsync(contract.Key));
    }

    [Fact]
    public async Task Release_CreatesRow_WithUserAndDate()
    {
        var contract = await SeedAsync();
        var before = DateTime.Now.AddMinutes(-1);

        await Service(PaymentReleaseDepartment.Fiscal).ExecuteAsync(contract.Key, PaymentReleaseDepartment.Fiscal, true, "maria");

        var row = Assert.Single(await RowsAsync(contract.Key));
        Assert.Equal(PaymentReleaseDepartment.Fiscal, row.Department);
        Assert.True(row.Released);
        Assert.Equal("maria", row.ReleasedBy);
        Assert.True(row.ReleasedAt >= before);

        var log = Assert.Single(await LogsAsync(contract.Key));
        Assert.Equal(ContractChangeLogFields.DepartmentRelease, log.Field);
        Assert.Equal("Lib. Fiscal: Não", log.OldValue);
        Assert.Equal("Lib. Fiscal: Sim", log.NewValue);
        Assert.Equal("maria", log.ChangedBy);
    }

    [Fact]
    public async Task Unrelease_ClearsRow()
    {
        var contract = await SeedAsync();
        var service = Service(PaymentReleaseDepartment.Stock);
        await service.ExecuteAsync(contract.Key, PaymentReleaseDepartment.Stock, true, "maria");

        await service.ExecuteAsync(contract.Key, PaymentReleaseDepartment.Stock, false, "joao");

        var row = Assert.Single(await RowsAsync(contract.Key));
        Assert.False(row.Released);
        Assert.Null(row.ReleasedAt);
        Assert.Null(row.ReleasedBy);

        var logs = await LogsAsync(contract.Key);
        Assert.Equal(2, logs.Count);
        Assert.Contains(logs, l => l.OldValue == "Lib. Estoque: Sim" && l.NewValue == "Lib. Estoque: Não");
    }

    [Fact]
    public async Task Unrelease_NeverReleased_IsNoOp_NoLog()
    {
        var contract = await SeedAsync();

        await Service(PaymentReleaseDepartment.Board).ExecuteAsync(contract.Key, PaymentReleaseDepartment.Board, false, "joao");

        Assert.Empty(await RowsAsync(contract.Key));
        Assert.Empty(await LogsAsync(contract.Key));
    }

    [Fact]
    public async Task Store_NotBarter_Throws_ExistingRowUntouched()
    {
        var contract = await SeedAsync(barter: false);
        _db.Context.PurchaseContractsPaymentReleases.Add(new PurchaseContractPaymentRelease
        {
            PurchaseContractKey = contract.Key,
            Department = PaymentReleaseDepartment.Store,
            Released = true,
            ReleasedAt = DateTime.Now,
            ReleasedBy = "ana",
        });
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(PaymentReleaseDepartment.Store).ExecuteAsync(contract.Key, PaymentReleaseDepartment.Store, false, "joao"));

        Assert.Equal("Lib. Loja só se aplica a contrato com barter.", ex.Message);
        var row = Assert.Single(await RowsAsync(contract.Key));
        Assert.True(row.Released);
        Assert.Equal("ana", row.ReleasedBy);
        Assert.Empty(await LogsAsync(contract.Key));
    }

    [Fact]
    public async Task Store_Barter_Saves()
    {
        var contract = await SeedAsync(barter: true);

        await Service(PaymentReleaseDepartment.Store).ExecuteAsync(contract.Key, PaymentReleaseDepartment.Store, true, "joao");

        var row = Assert.Single(await RowsAsync(contract.Key));
        Assert.True(row.Released);
        Assert.Equal("joao", row.ReleasedBy);
    }

    [Theory]
    [InlineData(ContractStatus.Finished)]
    [InlineData(ContractStatus.Canceled)]
    public async Task Release_FinishedContract_Saves(ContractStatus status)
    {
        var contract = await SeedAsync(status);

        await Service(PaymentReleaseDepartment.Legal).ExecuteAsync(contract.Key, PaymentReleaseDepartment.Legal, true, "joao");

        Assert.True(Assert.Single(await RowsAsync(contract.Key)).Released);
    }

    [Fact]
    public async Task Release_MissingContract_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service(PaymentReleaseDepartment.Legal).ExecuteAsync(Guid.NewGuid(), PaymentReleaseDepartment.Legal, true, "joao"));
    }
}
