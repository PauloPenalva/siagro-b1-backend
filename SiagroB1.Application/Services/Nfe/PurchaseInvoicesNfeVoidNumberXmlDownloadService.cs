using System.Text;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Comprovante da inutilização (procInutNFe) do documento de entrada: <c>&lt;serie&gt;-&lt;numero&gt;-procInutNFe.xml</c>.</summary>
public class PurchaseInvoicesNfeVoidNumberXmlDownloadService(IUnitOfWork db)
{
    public async Task<(byte[] Bytes, string FileName)> ExecuteAsync(Guid invoiceKey)
    {
        var store = new PurchaseInvoiceNfeStore(db);
        var xml = await store.LatestXmlAsync(invoiceKey, NfeXmlKind.NumberVoid)
                  ?? throw new NotFoundException("Esta inutilização não tem comprovante.");
        var invoice = await store.FindReadOnlyAsync(invoiceKey);

        return (Encoding.UTF8.GetBytes(xml), $"{invoice?.TaxDocumentSeries}-{invoice?.TaxDocumentNumber}-procInutNFe.xml");
    }
}
