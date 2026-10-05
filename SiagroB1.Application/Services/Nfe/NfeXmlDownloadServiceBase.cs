using System.Text;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>XML autorizado (procNFe) para download: <c>&lt;chave&gt;-procNFe.xml</c>.</summary>
public abstract class NfeXmlDownloadServiceBase<TDocument>(INfeDocumentStore<TDocument> store)
    where TDocument : class, INfeDocument
{
    public async Task<(byte[] Bytes, string FileName)> ExecuteAsync(Guid invoiceKey)
    {
        var xml = await store.LatestAuthorizedXmlAsync(invoiceKey);
        var invoice = await store.FindReadOnlyAsync(invoiceKey);

        return (Encoding.UTF8.GetBytes(xml), $"{invoice?.ChaveNFe}-procNFe.xml");
    }
}
