using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts;

/// <summary>
/// Comentários do contrato de venda: gravação, regra de autoria (autor ou admin), ausência
/// deliberada de guarda de status e as linhas que cada operação deixa no log de alterações.
/// </summary>
public class SalesContractsCommentTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsChangeLogService ChangeLog() => new(_db.Context);

    private SalesContractsCommentCreateService CreateService() => new(
        _db.Context, ChangeLog(), NullLogger<SalesContractsCommentCreateService>.Instance);

    private SalesContractsCommentUpdateService UpdateService() => new(
        _db.Context, ChangeLog(), NullLogger<SalesContractsCommentUpdateService>.Instance);

    private SalesContractsCommentDeleteService DeleteService() => new(
        _db.Context, ChangeLog(), NullLogger<SalesContractsCommentDeleteService>.Instance);

    private async Task<SalesContract> SeedContractAsync(
        ContractStatus status = ContractStatus.Approved, string code = "SC-001")
    {
        var sc = new SalesContract
        {
            Key = Guid.NewGuid(), Code = code, CardCode = "C0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25",
            TotalVolume = 1_000_000m, Status = status,
        };
        _db.Context.SalesContracts.Add(sc);
        await _db.Context.SaveChangesAsync();
        return sc;
    }

    private List<SalesContractChangeLog> LogsOf(Guid contractKey) =>
        _db.Context.SalesContractsChangeLogs.Where(l => l.SalesContractKey == contractKey).ToList();

    [Fact]
    public async Task Create_StampsAuthorAndLogsAsInclusion()
    {
        var sc = await SeedContractAsync();

        var comment = await CreateService().ExecuteAsync(sc.Key, "  Cliente pediu prorrogação.  ", "joao");

        Assert.Equal("Cliente pediu prorrogação.", comment.CommentText);
        Assert.Equal("joao", comment.CommentedBy);
        Assert.NotEqual(default, comment.CommentedAt);

        var log = Assert.Single(LogsOf(sc.Key));
        Assert.Equal(ContractChangeLogFields.Comment, log.Field);
        Assert.Null(log.OldValue);
        Assert.Equal("Cliente pediu prorrogação.", log.NewValue);
    }

    [Fact]
    public async Task Create_OnUnknownContract_Throws()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => CreateService().ExecuteAsync(Guid.NewGuid(), "texto", "joao"));

        Assert.Empty(_db.Context.SalesContractsComments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_WithoutText_Throws(string? text)
    {
        var sc = await SeedContractAsync();

        await Assert.ThrowsAsync<DefaultException>(
            () => CreateService().ExecuteAsync(sc.Key, text, "joao"));

        Assert.Empty(_db.Context.SalesContractsComments);
        Assert.Empty(LogsOf(sc.Key));
    }

    [Fact]
    public async Task Create_TextLongerThanColumn_Throws()
    {
        // A coluna é VARCHAR(500), igual à do log: nada é truncado silenciosamente.
        var sc = await SeedContractAsync();

        await Assert.ThrowsAsync<DefaultException>(
            () => CreateService().ExecuteAsync(sc.Key, new string('x', 501), "joao"));

        Assert.Empty(_db.Context.SalesContractsComments);
    }

    [Theory]
    [InlineData(ContractStatus.Draft)]
    [InlineData(ContractStatus.InApproval)]
    [InlineData(ContractStatus.Finished)]
    [InlineData(ContractStatus.Canceled)]
    public async Task Create_IsAllowedInAnyStatus(ContractStatus status)
    {
        // Comentário é anotação: editável a qualquer tempo, sem guarda de status.
        var sc = await SeedContractAsync(status);

        await CreateService().ExecuteAsync(sc.Key, "anotação", "joao");

        Assert.Single(_db.Context.SalesContractsComments);
    }

    [Fact]
    public async Task Update_ByAuthor_LogsOldAndNewAndRestampsAuthorship()
    {
        var sc = await SeedContractAsync();
        var created = await CreateService().ExecuteAsync(sc.Key, "primeira versão", "joao");
        var createdAt = created.CommentedAt;

        var updated = await UpdateService().ExecuteAsync(
            created.Key!.Value, "segunda versão", "joao", isAdmin: false);

        Assert.Equal("segunda versão", updated.CommentText);
        Assert.Equal("joao", updated.CommentedBy);
        Assert.True(updated.CommentedAt >= createdAt);

        var edit = Assert.Single(LogsOf(sc.Key).Where(l => l.OldValue is not null && l.NewValue is not null));
        Assert.Equal(ContractChangeLogFields.Comment, edit.Field);
        Assert.Equal("primeira versão", edit.OldValue);
        Assert.Equal("segunda versão", edit.NewValue);
    }

    [Fact]
    public async Task Update_ByAnotherUser_ThrowsAndLogsNothing()
    {
        var sc = await SeedContractAsync();
        var created = await CreateService().ExecuteAsync(sc.Key, "primeira versão", "joao");

        await Assert.ThrowsAsync<DefaultException>(
            () => UpdateService().ExecuteAsync(created.Key!.Value, "hackeado", "maria", isAdmin: false));

        Assert.Equal("primeira versão", _db.Context.SalesContractsComments.Single().CommentText);
        Assert.Single(LogsOf(sc.Key));
    }

    [Fact]
    public async Task Update_ByAdmin_IsAllowedAndTakesOverAuthorship()
    {
        var sc = await SeedContractAsync();
        var created = await CreateService().ExecuteAsync(sc.Key, "primeira versão", "joao");

        var updated = await UpdateService().ExecuteAsync(
            created.Key!.Value, "corrigido pelo admin", "maria", isAdmin: true);

        Assert.Equal("corrigido pelo admin", updated.CommentText);
        Assert.Equal("maria", updated.CommentedBy);
    }

    [Fact]
    public async Task Update_ByAuthorIgnoringCase_IsAllowed()
    {
        var sc = await SeedContractAsync();
        var created = await CreateService().ExecuteAsync(sc.Key, "primeira versão", "joao");

        var updated = await UpdateService().ExecuteAsync(
            created.Key!.Value, "segunda versão", "JOAO", isAdmin: false);

        Assert.Equal("segunda versão", updated.CommentText);
    }

    [Fact]
    public async Task Delete_ByAuthor_LogsAsRemoval()
    {
        var sc = await SeedContractAsync();
        var created = await CreateService().ExecuteAsync(sc.Key, "some daqui", "joao");

        await DeleteService().ExecuteAsync(created.Key!.Value, "joao", isAdmin: false);

        var removal = Assert.Single(LogsOf(sc.Key).Where(l => l.NewValue is null));
        Assert.Equal(ContractChangeLogFields.Comment, removal.Field);
        Assert.Equal("some daqui", removal.OldValue);
        Assert.Empty(_db.Context.SalesContractsComments);
    }

    [Fact]
    public async Task Delete_ByAnotherUser_ThrowsAndKeepsComment()
    {
        var sc = await SeedContractAsync();
        var created = await CreateService().ExecuteAsync(sc.Key, "some daqui", "joao");

        await Assert.ThrowsAsync<DefaultException>(
            () => DeleteService().ExecuteAsync(created.Key!.Value, "maria", isAdmin: false));

        Assert.Single(_db.Context.SalesContractsComments);
        Assert.Single(LogsOf(sc.Key));
    }

    [Fact]
    public async Task Delete_UnknownComment_Throws()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => DeleteService().ExecuteAsync(Guid.NewGuid(), "joao", isAdmin: true));
    }

    [Fact]
    public async Task Get_ReturnsOnlyContractCommentsNewestFirst()
    {
        var sc = await SeedContractAsync();
        var other = await SeedContractAsync(code: "SC-002");

        var first = await CreateService().ExecuteAsync(sc.Key, "mais antigo", "joao");
        first.CommentedAt = DateTime.Now.AddHours(-2);
        await _db.Context.SaveChangesAsync();

        await CreateService().ExecuteAsync(sc.Key, "mais novo", "maria");
        await CreateService().ExecuteAsync(other.Key, "de outro contrato", "joao");

        var list = new SalesContractsCommentsGetService(_db.Context).QueryAll(sc.Key).ToList();

        Assert.Equal(2, list.Count);
        Assert.Equal("mais novo", list[0].CommentText);
        Assert.Equal("mais antigo", list[1].CommentText);
    }
}
