using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.StorageCustody;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageCustody;

/// <summary>Guarda das notas de custódia (spec 2026-10-12 §3.3, §4.3, §4.4).</summary>
public class StorageCustodyGuardTests
{
    private static PurchaseInvoice Purchase(StorageTransaction? t, StorageCustodyRole role, decimal quantity,
        DocumentIssuerType issuer = DocumentIssuerType.Own, string card = StorageCustodySeed.Depositor,
        string item = StorageCustodySeed.Item, string branch = StorageCustodySeed.Branch) => new()
    {
        Key = Guid.NewGuid(), BranchCode = branch, CardCode = card, IssuerType = issuer,
        CustodyStorageTransactionKey = t?.Key,
        Items = [new PurchaseInvoiceItem { UnitOfMeasureCode = "KG", Key = Guid.NewGuid(), ItemCode = item, Quantity = quantity, UsageCode = StorageCustodySeed.UsageOf(role) }],
    };

    private static SalesInvoice Sales(StorageTransaction? t, StorageCustodyRole role, decimal quantity) => new()
    {
        Key = Guid.NewGuid(), BranchCode = StorageCustodySeed.Branch, CardCode = StorageCustodySeed.Depositor,
        CustodyStorageTransactionKey = t?.Key,
        Items = [new SalesInvoiceItem { Key = Guid.NewGuid(), ItemCode = StorageCustodySeed.Item, UnitOfMeasureCode = "KG", Quantity = quantity, UsageCode = StorageCustodySeed.UsageOf(role) }],
    };

    private static Task Ensure(UnitOfWork db, PurchaseInvoice i) => StorageCustodyGuard.EnsurePurchaseAsync(db.Context, i, i.Items.ToList());
    private static Task Ensure(UnitOfWork db, SalesInvoice i) => StorageCustodyGuard.EnsureSalesAsync(db.Context, i, i.Items.ToList());

    [Fact]
    public async Task Own_processing_entry_with_the_gross_weight_passes_and_records_the_role()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync(gross: 30_000m, net: 28_000m);
        var invoice = Purchase(t, StorageCustodyRole.ProcessingEntry, 30_000m);

        await Ensure(db, invoice);

