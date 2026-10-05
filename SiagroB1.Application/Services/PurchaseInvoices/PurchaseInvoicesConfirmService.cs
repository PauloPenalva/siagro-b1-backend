using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Confirma o documento de entrada: fecha para edição e o torna definitivo para a conciliação.
///
/// Nesta fase a confirmação SÓ transiciona o status — o gate de edição já é o valor: a devolução
/// antiga não tinha nenhum, e qualquer documento era alterável para sempre.
///
/// A Fase 3 pendura aqui o efeito da natureza de operação sobre o contrato de compra, sem mexer
/// nesta máquina de estados.
/// </summary>
public class PurchaseInvoicesConfirmService(IUnitOfWork db, TaxCalculationGate gate)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var invoice = await db.Context.PurchaseInvoices
                          .FirstOrDefaultAsync(x => x.Key == key)
                      ?? throw new NotFoundException("Documento de entrada não encontrado.");

        if (invoice.InvoiceStatus != InvoiceStatus.Pending)
            throw new DefaultException("Somente documento pendente pode ser confirmado.");

        // Emitir é o que confirma (spec D3): na filial com a regra ativa, o documento de emissão própria só
        // confirma com a NF-e autorizada — é o que a emissão chama. Documento de terceiro confirma como hoje.
        if (invoice.IssuerType == DocumentIssuerType.Own && invoice.NfeStatus != NfeStatus.Authorized &&
            await gate.IsActiveAsync(invoice.BranchCode))
            throw new DefaultException("Na filial que emite NF-e pelo Siagro, confirme emitindo a NF-e.");

        invoice.InvoiceStatus = InvoiceStatus.Confirmed;
        invoice.ApprovedAt = DateTime.Now;
        invoice.ApprovedBy = userName;

        await db.SaveChangesAsync();
    }
}
