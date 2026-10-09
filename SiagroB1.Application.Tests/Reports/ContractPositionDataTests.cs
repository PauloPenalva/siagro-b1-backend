using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using static SiagroB1.Application.Tests.Support.ContractPositionSeed;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// O saldo do relatório TEM de ser o AvaiableVolume do domínio depois de um recálculo. O Reports
/// não referencia o Application, então a soma é refeita lá em lote; estes testes rodam as duas
/// contas sobre o mesmo dado e exigem o mesmo número.
/// </summary>
public class ContractPositionDataTests
{
    private sealed class Request : ContractPositionReportRequest;

    // Devolução negativa, washout em todos os status, persistido defasado.
    [Fact]
    public async Task PurchaseBalance_EqualsTheDomainAfterARecalc()
    {
        var db = TestDb.CreateUnitOfWork();
        var plain = Purchase("PC000001", total: 100_000m, staleAllocated: 999m, staleWashedOut: 999m);
        var withReturn = Purchase("PC000002", total: 50_000m);
        var withWashouts = Purchase("PC000003", total: 80_000m, type: ContractType.ToBeDetermined);
        var untouched = Purchase("PC000004", total: 10_000m);
        var overDelivered = Purchase("PC000005", total: 1_000m);
        db.Context.PurchaseContracts.AddRange(plain, withReturn, withWashouts, untouched, overDelivered);
        db.Context.PurchaseContractsAllocations.AddRange(
            PurchaseAllocation(plain, 30_000.125m),
            PurchaseAllocation(plain, 12_345.678m),
            PurchaseAllocation(withReturn, 20_000m),
            PurchaseAllocation(withReturn, -5_000m), // devolução (tipo 9) entra negativa
            PurchaseAllocation(withReturn, 0m),      // tipos 10/11 gravam 0
            PurchaseAllocation(withWashouts, 10_000m),
            PurchaseAllocation(overDelivered, 1_200m));
        db.Context.PurchaseContractsWashouts.AddRange(
            Washout(withWashouts, 1, 5_000m, 1_000m, PurchaseContractWashoutStatus.Approved),
            Washout(withWashouts, 2, 0m, 2_000m, PurchaseContractWashoutStatus.InApproval),
            Washout(withWashouts, 3, 7_000m, 0m, PurchaseContractWashoutStatus.Rejected),
            Washout(withWashouts, 4, 3_000m, 0m, PurchaseContractWashoutStatus.Reversed));
        await Save(db);

        var positions = await ContractPositionData.PurchasePositionsAsync(
            db.Context, ContractPositionData.PurchaseQuery(db.Context, new Request()));

        Assert.Equal(5, positions.Count);
        foreach (var position in positions)
        {
            var contract = await db.Context.PurchaseContracts.SingleAsync(c => c.Key == position.Key);
            contract.AllocatedVolume = await PurchaseContractsRecalculateBalanceService.CalculateAllocatedAsync(db.Context, contract.Key);
            contract.WashedOutVolume = (await PurchaseContractsWashedOutVolumeService.ActiveVolumesAsync(db.Context, contract.Key)).Total;

            Assert.Equal(contract.AllocatedVolume, position.Delivered);
            Assert.Equal(contract.WashedOutVolume, position.WashedOut);
            Assert.Equal(contract.AvaiableVolume, position.Balance);
        }

        Assert.Equal(15_000m, positions.Single(p => p.Code == "PC000002").Delivered);
        Assert.Equal(8_000m, positions.Single(p => p.Code == "PC000003").WashedOut);
        Assert.Equal(62_000m, positions.Single(p => p.Code == "PC000003").Balance);
        Assert.Equal(-200m, positions.Single(p => p.Code == "PC000005").Balance);
    }

