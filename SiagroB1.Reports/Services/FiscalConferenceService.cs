using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Conferência Fiscal do mês (equivale à conferência do SPED Fiscal e do R-2055): lista os
/// documentos escrituráveis item a item. Lê os tributos gravados na linha — nunca recalcula.
/// </summary>
public class FiscalConferenceService(IUnitOfWork db, FunruralWithholdingReportService funrural)
{
    /// <summary>NF-e que não vale como documento escriturável.</summary>
    private static readonly NfeStatus[] VoidNfeStatuses = [NfeStatus.Cancelled, NfeStatus.Denied, NfeStatus.Voided];

    private static readonly InvoiceStatus[] BookableStatuses = [InvoiceStatus.Confirmed, InvoiceStatus.Returned];

    private static readonly TaxDocumentKind[] BookableKinds = [TaxDocumentKind.Nfe, TaxDocumentKind.Other];

    public async Task<FiscalConferenceData> BuildAsync(FiscalConferenceRequest request)
    {
        var (included, unauthorized) = await LoadLinesAsync(request);
        var (assessment, totals) = BuildAssessment(included);
        return new FiscalConferenceData
        {
            Documents = included,
            Summary = BuildSummary(included),
            Assessment = assessment,
            AssessmentTotals = totals,
            R2055 = await BuildR2055Async(request),
            Issues = BuildIssues(included, unauthorized),
        };
    }

