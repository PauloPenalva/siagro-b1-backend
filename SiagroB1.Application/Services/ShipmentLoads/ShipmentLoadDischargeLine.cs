namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Uma parcela do rateio do ticket de descarga, como chega da tela (GAC-1171, rateio): a linha da
/// nota e o peso dela. A nota não viaja — o servidor a resolve pela linha.
/// </summary>
public sealed record ShipmentLoadDischargeLine(Guid SalesInvoiceItemKey, decimal Quantity);
