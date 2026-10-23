using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Cte;

/// <summary>
/// ZIP com o cteProc de cada CT-e autorizado (CTe{chave}.xml); o cancelado leva também o procEventoCTe de
/// cancelamento (CTe{chave}-canc.xml). Quem não está autorizado/cancelado é ignorado sem erro.
/// </summary>
public class CteXmlZipService(IUnitOfWork db, IConfiguration configuration)
{
    public const string NothingAuthorized = "Nenhum CT-e autorizado entre os selecionados.";

    public async Task<byte[]> BuildAsync(IReadOnlyList<Guid> keys)
    {
        TransportDocumentsSaveService.EnsureStandalone(configuration);

        var distinct = keys.Distinct().ToList();
        if (distinct.Count > CteBatchService.MaxKeys)
            throw new DefaultException(CteBatchService.TooMany);

        var docs = await db.Context.TransportDocuments.AsNoTracking()
            .Where(x => distinct.Contains(x.Key) && (x.Status == CteStatus.Authorized || x.Status == CteStatus.Cancelled))
            .Select(x => new
            {
                x.AccessKey, x.Status,
                Authorized = x.Xmls.Where(m => m.Kind == CteXmlKind.Authorized).Select(m => m.Xml).FirstOrDefault(),
                Cancellation = x.Xmls.Where(m => m.Kind == CteXmlKind.Cancellation).Select(m => m.Xml).FirstOrDefault(),
            })
            .ToListAsync();

        var withXml = docs.Where(d => d.AccessKey is not null && d.Authorized is not null).ToList();
        if (withXml.Count == 0)
            throw new DefaultException(NothingAuthorized);

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var d in withXml)
            {
                Add(zip, $"CTe{d.AccessKey}.xml", d.Authorized!);
                if (d.Status == CteStatus.Cancelled && d.Cancellation is not null)
                    Add(zip, $"CTe{d.AccessKey}-canc.xml", d.Cancellation);
            }
        }

        return stream.ToArray();
    }

    private static void Add(ZipArchive zip, string name, string xml)
    {
        using var writer = zip.CreateEntry(name).Open();
        var bytes = new UTF8Encoding(false).GetBytes(xml);
        writer.Write(bytes, 0, bytes.Length);
    }
}
