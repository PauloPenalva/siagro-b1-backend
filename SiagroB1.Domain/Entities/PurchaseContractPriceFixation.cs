using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("PURCHASE_CONTRACTS_PRICE_FIXATIONS")]
public class PurchaseContractPriceFixation : BaseEntity
{
    public Guid? PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }

    public DateTime? FixationDate { get; set; } = DateTime.Now;

    /// <summary>Data de vencimento financeiro da parcela fixada.</summary>
    public DateTime? FinancialDueDate { get; set; }

    /// <summary>Dados para pagamento informados ao registrar a fixação (banco, chave PIX, etc.).</summary>
    [Column(TypeName = "VARCHAR(1000)")]
    public string? PaymentDetails { get; set; }

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal FreightCost { get; set; }

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal FixationVolume { get; set; } = 0;

    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal FixationPrice { get; set; } = 0;

    public PriceFixationStatus Status { get; set; } = PriceFixationStatus.InApproval;

    [Column(TypeName = "VARCHAR(500)")]
    public string? ApprovalComments { get; set; }
}
