namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Linha do modelo <c>StorageLotStatement.frx</c>, usado pela Posição do Lote e pelo Extrato do
/// Contrato com Lotes (GAC-1198). Uma fonte única com várias seções; cada linha traz os títulos das
/// colunas da própria seção e o .frx só desenha.
///
/// Todas as células são TEXTO já formatado pelo serviço: a mesma coluna guarda peso (3 casas) numa
/// seção e valor (2 casas) em outra, e o total da seção é uma linha comum com <see cref="Bold"/>.
/// W1..W4 são colunas numéricas estreitas, C1..C6 a classificação, Q1..Q3 as colunas largas da
/// direita.
/// </summary>
public class LotStatementRowDto
{
    public int Section { get; set; }
    public string SectionTitle { get; set; } = "";

    /// <summary>Linha de total da seção: sai em negrito.</summary>
    public bool Bold { get; set; }

    public string Date { get; set; } = "";
    public string Document { get; set; } = "";
    public string Movement { get; set; } = "";
    public string Detail1 { get; set; } = "";
    public string Detail2 { get; set; } = "";

    public string W1 { get; set; } = "";
    public string W2 { get; set; } = "";
    public string W3 { get; set; } = "";
    public string W4 { get; set; } = "";

    public string C1 { get; set; } = "";
    public string C2 { get; set; } = "";
    public string C3 { get; set; } = "";
    public string C4 { get; set; } = "";
    public string C5 { get; set; } = "";
    public string C6 { get; set; } = "";

    public string Q1 { get; set; } = "";
    public string Q2 { get; set; } = "";
    public string Q3 { get; set; } = "";

    public string HDate { get; set; } = "";
    public string HDocument { get; set; } = "";
    public string HMovement { get; set; } = "";
    public string HDetail1 { get; set; } = "";
    public string HDetail2 { get; set; } = "";
    public string HW1 { get; set; } = "";
    public string HW2 { get; set; } = "";
    public string HW3 { get; set; } = "";
    public string HW4 { get; set; } = "";
    public string HC1 { get; set; } = "";
    public string HC2 { get; set; } = "";
    public string HC3 { get; set; } = "";
    public string HC4 { get; set; } = "";
    public string HC5 { get; set; } = "";
    public string HC6 { get; set; } = "";
    public string HQ1 { get; set; } = "";
    public string HQ2 { get; set; } = "";
    public string HQ3 { get; set; } = "";

    /// <summary>Grava a classificação na coluna <paramref name="index"/> (0 a 5).</summary>
    public void SetQuality(int index, string value)
    {
        switch (index)
        {
            case 0: C1 = value; break;
            case 1: C2 = value; break;
            case 2: C3 = value; break;
            case 3: C4 = value; break;
            case 4: C5 = value; break;
            case 5: C6 = value; break;
        }
    }

    /// <summary>Grava o título da coluna de classificação <paramref name="index"/> (0 a 5).</summary>
    public void SetQualityHeader(int index, string value)
    {
        switch (index)
        {
            case 0: HC1 = value; break;
            case 1: HC2 = value; break;
            case 2: HC3 = value; break;
            case 3: HC4 = value; break;
            case 4: HC5 = value; break;
            case 5: HC6 = value; break;
        }
    }
}
