using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// Comentários do contrato de compra — espelho de SalesContractsCommentTests.
/// </summary>
public class PurchaseContractsCommentTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsChangeLogService ChangeLog() => new(_db.Context);

    private PurchaseContractsCommentCreateService CreateService() => new(
        _db.Context, ChangeLog(), NullLogger<PurchaseContractsCommentCreateService>.Instance);

    private PurchaseContractsCommentUpdateService UpdateService() => new(
        _db.Context, ChangeLog(), NullLogger<PurchaseContractsCommentUpdateService>.Instance);

    private PurchaseContractsCommentDeleteService DeleteService() => new(
        _db.Context, ChangeLog(), NullLogger<PurchaseContractsCommentDeleteService>.Instance);

    private async Task<PurchaseContract> SeedContractAsync(
        ContractStatus status = ContractStatus.Approved, string code = "PC-001")
    {
        var pc = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = code, CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 1_000_000m,
            Status = status,
            Type = ContractType.ToBeDetermined,
        };
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();
        return pc;
    }

    private List<PurchaseContractChangeLog> LogsOf(Guid contractKey) =>
        _db.Context.PurchaseContractsChangeLogs.Where(l => l.PurchaseContractKey == contractKey).ToList();

    [Fact]
    public async Task Create_StampsAuthorAndLogsAsInclusion()
    {
        var pc = await SeedContractAsync();

        var comment = await CreateService().ExecuteAsync(pc.Key, "  Produtor avisou do atraso.  ", "joao");

        Assert.Equal("Produtor avisou do atraso.", comment.CommentText);
        Assert.Equal("joao", comment.CommentedBy);
        Assert.NotEqual(default, comment.CommentedAt);

        var log = Assert.Single(LogsOf(pc.Key));
        Assert.Equal(ContractChangeLogFields.Comment, log.Field);
        Assert.Null(log.OldValue);
        Assert.Equal("Produtor avisou do atraso.", log.NewValue);
    }

    [Fact]
    public async Task Create_OnUnknownContract_Throws()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => CreateService().ExecuteAsync(Guid.NewGuid(), "texto", "joao"));

        Assert.Empty(_db.Context.PurchaseContractsComments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_WithoutText_Throws(string? text)
    {
        var pc = await SeedContractAsync();

        await Assert.ThrowsAsync<DefaultException>(
            () => CreateService().ExecuteAsync(pc.Key, text, "joao"));

        Assert.Empty(_db.Context.PurchaseContractsComments);
        Assert.Empty(LogsOf(pc.Key));
    }

    [Fact]
    public async Task Create_TextLongerThanColumn_Throws()
    {
        var pc = await SeedContractAsync();

        await Assert.ThrowsAsync<DefaultException>(
            () => CreateService().ExecuteAsync(pc.Key, new string('x', 501), "joao"));

        Assert.Empty(_db.Context.PurchaseContractsComments);
    }

    [Theory]
    [InlineData(ContractStatus.Draft)]
    [InlineData(ContractStatus.InApproval)]
    [InlineData(ContractStatus.Finished)]
    [InlineData(ContractStatus.Canceled)]
    public async Task Create_IsAllowedInAnyStatus(ContractStatus status)
    {
        // Contrato encerrado inclusive: o comentário está fora do
        // FinishedContractMutationGuardInterceptor de propósito.
        var pc = await SeedContractAsync(status);

        await CreateService().ExecuteAsync(pc.Key, "anotação", "joao");

        Assert.Single(_db.Context.PurchaseContractsComments);
    }

    [Fact]
    public async Task Update_ByAuthor_LogsOldAndNewAndRestampsAuthorship()
    {
        var pc = await SeedContractAsync();
        var created = await CreateService().ExecuteAsync(pc.Key, "primeira versão", "joao");
        var createdAt = created.CommentedAt;

        var updated = await UpdateService().ExecuteAsync(
            created.Key!.Value, "segunda versão", "joao", isAdmin: false);

        Assert.Equal("segunda versão", updated.CommentText);
        Assert.Equal("joao", updated.CommentedBy);
        Assert.True(updated.CommentedAt >= createdAt);

        var edit = Assert.Single(LogsOf(pc.Key).Where(l => l.OldValue is not null && l.NewValue is not null));
        Assert.Equal(ContractChangeLogFields.Comment, edit.Field);
        Assert.Equal("primeira versão", edit.OldValue);
        Assert.Equal("segunda versão", edit.NewValue);
    }

    [Fact]
    public async Task Update_ByAnotherUser_ThrowsAndLogsNothing()
    {
        var pc = await SeedContractAsync();
        var created = await CreateService().ExecuteAsync(pc.Key, "primeira versão", "joao");

        await Assert.ThrowsAsync<DefaultException>(
            () => UpdateService().ExecuteAsync(created.Key!.Value, "hackeado", "maria", isAdmin: false));

        Assert.Equal("primeira versão", _db.Context.PurchaseContractsComments.Single().CommentText);
        Assert.Single(LogsOf(pc.Key));
    }

    [Fact]
    public async Task Update_ByAdmin_IsAllowedAndTakesOverAuthorship()
    {
        var pc = await SeedContractAsync();
        var created = await CreateService().ExecuteAsync(pc.Key, "primeira versão", "joao");

        var updated = await UpdateService().ExecuteAsync(
            created.Key!.Value, "corrigido pelo admin", "maria", isAdmin: true);

        Assert.Equal("corrigido pelo admin", updated.CommentText);
        Assert.Equal("maria", updated.CommentedBy);
    }

    [Fact]
    public async Task Delete_ByAuthor_LogsAsRemoval()
    {
        var pc = await SeedContractAsync();
        var created = await CreateService().ExecuteAsync(pc.Key, "some daqui", "joao");

        await DeleteService().ExecuteAsync(created.Key!.Value, "joao", isAdmin: false);

        var removal = Assert.Single(LogsOf(pc.Key).Where(l => l.NewValue is null));
        Assert.Equal(ContractChangeLogFields.Comment, removal.Field);
        Assert.Equal("some daqui", removal.OldValue);
        Assert.Empty(_db.Context.PurchaseContractsComments);
    }

    [Fact]
    public async Task Delete_ByAnotherUser_ThrowsAndKeepsComment()
    {
        var pc = await SeedContractAsync();
        var created = await CreateService().ExecuteAsync(pc.Key, "some daqui", "joao");

        await Assert.ThrowsAsync<DefaultException>(
            () => DeleteService().ExecuteAsync(created.Key!.Value, "maria", isAdmin: false));

        Assert.Single(_db.Context.PurchaseContractsComments);
        Assert.Single(LogsOf(pc.Key));
    }

    [Fact]
    public async Task Get_ReturnsOnlyContractCommentsNewestFirst()
    {
        var pc = await SeedContractAsync();
        var other = await SeedContractAsync(code: "PC-002");

        var first = await CreateService().ExecuteAsync(pc.Key, "mais antigo", "joao");
        first.CommentedAt = DateTime.Now.AddHours(-2);
        await _db.Context.SaveChangesAsync();

        await CreateService().ExecuteAsync(pc.Key, "mais novo", "maria");
        await CreateService().ExecuteAsync(other.Key, "de outro contrato", "joao");

        var list = new PurchaseContractsCommentsGetService(_db.Context).QueryAll(pc.Key).ToList();

        Assert.Equal(2, list.Count);
        Assert.Equal("mais novo", list[0].CommentText);
        Assert.Equal("mais antigo", list[1].CommentText);
    }
}
