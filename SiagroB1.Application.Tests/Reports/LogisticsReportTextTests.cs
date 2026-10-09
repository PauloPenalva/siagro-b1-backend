using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using static SiagroB1.Application.Tests.Support.LogisticsReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class LogisticsReportTextTests
{
    private sealed class Request : LogisticsReportRequest;

    [Theory]
    [InlineData(ShipmentLoadStatus.Planned, "Planejada")]
    [InlineData(ShipmentLoadStatus.Open, "Carregada")]
    [InlineData(ShipmentLoadStatus.PartiallyInvoiced, "Faturada Parcial")]
    [InlineData(ShipmentLoadStatus.Invoiced, "Faturada")]
    [InlineData(ShipmentLoadStatus.Cancelled, "Cancelada")]
    [InlineData(ShipmentLoadStatus.Returned, "Devolvida")]
    [InlineData(ShipmentLoadStatus.Completed, "Concluída")]
    [InlineData(ShipmentLoadStatus.InTransshipment, "Em Transbordo")]
    [InlineData(ShipmentLoadStatus.Discharged, "Descarregada")]
    public void LoadStatusText_MatchesTheScreen(ShipmentLoadStatus status, string expected) =>
        Assert.Equal(expected, LogisticsReportText.LoadStatusText(status));

    [Theory]
    [InlineData(ShipmentLoadType.Normal, "Normal")]
    [InlineData(ShipmentLoadType.Removal, "Remoção")]
    public void LoadTypeText_MatchesTheScreen(ShipmentLoadType type, string expected) =>
        Assert.Equal(expected, LogisticsReportText.LoadTypeText(type));

    [Theory]
    [InlineData(ReleaseStatus.Pending, "Pendente")]
    [InlineData(ReleaseStatus.Actived, "Ativo")]
    [InlineData(ReleaseStatus.Completed, "Finalizado")]
    [InlineData(ReleaseStatus.Cancelled, "Cancelado")]
    [InlineData(ReleaseStatus.Paused, "Pausado")]
    public void ReleaseStatusText_MatchesTheScreen(ReleaseStatus status, string expected) =>
        Assert.Equal(expected, LogisticsReportText.ReleaseStatusText(status));

    [Theory]
    [InlineData(ReleaseOrigin.Standard, "Compra")]
    [InlineData(ReleaseOrigin.OwnershipTransfer, "Transferência")]
    [InlineData(ReleaseOrigin.SalesReturn, "Devolução")]
    [InlineData(ReleaseOrigin.Transshipment, "Transbordo")]
    public void OriginText_MatchesTheScreen(ReleaseOrigin origin, string expected) =>
        Assert.Equal(expected, LogisticsReportText.OriginText(origin));

    [Theory]
    [InlineData(StorageTransactionsStatus.Pending, "Pendente")]
    [InlineData(StorageTransactionsStatus.Confirmed, "Confirmado")]
    [InlineData(StorageTransactionsStatus.Cancelled, "Cancelado")]
    [InlineData(StorageTransactionsStatus.Invoiced, "Faturado")]
    [InlineData(StorageTransactionsStatus.Returned, "Devolvido")]
    public void TransactionStatusText_MatchesTheScreen(StorageTransactionsStatus status, string expected) =>
        Assert.Equal(expected, LogisticsReportText.TransactionStatusText(status));

    [Fact]
    public void EffectiveStatuses_EmptyMeansEverythingButCancelled()
    {
        var loads = LogisticsReportText.EffectiveLoadStatuses(null);
        Assert.Equal(9, loads.Length);
        Assert.DoesNotContain(ShipmentLoadStatus.Cancelled, loads);

        Assert.Equal(
            new[] { ReleaseStatus.Pending, ReleaseStatus.Actived, ReleaseStatus.Completed, ReleaseStatus.Paused },
            LogisticsReportText.EffectiveReleaseStatuses([]));

        Assert.Equal(
            new[]
            {
                StorageTransactionsStatus.Pending, StorageTransactionsStatus.Confirmed,
                StorageTransactionsStatus.Invoiced, StorageTransactionsStatus.Returned,
            },
            LogisticsReportText.EffectiveTransactionStatuses(null));
    }

    [Fact]
    public void EffectiveStatuses_KeepsAnExplicitChoiceWithoutDuplicates() =>
        Assert.Equal(
            new[] { ShipmentLoadStatus.Cancelled },
            LogisticsReportText.EffectiveLoadStatuses([ShipmentLoadStatus.Cancelled, ShipmentLoadStatus.Cancelled]));

    [Fact]
    public void StatusFilter_DescribesTheDefaultAsAllButCancelled() =>
        Assert.Equal(
            "Situação: todas, exceto Cancelada",
            LogisticsReportText.StatusFilter(
                LogisticsReportText.EffectiveLoadStatuses(null), LogisticsReportText.LoadStatusText, ShipmentLoadStatus.Cancelled));

    [Fact]
    public void StatusFilter_DescribesEverythingAsAll() =>
        Assert.Equal(
            "Situação: todas",
            LogisticsReportText.StatusFilter(
                Enum.GetValues<ReleaseStatus>(), LogisticsReportText.ReleaseStatusText, ReleaseStatus.Cancelled));

    [Fact]
    public void StatusFilter_ListsAnExplicitChoiceInEnumOrder() =>
        Assert.Equal(
            "Situação: Carregada, Faturada",
            LogisticsReportText.StatusFilter(
                new[] { ShipmentLoadStatus.Invoiced, ShipmentLoadStatus.Open },
                LogisticsReportText.LoadStatusText, ShipmentLoadStatus.Cancelled));

    [Fact]
    public void LoadCustomers_JoinsTheLiveInvoicesDistinctAndSorted()
    {
        var load = Load("CG000001", plannedCardCode: "C009", plannedCardName: "FAZENDA BOA VISTA");
        load.Invoices.Add(LoadInvoice(load, "000000101", "C001", "COOPERATIVA CENTRAL"));
        load.Invoices.Add(LoadInvoice(load, "000000102", "C002", "AGRO NORTE"));
        load.Invoices.Add(LoadInvoice(load, "000000103", "C001", "COOPERATIVA CENTRAL", status: null));
        load.Invoices.Add(LoadInvoice(load, "000000104", "C003", "CLIENTE CANCELADO", status: InvoiceStatus.Cancelled));

        Assert.Equal("AGRO NORTE, COOPERATIVA CENTRAL", LogisticsReportText.LoadCustomers(load));
    }

    [Fact]
    public void LoadCustomers_WithoutALiveInvoiceFallsBackToThePlan()
    {
        var planned = Load("CG000001", plannedCardCode: "C009", plannedCardName: "FAZENDA BOA VISTA");
        planned.Invoices.Add(LoadInvoice(planned, "000000104", "C003", "CLIENTE CANCELADO", status: InvoiceStatus.Cancelled));

        Assert.Equal("(planejado) FAZENDA BOA VISTA", LogisticsReportText.LoadCustomers(planned));
        Assert.Equal("", LogisticsReportText.LoadCustomers(Load("CG000002")));
        Assert.Equal("", LogisticsReportText.LoadCustomers(null));
    }

    [Fact]
    public void LoadHasCustomer_UsesTheInvoicesAndOnlyThenThePlan()
    {
        var invoiced = Load("CG000001", plannedCardCode: "C009", plannedCardName: "FAZENDA BOA VISTA");
        invoiced.Invoices.Add(LoadInvoice(invoiced, "000000101", "C001", "COOPERATIVA CENTRAL"));
        var planned = Load("CG000002", plannedCardCode: "C001", plannedCardName: "COOPERATIVA CENTRAL");

        Assert.True(LogisticsReportText.LoadHasCustomer(invoiced, "C001"));
        Assert.False(LogisticsReportText.LoadHasCustomer(invoiced, "C009"));
        Assert.True(LogisticsReportText.LoadHasCustomer(planned, "C001"));
        Assert.False(LogisticsReportText.LoadHasCustomer(null, "C001"));
    }

    [Fact]
    public void CustomerName_ComesFromTheInvoiceOrThePlan()
    {
        var invoiced = Load("CG000001");
        invoiced.Invoices.Add(LoadInvoice(invoiced, "000000101", "C001", "COOPERATIVA CENTRAL"));
        var planned = Load("CG000002", plannedCardCode: "C002", plannedCardName: "AGRO NORTE");

        Assert.Equal("COOPERATIVA CENTRAL", LogisticsReportText.CustomerName([invoiced, planned], "C001"));
        Assert.Equal("AGRO NORTE", LogisticsReportText.CustomerName([invoiced, planned], "C002"));
        Assert.Null(LogisticsReportText.CustomerName([invoiced, planned], "C777"));
        Assert.Null(LogisticsReportText.CustomerName([invoiced, null], null));
    }

    [Fact]
    public void Validate_RequiresAValidPeriod()
    {
        Assert.Equal("Informe o período.", LogisticsReportText.Validate(new Request { ToDate = Jul31 }));
        Assert.Equal("A data final não pode ser anterior à inicial.",
            LogisticsReportText.Validate(new Request { FromDate = Jul31, ToDate = Jul01 }));
        Assert.Null(LogisticsReportText.Validate(new Request { FromDate = Jul01, ToDate = Jul31 }));
    }

    [Fact]
    public void BuildFilters_ListsOnlyWhatWasInformed()
    {
        var request = new Request { FromDate = Jul01, ToDate = Jul31, BranchCode = "01", ItemCode = "10001" };

        Assert.Equal(
            "Data da carga: 01/07/2026 a 31/07/2026 | Filial: MATRIZ | Produto: SOJA EM GRÃOS (10001) | Situação: todas, exceto Cancelada | Tipo: Normal",
            LogisticsReportText.BuildFilters("Data da carga", request, "MATRIZ", "SOJA EM GRÃOS (10001)",
                "Situação: todas, exceto Cancelada", ["Tipo: Normal"]));

        Assert.Equal(
            "Data da carga: 01/07/2026 a 31/07/2026 | Filial: 01 | Produto: 10001 | Situação: todas",
            LogisticsReportText.BuildFilters("Data da carga", request, null, null, "Situação: todas", []));

        Assert.Equal(
            "Data da carga: 01/07/2026 a 31/07/2026 | Situação: todas",
            LogisticsReportText.BuildFilters("Data da carga", new Request { FromDate = Jul01, ToDate = Jul31 },
                "MATRIZ", "SOJA", "Situação: todas", []));
    }

    [Fact]
    public async Task BranchNameAsync_ReadsTheLocalBranchTable()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ LTDA", ShortName = "MATRIZ" });
        await db.Context.SaveChangesAsync();

        Assert.Equal("MATRIZ", await LogisticsReportText.BranchNameAsync(db.Context, "01"));
        Assert.Null(await LogisticsReportText.BranchNameAsync(db.Context, "99"));
        Assert.Null(await LogisticsReportText.BranchNameAsync(db.Context, null));
    }
}
