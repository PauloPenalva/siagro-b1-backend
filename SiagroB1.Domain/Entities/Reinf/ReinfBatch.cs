using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>Lote de eventos enviado à EFD-Reinf (requisição, protocolo e retorno).</summary>
[Table("REINF_BATCHES")]
public class ReinfBatch
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    [Column(TypeName = "VARCHAR(8)")] public string TaxIdRoot { get; set; } = string.Empty;
    public ReinfEnvironment Environment { get; set; }
    [Column(TypeName = "VARCHAR(60)")] public string? Protocol { get; set; }
    public ReinfBatchStatus Status { get; set; } = ReinfBatchStatus.Sent;
    [Column(TypeName = "VARCHAR(MAX)")] public string RequestXml { get; set; } = string.Empty;
    [Column(TypeName = "VARCHAR(MAX)")] public string? ResponseXml { get; set; }
    [Column(TypeName = "VARCHAR(MAX)")] public string? Error { get; set; }
    public DateTime SentAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int Attempts { get; set; }
}
