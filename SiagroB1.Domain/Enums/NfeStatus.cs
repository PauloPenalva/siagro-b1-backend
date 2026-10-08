namespace SiagroB1.Domain.Enums;

/// <summary>Situação da NF-e STANDALONE do documento (saída ou entrada). None = nunca emitida.</summary>
public enum NfeStatus
{
    None = 0,
    Processing = 1,
    Authorized = 2,
    Rejected = 3,
    Denied = 4,
    /// <summary>Cancelamento (evento 110111) registrado na SEFAZ. Número e chave ficam queimados.</summary>
    Cancelled = 5,
    /// <summary>
    /// Numeração inutilizada na SEFAZ (NfeInutilizacao4): a NF-e rejeitada de um documento cancelado
    /// nunca foi autorizada e o número foi queimado. Documento congelado como o cancelado.
    /// </summary>
    Voided = 6,
}
