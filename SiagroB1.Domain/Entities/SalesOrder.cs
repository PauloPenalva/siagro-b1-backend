using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

public class SalesOrder : BaseEntity
{

    public ICollection<StorageTransaction> Origins { get; set; } = [];
    
    public ICollection<SalesInvoice> Invoices { get; set; } = [];
}