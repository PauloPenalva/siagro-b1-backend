namespace SiagroB1.Domain.Enums;

/// <summary>Situação da averbação do CT-e na AT&amp;M.</summary>
public enum CteEndorsementStatus
{
    None = 0,
    Endorsed = 1,
    /// <summary>O CT-e foi cancelado na SEFAZ, mas o cancelamento da averbação ainda não chegou à AT&amp;M.</summary>
    CancellationPending = 2,
    Cancelled = 3,
}
