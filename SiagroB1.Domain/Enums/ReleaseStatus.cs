namespace SiagroB1.Domain.Enums;

public enum ReleaseStatus
{
    Pending = 0,    // Aguardando aprovação
    Approved = 1,   // Liberada - cria saldo no estoque
    Completed = 2,  // Totalmente romaneada
    Cancelled = 3
}