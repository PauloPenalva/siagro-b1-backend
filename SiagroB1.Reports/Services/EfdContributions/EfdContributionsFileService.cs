using System.Globalization;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Sped;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.SpedFiscal;
using static SiagroB1.Reports.Services.EfdContributions.EfdContributionsValidator;

namespace SiagroB1.Reports.Services.EfdContributions;

/// <summary>
/// Monta o arquivo da EFD Contribuições (PIS/COFINS) de uma empresa (CNPJ raiz) e mês, com um 0140
/// e um C010 por estabelecimento. Lê os tributos gravados nas linhas (mesma fonte da Conferência
/// Fiscal) — nunca recalcula. A, D, F, I e P saem abertos sem movimento.
/// </summary>
public class EfdContributionsFileService(EfdContributionsDataLoader loader, FiscalConferenceService conference)
{
    /// <summary>Regras da Conferência que aqui já são bloqueantes (não repetem como aviso).</summary>
    private static readonly HashSet<string> BlockingConferenceRules = ["Item sem CFOP"];

    public async Task<EfdContributionsCheckDto> CheckAsync(EfdContributionsRequest request)
    {
        var data = await loader.LoadAsync(request);
        var issues = Validate(data, request);

        foreach (var e in data.Establishments)
        {
            var preview = await conference.BuildAsync(new FiscalConferenceRequest
            {
                BranchCode = e.Branch.Code ?? "", Year = request.Year, Month = request.Month,
            });
            issues.AddRange(preview.Issues
                .Where(i => !BlockingConferenceRules.Contains(i.Rule))
                .Select(i => new EfdContributionsIssueDto
                {
                    Blocking = false,
                    Message = i.ItemCode == null
                        ? $"{i.Rule}: documento {i.Number} ({i.Partner})."
                        : $"{i.Rule}: documento {i.Number}, item {i.ItemCode} ({i.Partner}).",
                    Reference = i.ItemCode == null ? i.Number : $"{i.Number}/{i.ItemCode}",
                }));
        }

        var pis = Assess(data, Pis);
        var cofins = Assess(data, Cofins);
        return new EfdContributionsCheckDto
        {
            Issues = issues,
            Totals = new EfdContributionsTotalsDto
            {
                PisDue = pis.Due, PisCredit = pis.CreditUsed, PisPayable = pis.Payable,
                CofinsDue = cofins.Due, CofinsCredit = cofins.CreditUsed, CofinsPayable = cofins.Payable,
            },
        };
    }

    /// <summary>Gera o arquivo; qualquer inconsistência bloqueante recusa com todas as mensagens juntas.</summary>
    public async Task<(byte[] Content, string FileName)> GenerateAsync(EfdContributionsRequest request)
    {
        var data = await loader.LoadAsync(request);
        var blocking = Validate(data, request).Where(i => i.Blocking).Select(i => i.Message).ToList();
        if (blocking.Count > 0)
            throw new BusinessException(string.Join(" ", blocking));

        return (Build(data, request), $"EFD_CONTRIBUICOES_{Digits(data.Declarant!.TaxId)}_{request.Year:0000}{request.Month:00}.txt");
    }

    /// <summary>
    /// COD_VER do 0000 pelo período de apuração (tabela 3.1.1 do Guia Prático). Só os leiautes com o
    /// M210/M610 de 16 campos (ajustes de base, a partir de 2019) são suportados:
    /// 005 = 01/2019 a 12/2019; 006 = a partir de 01/2020 (vigente em 2026).
    /// </summary>
    public static string LayoutVersion(DateTime start) => start >= new DateTime(2020, 1, 1) ? "006" : "005";

    public static readonly DateTime FirstSupportedPeriod = new(2019, 1, 1);

