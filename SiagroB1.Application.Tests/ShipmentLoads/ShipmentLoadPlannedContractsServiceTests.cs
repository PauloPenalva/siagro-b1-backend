using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1185: contratos planejados da carga — vínculo informativo, sem efeito em saldo. As FKs
/// são NoAction, então os testes conferem que as linhas SOMEM junto com a carga ou o contrato.
/// </summary>
public class ShipmentLoadPlannedContractsServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsPlannedContractAddService Add() => new(
        _db.Context, new ShipmentLoadsChangeLogService(_db.Context),
        NullLogger<ShipmentLoadsPlannedContractAddService>.Instance);

    private ShipmentLoadsPlannedContractRemoveService Remove() => new(
        _db.Context, new ShipmentLoadsChangeLogService(_db.Context),
        NullLogger<ShipmentLoadsPlannedContractRemoveService>.Instance);

    private async Task<ShipmentLoad> SeedLoadAsync(ShipmentLoadStatus status = ShipmentLoadStatus.Planned)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(), Code = "CG000001", BranchCode = "01", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", TruckCode = "ABC1D23", WarehouseCode = "ARM01", Status = status,
        };
        _db.Context.ShipmentLoads.Add(load);
        await _db.Context.SaveChangesAsync();
        return load;
    }

    private async Task<PurchaseContract> SeedPurchaseAsync(
        ContractStatus status = ContractStatus.Approved, string item = "SOJA", string code = "CC000001")
    {
        var c = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = code, CardCode = "F0001", ItemCode = item, UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25", DeliveryLocationCode = "01", TotalVolume = 1_000m,
            Type = ContractType.Fixed, StandardCashFlowDate = new DateTime(2026, 12, 31),
            StandardCurrency = CurrencyType.Brl, Status = status,
        };
        _db.Context.PurchaseContracts.Add(c);
        await _db.Context.SaveChangesAsync();
        return c;
    }

    private async Task<SalesContract> SeedSalesAsync(
        ContractStatus status = ContractStatus.Approved, string item = "SOJA", string code = "CV000001")
    {
        var c = new SalesContract
        {
            Key = Guid.NewGuid(), Code = code, CardCode = "C0001", ItemCode = item, UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25", TotalVolume = 1_000m, Price = 3m, Type = ContractType.Fixed,
            StandardCashFlowDate = new DateTime(2026, 12, 31), StandardCurrency = CurrencyType.Brl, Status = status,
        };
        _db.Context.SalesContracts.Add(c);
        await _db.Context.SaveChangesAsync();
        return c;
    }

    private List<ShipmentLoadChangeLog> LogsOf(Guid loadKey) =>
        _db.Context.ShipmentLoadsChangeLogs.Where(l => l.ShipmentLoadKey == loadKey).ToList();

    [Fact]
    public async Task Adds_a_purchase_contract_and_logs_it()
    {
        var load = await SeedLoadAsync();
        var contract = await SeedPurchaseAsync();

        await Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, contract.Key, 500m, "  obs  ", "joao");

        var row = Assert.Single(_db.Context.ShipmentLoadsPlannedPurchaseContracts);
        Assert.Equal(load.Key, row.ShipmentLoadKey);
        Assert.Equal(contract.Key, row.ContractKey);
        Assert.Equal(500m, row.PlannedQuantity);
        Assert.Equal("obs", row.Notes);
        Assert.Equal("joao", row.CreatedBy);

        var log = Assert.Single(LogsOf(load.Key));
        Assert.Equal(ShipmentLoadChangeLogFields.PlannedPurchaseContract, log.Field);
        Assert.Null(log.OldValue);
        Assert.Contains("CC000001", log.NewValue);
    }

    [Fact]
    public async Task Adds_a_sales_contract_without_quantity_and_logs_it()
    {
        var load = await SeedLoadAsync();
        var contract = await SeedSalesAsync();

        await Add().ExecuteAsync(load.Key, PlannedContractType.Sales, contract.Key, null, null, "joao");

        var row = Assert.Single(_db.Context.ShipmentLoadsPlannedSalesContracts);
        Assert.Null(row.PlannedQuantity);
        Assert.Empty(_db.Context.ShipmentLoadsPlannedPurchaseContracts);

        var log = Assert.Single(LogsOf(load.Key));
        Assert.Equal(ShipmentLoadChangeLogFields.PlannedSalesContract, log.Field);
        Assert.Contains("CV000001", log.NewValue);
    }

    [Fact]
    public async Task Rejects_a_contract_that_is_not_approved()
    {
        var load = await SeedLoadAsync();
        var contract = await SeedPurchaseAsync(ContractStatus.Draft);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, contract.Key, null, null, "joao"));

        Assert.Equal("O contrato CC000001 não está aprovado.", ex.Message);
        Assert.Empty(_db.Context.ShipmentLoadsPlannedPurchaseContracts);
    }

    [Fact]
    public async Task Rejects_a_contract_of_another_item()
    {
        var load = await SeedLoadAsync();
        var contract = await SeedSalesAsync(item: "MILHO");

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Add().ExecuteAsync(load.Key, PlannedContractType.Sales, contract.Key, null, null, "joao"));

        Assert.Equal("O contrato CV000001 é de outro produto.", ex.Message);
    }

    [Fact]
    public async Task Rejects_a_duplicate_contract_on_the_same_load()
    {
        var load = await SeedLoadAsync();
        var contract = await SeedPurchaseAsync();
        await Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, contract.Key, null, null, "joao");

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, contract.Key, null, null, "joao"));

        Assert.Equal("O contrato CC000001 já está planejado nesta carga.", ex.Message);
        Assert.Single(_db.Context.ShipmentLoadsPlannedPurchaseContracts);
    }

    [Fact]
    public async Task Rejects_a_cancelled_load()
    {
        var load = await SeedLoadAsync(ShipmentLoadStatus.Cancelled);
        var contract = await SeedPurchaseAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, contract.Key, null, null, "joao"));

        Assert.Equal("A carga CG000001 está cancelada.", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Rejects_a_quantity_that_is_not_positive(int quantity)
    {
        var load = await SeedLoadAsync();
        var contract = await SeedPurchaseAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, contract.Key, quantity, null, "joao"));

        Assert.Equal("A quantidade planejada deve ser maior que zero.", ex.Message);
    }

    [Fact]
    public async Task Reports_a_missing_load_and_a_missing_contract()
    {
        var load = await SeedLoadAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Add().ExecuteAsync(Guid.NewGuid(), PlannedContractType.Purchase, Guid.NewGuid(), null, null, "joao"));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Add().ExecuteAsync(load.Key, PlannedContractType.Sales, Guid.NewGuid(), null, null, "joao"));
    }

    [Fact]
    public async Task Removes_a_planned_contract_and_logs_it()
    {
        var load = await SeedLoadAsync();
        var purchase = await SeedPurchaseAsync();
        var sales = await SeedSalesAsync();
        await Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, purchase.Key, null, null, "joao");
        await Add().ExecuteAsync(load.Key, PlannedContractType.Sales, sales.Key, null, null, "joao");

        var purchaseRow = _db.Context.ShipmentLoadsPlannedPurchaseContracts.Single();
        var salesRow = _db.Context.ShipmentLoadsPlannedSalesContracts.Single();

        await Remove().ExecuteAsync(PlannedContractType.Purchase, purchaseRow.Key!.Value, "maria");
        await Remove().ExecuteAsync(PlannedContractType.Sales, salesRow.Key!.Value, "maria");

        Assert.Empty(_db.Context.ShipmentLoadsPlannedPurchaseContracts);
        Assert.Empty(_db.Context.ShipmentLoadsPlannedSalesContracts);

        var removals = LogsOf(load.Key).Where(l => l.ChangedBy == "maria").ToList();
        Assert.Equal(2, removals.Count);
        Assert.All(removals, l => Assert.Null(l.NewValue));
        Assert.Contains(removals, l => l.Field == ShipmentLoadChangeLogFields.PlannedPurchaseContract
                                       && l.OldValue!.Contains("CC000001"));
        Assert.Contains(removals, l => l.Field == ShipmentLoadChangeLogFields.PlannedSalesContract
                                       && l.OldValue!.Contains("CV000001"));
    }

    [Fact]
    public async Task Remove_rejects_a_cancelled_load()
    {
        var load = await SeedLoadAsync();
        var contract = await SeedPurchaseAsync();
        await Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, contract.Key, null, null, "joao");
        var row = _db.Context.ShipmentLoadsPlannedPurchaseContracts.Single();
        load.Status = ShipmentLoadStatus.Cancelled;
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Remove().ExecuteAsync(PlannedContractType.Purchase, row.Key!.Value, "maria"));

        Assert.Equal("A carga CG000001 está cancelada.", ex.Message);
        Assert.Single(_db.Context.ShipmentLoadsPlannedPurchaseContracts);
        Assert.Single(LogsOf(load.Key));
    }

    [Fact]
    public async Task Remove_reports_a_missing_row()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Remove().ExecuteAsync(PlannedContractType.Sales, Guid.NewGuid(), "maria"));
    }

    [Fact]
    public async Task Deleting_the_load_removes_its_planned_rows()
    {
        var load = await SeedLoadAsync();
        var purchase = await SeedPurchaseAsync();
        var sales = await SeedSalesAsync();
        await Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, purchase.Key, null, null, "joao");
        await Add().ExecuteAsync(load.Key, PlannedContractType.Sales, sales.Key, null, null, "joao");

        await new ShipmentLoadsDeleteService(
            _db, new ShipmentLoadsCompositionGuardService(_db.Context)).ExecuteAsync(load.Key);

        Assert.Empty(_db.Context.ShipmentLoadsPlannedPurchaseContracts);
        Assert.Empty(_db.Context.ShipmentLoadsPlannedSalesContracts);
        Assert.Empty(_db.Context.ShipmentLoads);
    }

    [Fact]
    public async Task Deleting_a_purchase_contract_removes_its_planned_rows_instead_of_blocking()
    {
        var load = await SeedLoadAsync();
        var draft = await SeedPurchaseAsync(ContractStatus.Approved);
        await Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, draft.Key, null, null, "joao");
        draft.Status = ContractStatus.Draft;
        await _db.Context.SaveChangesAsync();

        var service = new PurchaseContractsDeleteService(
            _db.Context, new FakeStringLocalizer<Resource>(),
            FinancialDocumentTestServices.Cancel(_db.Context),
            NullLogger<PurchaseContractsDeleteService>.Instance);

        Assert.True(await service.ExecuteAsync(draft.Key, "tester"));

        Assert.False(await _db.Context.PurchaseContracts.AnyAsync(x => x.Key == draft.Key));
        Assert.Empty(_db.Context.ShipmentLoadsPlannedPurchaseContracts);
    }

    [Fact]
    public async Task Deleting_a_sales_contract_removes_its_planned_rows_instead_of_blocking()
    {
        var load = await SeedLoadAsync();
        var draft = await SeedSalesAsync(ContractStatus.Approved);
        await Add().ExecuteAsync(load.Key, PlannedContractType.Sales, draft.Key, null, null, "joao");
        draft.Status = ContractStatus.Draft;
        await _db.Context.SaveChangesAsync();

        var service = new SalesContractsDeleteService(
            _db.Context, FinancialDocumentTestServices.Cancel(_db.Context),
            NullLogger<SalesContractsDeleteService>.Instance);

        Assert.True(await service.ExecuteAsync(draft.Key, "tester"));

        Assert.False(await _db.Context.SalesContracts.AnyAsync(x => x.Key == draft.Key));
        Assert.Empty(_db.Context.ShipmentLoadsPlannedSalesContracts);
    }
}
