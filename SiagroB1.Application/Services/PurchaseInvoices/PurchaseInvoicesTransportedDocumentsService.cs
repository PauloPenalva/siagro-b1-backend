using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Ajuste manual dos vínculos do CT-e com as NF-e transportadas: incluir uma chave, remover, e refazer a busca
/// (a NF pode ter sido escriturada depois do CT-e). Só com o CT-e Pendente; o vínculo é informativo.
/// </summary>
public class PurchaseInvoicesTransportedDocumentsService(IUnitOfWork db)
{
    public async Task<PurchaseInvoiceTransportedDocument> AddAsync(Guid cteKey, string? accessKey)
    {
        var cte = await LoadPendingCteAsync(cteKey);

        var key = (accessKey ?? string.Empty).Trim();
        if (key.Length != 44 || !key.All(char.IsAsciiDigit))
            throw new DefaultException("A chave de acesso deve ter 44 dígitos.");

        if (await db.Context.PurchaseInvoiceTransportedDocuments.AnyAsync(x => x.PurchaseInvoiceKey == cte.Key && x.AccessKey == key))
            throw new DefaultException("Esta chave já está vinculada ao CT-e.");

        var doc = new PurchaseInvoiceTransportedDocument { Key = Guid.NewGuid(), PurchaseInvoiceKey = cte.Key, AccessKey = key };
        await PurchaseInvoiceTransportedDocuments.ResolveAsync(db.Context, doc, cte.Key);

        await db.Context.PurchaseInvoiceTransportedDocuments.AddAsync(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    public async Task RemoveAsync(Guid documentKey)
    {
        var doc = await db.Context.PurchaseInvoiceTransportedDocuments.FirstOrDefaultAsync(x => x.Key == documentKey)
                  ?? throw new NotFoundException("Documento transportado não encontrado.");
        await LoadPendingCteAsync(doc.PurchaseInvoiceKey);

        db.Context.PurchaseInvoiceTransportedDocuments.Remove(doc);
        await db.SaveChangesAsync();
    }

    public async Task RelinkAsync(Guid cteKey)
    {
        var cte = await LoadPendingCteAsync(cteKey);

        var docs = await db.Context.PurchaseInvoiceTransportedDocuments.Where(x => x.PurchaseInvoiceKey == cte.Key).ToListAsync();
        foreach (var doc in docs)
            await PurchaseInvoiceTransportedDocuments.ResolveAsync(db.Context, doc, cte.Key);

        await db.SaveChangesAsync();
    }

    private async Task<PurchaseInvoice> LoadPendingCteAsync(Guid cteKey)
    {
        var cte = await db.Context.PurchaseInvoices.FirstOrDefaultAsync(x => x.Key == cteKey)
                  ?? throw new NotFoundException("Documento de entrada não encontrado.");

        if (!PurchaseInvoiceCteRules.IsCte(cte))
            throw new DefaultException("O documento não é um CT-e.");

        if (cte.InvoiceStatus != InvoiceStatus.Pending)
            throw new DefaultException(PurchaseInvoiceTransportedDocuments.NotPendingMessage);

        return cte;
    }
}
