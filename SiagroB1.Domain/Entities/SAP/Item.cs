using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.SAP;

[Table("OITM")]
public class Item
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required string ItemCode { get; set; }

    public required string ItemName { get; set; }
}