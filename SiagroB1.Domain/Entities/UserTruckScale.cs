using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Balança que um usuário opera em cada etapa da pesagem. Onde há uma balança só, a mesma é
/// informada nas duas finalidades.
///
/// Sem FK para USERS de propósito: aquela tabela vive no banco COMMON e esta no banco da empresa.
/// A chave é o Username, que é o que a API tem em mãos (User.Identity.Name) e o mesmo padrão de
/// WEIGHING_TICKETS.FirstWeighUsername.
/// </summary>
[Table("USER_TRUCK_SCALES")]
[Index(nameof(Username), nameof(Purpose), IsUnique = true)]
public class UserTruckScale
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    [MaxLength(50)]
    public required string Username { get; set; }

    // Sem TypeName de propósito: TRUCK_SCALES.Code também não declara o seu e é nvarchar(450).
    // Declarar VARCHAR(11) aqui faz o SQL Server recusar a FK por diferença de tipo.
    [ForeignKey(nameof(TruckScale))]
    public required string TruckScaleCode { get; set; }

    public virtual TruckScale? TruckScale { get; set; }

    public required WeighingScalePurpose Purpose { get; set; }
}
