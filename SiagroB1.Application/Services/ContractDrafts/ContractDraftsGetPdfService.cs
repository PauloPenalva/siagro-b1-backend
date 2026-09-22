using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>
/// PDF da minuta em qualquer status. Assinada com anexo arquivado ⇒ devolve o PDF ASSINADO do
/// anexo (tem as assinaturas e o certificado do provedor); senão renderiza o BodyHtml na hora.
/// </summary>
public class ContractDraftsGetPdfService(
    AppDbContext context,
    ContractDraftsLoader loader,
    IHtmlToPdfRenderer pdf,
    PurchaseContractsAttachmentsGetService purchaseAttachments,
    SalesContractsAttachmentsGetService salesAttachments)
{
    public async Task<(byte[] Bytes, string FileName)> ExecuteAsync(Guid key, CancellationToken ct)
    {
        var draft = await loader.RequireDraftAsync(key, ct);

        if (draft.Status == ContractDraftStatus.Signed && draft.SignedAttachmentKey is { } attachmentKey)
        {
            if (draft.PurchaseContractKey.HasValue)
            {
                var a = await purchaseAttachments.GetByKey(attachmentKey);
                if (a is not null) return (a.FileData, a.FileName);
            }
            else
            {
                var a = await salesAttachments.GetByKey(attachmentKey);
                if (a is not null) return (a.FileData, a.FileName);
            }
        }

        var title = draft.Template?.Title ?? context.ContractTemplates
            .Where(t => t.Key == draft.TemplateKey).Select(t => t.Title).FirstOrDefault() ?? "Minuta";
        var bytes = await pdf.RenderAsync(ContractDraftPdfLayout.Wrap(draft.BodyHtml, title), ct);

        return (bytes, $"{draft.ContractCode}-minuta-{draft.Sequence}.pdf");
    }
}
