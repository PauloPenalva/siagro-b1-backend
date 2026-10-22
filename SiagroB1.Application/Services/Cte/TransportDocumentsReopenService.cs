using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Cte;

/// <summary>Reabre o CT-e confirmado para edição (Confirmado → Rascunho). Mantém número, cCT e chave.</summary>
public class TransportDocumentsReopenService(IUnitOfWork db, IConfiguration configuration)
{
    public async Task ReopenAsync(Guid key)
    {
        TransportDocumentsSaveService.EnsureStandalone(configuration);

        var doc = await db.Context.TransportDocuments.FirstOrDefaultAsync(x => x.Key == key)
                  ?? throw new DefaultException("CT-e não encontrado.");
        if (doc.Status != CteStatus.Confirmed)
            throw new DefaultException("Só é possível reabrir CT-e confirmado.");

        doc.Status = CteStatus.Draft;
        doc.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
    }
}
