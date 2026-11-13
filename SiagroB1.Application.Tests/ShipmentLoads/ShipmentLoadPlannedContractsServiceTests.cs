using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
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

    private async Task<ShipmentRelease> SeedPurchaseReleaseAsync(
        PurchaseContract contract, ReleaseStatus status = ReleaseStatus.Actived, int rowId = 1)
    {
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(), RowId = rowId, PurchaseContractKey = contract.Key,
            DeliveryLocationCode = "ARM01", ReleasedQuantity = 600m, Status = status,
        };
        _db.Context.ShipmentReleases.Add(release);
        await _db.Context.SaveChangesAsync();
        return release;
    }

    private async Task<SalesShipmentRelease> SeedSalesReleaseAsync(
        SalesContract contract, ReleaseStatus status = ReleaseStatus.Actived, int rowId = 1)
    {
        var release = new SalesShipmentRelease
        {
            Key = Guid.NewGuid(), RowId = rowId, SalesContractKey = contract.Key,
            DeliveryLocationCode = "ARM01", ReleasedQuantity = 600m, Status = status,
        };
        _db.Context.SalesShipmentReleases.Add(release);
        await _db.Context.SaveChangesAsync();
        return release;
    }

    /// <summary>Linha anterior à rodada 2: só o contrato, sem liberação.</summary>
    private async Task<ShipmentLoadPlannedPurchaseContract> SeedLegacyPurchaseRowAsync(
        ShipmentLoad load, PurchaseContract contract)
    {
        var row = new ShipmentLoadPlannedPurchaseContract
        {
            Key = Guid.NewGuid(), ShipmentLoadKey = load.Key, ContractKey = contract.Key, CreatedBy = "joao",
        };
        _db.Context.ShipmentLoadsPlannedPurchaseContracts.Add(row);
        await _db.Context.SaveChangesAsync();
        return row;
    }

    private async Task<ShipmentLoadPlannedSalesContract> SeedLegacySalesRowAsync(
        ShipmentLoad load, SalesContract contract)
    {
        var row = new ShipmentLoadPlannedSalesContract
        {
            Key = Guid.NewGuid(), ShipmentLoadKey = load.Key, ContractKey = contract.Key, CreatedBy = "joao",
        };
        _db.Context.ShipmentLoadsPlannedSalesContracts.Add(row);
        await _db.Context.SaveChangesAsync();
        return row;
    }

    private List<ShipmentLoadChangeLog> LogsOf(Guid loadKey) =>
        _db.Context.ShipmentLoadsChangeLogs.Where(l => l.ShipmentLoadKey == loadKey).ToList();

    [Fact]
    public async Task Adds_a_purchase_release_with_its_contract_and_logs_it()
    {
        var load = await SeedLoadAsync();
        var contract = await SeedPurchaseAsync();
        var release = await SeedPurchaseReleaseAsync(contract, rowId: 2);

        await Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, release.Key, 500m, "  obs  ", "joao");

        var row = Assert.Single(_db.Context.ShipmentLoadsPlannedPurchaseContracts);
        Assert.Equal(load.Key, row.ShipmentLoadKey);
        Assert.Equal(contract.Key, row.ContractKey);
        Assert.Equal(release.Key, row.ShipmentReleaseKey);
        Assert.Equal(500m, row.PlannedQuantity);
        Assert.Equal("obs", row.Notes);
        Assert.Equal("joao", row.CreatedBy);

        var log = Assert.Single(LogsOf(load.Key));
        Assert.Equal(ShipmentLoadChangeLogFields.PlannedPurchaseContract, log.Field);
        Assert.Null(log.OldValue);
        Assert.Equal("CC000001 (item 2) — 500,000", log.NewValue);
    }

    [Fact]
    public async Task Adds_a_sales_release_without_quantity_and_logs_it()
    {
        var load = await SeedLoadAsync();
        var contract = await SeedSalesAsync();
        var release = await SeedSalesReleaseAsync(contract, rowId: 3);

        await Add().ExecuteAsync(load.Key, PlannedContractType.Sales, release.Key, null, null, "joao");

        var row = Assert.Single(_db.Context.ShipmentLoadsPlannedSalesContracts);
        Assert.Equal(contract.Key, row.ContractKey);
        Assert.Equal(release.Key, row.SalesShipmentReleaseKey);
        Assert.Null(row.PlannedQuantity);
        Assert.Empty(_db.Context.ShipmentLoadsPlannedPurchaseContracts);

        var log = Assert.Single(LogsOf(load.Key));
        Assert.Equal(ShipmentLoadChangeLogFields.PlannedSalesContract, log.Field);
        Assert.Equal("CV000001 (item 3)", log.NewValue);
    }

    [Theory]
    [InlineData(ReleaseStatus.Pending)]
    [InlineData(ReleaseStatus.Paused)]
    [InlineData(ReleaseStatus.Completed)]
    [InlineData(ReleaseStatus.Cancelled)]
    public async Task Rejects_a_release_that_is_not_active(ReleaseStatus status)
    {
        var load = await SeedLoadAsync();
        var contract = await SeedPurchaseAsync();
        var release = await SeedPurchaseReleaseAsync(contract, status, rowId: 4);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, release.Key, null, null, "joao"));

        Assert.Equal("A liberação CC000001 (item 4) não está ativa.", ex.Message);
        Assert.Empty(_db.Context.ShipmentLoadsPlannedPurchaseContracts);
    }

    [Fact]
    public async Task Rejects_a_sales_release_that_is_not_active()
    {
        var load = await SeedLoadAsync();
        var contract = await SeedSalesAsync();
        var release = await SeedSalesReleaseAsync(contract, ReleaseStatus.Pending, rowId: 5);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Add().ExecuteAsync(load.Key, PlannedContractType.Sales, release.Key, null, null, "joao"));

        Assert.Equal("A liberação CV000001 (item 5) não está ativa.", ex.Message);
    }

    [Fact]
    public async Task Rejects_a_contract_that_is_not_approved()
    {
        var load = await SeedLoadAsync();
        var contract = await SeedPurchaseAsync(ContractStatus.Draft);
        var release = await SeedPurchaseReleaseAsync(contract);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, release.Key, null, null, "joao"));

        Assert.Equal("O contrato CC000001 não está aprovado.", ex.Message);
        Assert.Empty(_db.Context.ShipmentLoadsPlannedPurchaseContracts);
    }

    [Fact]
    public async Task Rejects_a_contract_of_another_item()
    {
        var load = await SeedLoadAsync();
        var contract = await SeedSalesAsync(item: "MILHO");
        var release = await SeedSalesReleaseAsync(contract);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Add().ExecuteAsync(load.Key, PlannedContractType.Sales, release.Key, null, null, "joao"));

        Assert.Equal("O contrato CV000001 é de outro produto.", ex.Message);
    }

    [Fact]
    public async Task Rejects_the_same_release_twice_on_the_same_load()
    {
        var load = await SeedLoadAsync();
        var contract = await SeedPurchaseAsync();
        var release = await SeedPurchaseReleaseAsync(contract, rowId: 2);
        await Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, release.Key, null, null, "joao");

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, release.Key, null, null, "joao"));

        Assert.Equal("A liberação CC000001 (item 2) já está planejada nesta carga.", ex.Message);
        Assert.Single(_db.Context.ShipmentLoadsPlannedPurchaseContracts);
    }

    [Fact]
    public async Task Rejects_the_same_sales_release_twice_on_the_same_load()
    {
        var load = await SeedLoadAsync();
        var contract = await SeedSalesAsync();
        var release = await SeedSalesReleaseAsync(contract, rowId: 2);
        await Add().ExecuteAsync(load.Key, PlannedContractType.Sales, release.Key, null, null, "joao");

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Add().ExecuteAsync(load.Key, PlannedContractType.Sales, release.Key, null, null, "joao"));

        Assert.Equal("A liberação CV000001 (item 2) já está planejada nesta carga.", ex.Message);
        Assert.Single(_db.Context.ShipmentLoadsPlannedSalesContracts);
    }

    /// <summary>O ponto da rodada 2: um contrato com várias liberações entra uma vez por liberação.</summary>
    [Fact]
    public async Task Accepts_two_releases_of_the_same_contract_on_the_same_load()
    {
        var load = await SeedLoadAsync();
        var contract = await SeedPurchaseAsync();
        var first = await SeedPurchaseReleaseAsync(contract, rowId: 1);
        var second = await SeedPurchaseReleaseAsync(contract, rowId: 2);

        await Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, first.Key, null, null, "joao");
        await Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, second.Key, null, null, "joao");

        var rows = _db.Context.ShipmentLoadsPlannedPurchaseContracts.ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(contract.Key, r.ContractKey));
        Assert.Equal(
            new[] { first.Key, second.Key }.OrderBy(k => k),
            rows.Select(r => r.ShipmentReleaseKey!.Value).OrderBy(k => k));
    }

    /// <summary>Linha antiga (só contrato) não impede planejar uma liberação do mesmo contrato.</summary>
    [Fact]
    public async Task Accepts_a_release_of_a_contract_that_has_a_legacy_row()
    {
        var load = await SeedLoadAsync();
        var contract = await SeedPurchaseAsync();
        await SeedLegacyPurchaseRowAsync(load, contract);
        var release = await SeedPurchaseReleaseAsync(contract);

        await Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, release.Key, null, null, "joao");

        Assert.Equal(2, _db.Context.ShipmentLoadsPlannedPurchaseContracts.Count());
    }

    [Fact]
    public async Task Rejects_a_cancelled_load()
    {
        var load = await SeedLoadAsync(ShipmentLoadStatus.Cancelled);
        var contract = await SeedPurchaseAsync();
        var release = await SeedPurchaseReleaseAsync(contract);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, release.Key, null, null, "joao"));

        Assert.Equal("A carga CG000001 está cancelada.", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Rejects_a_quantity_that_is_not_positive(int quantity)
    {
        var load = await SeedLoadAsync();
        var contract = await SeedPurchaseAsync();
        var release = await SeedPurchaseReleaseAsync(contract);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, release.Key, quantity, null, "joao"));

        Assert.Equal("A quantidade planejada deve ser maior que zero.", ex.Message);
    }

    [Fact]
    public async Task Reports_a_missing_load_and_a_missing_release()
    {
        var load = await SeedLoadAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Add().ExecuteAsync(Guid.NewGuid(), PlannedContractType.Purchase, Guid.NewGuid(), null, null, "joao"));

        var purchase = await Assert.ThrowsAsync<NotFoundException>(() =>
            Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, Guid.NewGuid(), null, null, "joao"));
        Assert.Equal("Liberação de embarque não encontrada.", purchase.Message);

        var sales = await Assert.ThrowsAsync<NotFoundException>(() =>
            Add().ExecuteAsync(load.Key, PlannedContractType.Sales, Guid.NewGuid(), null, null, "joao"));
        Assert.Equal("Liberação de entrega não encontrada.", sales.Message);
    }

    /// <summary>Chave de liberação de VENDA mandada como compra não pode achar nada.</summary>
    [Fact]
    public async Task Does_not_find_a_sales_release_when_the_type_is_purchase()
    {
        var load = await SeedLoadAsync();
        var release = await SeedSalesReleaseAsync(await SeedSalesAsync());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, release.Key, null, null, "joao"));
    }

    [Fact]
    public async Task Removes_a_planned_release_and_logs_it()
    {
        var load = await SeedLoadAsync();
        var purchase = await SeedPurchaseReleaseAsync(await SeedPurchaseAsync(), rowId: 2);
        var sales = await SeedSalesReleaseAsync(await SeedSalesAsync(), rowId: 3);
        await Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, purchase.Key, 500m, null, "joao");
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
                                       && l.OldValue == "CC000001 (item 2) — 500,000");
        Assert.Contains(removals, l => l.Field == ShipmentLoadChangeLogFields.PlannedSalesContract
                                       && l.OldValue == "CV000001 (item 3)");
    }

    /// <summary>Linha anterior à rodada 2 continua excluível, e o log sai só com o contrato.</summary>
    [Fact]
    public async Task Removes_a_legacy_row_that_has_no_release()
    {
        var load = await SeedLoadAsync();
        var purchaseRow = await SeedLegacyPurchaseRowAsync(load, await SeedPurchaseAsync());
        var salesRow = await SeedLegacySalesRowAsync(load, await SeedSalesAsync());

        await Remove().ExecuteAsync(PlannedContractType.Purchase, purchaseRow.Key!.Value, "maria");
        await Remove().ExecuteAsync(PlannedContractType.Sales, salesRow.Key!.Value, "maria");

        Assert.Empty(_db.Context.ShipmentLoadsPlannedPurchaseContracts);
        Assert.Empty(_db.Context.ShipmentLoadsPlannedSalesContracts);
        Assert.Equal(
            new[] { "CC000001", "CV000001" },
            LogsOf(load.Key).Select(l => l.OldValue).OrderBy(v => v));
    }

    [Fact]
    public async Task Remove_rejects_a_cancelled_load()
    {
        var load = await SeedLoadAsync();
        var release = await SeedPurchaseReleaseAsync(await SeedPurchaseAsync());
        await Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, release.Key, null, null, "joao");
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
        await SeedLegacyPurchaseRowAsync(load, purchase);
        await SeedLegacySalesRowAsync(load, sales);

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
        await SeedLegacyPurchaseRowAsync(load, draft);
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
        await SeedLegacySalesRowAsync(load, draft);
        draft.Status = ContractStatus.Draft;
        await _db.Context.SaveChangesAsync();

        var service = new SalesContractsDeleteService(
            _db.Context, FinancialDocumentTestServices.Cancel(_db.Context),
            NullLogger<SalesContractsDeleteService>.Instance);

        Assert.True(await service.ExecuteAsync(draft.Key, "tester"));

        Assert.False(await _db.Context.SalesContracts.AnyAsync(x => x.Key == draft.Key));
        Assert.Empty(_db.Context.ShipmentLoadsPlannedSalesContracts);
    }

    /// <summary>
    /// Só liberação Pendente pode ser excluída, e só Ativa pode ser planejada — mas a liberação
    /// planejada pode voltar a Pendente depois. A FK é NoAction: sem limpar a linha planejada, a
    /// exclusão estouraria 547 no SQL Server.
    /// </summary>
    [Fact]
    public async Task Deleting_a_purchase_release_removes_its_planned_rows()
    {
        var load = await SeedLoadAsync();
        var contract = await SeedPurchaseAsync();
        var release = await SeedPurchaseReleaseAsync(contract);
        var other = await SeedPurchaseReleaseAsync(contract, rowId: 2);
        await Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, release.Key, null, null, "joao");
        await Add().ExecuteAsync(load.Key, PlannedContractType.Purchase, other.Key, null, null, "joao");
        release.Status = ReleaseStatus.Pending;
        await _db.Context.SaveChangesAsync();

        Assert.True(await new ShipmentReleasesDeleteService(
            _db, NullLogger<ShipmentReleasesDeleteService>.Instance).ExecuteAsync(release.Key));

        Assert.False(await _db.Context.ShipmentReleases.AnyAsync(x => x.Key == release.Key));
        var remaining = Assert.Single(_db.Context.ShipmentLoadsPlannedPurchaseContracts);
        Assert.Equal(other.Key, remaining.ShipmentReleaseKey);
    }

    [Fact]
    public async Task Deleting_a_sales_release_removes_its_planned_rows()
    {
        var load = await SeedLoadAsync();
        var contract = await SeedSalesAsync();
        var release = await SeedSalesReleaseAsync(contract);
        var other = await SeedSalesReleaseAsync(contract, rowId: 2);
        await Add().ExecuteAsync(load.Key, PlannedContractType.Sales, release.Key, null, null, "joao");
        await Add().ExecuteAsync(load.Key, PlannedContractType.Sales, other.Key, null, null, "joao");
        release.Status = ReleaseStatus.Pending;
        await _db.Context.SaveChangesAsync();

        Assert.True(await new SalesShipmentReleasesDeleteService(
            _db, NullLogger<SalesShipmentReleasesDeleteService>.Instance).ExecuteAsync(release.Key));

        Assert.False(await _db.Context.SalesShipmentReleases.AnyAsync(x => x.Key == release.Key));
        var remaining = Assert.Single(_db.Context.ShipmentLoadsPlannedSalesContracts);
        Assert.Equal(other.Key, remaining.SalesShipmentReleaseKey);
    }
}
