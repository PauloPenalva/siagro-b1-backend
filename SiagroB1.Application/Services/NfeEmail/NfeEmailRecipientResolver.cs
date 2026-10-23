using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.NfeEmail;

public interface INfeEmailRecipientResolver
{
    /// <summary>E-mails de NF-e do parceiro na ordem cadastrada (vazio se não houver).</summary>
    Task<IReadOnlyList<string>> ResolveAsync(string cardCode);

    /// <summary>E-mails de NF-e de vários parceiros, na ordem dos códigos, sem repetidos (sem diferenciar caixa).</summary>
    Task<IReadOnlyList<string>> ResolveManyAsync(IReadOnlyList<string> cardCodes);
}

public class NfeEmailRecipientResolver(IUnitOfWork db) : INfeEmailRecipientResolver
{
    public async Task<IReadOnlyList<string>> ResolveAsync(string cardCode) =>
        await db.Context.BusinessPartnerNfeEmails.AsNoTracking()
            .Where(e => e.CardCode == cardCode)
            .OrderBy(e => e.LineNum)
            .Select(e => e.Email)
            .ToListAsync();

    public async Task<IReadOnlyList<string>> ResolveManyAsync(IReadOnlyList<string> cardCodes)
    {
        var codes = cardCodes.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var code in codes)
            foreach (var email in await ResolveAsync(code))
                if (seen.Add(email.Trim()))
                    result.Add(email.Trim());

        return result;
    }
}
