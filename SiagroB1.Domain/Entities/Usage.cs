using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Interfaces;
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
public class Usage : IAccountingAssignment
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

    /// <summary>
    /// Informações complementares padrão da NF-e (infCpl) na operação DENTRO do estado (CFOP 1xxx/5xxx).
    /// A tributação muda entre as duas operações (ex.: ICMS diferido dentro, tributado fora), então o
    /// texto também: a emissão escolhe pelo CFOP da linha.
    /// </summary>
    [Column(TypeName = "VARCHAR(2000)")]
    public string? DefaultAdditionalInfo { get; set; }

    /// <summary>Informações complementares padrão na operação FORA do estado e no exterior (CFOP 2/3/6/7xxx).</summary>
    [Column(TypeName = "VARCHAR(2000)")]
    public string? DefaultAdditionalInfoOutState { get; set; }

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

    // Contribuição social do produtor rural na compra (spec 2026-10-18): a natureza diz só o MODO; as alíquotas
    // vêm da tabela RuralContributionRates e o regime, do parceiro.
    public RuralContributionMode RuralContributionMode { get; set; }

    // Retenções de serviço (spec 2026-10-17 §2): alíquotas padrão da natureza, em percentual (5 = 5 %). Vazio/zero = não retém.
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? WithholdingIssRate { get; set; }
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? WithholdingIrrfRate { get; set; }
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? WithholdingInssRate { get; set; }
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? WithholdingPisRate { get; set; }
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? WithholdingCofinsRate { get; set; }
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? WithholdingCsllRate { get; set; }

    /// <summary>
    /// Natureza de devolução, SEMPRE da direção OPOSTA à desta (só STANDALONE): a natureza de Saída
    /// (venda) aponta a de Entrada que a devolução de venda usa; a de Entrada (compra) aponta a de
    /// Saída que a devolução de compra usa. A devolução criada pelo "Devolver" herda esta natureza
    /// em cada linha.
    /// </summary>
    public int? ReturnUsageCode { get; set; }

    [ForeignKey(nameof(ReturnUsageCode))]
    public Usage? ReturnUsage { get; set; }

    /// <summary>
    /// Permite gerar a contra-nota a partir da escrituração da NF-e do produtor (spec 2026-10-10 D4). Só natureza de
    /// Entrada; a contra-nota usa esta MESMA natureza e os mesmos tributos da escrituração (D5).
    /// </summary>
    public bool AllowsCounterNote { get; set; }

    /// <summary>Centro de custo (CTT). Sem FK: o cadastro é dual-mode.</summary>
    [Column(TypeName = "VARCHAR(20)")]
    public string? CostCenterCode { get; set; }

    /// <summary>Item contábil (CTD). Sem FK: validado por serviço.</summary>
    [Column(TypeName = "VARCHAR(20)")]
    public string? AccountingItemCode { get; set; }

    /// <summary>Rateio pré-configurado (CTJ). Exclusivo com centro de custo/item contábil.</summary>
    [Column(TypeName = "VARCHAR(10)")]
    public string? DistributionRuleCode { get; set; }

    /// <summary>Conta contábil. Sem FK: o cadastro é dual-mode (OACT/LEDGER_ACCOUNTS).</summary>
    [Column(TypeName = "VARCHAR(20)")]
    public string? LedgerAccountCode { get; set; }
}
