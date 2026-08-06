using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Máquina de estados do documento de entrada: Pendente → Confirmado → (estorno) → Pendente, e
/// Cancelado a partir de qualquer um dos dois.
///
/// Nesta fase as transições SÓ mudam status — a Fase 3 pendura nelas o efeito da natureza de
/// operação sobre o contrato de compra, sem alterar esta máquina.
/// </summary>
public class PurchaseInvoicesLifecycleTests
{
    private static async Task<(UnitOfWork db, PurchaseInvoice invoice)> SeedAsync(
        InvoiceStatus status = InvoiceStatus.Pending)
    {
        var db = TestDb.CreateUnitOfWork();

        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "F0001",
            InvoiceStatus = status,
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "SOJA", Quantity = 10m, UnitPrice = 1m,
        });

        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();

        return (db, invoice);
    }

    [Fact]
    public async Task Confirm_moves_pending_to_confirmed_and_stamps_the_approver()
    {
        var (db, invoice) = await SeedAsync();

        await new PurchaseInvoicesConfirmService(db).ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, invoice.InvoiceStatus);
        Assert.Equal("tester", invoice.ApprovedBy);
        Assert.NotNull(invoice.ApprovedAt);
    }

    [Fact]
    public async Task Confirm_refuses_a_document_that_is_not_pending()
    {
        var (db, invoice) = await SeedAsync(InvoiceStatus.Confirmed);

        await Assert.ThrowsAsync<DefaultException>(
            () => new PurchaseInvoicesConfirmService(db).ExecuteAsync(invoice.Key, "tester"));
    }

    [Fact]
    public async Task Reverse_confirm_moves_confirmed_back_to_pending_and_clears_the_approval()
    {
        var (db, invoice) = await SeedAsync(InvoiceStatus.Confirmed);
        invoice.ApprovedBy = "outro";
        invoice.ApprovedAt = DateTime.Now;
        await db.SaveChangesAsync();

        await new PurchaseInvoicesReverseConfirmService(db).ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Pending, invoice.InvoiceStatus);
        // O carimbo de aprovação precisa sair junto: documento pendente com aprovador preenchido
        // mente na tela e no relatório.
        Assert.Null(invoice.ApprovedAt);
        Assert.Null(invoice.ApprovedBy);
    }

    [Fact]
    public async Task Reverse_confirm_refuses_a_cancelled_document()
    {
        var (db, invoice) = await SeedAsync(InvoiceStatus.Cancelled);

        await Assert.ThrowsAsync<DefaultException>(
            () => new PurchaseInvoicesReverseConfirmService(db).ExecuteAsync(invoice.Key, "tester"));
    }

    [Fact]
    public async Task Reverse_confirm_refuses_a_pending_document()
    {
        var (db, invoice) = await SeedAsync();

        await Assert.ThrowsAsync<DefaultException>(
            () => new PurchaseInvoicesReverseConfirmService(db).ExecuteAsync(invoice.Key, "tester"));
    }

    [Fact]
    public async Task Cancel_marks_the_document_and_records_the_author()
    {
        var (db, invoice) = await SeedAsync(InvoiceStatus.Confirmed);

        await new PurchaseInvoicesCancelService(db).ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Cancelled, invoice.InvoiceStatus);
        Assert.Equal("tester", invoice.CanceledBy);
        Assert.NotNull(invoice.CanceledAt);
    }

    [Fact]
    public async Task Cancel_works_from_pending_too()
    {
        var (db, invoice) = await SeedAsync();

        await new PurchaseInvoicesCancelService(db).ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Cancelled, invoice.InvoiceStatus);
    }

    [Fact]
    public async Task Cancel_refuses_an_already_cancelled_document()
    {
        var (db, invoice) = await SeedAsync(InvoiceStatus.Cancelled);

        await Assert.ThrowsAsync<DefaultException>(
            () => new PurchaseInvoicesCancelService(db).ExecuteAsync(invoice.Key, "tester"));
    }

    [Fact]
    public async Task Cancel_keeps_the_document_so_the_access_key_stays_traceable()
    {
        var (db, invoice) = await SeedAsync(InvoiceStatus.Confirmed);

        await new PurchaseInvoicesCancelService(db).ExecuteAsync(invoice.Key, "tester");

        // Cancelar LIBERA a chave (o índice único é filtrado por status) sem apagar o documento:
        // o rastro do que foi lançado precisa sobreviver.
        Assert.Equal(1, await db.Context.PurchaseInvoices.CountAsync());
    }

    [Fact]
    public async Task Delete_removes_a_pending_document_with_its_lines()
    {
        var (db, invoice) = await SeedAsync();

        await new PurchaseInvoicesDeleteService(db).ExecuteAsync(invoice.Key);

        Assert.False(await db.Context.PurchaseInvoices.AnyAsync(x => x.Key == invoice.Key));
        // A relação é opcional: sem o RemoveRange as linhas ficariam órfãs em vez de sumir.
        Assert.Equal(0, await db.Context.PurchaseInvoicesItems.CountAsync());
    }

    [Fact]
    public async Task Delete_refuses_a_confirmed_document()
    {
        var (db, invoice) = await SeedAsync(InvoiceStatus.Confirmed);

        await Assert.ThrowsAsync<DefaultException>(
            () => new PurchaseInvoicesDeleteService(db).ExecuteAsync(invoice.Key));
    }

    [Fact]
    public async Task Every_lifecycle_service_throws_not_found_for_an_unknown_key()
    {
        var db = TestDb.CreateUnitOfWork();
        var missing = Guid.NewGuid();

        await Assert.ThrowsAsync<NotFoundException>(
            () => new PurchaseInvoicesConfirmService(db).ExecuteAsync(missing, "tester"));
        await Assert.ThrowsAsync<NotFoundException>(
            () => new PurchaseInvoicesReverseConfirmService(db).ExecuteAsync(missing, "tester"));
        await Assert.ThrowsAsync<NotFoundException>(
            () => new PurchaseInvoicesCancelService(db).ExecuteAsync(missing, "tester"));
        await Assert.ThrowsAsync<NotFoundException>(
            () => new PurchaseInvoicesDeleteService(db).ExecuteAsync(missing));
    }
}
