namespace SiagroB1.Domain.Enums;

/// <summary>Regime da contribuição social do produtor rural pessoa física.</summary>
public enum RuralContributionScheme
{
    /// <summary>Incide sobre a comercialização da produção (INSS + RAT + SENAR).</summary>
    Standard = 1,

    /// <summary>Produtor optante pela contribuição sobre a folha de pagamento.</summary>
    PayrollOption = 2,
}
