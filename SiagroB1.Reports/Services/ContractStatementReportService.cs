using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Extrato do Contrato (compra ou venda), para conferir e fechar o contrato nos três lados:
/// físico (alocações e washout), fiscal (itens de nota vinculados), financeiro (títulos e baixas).
/// Fecha com um quadro de saldos e as diferenças entre os lados. É o equivalente ao AGRR062A do
/// Protheus. Spec: docs/superpowers/specs/2026-10-21-contract-statement-report-design.md.
/// Os números físicos vêm de <see cref="ContractPositionData"/>, os mesmos da Posição de Contratos.
/// </summary>
public class ContractStatementReportService(IUnitOfWork db, IFastReportService reportService, IConfiguration configuration)
{
    public const string TemplateName = "ContractStatement.frx";
    public const string DataSourceName = "Rows";

    public const int PhysicalSection = 1;
    public const int FiscalSection = 2;
    public const int TitlesSection = 3;
    public const int SettlementsSection = 4;
    public const int FixationsSection = 5;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<byte[]> ExecuteAsync(ContractStatementSide side, Guid key)
    {
        var data = side == ContractStatementSide.Purchase
            ? await PurchaseAsync(key)
            : await SalesAsync(key);

        // Em SAPB1 o Siagro não calcula tributos: a coluna ICMS sai em branco.
        if (!IsStandalone())
        {
            foreach (var row in data.Rows.Where(r => r.Section == FiscalSection))
                (row.A2, row.HA2) = (0m, "");
        }

        var parameters = new Dictionary<string, object>
        {
            ["pTitle"] = side == ContractStatementSide.Purchase
                ? "EXTRATO DO CONTRATO DE COMPRA"
                : "EXTRATO DO CONTRATO DE VENDA",
            ["pHeader"] = data.Header,
            ["pSummary"] = data.Summary,
            ["pRowCount"] = data.Rows.Count,
            [FastReportService.StandaloneParameter] = IsStandalone(),
        };

        return await reportService.GeneratePdfAsync(TemplateName, data.Rows, DataSourceName, DataSourceName, parameters);
    }

    private bool IsStandalone() =>
        !string.Equals(configuration.GetValue<string>("Erp"), "SAPB1", StringComparison.OrdinalIgnoreCase);

    public sealed record StatementData(List<ContractStatementRowDto> Rows, string Header, string Summary);

    // ------------------------------------------------------------------ compra

