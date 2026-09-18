namespace SiagroB1.Domain.Enums;

/// <summary>
/// Natureza do documento anexado à carga (GAC-1171). Lista fechada, porque o objetivo é poder
/// perguntar "falta o ticket de descarga?" — o que uma descrição livre não responde.
/// </summary>
/// <remarks>
/// ⚠️ Persistido como <c>int</c>. Valor novo entra SEMPRE no fim da numeração: renumerar
/// reescreveria o significado de toda linha já gravada, e mudança de enum não gera migration.
/// </remarks>
public enum ShipmentLoadAttachmentType
{
    LoadingTicket = 0,    // Ticket de Carga
    DischargeTicket = 1,  // Ticket de Descarga
    TaxDocument = 2,      // Nota Fiscal
    FreightDocument = 3,  // Conhecimento de Frete
    Other = 4,            // Outro
}
