using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Efeito de negócio de uma natureza de operação: o que ela faz com o saldo e o valor do
/// contrato, e o que passa a ser obrigatório no documento.
///
/// Mora numa tabela SEPARADA da identidade fiscal porque as duas têm DONOS diferentes. A
/// identidade (nome, CFOP) vem de <see cref="Usage"/> em STANDALONE e de <c>OUSG</c> em
/// SAPB1 — nesse modo o Siagro não pode escrevê-la. Já o efeito é sempre do Siagro: o SAP não
/// tem conceito de "esta natureza devolve saldo ao contrato de venda". Juntar os dois na
/// mesma linha era o que deixava o modo SAPB1 sem lugar para gravar o efeito, e por isso
/// nenhum efeito disparava lá.
///
/// A chave é o MESMO int dos dois mundos: <see cref="Usage.Code"/> em STANDALONE e
/// <c>OUSG.ID</c> em SAPB1. Sem FK, justamente para atender aos dois.
/// </summary>
[Table("USAGE_EFFECTS")]
public class UsageEffect
{
    /// <summary>
    /// NÃO é gerada: vem de fora (Usage.Code ou OUSG.ID). Sem o DatabaseGenerated.None o EF
    /// criaria a coluna como IDENTITY e o banco recusaria a gravação com o código do SAP.
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int UsageCode { get; set; }

    public ContractBalanceEffect ContractBalanceEffect { get; set; }

    public ContractValueEffect ContractValueEffect { get; set; }

    public bool RequiresContract { get; set; }

    public bool RequiresQuantity { get; set; } = true;

    public bool RequiresWeight { get; set; }

    /// <summary>
    /// Natureza aplicada ao documento nascido de FATURAMENTO DE ROMANEIO, que não escolhe
    /// natureza em tela. Invariante de natureza única mantida pelo serviço, não pelo banco.
    /// </summary>
    public bool IsDefault { get; set; }
}
