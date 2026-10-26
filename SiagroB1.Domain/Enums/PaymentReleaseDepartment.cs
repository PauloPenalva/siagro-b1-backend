namespace SiagroB1.Domain.Enums;

/// <summary>Departamentos que liberam o pagamento do contrato de compra (GAC-1188).</summary>
public enum PaymentReleaseDepartment
{
    Stock = 0,
    Legal = 1,
    Fiscal = 2,
    Royalties = 3,
    Financial = 4,
    Store = 5,
    Board = 6,
}
