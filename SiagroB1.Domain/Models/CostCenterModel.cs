using System.ComponentModel.DataAnnotations;

namespace SiagroB1.Domain.Models;

/// <summary>
/// DTO exposto no OData para centro de custo. É alimentado pela tabela local
/// (STANDALONE) ou por OPRC (SAPB1), conforme a implementação de ICostCenterService.
/// </summary>
public class CostCenterModel
{
    [Key]
    public required string Code { get; set; }

    public required string Name { get; set; }

    public bool Inactive { get; set; }

    /// <summary>Dimensão gerencial (1 a 5) a que o centro pertence. No SAPB1 é sempre 1.</summary>
    public int DimensionNumber { get; set; } = 1;
}
