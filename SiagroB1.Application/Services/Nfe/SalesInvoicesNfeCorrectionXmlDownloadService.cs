using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

public class SalesInvoicesNfeCorrectionXmlDownloadService(IUnitOfWork db)
    : NfeCorrectionXmlDownloadServiceBase<SalesInvoice>(new SalesInvoiceNfeStore(db));
