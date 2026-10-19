namespace SiagroB1.Domain.Enums;

/// <summary>Documento de origem do e-mail da NF-e.</summary>
public enum NfeEmailDocumentType
{
    Sales = 1,
    Purchase = 2,
}

/// <summary>Evento da NF-e que o e-mail comunica.</summary>
public enum NfeEmailEventType
{
    Authorized = 1,
    Cancelled = 2,
}

/// <summary>Quem pediu o envio: o gatilho automático (um por documento e evento) ou o reenvio manual.</summary>
public enum NfeEmailTrigger
{
    Automatic = 1,
    Manual = 2,
}

public enum NfeEmailStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3,
    Skipped = 4,
}
