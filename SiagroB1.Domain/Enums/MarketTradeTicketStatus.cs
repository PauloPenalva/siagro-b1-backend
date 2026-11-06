namespace SiagroB1.Domain.Enums;

/// <summary>Situação da boleta de compra do Mercado.</summary>
public enum MarketTradeTicketStatus
{
    Pending = 0,
    PendingBoard = 1,
    Approved = 2,
    Rejected = 3,
    Canceled = 4,
}
