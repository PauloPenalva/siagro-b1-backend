using System.ComponentModel.DataAnnotations;

namespace SiagroB1.Domain.Models;

/// <summary>
/// União somente leitura dos códigos de centro de custo e de regra de distribuição
/// (o código é único entre os dois), usada para escolher o valor de uma dimensão.
/// </summary>
public class DimensionCodeModel
{
    public const string KindCostCenter = "CostCenter";
    public const string KindRule = "Rule";

    [Key]
    public required string Code { get; set; }

    public required string Name { get; set; }

    public int DimensionNumber { get; set; }

    /// <summary>"CostCenter" ou "Rule".</summary>
    public required string Kind { get; set; }

    public bool Inactive { get; set; }
}
