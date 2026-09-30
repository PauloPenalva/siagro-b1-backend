using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>Edição manual do texto renderizado, só em rascunho. Não re-resolve placeholders: o snapshot é do usuário agora.</summary>
public class ContractDraftsUpdateService(AppDbContext context, ContractDraftsLoader loader)
{
    /// <param name="draftType">
    /// Nulo = o chamador não mandou tipo, e o tipo atual é preservado — mesma regra de
    /// <paramref name="description"/>. Um Aditivo não pode virar Contrato só porque quem salvou
    /// o texto não repetiu o tipo: o documento seria assinado e arquivado como outra coisa, sem
    /// aviso e sem volta.
    /// </param>
    public async Task ExecuteAsync(Guid key, string? description, ContractDraftType? draftType, string bodyHtml, string userName)
    {
        var draft = await loader.RequireDraftAsync(key);
        ContractDraftsLoader.RequireEditable(draft);

        if (string.IsNullOrWhiteSpace(bodyHtml))
            throw new BusinessException("O texto da minuta não pode ficar vazio.");

        draft.Description = string.IsNullOrWhiteSpace(description) ? draft.Description : description.Trim();
        draft.DraftType = draftType ?? draft.DraftType;
        draft.BodyHtml = bodyHtml;
        draft.UpdatedAt = DateTime.Now;
        draft.UpdatedBy = userName;

        await context.SaveChangesAsync();
    }
}
