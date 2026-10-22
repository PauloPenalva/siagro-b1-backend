using SiagroB1.Infra.FiscalStock;

namespace SiagroB1.Reports.Dtos;

/// <summary>Camadas PEPS em aberto de um produto numa data (base do Inventário em Data).</summary>
public class FiscalInventoryItemLayers
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? UnitOfMeasureCode { get; set; }

    /// <summary>Chave da camada = linha de nota que a criou, ou a chave do saldo inicial.</summary>
    public List<FifoLayer> Layers { get; set; } = [];

    /// <summary>Quantidade em saldo negativo (saiu sem camada). Saldo = Σ camadas − Deficit.</summary>
    public decimal Deficit { get; set; }

    /// <summary>Complemento de valor sem camada aberta, fora do saldo até a próxima entrada.</summary>
    public decimal PendingValue { get; set; }

    /// <summary>Valor no saldo sem camada (diferença de ajuste de saída). Valor = soma das camadas + ResidualValue.</summary>
    public decimal ResidualValue { get; set; }
}

/// <summary>Filtros do Kardex Fiscal: filial obrigatória, produto opcional, período inclusivo em dias.</summary>
public class FiscalKardexRequest
{
    public string BranchCode { get; set; } = string.Empty;

    /// <summary>Produto; vazio = todos com saldo ou movimento.</summary>
    public string? ItemCode { get; set; }

    public DateTime FromDate { get; set; }

    public DateTime ToDate { get; set; }
}

public class FiscalKardexData
{
    public List<FiscalKardexItemDto> Items { get; set; } = [];
}

/// <summary>Um produto: saldo anterior (na data "de"), movimentos do período e saldo final.</summary>
public class FiscalKardexItemDto
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? UnitOfMeasureCode { get; set; }
    public decimal OpeningQuantity { get; set; }
    public decimal OpeningValue { get; set; }
    public List<FiscalKardexLineDto> Lines { get; set; } = [];
    public decimal ClosingQuantity { get; set; }
    public decimal ClosingValue { get; set; }
}

public class FiscalKardexLineDto
{
    public DateTime Date { get; set; }

    /// <summary>"E" ou "S".</summary>
    public string Direction { get; set; } = string.Empty;

    /// <summary>"Saldo inicial", "Entrada", "Saída", "Devolução de compra", "Devolução de venda",
    /// "Complemento de valor", "Ajuste manual — entrada", "Ajuste manual — saída".</summary>
    public string Kind { get; set; } = string.Empty;

    public string InternalNumber { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;

    /// <summary>Série fiscal da nota (vazio em ajuste e saldo inicial); usada no livro modelo 3.</summary>
    public string Series { get; set; } = string.Empty;

    /// <summary>Número fiscal da nota sem a série (ou o nº interno); usado no livro modelo 3.</summary>
    public string Number { get; set; } = string.Empty;
    public string Partner { get; set; } = string.Empty;
    public string Cfop { get; set; } = string.Empty;
    public string UsageName { get; set; } = string.Empty;
    public decimal QuantityIn { get; set; }
    public decimal QuantityOut { get; set; }
    public decimal UnitCost { get; set; }
    public decimal ValueIn { get; set; }
    public decimal ValueOut { get; set; }
    public decimal BalanceQuantity { get; set; }
    public decimal BalanceValue { get; set; }

    /// <summary>GrandTotal da linha da nota, só para conferência (a valorização é PEPS).</summary>
    public decimal InvoiceValue { get; set; }

    /// <summary>"Saldo insuficiente", "Unidade diferente", "Complemento sem saldo: …" (vários separados por "; ").</summary>
    public string? Warning { get; set; }
}
