using Microsoft.EntityFrameworkCore.ChangeTracking;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Travas do documento de saída com NF-e STANDALONE (spec §9.4 + decisão P11). Todas agem pelo
/// <see cref="SalesInvoice.NfeStatus"/>, que fica <c>None</c> para sempre na Yokotobi (SAPB1) e na
/// MH Agro (chave desligada) — por isso nenhuma delas muda o comportamento dessas bases.
/// </summary>
public static class SalesInvoiceNfeLock
{
    private const string ProcessingMessage =
        "A NF-e deste documento está em processamento na SEFAZ: aguarde e use Consultar situação.";

    private const string AuthorizedMessage =
        "A NF-e deste documento já foi autorizada: os dados que foram para a nota não podem mudar.";

    /// <summary>Campos do cabeçalho que vão para o XML.</summary>
    private static readonly string[] HeaderFiscalFields =
    [
        nameof(SalesInvoice.BranchCode), nameof(SalesInvoice.CardCode), nameof(SalesInvoice.InvoiceDate),
        nameof(SalesInvoice.InvoiceType), nameof(SalesInvoice.DeliveryCardCode), nameof(SalesInvoice.TruckingCompanyCode),
        nameof(SalesInvoice.TruckCode), nameof(SalesInvoice.FreightTerms), nameof(SalesInvoice.PaymentConditionCode),
        nameof(SalesInvoice.GrossWeight), nameof(SalesInvoice.NetWeight), nameof(SalesInvoice.TaxPayerComments),
        nameof(SalesInvoice.TaxComments),
    ];

    /// <summary>Campos da linha que vão para o XML (a Conferência de entregas mexe em outros).</summary>
    private static readonly string[] ItemFiscalFields =
    [
        nameof(SalesInvoiceItem.ItemCode), nameof(SalesInvoiceItem.UnitOfMeasureCode), nameof(SalesInvoiceItem.Quantity),
        nameof(SalesInvoiceItem.UnitPrice), nameof(SalesInvoiceItem.UsageCode),
    ];

    /// <summary>Campos que só a emissão escreve.</summary>
    private static readonly string[] IssuanceFields =
    [
        nameof(SalesInvoice.NfeStatus), nameof(SalesInvoice.NfeEnvironment), nameof(SalesInvoice.NfeRandomCode),
        nameof(SalesInvoice.NfeProtocol), nameof(SalesInvoice.NfeAuthorizedAt), nameof(SalesInvoice.NfeStatusCode),
        nameof(SalesInvoice.NfeStatusReason), nameof(SalesInvoice.NfeConfirmationError),
    ];

    /// <summary>Número, série e chave: depois da primeira emissão, também só a emissão escreve.</summary>
    private static readonly string[] TaxDocumentFields =
    [
        nameof(SalesInvoice.TaxDocumentNumber), nameof(SalesInvoice.TaxDocumentSeries), nameof(SalesInvoice.ChaveNFe),
    ];

    /// <summary>Chamado DEPOIS do SetValues: compara o gravado com o que chegou.</summary>
    public static void EnsureHeaderEditable(EntityEntry<SalesInvoice> entry)
    {
        var status = (NfeStatus)entry.OriginalValues[nameof(SalesInvoice.NfeStatus)]!;

        if (status == NfeStatus.Processing)
            throw new DefaultException(ProcessingMessage);

        if (status == NfeStatus.Authorized && AnyChanged(entry, HeaderFiscalFields))
            throw new DefaultException(AuthorizedMessage);
    }

    /// <summary>O PATCH/PUT não escreve situação, protocolo, retorno — nem número/série/chave depois de emitir.</summary>
    public static void RestoreIssuanceFields(EntityEntry<SalesInvoice> entry)
    {
        // Emitido OU com número reservado (cNF gravado): o número já pertence à emissão.
        var emitted = (NfeStatus)entry.OriginalValues[nameof(SalesInvoice.NfeStatus)]! != NfeStatus.None
                      || entry.OriginalValues[nameof(SalesInvoice.NfeRandomCode)] is not null;
        var fields = emitted ? IssuanceFields.Concat(TaxDocumentFields) : IssuanceFields;

        foreach (var field in fields)
            entry.Property(field).CurrentValue = entry.OriginalValues[field];
    }

    /// <summary>A criação nunca nasce emitida, venha o que vier no corpo.</summary>
    public static void ResetIssuanceFields(SalesInvoice invoice)
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

    /// <summary>Chamado DEPOIS do SetValues da linha.</summary>
    public static void EnsureItemEditable(NfeStatus invoiceStatus, EntityEntry<SalesInvoiceItem> entry)
    {
        if (invoiceStatus == NfeStatus.Processing)
            throw new DefaultException(ProcessingMessage);

        if (invoiceStatus == NfeStatus.Authorized && AnyChanged(entry, ItemFiscalFields))
            throw new DefaultException(AuthorizedMessage);
    }

    /// <summary>Incluir ou excluir linha.</summary>
    public static void EnsureLinesChangeable(NfeStatus invoiceStatus)
    {
        if (invoiceStatus == NfeStatus.Processing)
            throw new DefaultException(ProcessingMessage);

        if (invoiceStatus == NfeStatus.Authorized)
            throw new DefaultException(AuthorizedMessage);
    }

    public static void EnsureDeletable(SalesInvoice invoice)
    {
        if (invoice.NfeStatus is NfeStatus.Processing or NfeStatus.Authorized or NfeStatus.Denied)
            throw new DefaultException(
                "Documento com NF-e em processamento, autorizada ou denegada não pode ser excluído.");
    }

    public static void EnsureCancellable(SalesInvoice invoice)
    {
        if (invoice.NfeStatus == NfeStatus.Processing)
            throw new DefaultException(ProcessingMessage);

        if (invoice.NfeStatus == NfeStatus.Authorized)
            throw new DefaultException(
                "A NF-e deste documento está autorizada: o cancelamento precisa ser feito na SEFAZ, recurso da próxima etapa.");
    }

    public static void EnsureManualTaxDocument(SalesInvoice invoice)
    {
        if (invoice.NfeStatus != NfeStatus.None || invoice.NfeRandomCode is not null)
            throw new DefaultException("Número, série e chave deste documento vêm da emissão da NF-e pelo Siagro.");
    }

    private static bool AnyChanged(EntityEntry entry, IEnumerable<string> properties) =>
        properties.Any(p => !Equals(entry.OriginalValues[p], entry.CurrentValues[p]));
}
