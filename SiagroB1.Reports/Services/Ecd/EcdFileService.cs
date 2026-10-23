using System.Globalization;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Sped;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.Accounting;
using static SiagroB1.Reports.Services.Sped.CompanyBranches;

namespace SiagroB1.Reports.Services.Ecd;

/// <summary>
/// Monta a ECD (livro Diário geral, "G") de uma empresa (CNPJ raiz) e ano civil, centralizada na matriz. Saldos pelas
/// mesmas regras do Balancete (I155), do Balanço (J100) e da DRE (J150) do Siagro — nada é recalculado por fora.
/// </summary>
public class EcdFileService(EcdDataLoader loader)
{
    /// <summary>
    /// Leiaute da ECD por ano-calendário (COD_VER_LC do I010; o 0000 traz só "LECD") — Manual de Orientação do
    /// Leiaute da ECD, ADE Cofis: leiaute 9 ("9.00") para fatos contábeis a partir de 01/01/2020 (vigente em 2026).
    /// Os registros abaixo seguem o leiaute 9; ano anterior a 2020 é recusado pelo validador.
    /// </summary>
    private static readonly (int FromYear, string Version)[] Layouts = [(2020, "9.00")];

    public static readonly int FirstSupportedYear = Layouts[0].FromYear;

    public static string LayoutVersion(int year) => Layouts.Last(l => l.FromYear <= year).Version;

    private const string BookNature = "DIARIO GERAL";

    public async Task<EcdCheckDto> CheckAsync(EcdRequest request)
    {
        var data = await loader.LoadAsync(request);
        var postings = EcdLedger.Postings(data);
        return new EcdCheckDto
        {
            Issues = EcdValidator.Validate(data, request),
            Totals = new EcdTotalsDto
            {
                TotalDebit = postings.Sum(p => p.Debit),
                TotalCredit = postings.Sum(p => p.Credit),
                NetIncome = EcdLedger.NetIncome(data, postings),
            },
        };
    }

    /// <summary>Gera o arquivo; qualquer inconsistência bloqueante recusa com todas as mensagens juntas.</summary>
    public async Task<(byte[] Content, string FileName)> GenerateAsync(EcdRequest request)
    {
        var data = await loader.LoadAsync(request);
        var blocking = EcdValidator.Validate(data, request).Where(i => i.Blocking).Select(i => i.Message).ToList();
        if (blocking.Count > 0)
            throw new BusinessException(string.Join(" ", blocking));

        return (Build(data, request), $"ECD_{Digits(data.Declarant!.TaxId)}_{request.Year:0000}.txt");
    }

    /// <summary>
    /// Monta o arquivo. QTD_LIN do I030 e do J900 é o total de linhas do arquivo (0000 a 9999), que o bloco 9 só
    /// conhece no fim: monta uma vez para contar e de novo com o número — o valor não muda a quantidade de linhas.
    /// </summary>
    public static byte[] Build(EcdData data, EcdRequest request)
    {
        var draft = Build(data, request, 0);
        var lines = draft.Count(b => b == '\n');
        return Build(data, request, lines);
    }

    private static byte[] Build(EcdData data, EcdRequest request, int lineCount)
    {
        var w = new SpedRecordWriter();
        var postings = EcdLedger.Postings(data);
        WriteBlock0(w, data, request);
        WriteBlockI(w, data, request, postings, lineCount);
        WriteBlockJ(w, data, request, postings, lineCount);

        // K (conglomerados): sem dados
        w.Add("K001", "1");
        w.Add("K990", w.CountOfBlock('K') + 1);
        return w.Finish();
    }

    private static string Name(Branch b) => b.LegalName ?? b.BranchName;