    // Quebra de entrega só da linha dona e só com o item conferido.
    [Fact]
    public async Task SalesBalance_EqualsTheDomainAfterARecalc()
    {
        var db = TestDb.CreateUnitOfWork();
        var owner = Sales("CV000001", total: 1_000m, staleAllocated: 999m);
        var partner = Sales("CV000002", total: 1_000m);
        var open = Sales("CV000003", total: 1_000m);
        var returned = Sales("CV000004", total: 1_000m);
        var closedItem = SalesItem(100m, closed: true, delivered: 92m, loss: 2.5m);
        var openItem = SalesItem(300m, delivered: 250m);
        var returnedItem = SalesItem(400m, closed: true, delivered: 400m);
        db.Context.SalesContracts.AddRange(owner, partner, open, returned);
        db.Context.SalesInvoicesItems.AddRange(closedItem, openItem, returnedItem);
        db.Context.SalesContractsAllocations.AddRange(
            SalesAllocation(owner, closedItem, 60m, owner: true),
            SalesAllocation(partner, closedItem, 40m, owner: false),
            SalesAllocation(open, openItem, 300m),
            SalesAllocation(returned, returnedItem, 400m),
            SalesAllocation(returned, returnedItem, -150m, owner: false)); // devolução
        await Save(db);

        var positions = await ContractPositionData.SalesPositionsAsync(
            db.Context, ContractPositionData.SalesQuery(db.Context, new Request()));

        Assert.Equal(4, positions.Count);
        foreach (var position in positions)
        {
            var contract = await db.Context.SalesContracts.SingleAsync(c => c.Key == position.Key);
            contract.AllocatedVolume = await SalesContractsRecalculateBalanceService.CalculateAllocatedAsync(db.Context, contract.Key);

            Assert.Equal(contract.AllocatedVolume, position.Delivered);
            Assert.Equal(contract.AvaiableVolume, position.Balance);
            Assert.Equal(0m, position.WashedOut);
        }

        Assert.Equal(49.5m, positions.Single(p => p.Code == "CV000001").Delivered); // 60 − (100 − 89,5)
        Assert.Equal(40m, positions.Single(p => p.Code == "CV000002").Delivered);
        Assert.Equal(300m, positions.Single(p => p.Code == "CV000003").Delivered);  // aberto: nominal
        Assert.Equal(250m, positions.Single(p => p.Code == "CV000004").Delivered);
    }

    [Fact]
    public async Task Positions_CarryTheContractSnapshots()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.Add(Purchase("PC000001", price: 120.50m, cashFlow: new DateTime(2026, 11, 10),
            type: ContractType.ToBeDetermined));
        db.Context.SalesContracts.Add(Sales("CV000001", price: 130.25m));
        await Save(db);

        var purchase = (await ContractPositionData.PurchasePositionsAsync(
            db.Context, ContractPositionData.PurchaseQuery(db.Context, new Request()))).Single();
        var sale = (await ContractPositionData.SalesPositionsAsync(
            db.Context, ContractPositionData.SalesQuery(db.Context, new Request()))).Single();

