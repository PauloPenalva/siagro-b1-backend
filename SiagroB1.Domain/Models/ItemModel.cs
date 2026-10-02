using System.ComponentModel.DataAnnotations;

namespace SiagroB1.Domain.Models;

public class ItemModel 
{
    [Key]
    public required string ItemCode { get; set; }

    public required string ItemName { get; set; }
    
    public short? ItmsGrpCod {get; set;}
    
    public string? Enabled { get; set; }

    /// <summary>Origem da mercadoria (0 a 8). Nula em SAPB1 — o produto vem do OITM e a NF-e é do SAP.</summary>
    public byte? GoodsOrigin { get; set; }

    /// <summary>NCM (8 dígitos). Nulo em SAPB1, pelo mesmo motivo.</summary>
    public string? Ncm { get; set; }
}