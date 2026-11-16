using Microsoft.EntityFrameworkCore.ChangeTracking;
using SiagroB1.Application.Services.Nfe;
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
    /// <summary>Campos do cabeçalho que vão para o XML.</summary>
    private static readonly string[] HeaderFiscalFields =
    [
        nameof(SalesInvoice.BranchCode), nameof(SalesInvoice.CardCode), nameof(SalesInvoice.InvoiceDate),
        nameof(SalesInvoice.InvoiceType), nameof(SalesInvoice.DeliveryCardCode), nameof(SalesInvoice.TruckingCompanyCode),
        nameof(SalesInvoice.TruckCode), nameof(SalesInvoice.FreightTerms), nameof(SalesInvoice.PaymentConditionCode),
        nameof(SalesInvoice.GrossWeight), nameof(SalesInvoice.NetWeight), nameof(SalesInvoice.TaxPayerComments),
        nameof(SalesInvoice.TaxComments), nameof(SalesInvoice.VolumeQuantity), nameof(SalesInvoice.VolumeSpecies),
        nameof(SalesInvoice.VolumeBrand), nameof(SalesInvoice.VolumeNumbering), nameof(SalesInvoice.TaxDocumentKind),
    ];

    /// <summary>Campos da linha que vão para o XML (a Conferência de entregas mexe em outros).</summary>
    private static readonly string[] ItemFiscalFields =
    [
        nameof(SalesInvoiceItem.ItemCode), nameof(SalesInvoiceItem.UnitOfMeasureCode), nameof(SalesInvoiceItem.Quantity),
        nameof(SalesInvoiceItem.UnitPrice), nameof(SalesInvoiceItem.UsageCode), nameof(SalesInvoiceItem.FreightValue),
        nameof(SalesInvoiceItem.InsuranceValue), nameof(SalesInvoiceItem.DiscountValue), nameof(SalesInvoiceItem.OtherExpensesValue),
        nameof(SalesInvoiceItem.CustomerOrderNumber), nameof(SalesInvoiceItem.CustomerOrderItem),
    ];

    /// <summary>Campos que só a emissão escreve.</summary>
    private static readonly string[] IssuanceFields =
    [
        nameof(SalesInvoice.NfeStatus), nameof(SalesInvoice.NfeEnvironment), nameof(SalesInvoice.NfeRandomCode),
        nameof(SalesInvoice.NfeProtocol), nameof(SalesInvoice.NfeAuthorizedAt), nameof(SalesInvoice.NfeStatusCode),
        nameof(SalesInvoice.NfeStatusReason), nameof(SalesInvoice.NfeConfirmationError),
        nameof(SalesInvoice.NfeCancellationProtocol), nameof(SalesInvoice.NfeCancelledAt),
        nameof(SalesInvoice.NfeCancellationReason), nameof(SalesInvoice.NfeCancellationError),
    ];

    /// <summary>Número, série e chave: depois da primeira emissão, também só a emissão escreve.</summary>
    private static readonly string[] TaxDocumentFields =
    [
        nameof(SalesInvoice.TaxDocumentNumber), nameof(SalesInvoice.TaxDocumentSeries), nameof(SalesInvoice.ChaveNFe),
    ];

    /// <summary>Campos do vínculo com a invoice do SAP: do servidor, nunca do corpo (GAC-1226).</summary>
    private static readonly string[] SapLinkFields =
    [
        nameof(SalesInvoice.SapDocEntry), nameof(SalesInvoice.SapDocNum), nameof(SalesInvoice.SapCancelPending),
        nameof(SalesInvoice.SapCancelPendingReason), nameof(SalesInvoice.SapCancelDetectedAt),
    ];

    /// <summary>Campos comerciais da linha que vêm da NF do SAP (GAC-1226): a conferência de entrega não os toca.</summary>
    private static readonly string[] SapLinkedItemFields =
    [
        nameof(SalesInvoiceItem.ItemCode), nameof(SalesInvoiceItem.Quantity), nameof(SalesInvoiceItem.UnitPrice),
    ];

    public const string SapLinkedTaxDocumentMessage = "Número, série e chave deste documento vêm da nota fiscal do SAP.";

    public const string SapLinkedLinesMessage =
        "Documento vinculado à nota fiscal do SAP: quantidade, preço e item não podem ser alterados.";

    public const string ConfirmedMessage = "Documento confirmado: estorne a confirmação para alterar.";

    /// <summary>
    /// Confirmar e depois transmitir (spec 2026-10-06): com a regra da NF-e ativa, o documento Normal GRAVADO como
    /// Confirmado já baixou o contrato e é dele que a NF-e sai — até a transmissão, o que vai para o XML só muda
    /// estornando a confirmação. A devolução própria fica Pendente até autorizar, então nunca cai aqui.
    /// <paramref name="gateActive"/> é a regra da filial gravada: false na Yokotobi (SAPB1) e na MH Agro, que seguem
    /// editando o confirmado como antes.
    /// </summary>
    public static bool IsConfirmedFrozen(InvoiceStatus? storedStatus, SalesInvoiceType storedType, bool gateActive) =>
        gateActive && storedStatus == InvoiceStatus.Confirmed && storedType == SalesInvoiceType.Normal;

    /// <summary>Chamado DEPOIS do SetValues: compara o gravado com o que chegou.</summary>
    /// <param name="confirmedFrozen">Ver <see cref="IsConfirmedFrozen"/>.</param>
    public static void EnsureHeaderEditable(EntityEntry<SalesInvoice> entry, bool confirmedFrozen = false)
    {
        var status = (NfeStatus)entry.OriginalValues[nameof(SalesInvoice.NfeStatus)]!;

        if (status == NfeStatus.Processing)
            throw new DefaultException(NfeLockRules.ProcessingMessage);

        if (NfeLockRules.IsFrozen(status) && NfeLockRules.AnyChanged(entry, HeaderFiscalFields))
            throw new DefaultException(NfeLockRules.FrozenMessage(status));

        // Número já reservado pela emissão (ex.: NF-e rejeitada): o documento é NF-e. Virar "Outro" deixaria o número
        // da série sem nota.
        if (entry.OriginalValues[nameof(SalesInvoice.NfeRandomCode)] is not null
            && NfeLockRules.AnyChanged(entry, [nameof(SalesInvoice.TaxDocumentKind)]))
            throw new DefaultException("Este documento já tem número de NF-e reservado: o tipo de documento não pode mudar.");

        if (confirmedFrozen && NfeLockRules.AnyChanged(entry, HeaderFiscalFields))
            throw new DefaultException(ConfirmedMessage);
    }

    /// <summary>O PATCH/PUT não escreve situação, protocolo, retorno — nem número/série/chave depois de emitir.</summary>
    public static void RestoreIssuanceFields(EntityEntry<SalesInvoice> entry)
    {
        // Emitido OU com número reservado (cNF gravado): o número já pertence à emissão.
        var emitted = (NfeStatus)entry.OriginalValues[nameof(SalesInvoice.NfeStatus)]! != NfeStatus.None
                      || entry.OriginalValues[nameof(SalesInvoice.NfeRandomCode)] is not null;
        var fields = emitted ? IssuanceFields.Concat(TaxDocumentFields) : IssuanceFields;

        NfeLockRules.Restore(entry, fields);

        // A marca da devolução própria nasce no Devolver e nunca muda pela API.
        entry.Property(nameof(SalesInvoice.IsNfeReturn)).CurrentValue = entry.OriginalValues[nameof(SalesInvoice.IsNfeReturn)];

        // O vínculo com a recusa de carga nasce na recusa e nunca muda pela API.
        entry.Property(nameof(SalesInvoice.ShipmentLoadRefusalKey)).CurrentValue =
            entry.OriginalValues[nameof(SalesInvoice.ShipmentLoadRefusalKey)];
        entry.Property(nameof(SalesInvoice.RefusalStorageTransactionKey)).CurrentValue =
            entry.OriginalValues[nameof(SalesInvoice.RefusalStorageTransactionKey)];

        // O vínculo com a invoice do SAP (GAC-1226) só muda pela ação de vincular/desvincular.
        foreach (var field in SapLinkFields)
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
        invoice.NfeCancellationProtocol = null;
        invoice.NfeCancelledAt = null;
        invoice.NfeCancellationReason = null;
        invoice.NfeCancellationError = null;
        invoice.ShipmentLoadRefusalKey = null;
        invoice.RefusalStorageTransactionKey = null;
        invoice.SapDocEntry = null;
        invoice.SapDocNum = null;
        invoice.SapCancelPending = false;
        invoice.SapCancelPendingReason = null;
        invoice.SapCancelDetectedAt = null;
    }

    /// <summary>Chamado DEPOIS do SetValues da linha. A Conferência de entregas mexe em campos fora do XML e passa.</summary>
    /// <param name="confirmedFrozen">Ver <see cref="IsConfirmedFrozen"/>.</param>
    /// <param name="sapLinked">Documento vinculado a uma NF do SAP: item, quantidade e preço ficam travados.</param>
    public static void EnsureItemEditable(NfeStatus invoiceStatus, EntityEntry<SalesInvoiceItem> entry,
        bool confirmedFrozen = false, bool sapLinked = false)
    {
        if (sapLinked && NfeLockRules.AnyChanged(entry, SapLinkedItemFields))
            throw new DefaultException(SapLinkedLinesMessage);

        if (invoiceStatus == NfeStatus.Processing)
            throw new DefaultException(NfeLockRules.ProcessingMessage);

        if (NfeLockRules.IsFrozen(invoiceStatus) && NfeLockRules.AnyChanged(entry, ItemFiscalFields))
            throw new DefaultException(NfeLockRules.FrozenMessage(invoiceStatus));

        if (confirmedFrozen && NfeLockRules.AnyChanged(entry, ItemFiscalFields))
            throw new DefaultException(ConfirmedMessage);
    }

    /// <summary>Incluir ou excluir linha.</summary>
    /// <param name="confirmedFrozen">Ver <see cref="IsConfirmedFrozen"/>.</param>
    /// <param name="sapLinked">Documento vinculado a uma NF do SAP: as linhas vêm da nota e não mudam.</param>
    public static void EnsureLinesChangeable(NfeStatus invoiceStatus, bool confirmedFrozen = false, bool sapLinked = false)
    {
        if (sapLinked)
            throw new DefaultException(SapLinkedLinesMessage);

        if (invoiceStatus == NfeStatus.Processing)
            throw new DefaultException(NfeLockRules.ProcessingMessage);

        if (NfeLockRules.IsFrozen(invoiceStatus))
            throw new DefaultException(NfeLockRules.FrozenMessage(invoiceStatus));

        if (confirmedFrozen)
            throw new DefaultException(ConfirmedMessage);
    }

    public static void EnsureDeletable(SalesInvoice invoice)
    {
        if (invoice.NfeStatus is NfeStatus.Processing or NfeStatus.Authorized or NfeStatus.Denied
            or NfeStatus.Cancelled or NfeStatus.Voided)
            throw new DefaultException(
                "Documento com NF-e em processamento, autorizada, denegada, cancelada ou inutilizada não pode ser excluído.");
    }

    public static void EnsureCancellable(SalesInvoice invoice)
    {
        switch (invoice.NfeStatus)
        {
            case NfeStatus.Processing:
                throw new DefaultException(NfeLockRules.ProcessingMessage);
            case NfeStatus.Authorized:
                throw new DefaultException(NfeLockRules.AuthorizedCancelMessage);
            case NfeStatus.Cancelled:
                throw new DefaultException(NfeLockRules.CancelledPendingMessage);
        }
    }

    public static void EnsureManualTaxDocument(SalesInvoice invoice)
    {
        // Inclui "Sem NF" e limpar a NF: o vínculo desfaz-se excluindo ou cancelando o documento.
        if (invoice.IsSapLinked)
            throw new DefaultException(SapLinkedTaxDocumentMessage);

        if (invoice.IsNfeReturn)
            throw new DefaultException("Número, série e chave da devolução vêm da emissão da NF-e pelo Siagro.");

        // O complemento de ICMS (spec 2026-10-18) também só existe como NF-e emitida pelo Siagro.
        if (invoice.InvoiceType == SalesInvoiceType.IcmsComplement)
            throw new DefaultException("Número, série e chave do complemento de ICMS vêm da emissão da NF-e pelo Siagro.");

        if (invoice.NfeStatus != NfeStatus.None || invoice.NfeRandomCode is not null)
            throw new DefaultException("Número, série e chave deste documento vêm da emissão da NF-e pelo Siagro.");
    }
}
