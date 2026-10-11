namespace SiagroB1.Domain.Dtos;

/// <summary>Romaneio candidato à reconciliação com o retorno de depósito (spec 2026-10-10 §3.5).</summary>
public class StorageReturnTransactionDto
{
    public Guid Key { get; set; }
    public string? Code { get; set; }
    public DateTime? TransactionDate { get; set; }
    public string? TruckCode { get; set; }
    public decimal NetWeight { get; set; }
    public string? UnitOfMeasureCode { get; set; }

    /// <summary>Reconciliado com ESTE retorno.</summary>
    public bool IsReconciled { get; set; }
}
