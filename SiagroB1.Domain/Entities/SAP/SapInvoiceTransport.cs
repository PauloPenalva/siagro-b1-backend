using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.SAP;

/// <summary>Dados de transporte da A/R Invoice do SAP (tabela INV12). Somente leitura.</summary>
[Table("INV12")]
public class SapInvoiceTransport
{
    [Key]
    [Column("DocEntry")]
    public int DocEntry { get; set; }

    [Column("Carrier", TypeName = "NVARCHAR(15)")]
    public string? Carrier { get; set; }

    [Column("Incoterms", TypeName = "NVARCHAR(3)")]
    public string? Incoterms { get; set; }

    public SapInvoice? Invoice { get; set; }
}
