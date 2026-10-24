namespace SiagroB1.Domain.Enums;

/// <summary>Situacao de um lote enviado a EFD-Reinf.</summary>
public enum ReinfBatchStatus
{
    Sent,
    Processing,
    Processed,
    Refused,
    Failed,
}
