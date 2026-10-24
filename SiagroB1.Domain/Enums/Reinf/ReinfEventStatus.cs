namespace SiagroB1.Domain.Enums;

/// <summary>Situacao de um evento da EFD-Reinf.</summary>
public enum ReinfEventStatus
{
    Pending,
    Sent,
    Accepted,
    Rejected,
    Excluded,
    Outdated,
}
