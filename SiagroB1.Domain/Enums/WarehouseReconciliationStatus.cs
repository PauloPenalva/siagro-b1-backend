namespace SiagroB1.Domain.Enums;

public enum WarehouseReconciliationStatus
{
    Draft = 0,       // Rascunho
    InApproval = 1,  // Em aprovação
    Approved = 2,    // Aprovada - gerou o romaneio de Perda/Sobra
    Rejected = 3,    // Rejeitada (final)
    Cancelled = 4,   // Cancelada
}
