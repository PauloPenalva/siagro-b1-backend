using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos;

/// <summary>Situação de uma etapa da cadeia de custódia (spec 2026-10-12 §5).</summary>
public enum StorageCustodyStepSituation
{
    /// <summary>Sem nota ativa.</summary>
    Missing = 0,
    /// <summary>Nota própria sem NF-e autorizada, ou escrituração ainda Pendente.</summary>
    Generated = 1,
    /// <summary>NF-e própria autorizada.</summary>
    Authorized = 2,
    /// <summary>Escrituração da NF-e do depositante confirmada.</summary>
    Recorded = 3,
}

/// <summary>Romaneio de Recepção/Expedição em lote de terceiro, com o lote e a transferência (StorageCustodyQuery).</summary>
public sealed class StorageCustodyTransactionRow
{
    public Guid Key { get; set; }
    public string? Code { get; set; }
    public StorageTransactionType TransactionType { get; set; }
    public StorageTransactionsStatus TransactionStatus { get; set; }
    public DateTime? TransactionDate { get; set; }
    public decimal GrossWeight { get; set; }
    public decimal NetWeight { get; set; }
    public string? TruckCode { get; set; }
    public bool IsOwnershipTransfer { get; set; }
    public bool TransferCancelled { get; set; }
    public string StorageAddressCode { get; set; } = "";
    public string? BranchCode { get; set; }
    public string CardCode { get; set; } = "";
    public string? CardName { get; set; }
    public string ItemCode { get; set; } = "";
    public string? ItemName { get; set; }
    public string UoM { get; set; } = "";
    public StorageOwnershipType OwnershipType { get; set; }
    public StorageAddressNature Nature { get; set; }
    public bool RequiresProcessing { get; set; }
}

/// <summary>Nota de custódia ATIVA (não cancelada) de um romaneio — entrada ou saída.</summary>
public sealed class StorageCustodyDocumentRow
{
    public Guid TransactionKey { get; set; }
    public StorageCustodyRole Role { get; set; }
    public Guid DocumentKey { get; set; }
    public bool IsSalesInvoice { get; set; }
    /// <summary>Documento de saída é sempre próprio.</summary>
    public DocumentIssuerType IssuerType { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? TaxDocumentNumber { get; set; }
    public string? TaxDocumentSeries { get; set; }
    public string? ChaveNFe { get; set; }
    public InvoiceStatus InvoiceStatus { get; set; }
    public NfeStatus NfeStatus { get; set; }
    public DateTime? IssueDate { get; set; }
    public decimal Quantity { get; set; }
    public string? Cfop { get; set; }
}

/// <summary>Uma etapa esperada do romaneio, com a nota que a cumpre (ou nenhuma).</summary>
public sealed class StorageCustodyStepDto
{
    public StorageCustodyRole Role { get; set; }
    public string RoleText { get; set; } = "";
    public StorageCustodyStepSituation Situation { get; set; }
    public string SituationText { get; set; } = "";
    public Guid? DocumentKey { get; set; }
    public bool IsSalesInvoice { get; set; }
    /// <summary>Número interno (DE…/DS…).</summary>
    public string? InvoiceNumber { get; set; }
    /// <summary>Número da NF quando existe; senão o interno.</summary>
    public string? Number { get; set; }
    public string? TaxDocumentSeries { get; set; }
    public string? ChaveNFe { get; set; }
    public DateTime? IssueDate { get; set; }
    public string? Cfop { get; set; }
    public NfeStatus? NfeStatus { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? ExpectedQuantity { get; set; }
    /// <summary>Quantidade da nota − quantidade esperada (balança, ou nota do papel 1 na devolução simbólica).</summary>
    public decimal? Difference { get; set; }
}

/// <summary>Seção "Notas fiscais de custódia" do romaneio.</summary>
public sealed class StorageCustodySummaryDto
{
    public Guid TransactionKey { get; set; }
    /// <summary>False = romaneio fora do escopo (lote próprio, não confirmado, transferência cancelada…).</summary>
    public bool Applies { get; set; }
    /// <summary>Primeira etapa sem nota; None quando completa.</summary>
    public StorageCustodyRole NextRole { get; set; }
    public List<StorageCustodyStepDto> Steps { get; set; } = [];
}
