namespace SiagroB1.Domain.Enums;

/// <summary>Enquadramento do produtor rural para a contribuição previdenciária (Funrural).</summary>
public enum RuralProducerType
{
    /// <summary>Pessoa física.</summary>
    Individual = 1,

    /// <summary>Segurado especial.</summary>
    SpecialInsured = 2,

    /// <summary>Pessoa jurídica (agroindústria / produtor PJ).</summary>
    LegalEntity = 3,
}
