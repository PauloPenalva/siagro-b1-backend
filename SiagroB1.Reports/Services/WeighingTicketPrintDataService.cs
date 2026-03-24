using System.Data;
using FastReport;
using FastReport.Export.PdfSimple;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

using Microsoft.EntityFrameworkCore;

public class WeighingTicketPrintDataService(
    IUnitOfWork db,
    IWebHostEnvironment env,
    IDbConnection connection,
    IFastReportService reportService,
    IStringLocalizer<Resource> resource)
{
    
    public async Task<byte[]> GeneratePdfAsync(Guid key)
    {
        var data = await GetAsync(key);
        var list = new List<WeighingTicketPrintDto> { data };

        var parameters = new Dictionary<string, object>
        {
            ["COMPANY_LOGO"] = "logo.png"
        };
        
        
        var reportPath = Path.Combine(
            env.ContentRootPath,
            "Reports",
            "Templates",
            "WeighingTicket.frx");

        FastReport.Utils.Config.WebMode = true;
        using var report = new Report();
        report.Load(reportPath);
        
        report.RegisterData(list, "Ticket"); 
        report.RegisterData(data.QualityInspections, "QualityInspections");

        report.GetDataSource("Ticket").Enabled = true;
        report.GetDataSource("QualityInspections").Enabled = true;
        
        foreach (var param in parameters)
        {
            report.SetParameterValue(param.Key, param.Value);
        }

        if (!await report.PrepareAsync()) return Array.Empty<byte>();
        var pdfExport = new PDFSimpleExport();
        pdfExport.ShowProgress = false;
        pdfExport.Title = "WeighingTicket";
            
        using var stream = new MemoryStream();
            
        report.Export(pdfExport, stream);

        return stream.ToArray();
    }
    
    
    private async Task<WeighingTicketPrintDto> GetAsync(Guid key)
    {
        var ticket = await db.Context.WeighingTickets
            .AsNoTracking()
            .Include(x => x.Branch)
            .Include(x => x.Truck)
            .Include(x => x.TruckDriver)
            .Include(x => x.StorageAddress)
            .Include(x => x.QualityInspections)
                .ThenInclude(x => x.QualityAttrib)
            .FirstOrDefaultAsync(x => x.Key == key)
            ?? throw new Exception("Ticket não encontrado.");

        if (ticket.Status is not WeighingTicketStatus.Complete)
            throw new BusinessException(resource["WEIGHING_TICKET_STATUS_NOT_COMPLETE"]);
        
        return new WeighingTicketPrintDto
        {
            CompanyName = ticket.Branch?.BranchName,
            Title = "TICKET DE PESAGEM",

            Code = ticket.Code,
            Date = ticket.Date,

            Type = ticket.Type switch
            {
                WeighingTicketType.Receipt => "ENTRADA",
                WeighingTicketType.Shipment => "SAÍDA",
                _ => "PESAGEM"
            },
            
            Status = ticket.Status?.ToString(),
            Stage = ticket.Stage?.ToString(),

            ItemCode = ticket.ItemCode,
            ItemName = ticket.ItemName,

            CardCode = ticket.CardCode,
            CardName = ticket.CardName,

            TruckCode = ticket.TruckCode,
            TruckPlate = ticket.TruckCode,

            TruckDriverCode = ticket.TruckDriverCode,
            TruckDriverName = ticket.TruckDriver?.Name,

            FirstWeighValue = ticket.FirstWeighValue,
            FirstWeighDateTime = ticket.FirstWeighDateTime,

            SecondWeighValue = ticket.SecondWeighValue,
            SecondWeighDateTime = ticket.SecondWeighDateTime,

            GrossWeight = ticket.GrossWeight,

            StorageAddressCode = ticket.StorageAddressCode,
            ProcessingCostCode = ticket.ProcessingCostCode,

            Comments = ticket.Comments,
            
            FirstWeighUsername =  ticket.FirstWeighUsername,
            SecondWeighUsername =  ticket.SecondWeighUsername,

            QualityInspections = ticket.QualityInspections
                .OrderBy(x => x.QualityAttribCode)
                .Select(x => new WeighingTicketQualityPrintDto
                {
                    QualityAttribCode = x.QualityAttribCode,
                    QualityAttribName = x.QualityAttrib != null ? x.QualityAttrib.Name : null,
                    Value = x.Value
                })
                .ToList()
        };
    }
}