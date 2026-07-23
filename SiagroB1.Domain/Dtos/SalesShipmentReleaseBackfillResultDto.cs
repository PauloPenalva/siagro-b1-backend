namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Resultado do backfill de <c>DeliveryLocationName</c> das liberações de entrega de venda.
/// </summary>
public class SalesShipmentReleaseBackfillResultDto
{
    /// <summary>Liberações com nome em branco que entraram no escopo do backfill.</summary>
    public int Scanned { get; set; }

    /// <summary>Liberações efetivamente preenchidas (o cliente foi resolvido).</summary>
    public int Updated { get; set; }
}
