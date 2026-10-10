using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Usages;

/// <summary>"Permite contra-nota" na natureza (spec 2026-10-10 D4): vai e volta, e só existe na natureza de entrada.</summary>
public class UsageCounterNoteTests
{
    private static UsageService Service(UnitOfWork db) => new(db, NullLogger<UsageService>.Instance);

    private static UsageModel Purchase() => new()
    {
        Name = "Compra produtor rural", Direction = UsageDirection.Incoming,
        CfopIncomingInState = "1102", CfopIncomingOutState = "2102", PisCst = "74", CofinsCst = "74",
        RequiresQuantity = true, AllowsCounterNote = true,
    };

    [Fact]
    public async Task Flag_is_saved_and_read_back()
    {
        var name = Guid.NewGuid().ToString();
        var created = await Service(TestDb.CreateUnitOfWork(name)).CreateAsync(Purchase());

        var read = await Service(TestDb.CreateUnitOfWork(name)).GetByIdAsync(created.Code);

        Assert.True(read!.AllowsCounterNote);
    }

    [Fact]
    public async Task Update_can_clear_the_flag()
    {
        var name = Guid.NewGuid().ToString();
        var db = TestDb.CreateUnitOfWork(name);
        var created = await Service(db).CreateAsync(Purchase());

        var model = Purchase();
        model.AllowsCounterNote = false;
        await Service(db).UpdateAsync(created.Code, model);

        Assert.False((await Service(TestDb.CreateUnitOfWork(name)).GetByIdAsync(created.Code))!.AllowsCounterNote);
    }

    [Fact]
    public async Task Outgoing_usage_cannot_allow_a_counter_note()
    {
        var model = Purchase();
        model.Direction = UsageDirection.Outgoing;
        model.CfopOutgoingInState = "5102";
        model.CfopOutgoingOutState = "6102";

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(TestDb.CreateUnitOfWork()).CreateAsync(model));

        Assert.Equal("Só natureza de entrada pode permitir contra-nota.", e.Message);
    }
}
