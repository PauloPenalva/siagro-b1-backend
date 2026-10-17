namespace SiagroB1.Domain.Enums;

/// <summary>Destinação da mercadoria numa natureza de entrada (decide, por exemplo, se há DIFAL).</summary>
public enum PurchasePurpose
{
    /// <summary>Revenda ou insumo (padrão).</summary>
    ResaleOrInput = 0,

    /// <summary>Uso e consumo.</summary>
    UseAndConsumption = 1,

    /// <summary>Ativo imobilizado.</summary>
    FixedAsset = 2,
}
