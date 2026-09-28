using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class ContractDraftsUpdateAndDeleteServiceTests
{
    private readonly ContractDraftsTestContext _ctx = new();

    private async Task<ContractDraft> DraftAsync(ContractDraftStatus status = ContractDraftStatus.Draft)
    {
        var template = await _ctx.SeedTemplateAsync();
        var contract = await _ctx.SeedPurchaseAsync();
        var draft = await _ctx.Create().ExecuteAsync(ContractDraftContractType.Purchase, contract.Key, template.Key,
            ContractDraftType.Contract, "m", "t", default);
        if (status != ContractDraftStatus.Draft)
        {
            draft.Status = status;
            await _ctx.Db.Context.SaveChangesAsync();
        }
        return draft;
    }

    [Fact]
    public async Task Update_changes_text_type_and_description_while_draft()
    {
        var draft = await DraftAsync();

        await _ctx.Update().ExecuteAsync(draft.Key, "Aditivo 1", ContractDraftType.Amendment, "<p>texto editado</p>", "editor");

        var reloaded = await _ctx.Db.Context.ContractDrafts.AsNoTracking().SingleAsync(d => d.Key == draft.Key);
        Assert.Equal("<p>texto editado</p>", reloaded.BodyHtml);
        Assert.Equal(ContractDraftType.Amendment, reloaded.DraftType);
        Assert.Equal("Aditivo 1", reloaded.Description);
        Assert.Equal("editor", reloaded.UpdatedBy);
    }

    [Theory]
    [InlineData(ContractDraftStatus.AwaitingSignature)]
    [InlineData(ContractDraftStatus.Signed)]
    [InlineData(ContractDraftStatus.Canceled)]
    public async Task Update_and_delete_are_refused_after_draft(ContractDraftStatus status)
    {
        var draft = await DraftAsync(status);

        await Assert.ThrowsAsync<BusinessException>(() => _ctx.Update().ExecuteAsync(draft.Key, "x", ContractDraftType.Contract, "<p/>", "t"));
        await Assert.ThrowsAsync<BusinessException>(() => _ctx.Delete().ExecuteAsync(draft.Key));
    }

    [Fact]
    public async Task Delete_removes_the_draft()
    {
        var draft = await DraftAsync();

        await _ctx.Delete().ExecuteAsync(draft.Key);

        Assert.Empty(_ctx.Db.Context.ContractDrafts);
    }

    [Fact]
    public async Task Update_rejects_empty_body()
    {
        var draft = await DraftAsync();

        await Assert.ThrowsAsync<BusinessException>(() => _ctx.Update().ExecuteAsync(draft.Key, "x", ContractDraftType.Contract, "  ", "t"));
    }

    [Fact]
    public async Task List_by_contract_returns_dto_without_body_and_get_body_returns_it()
    {
        var draft = await DraftAsync();

        var list = await _ctx.Get().ListByContractAsync(ContractDraftContractType.Purchase, draft.PurchaseContractKey!.Value);
        var body = await _ctx.Get().GetBodyAsync(draft.Key);

        var dto = Assert.Single(list);
        Assert.Equal(draft.Key, dto.Key);
        Assert.Equal(1, dto.Sequence);
        Assert.NotNull(dto.TemplateName);
        Assert.Contains("PC-000123", body);
    }
}
