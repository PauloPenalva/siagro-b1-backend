using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Mdfe;

/// <summary>
/// ZIP com o mdfeProc de cada MDF-e autorizado (MDFe{chave}.xml); encerrado e cancelado também têm mdfeProc.
/// Os eventos gravados seguem junto, nomeados pela chave: -enc (encerramento), -canc (cancelamento) e -condutor
/// (inclusão de condutor). Quem não foi autorizado é ignorado sem erro.
/// </summary>
public class MdfeXmlZipService(IUnitOfWork db, IConfiguration configuration)
{
    public const string NothingAuthorized = "Nenhum MDF-e autorizado entre os selecionados.";

    public async Task<byte[]> BuildAsync(IReadOnlyList<Guid> keys)
    {
        TransportManifestsCreateFromCtesService.EnsureStandalone(configuration);

        var distinct = keys.Distinct().ToList();
        if (distinct.Count > TransportManifestsCloseBatchService.MaxKeys)
            throw new DefaultException($"Selecione no máximo {TransportManifestsCloseBatchService.MaxKeys} MDF-e por vez.");

        var docs = await db.Context.TransportManifests.AsNoTracking()
            .Where(x => distinct.Contains(x.Key)
                        && (x.Status == MdfeStatus.Authorized || x.Status == MdfeStatus.Closed || x.Status == MdfeStatus.Cancelled))
            .Select(x => new
            {
                x.AccessKey,
                Xmls = x.Xmls.OrderBy(m => m.CreatedAt).Select(m => new { m.Kind, m.Xml }).ToList(),
            })
            .ToListAsync();

        var withXml = docs.Where(d => d.AccessKey is not null && d.Xmls.Any(m => m.Kind == MdfeXmlKind.Authorized)).ToList();
        if (withXml.Count == 0)
            throw new DefaultException(NothingAuthorized);

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var d in withXml)
            {
                Add(zip, $"MDFe{d.AccessKey}.xml", d.Xmls.First(m => m.Kind == MdfeXmlKind.Authorized).Xml);
                foreach (var group in d.Xmls.Where(m => Suffix(m.Kind) is not null).GroupBy(m => m.Kind))
                {
                    var i = 0;
                    foreach (var m in group)
                        Add(zip, $"MDFe{d.AccessKey}-{Suffix(m.Kind)}{(i++ == 0 ? "" : $"-{i}")}.xml", m.Xml);
                }
            }
        }

        return stream.ToArray();
    }

    private static string? Suffix(MdfeXmlKind kind) => kind switch
    {
        MdfeXmlKind.Closing => "enc",
        MdfeXmlKind.Cancellation => "canc",
        MdfeXmlKind.DriverInclusion => "condutor",
        _ => null,
    };

    private static void Add(ZipArchive zip, string name, string xml)
    {
        using var writer = zip.CreateEntry(name).Open();
        var bytes = new UTF8Encoding(false).GetBytes(xml);
        writer.Write(bytes, 0, bytes.Length);
    }
}
