using Microsoft.EntityFrameworkCore;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Services.FiscalStock;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.FiscalStock;

public class FiscalStockPeriodsServiceTests
{
    private static async Task<(IUnitOfWork Db, FiscalStockPeriodsService Service)> Arrange(bool withOpening = true)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Items.Add(new Item { ItemCode = "SOJA", ItemName = "SOJA EM GRAOS" });
        if (withOpening)
            db.Context.FiscalInventoryOpeningBalances.Add(new FiscalInventoryOpeningBalance
            {
                BranchCode = "01", ItemCode = "SOJA", ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "SC",
                BalanceDate = new DateTime(2026, 9, 1), Quantity = 100m, TotalValue = 5000m,
            });
        await db.SaveChangesAsync();
        return (db, new FiscalStockPeriodsService(db));
    }

    private static void AddAdjustment(IUnitOfWork db, FiscalStockAdjustmentDirection direction, decimal quantity,
        decimal value, DateTime date, string number) =>
        db.Context.FiscalStockAdjustments.Add(new FiscalStockAdjustment
        {
            BranchCode = "01", ItemCode = "SOJA", ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "SC",
            AdjustmentDate = date, Direction = direction, Quantity = quantity, TotalValue = value,
            Reason = "teste", DocNumber = number,
        });

    [Fact]
    public async Task Close_without_opening_balance_is_refused()
    {
        var (_, service) = await Arrange(withOpening: false);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CloseAsync("01", 2026, 9, "paulo"));

        Assert.Equal("Lance o Saldo Inicial do Estoque antes de fechar.", ex.Message);
    }

    [Fact]
    public async Task Close_with_pending_exit_is_refused_listing_the_item()
    {
        var (db, service) = await Arrange();
        AddAdjustment(db, FiscalStockAdjustmentDirection.Out, 150m, 5000m, new DateTime(2026, 9, 10), "AJ000001");
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CloseAsync("01", 2026, 9, "paulo"));

        Assert.Contains("SOJA SOJA EM GRAOS: saldo -50 / R$ 0,00", ex.Message);
        Assert.False(await db.Context.FiscalStockPeriods.AnyAsync());
    }

    [Fact]
    public async Task Close_with_zero_quantity_and_value_is_refused()
    {
        var (db, service) = await Arrange();
        AddAdjustment(db, FiscalStockAdjustmentDirection.Out, 100m, 4000m, new DateTime(2026, 9, 10), "AJ000001");
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CloseAsync("01", 2026, 9, "paulo"));

        Assert.Contains("SOJA SOJA EM GRAOS: saldo 0 / R$ 1.000,00", ex.Message);
    }

    [Fact]
    public async Task Close_saves_snapshot_equal_to_open_layers_and_event()
    {
        var (db, service) = await Arrange();
        AddAdjustment(db, FiscalStockAdjustmentDirection.In, 20m, 1200m, new DateTime(2026, 9, 15), "AJ000001");
        await db.SaveChangesAsync();

        var period = await service.CloseAsync("01", 2026, 9, "paulo");

        Assert.Equal(FiscalStockPeriodStatus.Closed, period.Status);
        Assert.Equal("paulo", period.ClosedBy);
        var layers = await db.Context.FiscalStockPeriodLayers.Where(l => l.PeriodKey == period.Key)
            .OrderBy(l => l.Sequence).ToListAsync();
        Assert.Equal(2, layers.Count);
        Assert.Equal((100m, 5000m, new DateTime(2026, 9, 1), 1), (layers[0].Quantity, layers[0].Value, layers[0].LayerDate, layers[0].Sequence));
        Assert.Equal((20m, 1200m, new DateTime(2026, 9, 15), 2), (layers[1].Quantity, layers[1].Value, layers[1].LayerDate, layers[1].Sequence));
        Assert.Equal("SOJA EM GRAOS", layers[0].ItemName);
        Assert.Equal("SC", layers[0].UnitOfMeasureCode);
        var ev = Assert.Single(await db.Context.FiscalStockPeriodEvents.ToListAsync());
        Assert.Equal(FiscalStockPeriodEventType.Closed, ev.EventType);
        Assert.Equal("paulo", ev.By);
    }

    [Fact]
    public async Task Close_october_without_september_closed_is_refused()
    {
        var (_, service) = await Arrange();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CloseAsync("01", 2026, 10, "paulo"));

        Assert.Equal("Feche antes 09/2026.", ex.Message);
    }

    [Fact]
    public async Task Close_month_before_first_opening_is_refused()
    {
        var (_, service) = await Arrange();

        await Assert.ThrowsAsync<DefaultException>(() => service.CloseAsync("01", 2026, 8, "paulo"));
    }

    [Fact]
    public async Task Close_october_after_september_is_accepted()
    {
        var (_, service) = await Arrange();
        await service.CloseAsync("01", 2026, 9, "paulo");

        var october = await service.CloseAsync("01", 2026, 10, "paulo");

        Assert.Equal(FiscalStockPeriodStatus.Closed, october.Status);
    }

    [Fact]
    public async Task Reopen_september_with_october_closed_is_refused()
    {
        var (_, service) = await Arrange();
        await service.CloseAsync("01", 2026, 9, "paulo");
        await service.CloseAsync("01", 2026, 10, "paulo");

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            service.ReopenAsync("01", 2026, 9, "Lançamento atrasado", "paulo"));

        Assert.Equal("Reabra antes 10/2026.", ex.Message);
    }

    [Fact]
    public async Task Reopen_without_reason_is_refused()
    {
        var (_, service) = await Arrange();
        await service.CloseAsync("01", 2026, 9, "paulo");

        await Assert.ThrowsAsync<DefaultException>(() => service.ReopenAsync("01", 2026, 9, "  ", "paulo"));
    }

    [Fact]
    public async Task Reopen_deletes_snapshot_and_saves_event()
    {
        var (db, service) = await Arrange();
        await service.CloseAsync("01", 2026, 9, "paulo");

        var period = await service.ReopenAsync("01", 2026, 9, "Lançamento atrasado", "maria");

        Assert.Equal(FiscalStockPeriodStatus.Open, period.Status);
        Assert.Equal("maria", period.ReopenedBy);
        Assert.Equal("Lançamento atrasado", period.ReopenReason);
        Assert.False(await db.Context.FiscalStockPeriodLayers.AnyAsync());
        var events = await db.Context.FiscalStockPeriodEvents.OrderBy(e => e.EventType).ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.Equal(FiscalStockPeriodEventType.Reopened, events[1].EventType);
        Assert.Equal("Lançamento atrasado", events[1].Reason);

        var again = await service.CloseAsync("01", 2026, 9, "paulo");
        Assert.Equal(period.Key, again.Key);
    }

    [Fact]
    public async Task October_snapshot_starts_from_september_and_consumes_its_layers()
    {
        var (db, service) = await Arrange();
        AddAdjustment(db, FiscalStockAdjustmentDirection.In, 20m, 1200m, new DateTime(2026, 9, 15), "AJ000001");
        await db.SaveChangesAsync();
        await service.CloseAsync("01", 2026, 9, "paulo");
        AddAdjustment(db, FiscalStockAdjustmentDirection.Out, 110m, 5600m, new DateTime(2026, 10, 5), "AJ000002");
        await db.SaveChangesAsync();

        var october = await service.CloseAsync("01", 2026, 10, "paulo");

        var layer = Assert.Single(await db.Context.FiscalStockPeriodLayers.Where(l => l.PeriodKey == october.Key).ToListAsync());
        Assert.Equal((10m, 600m, new DateTime(2026, 9, 15), 1), (layer.Quantity, layer.Value, layer.LayerDate, layer.Sequence));
        Assert.Equal(3, await db.Context.FiscalStockPeriodLayers.CountAsync()); // setembro (2) intacto + outubro (1)
    }

    [Fact]
    public async Task Close_and_reopen_take_the_branch_lock()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.FiscalInventoryOpeningBalances.Add(new FiscalInventoryOpeningBalance
        {
            BranchCode = "01", ItemCode = "SOJA", ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "SC",
            BalanceDate = new DateTime(2026, 9, 1), Quantity = 100m, TotalValue = 5000m,
        });
        await db.SaveChangesAsync();
        var periodLock = new RecordingLock();
        var service = new FiscalStockPeriodsService(db, periodLock: periodLock);

        await service.CloseAsync("01", 2026, 9, "paulo");
        await service.ReopenAsync("01", 2026, 9, "motivo", "paulo");

        Assert.Equal(["01", "01"], periodLock.Acquired);
        Assert.Equal(2, periodLock.Released);
    }

    [Fact]
    public async Task Close_with_unrelated_save_failure_is_not_reported_as_already_closed()
    {
        var db = TestDb.CreateUnitOfWork(new ThrowOnSaveInterceptor(e =>
            e.Entity is FiscalStockPeriod && e.State == EntityState.Added));
        db.Context.FiscalInventoryOpeningBalances.Add(new FiscalInventoryOpeningBalance
        {
            BranchCode = "01", ItemCode = "SOJA", ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "SC",
            BalanceDate = new DateTime(2026, 9, 1), Quantity = 100m, TotalValue = 5000m,
        });
        await db.SaveChangesAsync();
        var service = new FiscalStockPeriodsService(db);

        await Assert.ThrowsAsync<DbUpdateException>(() => service.CloseAsync("01", 2026, 9, "paulo"));
    }

    [Theory]
    [InlineData(2601, true)]
    [InlineData(2627, true)]
    [InlineData(8152, false)]
    [InlineData(547, false)]
    public void Only_duplicate_key_is_unique_violation(int number, bool expected) =>
        Assert.Equal(expected, FiscalStockPeriodsService.IsUniqueViolation(
            new DbUpdateException("save", FakeSqlException(number))));

    [Fact]
    public void Non_sql_inner_exception_is_not_unique_violation() =>
        Assert.False(FiscalStockPeriodsService.IsUniqueViolation(
            new DbUpdateException("save", new InvalidOperationException())));

    /// <summary>SqlException não tem construtor público: monta via reflexão (SqlError + SqlErrorCollection).</summary>
    internal static Microsoft.Data.SqlClient.SqlException FakeSqlException(int number)
    {
        const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
        var errorType = typeof(Microsoft.Data.SqlClient.SqlError);
        var ctor = errorType.GetConstructors(any).OrderByDescending(c => c.GetParameters().Length).First();
        var args = ctor.GetParameters().Select(p => p.ParameterType switch
        {
            var t when t == typeof(int) && p.Name == "infoNumber" => (object?)number,
            var t when t == typeof(string) => "x",
            var t when t.IsValueType => Activator.CreateInstance(t),
            _ => null,
        }).ToArray();
        var error = ctor.Invoke(args);
        var collection = Activator.CreateInstance(typeof(Microsoft.Data.SqlClient.SqlErrorCollection), true)!;
        typeof(Microsoft.Data.SqlClient.SqlErrorCollection).GetMethod("Add", any)!.Invoke(collection, [error]);
        var create = typeof(Microsoft.Data.SqlClient.SqlException).GetMethods(any)
            .First(m => m.Name == "CreateException" && m.GetParameters().Length == 2 &&
                        m.GetParameters()[1].ParameterType == typeof(string));
        return (Microsoft.Data.SqlClient.SqlException)create.Invoke(null, [collection, "16.0"])!;
    }

    private sealed class RecordingLock : IFiscalStockPeriodLock
    {
        public List<string> Acquired { get; } = [];
        public int Released { get; private set; }

        public Task<IAsyncDisposable> AcquireAsync(string branchCode, CancellationToken ct = default)
        {
            Acquired.Add(branchCode);
            return Task.FromResult<IAsyncDisposable>(new Releaser(this));
        }

        private sealed class Releaser(RecordingLock owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.Released++;
                return ValueTask.CompletedTask;
            }
        }
    }

    [Fact]
    public void Edm_exposes_FiscalStockPeriods_and_actions()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        var model = builder.GetEdmModel();

        Assert.NotNull(model.EntityContainer.FindEntitySet("FiscalStockPeriods"));
        Assert.NotEmpty(model.EntityContainer.FindOperationImports("FiscalStockPeriodsClose"));
        Assert.NotEmpty(model.EntityContainer.FindOperationImports("FiscalStockPeriodsReopen"));
    }

    private static void AddSale(IUnitOfWork db, string number, NfeStatus status, DateTime date, bool movesStock = true,
        TaxDocumentKind kind = TaxDocumentKind.Nfe)
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = "C0001", InvoiceNumber = number, InvoiceDate = date,
            InvoiceStatus = InvoiceStatus.Confirmed, InvoiceType = SalesInvoiceType.Normal, TaxDocumentKind = kind,
            NfeStatus = status,
        };
        invoice.AddItem(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "SC", Quantity = 1m, UnitPrice = 1m, MovesFiscalInventory = movesStock,
        });
        db.Context.SalesInvoices.Add(invoice);
    }

    private static void AddPurchase(IUnitOfWork db, string number, DocumentIssuerType issuer, NfeStatus status, DateTime date)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = "F0001", InvoiceNumber = number, IssueDate = date,
            PostingDate = date, InvoiceStatus = InvoiceStatus.Confirmed, TaxDocumentKind = TaxDocumentKind.Nfe,
            IssuerType = issuer, NfeStatus = status,
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "SOJA", Quantity = 1m, UnitPrice = 1m, MovesFiscalInventory = true,
        });
        db.Context.PurchaseInvoices.Add(invoice);
    }

    [Fact]
    public async Task Close_with_confirmed_nfe_not_yet_authorized_is_refused_listing_documents()
    {
        var (db, service) = await Arrange();
        AddSale(db, "DS0000002", NfeStatus.Rejected, new DateTime(2026, 9, 20));
        AddSale(db, "DS0000001", NfeStatus.None, new DateTime(2026, 9, 5));
        AddPurchase(db, "DE0000003", DocumentIssuerType.Own, NfeStatus.Processing, new DateTime(2026, 9, 12));
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CloseAsync("01", 2026, 9, "paulo"));

        Assert.Equal(
            "Há NF-e confirmadas ainda não autorizadas no mês: DS0000001, DS0000002, DE0000003. Transmita ou cancele antes de fechar.",
            ex.Message);
        Assert.False(await db.Context.FiscalStockPeriods.AnyAsync());
    }

    [Fact]
    public async Task Close_ignores_authorized_void_third_party_other_month_and_non_stock_nfe()
    {
        var (db, service) = await Arrange();
        AddSale(db, "DS0000001", NfeStatus.Cancelled, new DateTime(2026, 9, 5));
        AddSale(db, "DS0000002", NfeStatus.Denied, new DateTime(2026, 9, 5));
        AddSale(db, "DS0000003", NfeStatus.None, new DateTime(2026, 10, 1));
        AddSale(db, "DS0000004", NfeStatus.None, new DateTime(2026, 9, 5), movesStock: false);
        AddSale(db, "DS0000005", NfeStatus.None, new DateTime(2026, 9, 5), kind: TaxDocumentKind.Other);
        AddPurchase(db, "DE0000001", DocumentIssuerType.ThirdParty, NfeStatus.None, new DateTime(2026, 9, 12));
        await db.SaveChangesAsync();

        var period = await service.CloseAsync("01", 2026, 9, "paulo");

        Assert.Equal(FiscalStockPeriodStatus.Closed, period.Status);
    }
}