    public async Task<StatementData> PurchaseAsync(Guid key)
    {
        var context = db.Context;
        var contract = await context.PurchaseContracts.AsNoTracking()
                           .FirstOrDefaultAsync(c => c.Key == key)
                       ?? throw new NotFoundException("Contrato de compra não encontrado.");

        var position = (await ContractPositionData.PurchasePositionsAsync(
            context, context.PurchaseContracts.Where(c => c.Key == key))).Single();

        var rows = new List<ContractStatementRowDto>();

        // Físico: uma linha por alocação.
        var allocations = await context.PurchaseContractsAllocations.AsNoTracking()
            .Where(a => a.PurchaseContractKey == key)
            .Select(a => new
            {
                a.Volume,
                T = a.StorageTransaction,
            })
            .ToListAsync();

        foreach (var a in allocations.OrderBy(a => a.T?.TransactionDate).ThenBy(a => a.T?.Code, StringComparer.Ordinal))
        {
            var t = a.T;
            rows.Add(PurchasePhysical(new ContractStatementRowDto
            {
                Date = Day(t?.TransactionDate),
                Document = t?.Code ?? "",
                Movement = t is null ? "" : PurchaseMovement(t.TransactionType),
                Detail1 = t?.TruckCode ?? "",
                Detail2 = InvoiceRef(t?.InvoiceNumber, t?.InvoiceSerie),
                Q1 = t?.GrossWeight ?? 0m,
                Q2 = t is null ? 0m : t.DryingDiscount + t.CleaningDiscount + t.OthersDicount,
                Q3 = t?.NetWeight ?? 0m,
                Q4 = a.Volume,
            }));
        }

        var washouts = await context.PurchaseContractsWashouts.AsNoTracking()
            .Where(w => w.PurchaseContractKey == key
                        && (w.Status == PurchaseContractWashoutStatus.InApproval
                            || w.Status == PurchaseContractWashoutStatus.Approved))
            .OrderBy(w => w.Sequence)
            .ToListAsync();
        foreach (var w in washouts)
            rows.Add(PurchasePhysical(WashoutRow(w.Sequence, w.CreatedAt, w.Status.ToString(), w.FixedVolume + w.UnfixedVolume)));

        // Fiscal: itens de nota com a chave do contrato, menos as canceladas.
        var items = await context.PurchaseInvoicesItems.AsNoTracking()
            .Where(i => i.PurchaseContractKey == key && i.PurchaseInvoice!.InvoiceStatus != InvoiceStatus.Cancelled)
            .Select(i => new
            {
                i.Quantity, i.UnitPrice, i.ComplementAmount, i.IcmsValue, i.Cfop, i.UsageName,
                i.PurchaseInvoice!.InvoiceType, i.PurchaseInvoice.InvoiceStatus, i.PurchaseInvoice.IssueDate,
                i.PurchaseInvoice.TaxDocumentNumber, i.PurchaseInvoice.TaxDocumentSeries, i.PurchaseInvoice.InvoiceNumber,
            })
            .ToListAsync();

        foreach (var i in items.OrderBy(i => i.IssueDate).ThenBy(i => i.TaxDocumentNumber, StringComparer.Ordinal))
        {
            var sign = i.InvoiceType == PurchaseInvoiceType.Return ? -1m : 1m;
            var isComplement = i.InvoiceType is PurchaseInvoiceType.ValueComplement or PurchaseInvoiceType.IcmsComplement;
            var quantity = isComplement ? 0m : sign * i.Quantity;
            var amount = i.InvoiceType switch
            {
                PurchaseInvoiceType.ValueComplement => i.ComplementAmount,
                PurchaseInvoiceType.IcmsComplement => 0m,
                _ => sign * Round2(i.Quantity * i.UnitPrice),
            };
            rows.Add(Fiscal(new ContractStatementRowDto
            {
                Date = Day(i.IssueDate),
                Document = InvoiceRef(i.TaxDocumentNumber ?? i.InvoiceNumber, i.TaxDocumentSeries),
                Movement = PurchaseInvoiceTypeText(i.InvoiceType),
                Detail1 = CfopUsage(i.Cfop, i.UsageName),
                Detail2 = InvoiceStatusText(i.InvoiceStatus),
                Q1 = quantity,
                Price = isComplement ? 0m : i.UnitPrice,
                A1 = amount,
                A2 = sign * i.IcmsValue,
            }));
        }

        var fixations = await context.PurchaseContractsPriceFixations.AsNoTracking()
            .Where(f => f.PurchaseContractKey == key && f.Status == PriceFixationStatus.Confirmed)
            .Select(f => new FixationLine(f.FixationDate, f.FinancialDueDate, f.FixationVolume, f.FixationPrice))
            .ToListAsync();

        var financial = await FinancialAsync(rows,
            context.FinancialDocuments.Where(d => d.PurchaseContractKey == key), FinancialDirection.Payable);

        if (contract.Type == ContractType.ToBeDetermined)
            AddFixations(rows, fixations);

        var header = Header(
            contract.Code, contract.Type, contract.Status, contract.CardCode, contract.CardName,
            contract.ItemCode, contract.ItemName, await HarvestNameAsync(contract.HarvestSeasonCode),
            contract.UnitOfMeasureCode, contract.DeliveryStartDate, contract.DeliveryEndDate,
            contract.TotalVolume, contract.StandardPrice, contract.StandardCurrency, contract.FreightTerms.ToString(),
            contract.StandardCashFlowDate, contract.PaymentTerms);

        var contractValue = ContractValue(contract.Type, contract.TotalVolume, contract.StandardPrice, fixations);
        var summary = Summary(ContractStatementSide.Purchase, position, contract.UnitOfMeasureCode, contractValue, rows, financial);
        return new StatementData(Order(rows), header, summary);
    }

