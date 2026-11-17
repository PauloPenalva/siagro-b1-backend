using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Sap;

/// <summary>
/// Sincronização do cancelamento da invoice do SAP (GAC-1226): o job roda a cada minuto e o serviço decide
/// pelo intervalo da empresa; cada documento é tratado em escopo de DI próprio (contexto novo, mesma base).
/// </summary>
public class SapInvoiceSyncServiceTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly SapErpDbContext _sap = new(
        new DbContextOptionsBuilder<SapErpDbContext>().UseInMemoryDatabase($"sap-{Guid.NewGuid()}").Options);

    private readonly TestClock _clock = new(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
    private readonly ServiceProvider _provider;
    private readonly AppDbContext _context;
    private readonly InMemorySapInvoiceSyncLock _lock = new();

    public SapInvoiceSyncServiceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new AppDbContext(TestDb.Options(_dbName)));
        services.AddScoped<UnitOfWork>(sp => new UnitOfWork(sp.GetRequiredService<AppDbContext>()));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<UnitOfWork>());
        services.AddScoped<ShipmentLoadsMovementLogService>();
        services.AddScoped<ShipmentLoadsBalanceHookService>();
        services.AddScoped<SalesShipmentReleasesRecalculateShippedService>();
        services.AddScoped<SalesContractsAllocationDeleteForInvoiceService>();
        services.AddScoped<SalesInvoiceTaxDocumentDuplicateGuard>();
        services.AddScoped<SalesInvoicesCancelService>();
        services.AddScoped<SalesInvoicesDeleteService>();
        services.AddScoped<SalesInvoicesChangeLogService>();
        services.AddScoped(sp => new ShipmentLoadsUnlinkSapCreditMemoService(
            sp.GetRequiredService<IUnitOfWork>(), sp.GetRequiredService<SalesInvoicesChangeLogService>(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Erp"] = "SAPB1" }).Build()));
        services.AddScoped<ICompanyContext>(_ => new FakeCompanyContext());
        _provider = services.BuildServiceProvider();
        _context = new AppDbContext(TestDb.Options(_dbName));
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
        // Fuso fixo -03:00 para distinguir horário local de UTC nas asserções.
        public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone("test-local", TimeSpan.FromHours(-3), "test-local", "test-local");
    }

    private sealed class FakeDbException() : DbException("SAP fora do ar");

    private sealed class FakeFiscal(
        bool available = false, Exception? failure = null, params SapFiscalDocumentInfo[] infos) : ISapFiscalDocumentSource
    {
        public bool IsAvailable => available;

        public Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForInvoicesAsync(
            IReadOnlyCollection<SapInvoiceRef> invoices, CancellationToken ct = default)
        {
            if (failure is not null) throw failure;
            var wanted = invoices.Select(i => i.DocEntry).ToHashSet();
            return Task.FromResult<IReadOnlyDictionary<int, SapFiscalDocumentInfo>>(
                infos.Where(i => wanted.Contains(i.DocEntry)).ToDictionary(i => i.DocEntry));
        }
    }

    /// <summary>Costura de teste da leitura do SAP: simula o banco fora do ar e registra o tamanho dos lotes.</summary>
    private sealed class SpySapSyncService(
        AppDbContext context, SapErpDbContext sap, ISapFiscalDocumentSource fiscal,
        IServiceScopeFactory scopes, TimeProvider clock, ISapInvoiceSyncLock syncLock, Exception? sapFailure = null,
        Exception? creditMemoFailure = null)
        : SapInvoiceSyncService(context, sap, fiscal, scopes, clock, NullLogger<SapInvoiceSyncService>.Instance,
            new FakeCompanyContext(), syncLock)
    {
        public List<int> ChunkSizes { get; } = [];

        protected override Task<List<SapInvoiceStatus>> ReadSapCreditMemoChunkAsync(int[] docEntries, CancellationToken ct) =>
            creditMemoFailure is not null ? throw creditMemoFailure : base.ReadSapCreditMemoChunkAsync(docEntries, ct);

        protected override Task<List<SapInvoiceStatus>> ReadSapChunkAsync(int[] docEntries, CancellationToken ct)
        {
            ChunkSizes.Add(docEntries.Length);
            return sapFailure is not null ? throw sapFailure : base.ReadSapChunkAsync(docEntries, ct);
        }
    }

    /// <summary>Falha ao abrir o primeiro escopo: um problema inesperado num documento não pode parar os demais.</summary>
    private sealed class FlakyScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        private int _calls;

        public IServiceScope CreateScope() =>
            Interlocked.Increment(ref _calls) == 1 ? throw new InvalidOperationException("falha inesperada") : inner.CreateScope();
    }

    private SpySapSyncService Service(
        ISapFiscalDocumentSource? fiscal = null, Exception? sapFailure = null, IServiceScopeFactory? scopes = null,
        ISapInvoiceSyncLock? syncLock = null, Exception? creditMemoFailure = null) =>
        new(new AppDbContext(TestDb.Options(_dbName)), _sap, fiscal ?? new FakeFiscal(),
            scopes ?? _provider.GetRequiredService<IServiceScopeFactory>(), _clock, syncLock ?? _lock, sapFailure,
            creditMemoFailure);

    /// <summary>Add-on que só conhece NF de devolução (A/R Credit Memo); para invoice responde vazio.</summary>
    private sealed class CreditMemoFiscal(params SapFiscalDocumentInfo[] infos) : ISapFiscalDocumentSource
    {
        public bool IsAvailable => true;

        public Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForInvoicesAsync(
            IReadOnlyCollection<SapInvoiceRef> invoices, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<int, SapFiscalDocumentInfo>>(new Dictionary<int, SapFiscalDocumentInfo>());

        public Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForCreditMemosAsync(
            IReadOnlyCollection<SapInvoiceRef> creditMemos, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<int, SapFiscalDocumentInfo>>(infos.ToDictionary(i => i.DocEntry));
    }

    /// <summary>Devolução confirmada com a NF de devolução do SAP vinculada.</summary>
    private async Task<SalesInvoice> SeedLinkedReturnAsync(int docEntry, string? chave = null)
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = "C0001", BranchCode = "3", InvoiceNumber = $"DV{docEntry:D6}",
            InvoiceType = SalesInvoiceType.Return, InvoiceStatus = InvoiceStatus.Confirmed,
            SapCreditMemoDocEntry = docEntry, SapCreditMemoDocNum = docEntry + 2000,
            TaxDocumentNumber = "000004512", TaxDocumentSeries = "001", ChaveNFe = chave,
        };
        _context.SalesInvoices.Add(invoice);
        await _context.SaveChangesAsync();
        return invoice;
    }

    private async Task AddSapCreditMemoAsync(int docEntry, string canceled = "N", int branch = 3)
    {
        _sap.SapCreditMemos.Add(new SapCreditMemo
            { DocEntry = docEntry, DocNum = docEntry + 2000, BPLId = branch, Canceled = canceled });
        await _sap.SaveChangesAsync();
    }

    /// <summary>Trava que estoura ao ADQUIRIR (ex.: banco da empresa fora do ar).</summary>
    private sealed class ThrowingLock(Exception failure) : ISapInvoiceSyncLock
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(string companyCode, CancellationToken ct = default) =>
            throw failure;
    }

    // ---------- semeadura ----------

    private async Task<SalesInvoice> SeedInvoiceAsync(
        int docEntry, InvoiceStatus status = InvoiceStatus.Confirmed, string? chave = null)
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C0001",
            BranchCode = "3",
            InvoiceNumber = $"DS{docEntry:D6}",
            InvoiceStatus = status,
            SapDocEntry = docEntry,
            SapDocNum = docEntry + 1000,
            ChaveNFe = chave,
        };
        _context.SalesInvoices.Add(invoice);
        await _context.SaveChangesAsync();
        return invoice;
    }

    private async Task AddSapInvoiceAsync(int docEntry, string canceled = "N", int branch = 3)
    {
        _sap.SapInvoices.Add(new SapInvoice { DocEntry = docEntry, DocNum = docEntry + 1000, BPLId = branch, Canceled = canceled });
        await _sap.SaveChangesAsync();
    }

    private async Task SetSettingsAsync(
        bool enabled = true, int interval = 15, DateTime? lastRunAt = null)
    {
        _context.SapIntegrationSettings.Add(new SapIntegrationSettings
        {
            InvoiceSyncEnabled = enabled, InvoiceSyncIntervalMinutes = interval, InvoiceSyncLastRunAt = lastRunAt,
        });
        await _context.SaveChangesAsync();
    }

    private async Task<SalesInvoice> ReloadAsync(Guid key)
    {
        await using var fresh = new AppDbContext(TestDb.Options(_dbName));
        return await fresh.SalesInvoices.AsNoTracking().SingleAsync(x => x.Key == key);
    }

    private async Task<SapIntegrationSettings?> SettingsRowAsync()
    {
        await using var fresh = new AppDbContext(TestDb.Options(_dbName));
        return await fresh.SapIntegrationSettings.AsNoTracking().SingleOrDefaultAsync();
    }

    /// <summary>Uma devolução viva aponta para o documento: o cancelamento recusa ("possui retorno").</summary>
    private async Task<SalesInvoice> AddBlockingReturnAsync(Guid originKey)
    {
        var ret = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = "C0001", BranchCode = "3", InvoiceNumber = "RET000001",
            InvoiceType = SalesInvoiceType.Return, InvoiceStatus = InvoiceStatus.Pending, SalesInvoiceOriginKey = originKey,
        };
        _context.SalesInvoices.Add(ret);
        await _context.SaveChangesAsync();
        return ret;
    }

    private const string ReturnRefusal = "Documento de saída possui retorno.";

    // ---------- configurações ----------

    [Fact]
    public async Task Settings_without_a_row_return_the_defaults()
    {
        var service = new SapIntegrationSettingsService(TestDb.CreateUnitOfWork(_dbName));

        var model = await service.GetAsync();

        Assert.True(model.InvoiceSyncEnabled);
        Assert.Equal(15, model.InvoiceSyncIntervalMinutes);
        Assert.Null(model.InvoiceSyncLastRunAt);
        Assert.Null(await SettingsRowAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1441)]
    public async Task Settings_refuse_an_interval_outside_1_to_1440(int interval)
    {
        var service = new SapIntegrationSettingsService(TestDb.CreateUnitOfWork(_dbName));

        var error = await Assert.ThrowsAsync<DefaultException>(() => service.SaveAsync(
            new SapIntegrationSettingsModel { InvoiceSyncEnabled = true, InvoiceSyncIntervalMinutes = interval }));

        Assert.Equal("Informe um intervalo entre 1 e 1440 minutos.", error.Message);
        Assert.Null(await SettingsRowAsync());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1440)]
    public async Task Settings_accept_the_interval_limits_and_create_the_row(int interval)
    {
        var service = new SapIntegrationSettingsService(TestDb.CreateUnitOfWork(_dbName));

        var saved = await service.SaveAsync(
            new SapIntegrationSettingsModel { InvoiceSyncEnabled = false, InvoiceSyncIntervalMinutes = interval });

        Assert.False(saved.InvoiceSyncEnabled);
        Assert.Equal(interval, saved.InvoiceSyncIntervalMinutes);
        var row = await SettingsRowAsync();
        Assert.False(row!.InvoiceSyncEnabled);
        Assert.Equal(interval, row.InvoiceSyncIntervalMinutes);
    }

    [Fact]
    public async Task Settings_never_accept_the_last_run_from_the_client()
    {
        var lastRun = new DateTime(2026, 11, 16, 11, 0, 0);
        await SetSettingsAsync(lastRunAt: lastRun);
        var service = new SapIntegrationSettingsService(TestDb.CreateUnitOfWork(_dbName));

        var saved = await service.SaveAsync(new SapIntegrationSettingsModel
        {
            InvoiceSyncEnabled = true, InvoiceSyncIntervalMinutes = 30, InvoiceSyncLastRunAt = new DateTime(2000, 1, 1),
        });

        Assert.Equal(lastRun, saved.InvoiceSyncLastRunAt);
        Assert.Equal(lastRun, (await SettingsRowAsync())!.InvoiceSyncLastRunAt);
    }

    // ---------- intervalo ----------

    [Fact]
    public async Task Disabled_sync_does_nothing()
    {
        var invoice = await SeedInvoiceAsync(1);
        await AddSapInvoiceAsync(1, canceled: "Y");
        await SetSettingsAsync(enabled: false);

        await Service().ExecuteAsync(CancellationToken.None);

        Assert.Equal(InvoiceStatus.Confirmed, (await ReloadAsync(invoice.Key)).InvoiceStatus);
        Assert.Null((await SettingsRowAsync())!.InvoiceSyncLastRunAt);
    }

    [Fact]
    public async Task Sync_before_the_interval_elapsed_does_nothing()
    {
        var invoice = await SeedInvoiceAsync(1);
        await AddSapInvoiceAsync(1, canceled: "Y");
        var lastRun = _clock.GetLocalNow().DateTime.AddMinutes(-5);
        await SetSettingsAsync(interval: 15, lastRunAt: lastRun);

        await Service().ExecuteAsync(CancellationToken.None);

        Assert.Equal(InvoiceStatus.Confirmed, (await ReloadAsync(invoice.Key)).InvoiceStatus);
        Assert.Equal(lastRun, (await SettingsRowAsync())!.InvoiceSyncLastRunAt);
    }

    [Fact]
    public async Task Sync_after_the_interval_runs_and_stamps_the_last_run()
    {
        var invoice = await SeedInvoiceAsync(1);
        await AddSapInvoiceAsync(1, canceled: "Y");
        await SetSettingsAsync(interval: 15, lastRunAt: _clock.GetLocalNow().DateTime.AddMinutes(-15));

        await Service().ExecuteAsync(CancellationToken.None);

        Assert.Equal(InvoiceStatus.Cancelled, (await ReloadAsync(invoice.Key)).InvoiceStatus);
        Assert.Equal(_clock.GetLocalNow().DateTime, (await SettingsRowAsync())!.InvoiceSyncLastRunAt);
    }

    [Fact]
    public async Task Force_runs_even_when_disabled_and_inside_the_interval()
    {
        var invoice = await SeedInvoiceAsync(1);
        await AddSapInvoiceAsync(1, canceled: "Y");
        await SetSettingsAsync(enabled: false, interval: 60, lastRunAt: _clock.GetLocalNow().DateTime.AddMinutes(-1));

        await Service().ExecuteAsync(CancellationToken.None, force: true);

        Assert.Equal(InvoiceStatus.Cancelled, (await ReloadAsync(invoice.Key)).InvoiceStatus);
        Assert.Equal(_clock.GetLocalNow().DateTime, (await SettingsRowAsync())!.InvoiceSyncLastRunAt);
    }

    [Fact]
    public async Task Without_a_settings_row_it_runs_with_the_defaults_and_creates_the_row()
    {
        var invoice = await SeedInvoiceAsync(1);
        await AddSapInvoiceAsync(1, canceled: "Y");

        await Service().ExecuteAsync(CancellationToken.None);

        Assert.Equal(InvoiceStatus.Cancelled, (await ReloadAsync(invoice.Key)).InvoiceStatus);
        var row = await SettingsRowAsync();
        Assert.True(row!.InvoiceSyncEnabled);
        Assert.Equal(15, row.InvoiceSyncIntervalMinutes);
        Assert.Equal(_clock.GetLocalNow().DateTime, row.InvoiceSyncLastRunAt);
    }

    // ---------- cancelamento ----------

    [Fact]
    public async Task Invoice_canceled_in_oinv_cancels_the_confirmed_document()
    {
        var invoice = await SeedInvoiceAsync(1, chave: new string('1', 44));
        await AddSapInvoiceAsync(1, canceled: "Y");

        await Service().ExecuteAsync(CancellationToken.None);

        var saved = await ReloadAsync(invoice.Key);
        Assert.Equal(InvoiceStatus.Cancelled, saved.InvoiceStatus);
        Assert.Equal("sap-sync", saved.CanceledBy);
        Assert.False(saved.SapCancelPending);
    }

    [Fact]
    public async Task Invoice_cancelled_only_in_the_addon_cancels_the_document()
    {
        var invoice = await SeedInvoiceAsync(1);
        await AddSapInvoiceAsync(1, canceled: "N");
        var fiscal = new FakeFiscal(true, null, new SapFiscalDocumentInfo(1, null, true, true, null));

        await Service(fiscal).ExecuteAsync(CancellationToken.None);

        Assert.Equal(InvoiceStatus.Cancelled, (await ReloadAsync(invoice.Key)).InvoiceStatus);
    }

    [Fact]
    public async Task The_nf_is_free_after_the_sync_cancels_the_document()
    {
        var key = new string('7', 44);
        var cancelled = await SeedInvoiceAsync(1, chave: key);
        await AddSapInvoiceAsync(1, canceled: "Y");

        await Service().ExecuteAsync(CancellationToken.None);

        Assert.Equal(InvoiceStatus.Cancelled, (await ReloadAsync(cancelled.Key)).InvoiceStatus);
        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SalesInvoiceTaxDocumentDuplicateGuard>()
            .EnsureUniqueAsync("3", null, null, key, Guid.NewGuid());
    }

    [Fact]
    public async Task Active_invoices_and_invoices_missing_from_sap_are_left_alone()
    {
        var active = await SeedInvoiceAsync(1);
        var missing = await SeedInvoiceAsync(2);
        await AddSapInvoiceAsync(1, canceled: "N");

        await Service().ExecuteAsync(CancellationToken.None);

        Assert.Equal(InvoiceStatus.Confirmed, (await ReloadAsync(active.Key)).InvoiceStatus);
        var untouched = await ReloadAsync(missing.Key);
        Assert.Equal(InvoiceStatus.Confirmed, untouched.InvoiceStatus);
        Assert.False(untouched.SapCancelPending);
    }

    [Fact]
    public async Task Documents_that_are_not_pending_or_confirmed_are_not_read()
    {
        await SeedInvoiceAsync(1, InvoiceStatus.Cancelled);
        await SeedInvoiceAsync(2, InvoiceStatus.Returned);
        await AddSapInvoiceAsync(1, canceled: "Y");
        var service = Service();

        await service.ExecuteAsync(CancellationToken.None);

        Assert.Empty(service.ChunkSizes);
    }

    [Fact]
    public async Task A_guard_refusal_flags_the_document_and_keeps_it_intact()
    {
        var invoice = await SeedInvoiceAsync(1);
        await AddBlockingReturnAsync(invoice.Key);
        await AddSapInvoiceAsync(1, canceled: "Y");

        await Service().ExecuteAsync(CancellationToken.None);

        var saved = await ReloadAsync(invoice.Key);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        Assert.True(saved.SapCancelPending);
        Assert.Equal(ReturnRefusal, saved.SapCancelPendingReason);
        Assert.Equal(_clock.GetLocalNow().DateTime, saved.SapCancelDetectedAt);
        Assert.Null(saved.CanceledAt);
    }

    [Fact]
    public async Task A_repeated_refusal_keeps_the_first_detection_time()
    {
        var invoice = await SeedInvoiceAsync(1);
        await AddBlockingReturnAsync(invoice.Key);
        await AddSapInvoiceAsync(1, canceled: "Y");
        var first = _clock.GetLocalNow().DateTime;

        await Service().ExecuteAsync(CancellationToken.None, force: true);
        _clock.Now = _clock.Now.AddHours(1);
        await Service().ExecuteAsync(CancellationToken.None, force: true);

        var saved = await ReloadAsync(invoice.Key);
        Assert.True(saved.SapCancelPending);
        Assert.Equal(first, saved.SapCancelDetectedAt);
    }

    [Fact]
    public async Task The_reason_is_cut_at_500_characters()
    {
        var invoice = await SeedInvoiceAsync(1);
        await AddSapInvoiceAsync(1, canceled: "Y");
        var longMessage = new string('x', 800);

        // Recusa com mensagem longa: a devolução viva barra e o texto vem de um Service customizado.
        var scopes = new RefusingScopeFactory(_provider.GetRequiredService<IServiceScopeFactory>(), longMessage);
        await Service(scopes: scopes).ExecuteAsync(CancellationToken.None);

        var saved = await ReloadAsync(invoice.Key);
        Assert.True(saved.SapCancelPending);
        Assert.Equal(500, saved.SapCancelPendingReason!.Length);
    }

    // ---------- concorrência: manual x agendado ----------

    [Fact]
    public async Task A_scheduled_run_skips_silently_while_another_run_of_the_company_holds_the_lock()
    {
        await SetSettingsAsync(lastRunAt: null);
        var invoice = await SeedInvoiceAsync(1);
        await AddSapInvoiceAsync(1, canceled: "Y");

        await using (await _lock.TryAcquireAsync("0001"))
            await Service().ExecuteAsync(CancellationToken.None);

        Assert.Equal(InvoiceStatus.Confirmed, (await ReloadAsync(invoice.Key)).InvoiceStatus);
        Assert.Null((await _context.SapIntegrationSettings.AsNoTracking().SingleAsync()).InvoiceSyncLastRunAt);
    }

    [Fact]
    public async Task Run_now_refuses_while_another_run_of_the_company_holds_the_lock()
    {
        var invoice = await SeedInvoiceAsync(1);
        await AddSapInvoiceAsync(1, canceled: "Y");

        await using (await _lock.TryAcquireAsync("0001"))
        {
            var e = await Assert.ThrowsAsync<DefaultException>(() => Service().ExecuteAsync(CancellationToken.None, force: true));
            Assert.Equal("Já existe uma sincronização em andamento. Tente novamente em instantes.", e.Message);
        }

        Assert.Equal(InvoiceStatus.Confirmed, (await ReloadAsync(invoice.Key)).InvoiceStatus);
    }

    [Fact]
    public async Task The_lock_is_released_at_the_end_of_the_run()
    {
        await Service().ExecuteAsync(CancellationToken.None, force: true);

        await using var again = await _lock.TryAcquireAsync("0001");
        Assert.NotNull(again);
    }

    [Fact]
    public async Task A_refusal_does_not_flag_a_document_cancelled_meanwhile_by_another_run()
    {
        var invoice = await SeedInvoiceAsync(1);
        await AddSapInvoiceAsync(1, canceled: "Y");

        // A outra execução cancela o documento e a nossa tentativa é recusada logo depois.
        var scopes = new RefusingScopeFactory(_provider.GetRequiredService<IServiceScopeFactory>(), ReturnRefusal,
            beforeRefusal: () =>
            {
                using var other = new AppDbContext(TestDb.Options(_dbName));
                other.SalesInvoices.Single(x => x.Key == invoice.Key).InvoiceStatus = InvoiceStatus.Cancelled;
                other.SaveChanges();
            });
        await Service(scopes: scopes).ExecuteAsync(CancellationToken.None, force: true);

        var saved = await ReloadAsync(invoice.Key);
        Assert.Equal(InvoiceStatus.Cancelled, saved.InvoiceStatus);
        Assert.False(saved.SapCancelPending);
        Assert.Null(saved.SapCancelPendingReason);
    }

    /// <summary>Faz o cancelamento do documento recusar com uma mensagem longa, sem depender de uma regra real.</summary>
    private sealed class RefusingScopeFactory(IServiceScopeFactory inner, string message, Action? beforeRefusal = null)
        : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new RefusingScope(inner.CreateScope(), message, beforeRefusal);

        private sealed class RefusingScope(IServiceScope scope, string message, Action? beforeRefusal) : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; } = new RefusingProvider(scope.ServiceProvider, message, beforeRefusal);
            public void Dispose() => scope.Dispose();
        }

        private sealed class RefusingProvider(IServiceProvider inner, string message, Action? beforeRefusal) : IServiceProvider
        {
            public object? GetService(Type serviceType)
            {
                if (serviceType != typeof(SalesInvoicesCancelService))
                    return inner.GetService(serviceType);

                beforeRefusal?.Invoke();
                throw new DefaultException(message);
            }
        }
    }

    [Fact]
    public async Task The_next_run_cancels_the_document_once_the_guard_is_gone_and_clears_the_flag()
    {
        var invoice = await SeedInvoiceAsync(1);
        var blocking = await AddBlockingReturnAsync(invoice.Key);
        await AddSapInvoiceAsync(1, canceled: "Y");
        await Service().ExecuteAsync(CancellationToken.None, force: true);
        Assert.True((await ReloadAsync(invoice.Key)).SapCancelPending);

        var ret = await _context.SalesInvoices.SingleAsync(x => x.Key == blocking.Key);
        ret.InvoiceStatus = InvoiceStatus.Cancelled;
        await _context.SaveChangesAsync();

        await Service().ExecuteAsync(CancellationToken.None, force: true);

        var saved = await ReloadAsync(invoice.Key);
        Assert.Equal(InvoiceStatus.Cancelled, saved.InvoiceStatus);
        Assert.False(saved.SapCancelPending);
        Assert.Null(saved.SapCancelPendingReason);
        Assert.Null(saved.SapCancelDetectedAt);
    }

    [Fact]
    public async Task A_manual_cancel_clears_the_pending_flag()
    {
        var invoice = await SeedInvoiceAsync(1);
        var stored = await _context.SalesInvoices.SingleAsync(x => x.Key == invoice.Key);
        stored.SapCancelPending = true;
        stored.SapCancelPendingReason = "Documento de saída possui retorno.";
        stored.SapCancelDetectedAt = new DateTime(2026, 11, 16, 9, 0, 0);
        await _context.SaveChangesAsync();

        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SalesInvoicesCancelService>().ExecuteAsync(invoice.Key, "tester");

        var saved = await ReloadAsync(invoice.Key);
        Assert.Equal(InvoiceStatus.Cancelled, saved.InvoiceStatus);
        Assert.False(saved.SapCancelPending);
        Assert.Null(saved.SapCancelPendingReason);
        Assert.Null(saved.SapCancelDetectedAt);
    }

    [Fact]
    public async Task A_pending_linked_document_is_deleted_and_the_nf_is_freed()
    {
        var key = new string('5', 44);
        var invoice = await SeedInvoiceAsync(1, InvoiceStatus.Pending, chave: key);
        await AddSapInvoiceAsync(1, canceled: "Y");

        await Service().ExecuteAsync(CancellationToken.None);

        await using var fresh = new AppDbContext(TestDb.Options(_dbName));
        Assert.False(await fresh.SalesInvoices.AnyAsync(x => x.Key == invoice.Key));
    }

    [Fact]
    public async Task An_unexpected_failure_on_one_document_does_not_stop_the_next()
    {
        var first = await SeedInvoiceAsync(1);
        var second = await SeedInvoiceAsync(2);
        await AddSapInvoiceAsync(1, canceled: "Y");
        await AddSapInvoiceAsync(2, canceled: "Y");
        var scopes = new FlakyScopeFactory(_provider.GetRequiredService<IServiceScopeFactory>());

        await Service(scopes: scopes).ExecuteAsync(CancellationToken.None);

        Assert.Equal(InvoiceStatus.Confirmed, (await ReloadAsync(first.Key)).InvoiceStatus);
        Assert.Equal(InvoiceStatus.Cancelled, (await ReloadAsync(second.Key)).InvoiceStatus);
        Assert.NotNull((await SettingsRowAsync())!.InvoiceSyncLastRunAt);
    }

    [Fact]
    public async Task Invoices_are_read_from_sap_in_chunks_of_at_most_1000()
    {
        for (var i = 1; i <= 1001; i++)
            _context.SalesInvoices.Add(new SalesInvoice
            {
                Key = Guid.NewGuid(), CardCode = "C0001", BranchCode = "3", InvoiceNumber = $"DS{i:D6}",
                InvoiceStatus = InvoiceStatus.Confirmed, SapDocEntry = i,
            });
        await _context.SaveChangesAsync();
        var service = Service();

        await service.ExecuteAsync(CancellationToken.None);

        Assert.Equal([1000, 1], service.ChunkSizes.OrderByDescending(x => x));
    }

    // ---------- chave ----------

    [Fact]
    public async Task A_linked_document_without_key_gets_the_addon_access_key()
    {
        var invoice = await SeedInvoiceAsync(1);
        await AddSapInvoiceAsync(1);
        var accessKey = new string('3', 44);
        var fiscal = new FakeFiscal(true, null, new SapFiscalDocumentInfo(1, accessKey, true, false, "135"));

        await Service(fiscal).ExecuteAsync(CancellationToken.None);

        var saved = await ReloadAsync(invoice.Key);
        Assert.Equal(accessKey, saved.ChaveNFe);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
    }

    [Fact]
    public async Task A_duplicate_access_key_is_skipped_without_failing_the_run()
    {
        var accessKey = new string('3', 44);
        await SeedInvoiceAsync(1, chave: accessKey);
        var other = await SeedInvoiceAsync(2);
        await AddSapInvoiceAsync(1);
        await AddSapInvoiceAsync(2);
        var fiscal = new FakeFiscal(true, null, new SapFiscalDocumentInfo(2, accessKey, true, false, "135"));

        await Service(fiscal).ExecuteAsync(CancellationToken.None);

        Assert.Null((await ReloadAsync(other.Key)).ChaveNFe);
        Assert.NotNull((await SettingsRowAsync())!.InvoiceSyncLastRunAt);
    }

    [Fact]
    public async Task A_document_that_already_has_a_key_keeps_it()
    {
        var current = new string('1', 44);
        var invoice = await SeedInvoiceAsync(1, chave: current);
        await AddSapInvoiceAsync(1);
        var fiscal = new FakeFiscal(true, null, new SapFiscalDocumentInfo(1, new string('2', 44), true, false, null));

        await Service(fiscal).ExecuteAsync(CancellationToken.None);

        Assert.Equal(current, (await ReloadAsync(invoice.Key)).ChaveNFe);
    }

    // ---------- SAP fora do ar ----------

    [Fact]
    public async Task Sap_down_does_not_throw_and_does_not_stamp_the_last_run()
    {
        var invoice = await SeedInvoiceAsync(1);
        await SetSettingsAsync();

        await Service(sapFailure: new FakeDbException()).ExecuteAsync(CancellationToken.None);

        Assert.Equal(InvoiceStatus.Confirmed, (await ReloadAsync(invoice.Key)).InvoiceStatus);
        Assert.Null((await SettingsRowAsync())!.InvoiceSyncLastRunAt);
    }

    [Fact]
    public async Task A_sap_timeout_does_not_throw_either()
    {
        await SeedInvoiceAsync(1);

        await Service(sapFailure: new TimeoutException()).ExecuteAsync(CancellationToken.None);

        Assert.Null(await SettingsRowAsync());
    }

    [Fact]
    public async Task Addon_down_does_not_throw_and_does_not_stamp_the_last_run()
    {
        var invoice = await SeedInvoiceAsync(1);
        await AddSapInvoiceAsync(1, canceled: "N");
        await SetSettingsAsync();

        await Service(new FakeFiscal(true, new FakeDbException())).ExecuteAsync(CancellationToken.None);

        Assert.Equal(InvoiceStatus.Confirmed, (await ReloadAsync(invoice.Key)).InvoiceStatus);
        Assert.Null((await SettingsRowAsync())!.InvoiceSyncLastRunAt);
    }

    // ---------- falha ao adquirir a trava (revisão final, item 8) ----------

    [Fact]
    public async Task Scheduled_run_skips_without_stamping_when_the_lock_cannot_be_acquired()
    {
        var invoice = await SeedInvoiceAsync(1);
        await AddSapInvoiceAsync(1, canceled: "Y");
        await SetSettingsAsync();

        await Service(syncLock: new ThrowingLock(new FakeDbException())).ExecuteAsync(CancellationToken.None);

        Assert.Equal(InvoiceStatus.Confirmed, (await ReloadAsync(invoice.Key)).InvoiceStatus);
        Assert.Null((await SettingsRowAsync())!.InvoiceSyncLastRunAt);
    }

    [Fact]
    public async Task Run_now_returns_a_business_message_when_the_lock_cannot_be_acquired()
    {
        await SetSettingsAsync();

        var error = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(syncLock: new ThrowingLock(new TimeoutException())).ExecuteAsync(CancellationToken.None, force: true));

        Assert.Equal(SapInvoiceSyncService.LockUnavailableMessage, error.Message);
        Assert.Equal("Não foi possível iniciar a sincronização. Tente novamente em instantes.", error.Message);
        Assert.Null((await SettingsRowAsync())!.InvoiceSyncLastRunAt);
    }

    [Fact]
    public async Task Cancellation_while_acquiring_the_lock_is_not_swallowed()
    {
        await SetSettingsAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(syncLock: new ThrowingLock(new OperationCanceledException())).ExecuteAsync(CancellationToken.None));
    }

    // ---------- filial da invoice (revisão final, item 13) ----------

    [Fact]
    public async Task Sap_invoice_of_another_branch_never_cancels_the_document()
    {
        var invoice = await SeedInvoiceAsync(1);
        await AddSapInvoiceAsync(1, canceled: "Y", branch: 7);
        await SetSettingsAsync();

        await Service().ExecuteAsync(CancellationToken.None);

        Assert.Equal(InvoiceStatus.Confirmed, (await ReloadAsync(invoice.Key)).InvoiceStatus);
        Assert.False((await ReloadAsync(invoice.Key)).SapCancelPending);
    }

    // ---------- NF de devolução (A/R Credit Memo) ----------

    private static void AssertUnlinked(SalesInvoice saved)
    {
        // Desvinculada, nunca cancelada: o físico da devolução não depende da nota.
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        Assert.Null(saved.SapCreditMemoDocEntry);
        Assert.Null(saved.SapCreditMemoDocNum);
        Assert.Null(saved.TaxDocumentNumber);
        Assert.Null(saved.TaxDocumentSeries);
        Assert.Null(saved.ChaveNFe);
    }

    [Fact]
    public async Task Credit_memo_canceled_in_orin_unlinks_the_return_and_keeps_it_confirmed()
    {
        var ret = await SeedLinkedReturnAsync(1, chave: new string('1', 44));
        await AddSapCreditMemoAsync(1, canceled: "Y");

        await Service().ExecuteAsync(CancellationToken.None);

        AssertUnlinked(await ReloadAsync(ret.Key));
        await using var fresh = new AppDbContext(TestDb.Options(_dbName));
        var log = await fresh.SalesInvoicesChangeLogs.AsNoTracking().SingleAsync();
        Assert.Equal("sap-sync", log.ChangedBy);
        Assert.Equal(ContractChangeLogFields.SapReturnInvoice, log.Field);
        Assert.Equal("NF de devolução cancelada no SAP.", log.NewValue);
        Assert.NotNull((await SettingsRowAsync())!.InvoiceSyncLastRunAt);
    }

    [Fact]
    public async Task Credit_memo_cancelled_only_in_the_addon_unlinks_the_return()
    {
        var ret = await SeedLinkedReturnAsync(1);
        await AddSapCreditMemoAsync(1);

        await Service(new CreditMemoFiscal(new SapFiscalDocumentInfo(1, null, true, true, null)))
            .ExecuteAsync(CancellationToken.None);

        AssertUnlinked(await ReloadAsync(ret.Key));
    }

    [Fact]
    public async Task Active_credit_memo_and_credit_memo_missing_from_sap_are_left_alone()
    {
        var active = await SeedLinkedReturnAsync(1);
        var missing = await SeedLinkedReturnAsync(2);
        await AddSapCreditMemoAsync(1);

        await Service().ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, (await ReloadAsync(active.Key)).SapCreditMemoDocEntry);
        Assert.Equal(2, (await ReloadAsync(missing.Key)).SapCreditMemoDocEntry);
    }

    [Fact]
    public async Task Credit_memo_of_another_branch_never_unlinks_the_return()
    {
        var ret = await SeedLinkedReturnAsync(1);
        await AddSapCreditMemoAsync(1, canceled: "Y", branch: 7);

        await Service().ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, (await ReloadAsync(ret.Key)).SapCreditMemoDocEntry);
    }

    [Fact]
    public async Task A_canceled_sales_invoice_with_the_same_doc_entry_does_not_touch_the_return()
    {
        // OINV 1 cancelada e ORIN 1 viva: sequências independentes, documentos diferentes.
        var ret = await SeedLinkedReturnAsync(1);
        await AddSapCreditMemoAsync(1);
        await AddSapInvoiceAsync(1, canceled: "Y");
        var fiscal = new FakeFiscal(true, null, new SapFiscalDocumentInfo(1, null, false, true, null));

        await Service(fiscal).ExecuteAsync(CancellationToken.None);

        var saved = await ReloadAsync(ret.Key);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        Assert.Equal(1, saved.SapCreditMemoDocEntry);
        Assert.Equal("000004512", saved.TaxDocumentNumber);
    }

    [Fact]
    public async Task A_canceled_credit_memo_with_the_same_doc_entry_does_not_cancel_the_sales_document()
    {
        var invoice = await SeedInvoiceAsync(1);
        await AddSapInvoiceAsync(1);
        await AddSapCreditMemoAsync(1, canceled: "Y");

        await Service(new CreditMemoFiscal(new SapFiscalDocumentInfo(1, null, false, true, null)))
            .ExecuteAsync(CancellationToken.None);

        var saved = await ReloadAsync(invoice.Key);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        Assert.Equal(1, saved.SapDocEntry);
    }

    [Fact]
    public async Task A_linked_return_without_key_gets_the_addon_access_key()
    {
        var ret = await SeedLinkedReturnAsync(1);
        await AddSapCreditMemoAsync(1);
        var accessKey = new string('4', 44);

        await Service(new CreditMemoFiscal(new SapFiscalDocumentInfo(1, accessKey, true, false, "135")))
            .ExecuteAsync(CancellationToken.None);

        var saved = await ReloadAsync(ret.Key);
        Assert.Equal(accessKey, saved.ChaveNFe);
        Assert.Equal(1, saved.SapCreditMemoDocEntry);
    }

    [Fact]
    public async Task Returns_that_are_not_confirmed_are_not_read()
    {
        var ret = await SeedLinkedReturnAsync(1);
        await using (var fresh = new AppDbContext(TestDb.Options(_dbName)))
        {
            (await fresh.SalesInvoices.SingleAsync(x => x.Key == ret.Key)).InvoiceStatus = InvoiceStatus.Cancelled;
            await fresh.SaveChangesAsync();
        }
        await AddSapCreditMemoAsync(1, canceled: "Y");

        await Service().ExecuteAsync(CancellationToken.None);

        // Cancelada pelo usuário: os campos da nota ficam como evidência.
        Assert.Equal(1, (await ReloadAsync(ret.Key)).SapCreditMemoDocEntry);
    }

    [Fact]
    public async Task Sap_down_while_reading_credit_memos_unlinks_nothing_and_does_not_stamp_the_last_run()
    {
        var ret = await SeedLinkedReturnAsync(1);
        await SetSettingsAsync();

        await Service(creditMemoFailure: new FakeDbException()).ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, (await ReloadAsync(ret.Key)).SapCreditMemoDocEntry);
        Assert.Null((await SettingsRowAsync())!.InvoiceSyncLastRunAt);
    }

    [Fact]
    public async Task A_failure_reading_credit_memos_does_not_hold_back_the_sales_step()
    {
        // O passo de venda é o que já existia: uma falha só do lado da NF de devolução não pode atrasar o
        // cancelamento da invoice de venda cancelada no SAP.
        await SeedLinkedReturnAsync(1);
        var invoice = await SeedInvoiceAsync(2);
        await AddSapInvoiceAsync(2, canceled: "Y");

        await Service(creditMemoFailure: new FakeDbException()).ExecuteAsync(CancellationToken.None);

        Assert.Equal(InvoiceStatus.Cancelled, (await ReloadAsync(invoice.Key)).InvoiceStatus);
    }

    [Fact]
    public async Task Sap_down_for_sales_invoices_also_leaves_the_returns_alone()
    {
        var ret = await SeedLinkedReturnAsync(1);
        await SeedInvoiceAsync(2);
        await AddSapCreditMemoAsync(1, canceled: "Y");
        await SetSettingsAsync();

        await Service(sapFailure: new FakeDbException()).ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, (await ReloadAsync(ret.Key)).SapCreditMemoDocEntry);
        Assert.Null((await SettingsRowAsync())!.InvoiceSyncLastRunAt);
    }
}
