using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Interfaces;

public interface IFreightInvoicePrintDataService
{
    Task<FreightInvoicePrintDto> GetAsync(Guid freightInvoiceKey, CancellationToken ct = default);
}
