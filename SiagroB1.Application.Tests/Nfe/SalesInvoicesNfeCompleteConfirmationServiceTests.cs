using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>"Concluir confirmação": NF-e autorizada cuja confirmação falhou (spec §9.2 passo 7).</summary>
public class SalesInvoicesNfeCompleteConfirmationServiceTests
{
    private static async Task<NfeScenario> AuthorizedWithErrorAsync()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var invoice = await scenario.Db.Context.SalesInvoices.SingleAsync();
        invoice.NfeStatus = NfeStatus.Authorized;
        invoice.NfeConfirmationError = "Liberação de entrega sem saldo.";
        await scenario.Db.SaveChangesAsync();
        return scenario;
    }

    [Fact]
    public async Task Success_confirms_and_clears_the_error()
    {
        var scenario = await AuthorizedWithErrorAsync();
        var confirm = new RecordingConfirmService(scenario.Db);

        var outcome = await new SalesInvoicesNfeCompleteConfirmationService(
            scenario.Db, new SalesInvoiceNfeResultHandler(scenario.Db, confirm)).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, outcome.InvoiceStatus);
        Assert.Null(outcome.ConfirmationError);
    }

    [Fact]
    public async Task New_failure_replaces_the_error()
    {
        var scenario = await AuthorizedWithErrorAsync();
        var confirm = new RecordingConfirmService(scenario.Db, failWith: new DefaultException("Contrato encerrado."));

        var outcome = await new SalesInvoicesNfeCompleteConfirmationService(
            scenario.Db, new SalesInvoiceNfeResultHandler(scenario.Db, confirm)).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal("Contrato encerrado.", outcome.ConfirmationError);
        Assert.Equal(InvoiceStatus.Pending, outcome.InvoiceStatus);
    }

    [Fact]
    public async Task Without_authorized_nfe_it_is_refused()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        await Assert.ThrowsAsync<DefaultException>(() => new SalesInvoicesNfeCompleteConfirmationService(
                scenario.Db, new SalesInvoiceNfeResultHandler(scenario.Db, new RecordingConfirmService(scenario.Db)))
            .ExecuteAsync(scenario.InvoiceKey, "tester"));
    }
}