    /// <summary>
    /// Prévia do R-2055: Funrural retido no mês por produtor. Reaproveita o relatório de Funrural
    /// (mesma fonte e mesmo abatimento das devoluções), somando só Item e Return — o Net repetiria.
    /// </summary>
    private async Task<List<R2055ProducerRowDto>> BuildR2055Async(FiscalConferenceRequest request)
    {
        var from = new DateTime(request.Year, request.Month, 1);
        var data = await funrural.BuildAsync(new FunruralWithholdingRequest
        {
            FromDate = from,
            ToDate = from.AddMonths(1).AddDays(-1),
            BranchCode = string.IsNullOrWhiteSpace(request.BranchCode) ? null : request.BranchCode,
            Statuses = [.. BookableStatuses],
            BookableOnly = true,
        });

        var totals = data.Rows
            .Where(r => r.Kind is FunruralRowKind.Item or FunruralRowKind.Return)
            .GroupBy(r => r.CardCode)
            .Select(g => (CardCode: g.Key,
                Name: PartnerName(g.First(r => r.Kind == FunruralRowKind.Item).Partner, g.Key),
                Amounts: new FunruralAmounts(g.Sum(r => r.Base), g.Sum(r => r.InssValue), g.Sum(r => r.RatValue), g.Sum(r => r.SenarValue))))
            .ToList();

        var codes = totals.Select(t => t.CardCode).ToArray();
        var partners = await db.Context.BusinessPartners
            .AsNoTracking()
            .Where(p => codes.Contains(p.CardCode))
            .ToDictionaryAsync(p => p.CardCode);

        return totals
            .Select(t =>
            {
                partners.TryGetValue(t.CardCode, out var partner);
                return new R2055ProducerRowDto
                {
                    CardCode = t.CardCode,
                    CardName = partner?.CardName ?? t.Name,
                    TaxId = !string.IsNullOrWhiteSpace(partner?.RuralProducerCpf) ? partner!.RuralProducerCpf! : partner?.TaxId ?? "",
                    ProducerType = ProducerTypeText(partner?.RuralProducerType),
                    Base = t.Amounts.Base,
                    Inss = t.Amounts.InssValue,
                    Rat = t.Amounts.RatValue,
                    Senar = t.Amounts.SenarValue,
                    Total = t.Amounts.Total,
                };
            })
            .OrderBy(r => r.CardName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.CardCode, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Tira o prefixo "(código) " do texto do parceiro; sem nome sobra o código.</summary>
    private static string PartnerName(string partner, string cardCode)
    {
        var prefix = $"({cardCode}) ";
        return partner.StartsWith(prefix, StringComparison.Ordinal) ? partner[prefix.Length..] : partner;
    }

    private static string ProducerTypeText(RuralProducerType? type) => type switch
    {
        RuralProducerType.Individual => "Pessoa física",
        RuralProducerType.SpecialInsured => "Segurado especial",
        RuralProducerType.LegalEntity => "Pessoa jurídica",
        _ => "",
    };

    private const string RuleNoCfop = "Item sem CFOP";
    private const string RuleNoCst = "Item sem CST de ICMS";
    private const string RuleIncomingCfopInOutgoing = "CFOP de entrada em documento de saída";
    private const string RuleOutgoingCfopInIncoming = "CFOP de saída em documento de entrada";
    private const string RuleIcmsMismatch = "ICMS difere de base × alíquota";
    private const string RuleNoAccessKey = "NF-e sem chave de acesso válida";
    private const string RuleNotAuthorized = "NF-e própria confirmada e não autorizada";

    /// <summary>Diferença tolerada entre o ICMS gravado e base × alíquota (arredondamento).</summary>
    private const decimal IcmsTolerance = 0.01m;

    /// <summary>
    /// Regras 1-5 sobre as linhas incluídas; regra 6 uma vez por documento de NF-e própria não
    /// autorizada (não por item). Ordem: data, número, item.
    /// </summary>
    private static List<FiscalIssueDto> BuildIssues(
        List<FiscalDocumentLineDto> included, List<FiscalDocumentLineDto> unauthorized)
    {
        var issues = new List<FiscalIssueDto>();

        foreach (var line in included)
        {
            if (string.IsNullOrWhiteSpace(line.Cfop))
                issues.Add(Issue(line, RuleNoCfop));

            if (string.IsNullOrWhiteSpace(line.CstIcms))
                issues.Add(Issue(line, RuleNoCst));

            // Devolução usa CFOP de direção oposta ao documento; não é erro.
            if (!line.IsReturn)
            {
                if (line.DocumentType == "Saída" && line.Direction == "E")
                    issues.Add(Issue(line, RuleIncomingCfopInOutgoing));
                else if (line.DocumentType == "Entrada" && line.Direction == "S")
                    issues.Add(Issue(line, RuleOutgoingCfopInIncoming));
            }

            if (Math.Abs(Math.Round(line.IcmsBase * line.IcmsRate / 100m, 2) - line.IcmsDeferredValue - line.IcmsValue) > IcmsTolerance)
                issues.Add(Issue(line, RuleIcmsMismatch));

            if (line.Model == "55" && !IsValidAccessKey(line.AccessKey))
                issues.Add(Issue(line, RuleNoAccessKey));
        }

        foreach (var document in unauthorized.GroupBy(l => l.DocumentKey))
            issues.Add(Issue(document.First(), RuleNotAuthorized, forDocument: true));

        return issues
            .OrderBy(i => i.Date)
            .ThenBy(i => i.Number, StringComparer.Ordinal)
            .ThenBy(i => i.ItemCode, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsValidAccessKey(string? key) =>
        key is { Length: 44 } && key.All(char.IsAsciiDigit);

    private static FiscalIssueDto Issue(FiscalDocumentLineDto line, string rule, bool forDocument = false) => new()
    {
        Direction = line.Direction,
        Date = line.Date,
        Number = line.Number,
        InternalNumber = line.InternalNumber,
        Partner = line.Partner,
        ItemCode = forDocument ? null : line.ItemCode,
        Rule = rule,
        DocumentKey = line.DocumentKey,
    };

    /// <summary>Agrupa por direção, CST, CFOP e alíquota; "E" vem antes de "S".</summary>
    private static List<FiscalSummaryRowDto> BuildSummary(List<FiscalDocumentLineDto> lines) => lines
        .GroupBy(l => (l.Direction, l.CstIcms, l.Cfop, l.IcmsRate))
        .Select(g => new FiscalSummaryRowDto
        {
            Direction = g.Key.Direction,
            CstIcms = g.Key.CstIcms,
            Cfop = g.Key.Cfop,
            IcmsRate = g.Key.IcmsRate,
            OperationValue = g.Sum(l => l.OperationValue),
            IcmsBase = g.Sum(l => l.IcmsBase),
            IcmsValue = g.Sum(l => l.IcmsValue),
        })
        .OrderBy(r => r.Direction, StringComparer.Ordinal)
        .ThenBy(r => r.Cfop, StringComparer.Ordinal)
        .ThenBy(r => r.CstIcms, StringComparer.Ordinal)
        .ThenBy(r => r.IcmsRate)
        .ToList();

    /// <summary>Saída (CFOP 5/6/7) debita o ICMS; entrada (1/2/3) credita. A direção já vem do CFOP.</summary>
    private static (List<IcmsAssessmentRowDto> Rows, IcmsAssessmentTotalsDto Totals) BuildAssessment(
        List<FiscalDocumentLineDto> lines)
    {
        var rows = lines
            .GroupBy(l => l.Cfop)
            .Select(g => new IcmsAssessmentRowDto
            {
                Cfop = g.Key,
                OperationValue = g.Sum(l => l.OperationValue),
                Debit = g.Where(l => l.Direction == "S").Sum(l => l.IcmsValue),
                Credit = g.Where(l => l.Direction == "E").Sum(l => l.IcmsValue),
            })
            .OrderBy(r => r.Cfop, StringComparer.Ordinal)
            .ToList();

        var debits = rows.Sum(r => r.Debit);
        var credits = rows.Sum(r => r.Credit);
        return (rows, new IcmsAssessmentTotalsDto { Debits = debits, Credits = credits, Balance = debits - credits });
    }

    /// <summary>
    /// Linhas escrituráveis do mês e, à parte, as de NF-e PRÓPRIA ainda não autorizada (ficam fora
    /// dos totais e viram inconsistência). Entrada de terceiro não depende da autorização.
    /// </summary>
    public async Task<(List<FiscalDocumentLineDto> Included, List<FiscalDocumentLineDto> Unauthorized)> LoadLinesAsync(
        FiscalConferenceRequest request)
    {
        var from = new DateTime(request.Year, request.Month, 1);
        var toExclusive = from.AddMonths(1);
        var branch = string.IsNullOrWhiteSpace(request.BranchCode) ? null : request.BranchCode;

        var sales = db.Context.SalesInvoicesItems
            .AsNoTracking()
            .Include(i => i.SalesInvoice)
            .Where(i => i.SalesInvoice != null)
            .Where(i => i.SalesInvoice!.InvoiceStatus != null && BookableStatuses.Contains(i.SalesInvoice.InvoiceStatus.Value))
            .Where(i => !i.SalesInvoice!.WithoutTaxDocument)
            .Where(i => BookableKinds.Contains(i.SalesInvoice!.TaxDocumentKind))
            .Where(i => !VoidNfeStatuses.Contains(i.SalesInvoice!.NfeStatus))
            .Where(i => i.SalesInvoice!.InvoiceDate >= from && i.SalesInvoice.InvoiceDate < toExclusive);

        var purchases = db.Context.PurchaseInvoicesItems
            .AsNoTracking()
            .Include(i => i.PurchaseInvoice)
            .Where(i => i.PurchaseInvoice != null)
            .Where(i => BookableStatuses.Contains(i.PurchaseInvoice!.InvoiceStatus))
            .Where(i => BookableKinds.Contains(i.PurchaseInvoice!.TaxDocumentKind))
            .Where(i => !VoidNfeStatuses.Contains(i.PurchaseInvoice!.NfeStatus))
            .Where(i => (i.PurchaseInvoice!.PostingDate ?? i.PurchaseInvoice.IssueDate) >= from
                     && (i.PurchaseInvoice.PostingDate ?? i.PurchaseInvoice.IssueDate) < toExclusive);

        if (branch is not null)
        {
            sales = sales.Where(i => i.SalesInvoice!.BranchCode == branch);
            purchases = purchases.Where(i => i.PurchaseInvoice!.BranchCode == branch);
        }

        var included = new List<FiscalDocumentLineDto>();
        var unauthorized = new List<FiscalDocumentLineDto>();

        foreach (var item in await sales.ToListAsync())
        {
            var invoice = item.SalesInvoice!;
            var notAuthorized = invoice.TaxDocumentKind == TaxDocumentKind.Nfe && invoice.NfeStatus != NfeStatus.Authorized;
            (notAuthorized ? unauthorized : included).Add(SaleLine(item, invoice));
        }

        foreach (var item in await purchases.ToListAsync())
        {
            var invoice = item.PurchaseInvoice!;
            var notAuthorized = invoice.IssuerType == DocumentIssuerType.Own
                && invoice.TaxDocumentKind == TaxDocumentKind.Nfe
                && invoice.NfeStatus != NfeStatus.Authorized;
            (notAuthorized ? unauthorized : included).Add(PurchaseLine(item, invoice));
        }

        return (Sort(included), Sort(unauthorized));
    }

    private static List<FiscalDocumentLineDto> Sort(List<FiscalDocumentLineDto> lines) => lines
        .OrderBy(l => l.Date)
        .ThenBy(l => l.DocumentType, StringComparer.Ordinal)
        .ThenBy(l => l.Number, StringComparer.Ordinal)
        .ThenBy(l => l.ItemCode, StringComparer.Ordinal)
        .ToList();

    private static FiscalDocumentLineDto SaleLine(SalesInvoiceItem item, SalesInvoice invoice) => new()
    {
        Direction = DirectionOf(item.Cfop, "S"),
        Date = invoice.InvoiceDate ?? default,
        Model = ModelOf(invoice.TaxDocumentKind),
        Series = invoice.TaxDocumentSeries ?? "",
        Number = invoice.TaxDocumentNumber ?? invoice.InvoiceNumber ?? "",
        InternalNumber = invoice.InvoiceNumber ?? "",
        AccessKey = invoice.ChaveNFe,
        Partner = Partner(invoice.CardCode, invoice.CardName),
        ItemCode = item.ItemCode,
        ItemName = item.ItemName ?? "",
        Cfop = item.Cfop ?? "",
        CstIcms = item.CstIcms ?? "",
        OperationValue = item.GrandTotal,
        IcmsBase = item.IcmsBase,
        IcmsRate = item.IcmsRate,
        IcmsValue = item.IcmsValue,
        CstPis = item.CstPis ?? "",
        PisValue = item.PisValue,
        CstCofins = item.CstCofins ?? "",
        CofinsValue = item.CofinsValue,
        DocumentType = "Saída",
        DocumentKey = invoice.Key,
        IsReturn = invoice.InvoiceType == SalesInvoiceType.Return,
        IcmsDeferredValue = item.IcmsDeferredValue,
    };

    private static FiscalDocumentLineDto PurchaseLine(PurchaseInvoiceItem item, PurchaseInvoice invoice) => new()
    {
        Direction = DirectionOf(item.Cfop, "E"),
        Date = invoice.PostingDate ?? invoice.IssueDate ?? default,
        Model = ModelOf(invoice.TaxDocumentKind),
        Series = invoice.TaxDocumentSeries ?? "",
        Number = invoice.TaxDocumentNumber ?? invoice.InvoiceNumber ?? "",
        InternalNumber = invoice.InvoiceNumber ?? "",
        AccessKey = invoice.ChaveNFe,
        Partner = Partner(invoice.CardCode, invoice.CardName),
        ItemCode = item.ItemCode ?? "",
        ItemName = item.ItemName ?? "",
        Cfop = item.Cfop ?? "",
        CstIcms = item.CstIcms ?? "",
        OperationValue = item.GrandTotal,
        IcmsBase = item.IcmsBase,
        IcmsRate = item.IcmsRate,
        IcmsValue = item.IcmsValue,
        CstPis = item.CstPis ?? "",
        PisValue = item.PisValue,
        CstCofins = item.CstCofins ?? "",
        CofinsValue = item.CofinsValue,
        DocumentType = "Entrada",
        DocumentKey = invoice.Key,
        IsReturn = invoice.InvoiceType == PurchaseInvoiceType.Return,
        IcmsDeferredValue = item.IcmsDeferredValue,
    };

    /// <summary>1/2/3 = entrada, 5/6/7 = saída; sem CFOP vale a direção do documento.</summary>
    private static string DirectionOf(string? cfop, string documentDirection) => cfop?.Trim() switch
    {
        [var first, ..] when first is '1' or '2' or '3' => "E",
        [var first, ..] when first is '5' or '6' or '7' => "S",
        _ => documentDirection,
    };

    private static string ModelOf(TaxDocumentKind kind) => kind == TaxDocumentKind.Nfe ? "55" : "01";

    private static string Partner(string code, string? name) => string.IsNullOrWhiteSpace(name) ? code : $"({code}) {name}";
}
