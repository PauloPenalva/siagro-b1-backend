namespace SiagroB1.Domain.Enums;

/// <summary>
/// De onde o documento de saída nasceu. Decide o ramo de processamento —
/// ver <c>SalesInvoiceOriginResolver</c>.
/// </summary>
public enum SalesInvoiceOrigin
{
    /// <summary>Faturamento de uma carga montada (fluxo novo). SalesTransactions é VAZIA.</summary>
    ShipmentLoad = 0,

    /// <summary>Faturamento de romaneios soltos (fluxo legado, anterior à Carga).</summary>
    LegacyShipment = 1,

    /// <summary>Documento avulso: sem romaneio e sem carga. O efeito vem da natureza de operação.</summary>
    Standalone = 2
}
