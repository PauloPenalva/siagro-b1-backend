using System.Drawing;
using FastReport;
using FastReport.Export.PdfSimple;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

public class StorageAddressReportService(
    IUnitOfWork db, 
    IWebHostEnvironment env,
    IConfiguration configuration
    )
{
    public async Task<byte[]> GeneratePdfAsync(StorageAddressReportRequest request, string userName, CancellationToken ct = default)
    {
        var rows = await BuildQuery(request)
            .OrderBy(x => x.BranchCode)
            .ThenBy(x => x.Code)
            .ToListAsync(ct);

        var reportPath = Path.Combine(env.ContentRootPath, "Reports", "Templates", "StorageAddressesBalance.frx");
        var logoPath = Path.Combine(env.WebRootPath, "images", "logo.jpeg");
        
        using var report = new Report();
        report.Load(reportPath);

        report.RegisterData(rows, "StorageAddresses");
        report.GetDataSource("StorageAddresses")!.Enabled = true;
        
        var picLogo = report.FindObject("picLogo") as PictureObject;
        if (picLogo != null && File.Exists(logoPath))
        {
            using var img = Image.FromFile(logoPath);
            picLogo.Image = (Image)img.Clone();
        }

        var companyName = configuration.GetValue<string>("CompanyName") ?? "Company Name";
        
        FillParameters(report, companyName, userName,request, rows);

        report.Prepare();

        using var ms = new MemoryStream();
        var pdf = new PDFSimpleExport();
        report.Export(pdf, ms);

        return ms.ToArray();
    }

    private IQueryable<StorageAddressReportResponse> BuildQuery(StorageAddressReportRequest filter)
    {
        var query = db.Context.StorageAddresses
            .AsNoTracking()
            .Include(x => x.Transactions)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.BranchCode))
            query = query.Where(x => x.BranchCode == filter.BranchCode);

        if (!string.IsNullOrWhiteSpace(filter.CodeFrom))
            query = query.Where(x => string.Compare(x.Code!, filter.CodeFrom) >= 0);

        if (!string.IsNullOrWhiteSpace(filter.CodeTo))
            query = query.Where(x => string.Compare(x.Code!, filter.CodeTo) <= 0);

        if (filter.CreationDateFrom.HasValue)
            query = query.Where(x => x.CreationDate >= filter.CreationDateFrom.Value.Date);

        if (filter.CreationDateTo.HasValue)
        {
            var dateTo = filter.CreationDateTo.Value.Date;
            query = query.Where(x => x.CreationDate <= dateTo);
        }

        if (filter.OwnershipType.HasValue)
            query = query.Where(x => x.OwnershipType == filter.OwnershipType.Value);

        if (filter.Status.HasValue)
            query = query.Where(x => x.Status == filter.Status.Value);

        if (!string.IsNullOrWhiteSpace(filter.CardCode))
            query = query.Where(x => x.CardCode == filter.CardCode);
        
        if (!string.IsNullOrWhiteSpace(filter.ItemCode))
            query = query.Where(x => x.ItemCode == filter.ItemCode);
        
        if (!string.IsNullOrWhiteSpace(filter.WarehouseCode))
            query = query.Where(x => x.WarehouseCode == filter.WarehouseCode);

       
        if (!string.IsNullOrWhiteSpace(filter.ProcessingCostCode))
            query = query.Where(x => x.ProcessingCostCode == filter.ProcessingCostCode);
        
        var projected = query.Select(x => new StorageAddressReportResponse()
        {
            BranchCode = x.BranchCode,
            Code = x.Code,
            CreationDate = x.CreationDate,
            Description = x.Description,

            OwnershipType = (int)x.OwnershipType,
            OwnershipTypeName = x.OwnershipType.ToString(),

            CardCode = x.CardCode,
            CardName = x.CardName,

            ItemCode = x.ItemCode,
            ItemName = x.ItemName,

            WarehouseCode = x.WarehouseCode,
            WarehouseName = x.WarehouseName,

            Status = (int?)x.Status,
            StatusName = x.Status.HasValue ? x.Status.Value.ToString() : string.Empty,

            UoM = x.UoM,
            ProcessingCostCode = x.ProcessingCostCode,

            TotalReceipt = x.Transactions
                .Where(t =>
                    (t.TransactionType == StorageTransactionType.Receipt ||
                     t.TransactionType == StorageTransactionType.ShipmentReleased) &&
                    (t.TransactionStatus == StorageTransactionsStatus.Confirmed ||
                     t.TransactionStatus == StorageTransactionsStatus.Invoiced))
                .Sum(t => (decimal?)t.NetWeight) ?? 0m,

            TotalShipment = x.Transactions
                .Where(t =>
                    (t.TransactionType == StorageTransactionType.Shipment ||
                     t.TransactionType == StorageTransactionType.SalesShipment) &&
                    (t.TransactionStatus == StorageTransactionsStatus.Confirmed ||
                     t.TransactionStatus == StorageTransactionsStatus.Invoiced))
                .Sum(t => (decimal?)t.NetWeight) ?? 0m,

            TotalQualityLoss = x.Transactions
                .Where(t =>
                    t.TransactionType == StorageTransactionType.TechnicalLoss &&
                    (t.TransactionStatus == StorageTransactionsStatus.Confirmed ||
                     t.TransactionStatus == StorageTransactionsStatus.Invoiced))
                .Sum(t => (decimal?)t.NetWeight) ?? 0m,

            Balance =
                ((x.Transactions
                    .Where(t =>
                        (t.TransactionType == StorageTransactionType.Receipt ||
                         t.TransactionType == StorageTransactionType.ShipmentReleased) &&
                        (t.TransactionStatus == StorageTransactionsStatus.Confirmed ||
                         t.TransactionStatus == StorageTransactionsStatus.Invoiced))
                    .Sum(t => (decimal?)t.NetWeight) ?? 0m)
                -
                ((x.Transactions
                    .Where(t =>
                        (t.TransactionType == StorageTransactionType.Shipment ||
                         t.TransactionType == StorageTransactionType.SalesShipment) &&
                        (t.TransactionStatus == StorageTransactionsStatus.Confirmed ||
                         t.TransactionStatus == StorageTransactionsStatus.Invoiced))
                    .Sum(t => (decimal?)t.NetWeight) ?? 0m)
                +
                (x.Transactions
                    .Where(t =>
                        t.TransactionType == StorageTransactionType.TechnicalLoss &&
                        (t.TransactionStatus == StorageTransactionsStatus.Confirmed ||
                         t.TransactionStatus == StorageTransactionsStatus.Invoiced))
                    .Sum(t => (decimal?)t.NetWeight) ?? 0m)))
        });

        if (filter.OnlyWithBalance)
            projected = projected.Where(x => x.Balance > 0);

        return projected;
    }

    private static void FillParameters(Report report, string companyName, string userName, StorageAddressReportRequest filter, List<StorageAddressReportResponse> rows)
    {
        report.SetParameterValue("pCompanyName", companyName ?? "");
        report.SetParameterValue("pTitle", "Relatório de Saldos por Lote");
        report.SetParameterValue("pBranchCode", filter.BranchCode ?? "");
        report.SetParameterValue("pCodeFrom", filter.CodeFrom ?? "");
        report.SetParameterValue("pCodeTo", filter.CodeTo ?? "");
        report.SetParameterValue("pCreationDateFrom", filter.CreationDateFrom?.ToString("dd/MM/yyyy") ?? "");
        report.SetParameterValue("pCreationDateTo", filter.CreationDateTo?.ToString("dd/MM/yyyy") ?? "");
        report.SetParameterValue("pOwnershipType", filter.OwnershipType?.ToString() ?? "");
        report.SetParameterValue("pStatus", filter.Status?.ToString() ?? "");
        report.SetParameterValue("pCardCode", filter.CardCode ?? "");
        report.SetParameterValue("pItemCode", filter.ItemCode ?? "");
        report.SetParameterValue("pWarehouseCode", filter.WarehouseCode ?? "");
        report.SetParameterValue("pOnlyWithBalance", filter.OnlyWithBalance ? "Sim" : "Não");
        report.SetParameterValue("pPrintedAt", DateTime.Now);
        report.SetParameterValue("pPrintedBy", userName ?? "");
        report.SetParameterValue("pRecordCount", rows.Count);
        report.SetParameterValue("pTotalReceipt", rows.Sum(x => x.TotalReceipt));
        report.SetParameterValue("pTotalShipment", rows.Sum(x => x.TotalShipment));
        report.SetParameterValue("pTotalQualityLoss", rows.Sum(x => x.TotalQualityLoss));
        report.SetParameterValue("pTotalBalance", rows.Sum(x => x.Balance));
    }
}