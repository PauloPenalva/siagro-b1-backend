namespace SiagroB1.Reports.Dtos;

/// <summary>Livro Registro Kardex (modelo 3 — Registro de Controle da Produção e do Estoque) da filial no período.</summary>
public class FiscalKardexBookRequest
{
    public string BranchCode { get; set; } = string.Empty;

    /// <summary>Produto; vazio = todos com saldo ou movimento.</summary>
    public string? ItemCode { get; set; }

    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int BookNumber { get; set; }

    /// <summary>Número da folha do termo de abertura; as demais seguem em sequência.</summary>
    public int FirstSheet { get; set; } = 1;
}

/// <summary>
/// Linha do livro, com os textos já formatados em pt-BR (o FastReport só imprime). Cada ficha
/// (produto × mês) tem a linha de saldo inicial, os movimentos e os totais repetidos em todas as
/// linhas — o rodapé do grupo lê os da última.
/// </summary>
public class FiscalKardexBookRowDto
{
    /// <summary>"AAAA-MM|código": agrupa a ficha e força folha nova a cada troca.</summary>
    public string SheetKey { get; set; } = string.Empty;

    public string Period { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string UnitOfMeasureCode { get; set; } = string.Empty;
    public string Ncm { get; set; } = string.Empty;

    public bool IsOpening { get; set; }
    public string Species { get; set; } = string.Empty;
    public string Series { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string DateText { get; set; } = string.Empty;
    public string Day { get; set; } = string.Empty;
    public string LedgerAccount { get; set; } = string.Empty;
    public string Cfop { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string QuantityText { get; set; } = string.Empty;
    public string ValueText { get; set; } = string.Empty;
    public string IpiText { get; set; } = string.Empty;
    public string BalanceText { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    public string TotalInQuantityText { get; set; } = string.Empty;
    public string TotalInValueText { get; set; } = string.Empty;
    public string TotalOutQuantityText { get; set; } = string.Empty;
    public string TotalOutValueText { get; set; } = string.Empty;
    public string ClosingBalanceText { get; set; } = string.Empty;
}