    /// <summary>Monta os registros a partir dos dados já carregados e validados.</summary>
    public static byte[] Build(EfdContributionsData data, EfdContributionsRequest request)
    {
        var w = new SpedRecordWriter();
        var start = new DateTime(request.Year, request.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);

        WriteBlock0(w, data, request, start, end);

        w.Add("A001", "1");
        w.Add("A990", w.CountOfBlock('A') + 1);

        WriteBlockC(w, data);

        // CT-e fora desta entrega (como no SPED Fiscal): aberto sem movimento
        w.Add("D001", "1");
        w.Add("D990", w.CountOfBlock('D') + 1);
        w.Add("F001", "1");
        w.Add("F990", w.CountOfBlock('F') + 1);
        w.Add("I001", "1");
        w.Add("I990", w.CountOfBlock('I') + 1);

        var pis = Assess(data, Pis);
        var cofins = Assess(data, Cofins);
        WriteBlockM(w, pis, cofins);

        w.Add("P001", "1");
        w.Add("P990", w.CountOfBlock('P') + 1);

        WriteBlock1(w, pis, cofins, start);

        return w.Finish();
    }

    /// <summary>COD_INC_TRIB pelo regime das filiais: só não cumulativo 1, só cumulativo 2, ambos 3.</summary>
    internal static string IncidenceCode(EfdContributionsData data)
    {
        var regimes = data.Establishments.Select(e => e.Branch.PisCofinsRegime).Distinct().ToList();
        if (regimes.Count > 1) return "3";
        return regimes.SingleOrDefault() == PisCofinsRegime.NonCumulative ? "1" : "2";
    }

    private static void WriteBlock0(SpedRecordWriter w, EfdContributionsData data, EfdContributionsRequest request, DateTime start, DateTime end)
    {
        var d = data.Declarant!;
        var s = data.Settings!;
        var incidence = IncidenceCode(data);
        var hasNonCumulative = incidence != "2";

        // 0000: REG, COD_VER, TIPO_ESCRIT, IND_SIT_ESP, NUM_REC_ANTERIOR, DT_INI, DT_FIN, NOME, CNPJ, UF, COD_MUN,
        //       SUFRAMA, IND_NAT_PJ, IND_ATIV
        w.Add("0000", LayoutVersion(start), request.Purpose.ToString(CultureInfo.InvariantCulture), "",
            request.Purpose == 1 ? request.PreviousReceiptNumber : "", start, end, d.LegalName ?? d.BranchName, Digits(d.TaxId),
            d.StateCode, d.MunicipalityCode, "", "00",
            // ActivityType dos parâmetros do SPED Fiscal: 0 industrial → "0"; outros → "2" (comércio)
            s.ActivityType == 0 ? "0" : "2");
        w.Add("0001", "0");

        // 0100: REG, NOME, CPF, CRC, CNPJ, CEP, END, NUM, COMPL, BAIRRO, FONE, FAX, EMAIL, COD_MUN
        w.Add("0100", s.AccountantName, Digits(s.AccountantCpf), s.AccountantCrc, Digits(s.AccountantCnpj), Digits(s.AccountantZipCode),
            s.AccountantStreet, s.AccountantNumber, s.AccountantComplement, s.AccountantDistrict, Digits(s.AccountantPhone), "",
            s.AccountantEmail, s.AccountantMunicipalityCode);

        // 0110: REG, COD_INC_TRIB, IND_APRO_CRED, COD_TIPO_CONT, IND_REG_CUM
        // IND_APRO_CRED (1 = apropriação direta) e COD_TIPO_CONT (1 = alíquota básica) só com não cumulativo;
        // IND_REG_CUM 9 (competência, detalhado) só no regime exclusivamente cumulativo (COD_INC_TRIB 2)
        w.Add("0110", incidence, hasNonCumulative ? "1" : "", hasNonCumulative ? "1" : "", incidence == "2" ? "9" : "");

        foreach (var e in data.Establishments)
        {
            var b = e.Branch;
            // 0140: REG, COD_EST, NOME, CNPJ, UF, IE, COD_MUN, IM, SUFRAMA
            w.Add("0140", b.Code, b.LegalName ?? b.BranchName, Digits(b.TaxId), b.StateCode, Digits(b.StateRegistration),
                b.MunicipalityCode, "", "");

            // Filhos do 0140: participantes, unidades e itens usados pelo estabelecimento
            // 0150: REG, COD_PART, NOME, COD_PAIS, CNPJ, CPF, IE, COD_MUN, SUFRAMA, END, NUM, COMPL, BAIRRO
            foreach (var p in e.MasterData.Participants)
                w.Add("0150", p.Code, p.Name, p.CountryCode, p.Cnpj, p.Cpf, p.StateRegistration, p.MunicipalityCode, "",
                    p.Street, p.Number, p.Complement, p.District);

            // 0190: REG, UNID, DESCR
            foreach (var u in e.MasterData.Units)
                w.Add("0190", u.Code, u.Description);

            // 0200: REG, COD_ITEM, DESCR_ITEM, COD_BARRA, COD_ANT_ITEM, UNID_INV, TIPO_ITEM, COD_NCM, EX_IPI, COD_GEN,
            //       COD_LST, ALIQ_ICMS (sem CEST e sem 0220 neste leiaute)
            foreach (var i in e.MasterData.Items)
                w.Add("0200", i.Code, i.Description, "", "", i.UnitCode, i.ItemType, i.Ncm, "", "", "", "");
        }

        w.Add("0990", w.CountOfBlock('0') + 1);
    }

