using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.SAP;

[Table("OCRD")]
public class BusinessPartner
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required string CardCode { get; set; }

    public required string CardName { get; set; }
}