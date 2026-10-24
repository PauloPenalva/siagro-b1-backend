namespace SiagroB1.Domain.Dtos;

/// <summary>Parâmetros da EFD-Reinf de uma raiz de CNPJ (enums como inteiros para a tela).</summary>
public class ReinfSettingsDto
{
    public string TaxIdRoot { get; set; } = string.Empty;
    public string BranchCode { get; set; } = string.Empty;
    public int Environment { get; set; }
    public string TaxClassification { get; set; } = string.Empty;
    public bool KeepsBookkeeping { get; set; }
    public bool PayrollRelief { get; set; }
    public bool FineExemptionAgreement { get; set; }
    public int? CompanySituation { get; set; }
    public string ContactName { get; set; } = string.Empty;
    public string ContactCpf { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public string ValidFrom { get; set; } = string.Empty;
    public string? ClosingResponsibleName { get; set; }
    public string? ClosingResponsibleCpf { get; set; }
    public string? ClosingResponsiblePhone { get; set; }
    public string? ClosingResponsibleEmail { get; set; }
    public int RegistrationStatus { get; set; }
    public string? RegistrationReceipt { get; set; }
    public bool RegistrationUpdatePending { get; set; }
    /// <summary>R-1000 sem confirmação da Receita (tempo esgotado/retorno ilegível).</summary>
    public bool RegistrationUnconfirmed { get; set; }
    public bool Exists { get; set; }
}

/// <summary>Linha da visão do período: apuração × evento transmitido, achatada.</summary>
public class ReinfOverviewRowDto
{
    public string EstablishmentTaxId { get; set; } = string.Empty;
    public string BranchCode { get; set; } = string.Empty;
    public string CardCode { get; set; } = string.Empty;
    public string CardName { get; set; } = string.Empty;
    public string ProducerTaxId { get; set; } = string.Empty;
    public bool PayrollOption { get; set; }
    public decimal Gross { get; set; }
    public decimal Inss { get; set; }
    public decimal Rat { get; set; }
    public decimal Senar { get; set; }
    public string Situation { get; set; } = string.Empty;
    public string? Receipt { get; set; }
    public string? Occurrences { get; set; }
    public Guid? EventKey { get; set; }
}

public class ReinfIssueDto
{
    public string Message { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

public class ReinfOverviewDto
{
    public string TaxIdRoot { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    /// <summary>Nome do enum <c>ReinfPeriodStatus</c> (Open, Closing, Closed...).</summary>
    public string PeriodStatus { get; set; } = string.Empty;
    public string? ClosingReceipt { get; set; }
    public List<ReinfOverviewRowDto> Rows { get; set; } = [];
    public List<ReinfIssueDto> Issues { get; set; } = [];
    /// <summary>Algum evento do período (qualquer tipo) ou R-1000 do contribuinte sem confirmação da Receita.</summary>
    public bool HasUnconfirmed { get; set; }
}

public class ReinfEventDto
{
    public Guid Key { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string? Period { get; set; }
    public string? Receipt { get; set; }
    public string? Occurrences { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? BatchKey { get; set; }
    public string? BatchProtocol { get; set; }
    public string? BatchStatus { get; set; }
    public string Xml { get; set; } = string.Empty;
}

public class ReinfBatchXmlDto
{
    public string RequestXml { get; set; } = string.Empty;
    public string? ResponseXml { get; set; }
    public string? Error { get; set; }
}

public class ReinfContributorDto
{
    public string TaxIdRoot { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool HasSettings { get; set; }
}
