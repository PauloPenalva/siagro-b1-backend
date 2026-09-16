using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Documento da troca de liberação de uma Expedição da carga (GAC-1177): amarra a Expedição
/// original (substituída, retirada da carga), o estorno gerado na origem (12 e, quando a
/// origem é Standard, 9 com a mesma quantidade) e a Expedição nova que assume a vaga na carga
/// (7 e, quando o destino consome contrato, 8).
/// </summary>
/// <remarks>
/// Sem navigation properties em nenhuma das nove FKs: a entidade só é lida pela própria chave
/// (grid da carga) ou por <see cref="ShipmentLoadKey"/>/<see cref="OperationGroupKey"/>, e
/// coleções inversas nas seis pontas repetidas para <see cref="StorageTransaction"/> e nas duas
/// para <see cref="ShipmentRelease"/> inflariam o EDM do OData sem que ninguém peça.
/// </remarks>
[Table("SHIPPING_RELEASE_CHANGES")]
[Index(nameof(ShipmentLoadKey))]
[Index(nameof(OperationGroupKey))]
public class ShippingReleaseChange : BaseEntity
{
    /// <summary>Carga cuja Expedição foi trocada.</summary>
    public Guid ShipmentLoadKey { get; set; }

    /// <summary>
    /// Correlaciona as linhas geradas pela MESMA chamada (troca com mais de um item, ou
    /// inversão entre duas liberações da mesma carga). Não é FK — não há uma linha "dona" do
    /// grupo, todas são irmãs; só índice para a consulta.
    /// </summary>
    public Guid OperationGroupKey { get; set; }

    /// <summary>Perna 7 (venda) da Expedição original, substituída pela troca.</summary>
    public Guid OriginalSalesStorageTransactionKey { get; set; }

    /// <summary>Perna 8 (compra) da Expedição original, quando a origem consome contrato.</summary>
    public Guid? OriginalPurchaseStorageTransactionKey { get; set; }

    /// <summary>Estorno de venda (tipo 12) gerado na origem.</summary>
    public Guid ReturnSalesStorageTransactionKey { get; set; }

    /// <summary>Estorno de compra (tipo 9) gerado na origem, só quando ela é Standard.</summary>
    public Guid? ReturnPurchaseStorageTransactionKey { get; set; }

    /// <summary>Perna 7 (venda) da Expedição nova, que assume a vaga vigente na carga.</summary>
    public Guid NewSalesStorageTransactionKey { get; set; }

    /// <summary>Perna 8 (compra) da Expedição nova, quando o destino consome contrato.</summary>
    public Guid? NewPurchaseStorageTransactionKey { get; set; }

    /// <summary>Liberação de onde a Expedição saiu.</summary>
    public Guid SourceShipmentReleaseKey { get; set; }

    /// <summary>Liberação para onde a Expedição foi movida.</summary>
    public Guid TargetShipmentReleaseKey { get; set; }

    /// <summary>Peso bruto da Expedição original — é o que o estorno devolve.</summary>
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal OriginalQuantity { get; set; }

    /// <summary>Quantidade da Expedição nova, já resolvida pela regra da feature.</summary>
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal NewQuantity { get; set; }

    /// <summary>Motivo da troca, digitado pelo operador.</summary>
    [Column(TypeName = "VARCHAR(500)")]
    public required string Reason { get; set; }
}
