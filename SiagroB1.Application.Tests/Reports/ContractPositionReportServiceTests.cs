using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.ContractPositionSeed;

namespace SiagroB1.Application.Tests.Reports;

public class ContractPositionReportServiceTests
{
    [Fact]
    public async Task BuildRows_PurchasesThenSalesInsideEachProductAndUnit()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesContracts.Add(Sales("CV000001"));
        db.Context.PurchaseContracts.Add(Purchase("PC000001"));
        db.Context.PurchaseContracts.Add(Purchase("PC000002", itemCode: "20001", itemName: "MILHO"));
        db.Context.SalesContracts.Add(Sales("CV000002", uom: "TN", total: 100m));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(new ContractPositionRequest());

        Assert.Equal(
            new[]
            {
                "MILHO (20001) - KG", "SOJA EM GRÃOS (10001) - KG", "SOJA EM GRÃOS (10001) - KG",
                "SOJA EM GRÃOS (10001) - TN",
            },
            rows.Select(r => r.Group));
        Assert.Equal(new[] { "PC000002", "PC000001", "CV000001", "CV000002" }, rows.Select(r => r.Code));
        Assert.Equal(new[] { "Compras", "Compras", "Vendas", "Vendas" }, rows.Select(r => r.Section));
    }

    [Fact]
    public async Task BuildRows_OrdersBySectionCashFlowThenDeadlineThenCode()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.AddRange(
            Purchase("PC000003", cashFlow: null, deliveryEnd: new DateTime(2026, 11, 30)),
            Purchase("PC000002", cashFlow: new DateTime(2026, 11, 10), deliveryEnd: new DateTime(2026, 12, 31)),
            Purchase("PC000001", cashFlow: new DateTime(2026, 11, 10), deliveryEnd: new DateTime(2026, 11, 30)),
            Purchase("PC000004", cashFlow: new DateTime(2026, 10, 20), deliveryEnd: new DateTime(1900, 1, 1)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(new ContractPositionRequest());

        Assert.Equal(new[] { "PC000004", "PC000001", "PC000002", "PC000003" }, rows.Select(r => r.Code));
    }

    [Fact]
    public async Task BuildRows_FormatsTheColumns()
    {
        var db = TestDb.CreateUnitOfWork();
        var purchase = Purchase("PC000001", total: 100_000m, type: ContractType.ToBeDetermined, price: 120.50m,
            cashFlow: new DateTime(2026, 11, 10), deliveryEnd: new DateTime(2026, 12, 31),
            creation: new DateTime(2026, 7, 2, 15, 30, 0), staleAllocated: 1m, staleWashedOut: 1m);
        db.Context.PurchaseContracts.Add(purchase);
        db.Context.PurchaseContractsAllocations.Add(PurchaseAllocation(purchase, 30_000m));
        db.Context.PurchaseContractsWashouts.Add(Washout(purchase, 1, 0m, 5_000m));
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(new ContractPositionRequest())).Single();

        Assert.Equal("SOJA EM GRÃOS (10001) - KG", row.Group);
        Assert.Equal("Compras", row.Section);
        Assert.Equal("PC000001", row.Code);
        Assert.Equal("02/07/2026", row.CreationDate);
        Assert.Equal("(F001) PRODUTOR RURAL", row.Partner);
        Assert.Equal("25/26", row.HarvestSeason);
        Assert.Equal("PAF", row.Type);
        Assert.Equal("10/11/2026", row.CashFlowDate);
        Assert.Equal("31/12/2026", row.DeliveryEndDate);
        Assert.Equal("Aprovado", row.Status);
        Assert.Equal("KG", row.UnitOfMeasure);
        Assert.Equal(120.50m, row.Price);
        Assert.Equal(100_000m, row.ContractedQuantity);
        Assert.Equal(30_000m, row.DeliveredQuantity);
        Assert.Equal(5_000m, row.WashedOutQuantity);
        Assert.Equal(65_000m, row.BalanceQuantity);
        Assert.Equal(65_000m, row.SignedBalanceQuantity);
    }

    // Contrato sem previsão de pagamento e sem prazo de entrega continua na lista.
    [Fact]
    public async Task BuildRows_ContractWithoutCashFlowNorDeadlineKeepsTheRow()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesContracts.Add(Sales("CV000001", cashFlow: null, deliveryEnd: DateTime.MinValue));
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(new ContractPositionRequest())).Single();

        Assert.Equal("", row.CashFlowDate);
        Assert.Equal("", row.DeliveryEndDate);
        Assert.Equal(100_000m, row.BalanceQuantity);
    }

    // Venda com quebra de entrega em item conferido: o saldo usa a quebra e pode ficar negativo.
    [Fact]
    public async Task BuildRows_SalesBalanceUsesTheShortageAndMayBeNegative()
    {
        var db = TestDb.CreateUnitOfWork();
        var sale = Sales("CV000001", total: 100m, staleAllocated: 0m);
        var closed = SalesItem(120m, closed: true, delivered: 110m, loss: 0m);
        db.Context.SalesContracts.Add(sale);
        db.Context.SalesInvoicesItems.Add(closed);
        db.Context.SalesContractsAllocations.Add(SalesAllocation(sale, closed, 120m));
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(new ContractPositionRequest())).Single();

        Assert.Equal(110m, row.DeliveredQuantity);
        Assert.Equal(-10m, row.BalanceQuantity);
        Assert.Equal(0m, row.WashedOutQuantity);
        Assert.Equal(10m, row.SignedBalanceQuantity); // ambos os lados: venda subtrai
    }

    [Theory]
    [InlineData(ContractPositionSide.Both, new[] { "PC000001", "CV000001" }, new[] { 70_000.0, -40_000.0 })]
    [InlineData(ContractPositionSide.Purchase, new[] { "PC000001" }, new[] { 70_000.0 })]
    [InlineData(ContractPositionSide.Sales, new[] { "CV000001" }, new[] { 40_000.0 })]
    public async Task BuildRows_SideChoosesTheSectionsAndTheSignOfTheGeneralBalance(
        ContractPositionSide side, string[] codes, double[] signed)
    {
        var db = TestDb.CreateUnitOfWork();
        var purchase = Purchase("PC000001", total: 100_000m);
        var sale = Sales("CV000001", total: 50_000m);
        var item = SalesItem(10_000m);
        db.Context.PurchaseContracts.Add(purchase);
        db.Context.SalesContracts.Add(sale);
        db.Context.SalesInvoicesItems.Add(item);
        db.Context.PurchaseContractsAllocations.Add(PurchaseAllocation(purchase, 30_000m));
        db.Context.SalesContractsAllocations.Add(SalesAllocation(sale, item, 10_000m));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(new ContractPositionRequest { Side = side });

        Assert.Equal(codes, rows.Select(r => r.Code));
        Assert.Equal(signed.Select(s => (decimal)s), rows.Select(r => r.SignedBalanceQuantity));
    }

    // Contrato finalizado aparece só quando pedido, com o saldo do domínio.
    [Fact]
    public async Task BuildRows_FinishedContractOnlyWhenChosen()
    {
        var db = TestDb.CreateUnitOfWork();
        var finished = Purchase("PC000001", total: 100_000m, status: ContractStatus.Finished);
        db.Context.PurchaseContracts.Add(finished);
        db.Context.PurchaseContractsAllocations.Add(PurchaseAllocation(finished, 90_000m));
        await Save(db);

        Assert.Empty(await Service(db).BuildRowsAsync(new ContractPositionRequest()));

        var row = (await Service(db).BuildRowsAsync(
            new ContractPositionRequest { Statuses = [ContractStatus.Finished] })).Single();
        Assert.Equal("Finalizado", row.Status);
        Assert.Equal(10_000m, row.BalanceQuantity);
    }

    // Washout em aprovação reserva volume; rejeitado e estornado não.
    [Fact]
    public async Task BuildRows_OnlyActiveWashoutsReduceThePurchaseBalance()
    {
        var db = TestDb.CreateUnitOfWork();
        var purchase = Purchase("PC000001", total: 100_000m, staleWashedOut: 50_000m);
        db.Context.PurchaseContracts.Add(purchase);
        db.Context.PurchaseContractsWashouts.AddRange(
            Washout(purchase, 1, 1_000m, 0m, PurchaseContractWashoutStatus.InApproval),
            Washout(purchase, 2, 2_000m, 0m, PurchaseContractWashoutStatus.Approved),
            Washout(purchase, 3, 4_000m, 0m, PurchaseContractWashoutStatus.Rejected),
            Washout(purchase, 4, 8_000m, 0m, PurchaseContractWashoutStatus.Reversed));
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(new ContractPositionRequest())).Single();

        Assert.Equal(3_000m, row.WashedOutQuantity);
        Assert.Equal(97_000m, row.BalanceQuantity);
    }

    // Devolução de compra entra negativa no entregue.
    [Fact]
    public async Task BuildRows_PurchaseReturnReducesTheDelivered()
    {
        var db = TestDb.CreateUnitOfWork();
        var purchase = Purchase("PC000001", total: 100_000m);
        db.Context.PurchaseContracts.Add(purchase);
        db.Context.PurchaseContractsAllocations.AddRange(
            PurchaseAllocation(purchase, 40_000m),
            PurchaseAllocation(purchase, -15_000m));
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(new ContractPositionRequest())).Single();

        Assert.Equal(25_000m, row.DeliveredQuantity);
        Assert.Equal(75_000m, row.BalanceQuantity);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Execute_FlagsWhetherTheResultHasASingleUnit(bool withTonnes, bool singleUom)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.Add(Purchase("PC000001"));
        if (withTonnes)
            db.Context.SalesContracts.Add(Sales("CV000001", uom: "TN", total: 100m));
        await Save(db);
        var recorder = new RecordingFastReportService();

        await new ContractPositionReportService(db, recorder).ExecuteAsync(new ContractPositionRequest(), Today);

        Assert.Equal("ContractPosition.frx", recorder.LastReportName);
        Assert.Equal(singleUom, recorder.LastParameters![FastReportService.SingleUomParameter]);
        Assert.Equal(withTonnes ? 2 : 1, ((ICollection<ContractPositionRowDto>)recorder.LastData!).Count);
    }

    [Theory]
    [InlineData(ContractPositionSide.Both, "Saldo geral (compra - venda):")]
    [InlineData(ContractPositionSide.Purchase, "Saldo geral (compra):")]
    [InlineData(ContractPositionSide.Sales, "Saldo geral (venda):")]
    public async Task Execute_LabelsTheGeneralBalanceBySide(ContractPositionSide side, string label)
    {
        var db = TestDb.CreateUnitOfWork();
        var recorder = new RecordingFastReportService();

        await new ContractPositionReportService(db, recorder)
            .ExecuteAsync(new ContractPositionRequest { Side = side }, Today);

        Assert.Equal(label, recorder.LastParameters!["pGeneralLabel"]);
    }

    [Fact]
    public async Task Execute_DescribesEveryFilter()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ LTDA", ShortName = "MATRIZ" });
        db.Context.PurchaseContracts.Add(Purchase("PC000001", type: ContractType.ToBeDetermined));
        await Save(db);
        var recorder = new RecordingFastReportService();

        await new ContractPositionReportService(db, recorder).ExecuteAsync(new ContractPositionRequest
        {
            Side = ContractPositionSide.Purchase,
            BranchCode = "01",
            ItemCode = "10001",
            HarvestSeasonCode = "25/26",
            CardCode = "F001",
            Type = ContractType.ToBeDetermined,
            DeliveryEndDateUntil = new DateTime(2026, 12, 31),
        }, Today);

        Assert.Equal(
            "Posição em: 08/10/2026 | Lado: Compra | Filial: MATRIZ | Produto: SOJA EM GRÃOS (10001) | " +
            "Safra: 25/26 | Parceiro: PRODUTOR RURAL | Tipo: PAF - Preço a Fixar | " +
            "Término da entrega até: 31/12/2026 | Situação: Aprovado",
            recorder.LastParameters!["pFilters"]);
    }

    private static ContractPositionReportService Service(IUnitOfWork db) => new(db, new RecordingFastReportService());

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
