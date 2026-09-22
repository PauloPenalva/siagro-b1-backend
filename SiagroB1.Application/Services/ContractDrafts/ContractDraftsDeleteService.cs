using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ContractDrafts;

public class ContractDraftsDeleteService(AppDbContext context, ContractDraftsLoader loader)
{
    public async Task ExecuteAsync(Guid key)
    {
        var draft = await loader.RequireDraftAsync(key);
        ContractDraftsLoader.RequireEditable(draft);

        context.ContractDraftSigners.RemoveRange(draft.Signers);
        context.ContractDrafts.Remove(draft);
        await context.SaveChangesAsync();
    }
}
