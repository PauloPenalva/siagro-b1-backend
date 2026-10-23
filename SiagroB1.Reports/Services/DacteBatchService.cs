using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Reports.Services;

/// <summary>ZIP com um DACTE (PDF) por CT-e autorizado ou cancelado; os demais são ignorados sem erro.</summary>
public class DacteBatchService(IUnitOfWork db, DacteReportService dacte)
{
    public const int MaxKeys = 200;
    public const string TooMany = "Selecione no máximo 200 CT-e por vez.";
    public const string NothingAuthorized = "Nenhum CT-e autorizado entre os selecionados.";

    public async Task<byte[]> BuildAsync(IReadOnlyList<Guid> keys)
    {
        var distinct = keys.Distinct().ToList();
        if (distinct.Count > MaxKeys)
            throw new DefaultException(TooMany);

        var eligible = await db.Context.TransportDocuments.AsNoTracking()
            .Where(x => distinct.Contains(x.Key) && (x.Status == CteStatus.Authorized || x.Status == CteStatus.Cancelled))
            .Select(x => x.Key).ToListAsync();

        var files = new List<(string Name, byte[] Pdf)>();
        foreach (var key in eligible)
        {
            try
            {
                var dto = await dacte.LoadAsync(key);
                using var report = dacte.Prepare(dto);
                files.Add(($"{dto.AccessKey}-dacte.pdf", PdfExporter.Export(report, $"DACTE {dto.AccessKey}")));
            }
            catch (DefaultException)
            {
                // XML ausente: o documento não tem DACTE; segue com os demais.
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
