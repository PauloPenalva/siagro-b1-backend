using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>Evento da EFD-Reinf gerado (XML assinado) e seu retorno.</summary>
[Table("REINF_EVENTS")]
public class ReinfEvent
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public ReinfEventType Type { get; set; }
    [Column(TypeName = "VARCHAR(8)")] public string TaxIdRoot { get; set; } = string.Empty;
    /// <summary>AAAA-MM; vazio no R-1000.</summary>
    [Column(TypeName = "VARCHAR(7)")] public string? Period { get; set; }
    [Column(TypeName = "VARCHAR(14)")] public string? EstablishmentTaxId { get; set; }
    [Column(TypeName = "VARCHAR(20)")] public string? CardCode { get; set; }
    [Column(TypeName = "VARCHAR(14)")] public string? ProducerTaxId { get; set; }
    /// <summary>Id do evento (36 posições, começa com "ID").</summary>
    [Column(TypeName = "VARCHAR(36)")] public string EventId { get; set; } = string.Empty;
    /// <summary>indRetif: 1 original, 2 retificação.</summary>
    public int RectificationIndicator { get; set; } = 1;
    [Column(TypeName = "VARCHAR(60)")] public string? PreviousReceipt { get; set; }
    [Column(TypeName = "VARCHAR(MAX)")] public string Xml { get; set; } = string.Empty;
    public ReinfEventStatus Status { get; set; } = ReinfEventStatus.Pending;
    [Column(TypeName = "VARCHAR(60)")] public string? Receipt { get; set; }
    [Column(TypeName = "VARCHAR(MAX)")] public string? Occurrences { get; set; }
    [Column(TypeName = "DECIMAL(18,2)")] public decimal GrossValue { get; set; }
    [Column(TypeName = "DECIMAL(18,2)")] public decimal InssValue { get; set; }
    [Column(TypeName = "DECIMAL(18,2)")] public decimal RatValue { get; set; }
    [Column(TypeName = "DECIMAL(18,2)")] public decimal SenarValue { get; set; }
    [Column(TypeName = "VARCHAR(64)")] public string? ContentHash { get; set; }
    public Guid? BatchKey { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column(TypeName = "VARCHAR(100)")] public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
