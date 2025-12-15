namespace SiagroB1.Domain.Enums;

public enum ReleaseStatus
{
    Pending = 0,    // Aguardando aprovação
    Actived = 1,    // Liberada
    Completed = 2,  // Totalmente romaneada
    Cancelled = 3,
    Paused = 4      // Embarques pausados
}