using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.ContractPositionSeed;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>"Hoje" nestes testes é <see cref="ContractPositionSeed.Today"/> = 08/10/2026.</summary>
public class ContractMonthlyPositionReportServiceTests
{
    [Fact]
    public async Task BuildRows_OverdueThenMonthsThenNoDeadline()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.AddRange(
            Purchase("PC000001", total: 1_000m, deliveryEnd: new DateTime(2026, 10, 7)),   // ontem: vencido
            Purchase("PC000002", total: 2_000m, deliveryEnd: new DateTime(2026, 10, 8)),   // hoje: 10/2026
            Purchase("PC000003", total: 4_000m, deliveryEnd: new DateTime(2026, 12, 15)),
            Purchase("PC000004", total: 8_000m, deliveryEnd: new DateTime(2026, 6, 30)),   // vencido
            Purchase("PC000005", total: 16_000m, deliveryEnd: DateTime.MinValue));                  // sem prazo
        db.Context.SalesContracts.AddRange(
            Sales("CV000001", total: 500m, deliveryEnd: new DateTime(2026, 12, 1)),
            Sales("CV000002", total: 300m, deliveryEnd: new DateTime(2027, 1, 31)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(new ContractMonthlyPositionRequest(), Today);

        Assert.Equal(new[] { "Vencido", "10/2026", "12/2026", "01/2027", "Sem prazo" }, rows.Select(r => r.Bucket));
        Assert.Equal(new[] { 9_000m, 2_000m, 4_000m, 0m, 16_000m }, rows.Select(r => r.PurchaseQuantity));
        Assert.Equal(new[] { 0m, 0m, 500m, 300m, 0m }, rows.Select(r => r.SalesQuantity));
        Assert.Equal(new[] { 9_000m, 2_000m, 3_500m, -300m, 16_000m }, rows.Select(r => r.NetQuantity));
        Assert.Equal(new[] { 9_000m, 11_000m, 14_500m, 14_200m, 30_200m }, rows.Select(r => r.AccumulatedQuantity));
    }

    // Contratos inseridos FORA da ordem dos meses: só o OrderBy do serviço coloca 11/2026 antes de 12/2026 e 01/2027.
    [Fact]
    public async Task BuildRows_MonthsComeOutInDateOrderWhateverTheInsertionOrder()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.AddRange(
            Purchase("PC000001", total: 100m, deliveryEnd: new DateTime(2027, 1, 15)),
            Purchase("PC000002", total: 200m, deliveryEnd: new DateTime(2026, 12, 15)),
            Purchase("PC000003", total: 400m, deliveryEnd: new DateTime(2026, 11, 15)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(new ContractMonthlyPositionRequest(), Today);

        Assert.Equal(new[] { "Vencido", "11/2026", "12/2026", "01/2027" }, rows.Select(r => r.Bucket));
        Assert.Equal(new[] { 0m, 400m, 600m, 700m }, rows.Select(r => r.AccumulatedQuantity));
    }

    [Fact]
    public async Task BuildRows_UomCaseAndTrailingSpaceDoNotSplitTheGroup()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.AddRange(
            Purchase("PC000001", total: 100m, uom: "KG", deliveryEnd: new DateTime(2026, 11, 15)),
            Purchase("PC000002", total: 50m, uom: "kg ", deliveryEnd: new DateTime(2026, 11, 20)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(new ContractMonthlyPositionRequest(), Today);

        Assert.Single(rows.Select(r => r.Group).Distinct());
        Assert.Equal(150m, rows.Single(r => r.Bucket == "11/2026").PurchaseQuantity);
    }

    [Fact]
    public async Task BuildRows_OverdueRowAlwaysOpensTheGroup()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesContracts.Add(Sales("CV000001", total: 500m, deliveryEnd: new DateTime(2026, 11, 30)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(new ContractMonthlyPositionRequest(), Today);

        Assert.Equal(new[] { "Vencido", "11/2026" }, rows.Select(r => r.Bucket));
        Assert.Equal(new[] { 0m, -500m }, rows.Select(r => r.AccumulatedQuantity));
    }

    [Fact]
    public async Task BuildRows_GroupHeaderCarriesContractedDeliveredAndWashout()
    {
        var db = TestDb.CreateUnitOfWork();
        var purchase = Purchase("PC000001", total: 100_000m, deliveryEnd: new DateTime(2026, 11, 30));
        var sale = Sales("CV000001", total: 60_000m, deliveryEnd: new DateTime(2026, 11, 30));
        var item = SalesItem(20_000m);
        db.Context.PurchaseContracts.Add(purchase);
        db.Context.SalesContracts.Add(sale);
        db.Context.SalesInvoicesItems.Add(item);
        db.Context.PurchaseContractsAllocations.Add(PurchaseAllocation(purchase, 30_000m));
        db.Context.PurchaseContractsWashouts.Add(Washout(purchase, 1, 10_000m));
        db.Context.SalesContractsAllocations.Add(SalesAllocation(sale, item, 20_000m));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(new ContractMonthlyPositionRequest(), Today);

        Assert.All(rows, r =>
        {
            Assert.Equal("SOJA EM GRÃOS (10001) - Safra 25/26 - KG", r.Group);
            Assert.Equal(100_000m, r.ContractedPurchase);
            Assert.Equal(60_000m, r.ContractedSales);
            Assert.Equal(40_000m, r.ContractedNet);
            Assert.Equal(30_000m, r.DeliveredPurchase);
            Assert.Equal(20_000m, r.DeliveredSales);
            Assert.Equal(10_000m, r.DeliveredNet);
            Assert.Equal(10_000m, r.WashedOutPurchase);
        });
        var november = rows.Single(r => r.Bucket == "11/2026");
        Assert.Equal(60_000m, november.PurchaseQuantity); // 100.000 − 30.000 − 10.000
        Assert.Equal(40_000m, november.SalesQuantity);    // 60.000 − 20.000
        Assert.Equal(20_000m, november.NetQuantity);
    }

    [Fact]
    public async Task BuildRows_GroupsByProductHarvestAndUnit()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.AddRange(
            Purchase("PC000001"),
            Purchase("PC000002", harvest: "26/27"),
            Purchase("PC000003", uom: "TN", total: 100m),
            Purchase("PC000004", itemCode: "20001", itemName: "MILHO"));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(new ContractMonthlyPositionRequest(), Today);

        Assert.Equal(
            new[]
            {
                "MILHO (20001) - Safra 25/26 - KG",
                "SOJA EM GRÃOS (10001) - Safra 25/26 - KG",
                "SOJA EM GRÃOS (10001) - Safra 25/26 - TN",
                "SOJA EM GRÃOS (10001) - Safra 26/27 - KG",
            },
            rows.Select(r => r.Group).Distinct());
    }

    // Saldo negativo (entregue além do contratado) não entra no mês: só saldos positivos somam.
    [Fact]
    public async Task BuildRows_NegativeBalanceIsLeftOutOfTheMonth()
    {
        var db = TestDb.CreateUnitOfWork();
        var sale = Sales("CV000001", total: 100m, deliveryEnd: new DateTime(2026, 11, 30));
        var item = SalesItem(130m);
        var purchase = Purchase("PC000001", total: 40m, deliveryEnd: new DateTime(2026, 11, 30));
        db.Context.SalesContracts.AddRange(sale, Sales("CV000002", total: 50m, deliveryEnd: new DateTime(2026, 11, 15)));
        db.Context.PurchaseContracts.AddRange(purchase,
            Purchase("PC000002", total: 70m, deliveryEnd: new DateTime(2026, 11, 20)));
        db.Context.SalesInvoicesItems.Add(item);
        db.Context.SalesContractsAllocations.Add(SalesAllocation(sale, item, 130m));
        db.Context.PurchaseContractsAllocations.Add(PurchaseAllocation(purchase, 60m));
        await Save(db);

        var november = (await Service(db).BuildRowsAsync(new ContractMonthlyPositionRequest(), Today))
            .Single(r => r.Bucket == "11/2026");

        Assert.Equal(50m, november.SalesQuantity); // −30 fica de fora
        Assert.Equal(70m, november.PurchaseQuantity); // −20 fica de fora
        Assert.Equal(20m, november.NetQuantity);
    }

    // Finalizado conta no contratado e no entregue, mas não tem mais o que entregar.
    [Fact]
    public async Task BuildRows_FinishedContractHasNothingLeftToDeliver()
    {
        var db = TestDb.CreateUnitOfWork();
        var finished = Purchase("PC000001", total: 1_000m, status: ContractStatus.Finished,
            deliveryEnd: new DateTime(2026, 11, 30));
        db.Context.PurchaseContracts.AddRange(finished,
            Purchase("PC000002", total: 500m, deliveryEnd: new DateTime(2026, 11, 30)));
        db.Context.PurchaseContractsAllocations.Add(PurchaseAllocation(finished, 900m));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(
            new ContractMonthlyPositionRequest { Statuses = [ContractStatus.Approved, ContractStatus.Finished] }, Today);

        var november = rows.Single(r => r.Bucket == "11/2026");
        Assert.Equal(500m, november.PurchaseQuantity);
        Assert.Equal(1_500m, november.ContractedPurchase);
        Assert.Equal(900m, november.DeliveredPurchase);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Execute_FlagsWhetherTheResultHasASingleUnit(bool withTonnes, bool singleUom)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.Add(Purchase("PC000001"));
        db.Context.SalesContracts.Add(Sales("CV000001"));
        if (withTonnes)
            db.Context.SalesContracts.Add(Sales("CV000002", uom: "TN", total: 100m));
        await Save(db);
        var recorder = new RecordingFastReportService();

        await new ContractMonthlyPositionReportService(db, recorder).ExecuteAsync(new ContractMonthlyPositionRequest(), Today);

        Assert.Equal("ContractMonthlyPosition.frx", recorder.LastReportName);
        Assert.Equal(singleUom, recorder.LastParameters![FastReportService.SingleUomParameter]);
        Assert.Equal(withTonnes ? 3 : 2, recorder.LastParameters["pContractCount"]);
    }

    [Fact]
    public async Task Execute_DescribesTheFiltersWithoutSide()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ LTDA", ShortName = "MATRIZ" });
        await Save(db);
        var recorder = new RecordingFastReportService();

        await new ContractMonthlyPositionReportService(db, recorder).ExecuteAsync(
            new ContractMonthlyPositionRequest { BranchCode = "01", CardCode = "C001" }, Today);

        Assert.Equal(
            "Posição em: 08/10/2026 | Filial: MATRIZ | Parceiro: C001 | Situação: Aprovado",
            recorder.LastParameters!["pFilters"]);
        Assert.Empty((ICollection<ContractMonthlyPositionRowDto>)recorder.LastData!);
    }

    private static ContractMonthlyPositionReportService Service(IUnitOfWork db) => new(db, new RecordingFastReportService());

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
