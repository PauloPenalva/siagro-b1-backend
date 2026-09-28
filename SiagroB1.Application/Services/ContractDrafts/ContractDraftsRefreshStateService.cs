using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>
/// Busca a situação no provedor e aplica. É o botão "Atualizar situação" da tela e o que o job
/// de reconciliação faz por minuta — a mesma porta de escrita, nunca uma segunda.
/// </summary>
public class ContractDraftsRefreshStateService(
    ContractDraftsLoader loader,
    IESignatureProvider provider,
    IConfiguration configuration,
    ContractDraftsApplyProviderStateService apply)
{
    public async Task<bool> ExecuteAsync(Guid key, string userName, CancellationToken ct = default)
    {
        // Mesma guarda do envio: sem ela, com a assinatura desligada e credenciais em branco, o
        // "Atualizar situação" bate no D4Sign, toma 401 e mostra ao usuário o corpo cru do
        // provedor como se fosse erro de negócio. O job de reconciliação só é registrado com
        // Signature:Enabled ligado (ver Program.cs), então esta guarda não o afeta em operação.
        if (!configuration.GetValue("Signature:Enabled", false))
            throw new BusinessException(ContractDraftsSendToSignatureService.DisabledMessage);

        var draft = await loader.RequireDraftAsync(key, ct);

        if (string.IsNullOrWhiteSpace(draft.ExternalDocumentId) || !IsRefreshable(draft))
            return false;

        var state = await provider.GetStateAsync(draft.ExternalDocumentId, ct);
        return await apply.ExecuteAsync(key, state, userName, ct);
    }

    /// <summary>
    /// Vale atualizar enquanto a assinatura corre — e também quando a minuta já está assinada mas
    /// ficou SEM o PDF assinado: esse é o artefato que dá sentido à funcionalidade, e sem esta
    /// segunda hipótese ele nunca mais seria buscado (o status é terminal e o job só varre
    /// pendentes). Quem decide o que fazer com o estado é o ApplyProviderState.
    /// </summary>
    private static bool IsRefreshable(ContractDraft draft) =>
        draft.Status is ContractDraftStatus.AwaitingSignature or ContractDraftStatus.PartiallySigned
        || (draft.Status == ContractDraftStatus.Signed && draft.SignedAttachmentKey is null);
}
