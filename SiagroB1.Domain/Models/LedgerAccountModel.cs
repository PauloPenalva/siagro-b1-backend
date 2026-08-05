using System.ComponentModel.DataAnnotations;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Models;

/// <summary>
/// DTO exposto no OData para conta contábil. É alimentado pela tabela local
/// (STANDALONE) ou por OACT (SAPB1), conforme a implementação de ILedgerAccountService.
/// </summary>
public class LedgerAccountModel
{
    [Key]
    public required string Code { get; set; }

    public required string Name { get; set; }

    /// <summary>Não é fornecido pelo SAP: vem nulo em modo SAPB1.</summary>
    public LedgerAccountType? Type { get; set; }

    public bool AllowsPosting { get; set; }

    public bool Inactive { get; set; }
}
