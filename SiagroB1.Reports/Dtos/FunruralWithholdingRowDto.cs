namespace SiagroB1.Reports.Dtos;

public enum FunruralRowKind
{
    /// <summary>Linha do Documento de Entrada, valores brutos.</summary>
    Item = 0,

    /// <summary>Devolução de compra abatida da linha acima, valores negativos.</summary>
    Return = 1,

    /// <summary>Líquido do item (só quando houve devolução).</summary>
    Net = 2,
}

/// <summary>
/// Linha do relatório de Funrural retido. Alíquotas vão como texto: só a linha do item as mostra,
/// e devolução/líquido saem em branco.
/// </summary>
public class FunruralWithholdingRowDto
{
    public FunruralRowKind Kind { get; set; }
    public string IssueDate { get; set; } = "";
    public string InternalNumber { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string Partner { get; set; } = "";

    /// <summary>Código do produtor da linha de origem (a devolução e o líquido herdam o do item).</summary>
    public string CardCode { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Base { get; set; }
    public string InssRate { get; set; } = "";
    public decimal InssValue { get; set; }
    public string RatRate { get; set; } = "";
    public decimal RatValue { get; set; }
    public string SenarRate { get; set; } = "";
    public decimal SenarValue { get; set; }
    public decimal Total { get; set; }
}

/// <summary>Soma de base e valores do Funrural.</summary>
public record FunruralAmounts(decimal Base, decimal InssValue, decimal RatValue, decimal SenarValue)
{
    public static readonly FunruralAmounts Zero = new(0m, 0m, 0m, 0m);

    public decimal Total => InssValue + RatValue + SenarValue;

    public static FunruralAmounts operator +(FunruralAmounts a, FunruralAmounts b) =>
        new(a.Base + b.Base, a.InssValue + b.InssValue, a.RatValue + b.RatValue, a.SenarValue + b.SenarValue);

    public static FunruralAmounts operator -(FunruralAmounts a) =>
        new(-a.Base, -a.InssValue, -a.RatValue, -a.SenarValue);
}

/// <summary>Resultado do relatório: linhas corridas e os três totais do rodapé.</summary>
public record FunruralWithholdingData(
    List<FunruralWithholdingRowDto> Rows,
    int ItemCount,
    FunruralAmounts Gross,
    FunruralAmounts Returns)
{
    public FunruralAmounts Net => Gross + Returns;
}
