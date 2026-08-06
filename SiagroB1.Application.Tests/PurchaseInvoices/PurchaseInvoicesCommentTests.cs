using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Comentários do documento de entrada: gravação, regra de autoria (autor ou admin), ausência
/// DELIBERADA de guarda de status, e as linhas que cada operação deixa no log de alterações.
/// </summary>
public class PurchaseInvoicesCommentTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseInvoicesChangeLogService ChangeLog() => new(_db.Context);

    private PurchaseInvoicesCommentCreateService CreateService() => new(
        _db.Context, ChangeLog(), NullLogger<PurchaseInvoicesCommentCreateService>.Instance);

    private PurchaseInvoicesCommentUpdateService UpdateService() => new(
        _db.Context, ChangeLog(), NullLogger<PurchaseInvoicesCommentUpdateService>.Instance);

    private PurchaseInvoicesCommentDeleteService DeleteService() => new(
        _db.Context, ChangeLog(), NullLogger<PurchaseInvoicesCommentDeleteService>.Instance);

    private async Task<PurchaseInvoice> SeedInvoiceAsync(
        InvoiceStatus status = InvoiceStatus.Pending)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), CardCode = "F0001", InvoiceStatus = status,
        };
        _db.Context.PurchaseInvoices.Add(invoice);
        await _db.Context.SaveChangesAsync();
        return invoice;
    }

    private List<PurchaseInvoiceChangeLog> LogsOf(Guid invoiceKey) =>
        _db.Context.PurchaseInvoicesChangeLogs
            .Where(l => l.PurchaseInvoiceKey == invoiceKey).ToList();

    [Fact]
    public async Task Create_stamps_the_author_and_logs_an_inclusion()
    {
        var invoice = await SeedInvoiceAsync();

        var comment = await CreateService()
            .ExecuteAsync(invoice.Key, "  Carga conferida na portaria.  ", "joao");

        Assert.Equal("Carga conferida na portaria.", comment.CommentText);
        Assert.Equal("joao", comment.CommentedBy);

        var log = Assert.Single(LogsOf(invoice.Key));
        Assert.Equal(ContractChangeLogFields.Comment, log.Field);
        Assert.Null(log.OldValue);
        Assert.Equal("Carga conferida na portaria.", log.NewValue);
    }

    [Fact]
    public async Task Create_on_unknown_invoice_throws_and_writes_nothing()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => CreateService().ExecuteAsync(Guid.NewGuid(), "texto", "joao"));

        Assert.Empty(_db.Context.PurchaseInvoicesComments);
    }

    [Fact]
    public async Task Comment_is_allowed_on_a_confirmed_document()
    {
        var invoice = await SeedInvoiceAsync(InvoiceStatus.Confirmed);

        // Sem guarda de status por decisão: comentário não altera valor, peso nem saldo.
        var comment = await CreateService().ExecuteAsync(invoice.Key, "conferido", "joao");

        Assert.NotNull(comment.Key);
    }

    [Fact]
    public async Task Comment_is_allowed_on_a_cancelled_document()
    {
        var invoice = await SeedInvoiceAsync(InvoiceStatus.Cancelled);

        var comment = await CreateService().ExecuteAsync(invoice.Key, "motivo do cancelamento", "joao");

        Assert.NotNull(comment.Key);
    }

    [Fact]
    public async Task Update_rewrites_the_text_and_logs_the_previous_one()
    {
        var invoice = await SeedInvoiceAsync();
        var comment = await CreateService().ExecuteAsync(invoice.Key, "primeiro", "joao");

        await UpdateService().ExecuteAsync(comment.Key!.Value, "segundo", "joao", isAdmin: false);

        Assert.Equal("segundo", comment.CommentText);

        var logs = LogsOf(invoice.Key);
        Assert.Equal(2, logs.Count);
        Assert.Contains(logs, l => l.OldValue == "primeiro" && l.NewValue == "segundo");
    }

    [Fact]
    public async Task Another_user_cannot_edit_someone_elses_comment()
    {
        var invoice = await SeedInvoiceAsync();
        var comment = await CreateService().ExecuteAsync(invoice.Key, "da ana", "ana");

        await Assert.ThrowsAsync<DefaultException>(
            () => UpdateService().ExecuteAsync(comment.Key!.Value, "editado", "bruno", isAdmin: false));
    }

    [Fact]
    public async Task An_admin_can_edit_someone_elses_comment()
    {
        var invoice = await SeedInvoiceAsync();
        var comment = await CreateService().ExecuteAsync(invoice.Key, "da ana", "ana");

        await UpdateService().ExecuteAsync(comment.Key!.Value, "corrigido", "chefe", isAdmin: true);

        Assert.Equal("corrigido", comment.CommentText);
        // A autoria passa a ser de quem escreveu por último.
        Assert.Equal("chefe", comment.CommentedBy);
    }

    [Fact]
    public async Task Delete_removes_the_comment_and_keeps_the_text_in_the_log()
    {
        var invoice = await SeedInvoiceAsync();
        var comment = await CreateService().ExecuteAsync(invoice.Key, "some daqui", "joao");

        await DeleteService().ExecuteAsync(comment.Key!.Value, "joao", isAdmin: false);

        Assert.Empty(_db.Context.PurchaseInvoicesComments);

        // É o log que permite reconstituir o que foi apagado.
        Assert.Contains(LogsOf(invoice.Key), l => l.OldValue == "some daqui" && l.NewValue == null);
    }

    [Fact]
    public async Task Blank_comment_is_refused()
    {
        var invoice = await SeedInvoiceAsync();

        await Assert.ThrowsAsync<DefaultException>(
            () => CreateService().ExecuteAsync(invoice.Key, "   ", "joao"));
    }
}
