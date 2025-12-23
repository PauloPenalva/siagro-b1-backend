using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Domain.Shared.Base;

public abstract class MasterEntity
{
    public Guid? DocNumberKey { get; set; }
    public virtual DocNumber? DocNumber { get; set; }
    
    [Column(TypeName = "VARCHAR(14) NOT NULL")]
    public string? BranchCode { get; set; }
    
    public virtual Branch? Branch { get; set; }
    
    [Key]
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public string? Code { get; set; }
    
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int RowId { get; set; }
    
    public DateTime? CreatedAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100)")]
    public string? CreatedBy { get; set; } = string.Empty;

    public DateTime? UpdatedAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100)")]
    public string? UpdatedBy { get; set; } = string.Empty;
    
    public DateTime? ApprovedAt { get; set; }
    
    [Column(TypeName = "VARCHAR(100)")]
    public string? ApprovedBy { get; set; } = string.Empty;
    
    public DateTime? CanceledAt { get; set; }
    
    [Column(TypeName = "VARCHAR(100)")]
    public string? CanceledBy { get; set; } = string.Empty;
    
}