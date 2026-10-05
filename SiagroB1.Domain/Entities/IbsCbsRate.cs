using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Alíquotas de IBS e CBS a partir de uma data. São nacionais e mudam por ano durante a
/// transição da reforma tributária; a virada de ano é um cadastro só, em vez de editar cada
/// natureza. Vale para um documento a linha com o maior <see cref="StartDate"/> menor ou igual
/// à data de emissão. Só STANDALONE.
/// </summary>
[Table("IBS_CBS_RATES")]
[Index(nameof(StartDate), IsUnique = true)]
public class IbsCbsRate
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Key { get; set; }

    /// <summary><c>DateOnly</c> ⇒ <c>Edm.Date</c>: sem fuso, a vigência não escorrega de dia.</summary>
    [Column(TypeName = "DATE")]
    public DateOnly StartDate { get; set; }

    [Column(TypeName = "DECIMAL(7,4)")]
    public decimal CbsRate { get; set; }

    [Column(TypeName = "DECIMAL(7,4)")]
    public decimal IbsStateRate { get; set; }

    [Column(TypeName = "DECIMAL(7,4)")]
    public decimal IbsMunicipalRate { get; set; }
}
