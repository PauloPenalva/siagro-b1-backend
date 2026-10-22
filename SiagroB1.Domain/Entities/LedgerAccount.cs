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

    /// <summary>Aceita centro de custo (CT1_ACCUST).</summary>
    public bool AcceptsCostCenter { get; set; } = true;

    /// <summary>Exige centro de custo (CT1_CCOBRG).</summary>
    public bool RequiresCostCenter { get; set; }

    /// <summary>Aceita item contábil (CT1_ACITEM).</summary>
    public bool AcceptsAccountingItem { get; set; } = true;

    /// <summary>Exige item contábil (CT1_ITOBRG).</summary>
    public bool RequiresAccountingItem { get; set; }

    /// <summary>Conta superior (CT1_CTASUP). Só conta sintética pode ser superior.</summary>
    [Column(TypeName = "VARCHAR(20)")]
    public string? ParentCode { get; set; }

    /// <summary>Natureza devedora/credora (CT1_NORMAL).</summary>
    public LedgerAccountNature? Nature { get; set; }

    /// <summary>Conta de apuração do resultado (CT1_CTALP); só em conta de receita/despesa.</summary>
    [Column(TypeName = "VARCHAR(20)")]
    public string? ResultClosingAccountCode { get; set; }

    /// <summary>Código no plano de contas referencial da RFB.</summary>
    [Column(TypeName = "VARCHAR(30)")]
    public string? ReferentialCode { get; set; }

    /// <summary>Bloqueio por data (CT1_DTBLIN/CT1_DTBLFI). Fim vazio = bloqueada a partir do início.</summary>
    public DateTime? BlockedFrom { get; set; }

    public DateTime? BlockedTo { get; set; }
}