        Assert.Equal(ContractPositionSide.Purchase, purchase.Side);
        Assert.Equal(120.50m, purchase.Price);
        Assert.Equal(new DateTime(2026, 11, 10), purchase.StandardCashFlowDate);
        Assert.Equal(ContractType.ToBeDetermined, purchase.Type);
        Assert.Equal("PRODUTOR RURAL", purchase.CardName);
        Assert.Equal("25/26", purchase.HarvestSeasonCode);
        Assert.Equal(100_000m, purchase.Balance);
        Assert.Equal(ContractPositionSide.Sales, sale.Side);
        Assert.Equal(130.25m, sale.Price);
        Assert.Equal("COOPERATIVA CENTRAL", sale.CardName);
    }

    [Fact]
    public async Task Query_DefaultStatusIsOnlyApproved()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.AddRange(
            Purchase("PC000001", status: ContractStatus.Approved),
            Purchase("PC000002", status: ContractStatus.Draft),
            Purchase("PC000003", status: null),
            Purchase("PC000004", status: ContractStatus.Finished),
            Purchase("PC000005", status: ContractStatus.Canceled));
        await Save(db);

        Assert.Equal(new[] { "PC000001" }, await PurchaseCodes(db, new Request()));
        Assert.Equal(
            new[] { "PC000002", "PC000003" },
            await PurchaseCodes(db, new Request { Statuses = [ContractStatus.Draft] }));
        Assert.Equal(
            new[] { "PC000001", "PC000004" },
            await PurchaseCodes(db, new Request { Statuses = [ContractStatus.Approved, ContractStatus.Finished] }));
    }

    [Theory]
    [InlineData("BranchCode")]
    [InlineData("ItemCode")]
    [InlineData("HarvestSeasonCode")]
    [InlineData("CardCode")]
    [InlineData("Type")]
    [InlineData("DeliveryEndDateUntil")]
    public async Task Query_EachOptionalFilterRestrictsBothSides(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.AddRange(
            Purchase("PC000001", deliveryEnd: new DateTime(2026, 11, 30)),
            Purchase("PC000002", branchCode: "99", itemCode: "20001", itemName: "MILHO", harvest: "26/27",
                cardCode: "F999", type: ContractType.ToBeDetermined, deliveryEnd: new DateTime(2026, 12, 1)));
        db.Context.SalesContracts.AddRange(
            Sales("CV000001", cardCode: "F001", deliveryEnd: new DateTime(2026, 11, 30, 18, 0, 0)),
            Sales("CV000002", branchCode: "99", itemCode: "20001", itemName: "MILHO", harvest: "26/27",
                cardCode: "C999", type: ContractType.ToBeDetermined, deliveryEnd: new DateTime(2026, 12, 1)));
        await Save(db);

        var request = new Request();
        switch (filter)
        {
            case "BranchCode": request.BranchCode = "01"; break;
            case "ItemCode": request.ItemCode = "10001"; break;
            case "HarvestSeasonCode": request.HarvestSeasonCode = "25/26"; break;
            case "CardCode": request.CardCode = "F001"; break;
            case "Type": request.Type = ContractType.Fixed; break;
            case "DeliveryEndDateUntil": request.DeliveryEndDateUntil = new DateTime(2026, 11, 30); break;
        }

        Assert.Equal(new[] { "PC000001" }, await PurchaseCodes(db, request));
        Assert.Equal(new[] { "CV000001" }, await SalesCodes(db, request));
    }

    // Sem prazo não "termina até" data nenhuma.
    [Fact]
    public async Task Query_DeliveryEndUntilLeavesContractsWithoutDeadlineOut()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.AddRange(
            Purchase("PC000001", deliveryEnd: new DateTime(2026, 11, 30)),
            Purchase("PC000002", deliveryEnd: new DateTime(1900, 1, 1)));
        await Save(db);

        Assert.Equal(new[] { "PC000001", "PC000002" }, await PurchaseCodes(db, new Request()));
        Assert.Equal(
            new[] { "PC000001" },
            await PurchaseCodes(db, new Request { DeliveryEndDateUntil = new DateTime(2026, 12, 31) }));
    }

    /// <summary>
    /// O InMemory aceita LINQ que o SQL Server recusa (500 só em produção). Aqui as consultas
    /// passam pelo tradutor do SQL Server sem conexão: tem de sair GROUP BY e IN (SELECT …).
    /// </summary>
    [Fact]
    public void Queries_TranslateToSqlServer()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options);
        var request = new Request
        {
            Statuses = [ContractStatus.Draft, ContractStatus.Approved],
            BranchCode = "01",
            ItemCode = "10001",
            HarvestSeasonCode = "25/26",
            CardCode = "F001",
            Type = ContractType.Fixed,
            DeliveryEndDateUntil = new DateTime(2026, 12, 31),
        };
        var purchaseKeys = ContractPositionData.PurchaseQuery(context, request).Select(c => c.Key);
        var salesKeys = ContractPositionData.SalesQuery(context, request).Select(c => c.Key);

        var sql = new[]
        {
            ContractPositionData.PurchaseQuery(context, request).ToQueryString(),
            ContractPositionData.SalesQuery(context, request).ToQueryString(),
            ContractPositionData.PurchaseDeliveredQuery(context, purchaseKeys).ToQueryString(),
            ContractPositionData.PurchaseWashedOutQuery(context, purchaseKeys).ToQueryString(),
            ContractPositionData.SalesNominalQuery(context, salesKeys).ToQueryString(),
            ContractPositionData.SalesShortageQuery(context, salesKeys).ToQueryString(),
        };

        Assert.All(sql[2..], s => Assert.Contains("GROUP BY", s, StringComparison.OrdinalIgnoreCase));
        // O tradutor quebra a linha depois do "IN (": aceita espaço em branco antes do SELECT.
        Assert.All(sql[2..], s => Assert.Matches(@"IN \(\s*SELECT", s));
        Assert.Contains("IS NULL", sql[0], StringComparison.OrdinalIgnoreCase); // Draft = situação nula
        Assert.Contains("[SALES_INVOICES_ITEMS]", sql[5]);
        Assert.DoesNotContain(sql, s => s.Contains("[BUSINESS_PARTNERS]") || s.Contains("[ITEMS]"));
    }

    private static async Task<string[]> PurchaseCodes(IUnitOfWork db, ContractPositionReportRequest request) =>
        (await ContractPositionData.PurchaseQuery(db.Context, request).Select(c => c.Code!).ToListAsync())
        .Order(StringComparer.Ordinal).ToArray();

    private static async Task<string[]> SalesCodes(IUnitOfWork db, ContractPositionReportRequest request) =>
        (await ContractPositionData.SalesQuery(db.Context, request).Select(c => c.Code!).ToListAsync())
        .Order(StringComparer.Ordinal).ToArray();

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
