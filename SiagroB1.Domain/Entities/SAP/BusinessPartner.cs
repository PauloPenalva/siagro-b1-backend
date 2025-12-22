using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities.SAP;

[Table("OCRD")]
public class BusinessPartner
{
    [Key]
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string CardCode { get; set; }

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public required string CardName { get; set; }
    
    /// <summary>
    /// BPType
    /// </summary>
    [Column(TypeName = "VARCHAR(1) NOT NULL")]
    public string CardType { get; set; }
    
}