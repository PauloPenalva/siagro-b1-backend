using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.Common;

[Table("COMPANIES")]
public class Company
{
    [Key]
    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    public required string Code { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string CompanyName { get; set; }

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public required string ConnectionString { get; set; }
    
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public string? SapCompanyDb { get; set; }
}