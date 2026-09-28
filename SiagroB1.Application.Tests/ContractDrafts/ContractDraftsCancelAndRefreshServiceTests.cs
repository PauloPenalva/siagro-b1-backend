using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class ContractDraftsCancelAndRefreshServiceTests
{
    private readonly ContractDraftsTestContext _ctx = new();

    private async Task<ContractDraft> SentDraftAsync()
    {
        var template = await _ctx.SeedTemplateAsync();
        var contract = await _ctx.SeedPurchaseAsync();
        var draft = await _ctx.Create().ExecuteAsync(ContractDraftContractType.Purchase, contract.Key, template.Key,
            ContractDraftType.Contract, "m", "t", default);
        await _ctx.SeedSignatoriesAsync();
        await _ctx.SendToSignature().ExecuteAsync(draft.Key, "tester", default);
        return draft;
    }

    [Fact]
    public async Task Cancel_cancels_at_the_provider_and_leaves_the_contract_alone()
    {
        var draft = await SentDraftAsync();

        await _ctx.Cancel().ExecuteAsync(draft.Key, "tester", default);

        Assert.Equal(1, _ctx.Signature.CancelCalls);
        var saved = await _ctx.Db.Context.ContractDrafts.SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.Canceled, saved.Status);
        Assert.Equal("tester", saved.CanceledBy);

        var contract = await _ctx.Db.Context.PurchaseContracts.SingleAsync(c => c.Key == saved.PurchaseContractKey);
        Assert.Equal(SignatureStatus.AwaitingSignature, contract.SignatureStatus);

        var log = await _ctx.Db.Context.PurchaseContractsChangeLogs.ToListAsync();
        Assert.Contains(log, l => l.NewValue == "Minuta 1 cancelada");
    }

    [Fact]
    public async Task Cancel_is_refused_on_a_draft_that_was_never_sent()
    {
        var template = await _ctx.SeedTemplateAsync();
        var contract = await _ctx.SeedPurchaseAsync();
        var draft = await _ctx.Create().ExecuteAsync(ContractDraftContractType.Purchase, contract.Key, template.Key,
            ContractDraftType.Contract, "m", "t", default);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _ctx.Cancel().ExecuteAsync(draft.Key, "t", default));

        Assert.Equal("Só é possível cancelar minuta enviada para assinatura.", ex.Message);
        Assert.Equal(0, _ctx.Signature.CancelCalls);
    }

    [Fact]
    public async Task Cancel_fails_loudly_when_the_provider_refuses()
    {
        var draft = await SentDraftAsync();
        _ctx.Signature.CancelFails("documento já finalizado");

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _ctx.Cancel().ExecuteAsync(draft.Key, "t", default));

        Assert.Contains("já finalizado", ex.Message);
        var saved = await _ctx.Db.Context.ContractDrafts.SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.AwaitingSignature, saved.Status);
    }

    [Fact]
    public async Task Refresh_pulls_the_state_from_the_provider_and_applies_it()
    {
        var draft = await SentDraftAsync();
        _ctx.Signature.StateIs(new ESignatureDocumentState(ESignatureDocumentStatus.Pending,
            [new ESignatureSignerState("diretor@tagui.com", true, DateTime.Now, null)]));

        var changed = await _ctx.RefreshState().ExecuteAsync(draft.Key, "tester", default);

        Assert.True(changed);
        Assert.Equal(1, _ctx.Signature.StateCalls);
        var saved = await _ctx.Db.Context.ContractDrafts.SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.PartiallySigned, saved.Status);
    }

    [Fact]
    public async Task Refresh_on_a_draft_that_was_never_sent_does_nothing()
    {
        var template = await _ctx.SeedTemplateAsync();
        var contract = await _ctx.SeedPurchaseAsync();
        var draft = await _ctx.Create().ExecuteAsync(ContractDraftContractType.Purchase, contract.Key, template.Key,
            ContractDraftType.Contract, "m", "t", default);

        var changed = await _ctx.RefreshState().ExecuteAsync(draft.Key, "tester", default);

        Assert.False(changed);
        Assert.Equal(0, _ctx.Signature.StateCalls);
    }
}
