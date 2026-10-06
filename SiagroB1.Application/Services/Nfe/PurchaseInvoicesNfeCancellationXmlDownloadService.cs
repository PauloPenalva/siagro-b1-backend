using System.Text;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>XML do evento de cancelamento (procEventoNFe) do documento de entrada: <c>&lt;chave&gt;-procEventoNFe.xml</c>.</summary>
public class PurchaseInvoicesNfeCancellationXmlDownloadService(IUnitOfWork db)
{
    public async Task<(byte[] Bytes, string FileName)> ExecuteAsync(Guid invoiceKey)
    {
        var store = new PurchaseInvoiceNfeStore(db);
        var xml = await store.LatestXmlAsync(invoiceKey, NfeXmlKind.CancellationEvent)
                  ?? throw new NotFoundException("Este documento não tem o XML do cancelamento da NF-e.");
        var invoice = await store.FindReadOnlyAsync(invoiceKey);

        return (Encoding.UTF8.GetBytes(xml), $"{invoice?.ChaveNFe}-procEventoNFe.xml");
    }
}
