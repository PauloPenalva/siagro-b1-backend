using SiagroB1.Domain.Entities;

namespace SiagroB1.Reports.Dtos;

/// <summary>Parâmetros da geração do SPED Fiscal (EFD ICMS/IPI) de uma filial e mês.</summary>
public class SpedFiscalRequest
{
    public string BranchCode { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }

    /// <summary>COD_FIN do 0000: 0 = original, 1 = substituto.</summary>
    public int Purpose { get; set; }
    public bool IncludeInventory { get; set; }

    /// <summary>Data do inventário (H005); nula = último dia do ano anterior.</summary>
    public DateTime? InventoryDate { get; set; }

    /// <summary>MOT_INV do H005 ("01".."06").</summary>
    public string InventoryReason { get; set; } = "01";

    /// <summary>Saldo credor do período anterior (VL_SLD_CREDOR_ANT do E110).</summary>
    public decimal PreviousCreditBalance { get; set; }
}

/// <summary>Tudo o que os registros do arquivo precisam, já lido do banco.</summary>
public class SpedFiscalData
{
    public required Branch Branch { get; set; }
    public SpedFiscalSettings? Settings { get; set; }
    public List<FiscalDocumentLineDto> Lines { get; set; } = [];
    public List<SpedVoidDocument> VoidDocuments { get; set; } = [];
    public List<SpedParticipant> Participants { get; set; } = [];
    public List<SpedItem> Items { get; set; } = [];
    public List<SpedUnit> Units { get; set; } = [];
    public List<SpedUnitConversion> Conversions { get; set; } = [];
    public FiscalInventoryData? Inventory { get; set; }
}

/// <summary>NF-e própria cancelada/denegada/inutilizada: C100 só com a chave (sem participante nem itens).</summary>
public class SpedVoidDocument
{
    /// <summary>IND_OPER: "0" entrada, "1" saída.</summary>
    public string Direction { get; set; } = "";

    /// <summary>COD_SIT: "02" cancelada, "04" denegada, "05" inutilizada.</summary>
    public string Situation { get; set; } = "";
    public string Model { get; set; } = "55";
    public string Series { get; set; } = "";
    public string Number { get; set; } = "";
    public string? AccessKey { get; set; }
}

/// <summary>Participante (registro 0150).</summary>
public class SpedParticipant
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string CountryCode { get; set; } = "01058";
    public string Cnpj { get; set; } = "";
    public string Cpf { get; set; } = "";
    public string StateRegistration { get; set; } = "";
    public string MunicipalityCode { get; set; } = "";
    public string Street { get; set; } = "";
    public string Number { get; set; } = "";
    public string Complement { get; set; } = "";
    public string District { get; set; } = "";
}

/// <summary>Produto/serviço (registro 0200).</summary>
public class SpedItem
{
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string UnitCode { get; set; } = "";

    /// <summary>TIPO_ITEM: "00" mercadoria para revenda, "09" serviço (item de frete da filial).</summary>
    public string ItemType { get; set; } = "00";
    public string Ncm { get; set; } = "";
    public string Cest { get; set; } = "";
}

/// <summary>Unidade de medida (registro 0190).</summary>
public class SpedUnit
{
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>Fator de conversão (registro 0220): 1 <see cref="UnitCode"/> = Factor unidades do produto.</summary>
public class SpedUnitConversion
{
    public string ItemCode { get; set; } = "";
    public string UnitCode { get; set; } = "";
    public decimal Factor { get; set; }
}
