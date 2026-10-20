namespace SiagroB1.Domain.Enums;

/// <summary>Manifestação do Destinatário já registrada na SEFAZ para o documento recebido.</summary>
public enum DfeManifestationStatus
{
    None = 0,
    /// <summary>Ciência da Operação (210210).</summary>
    Acknowledged = 1,
    /// <summary>Confirmação da Operação (210200).</summary>
    Confirmed = 2,
    /// <summary>Desconhecimento da Operação (210220).</summary>
    Unknown = 3,
    /// <summary>Operação Não Realizada (210240).</summary>
    NotPerformed = 4
}

/// <summary>Situação da NF-e na SEFAZ (cSitNFe do resumo).</summary>
public enum DfeInvoiceSituation
{
    Authorized = 1,
    Denied = 2,
    Cancelled = 3
}
