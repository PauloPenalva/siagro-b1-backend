using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>
/// Cancela a minuta no provedor e aqui. NÃO mexe no <c>SignatureStatus</c> do contrato: o
/// usuário pode estar cancelando a via eletrônica justamente porque assinou em papel.
/// </summary>
public class ContractDraftsCancelService(
    AppDbContext context,
    ContractDraftsLoader loader,
    IESignatureProvider provider,
    PurchaseContractsChangeLogService purchaseLog,
    SalesContractsChangeLogService salesLog,
    ILogger<ContractDraftsCancelService> logger)
{
    public const string NotSentMessage = "Só é possível cancelar minuta enviada para assinatura.";

    public async Task ExecuteAsync(Guid key, string userName, CancellationToken ct = default)
    {
        var draft = await loader.RequireDraftAsync(key, ct);

        if (draft.Status is not (ContractDraftStatus.AwaitingSignature or ContractDraftStatus.PartiallySigned))
            throw new BusinessException(NotSentMessage);

        var result = await provider.CancelAsync(draft.ExternalDocumentId ?? "", ct);
        if (!result.Succeeded)
        {
            logger.LogWarning("Provedor recusou cancelar a minuta {Key}", key);
            throw new BusinessException(result.ErrorMessage ?? "Não foi possível cancelar a minuta no provedor.");
        }

        draft.Status = ContractDraftStatus.Canceled;
        draft.CanceledAt = DateTime.Now;
        draft.CanceledBy = userName;
        draft.UpdatedAt = DateTime.Now;
        draft.UpdatedBy = userName;

        var what = ContractChangeLogFields.DescribeDraft(draft.Sequence, "cancelada");
        if (draft.PurchaseContractKey is { } pk) purchaseLog.Register(pk, ContractChangeLogFields.Draft, null, what, userName);
        if (draft.SalesContractKey is { } sk) salesLog.Register(sk, ContractChangeLogFields.Draft, null, what, userName);

        await context.SaveChangesAsync(ct);
    }
}
