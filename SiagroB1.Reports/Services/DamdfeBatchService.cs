using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Reports.Services;

/// <summary>ZIP com um DAMDFE (PDF) por MDF-e autorizado, encerrado ou cancelado; os demais são ignorados sem erro.</summary>
public class DamdfeBatchService(IUnitOfWork db, DamdfeReportService damdfe)
{
    public const int MaxKeys = 50;
    public const string TooMany = "Selecione no máximo 50 MDF-e por vez.";
    public const string NothingAuthorized = "Nenhum MDF-e autorizado entre os selecionados.";

    public async Task<byte[]> BuildAsync(IReadOnlyList<Guid> keys)
    {
        var distinct = keys.Distinct().ToList();
        if (distinct.Count > MaxKeys)
            throw new DefaultException(TooMany);

        var eligible = await db.Context.TransportManifests.AsNoTracking()
            .Where(x => distinct.Contains(x.Key)
                        && (x.Status == MdfeStatus.Authorized || x.Status == MdfeStatus.Closed || x.Status == MdfeStatus.Cancelled))
            .Select(x => x.Key).ToListAsync();

        var files = new List<(string Name, byte[] Pdf)>();
        foreach (var key in eligible)
        {
            try
            {
                var dto = await damdfe.LoadAsync(key);
                using var report = damdfe.Prepare(dto);
                files.Add(($"{dto.AccessKey}-damdfe.pdf", PdfExporter.Export(report, $"DAMDFE {dto.AccessKey}")));
            }
            catch (DefaultException)
            {
                // XML ausente: o documento não tem DAMDFE; segue com os demais.
            }
        }

        if (files.Count == 0)
            throw new DefaultException(NothingAuthorized);

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, pdf) in files)
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(pdf, 0, pdf.Length);
            }
        }

        return stream.ToArray();
    }
}
