namespace SiagroB1.Domain.Enums;

/// <summary>Tipo do XML guardado. O 2b acrescenta os eventos (cancelamento, CC-e).</summary>
public enum SalesInvoiceNfeXmlKind
{
    Signed = 1,
    Authorized = 2,
    /// <summary>procNFe da NF-e denegada: o emitente guarda o XML denegado.</summary>
    Denied = 3,
}
