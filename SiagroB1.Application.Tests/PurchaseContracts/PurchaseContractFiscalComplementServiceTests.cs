using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractFiscalComplementServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private const string User = "fiscal";

    private UsageService Usages() => new(_db, NullLogger<UsageService>.Instance);

    private PurchaseContractFiscalComplementService Service(bool granted = true, string mode = "STANDALONE") =>
        new(_db, Usages(),
            granted ? new FakeUserPermissions(PermissionCodes.PurchaseContractFiscalEdit) : new FakeUserPermissions(),
            new PurchaseContractsChangeLogService(_db.Context),
            TestConfiguration.Erp(mode));

    private async Task<Guid> SeedContractAsync(ContractStatus status = ContractStatus.Draft)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-001", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", DeliveryLocationCode = "01", Status = status,
        };
        _db.Context.PurchaseContracts.Add(contract);
        await _db.Context.SaveChangesAsync();
        return contract.Key;
    }

    private async Task<int> SeedUsageAsync(
        string name = "Compra produtor PF", UsageDirection direction = UsageDirection.Incoming, bool inactive = false,
        decimal? inss = 1.32m, decimal? rat = 0.11m, decimal? senar = 0.20m)
    {
        var model = new UsageModel
        {
            Name = name, Direction = direction, Inactive = inactive,
            FunruralInssRate = inss, FunruralRatRate = rat, FunruralSenarRate = senar,
        };
        if (direction == UsageDirection.Incoming) { model.CfopIncomingInState = "1102"; model.CfopIncomingOutState = "2102"; }
        else { model.CfopOutgoingInState = "5102"; model.CfopOutgoingOutState = "6102"; }
        return (await Usages().CreateAsync(model)).Code;
    }

    [Fact]
    public async Task Without_permission_is_refused()
    {
        var key = await SeedContractAsync();
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(granted: false).SetAsync(key, null, User));
        Assert.Equal("Você não tem permissão para alterar o complemento fiscal do contrato.", e.Message);
    }

    [Fact]
    public async Task Sapb1_mode_is_refused()
    {
        var key = await SeedContractAsync();
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(mode: "SAPB1").SetAsync(key, null, User));
        Assert.Equal("Complemento fiscal do contrato de compra não se aplica ao modo SAPB1.", e.Message);
    }

    [Fact]
    public async Task Unknown_contract_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Service().SetAsync(Guid.NewGuid(), null, User));
    }

    [Theory]
    [InlineData(ContractStatus.Finished)]
    [InlineData(ContractStatus.Canceled)]
    public async Task Finished_or_canceled_contract_is_refused(ContractStatus status)
    {
        var key = await SeedContractAsync(status);
        var usage = await SeedUsageAsync();
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().SetAsync(key, usage, User));
        Assert.Equal("Contrato encerrado ou cancelado: o complemento fiscal não pode ser alterado.", e.Message);
    }

    [Fact]
    public async Task Outgoing_usage_is_refused()
    {
        var key = await SeedContractAsync();
        var usage = await SeedUsageAsync(direction: UsageDirection.Outgoing);
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().SetAsync(key, usage, User));
        Assert.Equal($"A natureza de operação {usage} não é uma natureza de entrada ativa.", e.Message);
    }

    [Fact]
    public async Task Inactive_usage_is_refused()
    {
        var key = await SeedContractAsync();
        var usage = await SeedUsageAsync(inactive: true);
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().SetAsync(key, usage, User));
        Assert.Equal($"A natureza de operação {usage} não é uma natureza de entrada ativa.", e.Message);
    }

    [Fact]
    public async Task Unknown_usage_is_refused()
    {
        var key = await SeedContractAsync();
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().SetAsync(key, 999, User));
        Assert.Equal("A natureza de operação 999 não é uma natureza de entrada ativa.", e.Message);
    }

    [Fact]
    public async Task Usage_without_funrural_rates_is_accepted()
    {
        var key = await SeedContractAsync();
        var usage = await SeedUsageAsync(name: "Compra sem retenção", inss: null, rat: null, senar: null);

        var dto = await Service().SetAsync(key, usage, User);

        Assert.Equal(usage, dto.UsageCode);
        Assert.True(dto.IsComplete);
        Assert.Null(dto.FunruralInssRate);
    }

    [Fact]
    public async Task Set_creates_the_complement_and_returns_names_and_rates()
    {
        var key = await SeedContractAsync();
        var usage = await SeedUsageAsync();

        var dto = await Service().SetAsync(key, usage, User);

        Assert.Equal("Compra produtor PF", dto.UsageName);
        Assert.Equal(1.32m, dto.FunruralInssRate);
        Assert.Equal(0.11m, dto.FunruralRatRate);
        Assert.Equal(0.20m, dto.FunruralSenarRate);
        Assert.Equal(User, dto.UpdatedBy);
        Assert.True(dto.IsComplete);
    }

    [Fact]
    public async Task Get_without_complement_returns_null()
    {
        var key = await SeedContractAsync();
        Assert.Null(await Service().GetAsync(key));
    }

    [Fact]
    public async Task Changing_usage_logs_old_and_new()
    {
        var key = await SeedContractAsync(ContractStatus.Approved);
        var first = await SeedUsageAsync(name: "Compra PF");
        var second = await SeedUsageAsync(name: "Compra PJ", inss: 1.98m, rat: null, senar: 0.25m);

        await Service().SetAsync(key, first, User);
        await Service().SetAsync(key, second, User);

        var logs = await _db.Context.PurchaseContractsChangeLogs.AsNoTracking()
            .Where(x => x.PurchaseContractKey == key && x.Field == ContractChangeLogFields.Usage)
            .OrderBy(x => x.ChangedAt).ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.Null(logs[0].OldValue);
        Assert.Equal($"{first} - Compra PF", logs[0].NewValue);
        Assert.Equal($"{first} - Compra PF", logs[1].OldValue);
        Assert.Equal($"{second} - Compra PJ", logs[1].NewValue);
    }

    [Fact]
    public async Task Saving_same_usage_does_not_log()
    {
        var key = await SeedContractAsync();
        var usage = await SeedUsageAsync();

        await Service().SetAsync(key, usage, User);
        await Service().SetAsync(key, usage, User);

        Assert.Equal(1, await _db.Context.PurchaseContractsChangeLogs
            .CountAsync(x => x.PurchaseContractKey == key && x.Field == ContractChangeLogFields.Usage));
    }

    [Fact]
    public async Task Clearing_on_draft_is_accepted()
    {
        var key = await SeedContractAsync();
        var usage = await SeedUsageAsync();
        await Service().SetAsync(key, usage, User);

        var dto = await Service().SetAsync(key, null, User);

        Assert.Null(dto.UsageCode);
        Assert.False(dto.IsComplete);
    }

    [Fact]
    public async Task Clearing_on_approved_contract_is_refused()
    {
        var key = await SeedContractAsync(ContractStatus.Approved);
        var usage = await SeedUsageAsync();
        await Service().SetAsync(key, usage, User);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().SetAsync(key, null, User));
        Assert.Equal("Contrato aprovado precisa de natureza de operação: escolha outra em vez de limpar.", e.Message);
    }

    [Fact]
    public async Task Saving_an_inactivated_usage_again_is_refused()
    {
        var key = await SeedContractAsync();
        var usage = await SeedUsageAsync();
        await Service().SetAsync(key, usage, User);
        var row = await _db.Context.Usages.SingleAsync(x => x.Code == usage);
        row.Inactive = true;
        await _db.Context.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().SetAsync(key, usage, User));
        Assert.Equal($"A natureza de operação {usage} não é uma natureza de entrada ativa.", e.Message);
    }
}
