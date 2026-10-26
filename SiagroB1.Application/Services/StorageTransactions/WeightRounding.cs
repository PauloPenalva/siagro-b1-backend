namespace SiagroB1.Application.Services.StorageTransactions;

/// <summary>
/// GAC-1205: pesos dos romaneios de armazenagem em kg inteiro, arredondamento "Round Half Up"
/// (menor que 0,5 desce, igual ou maior sobe) — evita contratos e saldos de lote com casas decimais.
/// </summary>
public static class WeightRounding
{
    public static decimal ToKilograms(decimal value) =>
        Math.Round(value, 0, MidpointRounding.AwayFromZero);
}
