namespace SiagroB1.Domain.Enums;

/// <summary>
/// Tipo de movimento no histórico da carga (<c>SHIPMENT_LOAD_MOVEMENTS</c>).
/// O histórico é NARRATIVA, não autoridade: ver o XML-doc de
/// <see cref="Entities.ShipmentLoadMovement"/>.
/// </summary>
public enum ShipmentLoadMovementType
{
    Assembled = 0,          // Carga montada a partir dos romaneios
    Billed = 1,             // Documento de saída emitido — consome saldo
    BillingCancelled = 2,   // Documento cancelado — devolve saldo
    ReturnRequested = 3,    // Devolução criada — ainda não devolve saldo
    Returned = 4,           // Devolução confirmada — devolve saldo
    ReturnReversed = 5,     // Confirmação da devolução estornada — re-consome
    BillingDeleted = 6,     // Documento excluído — devolve saldo
    Cancelled = 7           // Carga cancelada — zera o saldo e solta os romaneios
}
