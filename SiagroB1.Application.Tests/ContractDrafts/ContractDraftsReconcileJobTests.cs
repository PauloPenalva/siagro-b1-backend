using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Jobs;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class ContractDraftsReconcileJobTests
{
    private readonly ContractDraftsTestContext _ctx = new();

    private ContractDraftsReconcileJob Job(IServiceScopeFactory? scopes = null) => new(
        _ctx.Db.Context, scopes ?? _ctx.Scopes(), NullLogger<ContractDraftsReconcileJob>.Instance);

    /// <summary>
    /// Leitura que NÃO passa pelo identity map do contexto do teste. O job trabalha cada minuta
    /// num escopo próprio — outro <see cref="AppDbContext"/> —, então a cópia rastreada aqui é
    /// anterior ao que o job gravou; sem AsNoTracking o teste conferiria o passado.
    /// </summary>
    private Task<ContractDraft> ReloadAsync(Guid key) =>
        _ctx.Db.Context.ContractDrafts.AsNoTracking().SingleAsync(d => d.Key == key);

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
        var saved = await ReloadAsync(draft.Key);
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

        // Documento FINALIZADO no provedor: é a única situação em que o PDF baixado é o
        // certificado e pode virar o anexo legal da minuta.
        _ctx.Signature.StateIs(new ESignatureDocumentState(ESignatureDocumentStatus.Finished, []));
        _ctx.Signature.SignedPdfIs([5, 5, 5]);

        var updated = await Job().ExecuteAsync(default);

        Assert.Equal(1, updated);
        var saved = await ReloadAsync(draft.Key);
        Assert.Equal(ContractDraftStatus.Signed, saved.Status);
        Assert.NotNull(saved.SignedAttachmentKey);
        Assert.Null(saved.LastError);

        var attachment = await _ctx.Db.Context.PurchaseContractAttachments.AsNoTracking().SingleAsync();
        Assert.Equal(saved.SignedAttachmentKey, attachment.Key);
        Assert.Equal(new byte[] { 5, 5, 5 }, attachment.FileData);
    }

    /// <summary>
    /// Inanição: numa minuta que ninguém assina nada muda, logo o UpdatedAt nunca anda. Se a
    /// antiguidade viesse dele, essa minuta voltaria ao lote a cada 30 min para sempre e, passando
    /// de 50 minutas assim — rotina numa fila de assinatura depois de alguns meses —, a rede de
    /// segurança nunca mais alcançaria uma minuta nova. O LastCheckedAt é carimbado mesmo sem
    /// mudança nenhuma, e é isso que tira a minuta parada da frente do próximo lote.
    /// </summary>
    [Fact]
    public async Task Stamps_the_check_even_when_nothing_changes_so_a_stuck_draft_leaves_the_batch()
    {
        var draft = await SentDraftAsync(DateTime.Now.AddHours(-7));
        _ctx.Signature.StateIs(new ESignatureDocumentState(ESignatureDocumentStatus.Pending, []));

        var first = await Job().ExecuteAsync(default);

        Assert.Equal(0, first);
        Assert.Equal(1, _ctx.Signature.StateCalls);

        var afterFirst = await ReloadAsync(draft.Key);
        Assert.NotNull(afterFirst.LastCheckedAt);
        Assert.Equal(ContractDraftStatus.AwaitingSignature, afterFirst.Status);

        // O UpdatedAt é auditoria que o usuário lê: uma conferência silenciosa não pode assinar
        // "reconciliacao" por cima de quem de fato mexeu na minuta.
        Assert.Equal("tester", afterFirst.UpdatedBy);

        var second = await Job().ExecuteAsync(default);

        Assert.Equal(0, second);
        Assert.Equal(1, _ctx.Signature.StateCalls);
    }

    /// <summary>
    /// LastCheckedAt nulo é o estado de toda linha anterior à criação do campo. A consulta cai no
    /// UpdatedAt nesse caso — se tratasse "nunca conferida" como recém-conferida, a migração teria
    /// deixado todas as minutas em voo fora da rede de segurança.
    /// </summary>
    [Fact]
    public async Task A_draft_that_was_never_checked_falls_back_to_updated_at()
    {
        var draft = await SentDraftAsync(DateTime.Now.AddHours(-7));
        Assert.Null((await ReloadAsync(draft.Key)).LastCheckedAt);

        await Job().ExecuteAsync(default);

        Assert.Equal(1, _ctx.Signature.StateCalls);
    }

    /// <summary>Recém-conferida ganha do UpdatedAt velho: o fallback vale só para quem nunca foi conferida.</summary>
    [Fact]
    public async Task A_recently_checked_draft_is_skipped_even_with_an_old_updated_at()
    {
        var draft = await SentDraftAsync(DateTime.Now.AddDays(-30));
        draft.LastCheckedAt = DateTime.Now.AddMinutes(-5);
        await _ctx.Db.Context.SaveChangesAsync();

        var updated = await Job().ExecuteAsync(default);

        Assert.Equal(0, updated);
        Assert.Equal(0, _ctx.Signature.StateCalls);
    }

    /// <summary>
    /// Isolamento por minuta. Com um único AppDbContext para o lote, o rollback de uma minuta
    /// deixa a entidade modificada e o anexo já salvo pendurados no change tracker, e o
    /// SaveChanges da minuta SEGUINTE os despeja dentro da transação dela — podendo commitar a
    /// primeira como Signed apontando para um anexo que o banco desfez. A garantia não está em
    /// tentar reproduzir esse rollback (transação é no-op no provider InMemory), e sim em o job
    /// resolver um escopo — logo um AppDbContext, logo um change tracker — para CADA minuta:
    /// assim não existe caminho por onde o que sobrou de uma alcance a transação da outra.
    /// </summary>
    [Fact]
    public async Task Gives_each_draft_its_own_db_context()
    {
        var first = await SentDraftAsync(DateTime.Now.AddHours(-7));

        // Segunda minuta do MESMO contrato: reaproveita filial, modelo e signatários já semeados.
        var second = await _ctx.Create().ExecuteAsync(ContractDraftContractType.Purchase,
            first.PurchaseContractKey!.Value, first.TemplateKey, ContractDraftType.Contract, "m", "t", default);
        await _ctx.SendToSignature().ExecuteAsync(second.Key, "tester", default);
        second.UpdatedAt = DateTime.Now.AddHours(-8);
        await _ctx.Db.Context.SaveChangesAsync();

        var scopes = new RecordingScopeFactory(_ctx.Scopes());
        await Job(scopes).ExecuteAsync(default);

        Assert.Equal(2, scopes.Contexts.Count);
        Assert.NotSame(scopes.Contexts[0], scopes.Contexts[1]);
        Assert.DoesNotContain(_ctx.Db.Context, scopes.Contexts);
    }

    /// <summary>Anota o <see cref="AppDbContext"/> que cada escopo entrega ao job.</summary>
    private sealed class RecordingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        public List<AppDbContext> Contexts { get; } = [];

        public IServiceScope CreateScope()
        {
            var scope = inner.CreateScope();

            // Resolvido aqui de propósito: o escopo guarda a instância, então é exatamente a que
            // o ContractDraftsRefreshStateService recebe logo em seguida.
            Contexts.Add(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            return scope;
        }
    }
}
