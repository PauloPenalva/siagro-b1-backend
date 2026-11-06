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
    public async Task Generate_DeletedContract_RecreatedWithSameKey()
    {
        var (db, id, key) = await SeedHalfDoneAsync(ContractStatus.Draft, null);
        db.Context.PurchaseContracts.Remove(await db.Context.PurchaseContracts.Include(c => c.PriceFixations).SingleAsync());
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var result = await TicketService(db, Approve).ApproveAsync(id, "gerente", CancellationToken.None);

        Assert.Equal(MarketTradeTicketStatus.Approved, result.Status);
        var contract = await db.Context.PurchaseContracts.AsNoTracking().SingleAsync();
        Assert.Equal(key, contract.Key);
        Assert.Equal(contract.Code, result.PurchaseContractCode);
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
    public async Task Approve_S1Ticket_GeneratedContractCarriesMarketReference()
    {
        var db = await SeedTicketScenarioAsync();
        var ticket = await TicketService(db).CreateAsync(Input(), "comprador", Today, CancellationToken.None);

        await TicketService(db, Approve).ApproveAsync(ticket.Id, "gerente", CancellationToken.None);

        var contract = await db.Context.PurchaseContracts.AsNoTracking().SingleAsync();
        Assert.Equal("ZSF7", contract.MarketInstrumentCode);
        Assert.Equal(1188.25m, contract.MarketExchangePrice);
        Assert.Equal(50m, contract.MarketPremium);
        Assert.Equal(5.7267m, contract.MarketFxRate);
        Assert.Null(contract.MarketUsdPrice);
        Assert.False(contract.MarketFxLocked);
    }

    [Fact]
    public async Task Approve_BrlInstrumentTicket_DoesNotStoreFxRate()
    {
        var db = await SeedTicketScenarioAsync();
        db.Context.MarketInstruments.Add(MarketPricingTestHelpers.Brl());
        var ticket = await TicketService(db).CreateAsync(Input(), "comprador", Today, CancellationToken.None);
        var saved = await db.Context.MarketTradeTickets.SingleAsync();
        saved.InstrumentCode = "CCMF27";
        saved.NetFx = 1m;
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        await TicketService(db, Approve).ApproveAsync(ticket.Id, "gerente", CancellationToken.None);

        var contract = await db.Context.PurchaseContracts.AsNoTracking().SingleAsync();
        Assert.Equal("CCMF27", contract.MarketInstrumentCode);
        Assert.Null(contract.MarketFxRate);
        Assert.Equal(1188.25m, contract.MarketExchangePrice);
        Assert.Equal(50m, contract.MarketPremium);
    }

    [Fact]
    public async Task Approve_ClaimSavesPreAssignedKey_ContractCreatedWithIt()
    {
        var db = await SeedTicketScenarioAsync();
        var ticket = await TicketService(db).CreateAsync(Input(), "comprador", Today, CancellationToken.None);

        var result = await TicketService(db, Approve).ApproveAsync(ticket.Id, "gerente", CancellationToken.None);

        var contract = await db.Context.PurchaseContracts.AsNoTracking().SingleAsync();
        Assert.Equal(contract.Key, result.PurchaseContractKey);
        Assert.Equal(contract.Code, result.PurchaseContractCode);
    }

    [Fact]
    public async Task Approve_RequestAbortedBetweenCreateAndLink_StillOneContract()
    {
        var name = Guid.NewGuid().ToString();
        var seed = await SeedTicketScenarioAsync(databaseName: name);
        var ticket = await TicketService(seed).CreateAsync(Input(), "comprador", Today, CancellationToken.None);
        using var cts = new CancellationTokenSource();
        // O navegador "desconecta" no instante em que o contrato é gravado.
        var db = TestDb.CreateUnitOfWork(name, new CallbackOnSaveInterceptor(context =>
        {
            if (context.ChangeTracker.Entries<PurchaseContract>().Any(e => e.State == EntityState.Added))
                cts.Cancel();
        }));

        var result = await TicketService(db, Approve).ApproveAsync(ticket.Id, "gerente", cts.Token);

        Assert.True(cts.IsCancellationRequested);
        Assert.Equal(MarketTradeTicketStatus.Approved, result.Status);
        db.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<BusinessException>(() =>
            TicketService(db, Approve).ApproveAsync(ticket.Id, "gerente", CancellationToken.None));
        var contract = await db.Context.PurchaseContracts.AsNoTracking().SingleAsync();
        Assert.Equal(contract.Key, (await db.Context.MarketTradeTickets.AsNoTracking().SingleAsync()).PurchaseContractKey);
    }

    [Fact]
    public async Task Approve_FailureAfterCreate_RetryReusesContract()
    {
        var name = Guid.NewGuid().ToString();
        var seed = await SeedTicketScenarioAsync(databaseName: name);
        var ticket = await TicketService(seed).CreateAsync(Input(), "comprador", Today, CancellationToken.None);
        // Cai na gravação final (boleta Aprovada), depois de o contrato já existir e estar aprovado.
        var failing = TestDb.CreateUnitOfWork(name, new ThrowOnSaveInterceptor(e =>
            e.Entity is MarketTradeTicket { Status: MarketTradeTicketStatus.Approved } && e.State == EntityState.Modified));

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            TicketService(failing, Approve).ApproveAsync(ticket.Id, "gerente", CancellationToken.None));
        var pending = await failing.Context.MarketTradeTickets.AsNoTracking().SingleAsync();
        Assert.Equal(MarketTradeTicketStatus.Pending, pending.Status);
        Assert.NotNull(pending.PurchaseContractKey);

        var db = TestDb.CreateUnitOfWork(name);
        var result = await TicketService(db, Approve).ApproveAsync(ticket.Id, "gerente", CancellationToken.None);

        Assert.Equal(MarketTradeTicketStatus.Approved, result.Status);
        var contract = await db.Context.PurchaseContracts.AsNoTracking().SingleAsync();
        Assert.Equal(pending.PurchaseContractKey, contract.Key);
        Assert.Single(db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task Generate_KeyWithoutContract_CreatesWithThatKey()
    {
        var db = await SeedTicketScenarioAsync();
        var created = await TicketService(db).CreateAsync(Input(), "comprador", Today, CancellationToken.None);
        var key = Guid.NewGuid();
        created.PurchaseContractKey = key;
        await db.SaveChangesAsync();

        await ContractService(db).GenerateAsync(created, "gerente", CancellationToken.None);

        var contract = await db.Context.PurchaseContracts.AsNoTracking().SingleAsync();
        Assert.Equal(key, contract.Key);
        Assert.Equal(ContractStatus.Approved, contract.Status);
        Assert.Equal(contract.Code, created.PurchaseContractCode);
    }

    [Fact]
    public async Task Generate_SecondCreateWithSameKey_DoesNotCreateAnother()
    {
        var name = Guid.NewGuid().ToString();
        var db = await SeedTicketScenarioAsync(databaseName: name);
        var ticket = await TicketService(db).CreateAsync(Input(), "comprador", Today, CancellationToken.None);
        var key = Guid.NewGuid();
        ticket.PurchaseContractKey = key;
        await db.SaveChangesAsync();
        await ContractService(db).GenerateAsync(ticket, "gerente", CancellationToken.None);

        // Outro "processo" (contexto próprio) tenta criar o contrato da mesma chave por fora.
        var other = TestDb.CreateUnitOfWork(name);
        var twin = await other.Context.PurchaseContracts.AsNoTracking().SingleAsync();
        twin.Key = key;
        await Assert.ThrowsAnyAsync<Exception>(() =>
            CreateService(other).ExecuteAsync(CloneForCreate(twin), "x"));

        Assert.Equal(key, (await TestDb.CreateUnitOfWork(name).Context.PurchaseContracts.AsNoTracking().SingleAsync()).Key);
    }

    [Fact]
    public async Task RejectAndCancel_AllowedWithKeyButNoContract()
    {
        var db = await SeedTicketScenarioAsync();
        var first = await TicketService(db).CreateAsync(Input(), "comprador", Today, CancellationToken.None);
        var second = await TicketService(db).CreateAsync(Input(), "comprador", Today, CancellationToken.None);
        first.PurchaseContractKey = Guid.NewGuid();
        second.PurchaseContractKey = Guid.NewGuid();
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var rejected = await TicketService(db, Approve).RejectAsync(first.Id, "não", "gerente", CancellationToken.None);
        var canceled = await TicketService(db).CancelAsync(second.Id, "comprador", CancellationToken.None);

        Assert.Equal(MarketTradeTicketStatus.Rejected, rejected.Status);
        Assert.Equal(MarketTradeTicketStatus.Canceled, canceled.Status);
        Assert.Empty(db.Context.PurchaseContracts);
    }

    private static PurchaseContract CloneForCreate(PurchaseContract c) => new()
    {
        Key = c.Key, DocNumberKey = c.DocNumberKey, CardCode = c.CardCode, ItemCode = c.ItemCode,
        UnitOfMeasureCode = c.UnitOfMeasureCode, HarvestSeasonCode = c.HarvestSeasonCode,
        DeliveryLocationCode = c.DeliveryLocationCode, AgentCode = c.AgentCode, TotalVolume = c.TotalVolume,
        StandardPrice = c.StandardPrice, Type = ContractType.Fixed, StandardCurrency = CurrencyType.Brl,
        DeliveryStartDate = c.DeliveryStartDate, DeliveryEndDate = c.DeliveryEndDate,
    };

    /// <summary>Executa uma ação no início de cada SaveChanges (para simular eventos no meio do fluxo).</summary>
    private sealed class CallbackOnSaveInterceptor(Action<DbContext> onSaving)
        : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not null)
                onSaving(eventData.Context);
            return ValueTask.FromResult(result);
        }
    }
}
