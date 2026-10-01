using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using Microsoft.AspNetCore.OData.Formatter;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Web.Actions.ContractDrafts;

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

    /// <summary>
    /// Regressao: o tipo da minuta so muda quando o chamador MANDA um tipo.
    ///
    /// TryGetDraftType devolvia Contract para parametro ausente e o service atribuia sem guarda,
    /// entao qualquer Salvar que omitisse DraftType - e a tela so manda Key e BodyHtml - virava
    /// um Aditivo em Contrato, calado. Description, na linha de cima, ja era guardado assim.
    /// </summary>
    [Fact]
    public async Task Update_keeps_the_type_when_the_caller_omits_it()
    {
        var draft = await DraftAsync();
        await _ctx.Update().ExecuteAsync(draft.Key, "Aditivo 1", ContractDraftType.Amendment, "<p>a</p>", "editor");

        await _ctx.Update().ExecuteAsync(draft.Key, null, null, "<p>so o texto mudou</p>", "editor");

        var reloaded = await _ctx.Db.Context.ContractDrafts.AsNoTracking().SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftType.Amendment, reloaded.DraftType);
        Assert.Equal("Aditivo 1", reloaded.Description);
        Assert.Equal("<p>so o texto mudou</p>", reloaded.BodyHtml);
    }

    [Fact]
    public void Optional_draft_type_distinguishes_absent_from_invalid()
    {
        Assert.True(ContractDraftActionParameters.TryGetOptionalDraftType(
            new ODataActionParameters(), out var absent));
        Assert.Null(absent);

        Assert.True(ContractDraftActionParameters.TryGetOptionalDraftType(
            new ODataActionParameters { ["DraftType"] = "Termination" }, out var given));
        Assert.Equal(ContractDraftType.Termination, given);

        Assert.False(ContractDraftActionParameters.TryGetOptionalDraftType(
            new ODataActionParameters { ["DraftType"] = "Xpto" }, out _));
    }
}