    // ------------------------------------------------------------------ venda

    public async Task<StatementData> SalesAsync(Guid key)
    {
        var context = db.Context;
        var contract = await context.SalesContracts.AsNoTracking()
                           .FirstOrDefaultAsync(c => c.Key == key)
                       ?? throw new NotFoundException("Contrato de venda não encontrado.");

        var position = (await ContractPositionData.SalesPositionsAsync(
            context, context.SalesContracts.Where(c => c.Key == key))).Single();

        var rows = new List<ContractStatementRowDto>();

        var allocations = await context.SalesContractsAllocations.AsNoTracking()
            .Where(a => a.SalesContractKey == key)
            .Select(a => new
            {
                a.Volume, a.Origin, a.OwnsDeliveryDifference,
                a.SalesInvoiceItem!.Quantity, a.SalesInvoiceItem.DeliveredQuantity, a.SalesInvoiceItem.QuantityLoss,
                ItemDeliveryStatus = a.SalesInvoiceItem.DeliveryStatus,
                a.SalesInvoiceItem.SalesInvoice!.InvoiceDate, a.SalesInvoiceItem.SalesInvoice.TaxDocumentNumber,
                a.SalesInvoiceItem.SalesInvoice.TaxDocumentSeries, a.SalesInvoiceItem.SalesInvoice.InvoiceNumber,
                a.SalesInvoiceItem.SalesInvoice.TruckCode,
            })
            .ToListAsync();

        foreach (var a in allocations.OrderBy(a => a.InvoiceDate).ThenBy(a => a.TaxDocumentNumber, StringComparer.Ordinal))
        {
            var closed = a.ItemDeliveryStatus == SalesInvoiceDeliveryStatus.Closed;
            // Sobra(+)/quebra(−) só na linha dona da diferença e com a entrega conferida
            // (ContractPositionData.SalesShortageQuery). O entregue é alocado + sobra/quebra.
            var difference = a.OwnsDeliveryDifference && closed
                ? (a.DeliveredQuantity - a.QuantityLoss) - a.Quantity
                : 0m;
            rows.Add(SalesPhysical(new ContractStatementRowDto
            {
                Date = Day(a.InvoiceDate),
                Document = InvoiceRef(a.TaxDocumentNumber ?? a.InvoiceNumber, a.TaxDocumentSeries),
                Movement = SalesOriginText(a.Origin),
                Detail1 = a.TruckCode ?? "",
                Detail2 = closed ? "Conferida" : "Em aberto",
                Q1 = a.Quantity,
                Q2 = closed ? a.DeliveredQuantity : 0m,
                Q3 = difference,
                Q4 = a.Volume,
            }));
        }

        var washouts = await context.SalesContractsWashouts.AsNoTracking()
            .Where(w => w.SalesContractKey == key
                        && (w.Status == SalesContractWashoutStatus.InApproval
                            || w.Status == SalesContractWashoutStatus.Approved))
            .OrderBy(w => w.Sequence)
            .ToListAsync();
        foreach (var w in washouts)
            rows.Add(SalesPhysical(WashoutRow(w.Sequence, w.CreatedAt, w.Status.ToString(), w.FixedVolume + w.UnfixedVolume)));

        var items = await context.SalesInvoicesItems.AsNoTracking()
            .Where(i => i.SalesContractKey == key && i.SalesInvoice!.InvoiceStatus != InvoiceStatus.Cancelled)
            .Select(i => new
            {
                i.Quantity, i.UnitPrice, i.IcmsValue, i.Cfop, i.UsageName,
                i.SalesInvoice!.InvoiceType, i.SalesInvoice.InvoiceStatus, i.SalesInvoice.InvoiceDate,
                i.SalesInvoice.TaxDocumentNumber, i.SalesInvoice.TaxDocumentSeries, i.SalesInvoice.InvoiceNumber,
            })
            .ToListAsync();

        foreach (var i in items.OrderBy(i => i.InvoiceDate).ThenBy(i => i.TaxDocumentNumber, StringComparer.Ordinal))
        {
            var sign = i.InvoiceType == SalesInvoiceType.Return ? -1m : 1m;
            var isComplement = i.InvoiceType == SalesInvoiceType.IcmsComplement;
            rows.Add(Fiscal(new ContractStatementRowDto
            {
                Date = Day(i.InvoiceDate),
                Document = InvoiceRef(i.TaxDocumentNumber ?? i.InvoiceNumber, i.TaxDocumentSeries),
                Movement = SalesInvoiceTypeText(i.InvoiceType),
                Detail1 = CfopUsage(i.Cfop, i.UsageName),
                Detail2 = InvoiceStatusText(i.InvoiceStatus ?? InvoiceStatus.Pending),
                Q1 = isComplement ? 0m : sign * i.Quantity,
                Price = isComplement ? 0m : i.UnitPrice,
                A1 = isComplement ? 0m : sign * Round2(i.Quantity * i.UnitPrice),
                A2 = sign * i.IcmsValue,
            }));
        }

        var fixations = await context.SalesContractsPriceFixations.AsNoTracking()
            .Where(f => f.SalesContractKey == key && f.Status == PriceFixationStatus.Confirmed)
            .Select(f => new FixationLine(f.FixationDate, f.FinancialDueDate, f.FixationVolume, f.FixationPrice))
            .ToListAsync();

        var financial = await FinancialAsync(rows,
            context.FinancialDocuments.Where(d => d.SalesContractKey == key), FinancialDirection.Receivable);

        if (contract.Type == ContractType.ToBeDetermined)
            AddFixations(rows, fixations);

        var header = Header(
            contract.Code, contract.Type, contract.Status, contract.CardCode, contract.CardName,
            contract.ItemCode, contract.ItemName, await HarvestNameAsync(contract.HarvestSeasonCode),
            contract.UnitOfMeasureCode, contract.DeliveryStartDate, contract.DeliveryEndDate,
            contract.TotalVolume, contract.Price, contract.StandardCurrency, contract.FreightTerms.ToString(),
            contract.StandardCashFlowDate, contract.PaymentTerms);

        var contractValue = ContractValue(contract.Type, contract.TotalVolume, contract.Price, fixations);
        var summary = Summary(ContractStatementSide.Sales, position, contract.UnitOfMeasureCode, contractValue, rows, financial);
        return new StatementData(Order(rows), header, summary);
    }

