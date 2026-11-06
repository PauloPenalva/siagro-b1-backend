using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities.Market;

/// <summary>Registro de estouro de limite: um por limite por dia, para o aviso não repetir.</summary>
[Table("MARKET_LIMIT_BREACHES")]
[Index(nameof(LimitId), nameof(Date), IsUnique = true)]
public class MarketLimitBreach
{
    [Key]
    public int Id { get; set; }

    public int LimitId { get; set; }

    [ForeignKey(nameof(LimitId))]
    public MarketExposureLimit? Limit { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime Date { get; set; }

    /// <summary>Posição líquida no momento da detecção.</summary>
    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal Value { get; set; }

    /// <summary>Limite violado (MaxLong ou -MaxShort).</summary>
    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal LimitValue { get; set; }

    public DateTime DetectedAt { get; set; }

    /// <summary>
    /// Reservada: não é preenchida. O estado real do envio fica na outbox de notificações
    /// (<c>NOTIFICATION_OUTBOX_MESSAGES</c>, <c>DocumentCode = "LIMITE-{LimitId}"</c>).
    /// </summary>
    public DateTime? NotifiedAt { get; set; }
}
