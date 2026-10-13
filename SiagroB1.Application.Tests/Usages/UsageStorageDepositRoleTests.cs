using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Usages;

/// <summary>"Papel no depósito" da natureza (spec 2026-10-10 §2.1): vai e volta, casa com o tipo e não mexe no contrato.</summary>
public class UsageStorageDepositRoleTests
{
    private static UsageService Service(UnitOfWork db) => new(db, NullLogger<UsageService>.Instance);

    private static UsageModel Remittance() => new()
    {
        Name = "Remessa para depósito", Direction = UsageDirection.Outgoing,
        CfopOutgoingInState = "5905", CfopOutgoingOutState = "6905", PisCst = "08", CofinsCst = "08",
        RequiresQuantity = true, StorageDepositRole = StorageDepositRole.Remittance,
    };

    private static UsageModel Return() => new()
    {
        Name = "Retorno de depósito", Direction = UsageDirection.Incoming,
        CfopIncomingInState = "1906", CfopIncomingOutState = "2906", PisCst = "98", CofinsCst = "98",
        RequiresQuantity = true, StorageDepositRole = StorageDepositRole.Return,
    };

    [Fact]
    public async Task Role_is_saved_and_read_back()
    {
        var name = Guid.NewGuid().ToString();
        var created = await Service(TestDb.CreateUnitOfWork(name)).CreateAsync(Remittance());

        var read = await Service(TestDb.CreateUnitOfWork(name)).GetByIdAsync(created.Code);

        Assert.Equal(StorageDepositRole.Remittance, read!.StorageDepositRole);
    }

    [Fact]
    public async Task Remittance_must_be_outgoing()
    {
        var model = Remittance();
        model.Direction = UsageDirection.Incoming;
        model.CfopIncomingInState = "1905";
        model.CfopIncomingOutState = "2905";

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(TestDb.CreateUnitOfWork()).CreateAsync(model));

        Assert.Equal("Natureza de remessa para depósito precisa ser de saída.", e.Message);
    }

    [Fact]
    public async Task Return_must_be_incoming()
    {
        var model = Return();
        model.Direction = UsageDirection.Outgoing;
        model.CfopOutgoingInState = "5906";
        model.CfopOutgoingOutState = "6906";

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(TestDb.CreateUnitOfWork()).CreateAsync(model));

        Assert.Equal("Natureza de retorno de depósito precisa ser de entrada.", e.Message);
    }

    [Fact]
    public async Task Deposit_role_cannot_touch_the_contract()
    {
        var model = Remittance();
        model.RequiresContract = true;
        model.ContractBalanceEffect = ContractBalanceEffect.Consume;

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(TestDb.CreateUnitOfWork()).CreateAsync(model));

        Assert.Equal("Natureza de remessa ou retorno de depósito não pode alterar o contrato.", e.Message);
    }

    [Fact]
    public async Task Update_can_clear_the_role()
    {
        var name = Guid.NewGuid().ToString();
        var db = TestDb.CreateUnitOfWork(name);
        var created = await Service(db).CreateAsync(Return());

        var model = Return();
        model.StorageDepositRole = StorageDepositRole.None;
        await Service(db).UpdateAsync(created.Code, model);

        Assert.Equal(StorageDepositRole.None,
            (await Service(TestDb.CreateUnitOfWork(name)).GetByIdAsync(created.Code))!.StorageDepositRole);
    }

    [Fact]
    public async Task Sap_mode_refuses_a_deposit_role()
    {
        var sapContext = new SapErpDbContext(
            new DbContextOptionsBuilder<SapErpDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var sapService = new SiagroB1.Application.Services.SAP.UsageService(
            sapContext, TestDb.CreateUnitOfWork(), NullLogger<SiagroB1.Application.Services.SAP.UsageService>.Instance);
        var model = new UsageModel { Name = "X", StorageDepositRole = StorageDepositRole.Remittance };

        var e = await Assert.ThrowsAsync<DefaultException>(() => sapService.UpdateAsync(1, model));

        Assert.Equal("Disponível somente no modo STANDALONE.", e.Message);
    }

    [Fact]
    public async Task Sap_mode_refuses_a_custody_role()
    {
        var sapContext = new SapErpDbContext(
            new DbContextOptionsBuilder<SapErpDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var sapService = new SiagroB1.Application.Services.SAP.UsageService(
            sapContext, TestDb.CreateUnitOfWork(), NullLogger<SiagroB1.Application.Services.SAP.UsageService>.Instance);
        var model = new UsageModel { Name = "X", StorageCustodyRole = StorageCustodyRole.StorageReturn };

        var e = await Assert.ThrowsAsync<DefaultException>(() => sapService.UpdateAsync(1, model));

        Assert.Equal("Disponível somente no modo STANDALONE.", e.Message);
    }
}
