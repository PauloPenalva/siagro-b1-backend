using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Liberação de um departamento para o pagamento do contrato de COMPRA (GAC-1188). Há no máximo
/// uma linha por contrato e departamento (índice único). A ausência de linha significa "não
/// liberado".
/// </summary>
[Table("PURCHASE_CONTRACT_PAYMENT_RELEASES")]
public class PurchaseContractPaymentRelease
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }

    public PaymentReleaseDepartment Department { get; set; }

    public bool Released { get; set; }

    public DateTime? ReleasedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? ReleasedBy { get; set; }
}