    private static void WriteBlock0(SpedRecordWriter w, EcdData data, EcdRequest request)
    {
        var d = data.Declarant!;
        var s = data.Settings!;
        // 0000: REG, LECD, DT_INI, DT_FIN, NOME, CNPJ, UF, IE, COD_MUN, IM, IND_SIT_ESP, IND_SIT_INI_PER, IND_NIRE,
        //       IND_FIN_ESC, COD_HASH_SUB, IND_GRANDE_PORTE, TIP_ECD, COD_SCP, IDENT_MF, IND_ESC_CONS,
        //       IND_CENTRALIZADA, IND_MUDANC_PC, COD_PLAN_REF
        w.Add("0000", "LECD", new DateTime(request.Year, 1, 1), new DateTime(request.Year, 12, 31), Name(d), Digits(d.TaxId),
            d.StateCode, Digits(d.StateRegistration), d.MunicipalityCode, "",
            "",                                   // sem situação especial
            "0",                                  // início no primeiro dia do ano
            s.HasNire ? "1" : "0",
            request.Purpose.ToString(CultureInfo.InvariantCulture),
            request.Purpose == 1 ? request.ReplacedBookHash?.Trim() : "",
            s.LargeCompany ? "1" : "0",
            "0",                                  // PJ não participante de SCP
            "",
            "N",                                  // sem moeda funcional
            "N",                                  // sem escrituração consolidada
            "0",                                  // centralizada
            "0",                                  // sem mudança do plano de contas
            s.ReferentialPlanCode);
        w.Add("0001", "0");
        // 0007: REG, COD_ENT_REF, COD_INSCR — "00": sem outras inscrições
        w.Add("0007", "00", "");
        w.Add("0990", w.CountOfBlock('0') + 1);
    }

    private static void WriteBlockI(SpedRecordWriter w, EcdData data, EcdRequest request, List<EcdPosting> postings, int lineCount)
    {
        var d = data.Declarant!;
        var s = data.Settings!;
        var end = new DateTime(request.Year, 12, 31);
        var changed = data.Accounting.CutoverDate ?? new DateTime(request.Year, 1, 1);
        var accounts = data.Accounts.ToDictionary(a => a.Code);
        var infos = EcdLedger.AccountInfos(data);

        w.Add("I001", "0");
        // I010: REG, IND_ESC, COD_VER_LC
        w.Add("I010", "G", LayoutVersion(request.Year));
        // I030: REG, DNRC_ABERT, NUM_ORD, NAT_LIVR, QTD_LIN, NOME, NIRE, CNPJ, DT_ARQ, DT_ARQ_CONV, DESC_MUN, DT_EX_SOCIAL
        w.Add("I030", "TERMO DE ABERTURA", s.BookNumber, BookNature, lineCount, Name(d), s.HasNire ? Digits(s.Nire) : "",
            Digits(d.TaxId), s.ConstitutiveActsDate, "", data.MunicipalityName, end);

        // Plano de contas: analíticas do arquivo e as superiores delas
        var used = EcdLedger.UsedAccounts(data, postings);
        var plan = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var code in used)
            for (var c = accounts.GetValueOrDefault(code); c != null && plan.Add(c.Code);
                 c = c.ParentCode == null ? null : accounts.GetValueOrDefault(c.ParentCode)) { }

        foreach (var code in plan)
        {
            var a = accounts[code];
            // I050: REG, DT_ALT, COD_NAT, IND_CTA, NIVEL, COD_CTA, COD_CTA_SUP, CTA
            w.Add("I050", changed, GroupNature(a.Type), a.AllowsPosting ? "A" : "S", Depth(a, accounts), a.Code,
                a.ParentCode != null && plan.Contains(a.ParentCode) ? a.ParentCode : "", a.Name);
            if (!a.AllowsPosting) continue;

            // I051: REG, COD_CCUS, COD_CTA_REF
            if (!string.IsNullOrWhiteSpace(a.ReferentialCode)) w.Add("I051", "", a.ReferentialCode);
            // I052: REG, COD_CCUS, COD_AGL — linha da visão do Balanço e/ou da DRE que contém a conta
            foreach (var line in new[] { EcdLedger.LineOf(data.BalanceSheet, a.Code), EcdLedger.LineOf(data.IncomeStatement, a.Code) })
                if (line != null) w.Add("I052", "", line.Code);
        }

        // I100: REG, DT_ALT, COD_CCUS, CCUS — só os centros usados
        var months = EcdLedger.Months(data, postings);
        var centers = data.CostCenters.ToDictionary(c => c.Code);
        foreach (var cc in months.SelectMany(m => m).Select(r => r.CostCenter).OfType<string>().Distinct().Order(StringComparer.Ordinal))
            w.Add("I100", changed, cc, centers[cc].Name);

