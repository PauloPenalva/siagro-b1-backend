namespace SiagroB1.Domain.Enums;

/// <summary>Situação do cálculo de preço de uma linha da tabela de preços.</summary>
public enum MarketPriceStatus
{
    Ok = 0,
    MissingQuote = 1,
    MissingFx = 2,
    MissingFreight = 3,
    InvalidSetup = 4,
}
