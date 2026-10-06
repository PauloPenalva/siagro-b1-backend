using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

public class PurchaseInvoicesNfeCorrectionXmlDownloadService(IUnitOfWork db)
    : NfeCorrectionXmlDownloadServiceBase<PurchaseInvoice>(new PurchaseInvoiceNfeStore(db));
