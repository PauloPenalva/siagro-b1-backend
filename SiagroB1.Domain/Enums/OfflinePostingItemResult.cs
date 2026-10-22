namespace SiagroB1.Domain.Enums;

public enum OfflinePostingItemResult
{
    /// <summary>Gravado (Processar).</summary>
    Posted = 1,

    /// <summary>Seria gravado (Simular).</summary>
    WouldPost = 2,

    Error = 3,

    /// <summary>Nada a lançar: informativo ou valores zerados.</summary>
    Skipped = 4,
}
