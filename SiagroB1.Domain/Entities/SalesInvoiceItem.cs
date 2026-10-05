using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Domain.Entities;

[Table("SALES_INVOICES_ITEMS")]
public class SalesInvoiceItem : INfeTaxedLine
{
    [Key]
    public Guid? Key { get; set; }
    
    public Guid? SalesInvoiceKey { get; set; }
    public virtual SalesInvoice? SalesInvoice { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string ItemCode { get; set; }
    
    [Column(TypeName = "VARCHAR(200)")]
    public string? ItemName { get; set; }
    
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal Quantity { get; set; }
    
    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal UnitPrice { get; set; }
    
    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    public required string UnitOfMeasureCode { get; set; }
    
    public Guid? SalesInvoiceItemOriginKey { get; set; }
    public SalesInvoiceItem? SalesInvoiceItemOrigin { get; set; }
    
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
    
    public Guid? SalesContractKey { get; set; }
    public virtual SalesContract? SalesContract { get; set; }

    /// <summary>
    /// Liberação de entrega de venda selecionada no faturamento. Transporta a chave do
    /// dialog até o vínculo dos romaneios (<c>SalesInvoicesCreateService</c>) e serve de
    /// rastro de auditoria da liberação consumida por esta linha.
    /// </summary>
    public Guid? SalesShipmentReleaseKey { get; set; }
    public virtual SalesShipmentRelease? SalesShipmentRelease { get; set; }
    
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal DeliveredQuantity { get; set; }
    
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal QuantityLoss { get; set; }

    /// <summary>
    /// Peso descarregado segundo os TICKETS (GAC-1171): soma de
    /// <c>ShipmentLoadDischarge.DischargedQuantity</c> dos tickets que apontam para esta linha.
    /// Persistido-derivado, escritor único <c>ShipmentLoadDischargesRecalculateService</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ Vive ao lado de <see cref="DeliveredQuantity"/> e NÃO se confunde com ele: este é o
    /// ticket do transportador, aquele é o relatório da trading digitado pelo conferente. Nenhum
    /// dos dois manda no outro — é justamente a divergência entre eles que o chamado quer expor,
    /// porque o ticket pode ter sido adulterado e o relatório pode ter vindo errado.
    /// NÃO participa de nenhum fator efetivo: saldo de contrato e de liberação continuam lendo só
    /// <see cref="DeliveredQuantity"/> e <see cref="QuantityLoss"/> de item encerrado.
    /// </remarks>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal TicketDeliveredQuantity { get; set; }

    /// <summary>
    /// Quanto desta linha já voltou em devoluções CONFIRMADAS (GAC-1171, rateio): soma de
    /// <c>Quantity</c> das linhas de devolução com <see cref="SalesInvoiceItemOriginKey"/> = esta
    /// linha. Persistido-derivado, escritor único <c>SalesInvoicesRecalculateReturnedService</c>.
    /// </summary>
    /// <remarks>
    /// "Confirmada" é o mesmo critério do saldo da carga: o projeto considera que a devolução ocorreu
    /// na confirmação. Serve à base do rateio do ticket de descarga (faturado − devolvido) e ao selo
    /// "Dev. parcial" das telas. NÃO muda o <c>InvoiceStatus</c>: a nota devolvida em parte continua
    /// Confirmada.
    /// </remarks>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal ReturnedQuantity { get; set; }

    /// <summary>
    /// Diferença entre o entregue e o faturado (DeliveredQuantity − Quantity). Negativa quando
    /// chegou menos do que foi faturado, que é o caso comum de quebra. Entrega ainda não
    /// conferida (zerada e em aberto) fica 0, e não a quantidade inteira negativa. É computed
    /// column PERSISTED: quem mantém o valor é o SQL Server, não a aplicação — por isso o
    /// setter é privado e nenhum serviço escreve nela. Configurada em
    /// <c>AppDbContext.OnModelCreating</c>.
    /// </summary>
    public decimal DeliveryDifference { get; private set; }

    [NotMapped]
    public decimal NetQuantity => DeliveredQuantity -  QuantityLoss;

    /// <summary>
    /// Quebra apurada na entrega: faturado − líquido efetivamente recebido.
    ///
    /// É EXATAMENTE o volume que o fator efetivo devolveu ao contrato (o consumo passa a ser
    /// <see cref="NetQuantity"/> quando a entrega fecha), e por isso é o número que a NF de
    /// devolução do cliente precisa espelhar para o fiscal bater com o físico.
    ///
    /// Zero enquanto a entrega está aberta: sem conferência não há quebra apurada, e o fator
    /// efetivo ainda vale 1.
    /// </summary>
    [NotMapped]
    public decimal AssessedShortage =>
        DeliveryStatus == SalesInvoiceDeliveryStatus.Closed
            ? decimal.Round(Quantity - NetQuantity, 3, MidpointRounding.ToEven)
            : 0m;

    [NotMapped] 
    public decimal NetTotal => NetQuantity * UnitPrice; 
    
    public SalesInvoiceDeliveryStatus DeliveryStatus { get; set; } = SalesInvoiceDeliveryStatus.Open;

    /// <summary>
    /// Quando a ENTREGA desta linha foi conferida pela última vez (telas de Conferência de
    /// entregas e de Estorno). Só é carimbada quando quantidade entregue, desconto ou status
    /// da entrega mudam — um ajuste fiscal feito em outra tela não finge ser conferência.
    ///
    /// Nulável e sem backfill: linha nunca conferida fica em branco na grade. A entidade não
    /// herda de <c>BaseEntity</c> de propósito (a chave aqui não é Identity e não há RowId).
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Usuário da última conferência de entrega. Ver <see cref="UpdatedAt"/>.</summary>
    [Column(TypeName = "VARCHAR(100)")]
    public string? UpdatedBy { get; set; }

    /// <summary>
    /// Natureza de operação da LINHA (<see cref="Usage"/>), como no SAP: cada item tem a sua,
    /// resolve o próprio CFOP e produz o próprio efeito no contrato. Um documento pode
    /// misturar naturezas.
    ///
    /// Gravada SEM chave estrangeira, como o restante do cadastro dual-mode: em modo SAPB1 a
    /// tabela local fica vazia e uma FK obrigatória viraria INNER JOIN, zerando a coleção
    /// inteira. A validação é no serviço.
    ///
    /// Obrigatória na criação; a COLUNA é nulável só por causa do legado — a migration de
    /// backfill preenche as linhas existentes com a natureza semente.
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

    /// <summary>Centro de custo da linha. Sem FK: o cadastro é dual-mode (OPRC/COST_CENTERS).</summary>
    [Column(TypeName = "VARCHAR(10)")]
    public string? CostCenterCode { get; set; }

    /// <summary>Conta contábil da linha. Sem FK: o cadastro é dual-mode (OACT/LEDGER_ACCOUNTS).</summary>
    [Column(TypeName = "VARCHAR(20)")]
    public string? LedgerAccountCode { get; set; }

    /// <summary>
    /// Total de impostos da linha. Derivado, sem coluna persistida — evita drift, ao custo
    /// de não poder $filter/$orderby por ele (mesma limitação que o documento já tem).
    /// </summary>
    [NotMapped]
    public decimal TotalTaxes => IcmsValue + PisValue + CofinsValue;

    /// <summary>
    /// IBS + CBS da linha. Separado de <see cref="TotalTaxes"/> porque em 2026 é informativo e
    /// não compõe o total do documento.
    /// </summary>
    [NotMapped]
    public decimal TotalIbsCbs => CbsValue + IbsStateValue + IbsMunicipalValue;
}