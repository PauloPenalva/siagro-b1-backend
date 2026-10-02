namespace SiagroB1.Domain.Enums;

/// <summary>Situação da NF-e do documento de saída (NF-e STANDALONE). None = nunca emitida.</summary>
public enum NfeStatus
{
    None = 0,
    Processing = 1,
    Authorized = 2,
    Rejected = 3,
    Denied = 4,
}
