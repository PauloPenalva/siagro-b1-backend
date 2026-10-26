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

    private Task<List<PurchaseContractPaymentReleaseHistory>> HistoryAsync(Guid key) =>
        _db.Context.PurchaseContractsPaymentReleaseHistory.AsNoTracking()
            .Where(h => h.PurchaseContractKey == key).OrderBy(h => h.ChangedAt).ToListAsync();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Release_WithoutReason_Throws_NothingSaved(string? reason)
    {
        var contract = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(PaymentReleaseDepartment.Fiscal).ExecuteAsync(contract.Key, PaymentReleaseDepartment.Fiscal, true, reason, "maria"));

        Assert.Equal("Informe o motivo da liberação.", ex.Message);
        Assert.Empty(await RowsAsync(contract.Key));
        Assert.Empty(await HistoryAsync(contract.Key));
        Assert.Empty(await LogsAsync(contract.Key));
    }

    [Fact]
    public async Task Unrelease_WithoutReason_Throws()
    {
        var contract = await SeedAsync();
        var service = Service(PaymentReleaseDepartment.Fiscal);
        await service.ExecuteAsync(contract.Key, PaymentReleaseDepartment.Fiscal, true, "ok", "maria");

        await Assert.ThrowsAsync<DefaultException>(() =>
            service.ExecuteAsync(contract.Key, PaymentReleaseDepartment.Fiscal, false, " ", "maria"));

        Assert.True(Assert.Single(await RowsAsync(contract.Key)).Released);
        Assert.Single(await HistoryAsync(contract.Key));
    }

    [Fact]
    public async Task Release_ReasonTooLong_Throws()
    {
        var contract = await SeedAsync();

        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(PaymentReleaseDepartment.Fiscal).ExecuteAsync(
                contract.Key, PaymentReleaseDepartment.Fiscal, true, new string('x', 501), "maria"));

        Assert.Empty(await RowsAsync(contract.Key));
        Assert.Empty(await HistoryAsync(contract.Key));
    }

    [Fact]
    public async Task Release_AppendsHistory_WithReasonUserAndDate()
    {
        var contract = await SeedAsync();
        var before = DateTime.Now.AddMinutes(-1);

        await Service(PaymentReleaseDepartment.Fiscal).ExecuteAsync(
            contract.Key, PaymentReleaseDepartment.Fiscal, true, "  Documentos conferidos  ", "maria");

        var h = Assert.Single(await HistoryAsync(contract.Key));
        Assert.Equal(PaymentReleaseDepartment.Fiscal, h.Department);
        Assert.True(h.Released);
        Assert.Equal("Documentos conferidos", h.Reason);
        Assert.Equal("maria", h.ChangedBy);
        Assert.True(h.ChangedAt >= before);

        var log = Assert.Single(await LogsAsync(contract.Key));
        Assert.Equal("Lib. Fiscal: Sim (motivo: Documentos conferidos)", log.NewValue);
    }

    [Fact]
    public async Task Release_SetsCurrentReason()
    {
        var contract = await SeedAsync();

        await Service(PaymentReleaseDepartment.Fiscal).ExecuteAsync(
            contract.Key, PaymentReleaseDepartment.Fiscal, true, "Motivo A", "maria");

        Assert.Equal("Motivo A", Assert.Single(await RowsAsync(contract.Key)).Reason);
    }

    [Fact]
    public async Task ReleaseThenUnrelease_KeepsTwoHistoryRows()
    {
        var contract = await SeedAsync();
        var service = Service(PaymentReleaseDepartment.Fiscal);
        await service.ExecuteAsync(contract.Key, PaymentReleaseDepartment.Fiscal, true, "Liberado", "maria");
        await service.ExecuteAsync(contract.Key, PaymentReleaseDepartment.Fiscal, false, "Pendência", "joao");

        var history = await HistoryAsync(contract.Key);
        Assert.Equal(2, history.Count);
        Assert.Contains(history, h => h.Released && h.Reason == "Liberado" && h.ChangedBy == "maria");
        Assert.Contains(history, h => !h.Released && h.Reason == "Pendência" && h.ChangedBy == "joao");
        Assert.Equal("Pendência", Assert.Single(await RowsAsync(contract.Key)).Reason);
    }

    [Fact]
    public async Task Unchanged_IsNoOp_NoHistory()
    {
        var contract = await SeedAsync();
        var service = Service(PaymentReleaseDepartment.Fiscal);
        await service.ExecuteAsync(contract.Key, PaymentReleaseDepartment.Fiscal, true, "Um", "maria");

        await service.ExecuteAsync(contract.Key, PaymentReleaseDepartment.Fiscal, true, "Dois", "joao");

        Assert.Single(await HistoryAsync(contract.Key));
        Assert.Equal("Um", Assert.Single(await RowsAsync(contract.Key)).Reason);
        Assert.Single(await LogsAsync(contract.Key));
    }

    [Fact]
    public async Task Release_WithoutPermission_Throws()
    {
        var contract = await SeedAsync();

        // Permissão de outro departamento não vale.
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(PaymentReleaseDepartment.Legal).ExecuteAsync(contract.Key, PaymentReleaseDepartment.Fiscal, true, "Motivo teste", "joao"));

        Assert.Equal("Você não tem permissão para a Lib. Fiscal.", ex.Message);
        Assert.Empty(await RowsAsync(contract.Key));
        Assert.Empty(await LogsAsync(contract.Key));
    }

    [Fact]
    public async Task Release_CreatesRow_WithUserAndDate()
    {
        var contract = await SeedAsync();
        var before = DateTime.Now.AddMinutes(-1);

        await Service(PaymentReleaseDepartment.Fiscal).ExecuteAsync(contract.Key, PaymentReleaseDepartment.Fiscal, true, "Motivo teste", "maria");

        var row = Assert.Single(await RowsAsync(contract.Key));
        Assert.Equal(PaymentReleaseDepartment.Fiscal, row.Department);
        Assert.True(row.Released);
        Assert.Equal("maria", row.ReleasedBy);
        Assert.True(row.ReleasedAt >= before);

        var log = Assert.Single(await LogsAsync(contract.Key));
        Assert.Equal(ContractChangeLogFields.DepartmentRelease, log.Field);
        Assert.Equal("Lib. Fiscal: Não", log.OldValue);
        Assert.Equal("Lib. Fiscal: Sim (motivo: Motivo teste)", log.NewValue);
        Assert.Equal("maria", log.ChangedBy);
    }

    [Fact]
    public async Task Unrelease_ClearsRow()
    {
        var contract = await SeedAsync();
        var service = Service(PaymentReleaseDepartment.Stock);
        await service.ExecuteAsync(contract.Key, PaymentReleaseDepartment.Stock, true, "Motivo teste", "maria");

        await service.ExecuteAsync(contract.Key, PaymentReleaseDepartment.Stock, false, "Motivo teste", "joao");

        var row = Assert.Single(await RowsAsync(contract.Key));
        Assert.False(row.Released);
        Assert.Null(row.ReleasedAt);
        Assert.Null(row.ReleasedBy);

        var logs = await LogsAsync(contract.Key);
        Assert.Equal(2, logs.Count);
        Assert.Contains(logs, l => l.OldValue == "Lib. Estoque: Sim" && l.NewValue == "Lib. Estoque: Não (motivo: Motivo teste)");
    }

    [Fact]
    public async Task Unrelease_NeverReleased_IsNoOp_NoLog()
    {
        var contract = await SeedAsync();

        await Service(PaymentReleaseDepartment.Board).ExecuteAsync(contract.Key, PaymentReleaseDepartment.Board, false, "Motivo teste", "joao");

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
            Service(PaymentReleaseDepartment.Store).ExecuteAsync(contract.Key, PaymentReleaseDepartment.Store, false, "Motivo teste", "joao"));

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

        await Service(PaymentReleaseDepartment.Store).ExecuteAsync(contract.Key, PaymentReleaseDepartment.Store, true, "Motivo teste", "joao");

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

        await Service(PaymentReleaseDepartment.Legal).ExecuteAsync(contract.Key, PaymentReleaseDepartment.Legal, true, "Motivo teste", "joao");

        Assert.True(Assert.Single(await RowsAsync(contract.Key)).Released);
    }

    [Fact]
    public async Task Release_MissingContract_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service(PaymentReleaseDepartment.Legal).ExecuteAsync(Guid.NewGuid(), PaymentReleaseDepartment.Legal, true, "Motivo teste", "joao"));
    }
}
