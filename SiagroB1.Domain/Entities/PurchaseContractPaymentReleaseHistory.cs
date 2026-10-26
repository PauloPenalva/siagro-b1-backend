using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Histórico da liberação de pagamento por departamento (GAC-1188 parte 2): cada marcação ou
/// desmarcação gera uma linha, com o motivo informado, o usuário e a data/hora.
/// </summary>
[Table("PURCHASE_CONTRACT_PAYMENT_RELEASE_HISTORY")]
public class PurchaseContractPaymentReleaseHistory
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }

    public PaymentReleaseDepartment Department { get; set; }

    public bool Released { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string Reason { get; set; } = string.Empty;

    public DateTime ChangedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string ChangedBy { get; set; } = string.Empty;
}
