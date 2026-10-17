using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// ICMS de uma UF: alíquota interna geral, FCP e regra da base do DIFAL (spec 2026-10-17 §1). A chave é a sigla da UF
/// ("SP"), igual a <see cref="Branch.StateCode"/>; nunca o código numérico do IBGE. A migration <c>SeedIcmsStateSettingsSp</c>
/// semeia SP; as demais UFs o usuário cadastra.
/// </summary>
[Table("ICMS_STATE_SETTINGS")]
public class IcmsStateSetting
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Column(TypeName = "VARCHAR(2) NOT NULL")]
    public string StateCode { get; set; } = string.Empty;

    /// <summary>Alíquota interna geral, em percentual (18 = 18 %).</summary>
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")]
    public decimal InternalRate { get; set; }

    /// <summary>FCP da UF, em percentual.</summary>
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")]
    public decimal FcpRate { get; set; }

    public DifalBaseRule DifalBaseRule { get; set; }
}
