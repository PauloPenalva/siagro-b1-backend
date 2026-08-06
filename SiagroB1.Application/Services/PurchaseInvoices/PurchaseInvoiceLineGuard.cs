using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Regras compartilhadas pelos serviços de LINHA do documento de entrada.
///
/// Existem porque a grade da tela grava por POST/PATCH/DELETE direto em
/// <c>/PurchaseInvoicesItems</c>, sem passar pelo <c>PurchaseInvoicesUpdateService</c>. Deixar a
/// guarda só no cabeçalho abriria a porta dos fundos para alterar documento confirmado.
/// </summary>
internal static class PurchaseInvoiceLineGuard
{
    public static async Task EnsureParentIsPendingAsync(IUnitOfWork db, Guid? purchaseInvoiceKey)
    {
        if (purchaseInvoiceKey is null)
            throw new DefaultException("Linha sem documento de entrada informado.");

        var status = await db.Context.PurchaseInvoices
            .Where(x => x.Key == purchaseInvoiceKey)
            .Select(x => (InvoiceStatus?)x.InvoiceStatus)
            .FirstOrDefaultAsync();

        if (status is null)
            throw new NotFoundException("Documento de entrada não encontrado.");

        if (status != InvoiceStatus.Pending)
            throw new DefaultException(
                "Somente documento pendente pode ter as linhas alteradas. " +
                "Estorne a confirmação antes.");
    }

    /// <summary>
    /// Resolve a descrição do produto SÓ quando ela não veio.
    ///
    /// A descrição lida do XML é a que CONSTA NA NOTA e vale mais que a do cadastro; e o código do
    /// emitente pode nem existir no cadastro local, caso em que sobrescrever apagaria a descrição
    /// em vez de melhorá-la.
    /// </summary>
    public static async Task<string?> ResolveItemNameAsync(
        IItemService itemService, string? itemCode, string? itemName)
    {
        if (!string.IsNullOrWhiteSpace(itemName) || string.IsNullOrWhiteSpace(itemCode))
            return itemName;

        return (await itemService.GetByIdAsync(itemCode))?.ItemName;
    }
}
