using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// O nItem da NF-e do fornecedor em cada linha do Documento de Entrada de terceiro — é o que a devolução referencia
/// (DFeReferenciado). Com o XML guardado, todo nItem informado precisa existir na nota. A tributação NÃO vem daqui:
/// o terceiro Normal é calculado pela natureza, como o próprio (spec terceiro-chave D1, §6.2).
/// </summary>
public static class PurchaseInvoiceSupplierItemNumbers
{
    public static void Ensure(PurchaseInvoice invoice, IEnumerable<PurchaseInvoiceItem> items)
    {
        if (invoice.IssuerType != DocumentIssuerType.ThirdParty || invoice.InvoiceType != PurchaseInvoiceType.Normal ||
            invoice.XmlData is not { Length: > 0 })
            return;

        var numbers = SupplierNfeXmlReader.Read(invoice.XmlData).Items.Select(d => d.ItemNumber).ToHashSet();

        foreach (var item in items)
            if (item.NfeItemNumber is { } number && !numbers.Contains(number))
                throw new DefaultException($"Item {item.ItemCode}: o item {number} não existe na NF-e do fornecedor.");
    }
}
