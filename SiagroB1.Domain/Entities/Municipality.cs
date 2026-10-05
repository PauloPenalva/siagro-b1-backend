using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Município do IBGE — <c>cMun</c>/<c>xMun</c> da NF-e. Tabela de referência, somente leitura,
/// semeada pela migration. Os 2 primeiros dígitos do código são o <c>cUF</c>.
/// </summary>
[Table("MUNICIPALITIES")]
public class Municipality
{
    [Key]
    [Column(TypeName = "VARCHAR(7)")]
    public required string Code { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    [Column(TypeName = "VARCHAR(2) NOT NULL")]
    public required string StateAbbreviation { get; set; }
}
