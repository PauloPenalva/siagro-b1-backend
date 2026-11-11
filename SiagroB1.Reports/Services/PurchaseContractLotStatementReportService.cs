using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Extrato do Contrato de Compra com Lotes (GAC-1198): depois da transferência de titularidade, um
/// papel só com o detalhado do(s) lote(s) de origem — romaneios, classificação, descontos e
/// despesas — seguido do extrato do contrato (físico, fiscal e financeiro).
///
/// Não recalcula nada: os lotes vêm de <see cref="StorageLotStatementReportService"/> e o contrato
/// de <see cref="ContractStatementReportService"/>, os mesmos números da Posição do Lote e do
/// Extrato do Contrato. Aqui só se junta e se põe a classificação nos romaneios do contrato.
///
/// "Lote do contrato" = lote de origem de uma transferência de titularidade não cancelada que
/// aponta para o contrato (<see cref="OwnershipTransfer.PurchaseContractKey"/>).
/// </summary>
public class PurchaseContractLotStatementReportService(
    IUnitOfWork db,
    IFastReportService reportService,
    IConfiguration configuration,
    ContractStatementReportService contractStatement,
    StorageLotStatementReportService lotStatement)
{
    /// <summary>Cada lote ocupa uma faixa de seções; o contrato vem depois de todos.</summary>
    public const int LotSectionStep = 10;
    public const int ContractSectionOffset = 100_000;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<byte[]> ExecuteAsync(Guid key, CancellationToken ct = default)
    {
        var data = await BuildAsync(key, ct);

        var parameters = new Dictionary<string, object>
        {
            ["pTitle"] = "EXTRATO DO CONTRATO DE COMPRA COM LOTES",
            ["pHeader"] = data.Header,
            ["pSummaryTitle"] = "QUADRO DE SALDOS",
            ["pSummary"] = data.Summary,
            ["pEmptyText"] = "Nenhuma movimentação para o contrato.",
            ["pFooterNote"] = "Contrato: positivo cumpre o contrato (entrega, nota, título); devoluções e estornos aparecem negativos.",
            ["pRowCount"] = data.Rows.Count,
        };

        return await reportService.GeneratePdfAsync(
            StorageLotStatementReportService.TemplateName, data.Rows,
            StorageLotStatementReportService.DataSourceName, StorageLotStatementReportService.DataSourceName, parameters);
    }

    public sealed record Data(List<LotStatementRowDto> Rows, string Header, string Summary, List<string> LotCodes);

    public async Task<Data> BuildAsync(Guid key, CancellationToken ct = default)
    {
        var context = db.Context;

        // Lança NotFound se o contrato não existe, antes de qualquer outra leitura.
        var statement = await contractStatement.PurchaseAsync(key);

        var lotCodes = await context.OwnershipTransfers.AsNoTracking()
            .Where(t => t.PurchaseContractKey == key && t.TransferStatus != OwnershipTransferStatus.Cancelled)
            .Select(t => t.StorageAddressOriginCode)
            .Distinct()
            .ToListAsync(ct);
        lotCodes.Sort(StringComparer.Ordinal);

        var rows = new List<LotStatementRowDto>();
        var summary = new StringBuilder();

        for (var i = 0; i < lotCodes.Count; i++)
        {
            var lot = await lotStatement.BuildAsync(lotCodes[i], i * LotSectionStep, $"LOTE {lotCodes[i]} - ", ct);
            rows.AddRange(lot.Rows);

            summary.AppendLine($"LOTE {lotCodes[i]} - {lot.Lot.CardName}".TrimEnd(' ', '-'));
            summary.AppendLine(StorageLotStatementReportService.Summary(lot.Lot.UoM, lot.Totals));
            summary.AppendLine();
        }

        rows.AddRange(await ContractRowsAsync(key, statement.Rows, ct));

        summary.AppendLine("CONTRATO");
        summary.Append(statement.Summary);

        var header = statement.Header + "\n" + (lotCodes.Count == 0
            ? "Lotes de armazenagem: nenhum (contrato sem transferência de titularidade)"
            : "Lotes de armazenagem: " + string.Join(", ", lotCodes));

        return new Data(rows, header, summary.ToString(), lotCodes);
    }

    // ------------------------------------------------------------------ seções do contrato

    private async Task<List<LotStatementRowDto>> ContractRowsAsync(
        Guid key, List<ContractStatementRowDto> statementRows, CancellationToken ct)
    {
        // Classificação dos romaneios alocados no contrato, pelo código do romaneio (a linha do
        // extrato só carrega o código).
        var ticketKeys = db.Context.PurchaseContractsAllocations
            .Where(a => a.PurchaseContractKey == key)
            .Select(a => a.StorageTransactionKey);
        var tickets = await db.Context.StorageTransactions.AsNoTracking()
            .Where(t => ticketKeys.Contains(t.Key))
            .Include(t => t.QualityInspections).ThenInclude(q => q.QualityAttrib)
            .ToListAsync(ct);
        var attribs = StorageLotStatementReportService.QualityColumns(tickets);
        var inspectionsByCode = tickets
            .Where(t => !string.IsNullOrEmpty(t.Code))
            .GroupBy(t => t.Code!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().QualityInspections, StringComparer.Ordinal);

        var standalone = !string.Equals(configuration.GetValue<string>("Erp"), "SAPB1", StringComparison.OrdinalIgnoreCase);
        var result = new List<LotStatementRowDto>();

        foreach (var section in statementRows.GroupBy(r => r.Section).OrderBy(g => g.Key))
        {
            var sectionRows = section.ToList();
            var first = sectionRows[0];
            var layout = Layout(section.Key, first, standalone);

            LotStatementRowDto Base(ContractStatementRowDto? source) => new()
            {
                Section = ContractSectionOffset + section.Key,
                SectionTitle = "CONTRATO - " + first.SectionTitle,
                HDate = first.HDate, HDocument = first.HDocument, HMovement = first.HMovement,
                HDetail1 = first.HDetail1, HDetail2 = first.HDetail2,
                HW1 = layout.HW1, HW2 = layout.HW2, HQ1 = layout.HQ1, HQ2 = layout.HQ2, HQ3 = layout.HQ3,
                Date = source?.Date ?? "Total", Document = source?.Document ?? "", Movement = source?.Movement ?? "",
                Detail1 = source?.Detail1 ?? "", Detail2 = source?.Detail2 ?? "",
                Bold = source is null,
            };

            foreach (var r in sectionRows)
            {
                var row = Base(r);
                layout.Fill(row, r);
                if (section.Key == ContractStatementReportService.PhysicalSection)
                {
                    StorageLotStatementReportService.SetQualityHeaders(row, attribs);
                    if (inspectionsByCode.TryGetValue(r.Document, out var inspections))
                        StorageLotStatementReportService.SetQuality(row, inspections, attribs);
                }
                result.Add(row);
            }

            var total = Base(null);
            layout.Fill(total, new ContractStatementRowDto
            {
                Q1 = sectionRows.Sum(r => r.Q1), Q2 = sectionRows.Sum(r => r.Q2),
                Q3 = sectionRows.Sum(r => r.Q3), Q4 = sectionRows.Sum(r => r.Q4),
                A1 = sectionRows.Sum(r => r.A1), A2 = sectionRows.Sum(r => r.A2), A3 = sectionRows.Sum(r => r.A3),
            });
            if (section.Key == ContractStatementReportService.PhysicalSection)
                StorageLotStatementReportService.SetQualityHeaders(total, attribs);
            result.Add(total);
        }

        return result;
    }

    private sealed record SectionLayout(
        string HW1, string HW2, string HQ1, string HQ2, string HQ3,
        Action<LotStatementRowDto, ContractStatementRowDto> Fill);

    /// <summary>
    /// Onde cada número do Extrato do Contrato (Q1..Q4, Price, A1..A3) cai no modelo do lote, que
    /// tem colunas estreitas (W) e três largas (Q). Seção desconhecida vira só texto.
    /// </summary>
    private static SectionLayout Layout(int section, ContractStatementRowDto first, bool standalone) => section switch
    {
        // Físico da compra: bruto, descontos, líquido, alocado.
        ContractStatementReportService.PhysicalSection => new(first.HQ1, first.HQ2, first.HQ3, first.HQ4, "",
            (row, r) => (row.W1, row.W2, row.Q1, row.Q2) = (Qty(r.Q1), Qty(r.Q2), Qty(r.Q3), Qty(r.Q4))),

        // Fiscal: quantidade, preço, valor, ICMS (em branco no SAPB1: o Siagro não calcula tributo).
        ContractStatementReportService.FiscalSection => new(first.HQ1, first.HPrice, first.HA1, standalone ? first.HA2 : "", "",
            (row, r) => (row.W1, row.W2, row.Q1, row.Q2) =
                (Qty(r.Q1), Price(r.Price), Money(r.A1), standalone ? Money(r.A2) : "")),

        ContractStatementReportService.TitlesSection => new("", "", first.HA1, first.HA2, first.HA3,
            (row, r) => (row.Q1, row.Q2, row.Q3) = (Money(r.A1), Money(r.A2), Money(r.A3))),

        ContractStatementReportService.SettlementsSection => new("", "", first.HA1, "", "",
            (row, r) => row.Q1 = Money(r.A1)),

        ContractStatementReportService.FixationsSection => new(first.HQ1, first.HPrice, first.HA1, "", "",
            (row, r) => (row.W1, row.W2, row.Q1) = (Qty(r.Q1), Price(r.Price), Money(r.A1))),

        _ => new("", "", "", "", "", (_, _) => { }),
    };

    private static string Qty(decimal value) => value == 0m ? "" : value.ToString("N3", PtBr);

    private static string Money(decimal value) => value == 0m ? "" : value.ToString("N2", PtBr);

    private static string Price(decimal value) => value == 0m ? "" : value.ToString("N4", PtBr);
}
