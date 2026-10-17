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

    /// <summary>String de conexão do banco de negócio desta empresa (informada no cadastro, como no SAP B1).</summary>
    [Column(TypeName = "VARCHAR(500) NOT NULL")]
    public required string ConnectionString { get; set; }

    /// <summary>Reservado: banco da empresa no SAP B1. Não é usado hoje (o modo SAPB1 continua por deploy).</summary>
    [Column(TypeName = "VARCHAR(100)")]
    public string? SapCompanyDb { get; set; }
}