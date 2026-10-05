using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>XML autorizado (procNFe) do documento de entrada para download: <c>&lt;chave&gt;-procNFe.xml</c>.</summary>
public class PurchaseInvoicesNfeXmlDownloadService(IUnitOfWork db)
    : NfeXmlDownloadServiceBase<PurchaseInvoice>(new PurchaseInvoiceNfeStore(db));
