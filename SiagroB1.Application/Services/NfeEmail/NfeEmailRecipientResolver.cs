using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.NfeEmail;

public interface INfeEmailRecipientResolver
{
    /// <summary>E-mails de NF-e do parceiro na ordem cadastrada (vazio se não houver).</summary>
    Task<IReadOnlyList<string>> ResolveAsync(string cardCode);
}

public class NfeEmailRecipientResolver(IUnitOfWork db) : INfeEmailRecipientResolver
{
    public async Task<IReadOnlyList<string>> ResolveAsync(string cardCode) =>
        await db.Context.BusinessPartnerNfeEmails.AsNoTracking()
            .Where(e => e.CardCode == cardCode)
            .OrderBy(e => e.LineNum)
            .Select(e => e.Email)
            .ToListAsync();
}
