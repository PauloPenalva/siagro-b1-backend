using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Cte;

public class CteVoidNumbersServiceTests : CteEventsTestBase
{
    private const string Reason = "Numeros reservados e nao utilizados";

    private CteVoidNumbersService Service() => new(Db, NfeTestSeed.Config(), Contexts(), Sefaz, () => Clock);

    private void Pending(int number) =>
        Db.Context.CteNumbersToVoid.Add(new CteNumberToVoid { BranchCode = "01", Series = 1, Number = number, Reason = "descartado" });

    [Fact]
    public async Task Void_range_with_authorized_number_is_rejected()
    {
        await SeedAsync(CteStatus.Authorized, number: 102);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().VoidAsync("01", 1, 100, 105, Reason));

        Assert.Contains("102", e.Message);
        Assert.Equal(0, Sefaz.VoidNumberCalls);
    }

    [Fact]
    public async Task Void_range_with_cancelled_number_is_rejected()
    {
        await SeedAsync(CteStatus.Cancelled, number: 103);

        await Assert.ThrowsAsync<DefaultException>(() => Service().VoidAsync("01", 1, 100, 105, Reason));
    }

    [Fact]
    public async Task Void_marks_pending_numbers()
    {
        await SeedAsync(CteStatus.Authorized, number: 200);
        Pending(100);
        Pending(101);
        Pending(150);
        await Db.SaveChangesAsync();
        Sefaz.VoidNumberResponses.Enqueue(FakeCteSefazClient.NumberVoided);

        var result = await Service().VoidAsync("01", 1, 100, 101, Reason);

        Assert.Equal(102, result.StatusCode);
        var request = Sefaz.VoidNumberRequests.Single();
        Assert.Equal((100, 101, "35", 2026), (request.From, request.To, request.StateCode, request.Year));
        var rows = await Db.Context.CteNumbersToVoid.AsNoTracking().OrderBy(x => x.Number).ToListAsync();
        Assert.NotNull(rows[0].VoidedAt);
        Assert.Equal(FakeCteSefazClient.VoidNumberProtocol, rows[1].VoidProtocol);
        Assert.Null(rows[2].VoidedAt);
    }

    [Fact]
    public async Task Void_unavailable_service_keeps_number_registered_and_unmarked()
    {
        await SeedAsync(CteStatus.Authorized, number: 200);
        Pending(100);
        await Db.SaveChangesAsync();
        Sefaz.VoidNumberResponses.Enqueue(_ => throw new NotSupportedException("sem serviço"));

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().VoidAsync("01", 1, 100, 100, Reason));

        Assert.Equal(CteVoidNumbersService.NotAvailable, e.Message);
        Assert.Equal(
            "A inutilização de numeração não está disponível para o CT-e 4.00 nesta versão; o número fica registrado na lista de descartados.",
            e.Message);
        var row = await Db.Context.CteNumbersToVoid.AsNoTracking().SingleAsync();
        Assert.Null(row.VoidedAt);
        Assert.Null(row.VoidProtocol);
    }

    [Fact]
    public async Task Void_short_justification_is_rejected()
    {
        await SeedAsync(CteStatus.Authorized, number: 200);

        await Assert.ThrowsAsync<DefaultException>(() => Service().VoidAsync("01", 1, 100, 100, "curta"));
    }
}
