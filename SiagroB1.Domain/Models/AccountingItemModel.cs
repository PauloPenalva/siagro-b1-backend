using System.ComponentModel.DataAnnotations;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Models;

/// <summary>DTO exposto no OData para o item contábil (CTD). Só STANDALONE.</summary>
public class AccountingItemModel
{
    [Key]
    public required string Code { get; set; }

    public required string Name { get; set; }

    public bool Inactive { get; set; }

    /// <summary>Sintético só agrupa; analítico recebe documento e rateio.</summary>
    public AccountingEntityClass Class { get; set; } = AccountingEntityClass.Analytic;

    public string? ParentCode { get; set; }

    public DateTime? ValidFrom { get; set; }

    public DateTime? ValidTo { get; set; }
}
