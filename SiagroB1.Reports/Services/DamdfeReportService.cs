using System.Drawing;
using FastReport;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

/// <summary>
/// DAMDFE (PDF do MDF-e) a partir do <c>mdfeProc</c> autorizado gravado em TRANSPORT_MANIFEST_XMLS — nunca da
/// entidade, para refletir o que a SEFAZ autorizou. Leiaute próprio (Reports/Templates/Damdfe.frx), no molde do
/// DACTE. Tarja (marca d'água): "CANCELADO", "ENCERRADO" ou, em homologação, "SEM VALOR FISCAL".
/// </summary>
public class DamdfeReportService(IUnitOfWork db, IWebHostEnvironment env, ReportHeaderService header)
{
    public const string NotAuthorizedMessage = "MDF-e não autorizado: o DAMDFE só sai de MDF-e autorizado pela SEFAZ.";

    public async Task<byte[]> RenderAsync(Guid key)
    {
        var dto = await LoadAsync(key);
        using var report = Prepare(dto);
        return PdfExporter.Export(report, $"DAMDFE {dto.AccessKey}");
    }

    public async Task<DamdfeDto> LoadAsync(Guid key)
    {
        var status = await db.Context.TransportManifests.AsNoTracking()
                         .Where(x => x.Key == key)
                         .Select(x => (MdfeStatus?)x.Status)
                         .FirstOrDefaultAsync()
                     ?? throw new DefaultException("MDF-e não encontrado.");

        if (status is not (MdfeStatus.Authorized or MdfeStatus.Closed or MdfeStatus.Cancelled))
            throw new DefaultException(NotAuthorizedMessage);

        var xml = await db.Context.TransportManifestXmls.AsNoTracking()
                      .Where(x => x.ManifestKey == key && x.Kind == MdfeXmlKind.Authorized)
                      .Select(x => x.Xml)
                      .FirstOrDefaultAsync()
                  ?? throw new DefaultException(NotAuthorizedMessage);

        var dto = DamdfeXmlReader.Read(xml, status == MdfeStatus.Cancelled, status == MdfeStatus.Closed);

        // O XML autorizado não traz os condutores incluídos por evento (110114): saem das linhas gravadas.
        var included = await db.Context.Set<TransportManifestDriver>().AsNoTracking()
            .Where(d => d.ManifestKey == key && d.AddedByEvent)
            .OrderBy(d => d.EventSequence).ThenBy(d => d.LineNum)
            .Select(d => new { d.Cpf, d.Name })
            .ToListAsync();
        var lines = dto.Drivers.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (var d in included)
        {
            var line = $"{DamdfeXmlReader.TaxId(d.Cpf)}  {d.Name}";
            if (!lines.Contains(line)) lines.Add(line);
        }
        dto.Drivers = string.Join("\n", lines);
        return dto;
    }

    /// <summary>
    /// Tarja da página: situação (CANCELADO/ENCERRADO) e, em homologação, sempre "SEM VALOR FISCAL" junto.
    /// Nulo quando autorizado em produção.
    /// </summary>
    public static string? Stamp(DamdfeDto dto)
    {
        var situation = dto.Cancelled ? "CANCELADO" : dto.Closed ? "ENCERRADO" : null;
        return (situation, dto.Homologation) switch
        {
            (null, false) => null,
            (null, true) => "SEM VALOR FISCAL",
            (_, false) => situation,
            _ => $"{situation} - SEM VALOR FISCAL",
        };
    }

    /// <summary>Carrega o modelo, preenche e prepara. Quem chama descarta o relatório.</summary>
    public Report Prepare(DamdfeDto dto)
    {
        FastReport.Utils.Config.WebMode = true;
        var report = new Report();
        try
        {
            report.Load(Path.Combine(env.ContentRootPath, "Reports", "Templates", "Damdfe.frx"));
            header.Apply(report);

            foreach (var property in typeof(DamdfeDto).GetProperties().Where(p => p.PropertyType == typeof(string)))
                report.SetParameterValue("p" + property.Name, property.GetValue(dto) as string ?? "");
            report.RegisterData(dto.DocumentLines, "DocumentLines");
            report.GetDataSource("DocumentLines").Enabled = true;

            var stamp = Stamp(dto);
            if (stamp is not null && report.Pages[0] is ReportPage page)
            {
                page.Watermark.Enabled = true;
                page.Watermark.Text = stamp;
                page.Watermark.Font = new Font("Arial", 60, FontStyle.Bold);
                page.Watermark.TextFill = new FastReport.SolidFill(Color.FromArgb(70, Color.Red));
                page.Watermark.TextRotation = FastReport.WatermarkTextRotation.ForwardDiagonal;
                page.Watermark.ShowTextOnTop = true;
            }

            report.Prepare();
            return report;
        }
        catch
        {
            report.Dispose();
            throw;
        }
    }
}
