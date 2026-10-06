using System.Text;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>XML (procEventoNFe) de uma CC-e: <c>&lt;chave&gt;-cce-&lt;seq&gt;-procEventoNFe.xml</c>.</summary>
public abstract class NfeCorrectionXmlDownloadServiceBase<TDocument>(INfeDocumentStore<TDocument> store)
    where TDocument : class, INfeDocument
{
    public async Task<(byte[] Bytes, string FileName)> ExecuteAsync(Guid invoiceKey, int sequence)
    {
        var xml = await store.CorrectionXmlAsync(invoiceKey, sequence)
                  ?? throw new NotFoundException("Carta de correção não encontrada ou sem o XML do evento.");
        var invoice = await store.FindReadOnlyAsync(invoiceKey);

        return (Encoding.UTF8.GetBytes(xml), $"{invoice?.ChaveNFe}-cce-{sequence}-procEventoNFe.xml");
    }
}
