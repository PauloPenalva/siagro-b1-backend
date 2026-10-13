using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Usages;

/// <summary>"Papel na custódia" da natureza (spec 2026-10-12 §3.1): vai e volta, casa com a direção e não convive com
/// contrato, remessa para depósito nem contra-nota.</summary>
public class UsageStorageCustodyRoleTests
{
    private static UsageService Service(UnitOfWork db) => new(db, NullLogger<UsageService>.Instance);

    private static UsageModel Entry() => new()
    {
        Name = "Entrada para beneficiamento", Direction = UsageDirection.Incoming,
        CfopIncomingInState = "1901", CfopIncomingOutState = "2901", PisCst = "98", CofinsCst = "98",
        RequiresQuantity = true, StorageCustodyRole = StorageCustodyRole.ProcessingEntry,
    };

    private static UsageModel Return() => new()
    {
        Name = "Retorno de armazenagem", Direction = UsageDirection.Outgoing,
        CfopOutgoingInState = "5906", CfopOutgoingOutState = "6906", PisCst = "08", CofinsCst = "08",
        RequiresQuantity = true, StorageCustodyRole = StorageCustodyRole.StorageReturn,
    };

    [Fact]
    public async Task Role_is_saved_and_read_back()
    {
        var name = Guid.NewGuid().ToString();
        var created = await Service(TestDb.CreateUnitOfWork(name)).CreateAsync(Entry());

        var read = await Service(TestDb.CreateUnitOfWork(name)).GetByIdAsync(created.Code);

        Assert.Equal(StorageCustodyRole.ProcessingEntry, read!.StorageCustodyRole);
    }

    [Theory]
    [InlineData(StorageCustodyRole.ProcessingEntry)]
    [InlineData(StorageCustodyRole.StorageEntry)]
    public async Task Entry_roles_must_be_incoming(StorageCustodyRole role)
    {
        var model = Return();
        model.StorageCustodyRole = role;

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(TestDb.CreateUnitOfWork()).CreateAsync(model));

        Assert.Equal("Natureza de entrada de custódia (beneficiamento ou armazenagem) precisa ser de entrada.", e.Message);
    }

    [Theory]
    [InlineData(StorageCustodyRole.ProcessingSymbolicReturn)]
    [InlineData(StorageCustodyRole.StorageReturn)]
    [InlineData(StorageCustodyRole.StorageSymbolicReturn)]
    public async Task Return_roles_must_be_outgoing(StorageCustodyRole role)
    {
        var model = Entry();
        model.StorageCustodyRole = role;

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(TestDb.CreateUnitOfWork()).CreateAsync(model));

        Assert.Equal("Natureza de devolução ou retorno de custódia precisa ser de saída.", e.Message);
    }

    [Fact]
    public async Task Custody_role_cannot_touch_the_contract()
    {
        var model = Entry();
        model.RequiresContract = true;
        model.ContractBalanceEffect = ContractBalanceEffect.Consume;

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(TestDb.CreateUnitOfWork()).CreateAsync(model));

        Assert.Equal("Natureza de custódia (armazenagem) não pode alterar o contrato.", e.Message);
    }

    [Fact]
    public async Task Custody_role_cannot_be_a_deposit_role_too()
    {
        var model = Entry();
        model.StorageDepositRole = StorageDepositRole.Return;

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(TestDb.CreateUnitOfWork()).CreateAsync(model));

        Assert.Equal("A natureza tem papel na remessa para depósito ou na custódia, não nos dois.", e.Message);
    }

    [Fact]
    public async Task Custody_role_cannot_allow_counter_note()
    {
        var model = Entry();
        model.AllowsCounterNote = true;

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(TestDb.CreateUnitOfWork()).CreateAsync(model));

        Assert.Equal("Natureza de custódia (armazenagem) não permite contra-nota.", e.Message);
    }

    [Fact]
    public async Task Update_can_clear_the_role()
    {
        var name = Guid.NewGuid().ToString();
        var db = TestDb.CreateUnitOfWork(name);
        var created = await Service(db).CreateAsync(Return());

        var model = Return();
        model.StorageCustodyRole = StorageCustodyRole.None;
        await Service(db).UpdateAsync(created.Code, model);

        Assert.Equal(StorageCustodyRole.None,
            (await Service(TestDb.CreateUnitOfWork(name)).GetByIdAsync(created.Code))!.StorageCustodyRole);
    }
}
