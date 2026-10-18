using System.ComponentModel.DataAnnotations;

namespace SiagroB1.Domain.Models;

public class ItemModel 
{
    /// <summary>Em STANDALONE pode chegar vazio quando a série escolhida é automática: o serviço gera o código.</summary>
    [Key]
    public string ItemCode { get; set; } = string.Empty;

    /// <summary>Série de numeração escolhida (Automático/Manual). Só entrada: não é persistida.</summary>
    public Guid? DocNumberKey { get; set; }

    public required string ItemName { get; set; }
    
    public short? ItmsGrpCod {get; set;}
    
    public string? Enabled { get; set; }

    /// <summary>Origem da mercadoria (0 a 8). Nula em SAPB1 — o produto vem do OITM e a NF-e é do SAP.</summary>
    public byte? GoodsOrigin { get; set; }

    /// <summary>NCM (8 dígitos). Nulo em SAPB1, pelo mesmo motivo.</summary>
    public string? Ncm { get; set; }

    /// <summary>CEST (7 dígitos), opcional. Nulo em SAPB1, pelo mesmo motivo.</summary>
    public string? Cest { get; set; }
}