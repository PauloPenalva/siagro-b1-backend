using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// País — tabela de referência, somente leitura, semeada pela migration. <see cref="Code"/> é o
/// ISO-2 que o endereço do parceiro guarda em <c>Country</c>; <see cref="BacenCode"/> é o
/// <c>cPais</c> da NF-e (hoje a NF-e ainda usa Brasil fixo).
/// </summary>
[Table("COUNTRIES")]
[Index(nameof(BacenCode), IsUnique = true)]
public class Country
{
    [Key]
    [Column(TypeName = "VARCHAR(2)")]
    public required string Code { get; set; }

    [Column(TypeName = "NVARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    public required string BacenCode { get; set; }
}
