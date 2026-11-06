using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Market.MarketTradeTicketTestHelpers;

namespace SiagroB1.Application.Tests.Market;

public class MarketTradeTicketContractServiceTests
{
    private const string Approve = PermissionCodes.MarketTicketApprove;

    /// <summary>Boleta pendente apontando para um contrato já criado (tentativa anterior parada no meio).</summary>
    private static async Task<(UnitOfWork Db, int TicketId, Guid ContractKey)> SeedHalfDoneAsync(
        ContractStatus status, DateTime? cashFlowDate)
    {
        var db = await SeedTicketScenarioAsync();
        var ticket = await TicketService(db).CreateAsync(Input(), "comprador", Today, CancellationToken.None);
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-1", CardCode = "F0001", CardName = "PRODUTOR TESTE", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "26/27", DeliveryLocationCode = "01", AgentCode = 1,
            TotalVolume = 100_000m, StandardPrice = 2.2945m, StandardCashFlowDate = cashFlowDate, Type = ContractType.Fixed,
            Status = status, StandardCurrency = CurrencyType.Brl,
        };
        contract.PriceFixations.Add(new PurchaseContractPriceFixation
        {
            FixationVolume = 100_000m, FixationPrice = 2.2945m, Status = PriceFixationStatus.Confirmed,
            FixationDate = new DateTime(2026, 11, 6),
        });
        db.Context.PurchaseContracts.Add(contract);
        ticket.ApprovedBy = "gerente";
        ticket.PurchaseContractKey = contract.Key;
        ticket.PurchaseContractCode = contract.Code;
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        return (db, ticket.Id, contract.Key);
    }

    [Theory]
    [InlineData(ContractStatus.Draft)]
    [InlineData(ContractStatus.InApproval)]
    public async Task Generate_ResumesAfterFailure(ContractStatus status)
    {
        var (db, id, key) = await SeedHalfDoneAsync(status, new DateTime(2027, 4, 15));

        var result = await TicketService(db, Approve).ApproveAsync(id, "gerente", CancellationToken.None);

        Assert.Equal(MarketTradeTicketStatus.Approved, result.Status);
        var contract = await db.Context.PurchaseContracts.AsNoTracking().SingleAsync();
        Assert.Equal(key, contract.Key);
        Assert.Equal(ContractStatus.Approved, contract.Status);
        Assert.Single(db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task Generate_FailureKeepsTicketOpen_AndRetryFinishes()
    {
        // Sem previsão de pagamento a aprovação do contrato falha: a boleta não pode virar Aprovada.
        var (db, id, key) = await SeedHalfDoneAsync(ContractStatus.Draft, null);

        var e = await Assert.ThrowsAsync<BusinessException>(() =>
            TicketService(db, Approve).ApproveAsync(id, "gerente", CancellationToken.None));
        Assert.Contains("previsão de pagamento", e.Message);

        var ticket = await db.Context.MarketTradeTickets.AsNoTracking().SingleAsync();
        Assert.Equal(MarketTradeTicketStatus.Pending, ticket.Status);
        Assert.Equal(key, ticket.PurchaseContractKey);
        Assert.Equal(ContractStatus.InApproval, (await db.Context.PurchaseContracts.AsNoTracking().SingleAsync()).Status);

        var contract = await db.Context.PurchaseContracts.SingleAsync();
        contract.StandardCashFlowDate = new DateTime(2027, 4, 15);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var result = await TicketService(db, Approve).ApproveAsync(id, "gerente", CancellationToken.None);
        Assert.Equal(MarketTradeTicketStatus.Approved, result.Status);
        Assert.Single(db.Context.PurchaseContracts);
    }

    [Theory]
    [InlineData(ContractStatus.Canceled)]
    [InlineData(ContractStatus.Rejected)]
    public async Task Generate_UnusableContract_Rejected(ContractStatus status)
    {
        var (db, id, _) = await SeedHalfDoneAsync(status, new DateTime(2027, 4, 15));

        var e = await Assert.ThrowsAsync<BusinessException>(() =>
            TicketService(db, Approve).ApproveAsync(id, "gerente", CancellationToken.None));
        Assert.Contains("não pode ser usado", e.Message);
        Assert.Equal(MarketTradeTicketStatus.Pending, (await db.Context.MarketTradeTickets.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Generate_DeletedContract_Rejected()
    {
        var (db, id, _) = await SeedHalfDoneAsync(ContractStatus.Draft, null);
        db.Context.PurchaseContracts.Remove(await db.Context.PurchaseContracts.Include(c => c.PriceFixations).SingleAsync());
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var e = await Assert.ThrowsAsync<BusinessException>(() =>
            TicketService(db, Approve).ApproveAsync(id, "gerente", CancellationToken.None));
        Assert.Contains("não pode ser usado", e.Message);
    }

    [Theory]
    [InlineData(ContractStatus.Draft)]
    [InlineData(ContractStatus.InApproval)]
    public async Task RejectAndCancel_BlockedWhileLinkedContractIsLive(ContractStatus status)
    {
        var (db, id, _) = await SeedHalfDoneAsync(status, new DateTime(2027, 4, 15));

        var reject = await Assert.ThrowsAsync<BusinessException>(() =>
            TicketService(db, Approve).RejectAsync(id, "não", "gerente", CancellationToken.None));
        Assert.Contains("já gerou o contrato PC-1", reject.Message);
        var cancel = await Assert.ThrowsAsync<BusinessException>(() =>
            TicketService(db, Approve).CancelAsync(id, "comprador", CancellationToken.None));
        Assert.Contains("antes de cancelar", cancel.Message);
        Assert.Equal(MarketTradeTicketStatus.Pending, (await db.Context.MarketTradeTickets.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Reject_AllowedAfterLinkedContractCanceled()
    {
        var (db, id, _) = await SeedHalfDoneAsync(ContractStatus.Canceled, null);

        var rejected = await TicketService(db, Approve).RejectAsync(id, "não", "gerente", CancellationToken.None);
        Assert.Equal(MarketTradeTicketStatus.Rejected, rejected.Status);
    }

    [Fact]
    public async Task Cancel_AllowedAfterLinkedContractDeleted()
    {
        var (db, id, _) = await SeedHalfDoneAsync(ContractStatus.Draft, null);
        db.Context.PurchaseContracts.Remove(await db.Context.PurchaseContracts.Include(c => c.PriceFixations).SingleAsync());
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var canceled = await TicketService(db).CancelAsync(id, "comprador", CancellationToken.None);
        Assert.Equal(MarketTradeTicketStatus.Canceled, canceled.Status);
    }

    [Fact]
    public async Task Generate_KeySaveFailure_ReportsCreatedContractCode()
    {
        var name = Guid.NewGuid().ToString();
        var seed = await SeedTicketScenarioAsync(databaseName: name);
        var ticket = await TicketService(seed).CreateAsync(Input(), "comprador", Today, CancellationToken.None);
        var db = TestDb.CreateUnitOfWork(name, new ThrowOnSaveInterceptor(e =>
            e.Entity is MarketTradeTicket { PurchaseContractKey: not null } && e.State == EntityState.Modified));

        var e = await Assert.ThrowsAsync<BusinessException>(() =>
            TicketService(db, Approve).ApproveAsync(ticket.Id, "gerente", CancellationToken.None));

        var code = (await db.Context.PurchaseContracts.AsNoTracking().SingleAsync()).Code;
        Assert.Contains($"O contrato {code} foi criado", e.Message);
    }
}
