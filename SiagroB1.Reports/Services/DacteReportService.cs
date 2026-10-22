using System.Drawing;
using FastReport;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

/// <summary>
/// DACTE (PDF do CT-e) a partir do <c>cteProc</c> autorizado gravado em TRANSPORT_DOCUMENT_XMLS — nunca da
/// entidade, para refletir o que a SEFAZ autorizou. Leiaute próprio (Reports/Templates/Dacte.frx; não há DACTE
/// da Zeus). Homologação sai com a tarja "SEM VALOR FISCAL"; cancelado, com "CANCELADO" (marca d'água da página).
/// Logo e nome da empresa pelo <see cref="ReportHeaderService"/> (picLogo/pCompanyName), como os demais modelos.
/// </summary>
public class DacteReportService(IUnitOfWork db, IWebHostEnvironment env, ReportHeaderService header)
{
    public const string NotAuthorizedMessage = "CT-e não autorizado: o DACTE só sai de CT-e autorizado pela SEFAZ.";

    public async Task<byte[]> RenderAsync(Guid key)
    {
        var dto = await LoadAsync(key);
        using var report = Prepare(dto);
        return PdfExporter.Export(report, $"DACTE {dto.AccessKey}");
    }

    public async Task<DacteDto> LoadAsync(Guid key)
    {
        var doc = await db.Context.TransportDocuments.AsNoTracking()
                      .Where(x => x.Key == key)
                      .Select(x => new { x.Status, x.CancellationProtocol })
                      .FirstOrDefaultAsync()
                  ?? throw new DefaultException("CT-e não encontrado.");

        if (doc.Status is not (CteStatus.Authorized or CteStatus.Cancelled))
            throw new DefaultException(NotAuthorizedMessage);

        var xml = await db.Context.TransportDocumentXmls.AsNoTracking()
                      .Where(x => x.TransportDocumentKey == key && x.Kind == CteXmlKind.Authorized)
                      .Select(x => x.Xml)
                      .FirstOrDefaultAsync()
                  ?? throw new DefaultException(NotAuthorizedMessage);

        var cancelled = doc.Status == CteStatus.Cancelled || !string.IsNullOrEmpty(doc.CancellationProtocol);
        return DacteXmlReader.Read(xml, cancelled);
    }

    /// <summary>Carrega o modelo, preenche e prepara. Quem chama descarta o relatório.</summary>
    public Report Prepare(DacteDto dto)
    {
        FastReport.Utils.Config.WebMode = true;
        var report = new Report();
        try
        {
            report.Load(Path.Combine(env.ContentRootPath, "Reports", "Templates", "Dacte.frx"));
            header.Apply(report);

            foreach (var property in typeof(DacteDto).GetProperties().Where(p => p.PropertyType == typeof(string)))
                report.SetParameterValue("p" + property.Name, property.GetValue(dto) as string ?? "");

            var stamp = dto.Cancelled ? "CANCELADO" : dto.Homologation ? "SEM VALOR FISCAL" : null;
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