        for (var month = 1; month <= 12; month++)
        {
            var first = new DateTime(request.Year, month, 1);
            // I150: REG, DT_INI, DT_FIN
            w.Add("I150", first, first.AddMonths(1).AddDays(-1));
            // I155: REG, COD_CTA, COD_CCUS, VL_SLD_INI, IND_DC_INI, VL_DEB, VL_CRED, VL_SLD_FIN, IND_DC_FIN
            foreach (var r in months[month - 1])
            {
                var info = infos[r.Account];
                w.Add("I155", r.Account, r.CostCenter ?? "", Math.Abs(r.Opening), Side(r.Opening, info), r.Debit, r.Credit,
                    Math.Abs(r.Closing), Side(r.Closing, info));
            }
        }

        foreach (var entry in postings.GroupBy(p => p.Entry))
        {
            var e = entry.Key;
            // I200: REG, NUM_LCTO, DT_LCTO, VL_LCTO, IND_LCTO, DT_LCTO_EXT
            w.Add("I200", e.DocEntry, e.PostingDate, entry.Sum(p => p.Debit), entry.First().ResultClosing ? "E" : "N", "");
            // I250: REG, COD_CTA, COD_CCUS, VL_DC, IND_DC, NUM_ARQ, COD_HIST_PAD, HIST, COD_PART
            foreach (var p in entry)
                w.Add("I250", p.Account, p.CostCenter ?? "", p.Debit + p.Credit, p.Debit > 0 ? "D" : "C", "", "",
                    string.IsNullOrWhiteSpace(p.History) ? $"Lançamento {e.DocEntry}" : p.History, "");
        }

