using System.Drawing;
using FastReport;
using Microsoft.EntityFrameworkCore;
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

        return DamdfeXmlReader.Read(xml, status == MdfeStatus.Cancelled, status == MdfeStatus.Closed);
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

            var stamp = dto.Cancelled ? "CANCELADO" : dto.Closed ? "ENCERRADO" : dto.Homologation ? "SEM VALOR FISCAL" : null;
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
