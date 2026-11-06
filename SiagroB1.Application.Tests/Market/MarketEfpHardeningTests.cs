using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Market.Hedges;
using SiagroB1.Application.Services.Market.Tickets;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Market.MarketFuturesTestHelpers;

namespace SiagroB1.Application.Tests.Market;

/// <summary>
/// Endurecimento do EFP (revisão final da parte 3): contexto velho, deadlock, concorrência no mesmo contrato, falha
/// depois da primeira gravação, trava das ações manuais de fixação e estorno que cai para a rejeição.
/// </summary>
public class MarketEfpHardeningTests
{
    private static readonly Guid PafPurchaseKey = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid PafSalesKey = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>SqlException real com o número informado (o tipo não tem construtor público).</summary>
    internal static SqlException SqlError(int number)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var errorCtor = typeof(SqlError).GetConstructors(flags).OrderByDescending(c => c.GetParameters().Length).First();
        var firstInt = true;
        var args = errorCtor.GetParameters().Select(p =>
        {
            if (p.ParameterType == typeof(int) && firstInt)
            {
                firstInt = false;
                return (object?)number;
            }

            if (p.ParameterType == typeof(string)) return "simulado";
            if (p.ParameterType == typeof(Exception)) return null;
            return p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
        }).ToArray();
        var error = errorCtor.Invoke(args);
        var collection = Activator.CreateInstance(typeof(SqlErrorCollection), true)!;
        typeof(SqlErrorCollection).GetMethod("Add", flags)!.Invoke(collection, [error]);
        var create = typeof(SqlException).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .First(m => m.Name == "CreateException" && m.GetParameters().Length == 2
                                                    && m.GetParameters()[1].ParameterType == typeof(string));
        return (SqlException)create.Invoke(null, [collection, "16.0"])!;
    }

    /// <summary>Lança a exceção informada nas N primeiras gravações que casarem com o predicado.</summary>
    private sealed class ThrowInterceptor(Func<EntityEntry, bool> match, Func<Exception> error, int times)
        : SaveChangesInterceptor
    {
        public int Fired { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Fired < times && eventData.Context!.ChangeTracker.Entries().Any(match))
            {
                Fired++;
                throw error();
            }

            return new ValueTask<InterceptionResult<int>>(result);
        }
    }

    /// <summary>
    /// Concorrência otimista do contrato emulada no InMemory (que não gera RowVersion): depois de cada gravação feita
    /// com a fixação ainda em aprovação, "outro EFP" grava no contrato (FixedVolume muda no banco); a gravação da
    /// aprovação recusa um contrato rastreado com valor original diferente do banco — como o SQL Server faz com o
    /// RowVersion velho.
    /// </summary>
    private sealed class StaleContractInterceptor(string databaseName) : SaveChangesInterceptor
    {
        public int Bumps { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var tracker = eventData.Context!.ChangeTracker;
            var approving = tracker.Entries<PurchaseContractPriceFixation>()
                .Any(e => e.State == EntityState.Modified && e.Entity.Status == PriceFixationStatus.Confirmed);
            if (approving && Bumps > 0)
            {
                var fresh = TestDb.CreateUnitOfWork(databaseName);
                foreach (var entry in tracker.Entries<PurchaseContract>())
                {
                    var stored = fresh.Context.PurchaseContracts.AsNoTracking().Single(c => c.Key == entry.Entity.Key);
                    if ((decimal)entry.OriginalValues[nameof(PurchaseContract.FixedVolume)]! != stored.FixedVolume)
                        throw new DbUpdateConcurrencyException("Contrato alterado por outro processo (simulado).");
                }
            }

            return new ValueTask<InterceptionResult<int>>(result);
        }

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            var tracker = eventData.Context!.ChangeTracker;
            if (tracker.Entries<PurchaseContractPriceFixation>().Any(e => e.Entity.Status == PriceFixationStatus.InApproval)
                && tracker.Entries<PurchaseContract>().Any())
            {
                var fresh = TestDb.CreateUnitOfWork(databaseName);
                var contract = fresh.Context.PurchaseContracts.Single(c => c.Key == PafPurchaseKey);
                contract.FixedVolume += 1m;
                fresh.Context.SaveChanges();
                Bumps++;
            }

            return new ValueTask<int>(result);
        }
    }

    private static MarketEfpService Service(string name, IInterceptor? interceptor = null, KeyedAsyncLock? locks = null) =>
        MarketEfpServiceTests.Service(
            interceptor is null ? TestDb.CreateUnitOfWork(name) : TestDb.CreateUnitOfWork(name, interceptor), locks);

    private static UnitOfWork Db(string name) => TestDb.CreateUnitOfWork(name);

    private static bool Approving(EntityEntry e) =>
        e.Entity is PurchaseContractPriceFixation { Status: PriceFixationStatus.Confirmed } && e.State == EntityState.Modified;

    // ---------- H1: deadlock ----------

    private sealed class FakeSqlError(int number, Exception? inner = null) : Exception("fake", inner)
    {
        public int Number { get; } = number;
    }

    private static int? FakeNumber(Exception e) => e is FakeSqlError f ? f.Number : null;

    [Fact]
    public void IsDeadlock_FindsNumber1205AnywhereInTheChain()
    {
        Assert.True(MarketEfpService.IsDeadlock(new FakeSqlError(1205), FakeNumber));
        Assert.True(MarketEfpService.IsDeadlock(
            new DbUpdateException("x", new InvalidOperationException("y", new FakeSqlError(1205))), FakeNumber));
        Assert.True(MarketEfpService.IsDeadlock(
            new AggregateException(new InvalidOperationException(), new FakeSqlError(1205)), FakeNumber));
        Assert.True(MarketEfpService.IsDeadlock(new ApplicationException("x", new FakeSqlError(1205)), FakeNumber));
        Assert.False(MarketEfpService.IsDeadlock(new FakeSqlError(2627), FakeNumber));
        Assert.False(MarketEfpService.IsDeadlock(new InvalidOperationException("x"), FakeNumber));
    }

    [Fact]
    public void IsDeadlock_RealSqlException()
    {
        Assert.True(MarketEfpService.IsDeadlock(SqlError(1205)));
        Assert.True(MarketEfpService.IsDeadlock(new DbUpdateException("x", SqlError(1205))));
        Assert.False(MarketEfpService.IsDeadlock(SqlError(2627)));
    }

    [Fact]
    public async Task Create_DeadlockOnceOnApproval_RetriesAndCompletes()
    {
        var name = await MarketEfpServiceTests.SeedEfpAsync();
        var deadlock = new ThrowInterceptor(Approving, () => new DbUpdateException("deadlock", SqlError(1205)), 1);

        var efp = await Service(name, deadlock).CreateAsync(MarketEfpServiceTests.SoyInput(), "ana", CancellationToken.None);

        Assert.Equal(1, deadlock.Fired);
        Assert.Equal(MarketEfpStatus.Active, efp.Status);
        var c = Db(name).Context;
        Assert.Single(await c.MarketFuturesTrades.ToListAsync());
        Assert.Equal(PriceFixationStatus.Confirmed, (await c.PurchaseContractsPriceFixations.SingleAsync()).Status);
        Assert.Single(await c.FinancialDocuments.ToListAsync());
    }

    [Fact]
    public async Task Create_DeadlockTwice_PtBrMessage_ThenResumeCompletes()
    {
        var name = await MarketEfpServiceTests.SeedEfpAsync();
        var deadlock = new ThrowInterceptor(Approving, () => SqlError(1205), 2);

        var e = await Assert.ThrowsAsync<MarketEfpPendingException>(() =>
            Service(name, deadlock).CreateAsync(MarketEfpServiceTests.SoyInput(), "ana", CancellationToken.None));

        Assert.Equal(2, deadlock.Fired);
        Assert.Contains(MarketEfpService.BusyMessage, e.Message);
        Assert.StartsWith($"O EFP nº {e.EfpId} ficou em processamento:", e.Message);
        var resumed = await Service(name).ResumeAsync(e.EfpId, "ana", CancellationToken.None);
        Assert.Equal(MarketEfpStatus.Active, resumed.Status);
        Assert.Single(await Db(name).Context.PurchaseContractsPriceFixations.ToListAsync());
    }

    // ---------- H1: contexto velho ----------

    [Fact]
    public async Task Create_ContractChangedByAnotherEfpBetweenSteps_ApprovalUsesFreshContract()
    {
        var name = await MarketEfpServiceTests.SeedEfpAsync();
        var stale = new StaleContractInterceptor(name);

        var efp = await Service(name, stale).CreateAsync(MarketEfpServiceTests.SoyInput(), "ana", CancellationToken.None);

        Assert.True(stale.Bumps > 0);
        Assert.Equal(MarketEfpStatus.Active, efp.Status);
    }

    // ---------- H1: dois EFPs no mesmo contrato ----------

    [Fact]
    public async Task Create_TwoConcurrentEfpsSameContract_BothActiveNothingDuplicated()
    {
        var name = await MarketEfpServiceTests.SeedEfpAsync();
        var locks = new KeyedAsyncLock();

        var results = await Task.WhenAll(
            Task.Run(() => Service(name, locks: locks).CreateAsync(
                MarketEfpServiceTests.SoyInput(volumeKg: 100_000m), "ana", CancellationToken.None)),
            Task.Run(() => Service(name, locks: locks).CreateAsync(
                MarketEfpServiceTests.SoyInput(volumeKg: 120_000m), "bia", CancellationToken.None)));

        Assert.All(results, r => Assert.Equal(MarketEfpStatus.Active, r.Status));
        var c = Db(name).Context;
        Assert.Equal(2, await c.MarketFuturesTrades.CountAsync());
        var fixations = await c.PurchaseContractsPriceFixations.Where(f => f.PurchaseContractKey == PafPurchaseKey).ToListAsync();
        Assert.Equal(2, fixations.Count);
        Assert.All(fixations, f => Assert.Equal(PriceFixationStatus.Confirmed, f.Status));
        Assert.Equal(220_000m, (await c.PurchaseContracts.SingleAsync(x => x.Key == PafPurchaseKey)).FixedVolume);
    }

    // ---------- H2: falha depois da primeira gravação ----------

    [Fact]
    public async Task Create_UnexpectedFailureAfterFirstSave_SaysProcessingWithId()
    {
        var name = await MarketEfpServiceTests.SeedEfpAsync();
        var fail = new ThrowInterceptor(e => e.Entity is MarketFuturesTrade && e.State == EntityState.Added,
            () => new InvalidOperationException("boom"), 1);

        var e = await Assert.ThrowsAsync<MarketEfpPendingException>(() =>
            Service(name, fail).CreateAsync(MarketEfpServiceTests.SoyInput(), "ana", CancellationToken.None));

        var stored = await Db(name).Context.MarketEfps.AsNoTracking().SingleAsync();
        Assert.Equal(stored.Id, e.EfpId);
        Assert.Equal(
            $"O EFP nº {stored.Id} ficou em processamento: {MarketEfpService.UnexpectedReason} " +
            "Use Retomar ou Estornar na lista de EFP.", e.Message);
        Assert.IsType<InvalidOperationException>(e.Cause);
    }

    [Fact]
    public async Task Controller_FailureAfterFirstSave_400WithHeader()
    {
        var name = await MarketEfpServiceTests.SeedEfpAsync();
        var fail = new ThrowInterceptor(e => e.Entity is MarketFuturesTrade && e.State == EntityState.Added,
            () => new InvalidOperationException("boom"), 1);
        var controller = MarketEfpsControllerTests.Controller(TestDb.CreateUnitOfWork(name, fail));

        var result = await controller.Create(MarketEfpServiceTests.SoyInput(), CancellationToken.None);

        var content = Assert.IsType<Microsoft.AspNetCore.Mvc.ContentResult>(result);
        Assert.Equal(400, content.StatusCode);
        var id = (await Db(name).Context.MarketEfps.AsNoTracking().SingleAsync()).Id;
        Assert.Contains($"O EFP nº {id} ficou em processamento", content.Content);
        Assert.Equal(id.ToString(), controller.Response.Headers[SiagroB1.Web.Controllers.MarketEfpsController.EfpIdHeader]);
    }

    // ---------- H3: ações manuais de fixação ----------

    private sealed record Manual(
        PurchaseContractsPriceFixationsUpdateService PurchaseUpdate,
        PurchaseContractsPriceFixationDeleteService PurchaseDelete,
        PurchaseContractsPriceFixationsRejectService PurchaseReject,
        PurchaseContractsPriceFixationsCancelService PurchaseReverse,
        SalesContractsPriceFixationsUpdateService SalesUpdate,
        SalesContractsPriceFixationDeleteService SalesDelete,
        SalesContractsPriceFixationsRejectService SalesReject,
        SalesContractsPriceFixationsCancelService SalesReverse);

    /// <summary>Os serviços manuais como o DI monta: guarda ligado, escopo do EFP desligado.</summary>
    private static Manual ManualServices(string name)
    {
        var c = Db(name).Context;
        var guard = new MarketEfpFixationGuard(c, new MarketEfpFixationScope());
        var outbox = TestNotificationOutbox.For(c);
        var cancel = FinancialDocumentTestServices.Cancel(c);
        var pFixed = new PurchaseContractsFixedVolumeService(c);
        var pLog = new PurchaseContractsChangeLogService(c);
        var sFixed = new SalesContractsFixedVolumeService(c);
        var sLog = new SalesContractsChangeLogService(c);
        return new Manual(
            new PurchaseContractsPriceFixationsUpdateService(c, pFixed,
                NullLogger<PurchaseContractsPriceFixationsUpdateService>.Instance, guard),
            new PurchaseContractsPriceFixationDeleteService(c, pFixed, pLog,
                NullLogger<PurchaseContractsPriceFixationDeleteService>.Instance, guard),
            new PurchaseContractsPriceFixationsRejectService(c, pFixed, pLog, outbox, guard),
            new PurchaseContractsPriceFixationsCancelService(c, pFixed, pLog, outbox, cancel, guard),
            new SalesContractsPriceFixationsUpdateService(c, sFixed,
                NullLogger<SalesContractsPriceFixationsUpdateService>.Instance, guard),
            new SalesContractsPriceFixationDeleteService(c, sFixed, sLog,
                NullLogger<SalesContractsPriceFixationDeleteService>.Instance, guard),
            new SalesContractsPriceFixationsRejectService(c, sFixed, sLog, outbox, guard),
            new SalesContractsPriceFixationsCancelService(c, sFixed, sLog, outbox, cancel, guard));
    }

    private static async Task SetEfpStatusAsync(string name, int id, MarketEfpStatus status)
    {
        var c = Db(name).Context;
        (await c.MarketEfps.SingleAsync(e => e.Id == id)).Status = status;
        await c.SaveChangesAsync();
    }

    private static async Task AssertRefusedAsync(Func<Task> action, int efpId)
    {
        var e = await Assert.ThrowsAsync<ApplicationException>(action);
        Assert.Equal($"Esta fixação foi gerada pelo EFP nº {efpId}. Estorne pelo EFP (Trading → EFP).", e.Message);
    }

    /// <summary>EFP Processing com a fixação EM APROVAÇÃO (a aprovação falhou).</summary>
    private static async Task<(MarketEfp Efp, Guid Key)> InApprovalEfpAsync(string name, bool sales)
    {
        var input = MarketEfpServiceTests.SoyInput(sales ? PafSalesKey : PafPurchaseKey,
            side: sales ? MarketEfpContractSide.Sales : MarketEfpContractSide.Purchase);
        var fail = new ThrowInterceptor(e =>
            (e.Entity is PurchaseContractPriceFixation { Status: PriceFixationStatus.Confirmed }
             || e.Entity is SalesContractPriceFixation { Status: PriceFixationStatus.Confirmed })
            && e.State == EntityState.Modified, () => new InvalidOperationException("falha"), 1);
        await Assert.ThrowsAsync<MarketEfpPendingException>(() => Service(name, fail).CreateAsync(input, "ana", CancellationToken.None));
        var efp = await Db(name).Context.MarketEfps.AsNoTracking().SingleAsync();
        return (efp, efp.FixationKey!.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualReverse_RefusedWhileEfpActive_AllowedAfterEfpCanceled(bool sales)
    {
        var name = await MarketEfpServiceTests.SeedEfpAsync();
        var input = MarketEfpServiceTests.SoyInput(sales ? PafSalesKey : PafPurchaseKey,
            side: sales ? MarketEfpContractSide.Sales : MarketEfpContractSide.Purchase);
        var efp = await Service(name).CreateAsync(input, "ana", CancellationToken.None);
        var key = efp.FixationKey!.Value;

        Func<Manual, Task> reverse = m => sales ? m.SalesReverse.ExecuteAsync(key, "bia") : m.PurchaseReverse.ExecuteAsync(key, "bia");
        await AssertRefusedAsync(() => reverse(ManualServices(name)), efp.Id);

        await SetEfpStatusAsync(name, efp.Id, MarketEfpStatus.Canceled);
        await reverse(ManualServices(name));
        var c = Db(name).Context;
        var status = sales
            ? (await c.SalesContractsPriceFixations.SingleAsync()).Status
            : (await c.PurchaseContractsPriceFixations.SingleAsync()).Status;
        Assert.Equal(PriceFixationStatus.InApproval, status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualUpdateAndDelete_RefusedWhileEfpProcessing_AllowedAfterEfpCanceled(bool sales)
    {
        var name = await MarketEfpServiceTests.SeedEfpAsync();
        var (efp, key) = await InApprovalEfpAsync(name, sales);

        async Task Update(Manual m)
        {
            var c = Db(name).Context;
            if (sales)
                await m.SalesUpdate.ExecuteAsync(key, await c.SalesContractsPriceFixations.AsNoTracking().SingleAsync());
            else
                await m.PurchaseUpdate.ExecuteAsync(key, await c.PurchaseContractsPriceFixations.AsNoTracking().SingleAsync());
        }

        Func<Manual, Task> delete = m => sales ? m.SalesDelete.ExecuteAsync(key, "bia") : m.PurchaseDelete.ExecuteAsync(key, "bia");

        await AssertRefusedAsync(() => Update(ManualServices(name)), efp.Id);
        await AssertRefusedAsync(() => delete(ManualServices(name)), efp.Id);

        await SetEfpStatusAsync(name, efp.Id, MarketEfpStatus.Canceled);
        await Update(ManualServices(name));
        await delete(ManualServices(name));
        var c2 = Db(name).Context;
        Assert.Empty(sales
            ? (await c2.SalesContractsPriceFixations.ToListAsync()).Cast<object>()
            : (await c2.PurchaseContractsPriceFixations.ToListAsync()).Cast<object>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualReject_RefusedWhileEfpProcessing_AllowedAfterEfpCanceled(bool sales)
    {
        var name = await MarketEfpServiceTests.SeedEfpAsync();
        var (efp, key) = await InApprovalEfpAsync(name, sales);
        Func<Manual, Task> reject = m => sales
            ? m.SalesReject.ExecuteAsync(key, "não", "bia")
            : m.PurchaseReject.ExecuteAsync(key, "não", "bia");

        await AssertRefusedAsync(() => reject(ManualServices(name)), efp.Id);

        await SetEfpStatusAsync(name, efp.Id, MarketEfpStatus.Canceled);
        await reject(ManualServices(name));
        var c = Db(name).Context;
        var status = sales
            ? (await c.SalesContractsPriceFixations.SingleAsync()).Status
            : (await c.PurchaseContractsPriceFixations.SingleAsync()).Status;
        Assert.Equal(PriceFixationStatus.Rejected, status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EfpCancel_PassesTheGuard(bool sales)
    {
        var name = await MarketEfpServiceTests.SeedEfpAsync();
        var input = MarketEfpServiceTests.SoyInput(sales ? PafSalesKey : PafPurchaseKey,
            side: sales ? MarketEfpContractSide.Sales : MarketEfpContractSide.Purchase);
        var efp = await Service(name).CreateAsync(input, "ana", CancellationToken.None);

        var canceled = await Service(name).CancelAsync(efp.Id, "bia", CancellationToken.None);

        Assert.Equal(MarketEfpStatus.Canceled, canceled.Status);
        var c = Db(name).Context;
        Assert.Empty(await c.PurchaseContractsPriceFixations.ToListAsync());
        Assert.Empty(await c.SalesContractsPriceFixations.ToListAsync());
    }

    [Fact]
    public async Task Guard_NoEfpRow_DoesNothing()
    {
        var name = await MarketEfpServiceTests.SeedEfpAsync();
        var guard = new MarketEfpFixationGuard(Db(name).Context, new MarketEfpFixationScope());
        await guard.EnsureNotEfpFixationAsync(Guid.NewGuid());
    }

    // ---------- H8: exclusão recusada cai para a rejeição ----------

    [Fact]
    public async Task Cancel_InApprovalFixationWithWashout_RejectsInsteadOfDeleting()
    {
        var name = await MarketEfpServiceTests.SeedEfpAsync();
        var (efp, key) = await InApprovalEfpAsync(name, false);
        var c = Db(name).Context;
        c.PurchaseContractsWashouts.Add(new PurchaseContractWashout
        {
            Key = Guid.NewGuid(), PurchaseContractKey = PafPurchaseKey, PriceFixationKey = key, Sequence = 1,
            Status = PurchaseContractWashoutStatus.Rejected,
        });
        await c.SaveChangesAsync();

        var canceled = await Service(name).CancelAsync(efp.Id, "bia", CancellationToken.None);

        Assert.Equal(MarketEfpStatus.Canceled, canceled.Status);
        var r = Db(name).Context;
        Assert.Equal(PriceFixationStatus.Rejected, (await r.PurchaseContractsPriceFixations.SingleAsync()).Status);
        Assert.Empty(await r.MarketFuturesTrades.ToListAsync());
    }

    // ---------- H7: insumos arredondados ----------

    [Fact]
    public async Task Create_RoundsInputsToSixPlacesBeforePricing()
    {
        var name = await MarketEfpServiceTests.SeedEfpAsync();
        var input = MarketEfpServiceTests.SoyInput(fxRate: 5.4000004m, price: 1190.0000004m) with { Premium = 60.0000004m };

        var efp = await Service(name).CreateAsync(input, "ana", CancellationToken.None);

        Assert.Equal(1190m, efp.Price);
        Assert.Equal(60m, efp.Premium);
        Assert.Equal(5.4m, efp.FxRate);
        Assert.Equal(MarketEfpService.FixationPricePerKg(MarketQuoteUnit.UsCentsPerBushel, 0.36745m, efp.Price,
            efp.Premium, efp.FxRate!.Value), efp.FixationPrice);
        Assert.Equal(2.48028750m, efp.FixationPrice);
    }
}
