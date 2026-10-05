using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>XML autorizado (procNFe) do documento de saída para download: <c>&lt;chave&gt;-procNFe.xml</c>.</summary>
public class SalesInvoicesNfeXmlDownloadService(IUnitOfWork db)
    : NfeXmlDownloadServiceBase<SalesInvoice>(new SalesInvoiceNfeStore(db));
