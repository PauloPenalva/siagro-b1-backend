namespace SiagroB1.Domain.Enums;

/// <summary>
/// Tipo da natureza de operação: em que documento ela pode ser usada. Decide também em quais
/// colunas o CFOP é gravado (as de saída ou as de entrada).
/// </summary>
public enum UsageDirection
{
    Outgoing = 1,
    Incoming = 2,
}