    private async Task<string> HarvestNameAsync(string code) =>
        await db.Context.HarvestSeasons.AsNoTracking().Where(h => h.Code == code).Select(h => h.Name).FirstOrDefaultAsync()
        ?? code;

    // ------------------------------------------------------------------ financeiro (comum)

    /// <summary>Totais do financeiro que não viram linha: provisórios e retenções.</summary>
    public sealed record FinancialTotals(decimal ProvisionalOpen, decimal Withholdings, decimal AdvanceUnapplied);

    private async Task<FinancialTotals> FinancialAsync(
        List<ContractStatementRowDto> rows, IQueryable<FinancialDocument> documents, FinancialDirection contractDirection)
    {
        var docs = await documents.AsNoTracking()
            .Where(d => d.Status != FinancialDocumentStatus.Canceled)
            .ToListAsync();

        decimal Sign(FinancialDocument d) => d.Direction == contractDirection ? 1m : -1m;

        var listed = docs
            .Where(d => d.Nature is not (FinancialDocumentNature.Provisional or FinancialDocumentNature.TaxWithholding))
            .OrderBy(d => d.DueDate).ThenBy(d => d.Code, StringComparer.Ordinal)
            .ToList();

        foreach (var d in listed)
        {
            var sign = Sign(d);
            rows.Add(Titles(new ContractStatementRowDto
            {
                Date = Day(d.DueDate),
                Document = d.Code ?? "",
                Movement = NatureText(d.Nature),
                Detail1 = OriginText(d.OriginType),
                Detail2 = DocumentStatusText(d.Status),
                A1 = sign * d.NetAmount,
                A2 = sign * d.SettledAmount,
                A3 = sign * d.OpenAmount,
            }));
        }

        var listedKeys = listed.Select(d => d.Key).ToList();
        var byKey = listed.ToDictionary(d => d.Key);
        var settlements = await db.Context.FinancialSettlements.AsNoTracking()
            .Where(s => listedKeys.Contains(s.FinancialDocumentKey))
            .Select(s => new
            {
                s.FinancialDocumentKey, s.SettlementDate, s.Amount, s.Origin, s.FinancialAccountCode,
                AccountName = s.FinancialAccount!.Name,
            })
            .ToListAsync();

        foreach (var s in settlements.OrderBy(s => s.SettlementDate))
        {
            var doc = byKey[s.FinancialDocumentKey];
            rows.Add(Settlements(new ContractStatementRowDto
            {
                Date = Day(s.SettlementDate),
                Document = doc.Code ?? "",
                Movement = SettlementOriginText(s.Origin),
                Detail1 = string.IsNullOrWhiteSpace(s.FinancialAccountCode)
                    ? ""
                    : $"{s.FinancialAccountCode} {s.AccountName}".Trim(),
                A1 = Sign(doc) * s.Amount,
            }));
        }

        var provisionalOpen = docs.Where(d => d.Nature == FinancialDocumentNature.Provisional).Sum(d => Sign(d) * d.OpenAmount);
        var withholdings = docs.Where(d => d.Nature == FinancialDocumentNature.TaxWithholding).Sum(d => d.NetAmount);
        // Adiantamento pago e ainda não compensado contra firme: crédito que abate o saldo.
        var advanceUnapplied = docs.Where(d => d.Nature == FinancialDocumentNature.Advance)
            .Sum(d => Sign(d) * Math.Max(0m, d.SettledAmount - d.AppliedAmount));

        return new FinancialTotals(provisionalOpen, withholdings, advanceUnapplied);
    }

