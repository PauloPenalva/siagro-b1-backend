namespace SiagroB1.Domain.Enums;

/// <summary>
/// Situação da entrada de compra em armazenagem própria. O par de romaneios
/// (Purchase/Receipt) nasce Confirmed e só muda no estorno.
/// </summary>
public enum StorageEntryTransactionStatus
{
    Confirmed = 0,
    Cancelled = 1,
}
