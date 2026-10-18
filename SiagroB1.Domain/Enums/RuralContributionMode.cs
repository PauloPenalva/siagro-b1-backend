namespace SiagroB1.Domain.Enums;

/// <summary>Como a natureza de operação trata a contribuição social do produtor rural na compra.</summary>
public enum RuralContributionMode
{
    /// <summary>A natureza não retém contribuição social.</summary>
    None = 0,

    /// <summary>Retém INSS, RAT e SENAR, conforme as alíquotas vigentes do produtor.</summary>
    Full = 1,

    /// <summary>Retém só o SENAR (decisão judicial).</summary>
    SenarOnly = 2,
}
