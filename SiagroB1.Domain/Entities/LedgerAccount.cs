using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Conta contábil mantida localmente (modo STANDALONE).
/// Em modo SAPB1 esta tabela fica vazia e o dado vem de OACT — ver
/// <see cref="SAP.LedgerAccount"/>.
/// </summary>
[Table("LEDGER_ACCOUNTS")]
public class LedgerAccount
{
    [Key]
    [Column(TypeName = "VARCHAR(20) NOT NULL")]
    public required string Code { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    /// <summary>
    /// Anulável porque o SAP não fornece esta classificação: em modo SAPB1 o campo
    /// vem vazio. A obrigatoriedade no cadastro local é validada no serviço.
    /// </summary>
    public LedgerAccountType? Type { get; set; }

    /// <summary>Conta analítica (aceita lançamento) x sintética (só agrupa).</summary>
    public bool AllowsPosting { get; set; } = true;

    public bool Inactive { get; set; }
}
