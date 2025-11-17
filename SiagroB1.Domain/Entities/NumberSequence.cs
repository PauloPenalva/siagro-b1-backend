using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

[Table("NUMBER_SEQUENCE")]
[Index("BranchKey", ["TransactionCode","Serie"], IsUnique = true)]
public class NumberSequence
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }
    
    [Column(TypeName = "VARCHAR(14) NOT NULL")]
    [ForeignKey(nameof(Branch))]
    public required string BranchKey { get; set; }
    public virtual Branch? Branch { get; set; }
    
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public TransactionCode TransactionCode { get; set; }

    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string Serie { get; set; }

    public int FirstNumber { get; set; } = 0;

    public int LastNumber { get; set; } = 0;

    public int NextNumber { get; set; } = 0;
}