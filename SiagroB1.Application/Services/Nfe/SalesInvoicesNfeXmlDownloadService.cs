using System.Text;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra;
using SiagroB1.Infra.Nfe;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>XML autorizado (procNFe) para download: <c>&lt;chave&gt;-procNFe.xml</c>.</summary>
public class SalesInvoicesNfeXmlDownloadService(IUnitOfWork db)
{
    public async Task<(byte[] Bytes, string FileName)> ExecuteAsync(Guid invoiceKey)
    {
        var xml = await db.Context.SalesInvoiceNfeXmls.LatestAuthorizedXmlAsync(invoiceKey);

        var accessKey = await db.Context.SalesInvoices.AsNoTracking()
            .Where(i => i.Key == invoiceKey)
            .Select(i => i.ChaveNFe)
            .FirstOrDefaultAsync();

        return (Encoding.UTF8.GetBytes(xml), $"{accessKey}-procNFe.xml");
    }
}
