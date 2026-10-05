using Microsoft.EntityFrameworkCore.ChangeTracking;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Travas da NF-e no Documento de Entrada (spec §7), espelho de <c>SalesInvoiceNfeLock</c>. Todas agem pelo
/// <see cref="PurchaseInvoice.NfeStatus"/>, que fica <c>None</c> para sempre no documento de terceiro, na
/// Yokotobi (SAPB1) e na MH Agro (chave desligada).
/// </summary>
public static class PurchaseInvoiceNfeLock
{
    private static readonly string[] HeaderFiscalFields =
    [
        nameof(PurchaseInvoice.BranchCode), nameof(PurchaseInvoice.CardCode), nameof(PurchaseInvoice.IssueDate),
        nameof(PurchaseInvoice.InvoiceType), nameof(PurchaseInvoice.IssuerType), nameof(PurchaseInvoice.TruckingCompanyCode),
        nameof(PurchaseInvoice.TruckCode), nameof(PurchaseInvoice.FreightTerms), nameof(PurchaseInvoice.PaymentConditionCode),
        nameof(PurchaseInvoice.GrossWeight), nameof(PurchaseInvoice.NetWeight), nameof(PurchaseInvoice.TaxPayerComments),
        nameof(PurchaseInvoice.VolumeQuantity), nameof(PurchaseInvoice.VolumeSpecies), nameof(PurchaseInvoice.VolumeBrand),
        nameof(PurchaseInvoice.VolumeNumbering),
    ];

    private static readonly string[] ItemFiscalFields =
    [
        nameof(PurchaseInvoiceItem.ItemCode), nameof(PurchaseInvoiceItem.UnitOfMeasureCode), nameof(PurchaseInvoiceItem.Quantity),
        nameof(PurchaseInvoiceItem.UnitPrice), nameof(PurchaseInvoiceItem.UsageCode),
    ];

    private static readonly string[] IssuanceFields =
    [
        nameof(PurchaseInvoice.NfeStatus), nameof(PurchaseInvoice.NfeEnvironment), nameof(PurchaseInvoice.NfeRandomCode),
        nameof(PurchaseInvoice.NfeProtocol), nameof(PurchaseInvoice.NfeAuthorizedAt), nameof(PurchaseInvoice.NfeStatusCode),
        nameof(PurchaseInvoice.NfeStatusReason), nameof(PurchaseInvoice.NfeConfirmationError),
    ];

    /// <summary>Número, série, chave e o vNF: depois da primeira emissão, só a emissão escreve.</summary>
    private static readonly string[] TaxDocumentFields =
    [
        nameof(PurchaseInvoice.TaxDocumentNumber), nameof(PurchaseInvoice.TaxDocumentSeries), nameof(PurchaseInvoice.ChaveNFe),
        nameof(PurchaseInvoice.TotalDocumentValue),
    ];

    /// <summary>Na devolução de compra o cabeçalho que identifica a operação não muda (spec §9.3).</summary>
    private static readonly string[] ReturnHeaderFields =
    [
        nameof(PurchaseInvoice.CardCode), nameof(PurchaseInvoice.BranchCode), nameof(PurchaseInvoice.InvoiceType),
        nameof(PurchaseInvoice.IssuerType), nameof(PurchaseInvoice.PurchaseInvoiceOriginKey),
    ];

    /// <summary>Na linha da devolução de compra só a quantidade é editável.</summary>
    private static readonly string[] ReturnLineFields =
    [
        nameof(PurchaseInvoiceItem.ItemCode), nameof(PurchaseInvoiceItem.ItemName), nameof(PurchaseInvoiceItem.UnitOfMeasureCode),
        nameof(PurchaseInvoiceItem.UnitPrice), nameof(PurchaseInvoiceItem.UsageCode),
        nameof(PurchaseInvoiceItem.PurchaseInvoiceItemOriginKey), nameof(PurchaseInvoiceItem.PurchaseContractKey),
        nameof(PurchaseInvoiceItem.SalesInvoiceItemKey),
    ];

    /// <summary>A criação nunca nasce emitida, venha o que vier no corpo.</summary>
    public static void ResetIssuanceFields(PurchaseInvoice invoice)
    {
        invoice.NfeStatus = NfeStatus.None;
        invoice.NfeEnvironment = null;
        invoice.NfeRandomCode = null;
        invoice.NfeProtocol = null;
        invoice.NfeAuthorizedAt = null;
        invoice.NfeStatusCode = null;
        invoice.NfeStatusReason = null;
        invoice.NfeConfirmationError = null;
    }