    // ------------------------------------------------------------------ quadro de saldos

    private static string Summary(
        ContractStatementSide side, ContractPosition position, string uom, decimal contractValue,
        List<ContractStatementRowDto> rows, FinancialTotals financial)
    {
        var fiscal = rows.Where(r => r.Section == FiscalSection).ToList();
        var titles = rows.Where(r => r.Section == TitlesSection).ToList();

        var invoicedQty = fiscal.Sum(r => r.Q1);
        var fiscalValue = fiscal.Sum(r => r.A1);
        // Títulos que espelham as notas: firme e devolução. Washout e adiantamento ficam fora da comparação.
        var invoiceTitles = titles
            .Where(r => r.Movement is "Firme" or "Devolução" && r.Detail1 != "Washout")
            .Sum(r => r.A1);
        var settled = titles.Sum(r => r.A2);
        var open = titles.Where(r => r.Movement != "Adiantamento").Sum(r => r.A3);
        var verb = side == ContractStatementSide.Purchase ? "pagar" : "receber";

        var sb = new StringBuilder();
        void Line(string label, string value) => sb.AppendLine($"{label.PadRight(46, '.')}: {value,22}");
        string Qty(decimal v) => $"{v.ToString("N3", PtBr)} {uom}";
        string Money(decimal v) => v.ToString("N2", PtBr);

        sb.AppendLine("FÍSICO");
        Line("  Quantidade contratada ", Qty(position.Contracted));
        Line("  Entregue ", Qty(position.Delivered));
        Line("  Washout ", Qty(position.WashedOut));
        Line("  Saldo físico ", Qty(position.Balance));
        sb.AppendLine();
        sb.AppendLine("FISCAL");
        Line("  Quantidade faturada (líquida de devoluções) ", Qty(invoicedQty));
        Line("  Diferença físico × fiscal ", Qty(position.Delivered - invoicedQty));
        Line("  Valor contratado ", Money(contractValue));
        Line("  Valor fiscal (líquido) ", Money(fiscalValue));
        sb.AppendLine();
        sb.AppendLine("FINANCEIRO");
        Line("  Títulos firmes (líquidos de devoluções) ", Money(invoiceTitles));
        Line("  Diferença fiscal × financeiro ", Money(fiscalValue - invoiceTitles));
        Line("  Baixado ", Money(settled));
        Line("  Em aberto ", Money(open));
        Line("  Adiantamento não compensado ", Money(financial.AdvanceUnapplied));
        Line($"  Saldo a {verb} ", Money(open - financial.AdvanceUnapplied));
        Line("  Saldo provisório a faturar ", Money(financial.ProvisionalOpen));
        if (financial.Withholdings != 0m)
            Line("  Retenções ", Money(financial.Withholdings));

        return sb.ToString().TrimEnd();
    }