        Assert.Equal(StorageCustodyRole.ProcessingEntry, invoice.CustodyRole);
    }

    [Fact]
    public async Task Own_entry_with_changed_quantity_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync(gross: 30_000m, net: 28_000m, code: "RM000010");

        var e = await Assert.ThrowsAsync<DefaultException>(() => Ensure(db, Purchase(t, StorageCustodyRole.ProcessingEntry, 28_000m)));

        Assert.Equal("A nota de entrada para beneficiamento precisa ter a quantidade do romaneio RM000010 (30.000,000).", e.Message);
    }

    [Fact]
    public async Task Producer_invoice_keeps_its_own_quantity()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync(gross: 30_000m);

        await Ensure(db, Purchase(t, StorageCustodyRole.ProcessingEntry, 29_500m, DocumentIssuerType.ThirdParty));
    }

    [Fact]
    public async Task Custody_usage_without_romaneio_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        await StorageCustodySeed.CreateAsync(db);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Ensure(db, Purchase(null, StorageCustodyRole.ProcessingEntry, 1m, DocumentIssuerType.ThirdParty)));

        Assert.Equal("A nota de entrada para beneficiamento precisa indicar o romaneio do lote de armazenagem.", e.Message);
    }

    [Fact]
    public async Task Removing_the_custody_line_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        var invoice = Purchase(t, StorageCustodyRole.ProcessingEntry, 30_000m);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            StorageCustodyGuard.EnsurePurchaseAsync(db.Context, invoice, []));

        Assert.Equal("O documento aponta um romaneio de armazenagem, mas nenhuma linha tem natureza de custódia.", e.Message);
    }

    [Fact]
    public async Task Mixed_roles_are_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        var invoice = Purchase(t, StorageCustodyRole.ProcessingEntry, 30_000m);
        invoice.Items.Add(new PurchaseInvoiceItem { UnitOfMeasureCode = "KG", ItemCode = StorageCustodySeed.Item, Quantity = 1m, UsageCode = StorageCustodySeed.StorageEntryUsage });

        var e = await Assert.ThrowsAsync<DefaultException>(() => Ensure(db, invoice));

        Assert.Equal("Nota de custódia não mistura naturezas: todas as linhas precisam ter o mesmo papel.", e.Message);
    }

    [Theory]
    [InlineData("P9999", StorageCustodySeed.Item, StorageCustodySeed.Branch, "A nota de custódia precisa ser do depositante do lote (P0001).")]
    [InlineData(StorageCustodySeed.Depositor, "MILHO", StorageCustodySeed.Branch, "A nota de custódia precisa ser do produto do lote (SOJA).")]
    [InlineData(StorageCustodySeed.Depositor, StorageCustodySeed.Item, "02", "A nota de custódia precisa ser da filial do lote (01).")]
    public async Task Partner_item_and_branch_must_be_the_lot_ones(string card, string item, string branch, string message)
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Ensure(db, Purchase(t, StorageCustodyRole.ProcessingEntry, 30_000m, DocumentIssuerType.ThirdParty, card, item, branch)));

        Assert.Equal(message, e.Message);
    }

    [Fact]
    public async Task Role_not_expected_by_the_romaneio_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db, requiresProcessing: false);
        var t = await seed.AddTransactionAsync(code: "RM000011");

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Ensure(db, Purchase(t, StorageCustodyRole.ProcessingEntry, 30_000m)));

        Assert.Equal("O romaneio RM000011 não espera a nota de entrada para beneficiamento.", e.Message);
    }

    [Fact]
    public async Task Cancelled_romaneio_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync(status: StorageTransactionsStatus.Cancelled, code: "RM000012");

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Ensure(db, Purchase(t, StorageCustodyRole.ProcessingEntry, 30_000m)));

        Assert.Equal("O romaneio RM000012 não espera a nota de entrada para beneficiamento.", e.Message);
    }

    [Fact]
    public async Task Second_active_document_for_the_same_step_is_refused_until_the_first_is_cancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync(code: "RM000013");
        var first = await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m, number: "000000055");

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Ensure(db, Purchase(t, StorageCustodyRole.ProcessingEntry, 30_000m)));
        Assert.Equal("O romaneio RM000013 já tem a nota de entrada para beneficiamento nº 000000055 ativa.", e.Message);

        first.InvoiceStatus = InvoiceStatus.Cancelled;
        await db.SaveChangesAsync();
        await Ensure(db, Purchase(t, StorageCustodyRole.ProcessingEntry, 30_000m));
    }

    [Fact]
    public async Task Editing_the_same_document_does_not_count_itself()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        var saved = await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m);

        await StorageCustodyGuard.EnsurePurchaseAsync(db.Context, saved, saved.Items.ToList());
    }

    [Fact]
    public async Task Symbolic_return_needs_the_processing_entry_with_access_key()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m); // própria, sem NF-e

        var e = await Assert.ThrowsAsync<DefaultException>(() => Ensure(db, Sales(t, StorageCustodyRole.ProcessingSymbolicReturn, 30_000m)));

        Assert.Equal("A devolução simbólica exige a entrada para beneficiamento com chave de acesso (NF-e própria autorizada ou escrituração confirmada).", e.Message);
    }

    [Fact]
    public async Task Symbolic_return_takes_the_quantity_of_the_producer_invoice()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync(gross: 30_000m);
        await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 29_500m, DocumentIssuerType.ThirdParty,
            InvoiceStatus.Confirmed, accessKey: StorageCustodySeed.AccessKey);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Ensure(db, Sales(t, StorageCustodyRole.ProcessingSymbolicReturn, 30_000m)));
        Assert.Equal("A devolução simbólica precisa ter a quantidade da entrada para beneficiamento (29.500,000).", e.Message);

        await Ensure(db, Sales(t, StorageCustodyRole.ProcessingSymbolicReturn, 29_500m));
    }

    [Fact]
    public async Task Storage_entry_after_processing_needs_the_symbolic_return()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync(net: 28_000m);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Ensure(db, Purchase(t, StorageCustodyRole.StorageEntry, 28_000m)));
        Assert.Equal("Gere antes a devolução simbólica do beneficiamento deste romaneio.", e.Message);

        await seed.AddSalesDocumentAsync(t, StorageCustodyRole.ProcessingSymbolicReturn, 30_000m);
        await Ensure(db, Purchase(t, StorageCustodyRole.StorageEntry, 28_000m));
    }

    [Fact]
    public async Task Sales_custody_usage_without_romaneio_points_to_the_action()
    {
        var db = TestDb.CreateUnitOfWork();
        await StorageCustodySeed.CreateAsync(db);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Ensure(db, Sales(null, StorageCustodyRole.StorageReturn, 1m)));

        Assert.Equal("Natureza de retorno de armazenagem: gere a nota pelo romaneio (Notas fiscais de custódia).", e.Message);
    }

    [Fact]
    public async Task Entry_role_in_a_sales_document_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Ensure(db, Sales(t, StorageCustodyRole.ProcessingEntry, 30_000m)));

        Assert.Equal("Natureza de entrada para beneficiamento é de entrada: lance no Documento de Entrada.", e.Message);
    }

    [Fact]
    public async Task Shipment_return_uses_the_net_weight()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync(StorageTransactionType.Shipment, gross: 15_200m, net: 15_000m);

        await Ensure(db, Sales(t, StorageCustodyRole.StorageReturn, 15_000m));
    }

    [Fact]
    public async Task Regular_document_without_romaneio_is_untouched()
    {
        var db = TestDb.CreateUnitOfWork();
        await StorageCustodySeed.CreateAsync(db);
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = "F1",
            Items = [new PurchaseInvoiceItem { UnitOfMeasureCode = "KG", ItemCode = "X", Quantity = 1m }],
        };

        await Ensure(db, invoice);

        Assert.Equal(StorageCustodyRole.None, invoice.CustodyRole);
    }

    [Fact]
    public async Task Processing_entry_access_key_feeds_the_symbolic_return_reference()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m, DocumentIssuerType.Own,
            InvoiceStatus.Confirmed, NfeStatus.Authorized, StorageCustodySeed.AccessKey);
        var symbolic = await seed.AddSalesDocumentAsync(t, StorageCustodyRole.ProcessingSymbolicReturn, 30_000m);

        Assert.Equal(StorageCustodySeed.AccessKey, await StorageCustodyGuard.ProcessingEntryAccessKeyAsync(db.Context, symbolic));
    }

    [Fact]
    public async Task Processing_entry_access_key_refuses_a_symbolic_return_whose_quantity_drifted()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m, DocumentIssuerType.Own,
            InvoiceStatus.Confirmed, NfeStatus.Authorized, StorageCustodySeed.AccessKey);
        var symbolic = await seed.AddSalesDocumentAsync(t, StorageCustodyRole.ProcessingSymbolicReturn, 30_000m);
        symbolic.Items.Single().Quantity = 29_000m;
        await db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => StorageCustodyGuard.ProcessingEntryAccessKeyAsync(db.Context, symbolic));

        Assert.Equal("A devolução simbólica precisa ter a quantidade da entrada para beneficiamento (30.000,000).", e.Message);
    }

    [Fact]
    public async Task Purchase_create_runs_the_guard()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync(gross: 30_000m);
        var create = new SiagroB1.Application.Services.PurchaseInvoices.PurchaseInvoicesCreateService(
            db, new FakeBusinessPartnerService(), new FakeItemService(), TaxTestServices.InactivePurchaseApply(db),
            new FakeDocNumberSequenceService());

        await Assert.ThrowsAsync<DefaultException>(() =>
            create.ExecuteAsync(Purchase(t, StorageCustodyRole.ProcessingEntry, 1m), "tester"));

        var ok = Purchase(t, StorageCustodyRole.ProcessingEntry, 30_000m);
        await create.ExecuteAsync(ok, "tester");
        Assert.Equal(StorageCustodyRole.ProcessingEntry,
            (await db.Context.PurchaseInvoices.SingleAsync(i => i.Key == ok.Key)).CustodyRole);
    }

    [Fact]
    public async Task Purchase_item_delete_cannot_leave_the_romaneio_without_custody_line()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        var saved = await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m);

        await Assert.ThrowsAsync<DefaultException>(() =>
            new SiagroB1.Application.Services.PurchaseInvoices.PurchaseInvoicesItemsDeleteService(db, TaxTestServices.Gate(db, "STANDALONE"))
                .ExecuteAsync(saved.Items.Single().Key!.Value));
    }
}
