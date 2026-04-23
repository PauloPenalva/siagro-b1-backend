using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Interfaces;

namespace SiagroB1.Reports.Services;

using Microsoft.EntityFrameworkCore;

public class StorageStatementReportService(IUnitOfWork db) : IStorageStatementReportService
{
    public async Task<(List<StorageStatementReportRowDto> rows, List<StorageStatementReportHeaderDto> header)>
    GetReportAsync(StorageStatementReportFilter filter, CancellationToken ct)
    {
        var query = db.Context.StorageTransactions
            .AsNoTracking()
            .Where(x => x.TransactionStatus != StorageTransactionsStatus.Cancelled)
            .Include(x => x.QualityInspections)
            .AsQueryable();

        if (filter.DateStart.HasValue)
            query = query.Where(x => x.TransactionDate >= filter.DateStart);

        if (filter.DateEnd.HasValue)
            query = query.Where(x => x.TransactionDate <= filter.DateEnd);

        var data = await query
            .OrderBy(x => x.CardCode)
            .ThenBy(x => x.ItemCode)
            .ThenBy(x => x.StorageAddressCode)
            .ThenBy(x => x.TransactionDate)
            .ThenBy(x => x.TransactionTime)
            .ToListAsync(ct);

        var rows = new List<StorageStatementReportRowDto>();

        decimal runningBalance = 0;
        string currentGroup = "";

        decimal groupEntrada = 0;
        decimal groupSaida = 0;
        decimal groupServicos = 0;

        foreach (var t in data)
        {
            var groupKey = $"{t.CardCode}|{t.ItemCode}|{t.StorageAddressCode}";

            if (groupKey != currentGroup)
            {
                // fecha grupo anterior
                if (rows.Count > 0)
                {
                    var last = rows.Last();
                    last.IsGroupLastRow = true;
                    last.GroupTotalEntrada = groupEntrada;
                    last.GroupTotalSaida = groupSaida;
                    last.GroupTotalServicos = groupServicos;
                }

                // reset
                runningBalance = 0;
                groupEntrada = 0;
                groupSaida = 0;
                groupServicos = 0;
                currentGroup = groupKey;
            }

            var entrada = (t.TransactionType == StorageTransactionType.Receipt ||
                           t.TransactionType == StorageTransactionType.Purchase)
                ? t.NetWeight : 0;

            var saida = (t.TransactionType == StorageTransactionType.Shipment ||
                         t.TransactionType == StorageTransactionType.SalesShipment)
                ? t.NetWeight : 0;

            runningBalance += entrada - saida;

            var totalServicos =
                t.DryingServicePrice +
                t.CleaningServicePrice +
                t.ShipmentPrice +
                t.ReceiptServicePrice;

            groupEntrada += entrada;
            groupSaida += saida;
            groupServicos += totalServicos;

            rows.Add(new StorageStatementReportRowDto
            {
                TransactionDate = t.TransactionDate,
                TransactionTime = t.TransactionTime,
                Code = t.Code,
                TransactionTypeName = t.TransactionType.ToString(),
                TransactionStatusName = t.TransactionStatus.ToString(),
                CardCode = t.CardCode,
                CardName = t.CardName,
                ItemCode = t.ItemCode,
                ItemName = t.ItemName,
                StorageAddressCode = t.StorageAddressCode,
                GrossWeight = t.GrossWeight,
                NetWeight = t.NetWeight,
                QtyEntrada = entrada,
                QtySaida = saida,
                RunningBalance = runningBalance,

                Umidade = GetQuality(t, "001"),
                Impurezas = GetQuality(t, "002"),
                Avariados = GetQuality(t, "003"),
                Ardidos = GetQuality(t, "004"),
                PH = GetQuality(t, "005"),
                FN = GetQuality(t, "006"),

                DryingServicePrice = t.DryingServicePrice,
                CleaningServicePrice = t.CleaningServicePrice,
                ShipmentPrice = t.ShipmentPrice,
                ReceiptServicePrice = t.ReceiptServicePrice,
                TotalServiceValue = totalServicos,

                GroupKey = groupKey,
                GroupDescription = $"{t.CardCode} | {t.ItemCode} | {t.StorageAddressCode}"
            });
        }

        // fecha último grupo
        if (rows.Count > 0)
        {
            var last = rows.Last();
            last.IsGroupLastRow = true;
            last.GroupTotalEntrada = groupEntrada;
            last.GroupTotalSaida = groupSaida;
            last.GroupTotalServicos = groupServicos;
        }

        var header = new List<StorageStatementReportHeaderDto>
        {
            new()
            {
                TotalEntrada = rows.Sum(x => x.QtyEntrada),
                TotalSaida = rows.Sum(x => x.QtySaida),
                TotalServicos = rows.Sum(x => x.TotalServiceValue)
            }
        };

        return (rows, header);
    }

    private static decimal GetQuality(StorageTransaction t, string code)
    {
        return t.QualityInspections?
            .FirstOrDefault(q => q.QualityAttribCode == code)?.Value ?? 0;
    }
}