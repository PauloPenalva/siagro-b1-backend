using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Washout do contrato de venda (spec 2026-10-15): desistência registrada de parte ou de todo o volume NÃO
/// faturado. Tira o volume do saldo, reduz o provisório a receber da fixação e gera um título a receber do
/// cliente com a diferença de mercado e a multa. Espelho de <see cref="PurchaseContractWashout"/>.
/// </summary>
[Table("SALES_CONTRACTS_WASHOUTS")]
[Index(nameof(SalesContractKey), nameof(Sequence), IsUnique = true)]
public class SalesContractWashout : BaseEntity
{
    public Guid SalesContractKey { get; set; }
    public virtual SalesContract? SalesContract { get; set; }

    /// <summary>Sequencial por contrato (1, 2, 3…). A tela exibe "WO-{Sequence}".</summary>
    public int Sequence { get; set; }

    /// <summary>Fixação cujo preço vale para o volume fixado. Nula quando <see cref="FixedVolume"/> é zero.</summary>
    public Guid? PriceFixationKey { get; set; }
    public virtual SalesContractPriceFixation? PriceFixation { get; set; }

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

    public SalesContractWashoutStatus Status { get; set; } = SalesContractWashoutStatus.InApproval;

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
    /// <c>round(max(contrato − mercado, 0) × volume fixado, 2) + multa</c>: sinal INVERTIDO em relação à compra. O
    /// cliente que desiste paga quando o mercado caiu abaixo do contrato; mercado acima vira zero e cobra-se só a
    /// multa — o cliente nunca recebe crédito. Mesma ordem de parâmetros de <see cref="PurchaseContractWashout.CalculateAmount"/>.
    /// </summary>
    public static decimal CalculateAmount(
        decimal marketPrice, decimal contractPrice, decimal fixedVolume, decimal penaltyAmount) =>
        decimal.Round(Math.Max(contractPrice - marketPrice, 0m) * fixedVolume, 2, MidpointRounding.ToEven)
        + decimal.Round(penaltyAmount, 2, MidpointRounding.ToEven);
}
