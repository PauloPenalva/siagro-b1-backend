using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Linha do documento de entrada.
///
/// <see cref="ItemCode"/> e <see cref="UnitOfMeasureCode"/> são NULÁVEIS aqui, ao contrário de
/// <see cref="SalesInvoiceItem"/>: o código vem do emitente e pode não existir no cadastro local —
/// quem vale para a conferência é a linha de origem.
///
/// Os campos fiscais (natureza de operação, CFOP, NCM, CST, impostos e a fotografia do cálculo)
/// existem e só são preenchidos pelo cálculo da emissão própria (modo NF-e); documento de terceiro
/// os deixa vazios. As amarrações a contrato de compra e a romaneio chegam na Fase 3, junto com o
/// value help e a coluna de divergência que as consomem.
/// </summary>
[Table("PURCHASE_INVOICES_ITEMS")]
public class PurchaseInvoiceItem : INfeTaxedLine
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid? PurchaseInvoiceKey { get; set; }
    public virtual PurchaseInvoice? PurchaseInvoice { get; set; }

    [Column(TypeName = "VARCHAR(30)")]
    public string? ItemCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? ItemName { get; set; }

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal UnitPrice { get; set; }

    [Column(TypeName = "VARCHAR(4)")]
    public string? UnitOfMeasureCode { get; set; }

    /// <summary>
    /// A AMARRAÇÃO da devolução, feita à mão pelo operador: a linha do documento de SAÍDA que esta
    /// linha espelha.
    ///
    /// Nulável por dois motivos, ambos legítimos: a linha nasce da importação do XML sem origem
    /// definida — o layout da NF-e não carrega esse vínculo — e a entrada NORMAL não tem origem
    /// alguma.
    /// </summary>
    public Guid? SalesInvoiceItemKey { get; set; }
    public virtual SalesInvoiceItem? SalesInvoiceItem { get; set; }

    /// <summary>
    /// Contrato de compra que esta linha referencia — a amarração que fecha a conciliação fiscal
    /// da compra, espelhando <c>SalesInvoiceItem.SalesContractKey</c> do lado da venda.
    ///
    /// É REFERÊNCIA e não efeito: não cria allocation e não move saldo. O saldo físico continua
    /// sendo movido só pelo romaneio.
    ///
    /// Nullable por três razões independentes: NF de insumo, serviço ou frete não tem contrato; a
    /// linha importada de XML nasce sem vínculo, porque o XML não o carrega; e amarrar depois de
    /// gravar é fluxo legítimo.
    /// </summary>
    public Guid? PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }

    /// <summary>Linha de remessa apontando a linha da NF de venda futura que a antecipou.</summary>
    public Guid? PurchaseInvoiceItemOriginKey { get; set; }
    public virtual PurchaseInvoiceItem? PurchaseInvoiceItemOrigin { get; set; }

    /// <summary>
    /// Natureza de operação da LINHA (<c>Usage</c>), como no SAP: cada item tem a sua,
    /// resolve o próprio CFOP e produz o próprio efeito no contrato. Um documento pode
    /// misturar naturezas.
    ///
    /// Gravada SEM chave estrangeira, como o restante do cadastro dual-mode: em modo SAPB1 a
    /// tabela local fica vazia e uma FK obrigatória viraria INNER JOIN, zerando a coleção
    /// inteira. A validação é no serviço.
    ///
    /// Nulável: só a entrada própria em filial que emite NF-e e a devolução de compra exigem a
    /// natureza (validado no cálculo dos tributos); documento de terceiro e o legado ficam sem ela.
    /// </summary>
    public int? UsageCode { get; set; }

    /// <summary>
    /// Nome da natureza, desnormalizado como <see cref="ItemName"/> — é o que a grade e os
    /// relatórios mostram sem depender do cadastro (que em SAPB1 nem é local). Quem manda é
    /// o servidor: o serviço de criação sobrescreve com o nome da natureza resolvida.
    /// </summary>
    [Column(TypeName = "VARCHAR(200)")]
    public string? UsageName { get; set; }

    /// <summary>
    /// CFOP resolvido da natureza de operação no momento da GRAVAÇÃO e congelado como
    /// histórico: se o cadastro da natureza mudar depois, o documento já emitido não pode
    /// mudar junto. Ver <c>SalesInvoicesCfopResolveService</c>.
    /// </summary>
    [Column(TypeName = "VARCHAR(4)")]
    public string? Cfop { get; set; }

    [Column(TypeName = "VARCHAR(8)")]
    public string? Ncm { get; set; }

    [Column(TypeName = "VARCHAR(3)")]
    public string? CstIcms { get; set; }

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal IcmsBase { get; set; }

    /// <summary>Percentual, como na NF-e: 18% = 18,0000.</summary>
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")]
    public decimal IcmsRate { get; set; }

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal IcmsValue { get; set; }

    [Column(TypeName = "VARCHAR(3)")]
    public string? CstPis { get; set; }

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal PisBase { get; set; }

    /// <summary>Percentual, como na NF-e: 1,65% = 1,6500.</summary>
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")]
    public decimal PisRate { get; set; }

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal PisValue { get; set; }

    [Column(TypeName = "VARCHAR(3)")]
    public string? CstCofins { get; set; }

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal CofinsBase { get; set; }

    /// <summary>Percentual, como na NF-e: 7,6% = 7,6000.</summary>
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")]
    public decimal CofinsRate { get; set; }

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal CofinsValue { get; set; }

    // --- Fotografia do cálculo de tributos da NF-e STANDALONE (só com a regra ativa). ---

    /// <summary>Origem da mercadoria copiada do produto na gravação.</summary>
    [Column(TypeName = "TINYINT")]
    public byte? GoodsOrigin { get; set; }

    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")] public decimal IcmsBaseReduction { get; set; }
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")] public decimal IcmsDeferral { get; set; }

    /// <summary>ICMS da operação (vICMSOp) — só no CST 51.</summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal IcmsOperationValue { get; set; }

    /// <summary>ICMS diferido (vICMSDif) — só no CST 51.</summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal IcmsDeferredValue { get; set; }

    [Column(TypeName = "VARCHAR(10)")] public string? IcmsBenefitCode { get; set; }

    [Column(TypeName = "VARCHAR(3)")] public string? IbsCbsCst { get; set; }
    [Column(TypeName = "VARCHAR(6)")] public string? IbsCbsClassCode { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal IbsCbsBase { get; set; }
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")] public decimal CbsRate { get; set; }
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")] public decimal CbsRateReduction { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal CbsValue { get; set; }
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")] public decimal IbsStateRate { get; set; }
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")] public decimal IbsMunicipalRate { get; set; }
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")] public decimal IbsRateReduction { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal IbsStateValue { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal IbsMunicipalValue { get; set; }

    /// <summary>Cópia das flags da natureza na gravação — sem efeito por ora (spec D14).</summary>
    public bool MovesFiscalInventory { get; set; }

    /// <summary>Cópia das flags da natureza na gravação — sem efeito por ora (spec D14).</summary>
    public bool CreatesFinancialDocument { get; set; }

    /// <summary>
    /// <c>det/@nItem</c> com que a linha saiu na NF-e. Gravado na emissão: a NF-e de devolução
    /// referencia o item da venda por este número (<c>DFeReferenciado</c>, regra VC02-14).
    /// </summary>
    public int? NfeItemNumber { get; set; }

    /// <summary>ICMS + PIS + COFINS da fotografia (o IBS/CBS de 2026 é informativo e fica fora).</summary>
    [NotMapped]
    public decimal TotalTaxes => IcmsValue + PisValue + CofinsValue;

    [NotMapped]
    public decimal TotalIbsCbs => CbsValue + IbsStateValue + IbsMunicipalValue;

    [NotMapped]
    public decimal Total => decimal.Round(Quantity * UnitPrice, 2, MidpointRounding.ToEven);

    // --- Frete, seguro, desconto e outras despesas da linha (spec 2026-10-05, D1). Padrão 0 em toda filial (R1). ---

    /// <summary>Frete cobrado na linha (<c>det/prod/vFrete</c>).</summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal FreightValue { get; set; }

    /// <summary>Seguro cobrado na linha (<c>det/prod/vSeg</c>).</summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal InsuranceValue { get; set; }

    /// <summary>
    /// Desconto incondicional da linha (<c>det/prod/vDesc</c>). Nunca passa de itens + frete + seguro + outras despesas
    /// (<c>InvoiceLineChargeRules</c>).
    /// </summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal DiscountValue { get; set; }

    /// <summary>Outras despesas acessórias da linha (<c>det/prod/vOutro</c>).</summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal OtherExpensesValue { get; set; }

    /// <summary>
    /// Total geral da linha (spec D2): <see cref="Total"/> (o vProd) + frete + seguro + outras despesas − desconto. É a
    /// base dos tributos (D3) e a parte da linha no vNF. Derivado, sem coluna.
    /// </summary>
    [NotMapped]
    public decimal GrandTotal => Total + FreightValue + InsuranceValue + OtherExpensesValue - DiscountValue;

    /// <summary>
    /// Quebra apurada da linha de ORIGEM — o número que o fiscal deveria espelhar.
    /// </summary>
    /// <remarks>
    /// Depende de <see cref="SalesInvoiceItem"/> CARREGADO. Sem o Include a navegação vem null e
    /// isto devolve 0 em silêncio, fazendo toda linha parecer divergente. Quem carrega é o
    /// <c>PurchaseInvoicesGetService</c>.
    /// </remarks>
    [NotMapped]
    public decimal AssessedShortage => SalesInvoiceItem?.AssessedShortage ?? 0m;

    /// <summary>
    /// Devolvido − quebra apurada. Zero é o caso em que fiscal e físico batem; diferente de zero a
    /// tela avisa, mas NÃO impede gravar — arredondamento e devolução parcial são legítimos, e quem
    /// decide é o usuário.
    /// </summary>
    [NotMapped]
    public decimal Difference =>
        decimal.Round(Quantity - AssessedShortage, 3, MidpointRounding.ToEven);
}
