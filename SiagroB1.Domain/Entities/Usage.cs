using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

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
/// A TRIBUTAÇÃO da NF-e STANDALONE (tipo, ICMS dentro/fora, PIS/COFINS, IBS/CBS e as flags de
/// estoque fiscal e financeiro) também mora aqui: ela só existe no STANDALONE, onde esta tabela
/// é a dona da natureza. Em SAPB1 a tributação é do SAP e esta tabela fica vazia.
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

    /// <summary>Tipo da natureza. As existentes nasceram de saída — é o default da migration.</summary>
    public UsageDirection Direction { get; set; } = UsageDirection.Outgoing;

    /// <summary>CFOP de entrada dentro do estado (natureza de Entrada).</summary>
    [Column(TypeName = "VARCHAR(4)")]
    public string? CfopIncomingInState { get; set; }

    /// <summary>CFOP de entrada interestadual (natureza de Entrada).</summary>
    [Column(TypeName = "VARCHAR(4)")]
    public string? CfopIncomingOutState { get; set; }

    /// <summary>Texto da natureza que vai na NF-e (ide/natOp). Vazio = a NF-e usa o <see cref="Name"/>.</summary>
    [Column(TypeName = "VARCHAR(60)")]
    public string? InvoiceOperationText { get; set; }

    /// <summary>Informações complementares padrão da NF-e (infCpl) — lidas pela emissão.</summary>
    [Column(TypeName = "VARCHAR(2000)")]
    public string? DefaultAdditionalInfo { get; set; }

    /// <summary>
    /// "Movimenta estoque" — estoque FISCAL (saldo por produto/filial movido pelos documentos
    /// fiscais, base do Bloco H). Nada a ver com o saldo gerencial de grãos. Sem efeito por ora:
    /// é copiado para a linha do documento para o futuro estoque fiscal ler o que valia na emissão.
    /// </summary>
    public bool MovesFiscalInventory { get; set; }

    /// <summary>
    /// "Gera financeiro" — conta a receber (saída) ou a pagar (entrada). Sem efeito por ora:
    /// consumido pela Fase 2 do financeiro.
    /// </summary>
    public bool CreatesFinancialDocument { get; set; }

    // ICMS — bloco "dentro do estado". CST vale para CRT 2/3, CSOSN para CRT 1/4.
    [Column(TypeName = "VARCHAR(3)")] public string? IcmsInStateCst { get; set; }
    [Column(TypeName = "VARCHAR(3)")] public string? IcmsInStateCsosn { get; set; }
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? IcmsInStateRate { get; set; }
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? IcmsInStateBaseReduction { get; set; }
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? IcmsInStateDeferral { get; set; }
    [Column(TypeName = "VARCHAR(10)")] public string? IcmsInStateBenefitCode { get; set; }

    // ICMS — bloco "fora do estado". Sem alíquota: ela é automática (7%/12%/4%).
    [Column(TypeName = "VARCHAR(3)")] public string? IcmsOutStateCst { get; set; }
    [Column(TypeName = "VARCHAR(3)")] public string? IcmsOutStateCsosn { get; set; }
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? IcmsOutStateBaseReduction { get; set; }
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? IcmsOutStateDeferral { get; set; }
    [Column(TypeName = "VARCHAR(10)")] public string? IcmsOutStateBenefitCode { get; set; }

    // PIS/COFINS.
    [Column(TypeName = "VARCHAR(2)")] public string? PisCst { get; set; }
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? PisRate { get; set; }
    [Column(TypeName = "VARCHAR(2)")] public string? CofinsCst { get; set; }
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? CofinsRate { get; set; }

    /// <summary>Tese do STF (Tema 69): a base do PIS/COFINS é o valor menos o ICMS destacado.</summary>
    public bool ExcludeIcmsFromPisCofinsBase { get; set; }

    // IBS/CBS (reforma tributária). As alíquotas vêm da tabela IBS_CBS_RATES por vigência.
    [Column(TypeName = "VARCHAR(3)")] public string? IbsCbsCst { get; set; }
    [Column(TypeName = "VARCHAR(6)")] public string? IbsCbsClassCode { get; set; }
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? IbsRateReduction { get; set; }
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? CbsRateReduction { get; set; }

    /// <summary>
    /// Natureza de ENTRADA da NF-e de devolução de uma venda feita com esta natureza (só natureza de
    /// Saída, só STANDALONE). A devolução criada pelo "Devolver" herda esta natureza em cada linha.
    /// </summary>
    public int? ReturnUsageCode { get; set; }

    [ForeignKey(nameof(ReturnUsageCode))]
    public Usage? ReturnUsage { get; set; }
}
