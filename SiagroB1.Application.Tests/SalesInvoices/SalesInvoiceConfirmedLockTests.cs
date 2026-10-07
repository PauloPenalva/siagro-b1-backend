using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Confirmar e depois transmitir: com a regra da NF-e ativa, o documento Normal confirmado é o que vai para o XML e já
/// baixou o contrato. Até a NF-e sair, o que vai para a nota não muda pela API — o caminho é estornar a confirmação.
/// Comentários e a Conferência de entregas seguem livres; na Yokotobi (SAPB1) e na MH Agro nada muda.
/// </summary>
public class SalesInvoiceConfirmedLockTests
{
    private const string ConfirmedMessage = "Documento confirmado: estorne a confirmação para alterar.";

    private static async Task<(UnitOfWork Db, SalesInvoice Invoice)> SeedAsync(
        InvoiceStatus status = InvoiceStatus.Confirmed, SalesInvoiceType type = SalesInvoiceType.Normal)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ", StateCode = "SP", IssuesNfe = true });
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = "C1", BranchCode = "01", InvoiceStatus = status, InvoiceType = type,
            NfeStatus = NfeStatus.None, InvoiceDate = new DateTime(2026, 10, 2), Comments = "antes",
            Items =
            [
                new SalesInvoiceItem { Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 10m, UnitPrice = 2m },
            ],
        };
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return (db, invoice);
    }

    private static FakeBusinessPartnerService Partners() =>
        new(names: new() { ["C1"] = "CLIENTE", ["C2"] = "OUTRO" }, states: new() { ["C1"] = "SP", ["C2"] = "SP" });

    private static SalesInvoicesTaxApplyService Apply(UnitOfWork db, string erp) => TaxTestServices.Apply(db, Partners(), erp);

    private static SalesInvoicesUpdateService HeaderUpdate(UnitOfWork db, string erp = "STANDALONE") =>
        new(db, Partners(), Apply(db, erp), TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesUpdateService>.Instance);

    private static SalesInvoicesItemsUpdateService ItemUpdate(UnitOfWork db, string erp = "STANDALONE") =>
        new(db, new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA" }),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            Apply(db, erp), TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesUpdateService>.Instance);

    private static SalesInvoicesItemsCreateService ItemCreate(UnitOfWork db, string erp = "STANDALONE") =>
        new(db, new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA" }), Apply(db, erp),
            TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesItemsCreateService>.Instance);

    private static SalesInvoicesItemsDeleteService ItemDelete(UnitOfWork db, string erp = "STANDALONE") =>
        new(db, Apply(db, erp), NullLogger<SalesInvoicesItemsDeleteService>.Instance);

    [Fact]
    public async Task Confirmed_document_cannot_change_the_customer()
    {
        var (db, invoice) = await SeedAsync();
        invoice.CardCode = "C2";

        var ex = await Assert.ThrowsAsync<DefaultException>(() => HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester"));

        Assert.Equal(ConfirmedMessage, ex.Message);
    }

    [Fact]
    public async Task Confirmed_document_can_change_internal_comments()
    {
        var (db, invoice) = await SeedAsync();
        invoice.Comments = "depois";

        await HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester");

        Assert.Equal("depois", (await db.Context.SalesInvoices.AsNoTracking().SingleAsync()).Comments);
    }

    /// <summary>Yokotobi: a chave da filial é ignorada em SAPB1, e o documento confirmado segue editável como hoje.</summary>
    [Fact]
    public async Task Sapb1_confirmed_document_can_still_change_the_customer()
    {
        var (db, invoice) = await SeedAsync();
        invoice.CardCode = "C2";

        await HeaderUpdate(db, "SAPB1").ExecuteAsync(invoice.Key, invoice, "tester");

        Assert.Equal("C2", (await db.Context.SalesInvoices.AsNoTracking().SingleAsync()).CardCode);
    }

    /// <summary>Campo que vai para o XML mas não entra no cálculo: prova a trava sem montar o cadastro tributário.</summary>
    [Fact]
    public async Task Pending_document_can_change_the_volume()
    {
        var (db, invoice) = await SeedAsync(InvoiceStatus.Pending);
        invoice.VolumeSpecies = "SACO";

        await HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester");

        Assert.Equal("SACO", (await db.Context.SalesInvoices.AsNoTracking().SingleAsync()).VolumeSpecies);
    }

    [Fact]
    public async Task Confirmed_document_cannot_change_the_volume()
    {
        var (db, invoice) = await SeedAsync();
        invoice.VolumeSpecies = "SACO";

        var ex = await Assert.ThrowsAsync<DefaultException>(() => HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester"));

        Assert.Equal(ConfirmedMessage, ex.Message);
    }

    [Fact]
    public async Task Confirmed_line_cannot_change_quantity()
    {
        var (db, _) = await SeedAsync();
        var item = await db.Context.SalesInvoicesItems.SingleAsync();
        item.Quantity = 20m;

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ItemUpdate(db).ExecuteAsync(item.Key!.Value, item, "tester"));

        Assert.Equal(ConfirmedMessage, ex.Message);
    }

    /// <summary>A Conferência de entregas acontece depois da confirmação, pelo mesmo serviço.</summary>
    [Fact]
    public async Task Confirmed_line_accepts_the_delivery_reconciliation()
    {
        var (db, _) = await SeedAsync();
        var item = await db.Context.SalesInvoicesItems.SingleAsync();
        item.DeliveredQuantity = 9m;

        await ItemUpdate(db).ExecuteAsync(item.Key!.Value, item, "tester");

        Assert.Equal(9m, (await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync()).DeliveredQuantity);
    }

    [Fact]
    public async Task Sapb1_confirmed_line_can_still_change_quantity()
    {
        var (db, _) = await SeedAsync();
        var item = await db.Context.SalesInvoicesItems.SingleAsync();
        item.Quantity = 20m;

        await ItemUpdate(db, "SAPB1").ExecuteAsync(item.Key!.Value, item, "tester");

        Assert.Equal(20m, (await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync()).Quantity);
    }

    [Fact]
    public async Task Line_cannot_be_added_to_a_confirmed_document()
    {
        var (db, invoice) = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ItemCreate(db).ExecuteAsync(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 1m,
        }, "tester"));

        Assert.Equal(ConfirmedMessage, ex.Message);
    }

    [Fact]
    public async Task Line_cannot_be_removed_from_a_confirmed_document()
    {
        var (db, _) = await SeedAsync();
        var item = await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ItemDelete(db).ExecuteAsync(item.Key!.Value));

        Assert.Equal(ConfirmedMessage, ex.Message);
    }

    [Fact]
    public async Task Sapb1_confirmed_document_line_can_still_be_removed()
    {
        var (db, _) = await SeedAsync();
        var item = await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync();

        Assert.True(await ItemDelete(db, "SAPB1").ExecuteAsync(item.Key!.Value));
    }
}
