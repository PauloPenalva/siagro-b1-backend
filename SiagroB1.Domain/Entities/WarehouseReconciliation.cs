using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Conferência de Saldo de Armazém (GAC-1164): o armazém de TERCEIROS informa o saldo físico
/// do grão da empresa e a diferença para o sistema vira, na aprovação, um romaneio de
/// <see cref="StorageTransactionType.WarehouseLoss"/> ou <see cref="StorageTransactionType.WarehouseGain"/>.
/// </summary>
/// <remarks>
/// Desde a revisão de 17/09/2026 (spec §9) o saldo do sistema é o saldo a embarcar das liberações e
/// a perda é distribuída em <see cref="Releases"/>: a aprovação consome cada liberação (Compra+Perda
/// na Standard, só Perda sem perna de compra). <see cref="StorageTransactionKey"/> só é preenchido em
/// conferência anterior à revisão.
/// </remarks>
[Table("WAREHOUSE_RECONCILIATIONS")]
public class WarehouseReconciliation : DocumentEntity
{
    [Column(TypeName = "VARCHAR(50)")]
    public string? Code { get; set; }

    public WarehouseReconciliationStatus Status { get; set; } = WarehouseReconciliationStatus.Draft;

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string WarehouseCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? WarehouseName { get; set; }

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string ItemCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? ItemName { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string UnitOfMeasureCode { get; set; }

    /// <summary>Parceiro do romaneio gerado: o próprio armazém (armazém é parceiro com QryGroup23).</summary>
    [Column(TypeName = "VARCHAR(50)")]
    public string? CardCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? CardName { get; set; }

    /// <summary>Data do extrato do armazém. O saldo do sistema é apurado até o fim deste dia.</summary>
    public DateTime ReferenceDate { get; set; }

    /// <summary>Saldo físico informado pelo armazém.</summary>
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal ReportedBalance { get; set; }

    /// <summary>Snapshot do saldo de armazém do sistema até <see cref="ReferenceDate"/>.</summary>
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal SystemBalance { get; set; }

    /// <summary><c>ReportedBalance − SystemBalance</c>: negativo = Perda, positivo = Sobra.</summary>
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal Difference { get; set; }

    public Guid ReasonKey { get; set; }
    public virtual WarehouseReconciliationReason? Reason { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }

    public DateTime? SentAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? SentBy { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? ApprovalComments { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? CancellationReason { get; set; }

    /// <summary>Romaneio de Perda/Sobra das conferências anteriores à revisão de 17/09/2026. Nas
    /// novas, as chaves ficam em <see cref="Releases"/>.</summary>
    public Guid? StorageTransactionKey { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }

    public virtual ICollection<WarehouseReconciliationAttachment> Attachments { get; set; } = [];

    public virtual ICollection<WarehouseReconciliationRelease> Releases { get; set; } = [];
}
