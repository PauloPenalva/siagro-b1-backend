using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Diferença entre o que a carga movimentou fisicamente (efetivo) e o que a NF do SAP vinculada cobre (GAC-1226).
/// Há no máximo UM registro aberto por documento de saída (índice único filtrado).
/// </summary>
[Table("SALES_INVOICE_DIVERGENCES")]
public class SalesInvoiceDivergence
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Key { get; set; }

    public Guid SalesInvoiceKey { get; set; }
    public SalesInvoice? SalesInvoice { get; set; }

    public Guid ShipmentLoadKey { get; set; }

    public SapLinkType LinkType { get; set; }

    /// <summary>Quantidade efetiva da carga (QE).</summary>
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal EffectiveQuantity { get; set; }

    /// <summary>Quantidade da NF do SAP (QNF).</summary>
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal InvoiceQuantity { get; set; }

    /// <summary>QE − QNF, com sinal.</summary>
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal QuantityDifference { get; set; }

    /// <summary>Valor efetivo (VE); nulo quando não há preço em R$.</summary>
    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal? EffectiveValue { get; set; }

    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal InvoiceValue { get; set; }

    /// <summary>VE − VNF.</summary>
    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal? ValueDifference { get; set; }

    public SalesInvoiceDivergenceStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? CreatedBy { get; set; }

    public DateTime? VoidedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? VoidedBy { get; set; }

    [Column(TypeName = "NVARCHAR(200)")]
    public string? VoidReason { get; set; }
}
