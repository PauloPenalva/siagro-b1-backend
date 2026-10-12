using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// OBSOLETA desde 12/10/2026: o armazém é o parceiro de negócio com <c>QryGroup23 = 'Y'</c> nos dois
/// modos. A migration <c>MigrateWarehousesToBusinessPartners</c> converteu as linhas; a tabela fica
/// só até ser apagada numa etapa posterior. Não leia nem grave aqui.
/// </summary>
[Table("WAREHOUSES")]
public class Warehouse 
{
    [Key]
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string Code { get; set; }
    
    [Required(ErrorMessage = "Code is mandatory.")]
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }
    
    /// <summary>
    /// Brazilian CNPJ
    /// </summary>
    [Column("TAXID", TypeName = "VARCHAR(14) NOT NULL")]
    public string? TaxId { get; set; }
    
}