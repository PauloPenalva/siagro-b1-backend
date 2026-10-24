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
    /// <summary>Envio sem confirmação da Receita (tempo esgotado/retorno ilegível): não é reenviado sozinho.</summary>
    Unconfirmed,
}
