using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.Common;

/// <summary>Logo da empresa mostrado na home quando o painel está desligado.</summary>
[Table("COMPANY_LOGOS")]
public class CompanyLogo
{
    [Key]
    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    public required string CompanyCode { get; set; }

    public required byte[] Content { get; set; }

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string ContentType { get; set; }
}
