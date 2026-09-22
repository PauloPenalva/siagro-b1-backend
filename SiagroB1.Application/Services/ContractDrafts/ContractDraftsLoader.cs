using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>Carregamentos e guardas repetidos pelos serviços da minuta.</summary>
public class ContractDraftsLoader(AppDbContext context)
{
    public async Task<ContractDraft> RequireDraftAsync(Guid key, CancellationToken ct = default) =>
        await context.ContractDrafts.Include(d => d.Signers).FirstOrDefaultAsync(d => d.Key == key, ct)
        ?? throw new NotFoundException("Minuta não encontrada.");

    /// <summary>Só rascunho aceita edição e exclusão — depois do envio o texto é o que foi assinado.</summary>
    public static void RequireEditable(ContractDraft draft)
    {
        if (draft.Status != ContractDraftStatus.Draft)
            throw new BusinessException("Minuta já enviada para assinatura não pode ser alterada.");
    }
}