    private static void WriteBlockC(SpedRecordWriter w, EfdContributionsData data)
    {
        var withMovement = data.Establishments.Where(e => e.Lines.Count > 0 || e.VoidDocuments.Count > 0).ToList();
        w.Add("C001", withMovement.Count > 0 ? "0" : "1");

        foreach (var e in withMovement)
        {
            // C010: REG, CNPJ, IND_ESCRI (2 = individualizado por NF-e, C100/C170)
            w.Add("C010", Digits(e.Branch.TaxId), "2");

            var items = e.MasterData.Items.ToDictionary(i => i.Code);
            var documents = e.Lines
                .GroupBy(l => l.DocumentKey)
                .Select(g => g.ToList())
                .OrderBy(doc => doc[0].Date)
                .ThenBy(doc => doc[0].Number, StringComparer.Ordinal)
                .ThenBy(doc => doc[0].DocumentType, StringComparer.Ordinal)
                .ToList();

            foreach (var doc in documents)
            {
                var h = doc[0];
                // C100: REG, IND_OPER, IND_EMIT, COD_PART, COD_MOD, COD_SIT, SER, NUM_DOC, CHV_NFE, DT_DOC, DT_E_S, VL_DOC,
                //       IND_PGTO, VL_DESC, VL_ABAT_NT, VL_MERC, IND_FRT, VL_FRT, VL_SEG, VL_OUT_DA, VL_BC_ICMS, VL_ICMS,
                //       VL_BC_ICMS_ST, VL_ICMS_ST, VL_IPI, VL_PIS, VL_COFINS, VL_PIS_ST, VL_COFINS_ST
                w.Add("C100",
                    // Sentido da operação (CFOP, mesma regra do SPED Fiscal): devolução de compra própria é saída
                    h.Direction == "E" ? "0" : "1",
                    h.IssuerType,
                    h.CardCode,
                    h.Model,
                    doc.Any(l => l.IsComplement) ? "06" : "00",
                    h.Series,
                    h.Number,
                    h.Model == "55" ? h.AccessKey : "",
                    h.IssueDate ?? h.Date,
                    h.Date,
                    doc.Sum(l => l.OperationValue),
                    // IND_PGTO (domínio da EFD Contribuições: 0 à vista, 1 a prazo, 9 sem pagamento): o
                    // prazo não é controlado por documento aqui, então sai 9
                    "9",
                    doc.Sum(l => l.DiscountValue),
                    0m,
                    doc.Sum(ProductValue),
                    FreightIndicator(h.FreightTerms),
                    doc.Sum(l => l.FreightValue),
                    doc.Sum(l => l.InsuranceValue),
                    doc.Sum(l => l.OtherExpensesValue),
                    doc.Sum(l => l.IcmsBase),
                    doc.Sum(l => l.IcmsValue),
                    0m, 0m, 0m,
                    doc.Sum(l => l.PisValue),
                    doc.Sum(l => l.CofinsValue),
                    0m, 0m);

                // Na EFD Contribuições toda NF-e (própria e de terceiro) detalha os itens
                var sequence = 0;
                foreach (var l in doc)
                {
                    sequence++;
                    // C170: REG, NUM_ITEM, COD_ITEM, DESCR_COMPL, QTD, UNID, VL_ITEM, VL_DESC, IND_MOV, CST_ICMS, CFOP,
                    //       COD_NAT, VL_BC_ICMS, ALIQ_ICMS, VL_ICMS, VL_BC_ICMS_ST, ALIQ_ST, VL_ICMS_ST, IND_APUR, CST_IPI,
                    //       COD_ENQ, VL_BC_IPI, ALIQ_IPI, VL_IPI, CST_PIS, VL_BC_PIS, ALIQ_PIS, QUANT_BC_PIS,
                    //       ALIQ_PIS_QUANT, VL_PIS, CST_COFINS, VL_BC_COFINS, ALIQ_COFINS, QUANT_BC_COFINS,
                    //       ALIQ_COFINS_QUANT, VL_COFINS, COD_CTA
                    w.Add("C170",
                        l.NfeItemNumber ?? sequence,
                        l.ItemCode,
                        "",
                        new SpedDecimal(l.Quantity, 5),
                        l.UnitOfMeasureCode,
                        ProductValue(l),
                        l.DiscountValue,
                        "0",
                        l.CstIcms == "" ? "" : SpedFiscalFileService.IcmsCst(l.CstIcms, items.GetValueOrDefault(l.ItemCode)),
                        l.Cfop,
                        "",
                        l.IcmsBase,
                        l.IcmsRate,
                        l.IcmsValue,
                        0m, 0m, 0m,
                        "", "", "",
                        0m, 0m, 0m,
                        l.CstPis,
                        l.PisBase,
                        new SpedDecimal(l.PisRate, 4),
                        "", "",
                        l.PisValue,
                        l.CstCofins,
                        l.CofinsBase,
                        new SpedDecimal(l.CofinsRate, 4),
                        "", "",
                        l.CofinsValue,
                        "");
                }
            }

            // NF-e própria cancelada/denegada/inutilizada: C100 só com os campos 02 a 09 (sem participante);
            // a numeração inutilizada (COD_SIT 05) não tem chave de acesso
            foreach (var v in e.VoidDocuments)
                w.Add("C100", [v.Direction, "0", "", v.Model, v.Situation, v.Series, v.Number, v.Situation == "05" ? "" : v.AccessKey,
                    .. Enumerable.Repeat<object?>(null, 20)]);
        }

        w.Add("C990", w.CountOfBlock('C') + 1);
    }

