using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Sap;

/// <summary>
/// GAC-1226: os escopos filhos da sincronização (cancelar, excluir, sinalizar pendência) precisam abrir o banco da
/// MESMA empresa do escopo que resolveu o serviço. No "Sincronizar agora" a empresa vem só explícita no escopo da
/// requisição (sem CompanyAmbient): um escopo novo cairia no banco padrão. Fiação igual à do Program: o AppDbContext
/// abre o InMemory nomeado pela connection string da empresa do escopo.
/// </summary>
public class SapInvoiceSyncServiceCompanyTests : IDisposable
{
    private static readonly string DbA = "sync-a-" + Guid.NewGuid();
    private static readonly string DbB = "sync-b-" + Guid.NewGuid();
    private static readonly CompanyInfo A = new("0001", "A", DbA);
    private static readonly CompanyInfo B = new("0002", "B", DbB);

    private readonly SapErpDbContext _sap = new(
        new DbContextOptionsBuilder<SapErpDbContext>().UseInMemoryDatabase($"sap-{Guid.NewGuid()}").Options);

    private readonly ServiceProvider _provider;

    public SapInvoiceSyncServiceCompanyTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // Banco padrão = banco da empresa A: um escopo sem empresa grava em A, e o teste vê.
        services.AddSingleton(TestConfiguration.With(("ConnectionStrings:SiagroDB", DbA)));
        services.AddSingleton<ICompanyCatalog>(new FakeCompanyCatalog(A, B));
        services.AddScoped<ICompanyContext, CompanyContext>();
        services.AddDbContext<AppDbContext>((sp, options) => options
            .UseInMemoryDatabase(sp.GetRequiredService<ICompanyContext>().ConnectionString)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddScoped<UnitOfWork>(sp => new UnitOfWork(sp.GetRequiredService<AppDbContext>()));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<UnitOfWork>());
        services.AddScoped<ShipmentLoadsMovementLogService>();
        services.AddScoped<ShipmentLoadsBalanceHookService>();
        services.AddScoped<SalesShipmentReleasesRecalculateShippedService>();
        services.AddScoped<SalesContractsAllocationDeleteForInvoiceService>();
        services.AddScoped<SalesInvoiceTaxDocumentDuplicateGuard>();
        services.AddScoped<SalesInvoicesCancelService>();
        services.AddScoped<SalesInvoicesDeleteService>();
        services.AddSingleton(_sap);
        services.AddSingleton<ISapFiscalDocumentSource>(new NoFiscal());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISapInvoiceSyncLock, InMemorySapInvoiceSyncLock>();
        services.AddScoped<SapInvoiceSyncService>();
        _provider = services.BuildServiceProvider();
    }

    public void Dispose() => _provider.Dispose();

    private sealed class NoFiscal : ISapFiscalDocumentSource
    {
        public bool IsAvailable => false;

        public Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForInvoicesAsync(
            IReadOnlyCollection<SapInvoiceRef> invoices, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<int, SapFiscalDocumentInfo>>(new Dictionary<int, SapFiscalDocumentInfo>());
    }

    private static AppDbContext Open(string db) => new(TestDb.Options(db));

    private static async Task<SalesInvoice> SeedAsync(string db, int docEntry, InvoiceStatus status, Guid? key = null)
    {
        await using var context = Open(db);
        var invoice = new SalesInvoice
        {
            Key = key ?? Guid.NewGuid(), CardCode = "C0001", BranchCode = "3", InvoiceNumber = $"DS{docEntry:D6}",
            InvoiceStatus = status, SapDocEntry = docEntry, SapDocNum = docEntry + 1000,
        };
        context.SalesInvoices.Add(invoice);
        await context.SaveChangesAsync();
        return invoice;
    }

    private static async Task AddBlockingReturnAsync(string db, Guid originKey)
    {
        await using var context = Open(db);
        context.SalesInvoices.Add(new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = "C0001", BranchCode = "3", InvoiceNumber = "RET000001",
            InvoiceType = SalesInvoiceType.Return, InvoiceStatus = InvoiceStatus.Pending, SalesInvoiceOriginKey = originKey,
        });
        await context.SaveChangesAsync();
    }

    private async Task AddCancelledSapInvoiceAsync(int docEntry)
    {
        _sap.SapInvoices.Add(new SapInvoice { DocEntry = docEntry, DocNum = docEntry + 1000, BPLId = 3, Canceled = "Y" });
        await _sap.SaveChangesAsync();
    }

    private static async Task<SalesInvoice?> ReloadAsync(string db, Guid key)
    {
        await using var context = Open(db);
        return await context.SalesInvoices.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key);
    }

    /// <summary>Como no HTTP: empresa só explícita no escopo da requisição, sem CompanyAmbient.</summary>
    private async Task RunNowAsHttpRequestAsync(CompanyInfo company)
    {
        Assert.Null(CompanyAmbient.CompanyCode);
        using var request = _provider.CreateScope();
        request.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);
        await request.ServiceProvider.GetRequiredService<SapInvoiceSyncService>()
            .ExecuteAsync(CancellationToken.None, force: true);
    }

    [Fact]
    public async Task Run_now_cancels_and_deletes_in_the_request_company_and_leaves_the_default_database_alone()
    {
        // Mesmas chaves nos dois bancos: um escopo no banco errado acertaria a linha de A.
        var confirmed = await SeedAsync(DbB, 1, InvoiceStatus.Confirmed);
        var pending = await SeedAsync(DbB, 2, InvoiceStatus.Pending);
        await SeedAsync(DbA, 1, InvoiceStatus.Confirmed, confirmed.Key);
        await SeedAsync(DbA, 2, InvoiceStatus.Pending, pending.Key);
        await AddCancelledSapInvoiceAsync(1);
        await AddCancelledSapInvoiceAsync(2);

        await RunNowAsHttpRequestAsync(B);

        Assert.Equal(InvoiceStatus.Cancelled, (await ReloadAsync(DbB, confirmed.Key))!.InvoiceStatus);
        Assert.Null(await ReloadAsync(DbB, pending.Key));
        Assert.Equal(InvoiceStatus.Confirmed, (await ReloadAsync(DbA, confirmed.Key))!.InvoiceStatus);
        Assert.Equal(InvoiceStatus.Pending, (await ReloadAsync(DbA, pending.Key))!.InvoiceStatus);
    }

    [Fact]
    public async Task Run_now_flags_the_refusal_in_the_request_company()
    {
        var invoice = await SeedAsync(DbB, 3, InvoiceStatus.Confirmed);
        await AddBlockingReturnAsync(DbB, invoice.Key);
        await SeedAsync(DbA, 3, InvoiceStatus.Confirmed, invoice.Key);
        await AddCancelledSapInvoiceAsync(3);

        await RunNowAsHttpRequestAsync(B);

        Assert.True((await ReloadAsync(DbB, invoice.Key))!.SapCancelPending);
        Assert.False((await ReloadAsync(DbA, invoice.Key))!.SapCancelPending);
    }

    [Fact]
    public async Task The_job_path_with_ambient_company_keeps_working()
    {
        var invoice = await SeedAsync(DbB, 4, InvoiceStatus.Confirmed);
        await SeedAsync(DbA, 4, InvoiceStatus.Confirmed, invoice.Key);
        await AddCancelledSapInvoiceAsync(4);

        // Igual ao CompanyScopedJobRunner: ambiente + explícita.
        using (CompanyAmbient.Use(B.Code))
        {
            using var scope = _provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(B);
            await scope.ServiceProvider.GetRequiredService<SapInvoiceSyncService>()
                .ExecuteAsync(CancellationToken.None, force: true);
        }

        Assert.Equal(InvoiceStatus.Cancelled, (await ReloadAsync(DbB, invoice.Key))!.InvoiceStatus);
        Assert.Equal(InvoiceStatus.Confirmed, (await ReloadAsync(DbA, invoice.Key))!.InvoiceStatus);
    }

    [Fact]
    public async Task Without_a_company_it_refuses_instead_of_falling_back()
    {
        var invoice = await SeedAsync(DbA, 5, InvoiceStatus.Confirmed);
        await AddCancelledSapInvoiceAsync(5);

        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SapInvoiceSyncService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(CancellationToken.None, force: true));
        Assert.Equal(InvoiceStatus.Confirmed, (await ReloadAsync(DbA, invoice.Key))!.InvoiceStatus);
    }
}
