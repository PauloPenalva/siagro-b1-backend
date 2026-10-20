using System.Text.Json.Serialization;

namespace SiagroB1.Reports.Dtos;

/// <summary>Resultado da Conferência Fiscal; cada lista alimenta uma aba da tela.</summary>
public class FiscalConferenceData
{
    public List<FiscalDocumentLineDto> Documents { get; set; } = [];
    public List<FiscalSummaryRowDto> Summary { get; set; } = [];
    public List<IcmsAssessmentRowDto> Assessment { get; set; } = [];
    public IcmsAssessmentTotalsDto AssessmentTotals { get; set; } = new();
    public List<R2055ProducerRowDto> R2055 { get; set; } = [];
    public List<FiscalIssueDto> Issues { get; set; } = [];
}

/// <summary>Uma linha por item de documento escriturável (equivale ao C100 + C170 do SPED Fiscal).</summary>
public class FiscalDocumentLineDto
{
    /// <summary>"E" (entrada) ou "S" (saída), decidido pelo 1º dígito do CFOP.</summary>
    public string Direction { get; set; } = "";
    public DateTime Date { get; set; }

    /// <summary>"55" (NF-e) ou "01" (outro documento fiscal).</summary>
    public string Model { get; set; } = "";
    public string Series { get; set; } = "";
    public string Number { get; set; } = "";

    /// <summary>Número interno do documento no sistema.</summary>
    public string InternalNumber { get; set; } = "";
    public string? AccessKey { get; set; }
    public string Partner { get; set; } = "";
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string Cfop { get; set; } = "";
    public string CstIcms { get; set; } = "";

    /// <summary>Total da linha (GrandTotal; na entrada o complemento já está no Total).</summary>
    public decimal OperationValue { get; set; }
    public decimal IcmsBase { get; set; }
    public decimal IcmsRate { get; set; }
    public decimal IcmsValue { get; set; }
    public string CstPis { get; set; } = "";
    public decimal PisValue { get; set; }
    public string CstCofins { get; set; } = "";
    public decimal CofinsValue { get; set; }

    /// <summary>Origem da tabela: "Entrada" ou "Saída".</summary>
    public string DocumentType { get; set; } = "";
    public Guid DocumentKey { get; set; }

    /// <summary>Devolução (compra ou venda); só serve às regras de inconsistência, não vai ao JSON.</summary>
    [JsonIgnore]
    public bool IsReturn { get; set; }

    /// <summary>ICMS diferido da linha; entra na conta da regra de ICMS, não vai ao JSON.</summary>
    [JsonIgnore]
    public decimal IcmsDeferredValue { get; set; }
}

/// <summary>Resumo por Direção + CST + CFOP + alíquota (equivale ao C190 do SPED Fiscal).</summary>
public class FiscalSummaryRowDto
{
    public string Direction { get; set; } = "";
    public string CstIcms { get; set; } = "";
    public string Cfop { get; set; } = "";
    public decimal IcmsRate { get; set; }
    public decimal OperationValue { get; set; }
    public decimal IcmsBase { get; set; }
    public decimal IcmsValue { get; set; }
}

/// <summary>Apuração do ICMS por CFOP (equivale ao E110): saída debita, entrada credita.</summary>
public class IcmsAssessmentRowDto
{
    public string Cfop { get; set; } = "";
    public decimal OperationValue { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}

public class IcmsAssessmentTotalsDto
{
    public decimal Debits { get; set; }
    public decimal Credits { get; set; }

    /// <summary>Débitos - Créditos; positivo = a recolher.</summary>
    public decimal Balance { get; set; }
}

/// <summary>Prévia do R-2055: Funrural retido do mês somado por produtor, já líquido das devoluções.</summary>
public class R2055ProducerRowDto
{
    public string CardCode { get; set; } = "";
    public string CardName { get; set; } = "";

    /// <summary>CPF do produtor (RuralProducerCpf) quando houver, senão o CPF/CNPJ do cadastro.</summary>
    public string TaxId { get; set; } = "";

    /// <summary>Enquadramento do produtor em pt-BR; vazio sem cadastro ou sem tipo.</summary>
    public string ProducerType { get; set; } = "";
    public decimal Base { get; set; }
    public decimal Inss { get; set; }
    public decimal Rat { get; set; }
    public decimal Senar { get; set; }
    public decimal Total { get; set; }
}

/// <summary>Uma inconsistência: documento, item (nulo quando vale para o documento todo) e regra violada.</summary>
public class FiscalIssueDto
{
    public string Direction { get; set; } = "";
    public DateTime Date { get; set; }
    public string Number { get; set; } = "";
    public string InternalNumber { get; set; } = "";
    public string Partner { get; set; } = "";
    public string? ItemCode { get; set; }

    /// <summary>Texto pt-BR da regra violada.</summary>
    public string Rule { get; set; } = "";
    public Guid DocumentKey { get; set; }
}
