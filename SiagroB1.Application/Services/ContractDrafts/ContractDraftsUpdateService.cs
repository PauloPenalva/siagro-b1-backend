using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>Edição manual do texto renderizado, só em rascunho. Não re-resolve placeholders: o snapshot é do usuário agora.</summary>
public class ContractDraftsUpdateService(AppDbContext context, ContractDraftsLoader loader)
{
    public async Task ExecuteAsync(Guid key, string description, ContractDraftType draftType, string bodyHtml, string userName)
    {
        var draft = await loader.RequireDraftAsync(key);
        ContractDraftsLoader.RequireEditable(draft);

        if (string.IsNullOrWhiteSpace(bodyHtml))
            throw new BusinessException("O texto da minuta não pode ficar vazio.");

        draft.Description = string.IsNullOrWhiteSpace(description) ? draft.Description : description.Trim();
        draft.DraftType = draftType;
        draft.BodyHtml = bodyHtml;
        draft.UpdatedAt = DateTime.Now;
        draft.UpdatedBy = userName;

        await context.SaveChangesAsync();
    }
}
