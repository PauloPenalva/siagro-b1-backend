using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("STORAGE_TRANSACTIONS")]
public class StorageTransaction : DocumentEntity
{
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public string? Code { get; set; }
    
    [Column(TypeName = "VARCHAR(50)")]
    [ForeignKey(nameof(StorageAddress))]
    public string? StorageAddressCode { get; set; }
    public virtual StorageAddress? StorageAddress { get; set; }
    
    public DateTime? TransactionDate { get; set; } = DateTime.Now.Date;
    
    [Column(TypeName = "VARCHAR(20)")]
    public string? TransactionTime { get; set; } = DateTime.Now.TimeOfDay.ToString();
    
    public StorageTransactionType TransactionType { get; set; }

    public StorageTransactionsStatus TransactionStatus { get; set; } = StorageTransactionsStatus.Pending;
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string CardCode { get; set; }
    
    [Column(TypeName = "VARCHAR(200)")]
    public string? CardName { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string ItemCode { get; set; }
    
    [Column(TypeName = "VARCHAR(200)")]
    public string? ItemName { get; set; }
    
    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    public required string UnitOfMeasureCode { get; set; }

    [Column(TypeName = "decimal(18,3) DEFAULT 0")]
    public decimal GrossWeight { get; set; }
    
    [Column(TypeName = "decimal(18,3) DEFAULT 0")]
    public decimal DryingDiscount { get; set; }
    
    [Column(TypeName = "decimal(18,2) DEFAULT 0")]
    public decimal DryingServicePrice { get; set; }

    [Column(TypeName = "decimal(18,3) DEFAULT 0")]
    public decimal CleaningDiscount { get; set; }
    
    [Column(TypeName = "decimal(18,2) DEFAULT 0")]
    public decimal CleaningServicePrice { get; set; }
    
    [Column(TypeName = "decimal(18,3) DEFAULT 0")]
    public decimal OthersDicount { get; set; }
    
    [Column(TypeName = "decimal(18,2) DEFAULT 0")]
    public decimal ShipmentPrice { get; set; }
    
    [Column(TypeName = "decimal(18,2) DEFAULT 0")]
    public decimal ReceiptServicePrice { get; set; }
    
    [Column(TypeName = "decimal(18,3) DEFAULT 0")]
    public decimal NetWeight  { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string WarehouseCode { get; set; }
    
    [Column(TypeName = "VARCHAR(200)")]
    public string? WarehouseName { get; set; }
    
    public Guid? ShipmentReleaseKey  { get; set; }
    public virtual ShipmentRelease? ShipmentRelease { get; set; }

    /// <summary>
    /// Liberação de entrega de VENDA que este romaneio consome. Gravada no faturamento
    /// (<c>SalesInvoicesCreateService</c>) e limpa no cancelamento da invoice. Independente
    /// de <see cref="ShipmentReleaseKey"/> (que segue exclusivo de compra).
    /// </summary>
    public Guid? SalesShipmentReleaseKey { get; set; }
    public virtual SalesShipmentRelease? SalesShipmentRelease { get; set; }

    /// <summary>
    /// Carga em que este romaneio foi montado. FK escalar (e não tabela de junção) porque é o
    /// único desenho em que o BANCO proíbe "romaneio em duas cargas". Nula enquanto o romaneio
    /// está solto na Montagem de Carga; o cancelamento da carga volta a zerá-la.
    /// </summary>
    public Guid? ShipmentLoadKey { get; set; }
    public virtual ShipmentLoad? ShipmentLoad { get; set; }

    /// <summary>
    /// Carga de onde esta mercadoria foi RECUSADA e devolvida a um armazém. Preenchida só nas
    /// transações <see cref="StorageTransactionType.SalesShipmentReturn"/> geradas pela recusa
    /// de carga.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Coluna própria, e NÃO <see cref="ShipmentLoadKey"/>.</b> Aquela FK significa
    /// "romaneio montado NESTA carga" e é o que <c>ShipmentLoadsRecalculateTotalService</c>
    /// soma para obter o volume embarcado — reusá-la faria a devolução AUMENTAR o total da
    /// carga de onde a mercadoria saiu. São vínculos opostos: um diz o que entrou na carga, o
    /// outro o que saiu dela de volta.
    /// <para>
    /// FK real com <c>NoAction</c>, como todo o projeto: a carga não é apagada enquanto tiver
    /// devolução, e o vínculo é o que alimenta
    /// <c>ShipmentLoadsRecalculateReturnedService</c>.
    /// </para>
    /// </remarks>
    public Guid? RefusedFromShipmentLoadKey { get; set; }
    public virtual ShipmentLoad? RefusedFromShipmentLoad { get; set; }

    /// <summary>
    /// Documento de RETORNO que gerou esta devolução ao armazém. Preenchida só nas transações
    /// <see cref="StorageTransactionType.SalesShipmentReturn"/> nascidas do retorno de um
    /// documento de saída legado com destino "armazém".
    /// </summary>
    /// <remarks>
    /// É o análogo legado de <see cref="RefusedFromShipmentLoadKey"/>, e existe pelo mesmo motivo:
    /// dar ao romaneio de devolução um vínculo que os guards reconheçam.
    /// <c>StorageTransactionsCancelService</c> e <c>StorageTransactionsReverseService</c> barram o
    /// cancelamento pela presença de um desses dois vínculos — sem este, a devolução do fluxo
    /// legado (que não tem carga, logo tem a outra coluna nula) escaparia dos dois, e cancelá-la
    /// pela tela de Romaneios derrubaria o crédito do armazém em silêncio.
    /// <para>
    /// ⚠️ <b>Aponta o documento de RETORNO, e não a nota de ORIGEM.</b> Uma nota pode ser retornada
    /// em várias parcelas, cada uma com sua devolução ao armazém: pela origem, o estorno de uma
    /// delas não teria como saber qual entrada cancelar, e cancelaria a errada ou todas. O retorno
    /// é único por operação, e chega-se à origem por <c>SalesInvoiceOriginKey</c>.
    /// </para>
    /// <para>
    /// ⚠️ <b>NÃO é <see cref="SalesInvoiceKey"/>.</b> Aquela significa "romaneio FATURADO nesta
    /// nota" e é o que <c>ShipmentBillingTransactionGuardService</c> lê para recusar refaturamento;
    /// gravá-la aqui faria a devolução parecer um embarque. Nem
    /// <see cref="ReturnInvoiceKey"/>, que é o discriminador <c>isNewFlow</c> do estorno e faria
    /// esta entrada ser carimbada como faturada.
    /// </para>
    /// </remarks>
    public Guid? GeneratedByReturnInvoiceKey { get; set; }
    public virtual SalesInvoice? GeneratedByReturnInvoice { get; set; }

    public TransactionCode? TransactionOrigin { get; set; } 
    
    public Guid? ShippingOrderKey { get; set; }
    
    [Column(TypeName = "VARCHAR(11) NULL")]
    [ForeignKey(nameof(TruckDriver))]
    public string? TruckDriverCode { get; set; }
    public virtual TruckDriver? TruckDriver { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public string? TruckCode { get; set; }
    
    public Guid? WeighingTicketKey { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public string? ProcessingCostCode { get; set; }
    public virtual ProcessingCost? ProcessingCost { get; set; }
    
    [Column(TypeName = "VARCHAR(9)")]
    public string? InvoiceNumber { get; set; }
    
    [Column(TypeName = "VARCHAR(3)")]
    public string? InvoiceSerie { get; set; }
    
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal InvoiceQty { get; set; }
    
    [Column(TypeName = "VARCHAR(44)")]
    public string? ChaveNFe { get; set; }
    
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal AvaiableVolumeToAllocate { get; set; }
    
    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }
    
    public ICollection<StorageTransactionQualityInspection> QualityInspections { get; set; } = [];
    
    public Guid? SalesInvoiceKey { get; set; }
    public virtual SalesInvoice? SalesInvoice { get; set; }

    public Guid? OwnershipTransferKey { get; set; }
    public virtual OwnershipTransfer? OwnershipTransfer { get; set; }
    
    [Column(TypeName = "VARCHAR(1) DEFAULT 'N'")]
    public string? IsOwnershipTransfer { get; set; }
    
    public bool IsInvoiced { get; set; }

    public Guid? StorageInvoiceKey { get; set; }

    public DateTime? InvoicedAt { get; set; }
    
    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal FreightPrice { get; set; }
    
    public Guid? ReturnInvoiceKey { get; set; }

    public DateTime? ReturnedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? ReturnedBy { get; set; }

    /// <summary>
    /// Token de concorrência otimista (SQL Server rowversion). Protege o
    /// saldo alocável (<see cref="AvaiableVolumeToAllocate"/>) contra
    /// atualizações concorrentes de aloca/desaloca.
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    /// <summary>
    /// Apenas romaneios da família Compra podem ser alocados a contratos.
    /// Espelha os tipos aceitos por PurchaseContractsAllocationCreateService.
    /// </summary>
    [NotMapped]
    public bool IsAllocatable => TransactionType is
        StorageTransactionType.Purchase or
        StorageTransactionType.PurchaseReturn or
        StorageTransactionType.PurchaseQtyComplement or
        StorageTransactionType.PurchasePriceComplement;

    /// <summary>
    /// Fonte única de verdade do saldo alocável: deriva de NetWeight menos o
    /// total já alocado (valores absolutos). Substitui os += / -= incrementais,
    /// eliminando drift — chamar após qualquer mudança de alocação.
    /// </summary>
    public void RecalculateAvailableVolume(decimal totalAllocated)
        => AvaiableVolumeToAllocate = IsAllocatable ? NetWeight - totalAllocated : decimal.Zero;
}