    /// <summary>O PATCH/PUT não escreve situação, protocolo, retorno — nem número/série/chave depois de emitir.</summary>
    public static void RestoreIssuanceFields(EntityEntry<PurchaseInvoice> entry)
    {
        // Emitido OU com número reservado (cNF gravado): o número já pertence à emissão.
        var emitted = (NfeStatus)entry.OriginalValues[nameof(PurchaseInvoice.NfeStatus)]! != NfeStatus.None
                      || entry.OriginalValues[nameof(PurchaseInvoice.NfeRandomCode)] is not null;

        NfeLockRules.Restore(entry, emitted ? IssuanceFields.Concat(TaxDocumentFields) : IssuanceFields);

        // Com número reservado a filial, o emitente e o tipo ficam como estão (spec D10): o número da filial A
        // não pode sair pela B nem ficar encalhado pela troca para terceiro. Sobrescreve, não recusa.
        if (emitted)
            NfeLockRules.Restore(entry,
                [nameof(PurchaseInvoice.BranchCode), nameof(PurchaseInvoice.IssuerType), nameof(PurchaseInvoice.InvoiceType)]);

        // A marca da devolução própria nasce no Devolver e nunca muda pela API.
        NfeLockRules.Restore(entry, [nameof(PurchaseInvoice.IsNfeReturn)]);
    }

    /// <summary>Chamado DEPOIS das atribuições do Update: compara o gravado com o que chegou.</summary>
    public static void EnsureHeaderEditable(EntityEntry<PurchaseInvoice> entry)
    {
        var status = (NfeStatus)entry.OriginalValues[nameof(PurchaseInvoice.NfeStatus)]!;

        if (status == NfeStatus.Processing)
            throw new DefaultException(NfeLockRules.ProcessingMessage);

        if (status == NfeStatus.Authorized && NfeLockRules.AnyChanged(entry, HeaderFiscalFields))
            throw new DefaultException(NfeLockRules.AuthorizedMessage);
    }

    public static void RestoreReturnHeader(EntityEntry<PurchaseInvoice> entry)
    {
        if ((bool)entry.OriginalValues[nameof(PurchaseInvoice.IsNfeReturn)]!)
            NfeLockRules.Restore(entry, ReturnHeaderFields);
    }

    /// <summary>Chamado DEPOIS das atribuições da linha.</summary>
    public static void EnsureItemEditable(NfeStatus invoiceStatus, EntityEntry<PurchaseInvoiceItem> entry)
    {
        if (invoiceStatus == NfeStatus.Processing)
            throw new DefaultException(NfeLockRules.ProcessingMessage);

        if (invoiceStatus == NfeStatus.Authorized && NfeLockRules.AnyChanged(entry, ItemFiscalFields))
            throw new DefaultException(NfeLockRules.AuthorizedMessage);
    }

    public static void RestoreReturnLine(EntityEntry<PurchaseInvoiceItem> entry) =>
        NfeLockRules.Restore(entry, ReturnLineFields);

    /// <summary>Incluir ou excluir linha.</summary>
    public static void EnsureLinesChangeable(NfeStatus invoiceStatus)
    {
        if (invoiceStatus == NfeStatus.Processing)
            throw new DefaultException(NfeLockRules.ProcessingMessage);

        if (invoiceStatus == NfeStatus.Authorized)
            throw new DefaultException(NfeLockRules.AuthorizedMessage);
    }

    public static void EnsureLineCanBeAdded(PurchaseInvoice invoice)
    {
        EnsureLinesChangeable(invoice.NfeStatus);

        if (invoice.IsNfeReturn)
            throw new DefaultException(
                "A devolução de compra só tem os itens que vieram da entrada: não é possível incluir item.");
    }

    public static void EnsureDeletable(PurchaseInvoice invoice)
    {
        if (invoice.NfeStatus is NfeStatus.Processing or NfeStatus.Authorized or NfeStatus.Denied)
            throw new DefaultException(
                "Documento com NF-e em processamento, autorizada ou denegada não pode ser excluído.");
    }

    public static void EnsureCancellable(PurchaseInvoice invoice)
    {
        if (invoice.NfeStatus == NfeStatus.Processing)
            throw new DefaultException(NfeLockRules.ProcessingMessage);

        if (invoice.NfeStatus == NfeStatus.Authorized)
            throw new DefaultException(
                "A NF-e deste documento está autorizada: o cancelamento precisa ser feito na SEFAZ, recurso da próxima etapa.");
    }
}
