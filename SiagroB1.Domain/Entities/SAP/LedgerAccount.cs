using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.SAP;

/// <summary>
/// Conta contábil do SAP B1 (OACT, plano de contas). Somente leitura — o cadastro
/// é mantido no SAP.
/// </summary>
[Table("OACT")]
public class LedgerAccount
{
    [Key]
    [Column("AcctCode")]
    public required string Code { get; set; }

    [Column("AcctName")]
    public required string Name { get; set; }

    /// <summary>OACT.Postable — 'Y' quando a conta aceita lançamento (analítica).</summary>
    [Column("Postable", TypeName = "VARCHAR(1)")]
    public string? Postable { get; set; }

    /// <summary>OACT.FrozenFor — 'Y' quando a conta está congelada (inativa).</summary>
    [Column("FrozenFor", TypeName = "VARCHAR(1)")]
    public string? FrozenFor { get; set; }
}
