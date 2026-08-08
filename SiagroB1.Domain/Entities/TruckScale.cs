using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

[Table("TRUCK_SCALES")]
[Index(nameof(Code), IsUnique = true)]
public class TruckScale
{
    [Key]
    public required string Code { get; set; }

    public required string Name { get; set; }

    public required string Localization { get; set; }

    [Column(TypeName = "VARCHAR(50)")]
    public string? IpAddress { get; set; }

    public int Port { get; set; }

    public ScaleProtocolType Protocol { get; set; } = ScaleProtocolType.JundiaiBj850;

    // Sobrescritas do preset. Nulas usam o padrão do protocolo - é assim que o BJ850 real é
    // calibrado em campo, sem recompilar.
    public int? FramePrefixLength { get; set; }

    public int? WeightLength { get; set; }

    public int? DecimalPlaces { get; set; }

    [Column(TypeName = "VARCHAR(10)")]
    public string? FrameTerminator { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? FramePattern { get; set; }

    public bool ValidateTare { get; set; }

    public int TareToleranceKg { get; set; }

    /// <summary>Grava os frames crus no log, para calibrar o protocolo em campo.</summary>
    public bool LogRawFrames { get; set; }
}