    private static void WriteBlockM(SpedRecordWriter w, Assessment pis, Assessment cofins)
    {
        w.Add("M001", "0");
        WriteContribution(w, pis, "M100", "M105", "M200", "M205", "M210", "M400", "M410");
        WriteContribution(w, cofins, "M500", "M505", "M600", "M605", "M610", "M800", "M810");
        w.Add("M990", w.CountOfBlock('M') + 1);
    }

    /// <summary>Mesmo desenho para PIS (M100..M410) e COFINS (M500..M810), só muda o nome dos registros.</summary>
    private static void WriteContribution(SpedRecordWriter w, Assessment a, string credit, string creditDetail, string total,
        string revenueCode, string detail, string untaxed, string untaxedDetail)
    {
        foreach (var c in a.Credits)
        {
            // M100/M500: REG, COD_CRED, IND_CRED_ORI, VL_BC, ALIQ, QUANT_BC, ALIQ_QUANT, VL_CRED, VL_AJUS_ACRES,
            //            VL_AJUS_REDUC, VL_CRED_DIF, VL_CRED_DISP, IND_DESC_CRED, VL_CRED_DESC, SLD_CRED
            w.Add(credit, c.Code, "0", c.Base, new SpedDecimal(c.Rate, 4), "", "", c.Value, 0m, 0m, 0m, c.Value,
                // 0 = desconto total do crédito disponível; 1 = parcial (sobra vai para 1100/1500)
                c.Used == c.Value ? "0" : "1", c.Used, c.Value - c.Used);

            // M105/M505: REG, NAT_BC_CRED, CST, VL_BC_TOT, VL_BC_CUM, VL_BC_NC, VL_BC, QUANT_BC_TOT, QUANT_BC, DESC_CRED
            foreach (var d in c.Details)
                w.Add(creditDetail, d.CreditBase, d.Cst, d.Base, 0m, d.Base, d.Base, "", "", "");
        }

        // M200/M600: REG, VL_TOT_CONT_NC_PER, VL_TOT_CRED_DESC, VL_TOT_CRED_DESC_ANT, VL_TOT_CONT_NC_DEV, VL_RET_NC,
        //            VL_OUT_DED_NC, VL_CONT_NC_REC, VL_TOT_CONT_CUM_PER, VL_RET_CUM, VL_OUT_DED_CUM, VL_CONT_CUM_REC,
        //            VL_TOT_CONT_REC
        var ncDue = a.NonCumulativeDue - a.CreditUsed;
        w.Add(total, a.NonCumulativeDue, a.CreditUsed, 0m, ncDue, 0m, 0m, ncDue, a.CumulativeDue, 0m, 0m, a.CumulativeDue,
            ncDue + a.CumulativeDue);

        // M205/M605: REG, NUM_CAMPO, COD_REC, VL_DEBITO — campo 08 (não cumulativo) e 12 (cumulativo) do M200/M600
        if (ncDue > 0) w.Add(revenueCode, "08", a.NonCumulativeRevenueCode, ncDue);
        if (a.CumulativeDue > 0) w.Add(revenueCode, "12", a.CumulativeRevenueCode, a.CumulativeDue);

        // M210/M610 (leiaute 005+): REG, COD_CONT, VL_REC_BRT, VL_BC_CONT, VL_AJUS_ACRES_BC, VL_AJUS_REDUC_BC,
        //            VL_BC_CONT_AJUS, ALIQ, QUANT_BC, ALIQ_QUANT, VL_CONT_APUR, VL_AJUS_ACRES, VL_AJUS_REDUC,
        //            VL_CONT_DIFER, VL_CONT_DIFER_ANT, VL_CONT_PER
        foreach (var c in a.Contributions)
            w.Add(detail, c.Code, c.Revenue, c.Base, 0m, 0m, c.Base, new SpedDecimal(c.Rate, 4), "", "", c.Value, 0m, 0m, 0m, 0m,
                c.Value);

        foreach (var u in a.Untaxed)
        {
            // M400/M800: REG, CST, VL_TOT_REC, COD_CTA, DESC_COMPL
            w.Add(untaxed, u.Cst, u.Revenue, "", "");
            // M410/M810: REG, NAT_REC, VL_REC, COD_CTA, DESC_COMPL
            foreach (var (nature, revenue) in u.ByNature)
                w.Add(untaxedDetail, nature, revenue, "", "");
        }
    }

