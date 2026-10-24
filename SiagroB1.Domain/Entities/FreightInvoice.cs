using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>Fatura de frete: aglutina CT-e autorizados de um tomador em um único título a receber. Só STANDALONE.</summary>
[Table("FREIGHT_INVOICES")]
public class FreightInvoice
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    [Column(TypeName = "VARCHAR(14)")] public required string BranchCode { get; set; }
    public Guid DocNumberKey { get; set; }
    [Column(TypeName = "VARCHAR(20)")] public required string Code { get; set; }

    [Column(TypeName = "VARCHAR(15)")] public required string TakerCardCode { get; set; }
    /// <summary>Nome do tomador gravado na inclusão (snapshot): as listas exibem sem consultar o cadastro.</summary>
    [Column(TypeName = "VARCHAR(100)")] public string? TakerName { get; set; }

    public DateTime IssueDate { get; set; }
    public DateTime? DueDate { get; set; }
    public int? PaymentConditionCode { get; set; }

    [Column(TypeName = "DECIMAL(18,2)")] public decimal TotalAmount { get; set; }
    [Column(TypeName = "VARCHAR(2000)")] public string? Observation { get; set; }

    public FreightInvoiceStatus Status { get; set; } = FreightInvoiceStatus.Draft;
    public DateTime? IssuedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")] public required string CreatedBy { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }

    public virtual ICollection<FreightInvoiceItem> Items { get; set; } = new List<FreightInvoiceItem>();
}
