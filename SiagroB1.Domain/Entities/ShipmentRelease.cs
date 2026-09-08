using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;
    
namespace SiagroB1.Domain.Entities;

[Table("SHIPMENT_RELEASES")]
public class ShipmentRelease : DocumentEntity
{
    public required Guid PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }

    public DateTime ReleaseDate { get; set; } = DateTime.Now.Date;

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal ReleasedQuantity { get; set; } // Quantidade liberada

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string DeliveryLocationCode { get; set; }
    
    [Column(TypeName = "VARCHAR(200)")]
    public string? DeliveryLocationName { get; set; }
    
    public ReleaseStatus Status { get; set; } = ReleaseStatus.Pending;

    /// <summary>
    /// Motivo informado no cancelamento da liberação (obrigatório na ação de cancelar).
    /// </summary>
    [Column(TypeName = "VARCHAR(500)")]
    public string? CancellationReason { get; set; }

    public virtual ICollection<StorageTransaction> Transactions { get; } = [];

    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal ShippedQuantity { get; set; }

    /// <summary>
    /// De onde a liberação nasceu. Em <see cref="ReleaseOrigin.OwnershipTransfer"/> e
    /// <see cref="ReleaseOrigin.SalesReturn"/> o físico JÁ está em nosso poder — a liberação
    /// existe apenas para que a mercadoria seja embarcada e faturada.
    /// </summary>
    /// <remarks>
    /// Não compare esta propriedade à mão: as três perguntas que o sistema faz sobre a origem
    /// (embarca sem perna de compra? consome contrato? exige lote?) têm respostas DIFERENTES
    /// para as mesmas origens. Use <see cref="ReleaseOriginRules"/>.
    /// </remarks>
    public ReleaseOrigin Origin { get; set; } = ReleaseOrigin.Standard;

    /// <summary>
    /// Transferência de titularidade que emitiu esta liberação. Índice único
    /// filtrado no banco garante a invariante "uma transferência, uma liberação".
    /// </summary>
    public Guid? OwnershipTransferKey { get; set; }
    public virtual OwnershipTransfer? OwnershipTransfer { get; set; }

    /// <summary>
    /// Romaneio de devolução (<see cref="StorageTransactionType.SalesShipmentReturn"/>) que
    /// emitiu esta liberação. Preenchida apenas nas liberações
    /// <see cref="ReleaseOrigin.SalesReturn"/>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Aponta a ENTRADA no armazém, e não a nota de retorno nem a carga recusada.</b> A
    /// entrada é <b>uma por operação de devolução</b>, enquanto nota e carga não são: uma carga
    /// pode ser recusada em parcelas, e cancelar uma dessas recusas por
    /// <c>RefusedFromShipmentLoadKey</c> derrubaria também as liberações das outras. Chega-se à
    /// carga ou à nota num salto, pelas chaves que a própria entrada já carrega.
    /// <para>
    /// <b>Não é única:</b> uma devolução cujos romaneios venham de contratos de compra diferentes
    /// emite uma liberação por contrato, todas apontando a mesma entrada.
    /// </para>
    /// <para>
    /// Declarada à mão no <c>AppDbContext</c>: é a SEGUNDA relação entre estas duas entidades, e a
    /// primeira (<see cref="Transactions"/>, "romaneios que CONSOMEM esta liberação") tem
    /// significado oposto. Deixar a convenção parear sozinha faria o romaneio de devolução contar
    /// como romaneio da liberação — e o saldo dela nasceria negativo.
    /// </para>
    /// </remarks>
    public Guid? GeneratedByStorageTransactionKey { get; set; }
    public virtual StorageTransaction? GeneratedByStorageTransaction { get; set; }

    /// <summary>
    /// Texto livre explicando de onde a liberação veio. Hoje só as liberações de devolução o
    /// preenchem — sem ele o operador que abre a Expedição de Grãos não tem nenhuma pista de que
    /// aquele saldo é mercadoria que voltou, nem de qual documento ou carga.
    /// </summary>
    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }

    /// <summary>
    /// Lote de armazenagem próprio onde a mercadoria desta liberação já está
    /// fisicamente depositada. Preenchido apenas nas liberações de transferência
    /// de titularidade.
    /// </summary>
    /// <remarks>
    /// Existe porque a Expedição de Grãos opera em nível de armazém, não de lote:
    /// sem este vínculo, o par Purchase(8)/SalesShipment(7) do embarque não daria
    /// baixa no lote e o Receipt(0) gravado pela transferência ficaria como saldo
    /// fantasma para sempre. <c>ShippingTransactionsCreateService</c> propaga este
    /// código para a perna de saída.
    /// </remarks>
    [Column(TypeName = "VARCHAR(50)")]
    [ForeignKey(nameof(StorageAddress))]
    public string? StorageAddressCode { get; set; }
    public virtual StorageAddress? StorageAddress { get; set; }

    /// <summary>
    /// Token de concorrência otimista (SQL Server rowversion).
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    /// <summary>
    /// Saldo disponível para romanear, derivado de <see cref="ShippedQuantity"/>
    /// (persistido, recalculado nos hooks de romaneio). Não depende de navegação.
    /// </summary>
    [NotMapped]
    public decimal AvailableQuantity =>
        Status != ReleaseStatus.Cancelled
            ? CalculateAvailableQuantity(ReleasedQuantity, ShippedQuantity)
            : decimal.Zero;

    /// <summary>
    /// Regra de arredondamento do saldo, compartilhada com quem precisa avaliá-lo
    /// antes de gravar (ex.: <c>ShipmentReleasesCancelationService</c>).
    /// </summary>
    public static decimal CalculateAvailableQuantity(decimal releasedQuantity, decimal shippedQuantity) =>
        decimal.Round(releasedQuantity - shippedQuantity, 3, MidpointRounding.ToEven);
    
    /// <summary>
    /// Quantidade que esta liberação consome do contrato de origem.
    /// Cancelada => apenas o que foi efetivamente romaneado (o saldo não romaneado
    /// volta para <c>PurchaseContract.TotalAvailableToRelease</c>); caso contrário,
    /// o total liberado. Derivada de <see cref="ShippedQuantity"/> (persistido) —
    /// não depende de navegação, funciona sob $select do OData.
    /// </summary>
    /// <remarks>
    /// Se um romaneio de uma liberação já cancelada for posteriormente cancelado,
    /// os hooks de <c>ShipmentReleasesRecalculateShippedService</c> reduzem
    /// <see cref="ShippedQuantity"/> e o contrato recupera mais saldo automaticamente.
    /// <para>
    /// ⚠️ <b><see cref="ReleaseOrigin.SalesReturn"/> consome ZERO.</b> Aquela liberação
    /// nasce de mercadoria que VOLTOU ao armazém, e o volume dela já foi debitado do
    /// contrato quando a mercadoria saiu pela primeira vez — contá-lo aqui duplicaria o
    /// liberado. É por este ponto único que os quatro computed de
    /// <c>PurchaseContract</c> (<c>TotalShipmentReleases</c>, <c>TotalAvailableToRelease</c>
    /// e os <c>...WithoutProvisioning</c>) a excluem de graça. O espelho em SQL de
    /// <c>PurchaseContractsGetShipmentReleasesAvailableService</c> NÃO deriva daqui e
    /// precisa ser mantido em sincronia à mão.
    /// </para>
    /// </remarks>
    [NotMapped]
    public decimal ConsumedQuantity =>
        !ReleaseOriginRules.ConsumesPurchaseContract(Origin)
            ? decimal.Zero
            : Status == ReleaseStatus.Cancelled
                ? decimal.Round(Math.Max(decimal.Zero, ShippedQuantity), 3, MidpointRounding.ToEven)
                : ReleasedQuantity;

    /// <summary>
    /// Volume que o cancelamento devolveu ao contrato de origem — o saldo que estava
    /// liberado mas não chegou a ser romaneado. Zero enquanto a liberação não é
    /// cancelada, já que nesse caso ela consome o total liberado.
    /// Vale <see cref="ConsumedQuantity"/> + este = <see cref="ReleasedQuantity"/> em toda
    /// origem <b>exceto</b> <see cref="ReleaseOrigin.SalesReturn"/>, onde os dois são zero.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b><see cref="ReleaseOrigin.SalesReturn"/> devolve ZERO.</b> Cancelar uma liberação
    /// de devolução não pode CREDITAR o contrato: ela nunca o debitou. Sem esta exceção o
    /// cancelamento devolveria ao contrato um volume que nunca saiu dele — o espelho exato
    /// do erro que a regra de <see cref="ConsumedQuantity"/> evita do outro lado.
    /// </remarks>
    [NotMapped]
    public decimal ReturnedToContractQuantity =>
        !ReleaseOriginRules.ConsumesPurchaseContract(Origin)
            ? decimal.Zero
            : Status == ReleaseStatus.Cancelled
                ? decimal.Round(Math.Max(decimal.Zero, ReleasedQuantity - ConsumedQuantity), 3, MidpointRounding.ToEven)
                : decimal.Zero;
}