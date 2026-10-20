using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Vínculo entre o Documento de Entrada e o DF-e recebido (manifestação do destinatário). O DF-e guarda o
/// <c>RowId</c> do documento (sem FK), então quem apaga ou cancela o documento precisa soltar o vínculo — senão o
/// "Dar entrada" do DF-e fica bloqueado para sempre.
/// </summary>
public static class PurchaseInvoiceReceivedDfeLink
{
    /// <summary>Marca o DF-e da mesma filial e chave como já lançado. Chamar DEPOIS do SaveChanges (o RowId nasce no banco); não salva.</summary>
    public static async Task LinkAsync(AppDbContext context, PurchaseInvoice invoice)
    {
        if (string.IsNullOrWhiteSpace(invoice.ChaveNFe) || string.IsNullOrWhiteSpace(invoice.BranchCode))
            return;

        var dfe = await context.ReceivedDfes.FirstOrDefaultAsync(x =>
            x.BranchCode == invoice.BranchCode && x.AccessKey == invoice.ChaveNFe);

        if (dfe is not null)
            dfe.PurchaseInvoiceDocEntry = invoice.RowId;
    }

    /// <summary>Solta o vínculo dos DF-e que apontam para o documento. Não salva: segue no SaveChanges de quem chama.</summary>
    public static async Task ClearAsync(AppDbContext context, PurchaseInvoice invoice)
    {
        if (invoice.RowId <= 0 || string.IsNullOrWhiteSpace(invoice.BranchCode))
            return;

        var links = await context.ReceivedDfes
            .Where(x => x.BranchCode == invoice.BranchCode && x.PurchaseInvoiceDocEntry == invoice.RowId)
            .ToListAsync();

        foreach (var dfe in links)
            dfe.PurchaseInvoiceDocEntry = null;
    }
}
