using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Security;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.SalesContracts;

public class SalesContractFiscalComplementServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private const string User = "fiscal";

    private SalesContractFiscalComplementService Service(bool granted = true) =>
        new(_db, new UsageService(_db, NullLogger<UsageService>.Instance),
            granted ? new FakeUserPermissions(PermissionCodes.SalesContractFiscalEdit) : new FakeUserPermissions());

    private async Task<Guid> SeedContractAsync()
    {
        var contract = new SalesContract
        {
            Key = Guid.NewGuid(), Code = "SC-000001", BranchCode = "01", CardCode = "C0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", Volume = 500m, Price = 3m, Type = ContractType.Fixed,
            Status = ContractStatus.Approved, DeliveryStartDate = new DateTime(2026, 10, 1), DeliveryEndDate = new DateTime(2026, 10, 31),
        };
        _db.Context.SalesContracts.Add(contract);
        await _db.Context.SaveChangesAsync();
        return contract.Key;
    }

    private async Task<int> SeedUsageAsync(UsageDirection direction = UsageDirection.Outgoing, bool inactive = false)
    {
        var model = new UsageModel { Name = "Venda de grãos", Direction = direction, Inactive = inactive };
        if (direction == UsageDirection.Outgoing) { model.CfopOutgoingInState = "5102"; model.CfopOutgoingOutState = "6102"; }
        else { model.CfopIncomingInState = "1102"; model.CfopIncomingOutState = "2102"; }
        return (await new UsageService(_db, NullLogger<UsageService>.Instance).CreateAsync(model)).Code;
    }

    private async Task<int> SeedConditionAsync()
    {
        var condition = new PaymentCondition { Name = "30 dias", Days = "30", PaymentMeans = "15" };
        _db.Context.PaymentConditions.Add(condition);
        await _db.Context.SaveChangesAsync();
        return condition.Code;
    }

    private static SalesContractFiscalComplementInput Input(int? usage = null, int? condition = null,
        string? order = null, string? item = null) => new(usage, condition, null, order, item);

    [Fact]
    public async Task Without_permission_is_refused()
    {
        var key = await SeedContractAsync();
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(granted: false).SetAsync(key, Input(), User));
        Assert.Equal("Você não tem permissão para alterar o complemento fiscal do contrato.", e.Message);
    }

    [Fact]
    public async Task Incoming_usage_is_refused()
    {
        var key = await SeedContractAsync();
        var usage = await SeedUsageAsync(UsageDirection.Incoming);
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().SetAsync(key, Input(usage: usage), User));
        Assert.Equal($"A natureza de operação {usage} não é uma natureza de saída ativa.", e.Message);
    }

    [Fact]
    public async Task Inactive_usage_is_refused()
    {
        var key = await SeedContractAsync();
        var usage = await SeedUsageAsync(inactive: true);
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().SetAsync(key, Input(usage: usage), User));
        Assert.Equal($"A natureza de operação {usage} não é uma natureza de saída ativa.", e.Message);
    }

    [Fact]
    public async Task Unknown_payment_condition_is_refused()
    {
        var key = await SeedContractAsync();
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().SetAsync(key, Input(condition: 999), User));
        Assert.Equal("Condição de pagamento 999 não encontrada.", e.Message);
    }

    [Theory]
    [InlineData("1A")]
    [InlineData("1234567")]
    public async Task Order_item_with_letters_or_seven_digits_is_refused(string item)
    {
        var key = await SeedContractAsync();
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().SetAsync(key, Input(item: item), User));
        Assert.Equal("O item do pedido do cliente tem de 1 a 6 dígitos.", e.Message);
    }

    [Fact]
    public async Task Order_number_longer_than_15_is_refused()
    {
        var key = await SeedContractAsync();
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().SetAsync(key, Input(order: new string('A', 16)), User));
        Assert.Equal("O pedido do cliente tem no máximo 15 caracteres.", e.Message);
    }

    /// <summary>
    /// Spec §7: o administrador (flag IsAdmin) passa por cima da permissão. Usa o UserPermissionsService REAL sobre um
    /// CommonDbContext de teste, e não o fake, para provar o contrato de verdade.
    /// </summary>
    [Fact]
    public async Task Admin_without_the_permission_can_save_but_a_regular_user_cannot()
    {
        var key = await SeedContractAsync();
        using var common = new CommonDbContext(new DbContextOptionsBuilder<CommonDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        common.Users.Add(new SiagroB1.Domain.Entities.Common.User { Username = "chefe", FullName = "Chefe", IsAdmin = true });
        common.Users.Add(new SiagroB1.Domain.Entities.Common.User { Username = "comum", FullName = "Comum", IsAdmin = false });
        await common.SaveChangesAsync();

        var service = new SalesContractFiscalComplementService(
            _db, new UsageService(_db, NullLogger<UsageService>.Instance), new UserPermissionsService(common));

        var saved = await service.SetAsync(key, Input(order: "PED-9"), "chefe");
        Assert.Equal("PED-9", saved.CustomerOrderNumber);

        var e = await Assert.ThrowsAsync<DefaultException>(() => service.SetAsync(key, Input(), "comum"));
        Assert.Equal("Você não tem permissão para alterar o complemento fiscal do contrato.", e.Message);
    }

    [Fact]
    public async Task Set_creates_then_updates_and_stamps_the_user()
    {
        var key = await SeedContractAsync();
        var usage = await SeedUsageAsync();

        await Service().SetAsync(key, Input(usage: usage, order: "PED-1", item: "10"), "ana");
        var second = await Service().SetAsync(key, Input(usage: usage, order: "PED-2", item: "20"), "bia");

        Assert.Equal(1, await _db.Context.SalesContractFiscalComplements.CountAsync());
        Assert.Equal("bia", second.UpdatedBy);
        Assert.Equal("PED-2", second.CustomerOrderNumber);
        Assert.NotNull(second.UpdatedAt);
    }

    [Fact]
    public async Task Get_returns_names_and_completeness()
    {
        var key = await SeedContractAsync();
        var usage = await SeedUsageAsync();
        var condition = await SeedConditionAsync();

        var complete = await Service().SetAsync(key, Input(usage: usage, condition: condition), User);
        Assert.Equal("Venda de grãos", complete.UsageName);
        Assert.Equal("30 dias", complete.PaymentConditionName);
        Assert.True(complete.IsComplete);

        var partial = await Service().SetAsync(key, Input(usage: usage), User);
        Assert.False(partial.IsComplete);
        Assert.False((await Service().GetAsync(key))!.IsComplete);
    }

    [Fact]
    public async Task Get_without_row_returns_null() =>
        Assert.Null(await Service().GetAsync(Guid.NewGuid()));

    [Fact]
    public async Task Deleting_the_contract_deletes_the_complement()
    {
        var key = await SeedContractAsync();
        await Service().SetAsync(key, Input(), User);

        var contract = await _db.Context.SalesContracts.SingleAsync(x => x.Key == key);
        _db.Context.SalesContracts.Remove(contract);
        await _db.Context.SaveChangesAsync();

        Assert.Empty(await _db.Context.SalesContractFiscalComplements.ToListAsync());
    }
}