        // I350/I355: em cada data de apuração (IND_LCTO "E"), saldo das contas de resultado antes dela
        var result = data.Accounts.Where(a => a.Type is LedgerAccountType.Revenue or LedgerAccountType.Expense)
            .Select(a => a.Code).ToHashSet();
        var opening = EcdLedger.Opening(data);
        foreach (var date in postings.Where(p => p.ResultClosing).Select(p => p.Entry.PostingDate.Date).Distinct().Order())
        {
            // I350: REG, DT_RES
            w.Add("I350", date);
            var before = postings.Where(p => p.Entry.PostingDate.Date < date || (p.Entry.PostingDate.Date == date && !p.ResultClosing));
            var balances = opening.Where(kv => result.Contains(kv.Key.Account)).ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var p in before.Where(p => result.Contains(p.Account)))
                balances[(p.Account, p.CostCenter)] = balances.GetValueOrDefault((p.Account, p.CostCenter)) + p.Debit - p.Credit;
            // I355: REG, COD_CTA, COD_CCUS, VL_CTA, IND_DC
            foreach (var (key, value) in balances.Where(kv => kv.Value != 0)
                         .OrderBy(kv => kv.Key.Account, StringComparer.Ordinal).ThenBy(kv => kv.Key.CostCenter ?? "", StringComparer.Ordinal))
                w.Add("I355", key.Account, key.CostCenter ?? "", Math.Abs(value), Side(value, infos[key.Account]));
        }

        w.Add("I990", w.CountOfBlock('I') + 1);
    }

    private static void WriteBlockJ(SpedRecordWriter w, EcdData data, EcdRequest request, List<EcdPosting> postings, int lineCount)
    {
        var d = data.Declarant!;
        var s = data.Settings!;
        var a = data.Accountant!;
        var start = new DateTime(request.Year, 1, 1);
        var end = new DateTime(request.Year, 12, 31);
        var infos = EcdLedger.AccountInfos(data);

        w.Add("J001", "0");
        // J005: REG, DT_INI, DT_FIN, ID_DEM (1 = da própria empresa), CAB_DEM
        w.Add("J005", start, end, "1", "");

        // J100: REG, COD_AGL, IND_COD_AGL, NIVEL_AGL, COD_AGL_SUP, IND_GRP_BAL, DESCR_COD_AGL, VL_CTA_INI,
        //       IND_DC_CTA_INI, VL_CTA_FIN, IND_DC_CTA_FIN, NOTA_EXP_REF
        foreach (var row in BalanceSheetRows(data, postings))
            w.Add("J100", row.Line.Code, row.Line.Kind == ManagementViewLineKind.Accounts ? "D" : "T", row.Level,
                row.Line.ParentCode ?? "", row.Group, row.Line.Description, Math.Abs(row.Initial), Side(row.Initial, row.Factor),
                Math.Abs(row.Final), Side(row.Final, row.Factor), "");

        // J150: REG, NU_ORDEM, COD_AGL, IND_COD_AGL, NIVEL_AGL, COD_AGL_SUP, DESCR_COD_AGL, VL_CTA_INI, IND_DC_CTA_INI,
        //       VL_CTA_FIN, IND_DC_CTA_FIN, IND_GRP_DRE, NOTA_EXP_REF
        // Valor do ano pela DRE do Siagro (sem a apuração); sem comparativo do período anterior (VL_CTA_INI zero).
        var dre = data.IncomeStatement!;
        var dreLines = Ordered(dre);
        var byCode = dreLines.ToDictionary(l => l.Code, StringComparer.Ordinal);
        var year = postings.Where(p => !p.ResultClosing).GroupBy(p => p.Account).ToDictionary(g => g.Key, g => g.Sum(p => p.Debit - p.Credit));
        var values = ManagementViewCalculator.Compute(dre.Lines, infos, code => year.GetValueOrDefault(code));
        var order = 0;
        foreach (var line in dreLines)
        {
            // Totais e fórmulas da DRE: positivo = lucro (credor)
            var factor = LineFactor(line, infos, -1);
            var value = values.GetValueOrDefault(line.Code);
            w.Add("J150", ++order, line.Code, line.Kind == ManagementViewLineKind.Accounts ? "D" : "T",
                ManagementViewCalculator.Level(line, byCode), line.ParentCode ?? "", line.Description, 0m, Side(0m, factor),
                Math.Abs(value), Side(value, factor), factor < 0 ? "R" : "D", "");
        }

        // J900: REG, DNRC_ENCER, NUM_ORD, NAT_LIVRO, NOME, QTD_LIN, DT_INI_ESCR, DT_FIN_ESCR
        w.Add("J900", "TERMO DE ENCERRAMENTO", s.BookNumber, BookNature, Name(d), lineCount, start, end);

        // J930: REG, IDENT_NOM, IDENT_CPF_CNPJ, IDENT_QUALIF, COD_ASSIN, IND_CRC, EMAIL, FONE, UF_CRC, NUM_SEQ_CRC, DT_CRC,
        //       IND_RESP_LEGAL — responsável legal e contabilista
        w.Add("J930", s.SignerName, Digits(s.SignerCpf), EcdValidator.SignerQualifications[s.SignerQualificationCode],
            s.SignerQualificationCode, "", s.SignerEmail, Digits(s.SignerPhone), "", "", "", "S");
        var crc = a.AccountantCrc.Trim();
        var crcState = crc.Length >= 2 && char.IsAsciiLetter(crc[0]) && char.IsAsciiLetter(crc[1]) ? crc[..2].ToUpperInvariant() : d.StateCode;
        w.Add("J930", a.AccountantName, Digits(a.AccountantCpf), EcdValidator.SignerQualifications["900"], "900", crc,
            a.AccountantEmail, Digits(a.AccountantPhone), crcState, s.AccountantCrcSequence, s.AccountantCrcValidUntil, "N");

        w.Add("J990", w.CountOfBlock('J') + 1);
    }

    internal sealed record BalanceSheetRow(ManagementViewLine Line, int Level, string Group, int Factor, decimal Initial, decimal Final);

    /// <summary>
    /// Linhas do J100 com os valores do Balanço do Siagro em 31/12 do ano anterior e em 31/12 do ano. Resultado não
    /// apurado em 31/12 é bloqueante (<see cref="EcdValidator"/>), então não há a linha calculada do relatório.
    /// </summary>
    internal static List<BalanceSheetRow> BalanceSheetRows(EcdData data, List<EcdPosting> postings)
    {
        var view = data.BalanceSheet!;
        var infos = EcdLedger.AccountInfos(data);
        var lines = Ordered(view);
        var group = BalanceSheetReportService.LiabilityGroup(lines);
        var (initial, _) = BalanceSheetReportService.Compute(view.Lines, infos, EcdLedger.Balances(data, []), group);
        var (final, _) = BalanceSheetReportService.Compute(view.Lines, infos, EcdLedger.Balances(data, postings), group);
        var byCode = lines.ToDictionary(l => l.Code, StringComparer.Ordinal);
        var assets = lines.FirstOrDefault(l => l.ParentCode is null);

        ManagementViewLine Top(ManagementViewLine l)
        {
            var seen = new HashSet<string>();
            while (l.ParentCode is { } p && seen.Add(l.Code) && byCode.TryGetValue(p, out var parent)) l = parent;
            return l;
        }

        var rows = new List<BalanceSheetRow>();
        foreach (var line in lines)
        {
            var groupCode = Top(line) == assets ? "A" : "P";
            rows.Add(new BalanceSheetRow(line, ManagementViewCalculator.Level(line, byCode), groupCode,
                LineFactor(line, infos, groupCode == "A" ? 1 : -1), initial.GetValueOrDefault(line.Code), final.GetValueOrDefault(line.Code)));
        }

        return rows;
    }

    /// <summary>Ativo − (passivo + PL) em 31/12, pelos dois grupos de nível 1 da visão do Balanço.</summary>
    internal static decimal BalanceSheetDifference(EcdData data, List<EcdPosting> postings)
    {
        var tops = BalanceSheetRows(data, postings).Where(r => r.Line.ParentCode is null).ToList();
        return tops.Count < 2 ? 0m : tops[0].Final - tops[1].Final;
    }

    private static List<ManagementViewLine> Ordered(ManagementView view) =>
        view.Lines.OrderBy(l => l.Order).ThenBy(l => l.Code, StringComparer.Ordinal).ToList();

    /// <summary>Sentido natural da linha: o da conta "de" da faixa (regra do calculador); totais pelo grupo.</summary>
    private static int LineFactor(ManagementViewLine line, Dictionary<string, AccountInfo> infos, int fallback)
    {
        if (line.Kind != ManagementViewLineKind.Accounts || string.IsNullOrWhiteSpace(line.AccountFrom)
            || string.IsNullOrWhiteSpace(line.AccountTo)) return fallback;
        var from = line.AccountFrom.Trim();
        var to = line.AccountTo.Trim();
        var head = infos.GetValueOrDefault(from) ?? infos.Values
            .Where(a => a.Analytic && string.CompareOrdinal(a.Code, from) >= 0 && string.CompareOrdinal(a.Code, to) <= 0)
            .OrderBy(a => a.Code, StringComparer.Ordinal).FirstOrDefault();
        return head == null ? fallback : ManagementViewCalculator.NaturalFactor(head);
    }

    /// <summary>D/C do saldo (débito − crédito); zerado sai pelo sentido natural da conta.</summary>
    private static string Side(decimal signed, AccountInfo account) =>
        signed > 0 ? "D" : signed < 0 ? "C" : ManagementViewCalculator.NaturalFactor(account) > 0 ? "D" : "C";

    /// <summary>D/C de um valor no sentido natural da linha (fator +1 devedor, −1 credor).</summary>
    private static string Side(decimal natural, int factor) => (natural >= 0) == (factor > 0) ? "D" : "C";

    /// <summary>Natureza do grupo (COD_NAT do I050).</summary>
    private static string GroupNature(LedgerAccountType? type) => type switch
    {
        LedgerAccountType.Asset => "01",
        LedgerAccountType.Liability => "02",
        LedgerAccountType.Equity => "03",
        _ => "04",
    };

    private static int Depth(LedgerAccount account, Dictionary<string, LedgerAccount> accounts)
    {
        var depth = 1;
        var seen = new HashSet<string> { account.Code };
        for (var p = account.ParentCode; p != null && seen.Add(p) && accounts.TryGetValue(p, out var parent); p = parent.ParentCode)
            depth++;
        return depth;
    }
}
