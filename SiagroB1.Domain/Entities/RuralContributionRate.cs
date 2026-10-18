using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Alíquotas da contribuição social do produtor rural (INSS, RAT e SENAR) a partir de uma data,
/// por regime. São nacionais; a virada de vigência é um cadastro só, em vez de editar cada
/// natureza de operação. Vale para um documento a linha do regime com o maior
/// <see cref="StartDate"/> menor ou igual à data de emissão. Só STANDALONE.
/// </summary>
[Table("RURAL_CONTRIBUTION_RATES")]
[Index(nameof(Scheme), nameof(StartDate), IsUnique = true)]
public class RuralContributionRate
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Key { get; set; }

    /// <summary><c>DateOnly</c> ⇒ <c>Edm.Date</c>: sem fuso, a vigência não escorrega de dia.</summary>
    [Column(TypeName = "DATE")]
    public DateOnly StartDate { get; set; }

    public RuralContributionScheme Scheme { get; set; }

    [Column(TypeName = "DECIMAL(7,4)")]
    public decimal InssRate { get; set; }

    [Column(TypeName = "DECIMAL(7,4)")]
    public decimal RatRate { get; set; }

    [Column(TypeName = "DECIMAL(7,4)")]
    public decimal SenarRate { get; set; }
}
