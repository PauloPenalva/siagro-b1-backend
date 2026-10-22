namespace SiagroB1.Domain.Enums;

/// <summary>Papel da parte no CT-e. Os valores 0 a 3 coincidem com o código do tomador (toma3).</summary>
public enum CtePartyRole
{
    /// <summary>Remetente.</summary>
    Sender = 0,
    /// <summary>Expedidor.</summary>
    Shipper = 1,
    /// <summary>Recebedor.</summary>
    Receiver = 2,
    /// <summary>Destinatário.</summary>
    Recipient = 3,
    /// <summary>Outro tomador (toma4).</summary>
    OtherTaker = 4,
}