    private static void WriteBlock1(SpedRecordWriter w, Assessment pis, Assessment cofins, DateTime start)
    {
        var leftover = pis.CreditTotal > pis.CreditUsed || cofins.CreditTotal > cofins.CreditUsed;
        w.Add("1001", leftover ? "0" : "1");
        WriteCarryForward(w, "1100", pis, start);
        WriteCarryForward(w, "1500", cofins, start);
        w.Add("1990", w.CountOfBlock('1') + 1);
    }

    /// <summary>Crédito do próprio mês não descontado: controle do saldo (só o período corrente).</summary>
    private static void WriteCarryForward(SpedRecordWriter w, string register, Assessment a, DateTime start)
    {
        // Um 1100/1500 por COD_CRED com sobra
        foreach (var g in a.Credits.GroupBy(c => c.Code).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var total = g.Sum(c => c.Value);
            var used = g.Sum(c => c.Used);
            if (total > used) WriteCarryForward(w, register, g.Key, total, used, start);
        }
    }

    private static void WriteCarryForward(SpedRecordWriter w, string register, string code, decimal total, decimal used, DateTime start)
    {
        // 1100/1500: REG, PER_APU_CRED, ORIG_CRED, CNPJ_SUC, COD_CRED, VL_CRED_APU, VL_CRED_EXT_APU, VL_TOT_CRED_APU,
        //            VL_CRED_DESC_PA_ANT, VL_CRED_PER_PA_ANT, VL_CRED_DCOMP_PA_ANT, SD_CRED_DISP_EFD, VL_CRED_DESC_EFD,
        //            VL_CRED_PER_EFD, VL_CRED_DCOMP_EFD, VL_CRED_TRANS, VL_CRED_OUT, SLD_CRED_FIM
        w.Add(register, start.ToString("MMyyyy", CultureInfo.InvariantCulture), "01", "", code, total, 0m,
            total, 0m, 0m, 0m, total, used, 0m, 0m, 0m, 0m, total - used);
    }

