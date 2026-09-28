using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class ContractDraftsApplyProviderStateServiceTests
{
    private readonly ContractDraftsTestContext _ctx = new();

    /// <summary>Minuta já enviada, com os dois signatários no snapshot.</summary>
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

    /// <summary>Espelho de <see cref="SentDraftAsync"/> do lado de venda.</summary>
    private async Task<ContractDraft> SentSalesDraftAsync()
    {
        var template = await _ctx.SeedTemplateAsync("{{numero}}", ContractTemplateScope.Sales);
        var contract = await _ctx.SeedSalesAsync();
        var draft = await _ctx.Create().ExecuteAsync(ContractDraftContractType.Sales, contract.Key, template.Key,
            ContractDraftType.Contract, "m", "t", default);
        await _ctx.SeedSignatoriesAsync(cardCode: "C0001");
        await _ctx.SendToSignature().ExecuteAsync(draft.Key, "tester", default);
        return draft;
    }

    private static ESignatureDocumentState State(ESignatureDocumentStatus status, params (string Email, bool Signed)[] signers) =>
        new(status, signers.Select(s => new ESignatureSignerState(s.Email, s.Signed, s.Signed ? DateTime.Now : null, null)).ToList());

    [Fact]
    public async Task One_signature_moves_the_draft_to_partially_signed()
    {
        var draft = await SentDraftAsync();

        var changed = await _ctx.ApplyState().ExecuteAsync(draft.Key,
            State(ESignatureDocumentStatus.Pending, ("diretor@tagui.com", true), ("produtor@x.com", false)),
            "webhook", default);

        Assert.True(changed);
        var saved = await _ctx.Db.Context.ContractDrafts.Include(d => d.Signers).SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.PartiallySigned, saved.Status);
        Assert.Equal(SignerStatus.Signed, saved.Signers.Single(s => s.Email == "diretor@tagui.com").Status);
        Assert.Equal(SignerStatus.Pending, saved.Signers.Single(s => s.Email == "produtor@x.com").Status);
        Assert.Equal(0, _ctx.Signature.DownloadCalls);
    }

    [Fact]
    public async Task Finished_attaches_the_signed_pdf_and_marks_the_contract_signed()
    {
        var draft = await SentDraftAsync();
        _ctx.Signature.SignedPdfIs([9, 9, 9]);

        await _ctx.ApplyState().ExecuteAsync(draft.Key,
            State(ESignatureDocumentStatus.Finished, ("diretor@tagui.com", true), ("produtor@x.com", true)),
            "webhook", default);

        var saved = await _ctx.Db.Context.ContractDrafts.SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.Signed, saved.Status);
        Assert.NotNull(saved.SignedAt);
        Assert.NotNull(saved.SignedAttachmentKey);

        var attachment = await _ctx.Db.Context.PurchaseContractAttachments.SingleAsync();
        Assert.Equal(saved.SignedAttachmentKey, attachment.Key);
        Assert.Equal([9, 9, 9], attachment.FileData);
        Assert.Equal("Minuta 1 assinada", attachment.Description);
        Assert.Equal("application/pdf", attachment.ContentType);

        var contract = await _ctx.Db.Context.PurchaseContracts.SingleAsync(c => c.Key == saved.PurchaseContractKey);
        Assert.Equal(SignatureStatus.Signed, contract.SignatureStatus);
    }

    [Fact]
    public async Task Applying_the_same_state_twice_changes_nothing()
    {
        var draft = await SentDraftAsync();
        var state = State(ESignatureDocumentStatus.Finished, ("diretor@tagui.com", true), ("produtor@x.com", true));

        await _ctx.ApplyState().ExecuteAsync(draft.Key, state, "webhook", default);
        var changedAgain = await _ctx.ApplyState().ExecuteAsync(draft.Key, state, "webhook", default);

        Assert.False(changedAgain);
        Assert.Equal(1, await _ctx.Db.Context.PurchaseContractAttachments.CountAsync());
        Assert.Equal(1, _ctx.Signature.DownloadCalls);
        var logs = await _ctx.Db.Context.PurchaseContractsChangeLogs
            .Where(l => l.NewValue == "Minuta 1 assinada").CountAsync();
        Assert.Equal(1, logs);
    }

    [Fact]
    public async Task Canceled_at_the_provider_cancels_the_draft_without_touching_the_contract()
    {
        var draft = await SentDraftAsync();

        await _ctx.ApplyState().ExecuteAsync(draft.Key, State(ESignatureDocumentStatus.Canceled), "webhook", default);

        var saved = await _ctx.Db.Context.ContractDrafts.SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.Canceled, saved.Status);

        // O usuário pode ter assinado em papel: o contrato continua como estava.
        var contract = await _ctx.Db.Context.PurchaseContracts.SingleAsync(c => c.Key == saved.PurchaseContractKey);
        Assert.Equal(SignatureStatus.AwaitingSignature, contract.SignatureStatus);
    }

    [Fact]
    public async Task Unknown_state_is_ignored()
    {
        var draft = await SentDraftAsync();

        var changed = await _ctx.ApplyState().ExecuteAsync(draft.Key,
            ESignatureDocumentState.Unknown("provedor fora do ar"), "job", default);

        Assert.False(changed);
        var saved = await _ctx.Db.Context.ContractDrafts.SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.AwaitingSignature, saved.Status);
    }

    [Fact]
    public async Task Finished_without_a_signed_pdf_still_marks_signed_and_records_the_reason()
    {
        var draft = await SentDraftAsync();
        _ctx.Signature.SignedPdfIs(null);

        await _ctx.ApplyState().ExecuteAsync(draft.Key,
            State(ESignatureDocumentStatus.Finished, ("diretor@tagui.com", true), ("produtor@x.com", true)),
            "webhook", default);

        var saved = await _ctx.Db.Context.ContractDrafts.SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.Signed, saved.Status);
        Assert.Null(saved.SignedAttachmentKey);
        Assert.Contains("PDF assinado", saved.LastError);
        Assert.Empty(_ctx.Db.Context.PurchaseContractAttachments);
    }

    /// <summary>
    /// Caminho finalizada-e-assinada do lado de VENDA. Os dois serviços de anexo têm assinaturas
    /// diferentes (o de venda recebe userName, o de compra não) — este é o único teste que passa
    /// pelo ramo de venda de <c>AttachAsync</c>, então uma chamada compilando mas errada se
    /// esconderia sem ele.
    /// </summary>
    [Fact]
    public async Task Finished_attaches_the_signed_pdf_to_the_sales_contract_and_marks_it_signed()
    {
        var draft = await SentSalesDraftAsync();
        _ctx.Signature.SignedPdfIs([7, 7, 7]);

        await _ctx.ApplyState().ExecuteAsync(draft.Key,
            State(ESignatureDocumentStatus.Finished, ("diretor@tagui.com", true), ("produtor@x.com", true)),
            "webhook", default);

        var saved = await _ctx.Db.Context.ContractDrafts.SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.Signed, saved.Status);
        Assert.NotNull(saved.SignedAt);
        Assert.NotNull(saved.SignedAttachmentKey);

        var attachment = await _ctx.Db.Context.SalesContractAttachments.SingleAsync();
        Assert.Equal(saved.SignedAttachmentKey, attachment.Key);
        Assert.Equal([7, 7, 7], attachment.FileData);
        Assert.Equal("Minuta 1 assinada", attachment.Description);
        Assert.Equal("application/pdf", attachment.ContentType);

        var contract = await _ctx.Db.Context.SalesContracts.SingleAsync(c => c.Key == saved.SalesContractKey);
        Assert.Equal(SignatureStatus.Signed, contract.SignatureStatus);
    }

    /// <summary>
    /// Fecha a corrida entre a leitura do estado (antes do download, fora da transação) e a
    /// abertura da transação: simula outra chamada concorrente que cancela a minuta enquanto o
    /// download do PDF assinado está em voo. Usa <c>ChangeTracker.Clear()</c> no MESMO contexto
    /// (em vez de uma segunda conexão) para forçar uma leitura/gravação que realmente passa pelo
    /// banco — a instância `draft` que o serviço já carregou fica intencionalmente desatualizada,
    /// exatamente a situação que o reforço dentro da transação precisa detectar.
    /// </summary>
    [Fact]
    public async Task Loses_the_race_when_the_draft_turns_terminal_during_the_download_call()
    {
        var draft = await SentDraftAsync();
        _ctx.Signature.SignedPdfIs([9, 9, 9]);
        _ctx.Signature.OnDownload(async () =>
        {
            _ctx.Db.Context.ChangeTracker.Clear();
            var racing = await _ctx.Db.Context.ContractDrafts.SingleAsync(d => d.Key == draft.Key);
            racing.Status = ContractDraftStatus.Canceled;
            racing.CanceledAt = DateTime.Now;
            racing.CanceledBy = "webhook-vencedor";
            await _ctx.Db.Context.SaveChangesAsync();
        });

        var changed = await _ctx.ApplyState().ExecuteAsync(draft.Key,
            State(ESignatureDocumentStatus.Finished, ("diretor@tagui.com", true), ("produtor@x.com", true)),
            "webhook", default);

        Assert.False(changed);
        Assert.Empty(_ctx.Db.Context.PurchaseContractAttachments);
        var draftLogs = await _ctx.Db.Context.PurchaseContractsChangeLogs
            .Where(l => l.NewValue == "Minuta 1 assinada").CountAsync();
        Assert.Equal(0, draftLogs);

        // O vencedor da corrida prevalece: continua Cancelada, como quem chegou primeiro gravou.
        var saved = await _ctx.Db.Context.ContractDrafts.AsNoTracking().SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.Canceled, saved.Status);
        Assert.Equal("webhook-vencedor", saved.CanceledBy);
    }

    /// <summary>
    /// Minuta já Signed mas sem anexo (download anterior falhou). Uma nova aplicação do estado,
    /// agora com o PDF disponível, deve completar só o que faltou — sem duplicar o log "Minuta N
    /// assinada" nem escrever de novo o SignatureStatus do contrato. Essa última contagem é o
    /// ponto do teste: sem ela, um bug que repetisse a gravação passaria despercebido.
    /// </summary>
    [Fact]
    public async Task Retries_the_missing_pdf_on_a_draft_already_marked_signed()
    {
        var draft = await SentDraftAsync();
        _ctx.Signature.SignedPdfIs(null);
        var state = State(ESignatureDocumentStatus.Finished, ("diretor@tagui.com", true), ("produtor@x.com", true));

        var firstPass = await _ctx.ApplyState().ExecuteAsync(draft.Key, state, "webhook", default);
        Assert.True(firstPass);
        var afterFirstPass = await _ctx.Db.Context.ContractDrafts.SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.Signed, afterFirstPass.Status);
        Assert.Null(afterFirstPass.SignedAttachmentKey);

        _ctx.Signature.SignedPdfIs([5, 5, 5]);
        var secondPass = await _ctx.ApplyState().ExecuteAsync(draft.Key, state, "webhook", default);

        Assert.True(secondPass);
        var saved = await _ctx.Db.Context.ContractDrafts.SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.Signed, saved.Status);
        Assert.NotNull(saved.SignedAttachmentKey);
        Assert.Null(saved.LastError);

        var attachment = await _ctx.Db.Context.PurchaseContractAttachments.SingleAsync();
        Assert.Equal(saved.SignedAttachmentKey, attachment.Key);
        Assert.Equal([5, 5, 5], attachment.FileData);
        Assert.Equal("Minuta 1 assinada", attachment.Description);

        var draftLogs = await _ctx.Db.Context.PurchaseContractsChangeLogs
            .Where(l => l.NewValue == "Minuta 1 assinada").CountAsync();
        Assert.Equal(1, draftLogs);

        // SentDraftAsync() já grava um log de SignatureStatus (nulo -> AguardandoAssinatura) no
        // envio; o que este teste precisa provar é que a transição PARA Assinado, especificamente,
        // não foi gravada duas vezes pelo reprocessamento.
        var signedStatusLogs = await _ctx.Db.Context.PurchaseContractsChangeLogs
            .Where(l => l.Field == ContractChangeLogFields.SignatureStatus && l.NewValue == "Assinado").CountAsync();
        Assert.Equal(1, signedStatusLogs);

        var contract = await _ctx.Db.Context.PurchaseContracts.SingleAsync(c => c.Key == saved.PurchaseContractKey);
        Assert.Equal(SignatureStatus.Signed, contract.SignatureStatus);
    }
}
