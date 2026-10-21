using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>Item contábil (CTD do Protheus): projeto, cliente, fornecedor, veículo. Só STANDALONE.</summary>
[Table("ACCOUNTING_ITEMS")]
public class AccountingItem
{
    [Key]
    [Column(TypeName = "VARCHAR(20) NOT NULL")]
    public required string Code { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    /// <summary>Classe (CTD_CLASSE): só analítico recebe documento ou rateio.</summary>
    public AccountingEntityClass Class { get; set; } = AccountingEntityClass.Analytic;

    /// <summary>Item superior (CTD_ITSUP); precisa ser sintético.</summary>
    [Column(TypeName = "VARCHAR(20)")]
    public string? ParentCode { get; set; }

    public bool Inactive { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime? ValidFrom { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime? ValidTo { get; set; }
}
