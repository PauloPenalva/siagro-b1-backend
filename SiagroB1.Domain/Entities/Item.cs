using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

[Table("ITEMS")]
public class Item
{
    [Key]
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string ItemCode { get; set; }

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public required string ItemName { get; set; }

    public short? ItmsGrpCod { get; set; } = 105;

    [Column(TypeName = "VARCHAR(3)")]
    public string? Enabled { get; set; } = "SIM";

    /// <summary>Origem da mercadoria (tabela da SEFAZ, 0 a 8). Vai no XML e decide os 4% interestaduais.</summary>
    [Column(TypeName = "TINYINT")]
    public byte? GoodsOrigin { get; set; }

    /// <summary>NCM, 8 dígitos. Copiado para a linha do documento fiscal.</summary>
    [Column(TypeName = "VARCHAR(8)")]
    public string? Ncm { get; set; }

    /// <summary>CEST, 7 dígitos (opcional). Vai no item da NF-e, com escala relevante.</summary>
    [Column(TypeName = "VARCHAR(7)")]
    public string? Cest { get; set; }
}