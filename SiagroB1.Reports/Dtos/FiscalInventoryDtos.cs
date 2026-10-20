namespace SiagroB1.Reports.Dtos;

/// <summary>Filtros do Inventário em Data: filial e data obrigatórias (inclusiva), produto opcional.</summary>
public class FiscalInventoryRequest
{
    public string BranchCode { get; set; } = string.Empty;

    /// <summary>Posição do estoque ao fim deste dia (mesmo corte do "até" do Kardex Fiscal).</summary>
    public DateTime Date { get; set; }

    /// <summary>Produto; vazio = todos com saldo.</summary>
    public string? ItemCode { get; set; }
}

public class FiscalInventoryRowDto
{
    /// <summary>NCM do cadastro do produto; vazio quando não informado.</summary>
    public string Ncm { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? UnitOfMeasureCode { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>Custo médio (valor ÷ quantidade, 6 casas); nulo quando a quantidade é zero ou negativa.</summary>
    public decimal? UnitCost { get; set; }
    public decimal Value { get; set; }
}

public class FiscalInventoryData
{
    public DateTime Date { get; set; }
    public string BranchCode { get; set; } = string.Empty;
    public List<FiscalInventoryRowDto> Rows { get; set; } = [];
    public decimal TotalValue { get; set; }

    /// <summary>"código - descrição" dos produtos com complemento de valor sem camada aberta.</summary>
    public List<string> PendingComplementItems { get; set; } = [];
}
