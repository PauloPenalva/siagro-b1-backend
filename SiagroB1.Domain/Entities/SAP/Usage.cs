using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.SAP;

[Table("OUSG")]
public class Usage
{
    [Key]
    [Column("ID")]
    public int Code { get; set; }

    [Column("Usage",TypeName = "VARCHAR(200) NOT NULL")]
    public required string Name { get; set; }

    [Column("Descr",TypeName = "VARCHAR(200)")]
    public string? Description { get; set; }
    
    [Column("CFOPIIS", TypeName = "VARCHAR(4)")]
    public string? CfopIncomingInState { get; set; }
    
    [Column("CFOPIOS", TypeName = "VARCHAR(4)")]
    public string? CfopIncomingOutState { get; set; }
    
    [Column("CFOPII", TypeName = "VARCHAR(4)")]
    public string? CfopIncomingImport { get; set; }
    
    [Column("CFOPOIS", TypeName = "VARCHAR(4)")]
    public string? CfopOutgoingInState { get; set; }
    
    [Column("CFOPOOS", TypeName = "VARCHAR(4)")]
    public string? CfopOutgoingOutState { get; set; }
    
    [Column("CFOPOE", TypeName = "VARCHAR(4)")]
    public string? CfopOutgoingExport { get; set; }
}