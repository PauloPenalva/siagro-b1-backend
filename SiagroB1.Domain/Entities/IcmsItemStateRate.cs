using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Alíquota interna do ICMS de um produto numa UF (spec 2026-10-17 §1). Prevalece sobre a da UF; <see cref="FcpRate"/> nulo
/// herda o FCP da UF. A chave natural é (ItemCode, StateCode), garantida por índice único; a chave técnica é um Guid porque
/// o OData V4 do UI5 lida mal com chave composta no PATCH/DELETE.
/// </summary>
[Table("ICMS_ITEM_STATE_RATES")]
[Index(nameof(ItemCode), nameof(StateCode), IsUnique = true)]
public class IcmsItemStateRate
{
    [Key]
    public Guid Key { get; set; }

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public string ItemCode { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(2) NOT NULL")]
    public string StateCode { get; set; } = string.Empty;

    /// <summary>Alíquota interna do produto na UF, em percentual.</summary>
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")]
    public decimal InternalRate { get; set; }

    /// <summary>FCP do produto na UF; nulo herda o da UF.</summary>
    [Column(TypeName = "DECIMAL(7,4)")]
    public decimal? FcpRate { get; set; }
}
