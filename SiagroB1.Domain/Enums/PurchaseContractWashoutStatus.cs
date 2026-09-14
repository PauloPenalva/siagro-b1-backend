namespace SiagroB1.Domain.Enums;

/// <summary>
/// Ciclo do washout do contrato de compra. InApproval e Approved RESERVAM volume
/// (<c>PurchaseContract.WashedOutVolume</c>); Rejected e Reversed o devolvem. Os valores são
/// contrato com o banco e com o frontend — não renumere.
/// </summary>
public enum PurchaseContractWashoutStatus
{
    InApproval = 0,
    Approved = 1,
    Rejected = 2,
    Reversed = 3
}
