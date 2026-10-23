namespace SiagroB1.Domain.Enums;

/// <summary>
/// Situação do MDF-e. Só Rascunho é editável; o número nasce no Confirmar. O vínculo do CT-e fica ativo
/// em Draft, Confirmed, Processing, Rejected e Authorized; inativo em Voided, Cancelled e Closed.
/// </summary>
public enum MdfeStatus
{
    Draft = 0,
    Confirmed = 1,
    Processing = 2,
    Authorized = 3,
    Rejected = 4,
    Denied = 5,
    Cancelled = 6,
    Voided = 7,
    Closed = 8,
}

/// <summary>Tipo do XML guardado do MDF-e.</summary>
public enum MdfeXmlKind
{
    Authorized = 0,
    Cancellation = 1,
    Closing = 2,
    DriverInclusion = 3,
    /// <summary>MDF-e assinado exatamente como enviado à SEFAZ (gravado antes do envio).</summary>
    Signed = 4,
}

/// <summary>Evento registrado do MDF-e.</summary>
public enum MdfeEventType
{
    Closing = 0,
    Cancellation = 1,
    DriverInclusion = 2,
}
