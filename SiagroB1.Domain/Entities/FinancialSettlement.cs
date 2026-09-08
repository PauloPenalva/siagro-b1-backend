using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Ledger de baixas: uma linha por evento financeiro, com valor ASSINADO e origem enumerada.
/// INSERT-ONLY — é dinheiro, nunca se apaga; estorno é linha negativa apontando para a baixa
/// original por <see cref="ReversedSettlementKey"/>.
///
/// É deste ledger que FinancialDocument.SettledAmount é derivado, sempre por soma.
/// </summary>
[Table("FINANCIAL_SETTLEMENTS")]
[Index(nameof(FinancialDocumentKey))]
[Index(nameof(SettlementDate))]
[Index(nameof(FinancialAccountCode))]
public class FinancialSettlement : BaseEntity
{
    [ForeignKey(nameof(FinancialDocument))]
    public Guid FinancialDocumentKey { get; set; }

    public virtual FinancialDocument? FinancialDocument { get; set; }

    /// <summary>
    /// Nulável para as origens não-caixa das Fases 2 e 3 (abatimento pelo documento fiscal,
    /// amortização de adiantamento, encontro de contas), que não passam por conta financeira.
    /// Na Fase 1 o guard a exige.
    /// </summary>
    [Column(TypeName = "VARCHAR(10)")]
    [ForeignKey(nameof(FinancialAccount))]
    public string? FinancialAccountCode { get; set; }

    public virtual FinancialAccount? FinancialAccount { get; set; }

    /// <summary>Data-caixa EFETIVA, distinta de CreatedAt (quando a linha foi digitada).</summary>
    public DateTime SettlementDate { get; set; }

    /// <summary>ASSINADO: baixa positiva, estorno negativo.</summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal Amount { get; set; }

    /// <remarks>
    /// Juros, multa e desconto NÃO entram em <see cref="Amount"/>: Amount é o que abate o
    /// documento; os três compõem o valor efetivamente pago ou recebido. Somá-los faria o
    /// documento liquidar por valor diferente do devido.
    /// </remarks>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal InterestAmount { get; set; }

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal FineAmount { get; set; }

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal DiscountAmount { get; set; }

    public FinancialSettlementOrigin Origin { get; set; }

    /// <summary>
    /// A baixa que esta linha estorna. SEM FK: auto-relação para a mesma tabela deixaria a
    /// convenção do EF ambígua. O índice único filtrado impede estornar a mesma baixa duas vezes.
    /// </summary>
    public Guid? ReversedSettlementKey { get; set; }

    [Column(TypeName = "VARCHAR(50)")]
    public string? DocumentReference { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Notes { get; set; }
}
