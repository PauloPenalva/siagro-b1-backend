using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

[Table("SALES_INVOICES_ITEMS")]
public class SalesInvoiceItem
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

    [Column(TypeName = "DECIMAL(5,4) DEFAULT 0")]
    public decimal IcmsRate { get; set; }

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal IcmsValue { get; set; }

    [Column(TypeName = "VARCHAR(3)")]
    public string? CstPis { get; set; }

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal PisBase { get; set; }

    [Column(TypeName = "DECIMAL(5,4) DEFAULT 0")]
    public decimal PisRate { get; set; }

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal PisValue { get; set; }

    [Column(TypeName = "VARCHAR(3)")]
    public string? CstCofins { get; set; }

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal CofinsBase { get; set; }

    [Column(TypeName = "DECIMAL(5,4) DEFAULT 0")]
    public decimal CofinsRate { get; set; }

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal CofinsValue { get; set; }

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
}