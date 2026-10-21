namespace SiagroB1.Reports.Dtos;

/// <summary>Lado do contrato no Extrato do Contrato.</summary>
public enum ContractStatementSide
{
    Purchase,
    Sales,
}

/// <summary>
/// Linha do Extrato do Contrato. Uma fonte única com várias seções (físico, fiscal, títulos, baixas,
/// fixações). Cada linha traz os títulos das colunas da própria seção, e o .frx só desenha. Colunas
/// genéricas: Q1..Q4 são quantidades na UM do contrato, A1..A3 são valores. Zero não é impresso.
/// Sinal: positivo cumpre o contrato (entrega, nota, título no sentido do contrato).
/// </summary>
public class ContractStatementRowDto
{
    public int Section { get; set; }
    public string SectionTitle { get; set; } = "";

    public string Date { get; set; } = "";
    public string Document { get; set; } = "";
    public string Movement { get; set; } = "";
    public string Detail1 { get; set; } = "";
    public string Detail2 { get; set; } = "";

    public decimal Q1 { get; set; }
    public decimal Q2 { get; set; }
    public decimal Q3 { get; set; }
    public decimal Q4 { get; set; }
    public decimal Price { get; set; }
    public decimal A1 { get; set; }
    public decimal A2 { get; set; }
    public decimal A3 { get; set; }

    public string HDate { get; set; } = "";
    public string HDocument { get; set; } = "";
    public string HMovement { get; set; } = "";
    public string HDetail1 { get; set; } = "";
    public string HDetail2 { get; set; } = "";
    public string HQ1 { get; set; } = "";
    public string HQ2 { get; set; } = "";
    public string HQ3 { get; set; } = "";
    public string HQ4 { get; set; } = "";
    public string HPrice { get; set; } = "";
    public string HA1 { get; set; } = "";
    public string HA2 { get; set; } = "";
    public string HA3 { get; set; } = "";
}
