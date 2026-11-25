namespace SiagroB1.Domain.Enums;

/// <summary>Como o documento de saída foi vinculado à invoice do SAP (GAC-1226).</summary>
public enum SapLinkType
{
    /// <summary>Vínculo sem divergência.</summary>
    Normal = 0,

    /// <summary>A quantidade confere, mas o valor da NF difere do efetivo.</summary>
    ValueDivergent = 1,

    /// <summary>Quantidade e valor da NF diferem do efetivo da carga.</summary>
    QuantityAndValueDivergent = 2,
}
