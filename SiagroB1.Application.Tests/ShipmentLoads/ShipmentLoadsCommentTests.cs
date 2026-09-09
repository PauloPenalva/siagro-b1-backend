using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Comentários da carga: gravação, regra de autoria (autor ou admin), ausência DELIBERADA de
/// guarda de status, e as linhas que cada operação deixa no log de alterações.
/// </summary>
public class ShipmentLoadsCommentTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsChangeLogService ChangeLog() => new(_db.Context);

    private ShipmentLoadsCommentCreateService CreateService() => new(
        _db.Context, ChangeLog(), NullLogger<ShipmentLoadsCommentCreateService>.Instance);

    private ShipmentLoadsCommentUpdateService UpdateService() => new(
        _db.Context, ChangeLog(), NullLogger<ShipmentLoadsCommentUpdateService>.Instance);

    private ShipmentLoadsCommentDeleteService DeleteService() => new(
        _db.Context, ChangeLog(), NullLogger<ShipmentLoadsCommentDeleteService>.Instance);

    private async Task<ShipmentLoad> SeedLoadAsync(
        ShipmentLoadStatus status = ShipmentLoadStatus.Planned)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = "ARM01",
            Status = status,
        };

        _db.Context.ShipmentLoads.Add(load);
        await _db.Context.SaveChangesAsync();
        return load;
    }

    private List<ShipmentLoadChangeLog> LogsOf(Guid loadKey) =>
        _db.Context.ShipmentLoadsChangeLogs.Where(l => l.ShipmentLoadKey == loadKey).ToList();

    [Fact]
    public async Task Create_stamps_the_author_and_logs_an_inclusion()
    {
        var load = await SeedLoadAsync();

        var comment = await CreateService()
            .ExecuteAsync(load.Key, "  Motorista trocou na portaria.  ", "joao");

        Assert.Equal("Motorista trocou na portaria.", comment.CommentText);
        Assert.Equal("joao", comment.CommentedBy);

        var log = Assert.Single(LogsOf(load.Key));
        Assert.Equal(ShipmentLoadChangeLogFields.Comment, log.Field);
        Assert.Null(log.OldValue);
        Assert.Equal("Motorista trocou na portaria.", log.NewValue);
    }

    [Fact]
    public async Task Create_on_unknown_load_throws_and_writes_nothing()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => CreateService().ExecuteAsync(Guid.NewGuid(), "texto", "joao"));

        Assert.Empty(_db.Context.ShipmentLoadsComments.ToList());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Create_refuses_an_empty_text(string? text)
    {
        var load = await SeedLoadAsync();

        await Assert.ThrowsAsync<DefaultException>(
            () => CreateService().ExecuteAsync(load.Key, text, "joao"));
    }

    [Fact]
    public async Task Create_refuses_a_text_longer_than_the_column_instead_of_truncating()
    {
        var load = await SeedLoadAsync();

        // Recusa, e nao trunca: cortar em silencio esconderia parte do que o usuario escreveu.
        await Assert.ThrowsAsync<DefaultException>(
            () => CreateService().ExecuteAsync(load.Key, new string('a', 501), "joao"));
    }

    [Fact]
    public async Task Comment_is_allowed_on_a_cancelled_load()
    {
        // Sem guarda de status por decisao: comentar carga cancelada e o uso mais comum.
        var load = await SeedLoadAsync(ShipmentLoadStatus.Cancelled);

        var comment = await CreateService().ExecuteAsync(load.Key, "motivo do cancelamento", "joao");

        Assert.NotNull(comment.Key);
    }

    [Fact]
    public async Task Update_rewrites_the_stamp_and_logs_the_previous_text()
    {
        var load = await SeedLoadAsync();
        var comment = await CreateService().ExecuteAsync(load.Key, "antes", "joao");

        var updated = await UpdateService()
            .ExecuteAsync(comment.Key!.Value, "depois", "joao", isAdmin: false);

        Assert.Equal("depois", updated.CommentText);
        Assert.Equal("joao", updated.CommentedBy);

        var log = LogsOf(load.Key).Single(l => l.OldValue == "antes");
        Assert.Equal(ShipmentLoadChangeLogFields.Comment, log.Field);
        Assert.Equal("depois", log.NewValue);
    }

    [Fact]
    public async Task Delete_keeps_the_removed_text_in_the_log()
    {
        var load = await SeedLoadAsync();
        var comment = await CreateService().ExecuteAsync(load.Key, "some daqui", "joao");

        await DeleteService().ExecuteAsync(comment.Key!.Value, "joao", isAdmin: false);

        Assert.Empty(_db.Context.ShipmentLoadsComments.ToList());

        var log = LogsOf(load.Key).Single(l => l.NewValue == null);
        Assert.Equal("some daqui", log.OldValue);
    }

    [Fact]
    public async Task A_non_author_can_neither_edit_nor_delete()
    {
        var load = await SeedLoadAsync();
        var comment = await CreateService().ExecuteAsync(load.Key, "do joao", "joao");

        await Assert.ThrowsAsync<DefaultException>(
            () => UpdateService().ExecuteAsync(comment.Key!.Value, "x", "maria", isAdmin: false));

        await Assert.ThrowsAsync<DefaultException>(
            () => DeleteService().ExecuteAsync(comment.Key!.Value, "maria", isAdmin: false));
    }

    [Fact]
    public async Task An_admin_can_edit_and_delete_someone_elses_comment()
    {
        var load = await SeedLoadAsync();
        var comment = await CreateService().ExecuteAsync(load.Key, "do joao", "joao");

        var updated = await UpdateService()
            .ExecuteAsync(comment.Key!.Value, "editado pelo admin", "maria", isAdmin: true);

        // O carimbo passa a ser de quem editou.
        Assert.Equal("maria", updated.CommentedBy);

        await DeleteService().ExecuteAsync(comment.Key!.Value, "maria", isAdmin: true);
        Assert.Empty(_db.Context.ShipmentLoadsComments.ToList());
    }
}