    // ---- apuração ----

    /// <summary>
    /// COD_CRED (tabela 4.3.6) pelo CST da entrada, alíquota básica: 50→101, 51→201, 52→301, 53→401,
    /// 54→501, 55→601, 56→701 (o 1º dígito é o tipo de vinculação à receita).
    /// </summary>
    internal static string CreditCode(string cst) => $"{cst[1] - '0' + 1}01";

    /// <summary>Acesso ao CST/base/alíquota/valor de um dos tributos na linha.</summary>
    internal sealed record Tax(
        Func<FiscalDocumentLineDto, string> Cst, Func<FiscalDocumentLineDto, decimal> Base,
        Func<FiscalDocumentLineDto, decimal> Rate, Func<FiscalDocumentLineDto, decimal> Value,
        string NonCumulativeRevenueCode, string CumulativeRevenueCode);

    // Códigos de receita DCTF do M205/M605 (com variação, 6 caracteres): não cumulativo / cumulativo
    internal static readonly Tax Pis = new(l => l.CstPis, l => l.PisBase, l => l.PisRate, l => l.PisValue, "691201", "810902");
    internal static readonly Tax Cofins = new(l => l.CstCofins, l => l.CofinsBase, l => l.CofinsRate, l => l.CofinsValue, "585601", "217201");

    internal sealed record CreditDetail(string CreditBase, string Cst, decimal Base);

    internal sealed record Credit(string Code, decimal Rate, decimal Base, decimal Value, decimal Used, List<CreditDetail> Details);

    internal sealed record Contribution(string Code, decimal Rate, decimal Revenue, decimal Base, decimal Value);

    internal sealed record Untaxed(string Cst, decimal Revenue, List<(string Nature, decimal Revenue)> ByNature);

    internal sealed record Assessment(
        List<Credit> Credits, List<Contribution> Contributions, List<Untaxed> Untaxed,
        decimal NonCumulativeDue, decimal CumulativeDue, decimal CreditTotal, decimal CreditUsed,
        string NonCumulativeRevenueCode, string CumulativeRevenueCode)
    {
        public decimal Due => NonCumulativeDue + CumulativeDue;
        public decimal Payable => Due - CreditUsed;
    }

