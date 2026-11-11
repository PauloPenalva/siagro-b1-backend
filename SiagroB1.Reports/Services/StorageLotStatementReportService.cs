using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.PartnerSources;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Posição do Lote de Armazenagem (GAC-1198): o documento que o armazém entrega ao produtor quando
/// ele pergunta pelo saldo. Lista os romaneios que formam o saldo, com classificação e descontos,
/// o saldo corrido, e as despesas do lote (serviços dos romaneios e cobranças de armazenagem).
///
/// O saldo segue a mesma regra de <see cref="StorageAddress.TotalReceipt"/> /
/// <see cref="StorageAddress.TotalShipment"/> / <see cref="StorageAddress.TotalQualityLoss"/>, para
/// o número do papel bater com o da tela de lotes. Mudou a regra lá, muda aqui.
///
/// Vale nos dois modos de ERP: tudo sai do banco do Siagro, menos o CPF/CNPJ e a cidade do
/// depositante, que passam pelo <see cref="IPartnerSource"/> (OCRD no SAPB1, cadastro local no
/// STANDALONE).
/// </summary>
public class StorageLotStatementReportService(
    IUnitOfWork db,
    IFastReportService reportService,
    IPartnerSource partners,
    ILogger<StorageLotStatementReportService>? logger = null)
{
    public const string TemplateName = "StorageLotStatement.frx";
    public const string DataSourceName = "Rows";

    public const int MovementSection = 1;
    public const int TicketServicesSection = 2;
    public const int ChargesSection = 3;

    /// <summary>Colunas de classificação que cabem na página.</summary>
    public const int MaxQualityColumns = 6;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<byte[]> ExecuteAsync(string code, CancellationToken ct = default)
    {
        var data = await BuildAsync(code, ct: ct);

        var parameters = new Dictionary<string, object>
        {
            ["pTitle"] = "POSIÇÃO DO LOTE DE ARMAZENAGEM",
            ["pHeader"] = await HeaderAsync(data.Lot, ct),
            ["pSummaryTitle"] = "RESUMO DO LOTE",
            ["pSummary"] = Summary(data.Lot.UoM, data.Totals),
            ["pEmptyText"] = "Nenhum romaneio confirmado no lote.",
            ["pFooterNote"] = "Entram no saldo os romaneios confirmados ou faturados. Pesos em " + data.Lot.UoM + ".",
            ["pRowCount"] = data.Rows.Count,
        };

        return await reportService.GeneratePdfAsync(TemplateName, data.Rows, DataSourceName, DataSourceName, parameters);
    }

    public sealed record LotTotals(
        decimal Receipts, decimal Shipments, decimal TechnicalLoss, decimal TicketServices, decimal Charges,
        decimal Invoiced, int HiddenQualityAttribs)
    {
        public decimal Balance => Receipts - Shipments - TechnicalLoss;
        public decimal Expenses => TicketServices + Charges;
        public decimal ToInvoice => Expenses - Invoiced;
    }

    public sealed record LotStatementData(StorageAddress Lot, List<LotStatementRowDto> Rows, LotTotals Totals);

    /// <summary>
    /// Monta as seções do lote. <paramref name="sectionOffset"/> e <paramref name="titlePrefix"/>
    /// existem para o Extrato do Contrato com Lotes, que imprime vários lotes no mesmo papel.
    /// </summary>
    public async Task<LotStatementData> BuildAsync(
        string code, int sectionOffset = 0, string titlePrefix = "", CancellationToken ct = default)
    {
        var context = db.Context;
        var lot = await context.StorageAddresses.AsNoTracking().FirstOrDefaultAsync(a => a.Code == code, ct)
                  ?? throw new NotFoundException("Lote de armazenagem não encontrado.");

        // Só o que forma o saldo: pendente, cancelado e devolvido ficam fora, como na tela de lotes.
        var transactions = await context.StorageTransactions.AsNoTracking()
            .Where(t => t.StorageAddressCode == code
                        && (t.TransactionStatus == StorageTransactionsStatus.Confirmed
                            || t.TransactionStatus == StorageTransactionsStatus.Invoiced))
            .Include(t => t.QualityInspections).ThenInclude(q => q.QualityAttrib)
            .ToListAsync(ct);
        transactions = transactions.OrderBy(t => t.TransactionDate).ThenBy(t => t.RowId).ToList();

        var movements = transactions.Where(t => Direction(t.TransactionType) != 0).ToList();
        var attribs = QualityColumns(movements);

        var rows = new List<LotStatementRowDto>();
        decimal receipts = 0m, shipments = 0m, technicalLoss = 0m, balance = 0m;

        foreach (var t in movements)
        {
            var isReceipt = Direction(t.TransactionType) > 0;
            if (isReceipt) receipts += t.NetWeight;
            else if (t.TransactionType == StorageTransactionType.TechnicalLoss) technicalLoss += t.NetWeight;
            else shipments += t.NetWeight;
            balance += isReceipt ? t.NetWeight : -t.NetWeight;

            var row = Movement(new LotStatementRowDto
            {
                Date = Day(t.TransactionDate),
                Document = t.Code ?? "",
                Movement = MovementText(t),
                Detail1 = t.TruckCode ?? "",
                Detail2 = InvoiceRef(t.InvoiceNumber, t.InvoiceSerie),
                W1 = Weight(t.GrossWeight),
                W2 = Weight(t.DryingDiscount),
                W3 = Weight(t.CleaningDiscount),
                W4 = Weight(t.OthersDicount),
                Q1 = isReceipt ? Weight(t.NetWeight) : "",
                Q2 = isReceipt ? "" : Weight(t.NetWeight),
                Q3 = balance.ToString("N3", PtBr),
            }, attribs);
            SetQuality(row, t.QualityInspections, attribs);
            rows.Add(row);
        }

        if (movements.Count > 0)
        {
            rows.Add(Movement(new LotStatementRowDto
            {
                Bold = true,
                Date = "Total",
                W1 = Weight(movements.Sum(t => t.GrossWeight)),
                W2 = Weight(movements.Sum(t => t.DryingDiscount)),
                W3 = Weight(movements.Sum(t => t.CleaningDiscount)),
                W4 = Weight(movements.Sum(t => t.OthersDicount)),
                Q1 = Weight(receipts),
                Q2 = Weight(shipments + technicalLoss),
                Q3 = balance.ToString("N3", PtBr),
            }, attribs));
        }

        // Serviços cobrados no próprio romaneio (secagem, limpeza, recepção, expedição).
        decimal ticketServices = 0m, invoiced = 0m;
        var serviced = transactions.Where(t => ServiceTotal(t) != 0m).ToList();
        foreach (var t in serviced)
        {
            var total = ServiceTotal(t);
            ticketServices += total;
            if (t.IsInvoiced) invoiced += total;
            rows.Add(TicketServices(new LotStatementRowDto
            {
                Date = Day(t.TransactionDate),
                Document = t.Code ?? "",
                Movement = MovementText(t),
                Detail2 = t.IsInvoiced ? "Faturado" : "A faturar",
                W1 = Money(t.DryingServicePrice),
                W2 = Money(t.CleaningServicePrice),
                W3 = Money(t.ReceiptServicePrice),
                W4 = Money(t.ShipmentPrice),
                Q1 = Money(total),
            }));
        }

        if (serviced.Count > 0)
        {
            rows.Add(TicketServices(new LotStatementRowDto
            {
                Bold = true,
                Date = "Total",
                W1 = Money(serviced.Sum(t => t.DryingServicePrice)),
                W2 = Money(serviced.Sum(t => t.CleaningServicePrice)),
                W3 = Money(serviced.Sum(t => t.ReceiptServicePrice)),
                W4 = Money(serviced.Sum(t => t.ShipmentPrice)),
                Q1 = Money(ticketServices),
            }));
        }

        // Cobranças calculadas pelo processamento diário (armazenagem, quebra técnica, expurgo).
        var charges = await context.StorageCharges.AsNoTracking()
            .Where(c => c.StorageAddressCode == code)
            .ToListAsync(ct);
        charges = charges.OrderBy(c => c.PeriodStart).ThenBy(c => c.ChargeType).ToList();

        decimal chargesTotal = 0m;
        foreach (var c in charges)
        {
            chargesTotal += c.TotalAmount;
            if (c.IsInvoiced) invoiced += c.TotalAmount;
            rows.Add(Charges(new LotStatementRowDto
            {
                Date = Day(c.PeriodStart),
                Document = Day(c.PeriodEnd),
                Movement = ChargeText(c.ChargeType),
                Detail2 = c.IsInvoiced ? "Faturado" : "A faturar",
                W1 = Weight(c.BaseQuantity),
                W2 = c.UnitPriceOrRate == 0m ? "" : c.UnitPriceOrRate.ToString("0.########", PtBr),
                Q1 = Money(c.TotalAmount),
                Q2 = Weight(c.TotalQuantityLoss),
            }));
        }

        if (charges.Count > 0)
        {
            rows.Add(Charges(new LotStatementRowDto
            {
                Bold = true,
                Date = "Total",
                Q1 = Money(chargesTotal),
                Q2 = Weight(charges.Sum(c => c.TotalQuantityLoss)),
            }));
        }

        foreach (var row in rows)
        {
            row.Section += sectionOffset;
            row.SectionTitle = titlePrefix + row.SectionTitle;
        }

        var hidden = movements.SelectMany(t => t.QualityInspections).Select(q => q.QualityAttribCode).Distinct().Count()
                     - attribs.Count;

        return new LotStatementData(lot, rows,
            new LotTotals(receipts, shipments, technicalLoss, ticketServices, chargesTotal, invoiced, hidden));
    }

    // ------------------------------------------------------------------ regra do saldo

    /// <summary>+1 entra no lote, −1 sai, 0 não mexe no saldo. Espelha <see cref="StorageAddress"/>.</summary>
    public static int Direction(StorageTransactionType type) => type switch
    {
        StorageTransactionType.Receipt or
            StorageTransactionType.ShipmentReleased or
            StorageTransactionType.SalesShipmentReturn => 1,
        StorageTransactionType.Shipment or
            StorageTransactionType.SalesShipment or
            StorageTransactionType.TechnicalLoss => -1,
        _ => 0,
    };

    private static decimal ServiceTotal(StorageTransaction t) =>
        t.DryingServicePrice + t.CleaningServicePrice + t.ReceiptServicePrice + t.ShipmentPrice;

    // ------------------------------------------------------------------ classificação

    public sealed record QualityColumn(string Code, string Header);

    /// <summary>
    /// Atributos de classificação que viram coluna, em ordem de código. Só cabem
    /// <see cref="MaxQualityColumns"/>; o resumo avisa quando sobra atributo sem coluna.
    /// </summary>
    public static List<QualityColumn> QualityColumns(IEnumerable<StorageTransaction> transactions) =>
        transactions.SelectMany(t => t.QualityInspections)
            .GroupBy(q => q.QualityAttribCode)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Take(MaxQualityColumns)
            .Select(g => new QualityColumn(g.Key,
                QualityHeader(g.Select(q => q.QualityAttrib?.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? g.Key)))
            .ToList();

    /// <summary>"% UMIDADE" vira "UMIDADE": a coluna é estreita e o "%" não cabe em todo atributo (PH, FN).</summary>
    public static string QualityHeader(string name)
    {
        var text = name.Trim().TrimStart('%').Trim().ToUpper(PtBr);
        return text.Length > 6 ? text[..6] : text;
    }

    public static void SetQuality(
        LotStatementRowDto row, IEnumerable<StorageTransactionQualityInspection> inspections, List<QualityColumn> attribs)
    {
        foreach (var inspection in inspections)
        {
            var index = attribs.FindIndex(a => a.Code == inspection.QualityAttribCode);
            if (index >= 0)
                row.SetQuality(index, inspection.Value.ToString("N1", PtBr));
        }
    }

    public static void SetQualityHeaders(LotStatementRowDto row, List<QualityColumn> attribs)
    {
        for (var i = 0; i < attribs.Count; i++)
            row.SetQualityHeader(i, attribs[i].Header);
    }

    // ------------------------------------------------------------------ seções

    private static LotStatementRowDto Movement(LotStatementRowDto r, List<QualityColumn> attribs)
    {
        r.Section = MovementSection;
        r.SectionTitle = "MOVIMENTAÇÃO DO LOTE";
        (r.HDate, r.HDocument, r.HMovement, r.HDetail1, r.HDetail2) = ("Data", "Romaneio", "Movimento", "Placa", "NF");
        (r.HW1, r.HW2, r.HW3, r.HW4) = ("Peso bruto", "Secagem", "Limpeza", "Outros");
        (r.HQ1, r.HQ2, r.HQ3) = ("Entrada", "Saída", "Saldo");
        SetQualityHeaders(r, attribs);
        return r;
    }

    private static LotStatementRowDto TicketServices(LotStatementRowDto r)
    {
        r.Section = TicketServicesSection;
        r.SectionTitle = "SERVIÇOS DOS ROMANEIOS (R$)";
        (r.HDate, r.HDocument, r.HMovement, r.HDetail2) = ("Data", "Romaneio", "Movimento", "Situação");
        (r.HW1, r.HW2, r.HW3, r.HW4) = ("Secagem", "Limpeza", "Recepção", "Expedição");
        r.HQ1 = "Total";
        return r;
    }

    private static LotStatementRowDto Charges(LotStatementRowDto r)
    {
        r.Section = ChargesSection;
        r.SectionTitle = "ARMAZENAGEM E DEMAIS COBRANÇAS";
        (r.HDate, r.HDocument, r.HMovement, r.HDetail2) = ("Início", "Fim", "Cobrança", "Situação");
        (r.HW1, r.HW2) = ("Base", "Tarifa");
        (r.HQ1, r.HQ2) = ("Valor (R$)", "Quebra");
        return r;
    }

    // ------------------------------------------------------------------ cabeçalho e resumo

    public async Task<string> HeaderAsync(StorageAddress lot, CancellationToken ct = default)
    {
        var partner = await PartnerAsync(lot.CardCode, ct);
        var taxId = FirstFilled(partner?.Cnpj, partner?.Cpf, partner?.TaxId);
        var city = string.Join("/", new[] { partner?.City, partner?.State }.Where(s => !string.IsNullOrWhiteSpace(s)));

        var sb = new StringBuilder();
        sb.Append($"Lote: {lot.Code} - {lot.Description}   Situação: {StatusText(lot)}");
        sb.AppendLine($"   Abertura: {Day(lot.CreationDate)}");
        sb.Append($"Depositante: {lot.CardCode} - {partner?.CardName ?? lot.CardName}");
        if (taxId != "") sb.Append($"   CPF/CNPJ: {taxId}");
        if (city != "") sb.Append($"   {city}");
        sb.AppendLine();
        sb.AppendLine($"Produto: {lot.ItemCode} - {lot.ItemName}   Armazém: {lot.WarehouseCode} - {lot.WarehouseName}");
        sb.Append($"Propriedade: {OwnershipText(lot.OwnershipType)}   Posição em: {DateTime.Now.ToString("dd/MM/yyyy HH:mm", PtBr)}");
        return sb.ToString();
    }

    /// <summary>
    /// O cadastro do depositante só enfeita o cabeçalho. Se a origem falhar (SAP fora do ar), o
    /// produtor ainda recebe a posição, com o nome que o lote já guarda.
    /// </summary>
    private async Task<ReportPartnerDto?> PartnerAsync(string cardCode, CancellationToken ct)
    {
        try
        {
            return await partners.GetByCardCodeAsync(cardCode, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger?.LogWarning(e, "Posição do lote: cadastro do depositante {CardCode} indisponível.", cardCode);
            return null;
        }
    }

    public static string Summary(string uom, LotTotals totals)
    {
        var sb = new StringBuilder();
        void Line(string label, string value) => sb.AppendLine($"{label.PadRight(46, '.')}: {value,22}");
        string Qty(decimal v) => $"{v.ToString("N3", PtBr)} {uom}";
        string Value(decimal v) => v.ToString("N2", PtBr);

        sb.AppendLine("SALDO");
        Line("  Entradas ", Qty(totals.Receipts));
        Line("  Saídas ", Qty(totals.Shipments));
        Line("  Quebra técnica ", Qty(totals.TechnicalLoss));
        Line("  Saldo do lote ", Qty(totals.Balance));

        if (totals.Expenses != 0m)
        {
            sb.AppendLine();
            sb.AppendLine("DESPESAS (R$)");
            Line("  Serviços dos romaneios ", Value(totals.TicketServices));
            Line("  Armazenagem e demais cobranças ", Value(totals.Charges));
            Line("  Total ", Value(totals.Expenses));
            Line("  Já faturado ", Value(totals.Invoiced));
            Line("  A faturar ", Value(totals.ToInvoice));
        }

        if (totals.HiddenQualityAttribs > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Classificação: {totals.HiddenQualityAttribs} atributo(s) sem coluna neste papel " +
                          $"(cabem {MaxQualityColumns}).");
        }

        return sb.ToString().TrimEnd();
    }

    // ------------------------------------------------------------------ textos

    public static string MovementText(StorageTransaction t)
    {
        if (t.IsOwnershipTransfer == "Y")
            return "Transf. titular.";

        return t.TransactionType switch
        {
            StorageTransactionType.Receipt => "Entrada",
            StorageTransactionType.Shipment => "Saída",
            StorageTransactionType.TechnicalLoss => "Quebra técnica",
            StorageTransactionType.ShipmentReleased => "Lib. embarque",
            StorageTransactionType.SalesShipment => "Venda",
            StorageTransactionType.SalesShipmentReturn => "Dev. de venda",
            _ => Helpers.StorageStatementReportHelper.GetTransactionTypeName(t.TransactionType),
        };
    }

    private static string ChargeText(StorageChargeType type) => type switch
    {
        StorageChargeType.Storage => "Armazenagem",
        StorageChargeType.TechnicalLoss => "Quebra técnica",
        StorageChargeType.Fumigation => "Expurgo",
        _ => type.ToString(),
    };

    private static string StatusText(StorageAddress lot) => lot.Status == StorageAddressStatus.Closed
        ? $"Encerrado em {Day(lot.ClosingDate)}".TrimEnd()
        : "Aberto";

    private static string OwnershipText(StorageOwnershipType type) => type switch
    {
        StorageOwnershipType.OwnedInOurCustody => "Própria, em nosso poder",
        StorageOwnershipType.OwnedInThirdPartyCustody => "Própria, em poder de terceiros",
        StorageOwnershipType.ThirdParty => "De terceiros",
        _ => type.ToString(),
    };

    private static string FirstFilled(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";

    public static string Day(DateTime? date) => date?.ToString("dd/MM/yyyy", PtBr) ?? "";

    /// <summary>Peso com três casas; zero sai em branco para a linha não virar uma parede de "0,000".</summary>
    public static string Weight(decimal value) => value == 0m ? "" : value.ToString("N3", PtBr);

    public static string Money(decimal value) => value == 0m ? "" : value.ToString("N2", PtBr);

    public static string InvoiceRef(string? number, string? series) =>
        string.IsNullOrWhiteSpace(number) ? "" :
        string.IsNullOrWhiteSpace(series) ? number : $"{number}/{series}";
}