    // ------------------------------------------------------------------ cabeçalho

    private static string Header(
        string? code, ContractType type, ContractStatus? status, string cardCode, string? cardName,
        string itemCode, string? itemName, string? harvest, string uom, DateTime start, DateTime end,
        decimal volume, decimal price, CurrencyType? currency, string freight, DateTime? dueDate, string? paymentTerms)
    {
        var currencyText = currency == CurrencyType.Usd ? "US$" : "R$";
        var typeText = type == ContractType.Fixed ? "Preço fixo" : "A fixar";
        var sb = new StringBuilder();
        sb.AppendLine($"Contrato: {code}   Tipo: {typeText}   Situação: {ContractStatusText(status)}");
        sb.AppendLine($"Parceiro: {cardCode} - {cardName}");
        sb.AppendLine($"Produto: {itemCode} - {itemName}   Safra: {harvest}   Entrega: {Day(start)} a {Day(end)}");
        var priceText = type == ContractType.Fixed
            ? $"   Preço: {currencyText} {price.ToString("N4", PtBr)}   Valor: {currencyText} {Round2(volume * price).ToString("N2", PtBr)}"
            : "";
        sb.Append($"Quantidade: {volume.ToString("N3", PtBr)} {uom}{priceText}   Frete: {freight.ToUpperInvariant()}");
        if (dueDate is not null)
            sb.Append($"   Vencimento: {Day(dueDate)}");
        if (!string.IsNullOrWhiteSpace(paymentTerms))
            sb.Append($"\nCondição: {paymentTerms}");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ seções

    private static ContractStatementRowDto PurchasePhysical(ContractStatementRowDto r)
    {
        r.Section = PhysicalSection;
        r.SectionTitle = "MOVIMENTAÇÃO FÍSICA";
        (r.HDate, r.HDocument, r.HMovement, r.HDetail1, r.HDetail2) = ("Data", "Romaneio", "Movimento", "Placa", "NF produtor");
        (r.HQ1, r.HQ2, r.HQ3, r.HQ4) = ("Peso bruto", "Descontos", "Peso líquido", "Alocado");
        return r;
    }

    private static ContractStatementRowDto SalesPhysical(ContractStatementRowDto r)
    {
        r.Section = PhysicalSection;
        r.SectionTitle = "MOVIMENTAÇÃO FÍSICA";
        (r.HDate, r.HDocument, r.HMovement, r.HDetail1, r.HDetail2) = ("Data", "NF/Série", "Origem", "Placa", "Entrega");
        (r.HQ1, r.HQ2, r.HQ3, r.HQ4) = ("Qtd. nota", "Peso destino", "Sobra/Quebra", "Alocado");
        return r;
    }

    private static ContractStatementRowDto WashoutRow(int sequence, DateTime? createdAt, string status, decimal volume) => new()
    {
        Date = Day(createdAt),
        Document = $"Washout {sequence}",
        Movement = "Washout",
        Detail2 = status == nameof(PurchaseContractWashoutStatus.Approved) ? "Aprovado" : "Em aprovação",
        Q4 = volume,
    };

    private static ContractStatementRowDto Fiscal(ContractStatementRowDto r)
    {
        r.Section = FiscalSection;
        r.SectionTitle = "DOCUMENTOS FISCAIS";
        (r.HDate, r.HDocument, r.HMovement, r.HDetail1, r.HDetail2) = ("Emissão", "NF/Série", "Tipo", "CFOP/Natureza", "Situação");
        (r.HQ1, r.HPrice, r.HA1, r.HA2) = ("Quantidade", "Preço", "Valor", "ICMS");
        return r;
    }

    private static ContractStatementRowDto Titles(ContractStatementRowDto r)
    {
        r.Section = TitlesSection;
        r.SectionTitle = "TÍTULOS FINANCEIROS";
        (r.HDate, r.HDocument, r.HMovement, r.HDetail1, r.HDetail2) = ("Vencimento", "Título", "Natureza", "Origem", "Situação");
        (r.HA1, r.HA2, r.HA3) = ("Valor", "Baixado", "Em aberto");
        return r;
    }

    private static ContractStatementRowDto Settlements(ContractStatementRowDto r)
    {
        r.Section = SettlementsSection;
        r.SectionTitle = "BAIXAS";
        (r.HDate, r.HDocument, r.HMovement, r.HDetail1) = ("Data", "Título", "Tipo", "Conta");
        r.HA1 = "Valor";
        return r;
    }

    private sealed record FixationLine(DateTime? Date, DateTime? DueDate, decimal Volume, decimal Price);

    private static void AddFixations(List<ContractStatementRowDto> rows, List<FixationLine> fixations)
    {
        foreach (var f in fixations.OrderBy(f => f.Date))
        {
            rows.Add(new ContractStatementRowDto
            {
                Section = FixationsSection,
                SectionTitle = "FIXAÇÕES DE PREÇO",
                Date = Day(f.Date),
                Detail1 = Day(f.DueDate),
                Q1 = f.Volume,
                Price = f.Price,
                A1 = Round2(f.Volume * f.Price),
                HDate = "Data", HDetail1 = "Vencimento", HQ1 = "Volume", HPrice = "Preço", HA1 = "Valor",
            });
        }
    }

    private static decimal ContractValue(ContractType type, decimal volume, decimal price, List<FixationLine> fixations) =>
        type == ContractType.Fixed
            ? Round2(volume * price)
            : fixations.Sum(f => Round2(f.Volume * f.Price));

    // A ordem de inserção dentro da seção já é a cronológica; aqui só agrupa as seções.
    private static List<ContractStatementRowDto> Order(List<ContractStatementRowDto> rows) =>
        rows.Select((r, i) => (r, i)).OrderBy(x => x.r.Section).ThenBy(x => x.i).Select(x => x.r).ToList();

    // ------------------------------------------------------------------ textos

    private static string Day(DateTime? date) => date?.ToString("dd/MM/yyyy", PtBr) ?? "";

    private static decimal Round2(decimal v) => decimal.Round(v, 2, MidpointRounding.ToEven);

    private static string InvoiceRef(string? number, string? series) =>
        string.IsNullOrWhiteSpace(number) ? "" :
        string.IsNullOrWhiteSpace(series) ? number : $"{number}/{series}";

    private static string CfopUsage(string? cfop, string? usage) =>
        string.Join(" ", new[] { cfop, usage }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public static string PurchaseMovement(StorageTransactionType type) => type switch
    {
        StorageTransactionType.Purchase => "Compra",
        StorageTransactionType.Receipt => "Entrada",
        StorageTransactionType.PurchaseReturn => "Devolução",
        StorageTransactionType.PurchaseQtyComplement => "Compl. quantidade",
        StorageTransactionType.PurchasePriceComplement => "Compl. valor",
        StorageTransactionType.PurchaseDischargeLoss => "Quebra descarga",
        StorageTransactionType.PurchaseDischargeGain => "Sobra descarga",
        StorageTransactionType.ShipmentReleased => "Embarque",
        StorageTransactionType.Transfer => "Transferência",
        StorageTransactionType.TransshipmentReceipt => "Transbordo",
        _ => type.ToString(),
    };

    private static string SalesOriginText(SalesContractAllocationOrigin origin) => origin switch
    {
        SalesContractAllocationOrigin.Billing => "Faturamento",
        SalesContractAllocationOrigin.Reallocation => "Realocação",
        SalesContractAllocationOrigin.Return => "Devolução",
        SalesContractAllocationOrigin.Backfill => "Carga inicial",
        SalesContractAllocationOrigin.Reconciliation => "Conciliação",
        SalesContractAllocationOrigin.FiscalAdjustment => "Ajuste fiscal",
        _ => origin.ToString(),
    };

    private static string PurchaseInvoiceTypeText(PurchaseInvoiceType type) => type switch
    {
        PurchaseInvoiceType.Normal => "Normal",
        PurchaseInvoiceType.Return => "Devolução",
        PurchaseInvoiceType.ValueComplement => "Compl. valor",
        PurchaseInvoiceType.IcmsComplement => "Compl. ICMS",
        _ => type.ToString(),
    };

    private static string SalesInvoiceTypeText(SalesInvoiceType type) => type switch
    {
        SalesInvoiceType.Normal => "Normal",
        SalesInvoiceType.Return => "Devolução",
        SalesInvoiceType.IcmsComplement => "Compl. ICMS",
        _ => type.ToString(),
    };

    private static string InvoiceStatusText(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Pending => "Pendente",
        InvoiceStatus.Confirmed => "Confirmada",
        InvoiceStatus.Cancelled => "Cancelada",
        InvoiceStatus.Returned => "Devolvida",
        _ => status.ToString(),
    };

    private static string NatureText(FinancialDocumentNature nature) => nature switch
    {
        FinancialDocumentNature.Provisional => "Provisório",
        FinancialDocumentNature.Firm => "Firme",
        FinancialDocumentNature.Advance => "Adiantamento",
        FinancialDocumentNature.TaxWithholding => "Retenção",
        FinancialDocumentNature.PurchaseReturn or FinancialDocumentNature.SalesReturn => "Devolução",
        _ => nature.ToString(),
    };

    private static string OriginText(FinancialDocumentOrigin origin) => origin switch
    {
        FinancialDocumentOrigin.Manual => "Manual",
        FinancialDocumentOrigin.PurchaseContractPriceFixation or FinancialDocumentOrigin.SalesContractPriceFixation => "Fixação",
        FinancialDocumentOrigin.PurchaseInvoice or FinancialDocumentOrigin.OtherPurchaseInvoice => "Doc. entrada",
        FinancialDocumentOrigin.SalesInvoice or FinancialDocumentOrigin.SalesInvoiceNoContract => "Doc. saída",
        FinancialDocumentOrigin.PurchaseContractWashout or FinancialDocumentOrigin.SalesContractWashout => "Washout",
        FinancialDocumentOrigin.PurchaseFiscalReconciliation => "Conciliação fiscal",
        FinancialDocumentOrigin.FinancialDocument => "Título",
        _ => origin.ToString(),
    };

    private static string DocumentStatusText(FinancialDocumentStatus status) => status switch
    {
        FinancialDocumentStatus.Open => "Aberto",
        FinancialDocumentStatus.PartiallySettled => "Baixado parcial",
        FinancialDocumentStatus.Settled => "Baixado",
        FinancialDocumentStatus.Canceled => "Cancelado",
        _ => status.ToString(),
    };

    private static string SettlementOriginText(FinancialSettlementOrigin origin) => origin switch
    {
        FinancialSettlementOrigin.Manual => "Baixa",
        FinancialSettlementOrigin.Reversal => "Estorno",
        FinancialSettlementOrigin.InvoiceOffset => "Composição",
        FinancialSettlementOrigin.AdvanceApplication => "Compensação adiant.",
        FinancialSettlementOrigin.Netting => "Encontro de contas",
        FinancialSettlementOrigin.AdvanceRefund => "Devolução adiant.",
        FinancialSettlementOrigin.Dation => "Dação",
        FinancialSettlementOrigin.AdvanceTransfer => "Transferência adiant.",
        _ => origin.ToString(),
    };

    private static string ContractStatusText(ContractStatus? status) => status switch
    {
        ContractStatus.Draft => "Rascunho",
        ContractStatus.Approved => "Aprovado",
        ContractStatus.Finished => "Encerrado",
        ContractStatus.Canceled => "Cancelado",
        ContractStatus.InApproval => "Em aprovação",
        ContractStatus.Rejected => "Rejeitado",
        _ => "",
    };
}
