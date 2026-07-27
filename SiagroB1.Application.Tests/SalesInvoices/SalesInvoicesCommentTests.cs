using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Comentários do documento de saída: gravação, regra de autoria (autor ou admin), ausência
/// deliberada de guarda de status e as linhas que cada operação deixa no log de alterações.
/// </summary>
public class SalesInvoicesCommentTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesInvoicesChangeLogService ChangeLog() => new(_db.Context);

    private SalesInvoicesCommentCreateService CreateService() => new(
        _db.Context, ChangeLog(), NullLogger<SalesInvoicesCommentCreateService>.Instance);

    private SalesInvoicesCommentUpdateService UpdateService() => new(
        _db.Context, ChangeLog(), NullLogger<SalesInvoicesCommentUpdateService>.Instance);

    private SalesInvoicesCommentDeleteService DeleteService() => new(
        _db.Context, ChangeLog(), NullLogger<SalesInvoicesCommentDeleteService>.Instance);

    private async Task<SalesInvoice> SeedInvoiceAsync(
        InvoiceStatus status = InvoiceStatus.Pending, string invoiceNumber = "000000001")
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), InvoiceNumber = invoiceNumber, CardCode = "C0001",
            InvoiceStatus = status, InvoiceType = SalesInvoiceType.Normal,
        };
        _db.Context.SalesInvoices.Add(invoice);
        await _db.Context.SaveChangesAsync();
        return invoice;
    }

    private List<SalesInvoiceChangeLog> LogsOf(Guid invoiceKey) =>
        _db.Context.SalesInvoicesChangeLogs.Where(l => l.SalesInvoiceKey == invoiceKey).ToList();

    [Fact]
    public async Task Create_StampsAuthorAndLogsAsInclusion()
    {
        var invoice = await SeedInvoiceAsync();

        var comment = await CreateService()
            .ExecuteAsync(invoice.Key, "  Carga conferida na portaria.  ", "joao");

        Assert.Equal("Carga conferida na portaria.", comment.CommentText);
        Assert.Equal("joao", comment.CommentedBy);
        Assert.NotEqual(default, comment.CommentedAt);

        var log = Assert.Single(LogsOf(invoice.Key));
        Assert.Equal(ContractChangeLogFields.Comment, log.Field);
        Assert.Null(log.OldValue);
        Assert.Equal("Carga conferida na portaria.", log.NewValue);
    }

    [Fact]
    public async Task Create_OnUnknownInvoice_Throws()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => CreateService().ExecuteAsync(Guid.NewGuid(), "texto", "joao"));

        Assert.Empty(_db.Context.SalesInvoicesComments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_WithoutText_Throws(string? text)
    {
        var invoice = await SeedInvoiceAsync();

        await Assert.ThrowsAsync<DefaultException>(
            () => CreateService().ExecuteAsync(invoice.Key, text, "joao"));

        Assert.Empty(_db.Context.SalesInvoicesComments);
        Assert.Empty(LogsOf(invoice.Key));
    }

    [Fact]
    public async Task Create_TextLongerThanColumn_Throws()
    {
        // A coluna é VARCHAR(500), igual à do log: nada é truncado silenciosamente.
        var invoice = await SeedInvoiceAsync();

        await Assert.ThrowsAsync<DefaultException>(
            () => CreateService().ExecuteAsync(invoice.Key, new string('x', 501), "joao"));

        Assert.Empty(_db.Context.SalesInvoicesComments);
    }

    [Theory]
    [InlineData(InvoiceStatus.Pending)]
    [InlineData(InvoiceStatus.Confirmed)]
    [InlineData(InvoiceStatus.Cancelled)]
    [InlineData(InvoiceStatus.Returned)]
    public async Task Create_IsAllowedInAnyStatus(InvoiceStatus status)
    {
        // Comentário é anotação: editável a qualquer tempo, sem guarda de status.
        var invoice = await SeedInvoiceAsync(status);

        await CreateService().ExecuteAsync(invoice.Key, "anotação", "joao");

        Assert.Single(_db.Context.SalesInvoicesComments);
    }

    [Fact]
    public async Task Update_ByAuthor_LogsOldAndNewAndRestampsAuthorship()
    {
        var invoice = await SeedInvoiceAsync();
        var created = await CreateService().ExecuteAsync(invoice.Key, "primeira versão", "joao");
        var createdAt = created.CommentedAt;

        var updated = await UpdateService().ExecuteAsync(
            created.Key!.Value, "segunda versão", "joao", isAdmin: false);

        Assert.Equal("segunda versão", updated.CommentText);
        Assert.Equal("joao", updated.CommentedBy);
        Assert.True(updated.CommentedAt >= createdAt);

        var edit = Assert.Single(
            LogsOf(invoice.Key), l => l.OldValue is not null && l.NewValue is not null);
        Assert.Equal(ContractChangeLogFields.Comment, edit.Field);
        Assert.Equal("primeira versão", edit.OldValue);
        Assert.Equal("segunda versão", edit.NewValue);
    }

    [Fact]
    public async Task Update_ByAnotherUser_ThrowsAndLogsNothing()
    {
        var invoice = await SeedInvoiceAsync();
        var created = await CreateService().ExecuteAsync(invoice.Key, "primeira versão", "joao");

        await Assert.ThrowsAsync<DefaultException>(
            () => UpdateService().ExecuteAsync(created.Key!.Value, "hackeado", "maria", isAdmin: false));

        Assert.Equal("primeira versão", _db.Context.SalesInvoicesComments.Single().CommentText);
        Assert.Single(LogsOf(invoice.Key));
    }

    [Fact]
    public async Task Update_ByAdmin_IsAllowedAndTakesOverAuthorship()
    {
        var invoice = await SeedInvoiceAsync();
        var created = await CreateService().ExecuteAsync(invoice.Key, "primeira versão", "joao");

        var updated = await UpdateService().ExecuteAsync(
            created.Key!.Value, "corrigido pelo admin", "maria", isAdmin: true);

        Assert.Equal("corrigido pelo admin", updated.CommentText);
        Assert.Equal("maria", updated.CommentedBy);
    }

    [Fact]
    public async Task Update_ByAuthorIgnoringCase_IsAllowed()
    {
        var invoice = await SeedInvoiceAsync();
        var created = await CreateService().ExecuteAsync(invoice.Key, "primeira versão", "joao");

        var updated = await UpdateService().ExecuteAsync(
            created.Key!.Value, "segunda versão", "JOAO", isAdmin: false);

        Assert.Equal("segunda versão", updated.CommentText);
    }

    [Fact]
    public async Task Delete_ByAuthor_LogsAsRemoval()
    {
        var invoice = await SeedInvoiceAsync();
        var created = await CreateService().ExecuteAsync(invoice.Key, "some daqui", "joao");

        await DeleteService().ExecuteAsync(created.Key!.Value, "joao", isAdmin: false);

        var removal = Assert.Single(LogsOf(invoice.Key), l => l.NewValue is null);
        Assert.Equal(ContractChangeLogFields.Comment, removal.Field);
        Assert.Equal("some daqui", removal.OldValue);
        Assert.Empty(_db.Context.SalesInvoicesComments);
    }

    [Fact]
    public async Task Delete_ByAnotherUser_ThrowsAndKeepsComment()
    {
        var invoice = await SeedInvoiceAsync();
        var created = await CreateService().ExecuteAsync(invoice.Key, "some daqui", "joao");

        await Assert.ThrowsAsync<DefaultException>(
            () => DeleteService().ExecuteAsync(created.Key!.Value, "maria", isAdmin: false));

        Assert.Single(_db.Context.SalesInvoicesComments);
        Assert.Single(LogsOf(invoice.Key));
    }

    [Fact]
    public async Task Delete_UnknownComment_Throws()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => DeleteService().ExecuteAsync(Guid.NewGuid(), "joao", isAdmin: true));
    }

    [Fact]
    public async Task Get_ReturnsOnlyInvoiceCommentsNewestFirst()
    {
        var invoice = await SeedInvoiceAsync();
        var other = await SeedInvoiceAsync(invoiceNumber: "000000002");

        var first = await CreateService().ExecuteAsync(invoice.Key, "mais antigo", "joao");
        first.CommentedAt = DateTime.Now.AddHours(-2);
        await _db.Context.SaveChangesAsync();

        await CreateService().ExecuteAsync(invoice.Key, "mais novo", "maria");
        await CreateService().ExecuteAsync(other.Key, "de outro documento", "joao");

        var list = new SalesInvoicesCommentsGetService(_db.Context).QueryAll(invoice.Key).ToList();

        Assert.Equal(2, list.Count);
        Assert.Equal("mais novo", list[0].CommentText);
        Assert.Equal("mais antigo", list[1].CommentText);
    }

    [Fact]
    public async Task ChangeLogsGet_ReturnsOnlyInvoiceLogsNewestFirst()
    {
        var invoice = await SeedInvoiceAsync();
        var other = await SeedInvoiceAsync(invoiceNumber: "000000002");

        await CreateService().ExecuteAsync(invoice.Key, "mais antigo", "joao");
        LogsOf(invoice.Key).Single().ChangedAt = DateTime.Now.AddHours(-2);
        await _db.Context.SaveChangesAsync();

        await CreateService().ExecuteAsync(invoice.Key, "mais novo", "joao");
        await CreateService().ExecuteAsync(other.Key, "de outro documento", "joao");

        var list = new SalesInvoicesChangeLogsGetService(_db.Context).QueryAll(invoice.Key).ToList();

        Assert.Equal(2, list.Count);
        Assert.Equal("mais novo", list[0].NewValue);
        Assert.Equal("mais antigo", list[1].NewValue);
    }
}
