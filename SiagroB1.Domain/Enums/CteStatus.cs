namespace SiagroB1.Domain.Enums;

/// <summary>Situação do CT-e. Só Rascunho é editável; o número nasce no Confirmar.</summary>
public enum CteStatus
{
    Draft = 0,
    Confirmed = 1,
    Processing = 2,
    Authorized = 3,
    Rejected = 4,
    Denied = 5,
    Cancelled = 6,
    Voided = 7,
}

/// <summary>Tipo do XML guardado do CT-e.</summary>
public enum CteXmlKind
{
    Authorized = 0,
    Cancellation = 1,
    Correction = 2,
}
