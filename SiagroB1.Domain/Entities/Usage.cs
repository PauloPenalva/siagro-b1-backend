using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// IDENTIDADE FISCAL da natureza de operação (nome e CFOP), mantida localmente no modo
/// STANDALONE. Em modo SAPB1 esta tabela fica vazia e a identidade vem de <c>OUSG</c> —
/// ver <see cref="SAP.Usage"/>.
///
/// O EFEITO de negócio (saldo, valor, obrigatoriedades) NÃO está aqui: mora em
/// <see cref="UsageEffect"/>, porque em SAPB1 a identidade é do ERP e o efeito continua
/// sendo do Siagro. Ver o comentário daquela entidade.
///
/// A chave é INT, e não string, para ser a mesma dos dois mundos: aqui é gerada, e em SAPB1
/// é o <c>OUSG.ID</c>.
/// </summary>
[Table("USAGES")]
public class Usage
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Code { get; set; }

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public required string Name { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? Description { get; set; }

    /// <summary>CFOP de saída dentro do estado.</summary>
    [Column(TypeName = "VARCHAR(4)")]
    public string? CfopOutgoingInState { get; set; }

    /// <summary>CFOP de saída interestadual.</summary>
    [Column(TypeName = "VARCHAR(4)")]
    public string? CfopOutgoingOutState { get; set; }

    public bool Inactive { get; set; }
}
