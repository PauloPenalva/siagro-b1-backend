using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Valor declarado (<see cref="PurchaseInvoice.TotalDocumentValue"/>) do documento de entrada de terceiro Normal na
/// filial que emite NF-e pelo Siagro: como na emissão própria, quem grava é o sistema — a soma das linhas, a cada
/// gravação do documento ou de uma linha (pedido do usuário em 05/10/2026). O valor vindo da tela não vale. Quem
/// pergunta pela regra ativa é o chamador; devolução do cliente e filial sem a regra ficam com o valor digitado.
/// </summary>
public static class PurchaseInvoiceDeclaredTotal
{
    public static bool AppliesTo(PurchaseInvoice invoice) =>
        invoice.IssuerType == DocumentIssuerType.ThirdParty && invoice.InvoiceType == PurchaseInvoiceType.Normal;

    /// <summary>Documento inteiro em memória (inclusão e alteração do cabeçalho com as linhas).</summary>
    public static void Apply(PurchaseInvoice invoice, IEnumerable<PurchaseInvoiceItem> lines) =>
        invoice.TotalDocumentValue = lines.Sum(l => l.Total);

    /// <summary>
    /// Gravação de UMA linha: soma as demais linhas do banco com a linha incluída ou alterada (<paramref name="line"/>
    /// nula = linha excluída). <paramref name="invoice"/> precisa estar rastreado; quem salva é o chamador.
    /// </summary>
    public static async Task ApplyAsync(IUnitOfWork db, PurchaseInvoice invoice, Guid? lineKey, PurchaseInvoiceItem? line)
    {
        var others = await db.Context.PurchaseInvoicesItems.AsNoTracking()
            .Where(i => i.PurchaseInvoiceKey == invoice.Key && i.Key != lineKey)
            .ToListAsync();

        invoice.TotalDocumentValue = others.Sum(i => i.Total) + (line?.Total ?? 0m);
    }
}
