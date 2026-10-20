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

/// <summary>Livro de Registro de Inventário (modelo 7): sempre da filial inteira.</summary>
public class FiscalInventoryBookRequest
{
    public string BranchCode { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public int BookNumber { get; set; }

    /// <summary>Número da folha do termo de abertura; as demais seguem em sequência.</summary>
    public int FirstSheet { get; set; } = 1;
}

/// <summary>Linha do livro, com os textos já formatados em pt-BR (o FastReport só imprime).</summary>
public class FiscalInventoryBookRowDto
{
    public string Ncm { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string UnitOfMeasureCode { get; set; } = string.Empty;
    public string QuantityText { get; set; } = string.Empty;
    public string UnitCostText { get; set; } = string.Empty;

    /// <summary>Valor parcial; somado pelo relatório para o "a transportar".</summary>
    public decimal Value { get; set; }
    public string ValueText { get; set; } = string.Empty;

    /// <summary>Subtotal do NCM, só na última linha de cada NCM.</summary>
    public decimal? NcmGroupTotal { get; set; }
    public string NcmGroupTotalText { get; set; } = string.Empty;
}