    /// <summary>
    /// Apuração de um tributo para a empresa: contribuição das saídas CST 01/02 (COD_CONT 01/02 no não
    /// cumulativo, 51/52 no cumulativo, pelo regime da filial), crédito das entradas CST 50-56 só das
    /// filiais não cumulativas (descontado até a contribuição não cumulativa) e receitas sem
    /// contribuição (CST 04/06/07/08/09) por natureza da receita.
    /// </summary>
    internal static Assessment Assess(EfdContributionsData data, Tax tax)
    {
        var lines = data.Establishments
            .SelectMany(e => e.Lines.Select(l => (Line: l, NonCumulative: e.Branch.PisCofinsRegime == PisCofinsRegime.NonCumulative)))
            .ToList();

        var contributions = lines
            .Where(x => x.Line.Direction == "S" && TaxedRevenueCsts.Contains(tax.Cst(x.Line)))
            .GroupBy(x => (Code: (x.NonCumulative ? "0" : "5") + tax.Cst(x.Line)[1], Rate: tax.Rate(x.Line)))
            .OrderBy(g => g.Key.Code, StringComparer.Ordinal).ThenBy(g => g.Key.Rate)
            .Select(g => new Contribution(g.Key.Code, g.Key.Rate, g.Sum(x => x.Line.OperationValue), g.Sum(x => tax.Base(x.Line)),
                g.Sum(x => tax.Value(x.Line))))
            .ToList();
        var nonCumulativeDue = contributions.Where(c => c.Code[0] == '0').Sum(c => c.Value);
        var cumulativeDue = contributions.Where(c => c.Code[0] == '5').Sum(c => c.Value);

        var creditGroups = lines
            .Where(x => x.NonCumulative && x.Line.Direction == "E" && CreditCsts.Contains(tax.Cst(x.Line)))
            .Select(x => x.Line)
            .GroupBy(l => (Code: CreditCode(tax.Cst(l)), Rate: tax.Rate(l)))
            .OrderBy(g => g.Key.Code, StringComparer.Ordinal).ThenBy(g => g.Key.Rate)
            .ToList();
        var creditTotal = creditGroups.Sum(g => g.Sum(tax.Value));
        var remaining = Math.Min(creditTotal, nonCumulativeDue);
        var credits = new List<Credit>();
        foreach (var g in creditGroups)
        {
            var value = g.Sum(tax.Value);
            var used = Math.Min(value, remaining);
            remaining -= used;
            var details = g
                .GroupBy(l => (CreditBase: CreditBaseCode(data, l), Cst: tax.Cst(l)))
                .OrderBy(d => d.Key.CreditBase, StringComparer.Ordinal).ThenBy(d => d.Key.Cst, StringComparer.Ordinal)
                .Select(d => new CreditDetail(d.Key.CreditBase, d.Key.Cst, d.Sum(tax.Base)))
                .ToList();
            credits.Add(new Credit(g.Key.Code, g.Key.Rate, g.Sum(tax.Base), value, used, details));
        }

        var untaxed = lines
            .Where(x => x.Line.Direction == "S" && UntaxedRevenueCsts.Contains(tax.Cst(x.Line)))
            .Select(x => x.Line)
            .GroupBy(tax.Cst)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new Untaxed(g.Key, g.Sum(l => l.OperationValue), g
                .GroupBy(l => RevenueNatureCode(data, l))
                .OrderBy(n => n.Key, StringComparer.Ordinal)
                .Select(n => (n.Key, n.Sum(l => l.OperationValue)))
                .ToList()))
            .ToList();

        return new Assessment(credits, contributions, untaxed, nonCumulativeDue, cumulativeDue, creditTotal,
            Math.Min(creditTotal, nonCumulativeDue), tax.NonCumulativeRevenueCode, tax.CumulativeRevenueCode);
    }

    private static string CreditBaseCode(EfdContributionsData data, FiscalDocumentLineDto l) =>
        (l.UsageCode is int u ? data.Usages.GetValueOrDefault(u)?.PisCofinsCreditBaseCode : null) ?? "";

    private static string RevenueNatureCode(EfdContributionsData data, FiscalDocumentLineDto l) =>
        (l.UsageCode is int u ? data.Usages.GetValueOrDefault(u)?.PisCofinsRevenueNatureCode : null) ?? "";

    /// <summary>Valor do produto da linha (vProd), como no SPED Fiscal.</summary>
    private static decimal ProductValue(FiscalDocumentLineDto l) =>
        l.OperationValue - l.FreightValue - l.InsuranceValue - l.OtherExpensesValue + l.DiscountValue;

    private static string FreightIndicator(FreightTerms terms) => terms switch
    {
        FreightTerms.Cif => "0",
        FreightTerms.Fob => "1",
        FreightTerms.Ter => "2",
        _ => "9",
    };
}
