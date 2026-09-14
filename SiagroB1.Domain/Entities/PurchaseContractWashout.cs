using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Washout: desistência registrada de parte ou de todo o volume NÃO entregue de um contrato de
/// compra. Tira o volume do saldo, reduz o provisório a pagar da fixação e gera um título a
/// receber do produtor com a diferença de mercado e a multa.
///
/// Registro filho do contrato, no molde de <see cref="PurchaseContractPriceFixation"/> — e não um
/// contrato de venda "WO" (decisão de 14/09/2026, GAC-1164).
/// </summary>
[Table("PURCHASE_CONTRACTS_WASHOUTS")]
[Index(nameof(PurchaseContractKey), nameof(Sequence), IsUnique = true)]
public class PurchaseContractWashout : BaseEntity
{
    public Guid PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }

    /// <summary>Sequencial por contrato (1, 2, 3…). A tela exibe "WO-{Sequence}".</summary>
    public int Sequence { get; set; }

    /// <summary>
    /// Fixação cujo preço vale para o volume fixado. Nula quando <see cref="FixedVolume"/> é zero —
    /// washout só de volume não fixado não prende nenhuma fixação.
    /// </summary>
    public Guid? PriceFixationKey { get; set; }
    public virtual PurchaseContractPriceFixation? PriceFixation { get; set; }

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal FixedVolume { get; set; }

    /// <summary>Só em contrato a fixar (PAF); no preço fixo é sempre zero.</summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal UnfixedVolume { get; set; }

    /// <summary>Cópia do FixationPrice da fixação no momento do registro.</summary>
    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal ContractPrice { get; set; }

    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal MarketPrice { get; set; }

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal PenaltyAmount { get; set; }

    /// <summary>Valor do título a receber. Ver <see cref="CalculateAmount"/>.</summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal Amount { get; set; }

    /// <summary>Vencimento do título; obrigatório quando <see cref="Amount"/> é maior que zero.</summary>
    public DateTime? DueDate { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Reason { get; set; }

    public PurchaseContractWashoutStatus Status { get; set; } = PurchaseContractWashoutStatus.InApproval;

    [Column(TypeName = "VARCHAR(500)")]
    public string? ApprovalComments { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? ReversalReason { get; set; }

    /// <summary>Título a receber gerado na aprovação.</summary>
    public Guid? FinancialDocumentKey { get; set; }
    public virtual FinancialDocument? FinancialDocument { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }

    /// <summary>
    /// <c>round(max(mercado − contrato, 0) × volume fixado, 2) + multa</c>. Mercado abaixo do
    /// contrato vira zero: o produtor que desiste nunca recebe crédito por isso.
    /// </summary>
    public static decimal CalculateAmount(
        decimal marketPrice, decimal contractPrice, decimal fixedVolume, decimal penaltyAmount) =>
        decimal.Round(Math.Max(marketPrice - contractPrice, 0m) * fixedVolume, 2, MidpointRounding.ToEven)
        + decimal.Round(penaltyAmount, 2, MidpointRounding.ToEven);
}
