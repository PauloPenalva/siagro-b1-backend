using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.FiscalStock;

/// <summary>
/// Transmitir a NF-e é o que a faz contar no estoque fiscal (só a autorizada é escriturável): no mês fechado a
/// transmissão é recusada antes de reservar número ou falar com a SEFAZ — senão a autorização mudaria o mês fechado.
/// </summary>
public class FiscalStockPeriodNfeIssueTests
{
    private const string ClosedMessage =
        "O estoque fiscal de 10/2026 da filial 01 está fechado. Use uma data de entrada no estoque a partir de 01/11/2026 ou peça a reabertura do mês.";

    private static async Task CloseOctoberAsync(IUnitOfWork db)
    {
        db.Context.FiscalStockPeriods.Add(new FiscalStockPeriod
            { BranchCode = "01", Year = 2026, Month = 10, Status = FiscalStockPeriodStatus.Closed });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Sales_nfe_transmission_in_closed_month_is_refused_before_sefaz()
    {
        var scenario = await NfeTestSeed.SeedAsync(InvoiceStatus.Confirmed);
        foreach (var line in await scenario.Db.Context.SalesInvoicesItems.Where(l => l.SalesInvoiceKey == scenario.InvoiceKey).ToListAsync())
            line.MovesFiscalInventory = true;
        await scenario.Db.SaveChangesAsync();
        await CloseOctoberAsync(scenario.Db);
        var sefaz = new FakeNfeSefazClient();
        var sent = false;
        sefaz.BeforeAuthorize = () => { sent = true; return Task.CompletedTask; };
        var reservation = new FakeNfeNumberReservationService();
        var config = NfeTestSeed.Config();
        var options = new NfeOptions(config);
        var service = new SalesInvoicesNfeIssueService(
            scenario.Db, new TaxCalculationGate(scenario.Db, config), new NfeReadinessValidator(scenario.Db, options),
            new BranchNfeSettingsService(scenario.Db, options, sefaz), reservation, sefaz,
            new SalesInvoiceNfeResultHandler(scenario.Db, new RecordingConfirmService(scenario.Db),
                NullLogger<SalesInvoiceNfeResultHandler>.Instance),
            options, NullLogger<SalesInvoicesNfeIssueService>.Instance, NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal(ClosedMessage, ex.Message);
        Assert.False(sent);
        Assert.Equal(0, reservation.Calls);
    }

    [Fact]
    public async Task Own_purchase_nfe_issue_in_closed_month_is_refused_before_sefaz()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        foreach (var line in await scenario.Db.Context.PurchaseInvoicesItems.Where(l => l.PurchaseInvoiceKey == scenario.InvoiceKey).ToListAsync())
            line.MovesFiscalInventory = true;
        await scenario.Db.SaveChangesAsync();
        await CloseOctoberAsync(scenario.Db);
        var sefaz = new FakeNfeSefazClient();
        var sent = false;
        sefaz.BeforeAuthorize = () => { sent = true; return Task.CompletedTask; };
        var reservation = new FakeNfeNumberReservationService();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, sefaz, reservation).ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal(ClosedMessage, ex.Message);
        Assert.False(sent);
        Assert.Equal(0, reservation.Calls);
    }
}
