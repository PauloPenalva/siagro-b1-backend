using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>
/// Porta ÚNICA de escrita a partir do provedor: o webhook e o job de reconciliação chamam este
/// mesmo serviço. Idempotente — aplicar o mesmo estado duas vezes não duplica anexo nem log,
/// porque o webhook do D4Sign reenvia (imediato, 3× em 1 h, 2× em 6 h, 1× em 12 h).
/// </summary>
public class ContractDraftsApplyProviderStateService(
    AppDbContext context,
    ContractDraftsLoader loader,
    IESignatureProvider provider,
    PurchaseContractsAttachmentsCreateService purchaseAttachments,
    SalesContractsAttachmentsCreateService salesAttachments,
    PurchaseContractsChangeLogService purchaseLog,
    SalesContractsChangeLogService salesLog,
    PurchaseContractsSetSignatureStatusService purchaseSignatureStatus,
    SalesContractsSetSignatureStatusService salesSignatureStatus,
    ILogger<ContractDraftsApplyProviderStateService> logger)
{
    /// <returns><c>true</c> quando algo mudou; <c>false</c> quando o estado já era o mesmo.</returns>
    public async Task<bool> ExecuteAsync(Guid draftKey, ESignatureDocumentState state, string userName, CancellationToken ct = default)
    {
        if (state.Status == ESignatureDocumentStatus.Unknown)
        {
            logger.LogWarning("Estado desconhecido do provedor para a minuta {Key}: {Message}", draftKey, state.ErrorMessage);
            return false;
        }

        var draft = await loader.RequireDraftAsync(draftKey, ct);

        // Terminal: nada do provedor reabre uma minuta cancelada, ou assinada COM o PDF já
        // anexado. Assinada SEM anexo (download anterior falhou — ver LastError) continua
        // processável só para tentar buscar o PDF que faltou; não é reabrir, o status já é
        // definitivo. Ver RetryMissingPdfAsync.
        if (draft.Status == ContractDraftStatus.Canceled)
            return false;

        if (draft.Status == ContractDraftStatus.Signed)
            return draft.SignedAttachmentKey is null
                ? await RetryMissingPdfAsync(draft, userName, ct)
                : false;

        var changed = ApplySigners(draft, state);
        var target = NextStatus(draft, state);

        if (target == draft.Status && !changed)
            return false;

        byte[]? signedPdf = null;
        if (target == ContractDraftStatus.Signed)
        {
            signedPdf = await provider.DownloadSignedAsync(draft.ExternalDocumentId ?? "", ct);
            if (signedPdf is null || signedPdf.Length == 0)
                logger.LogWarning("Minuta {Key} finalizada sem PDF assinado disponível", draftKey);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        try
        {
            // Corrida: draft.Status acima é uma leitura de ANTES do download (chamada HTTP de
            // propósito fora da transação — não segura conexão de banco esperando a rede). Outra
            // chamada concorrente para a mesma minuta (webhook e reconciliação chamam este mesmo
            // serviço) pode ter terminalizado a minuta nesse meio-tempo. AsNoTracking: `draft` já
            // está rastreado em memória desde antes do download, então uma consulta rastreada
            // normal seria resolvida pelo identity map e devolveria essa mesma cópia velha sem
            // tocar o banco — só sem tracking a leitura realmente confere o que está gravado agora.
            var current = await context.ContractDrafts.AsNoTracking()
                .Where(d => d.Key == draftKey)
                .Select(d => new { d.Status })
                .FirstOrDefaultAsync(ct);

            if (current is null || current.Status is ContractDraftStatus.Signed or ContractDraftStatus.Canceled)
            {
                await transaction.RollbackAsync(ct);
                return false;
            }

            if (target != draft.Status)
            {
                draft.Status = target;

                if (target == ContractDraftStatus.Signed)
                {
                    draft.SignedAt = DateTime.Now;

                    if (signedPdf is { Length: > 0 })
                    {
                        var description = ContractChangeLogFields.DescribeDraft(draft.Sequence, "assinada");
                        draft.SignedAttachmentKey = await AttachAsync(draft, signedPdf, description, userName);
                        draft.LastError = null;
                    }
                    else
                    {
                        draft.LastError = "Documento finalizado, mas o PDF assinado não pôde ser baixado do provedor.";
                    }

                    var what = ContractChangeLogFields.DescribeDraft(draft.Sequence, "assinada");
                    if (draft.PurchaseContractKey is { } pk)
                    {
                        purchaseLog.Register(pk, ContractChangeLogFields.Draft, null, what, userName);
                        await purchaseSignatureStatus.ExecuteAsync(pk, SignatureStatus.Signed, userName);
                    }
                    if (draft.SalesContractKey is { } sk)
                    {
                        salesLog.Register(sk, ContractChangeLogFields.Draft, null, what, userName);
                        await salesSignatureStatus.ExecuteAsync(sk, SignatureStatus.Signed, userName);
                    }
                }
                else if (target == ContractDraftStatus.Canceled)
                {
                    draft.CanceledAt = DateTime.Now;
                    draft.CanceledBy = userName;

                    // NÃO toca o SignatureStatus do contrato: pode ter sido assinado em papel.
                    var what = ContractChangeLogFields.DescribeDraft(draft.Sequence, "cancelada no provedor");
                    if (draft.PurchaseContractKey is { } pk) purchaseLog.Register(pk, ContractChangeLogFields.Draft, null, what, userName);
                    if (draft.SalesContractKey is { } sk) salesLog.Register(sk, ContractChangeLogFields.Draft, null, what, userName);
                }
            }

            draft.UpdatedAt = DateTime.Now;
            draft.UpdatedBy = userName;

            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync(ct);
            logger.LogError(e, "Falha ao aplicar estado do provedor na minuta {Key}", draftKey);
            throw;
        }
    }

    /// <summary>
    /// Minuta já <see cref="ContractDraftStatus.Signed"/> mas sem PDF anexado — o download
    /// anterior falhou e ficou só o <c>LastError</c> registrado. Não é reabrir a minuta: o status
    /// já é definitivo, o único trabalho possível aqui é tentar buscar o PDF de novo. Por isso não
    /// retransiciona status, não grava um segundo log "Minuta N assinada" nem chama de novo o
    /// *SetSignatureStatusService — isso já aconteceu na primeira passada, e repetir quebraria a
    /// idempotência.
    /// </summary>
    private async Task<bool> RetryMissingPdfAsync(ContractDraft draft, string userName, CancellationToken ct)
    {
        var signedPdf = await provider.DownloadSignedAsync(draft.ExternalDocumentId ?? "", ct);
        if (signedPdf is not { Length: > 0 })
        {
            logger.LogWarning("Minuta {Key} continua sem PDF assinado disponível no provedor", draft.Key);
            return false;
        }

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        try
        {
            // Mesma corrida do fluxo principal: outra chamada pode ter conseguido o PDF enquanto
            // este download estava em voo. AsNoTracking pelo mesmo motivo — ver ExecuteAsync.
            var current = await context.ContractDrafts.AsNoTracking()
                .Where(d => d.Key == draft.Key)
                .Select(d => new { d.SignedAttachmentKey })
                .FirstOrDefaultAsync(ct);

            if (current is null || current.SignedAttachmentKey is not null)
            {
                await transaction.RollbackAsync(ct);
                return false;
            }

            var description = ContractChangeLogFields.DescribeDraft(draft.Sequence, "assinada");
            draft.SignedAttachmentKey = await AttachAsync(draft, signedPdf, description, userName);
            draft.LastError = null;
            draft.UpdatedAt = DateTime.Now;
            draft.UpdatedBy = userName;

            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync(ct);
            logger.LogError(e, "Falha ao anexar o PDF assinado (nova tentativa) na minuta {Key}", draft.Key);
            throw;
        }
    }

    /// <summary>Correlaciona por e-mail. Devolve true se algum signatário mudou.</summary>
    private static bool ApplySigners(ContractDraft draft, ESignatureDocumentState state)
    {
        var changed = false;

        foreach (var incoming in state.Signers)
        {
            var signer = draft.Signers.FirstOrDefault(s =>
                string.Equals(s.Email, incoming.Email, StringComparison.OrdinalIgnoreCase));
            if (signer is null) continue;

            var status = incoming.Signed ? SignerStatus.Signed : signer.Status;
            if (signer.Status != status)
            {
                signer.Status = status;
                signer.SignedAt = incoming.SignedAt ?? (incoming.Signed ? DateTime.Now : null);
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(incoming.Message) && signer.LastMessage != incoming.Message)
            {
                signer.LastMessage = incoming.Message;
                changed = true;
            }
        }

        return changed;
    }

    private static ContractDraftStatus NextStatus(ContractDraft draft, ESignatureDocumentState state)
    {
        if (state.Status == ESignatureDocumentStatus.Canceled) return ContractDraftStatus.Canceled;
        if (state.Status == ESignatureDocumentStatus.Finished) return ContractDraftStatus.Signed;

        var any = draft.Signers.Any(s => s.Status == SignerStatus.Signed);
        var all = draft.Signers.Count > 0 && draft.Signers.All(s => s.Status == SignerStatus.Signed);

        if (all) return ContractDraftStatus.Signed;
        return any ? ContractDraftStatus.PartiallySigned : draft.Status;
    }

    /// <summary>
    /// Anexa ao contrato. Atenção à assimetria real dos dois serviços: o de compra NÃO recebe
    /// userName (preenche-se CreatedBy no objeto), o de venda recebe. A Key dos dois anexos é
    /// gerada pelo banco (DatabaseGeneratedOption.Identity) — não se atribui aqui, como em todo
    /// o resto da casa (ver *AttachmentUploadController).
    /// </summary>
    private async Task<Guid> AttachAsync(ContractDraft draft, byte[] pdf, string description, string userName)
    {
        var fileName = $"{draft.ContractCode}-minuta-{draft.Sequence}-assinada.pdf";

        if (draft.PurchaseContractKey is { } pk)
        {
            var attachment = new PurchaseContractAttachment
            {
                PurchaseContractKey = pk, Description = description, FileName = fileName,
                ContentType = "application/pdf", FileData = pdf, CreatedAt = DateTime.Now, CreatedBy = userName,
            };
            await purchaseAttachments.SaveAsync(pk, attachment);
            return attachment.Key!.Value;
        }

        var sk = draft.SalesContractKey!.Value;
        var salesAttachment = new SalesContractAttachment
        {
            SalesContractKey = sk, Description = description, FileName = fileName,
            ContentType = "application/pdf", FileData = pdf, CreatedAt = DateTime.Now, CreatedBy = userName,
        };
        await salesAttachments.SaveAsync(sk, salesAttachment, userName);
        return salesAttachment.Key!.Value;
    }
}
