using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Jobs;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class ContractDraftsReconcileJobTests
{
    private readonly ContractDraftsTestContext _ctx = new();

    private ContractDraftsReconcileJob Job() => new(
        _ctx.Db.Context, _ctx.RefreshState(), NullLogger<ContractDraftsReconcileJob>.Instance);

    private async Task<ContractDraft> SentDraftAsync(DateTime updatedAt)
    {
        var template = await _ctx.SeedTemplateAsync();
        var contract = await _ctx.SeedPurchaseAsync();
        var draft = await _ctx.Create().ExecuteAsync(ContractDraftContractType.Purchase, contract.Key, template.Key,
            ContractDraftType.Contract, "m", "t", default);
        await _ctx.SeedSignatoriesAsync();
        await _ctx.SendToSignature().ExecuteAsync(draft.Key, "tester", default);

        draft.UpdatedAt = updatedAt;
        await _ctx.Db.Context.SaveChangesAsync();
        return draft;
    }

    [Fact]
    public async Task Picks_up_drafts_untouched_for_more_than_six_hours()
    {
        var draft = await SentDraftAsync(DateTime.Now.AddHours(-7));
        _ctx.Signature.StateIs(new ESignatureDocumentState(ESignatureDocumentStatus.Pending,
            [new ESignatureSignerState("diretor@tagui.com", true, DateTime.Now, null)]));

        var updated = await Job().ExecuteAsync(default);

        Assert.Equal(1, updated);
        var saved = await _ctx.Db.Context.ContractDrafts.SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.PartiallySigned, saved.Status);
    }

    [Fact]
    public async Task Skips_drafts_touched_recently()
    {
        await SentDraftAsync(DateTime.Now.AddMinutes(-10));

        var updated = await Job().ExecuteAsync(default);

        Assert.Equal(0, updated);
        Assert.Equal(0, _ctx.Signature.StateCalls);
    }

    [Fact]
    public async Task Skips_drafts_in_a_terminal_status()
    {
        var draft = await SentDraftAsync(DateTime.Now.AddHours(-7));
        // Signed COM anexo é o terminal de verdade — sem o anexo, cairia na segunda população da
        // consulta do job (Signed sem PDF), que é justamente a rede de segurança do teste seguinte.
        draft.Status = ContractDraftStatus.Signed;
        draft.SignedAttachmentKey = Guid.NewGuid();
        await _ctx.Db.Context.SaveChangesAsync();

        var updated = await Job().ExecuteAsync(default);

        Assert.Equal(0, updated);
        Assert.Equal(0, _ctx.Signature.StateCalls);
    }

    /// <summary>
    /// Uma minuta assinada sem PDF (download anterior falhou) é a segunda população da consulta —
    /// sem ela, ninguém tentaria buscar de novo o PDF assinado, o artefato que dá sentido à
    /// funcionalidade. Este teste prova que a consulta do job realmente inclui essa população e
    /// que, com o provedor devolvendo o PDF desta vez, o anexo aparece e o ponteiro é gravado —
    /// não basta o job dizer que "atualizou 1", é preciso ver o anexo em pé.
    /// </summary>
    [Fact]
    public async Task Recovers_a_missing_signed_pdf_on_a_stale_signed_draft()
    {
        var draft = await SentDraftAsync(DateTime.Now.AddHours(-7));
        draft.Status = ContractDraftStatus.Signed;
        draft.SignedAttachmentKey = null;
        draft.LastError = "Documento finalizado, mas o PDF assinado não pôde ser baixado do provedor.";
        await _ctx.Db.Context.SaveChangesAsync();

        _ctx.Signature.SignedPdfIs([5, 5, 5]);

        var updated = await Job().ExecuteAsync(default);

        Assert.Equal(1, updated);
        var saved = await _ctx.Db.Context.ContractDrafts.SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.Signed, saved.Status);
        Assert.NotNull(saved.SignedAttachmentKey);
        Assert.Null(saved.LastError);

        var attachment = await _ctx.Db.Context.PurchaseContractAttachments.SingleAsync();
        Assert.Equal(saved.SignedAttachmentKey, attachment.Key);
        Assert.Equal(new byte[] { 5, 5, 5 }, attachment.FileData);
    }
}
