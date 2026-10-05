using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Uma chave de NF-e, um documento registrado. O índice único no banco é a rede de segurança; esta checagem existe
/// para a mensagem sair legível em pt-BR — na inclusão e, desde a spec terceiro-chave, também na alteração.
///
/// Documento CANCELADO não segura a chave — relançar é caminho legítimo, e é por isso que o índice do banco também é
/// filtrado por status. Chave vazia ou em branco é tratada como AUSENTE.
/// </summary>
public static class PurchaseInvoiceChaveNFe
{
    public static async Task EnsureFreeAsync(IUnitOfWork db, string? chaveNFe, Guid key)
    {
        if (string.IsNullOrWhiteSpace(chaveNFe))
            return;

        var duplicated = await db.Context.PurchaseInvoices
            .AnyAsync(x => x.ChaveNFe == chaveNFe && x.InvoiceStatus != InvoiceStatus.Cancelled && x.Key != key);

        if (duplicated)
            throw new DefaultException($"Já existe documento de entrada com a chave de NF-e {chaveNFe}.");
    }
}
