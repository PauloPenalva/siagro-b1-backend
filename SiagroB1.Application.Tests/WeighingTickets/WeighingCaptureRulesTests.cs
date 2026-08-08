using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.WeighingTickets;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Scales;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.WeighingTickets;

public class WeighingCaptureRulesTests
{
    private sealed class FakePermissions(bool canType) : IUserPermissions
    {
        public Task<bool> HasAsync(string username, string permissionCode) => Task.FromResult(canType);

        public Task<List<string>> GetAsync(string username)
        {
            var granted = canType
                ? new List<string> { PermissionCodes.WeighingManualEntry }
                : new List<string>();

            return Task.FromResult(granted);
        }
    }

    private static readonly DateTime Now = DateTime.Now;

    private static async Task<(IUnitOfWork db, WeighingTicket ticket)> SeedAsync(
        int? tareWeight = 15000,
        bool validateTare = true,
        int tolerance = 200)
    {
        var db = TestDb.CreateUnitOfWork();

        db.Context.TruckScales.Add(new TruckScale
        {
            Code = "TS01",
            Name = "Balança 1",
            Localization = "Portaria",
            IpAddress = "192.168.1.201",
            Port = 4000,
            ValidateTare = validateTare,
            TareToleranceKg = tolerance
        });

        db.Context.UserTruckScales.Add(new UserTruckScale
        {
            Username = "joao",
            TruckScaleCode = "TS01",
            Purpose = WeighingScalePurpose.Opening
        });

        db.Context.Trucks.Add(new Truck { Code = "ABC1D23", TareWeight = tareWeight });

        var ticket = new WeighingTicket
        {
            Key = Guid.NewGuid(),
            Type = WeighingTicketType.Receipt,
            ItemCode = "SOJA",
            CardCode = "F0001",
            TruckCode = "ABC1D23",
            TruckDriverCode = "1",
            Stage = WeighingTicketStage.ReadyForFirstWeighing
        };

        db.Context.WeighingTickets.Add(ticket);

        await db.SaveChangesAsync();

        return (db, ticket);
    }

    private static WeighingTicketsFirstWeighingService Service(
        IUnitOfWork db, CaptureStore captures, bool canType) =>
        new(db, new WeighingCaptureValidator(db, new FakePermissions(canType), captures));

    [Fact]
    public async Task Without_the_permission_a_capture_is_required()
    {
        var (db, ticket) = await SeedAsync();
        var service = Service(db, new CaptureStore(TimeSpan.FromMinutes(10)), canType: false);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => service.ExecuteAsync(ticket.Key, 32000, null, "joao", captureId: null));

        Assert.Contains("capturado", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task With_a_valid_capture_the_weight_is_saved_and_marked_as_captured()
    {
        var (db, ticket) = await SeedAsync();
        var captures = new CaptureStore(TimeSpan.FromMinutes(10));
        var capture = captures.Create("TS01", 32000, "joao", Now);

        await Service(db, captures, canType: false)
            .ExecuteAsync(ticket.Key, 32000, null, "joao", capture.CaptureId);

        var saved = db.Context.WeighingTickets.Single();

        Assert.Equal(32000, saved.FirstWeighValue);
        Assert.Equal("TS01", saved.FirstWeighScaleCode);
        Assert.True(saved.FirstWeighCaptured);
        Assert.Equal(WeighingTicketStage.ReadyForSecondWeighing, saved.Stage);
    }

    [Fact]
    public async Task A_capture_cannot_be_reused()
    {
        var (db, ticket) = await SeedAsync();
        var captures = new CaptureStore(TimeSpan.FromMinutes(10));
        var capture = captures.Create("TS01", 32000, "joao", Now);

        await Service(db, captures, canType: false)
            .ExecuteAsync(ticket.Key, 32000, null, "joao", capture.CaptureId);

        ticket.Stage = WeighingTicketStage.ReadyForFirstWeighing;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service(db, captures, canType: false)
                .ExecuteAsync(ticket.Key, 32000, null, "joao", capture.CaptureId));
    }

    [Fact]
    public async Task A_weight_that_does_not_match_the_capture_is_refused()
    {
        var (db, ticket) = await SeedAsync();
        var captures = new CaptureStore(TimeSpan.FromMinutes(10));
        var capture = captures.Create("TS01", 32000, "joao", Now);

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service(db, captures, canType: false)
                .ExecuteAsync(ticket.Key, 31000, null, "joao", capture.CaptureId));
    }

    [Fact]
    public async Task With_the_permission_a_typed_weight_is_accepted_and_not_marked_as_captured()
    {
        var (db, ticket) = await SeedAsync();

        await Service(db, new CaptureStore(TimeSpan.FromMinutes(10)), canType: true)
            .ExecuteAsync(ticket.Key, 32000, null, "joao", captureId: null);

        var saved = db.Context.WeighingTickets.Single();

        Assert.Equal(32000, saved.FirstWeighValue);
        Assert.False(saved.FirstWeighCaptured);
    }

    [Fact]
    public async Task A_weight_below_the_tare_minus_the_tolerance_is_refused()
    {
        var (db, ticket) = await SeedAsync(tareWeight: 15000, tolerance: 200);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service(db, new CaptureStore(TimeSpan.FromMinutes(10)), canType: true)
                .ExecuteAsync(ticket.Key, 14700, null, "joao", captureId: null));

        Assert.Contains("tara", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_weight_inside_the_tolerance_is_accepted()
    {
        var (db, ticket) = await SeedAsync(tareWeight: 15000, tolerance: 200);

        await Service(db, new CaptureStore(TimeSpan.FromMinutes(10)), canType: true)
            .ExecuteAsync(ticket.Key, 14900, null, "joao", captureId: null);

        Assert.Equal(14900, db.Context.WeighingTickets.Single().FirstWeighValue);
    }

    [Fact]
    public async Task A_truck_without_a_registered_tare_is_refused_when_validation_is_on()
    {
        var (db, ticket) = await SeedAsync(tareWeight: null);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service(db, new CaptureStore(TimeSpan.FromMinutes(10)), canType: true)
                .ExecuteAsync(ticket.Key, 32000, null, "joao", captureId: null));

        Assert.Contains("tara", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tare_is_not_validated_when_the_scale_has_it_turned_off()
    {
        var (db, ticket) = await SeedAsync(tareWeight: null, validateTare: false);

        await Service(db, new CaptureStore(TimeSpan.FromMinutes(10)), canType: true)
            .ExecuteAsync(ticket.Key, 32000, null, "joao", captureId: null);

        Assert.Equal(32000, db.Context.WeighingTickets.Single().FirstWeighValue);
    }
}
