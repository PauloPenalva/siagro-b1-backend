using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos;

/// <summary>Romaneio de recebimento elegível ao controle de royalties, com o saldo a baixar no ITS.</summary>
public class RoyaltyBalanceRow
{
    public Guid StorageTransactionKey { get; set; }
    public string? Code { get; set; }
    /// <summary>Receipt (recebimento) ou Purchase (compra — carregamento em terceiro).</summary>
    public StorageTransactionType TransactionType { get; set; }
    public DateTime? TransactionDate { get; set; }
    public string? BranchCode { get; set; }
    public string CardCode { get; set; } = "";
    public string? CardName { get; set; }
    public string? RoyaltyOriginCode { get; set; }
    public string? RoyaltyOriginName { get; set; }
    public string ItemCode { get; set; } = "";
    public string? ItemName { get; set; }
    public string? StorageAddressCode { get; set; }
    public RoyaltySituation Situation { get; set; }
    public string? RoyaltyTestCode { get; set; }
    public decimal NetWeight { get; set; }
    public decimal WrittenOffWeight { get; set; }
    public decimal Balance { get; set; }
}

public class RoyaltyBalanceTotal
{
    public RoyaltySituation Situation { get; set; }
    public int Count { get; set; }
    public decimal NetWeight { get; set; }
    public decimal WrittenOffWeight { get; set; }
    public decimal Balance { get; set; }
}
