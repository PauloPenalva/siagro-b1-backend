namespace SiagroB1.Domain.Enums;

/// <summary>
/// Código de Regime Tributário (CRT) da NF-e. Decide o código de ICMS da linha:
/// Simples Nacional e MEI usam CSOSN; os demais usam CST.
/// </summary>
public enum TaxRegime
{
    SimplesNacional = 1,
    SimplesNacionalExcess = 2,
    Normal = 3,
    Mei = 4,
}
