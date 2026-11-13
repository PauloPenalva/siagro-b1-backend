using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Services.WeighingTickets;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.WeighingTickets;

/// <summary>
/// Ticket de pesagem ainda não concluído (sem romaneio) pode ser cancelado, mas só por quem tem o
/// papel ADMIN atribuído pelo perfil. O ticket concluído segue a regra antiga: cancela junto com o
/// romaneio pendente, sem exigir o papel.
/// </summary>
public class WeighingTicketCancelUnfinishedTests
{
    private sealed class RolePermissions(params string[] roles) : IUserPermissions
    {
        public Task<bool> HasAsync(string username, string permissionCode) => Task.FromResult(false);
        public Task<List<string>> GetAsync(string username) => Task.FromResult(new List<string>());
        public Task<bool> HasRoleAsync(string username, string roleCode) => Task.FromResult(roles.Contains(roleCode));
    }

    private static WeighingTicketsCancelService Service(IUnitOfWork db, bool admin) => new(
        db,
        new WeighingTicketsGetService(db.Context, NullLogger<WeighingTicketsGetService>.Instance),
        new StorageTransactionsCancelService(db, new ShipmentReleasesRecalculateShippedService(db.Context)),
        new RolePermissions(admin ? [Domain.Constants.Roles.Admin] : []),
        new FakeStringLocalizer<Resource>());

    private static async Task<(IUnitOfWork Db, WeighingTicket Ticket)> SeedTicketAsync(
        WeighingTicketStatus status,
        WeighingTicketStage stage)
    {
        var db = TestDb.CreateUnitOfWork();

        db.Context.Trucks.Add(new Truck { Code = "ABC1D23" });
        db.Context.TruckDrivers.Add(new TruckDriver { Code = "1", Name = "Motorista", Document = "0", Phone = "0" });

        var ticket = new WeighingTicket
        {
            Key = Guid.NewGuid(),
            Type = WeighingTicketType.Receipt,
            ItemCode = "TRIGO",
            CardCode = "C0001",
            BranchCode = "01",
            TruckCode = "ABC1D23",
            TruckDriverCode = "1",
            Status = status,
            Stage = stage,
        };
        db.Context.WeighingTickets.Add(ticket);
        await db.SaveChangesAsync();

        return (db, ticket);
    }

    private static Task<WeighingTicket> ReloadAsync(IUnitOfWork db, Guid key) =>
        db.Context.WeighingTickets.AsNoTracking().SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Admin_cancels_ticket_waiting_first_weighing()
    {
        var (db, ticket) = await SeedTicketAsync(WeighingTicketStatus.Waiting, WeighingTicketStage.ReadyForFirstWeighing);

        await Service(db, admin: true).ExecuteAsync(ticket.Key, "ana");

        var saved = await ReloadAsync(db, ticket.Key);
        Assert.Equal(WeighingTicketStatus.Cancelled, saved.Status);
        Assert.Equal(WeighingTicketStage.Completed, saved.Stage);
        Assert.Equal("ana", saved.UpdatedBy);
    }

    [Fact]
    public async Task Admin_cancels_ticket_in_processing()
    {
        var (db, ticket) = await SeedTicketAsync(WeighingTicketStatus.Processing, WeighingTicketStage.ReadyForCompleting);

        await Service(db, admin: true).ExecuteAsync(ticket.Key, "ana");

        var saved = await ReloadAsync(db, ticket.Key);
        Assert.Equal(WeighingTicketStatus.Cancelled, saved.Status);
        Assert.Equal(WeighingTicketStage.Completed, saved.Stage);
    }

    [Fact]
    public async Task Non_admin_cannot_cancel_unfinished_ticket()
    {
        var (db, ticket) = await SeedTicketAsync(WeighingTicketStatus.Waiting, WeighingTicketStage.ReadyForFirstWeighing);

        var error = await Assert.ThrowsAsync<BusinessException>(
            () => Service(db, admin: false).ExecuteAsync(ticket.Key, "joao"));

        Assert.Equal(
            "Somente usuário com o papel ADMIN pode cancelar um ticket de pesagem não concluído.",
            error.Message);

        var saved = await ReloadAsync(db, ticket.Key);
        Assert.Equal(WeighingTicketStatus.Waiting, saved.Status);
        Assert.Equal(WeighingTicketStage.ReadyForFirstWeighing, saved.Stage);
    }

    [Fact]
    public async Task Already_cancelled_ticket_is_refused()
    {
        var (db, ticket) = await SeedTicketAsync(WeighingTicketStatus.Cancelled, WeighingTicketStage.Completed);

        var error = await Assert.ThrowsAsync<BusinessException>(
            () => Service(db, admin: true).ExecuteAsync(ticket.Key, "ana"));

        Assert.Equal("Ticket de pesagem já está cancelado.", error.Message);
    }

    /// <summary>Regressão: o ticket concluído não passa pela regra nova nem exige o papel.</summary>
    [Fact]
    public async Task Complete_ticket_without_storage_transaction_keeps_current_error()
    {
        var (db, ticket) = await SeedTicketAsync(WeighingTicketStatus.Complete, WeighingTicketStage.Completed);

        await Assert.ThrowsAsync<NotFoundException>(
            () => Service(db, admin: false).ExecuteAsync(ticket.Key, "joao"));
    }
}
