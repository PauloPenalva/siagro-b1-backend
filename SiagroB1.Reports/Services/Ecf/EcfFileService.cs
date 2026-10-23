using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Sped;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.Ecd;
using static SiagroB1.Reports.Services.Sped.CompanyBranches;

namespace SiagroB1.Reports.Services.Ecf;

/// <summary>
/// Monta a ECF (Lucro Real) de uma empresa (CNPJ raiz) e ano-calendário, declarada pela matriz. Blocos J/K com os
/// mesmos números da ECD do ano (mesma carga e mesmos calculadores de <see cref="EcdLedger"/>); IRPJ/CSLL pelo
/// <see cref="IncomeTaxCalculator"/> via <see cref="EcfAssessment"/> — os mesmos totais da tela de Verificar.
/// </summary>
public class EcfFileService(EcfDataLoader loader)
{
    /// <summary>
    /// Leiaute da ECF por ano-calendário (COD_VER do 0000) — Manual de Orientação do Leiaute da ECF (ADE Cofis):
    /// leiaute 11 ("0011") para o ano-calendário 2024; leiaute 12 ("0012") a partir de 2025, vigente para 2026
    /// enquanto a RFB não publicar outro. Os registros abaixo seguem o leiaute 11/12 (mesma estrutura nos registros
    /// gerados). Ano anterior ao primeiro é recusado pelo validador. Nova versão = nova linha aqui.
    /// </summary>
    private static readonly (int FromYear, string Version)[] Layouts = [(2024, "0011"), (2025, "0012")];

    public static readonly int FirstSupportedYear = Layouts[0].FromYear;

    public static string LayoutVersion(int year) => Layouts.Last(l => l.FromYear <= year).Version;

    /// <summary>
    /// Natureza jurídica do 0030 (tabela da RFB): 2062 = sociedade empresária limitada. Não há o campo no cadastro;
    /// fixo até existir (a CEAGUI é Ltda).
    /// </summary>
    public const string LegalNatureCode = "2062";

    public async Task<EcfCheckDto> CheckAsync(EcfRequest request)
    {
        var data = await loader.LoadAsync(request);
        var issues = EcfValidator.Validate(data, request);
        var check = new EcfCheckDto { Issues = issues };
        if (data.Settings == null) return check;

        var annual = data.Settings.AssessmentForm == EcfAssessmentForm.Annual;
        var estimates = annual ? EcfAssessment.MonthlyEstimates(data, request.Year) : [];
        check.Estimates = estimates.Select(e => new EcfMonthlyEstimateDto
            { Month = e.Month, CumulativeProfit = e.CumulativeProfit, Irpj = e.Irpj, Csll = e.Csll }).ToList();
        check.Periods = EcfAssessment.Assess(data, data.Settings.AssessmentForm, request.Year).Select(p => new EcfPeriodTotalsDto
        {
            Period = p.Period.Code, Start = p.Period.Start, End = p.Period.End, BookProfit = p.BookProfit,
            IrpjAdditions = p.IrpjAdditions, IrpjExclusions = p.IrpjExclusions, IrpjLossOffset = p.Irpj.OffsetApplied,
            IrpjBase = p.Irpj.TaxableBase, Irpj = p.Irpj.Tax, IrpjSurtax = p.Irpj.Surtax,
            CsllAdditions = p.CsllAdditions, CsllExclusions = p.CsllExclusions, CsllLossOffset = p.Csll.OffsetApplied,
            CsllBase = p.Csll.TaxableBase, Csll = p.Csll.Tax,
            IrpjEstimates = estimates.Sum(e => e.Irpj), CsllEstimates = estimates.Sum(e => e.Csll),
        }).ToList();
        return check;
    }

    /// <summary>Gera o arquivo; qualquer inconsistência bloqueante recusa com todas as mensagens juntas.</summary>
    public async Task<(byte[] Content, string FileName)> GenerateAsync(EcfRequest request)
    {
        var data = await loader.LoadAsync(request);
        var blocking = EcfValidator.Validate(data, request).Where(i => i.Blocking).Select(i => i.Message).ToList();
        if (blocking.Count > 0)
            throw new BusinessException(string.Join(" ", blocking));

        return (Build(data, request), $"ECF_{Digits(data.Ledger.Declarant!.TaxId)}_{request.Year:0000}.txt");
    }

    public static byte[] Build(EcfData data, EcfRequest request)
    {
        var w = new SpedRecordWriter();
        var postings = EcdLedger.Postings(data.Ledger);
        var periods = EcfAssessment.Assess(data, data.Settings!.AssessmentForm, request.Year);
        WriteBlock0(w, data, request);

        // C (dados recuperados da ECD) e E (da ECF anterior): o PVA preenche na recuperação
        w.Add("C001", "1");
        w.Add("C990", w.CountOfBlock('C') + 1);
        w.Add("E001", "1");
        w.Add("E990", w.CountOfBlock('E') + 1);

        WriteBlockJ(w, data.Ledger, postings, request.Year);
        var k = WriteBlockK(w, data.Ledger, postings, periods);
        WriteBlockL(w, data.Ledger, periods, k);
        WriteBlockM(w, data, request, periods);
        WriteBlockN(w, data, request, periods);

        // P (presumido), Q (livro caixa), T (arbitrado), U (imunes/isentas), V (DEREX), W (país a país), X (econômicas):
        // sem dados no Lucro Real deste escopo
        foreach (var block in new[] { 'P', 'Q', 'T', 'U', 'V', 'W', 'X' })
        {
            w.Add($"{block}001", "1");
            w.Add($"{block}990", w.CountOfBlock(block) + 1);
        }

        WriteBlockY(w, data, request, postings);
        return w.Finish();
    }

    private static string Name(Branch b) => b.LegalName ?? b.BranchName;

    private static void WriteBlock0(SpedRecordWriter w, EcfData data, EcfRequest request)
    {
        var d = data.Ledger.Declarant!;
        var s = data.Settings!;
        var ecd = data.Ledger.Settings!;
        var annual = s.AssessmentForm == EcfAssessmentForm.Annual;

        // 0000: REG, NOME_ESC, COD_VER, CNPJ, NOME, IND_SIT_INI_PER, SIT_ESPECIAL, PAT_REMAN_CIS, DT_SIT_ESP, DT_INI,
        //       DT_FIN, RETIFICADORA, NUM_REC, TIP_ECF, COD_SCP
        w.Add("0000", "LECF", LayoutVersion(request.Year), Digits(d.TaxId), Name(d),
            "0",                                  // regular (início no primeiro dia do ano)
            "0",                                  // sem situação especial
            "", "",
            new DateTime(request.Year, 1, 1), new DateTime(request.Year, 12, 31),
            request.Purpose == 1 ? "S" : "N",
            request.Purpose == 1 ? s.PreviousEcfHash?.Trim() : "",
            "0",                                  // PJ não participante de SCP
            "");
        w.Add("0001", "0");

        // 0010: REG, HASH_ECF_ANTERIOR, OPT_REFIS, FORMA_TRIB, FORMA_APUR, COD_QUALIF_PJ, FORMA_TRIB_PER, MES_BAL_RED,
        //       TIP_ESC_PRE, TIP_ENT, FORMA_APUR_I, APUR_CSLL, IND_REC_RECEITA
        // FORMA_TRIB 1 = Lucro Real; MES_BAL_RED só no anual: "B" = balancete de suspensão/redução nos 12 meses.
        // FORMA_TRIB_PER, TIP_ESC_PRE, TIP_ENT, FORMA_APUR_I, APUR_CSLL e IND_REC_RECEITA são de outras formas.
        w.Add("0010", "", "N", "1", annual ? "A" : "T", s.LegalEntityQualification, "",
            annual ? new string('B', 12) : "", "", "", "", "", "");

        // 0020: REG, IND_ALIQ_CSLL, IND_QTE_SCP, IND_ADM_FUN_CLU, IND_PART_CONS, IND_OP_EXT, IND_OP_VINC, IND_PJ_ENQUAD,
        //       IND_PART_EXT, IND_ATIV_RURAL, IND_LUC_EXP, IND_RED_ISEN, IND_FIN, IND_PART_COLIG, IND_VEND_EXP,
        //       IND_REC_EXT, IND_ATIV_EXT, IND_COM_EXP, IND_PGTO_EXT, IND_E-COM_TI, IND_ROY_REC, IND_ROY_PAG,
        //       IND_REND_SERV, IND_PGTO_REM, IND_INOV_TEC, IND_CAP_INF, IND_PJ_HAB, IND_POLO_AM, IND_ZON_EXP,
        //       IND_AREA_COM, IND_PAIS_A_PAIS, IND_DEREX, IND_PR_TRANSF
        // IND_ALIQ_CSLL 1 = 9% (alíquota geral); IND_QTE_SCP 0; demais "N" (não deduzíveis pelo sistema).
        var flags = new object?[32];
        flags[0] = "1";
        flags[1] = "0";
        for (var i = 2; i < flags.Length; i++) flags[i] = "N";
        w.Add("0020", flags);

        // 0030: REG, COD_NAT, CNAE_FISCAL, ENDERECO, NUM, COMPL, BAIRRO, UF, COD_MUN, CEP, NUM_TEL, EMAIL
        w.Add("0030", LegalNatureCode, Digits(s.MainCnae), d.Street, d.StreetNumber, d.Complement, d.District, d.StateCode,
            d.MunicipalityCode, Digits(d.ZipCode), Digits(d.Phone), ecd.SignerEmail);

        // 0930: REG, IDENT_NOM, IDENT_CPF_CNPJ, IDENT_QUALIF, IND_CRC, EMAIL, FONE — responsável e contabilista (os da ECD)
        var a = data.Ledger.Accountant!;
        w.Add("0930", ecd.SignerName, Digits(ecd.SignerCpf), ecd.SignerQualificationCode, "", ecd.SignerEmail, Digits(ecd.SignerPhone));
        w.Add("0930", a.AccountantName, Digits(a.AccountantCpf), "900", a.AccountantCrc.Trim(), a.AccountantEmail, Digits(a.AccountantPhone));
        w.Add("0990", w.CountOfBlock('0') + 1);
    }

    private static void WriteBlockJ(SpedRecordWriter w, EcdData data, List<EcdPosting> postings, int year)
    {
        var accounts = data.Accounts.ToDictionary(a => a.Code);
        var changed = data.Accounting.CutoverDate ?? new DateTime(year, 1, 1);
        w.Add("J001", "0");
        // Mesmo plano do I050 da ECD (analíticas usadas e superiores)
        foreach (var code in EcdLedger.Plan(data, postings))
        {
            var a = accounts[code];
            // J050: REG, DT_ALT, COD_NAT, IND_CTA, NIVEL, COD_CTA, COD_CTA_SUP, CTA
            w.Add("J050", changed, EcdLedger.GroupNature(a.Type), a.AllowsPosting ? "A" : "S", EcdLedger.Depth(a, accounts), a.Code,
                a.ParentCode != null && accounts.ContainsKey(a.ParentCode) ? a.ParentCode : "", a.Name);
            // J051: REG, COD_CCUS, COD_CTA_REF
            if (a.AllowsPosting && !string.IsNullOrWhiteSpace(a.ReferentialCode)) w.Add("J051", "", a.ReferentialCode);
        }

        // J100: REG, DT_ALT, COD_CCUS, CCUS — os centros usados (I100 da ECD)
        var centers = data.CostCenters.ToDictionary(c => c.Code);
        foreach (var cc in EcdLedger.UsedCostCenters(EcdLedger.Months(data, postings)))
            w.Add("J100", changed, cc, centers[cc].Name);
        w.Add("J990", w.CountOfBlock('J') + 1);
    }

    /// <summary>Linha do K155: saldo patrimonial de conta + centro no período.</summary>
    internal sealed record PeriodBalance(string Account, string? CostCenter, decimal Opening, decimal Debit, decimal Credit)
    {
        public decimal Closing => Opening + Debit - Credit;
    }

    /// <summary>K155 e K355 de cada período, para o bloco L somar pelas contas referenciais.</summary>
    internal sealed record PeriodK(EcfPeriod Period, List<PeriodBalance> Balances,
        List<KeyValuePair<(string Account, string? CostCenter), decimal>> Results);

    /// <summary>
    /// K155 do período pelos meses do I155 da ECD: saldo inicial do primeiro mês, débitos e créditos somados, saldo
    /// final do último — só contas patrimoniais (as de resultado vão para o K355).
    /// </summary>
    internal static List<PeriodBalance> PatrimonialBalances(EcdData data, List<List<EcdMonthBalance>> months, EcfPeriod period)
    {
        var result = EcdLedger.ResultAccounts(data);
        // Chave ausente no primeiro mês tinha saldo zero no início do período: a primeira linha dela traz o inicial certo
        return months.Skip(period.Start.Month - 1).Take(period.Months).SelectMany(m => m)
            .Where(r => !result.Contains(r.Account))
            .GroupBy(r => (r.Account, r.CostCenter))
            .Select(g => new PeriodBalance(g.Key.Account, g.Key.CostCenter, g.First().Opening, g.Sum(r => r.Debit), g.Sum(r => r.Credit)))
            .OrderBy(r => r.Account, StringComparer.Ordinal).ThenBy(r => r.CostCenter ?? "", StringComparer.Ordinal)
            .ToList();
    }

    private static List<PeriodK> WriteBlockK(SpedRecordWriter w, EcdData data, List<EcdPosting> postings,
        List<EcfPeriodAssessment> periods)
    {
        var infos = EcdLedger.AccountInfos(data);
        var accounts = data.Accounts.ToDictionary(a => a.Code);
        var months = EcdLedger.Months(data, postings);
        var result = new List<PeriodK>();
        w.Add("K001", "0");
        foreach (var p in periods.Select(x => x.Period))
        {
            // K030: REG, DT_INI, DT_FIN, PER_APUR
            w.Add("K030", p.Start, p.End, p.Code);
            var balances = PatrimonialBalances(data, months, p);
            foreach (var r in balances)
            {
                // K155: REG, COD_CTA, COD_CCUS, VL_SLD_INI, IND_VL_SLD_INI, VL_DEB, VL_CRED, VL_SLD_FIN, IND_VL_SLD_FIN
                w.Add("K155", r.Account, r.CostCenter ?? "", Math.Abs(r.Opening), EcdLedger.Side(r.Opening, infos[r.Account]),
                    r.Debit, r.Credit, Math.Abs(r.Closing), EcdLedger.Side(r.Closing, infos[r.Account]));
                // K156: REG, COD_CTA_REF, VL_SLD_INI, IND_VL_SLD_INI, VL_DEB, VL_CRED, VL_SLD_FIN, IND_VL_SLD_FIN
                if (accounts[r.Account].ReferentialCode is { Length: > 0 } reference)
                    w.Add("K156", reference, Math.Abs(r.Opening), EcdLedger.Side(r.Opening, infos[r.Account]), r.Debit, r.Credit,
                        Math.Abs(r.Closing), EcdLedger.Side(r.Closing, infos[r.Account]));
            }

            // K355: REG, COD_CTA, COD_CCUS, VL_SLD_FIN, IND_VL_SLD_FIN — resultado antes do encerramento (I355 da ECD)
            var results = EcfAssessment.ResultBalances(data, postings, p.Start, p.End);
            foreach (var (key, value) in results)
            {
                w.Add("K355", key.Account, key.CostCenter ?? "", Math.Abs(value), EcdLedger.Side(value, infos[key.Account]));
                // K356: REG, COD_CTA_REF, VL_SLD_FIN, IND_VL_SLD_FIN
                if (accounts[key.Account].ReferentialCode is { Length: > 0 } reference)
                    w.Add("K356", reference, Math.Abs(value), EcdLedger.Side(value, infos[key.Account]));
            }

            result.Add(new PeriodK(p, balances, results));
        }

        w.Add("K990", w.CountOfBlock('K') + 1);
        return result;
    }

    /// <summary>
    /// Bloco L: uma linha por conta referencial analítica usada, com a soma das contas mapeadas (o PVA totaliza as
    /// sintéticas do plano referencial). Nível e superior saem do próprio código ("1.01.01" → nível 3, superior
    /// "1.01"); descrição = código (o PVA usa a da tabela do plano referencial).
    /// </summary>
    private static void WriteBlockL(SpedRecordWriter w, EcdData data, List<EcfPeriodAssessment> periods, List<PeriodK> k)
    {
        var accounts = data.Accounts.ToDictionary(a => a.Code);
        string Nature(string account) => EcdLedger.GroupNature(accounts[account].Type);
        static int Level(string code) => code.Count(c => c == '.') + 1;
        static string Parent(string code) => code.LastIndexOf('.') is var i and > 0 ? code[..i] : "";
        static string Side(decimal v, string nature) => v > 0 ? "D" : v < 0 ? "C" : nature == "01" || nature == "04" ? "D" : "C";

        w.Add("L001", "0");
        foreach (var pk in k)
        {
            // L030: REG, DT_INI, DT_FIN, PER_APUR
            w.Add("L030", pk.Period.Start, pk.Period.End, pk.Period.Code);
            foreach (var g in pk.Balances.Where(r => !string.IsNullOrWhiteSpace(accounts[r.Account].ReferentialCode))
                         .GroupBy(r => accounts[r.Account].ReferentialCode!).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var nature = Nature(g.First().Account);
                var opening = g.Sum(r => r.Opening);
                var closing = g.Sum(r => r.Closing);
                // L100: REG, CODIGO, DESCRICAO, TIPO, NIVEL, COD_NAT, COD_CTA_SUP, VAL_CTA_REF_INI, IND_VAL_CTA_REF_INI,
                //       VAL_CTA_REF_DEB, VAL_CTA_REF_CRED, VAL_CTA_REF_FIN, IND_VAL_CTA_REF_FIN
                w.Add("L100", g.Key, g.Key, "A", Level(g.Key), nature, Parent(g.Key), Math.Abs(opening), Side(opening, nature),
                    g.Sum(r => r.Debit), g.Sum(r => r.Credit), Math.Abs(closing), Side(closing, nature));
            }

            foreach (var g in pk.Results.Where(r => !string.IsNullOrWhiteSpace(accounts[r.Key.Account].ReferentialCode))
                         .GroupBy(r => accounts[r.Key.Account].ReferentialCode!).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var value = g.Sum(r => r.Value);
                // L300: REG, CODIGO, DESCRICAO, TIPO, NIVEL, COD_NAT, COD_CTA_SUP, VALOR, IND_VALOR
                w.Add("L300", g.Key, g.Key, "A", Level(g.Key), "04", Parent(g.Key), Math.Abs(value), Side(value, "04"));
            }
        }

        w.Add("L990", w.CountOfBlock('L') + 1);
    }

    private static string TaxCode(bool irpj) => irpj ? "I" : "C";

    private static void WriteBlockM(SpedRecordWriter w, EcfData data, EcfRequest request, List<EcfPeriodAssessment> periods)
    {
        w.Add("M001", "0");

        // M010: REG, COD_CTA_B, DESC_CTA_LAL, DT_AP_LAL, COD_PB_RFB, COD_LAN_ORIG, DESC_LAN_ORIG, DT_LIM_LAL, TRIBUTO,
        //       VL_SALDO_INI, IND_VL_SALDO_INI, CNPJ_SIT_INI
        // Uma por conta da Parte B usada e tributo; saldo inicial zero (controle entre anos fora do escopo).
        var partB = PartBAccounts(periods);
        foreach (var (code, irpj, adjustment) in partB)
            w.Add("M010", code, adjustment.PartBAccountName ?? adjustment.Description, new DateTime(request.Year, 1, 1), "",
                adjustment.TableCode, adjustment.Description, "", TaxCode(irpj), 0m, "D", "");

        foreach (var p in periods)
        {
            // M030: REG, DT_INI, DT_FIN, PER_APUR
            w.Add("M030", p.Period.Start, p.Period.End, p.Period.Code);
            WritePartA(w, "M300", "M305", "M310", EcfTableCodes.M300BookProfit, p.BookProfit, p.IrpjAdjustments);
            WritePartA(w, "M350", "M355", "M360", EcfTableCodes.M350BookProfit, p.BookProfit, p.CsllAdjustments);

            // M500: REG, COD_CTA_B, COD_TRIBUTO, SD_INI_LAL, IND_SD_INI_LAL, VL_LCTO_PARTE_A, IND_VL_LCTO_PARTE_A,
            //       VL_LCTO_PARTE_B, IND_VL_LCTO_PARTE_B, SD_FIM_LAL, IND_SD_FIM_LAL
            // Adição credita a conta da Parte B (valor a excluir no futuro); exclusão e compensação debitam.
            // Sem lançamentos só da Parte B (M410): os ajustes do Siagro sempre passam pela Parte A.
            foreach (var (code, irpj, _) in partB)
            {
                var list = irpj ? p.IrpjAdjustments : p.CsllAdjustments;
                var movement = list.Where(a => a.PartBAccountCode?.Trim() == code)
                    .Sum(a => a.Kind == EcfAdjustmentKind.Addition ? -a.Amount : a.Amount);
                w.Add("M500", code, TaxCode(irpj), 0m, "D", Math.Abs(movement), movement > 0 ? "D" : "C", 0m, "D",
                    Math.Abs(movement), movement > 0 ? "D" : "C");
            }
        }

        w.Add("M990", w.CountOfBlock('M') + 1);
    }

    private static List<(string Code, bool Irpj, EcfAdjustment First)> PartBAccounts(List<EcfPeriodAssessment> periods) =>
        periods.SelectMany(p => p.IrpjAdjustments.Select(a => (Irpj: true, a)).Concat(p.CsllAdjustments.Select(a => (Irpj: false, a))))
            .Where(x => !string.IsNullOrWhiteSpace(x.a.PartBAccountCode))
            .GroupBy(x => (Code: x.a.PartBAccountCode!.Trim(), x.Irpj))
            .OrderBy(g => g.Key.Code, StringComparer.Ordinal).ThenByDescending(g => g.Key.Irpj)
            .Select(g => (g.Key.Code, g.Key.Irpj, g.First().a)).ToList();

    /// <summary>
    /// M300/M350: linha do lucro antes do tributo ("L") e um lançamento por ajuste (A adição, E exclusão,
    /// P compensação), com a conta da Parte B (M305/M355) e a contábil (M310/M360) quando informadas.
    /// </summary>
    private static void WritePartA(SpedRecordWriter w, string reg, string partBReg, string accountReg, EcfTableLine bookProfitLine,
        decimal bookProfit, List<EcfAdjustment> adjustments)
    {
        // M300/M350: REG, CODIGO, DESCRICAO, TIPO_LANCAMENTO, IND_RELACAO, VALOR, HIST_LAN_LAL
        // IND_RELACAO: 1 com Parte B, 2 com conta contábil, 3 ambas, 4 nenhuma; lucro/prejuízo pelo valor absoluto no rótulo
        w.Add(reg, bookProfitLine.Code, bookProfitLine.Description, "L", "4", bookProfit, "");
        foreach (var a in adjustments)
        {
            var hasB = !string.IsNullOrWhiteSpace(a.PartBAccountCode);
            var hasAccount = !string.IsNullOrWhiteSpace(a.LedgerAccountCode);
            var kind = a.Kind switch { EcfAdjustmentKind.Addition => "A", EcfAdjustmentKind.Exclusion => "E", _ => "P" };
            w.Add(reg, a.TableCode.Trim(), a.Description, kind, hasB && hasAccount ? "3" : hasB ? "1" : hasAccount ? "2" : "4",
                a.Amount, a.Description);
            // M305/M355: REG, COD_CTA_B, VL_CTA, IND_VL_CTA
            if (hasB) w.Add(partBReg, a.PartBAccountCode!.Trim(), a.Amount, a.Kind == EcfAdjustmentKind.Addition ? "C" : "D");
            // M310/M360: REG, COD_CTA, COD_CCUS, VL_CTA, IND_VL_CTA
            if (hasAccount) w.Add(accountReg, a.LedgerAccountCode!.Trim(), "", a.Amount, a.Kind == EcfAdjustmentKind.Addition ? "D" : "C");
        }
    }

    private static void Lines(SpedRecordWriter w, string reg, params (EcfTableLine Line, decimal Value)[] lines)
    {
        // N500/N620/N630/N650/N660/N670: REG, CODIGO, DESCRICAO, VALOR
        foreach (var (line, value) in lines) w.Add(reg, line.Code, line.Description, value);
    }

    private static void WriteBlockN(SpedRecordWriter w, EcfData data, EcfRequest request, List<EcfPeriodAssessment> periods)
    {
        var annual = data.Settings!.AssessmentForm == EcfAssessmentForm.Annual;
        var estimates = annual ? EcfAssessment.MonthlyEstimates(data, request.Year) : [];
        w.Add("N001", "0");

        // Anual: um período A01..A12 por balancete acumulado (estimativas), depois o A00 do ano
        foreach (var e in estimates)
        {
            var first = new DateTime(request.Year, e.Month, 1);
            // N030: REG, DT_INI, DT_FIN, PER_APUR
            w.Add("N030", first, first.AddMonths(1).AddDays(-1), $"A{e.Month:00}");
            Lines(w, "N620",
                (EcfTableCodes.N620Base, e.IrpjCumulative.TaxableBase), (EcfTableCodes.N620Tax, e.IrpjCumulative.Tax),
                (EcfTableCodes.N620Surtax, e.IrpjCumulative.Surtax), (EcfTableCodes.N620Due, e.IrpjCumulative.Total),
                (EcfTableCodes.N620PreviouslyDue, e.IrpjPreviouslyDue), (EcfTableCodes.N620Payable, e.Irpj));
            Lines(w, "N660",
                (EcfTableCodes.N660Base, e.CsllCumulative.TaxableBase), (EcfTableCodes.N660Tax, e.CsllCumulative.Tax),
                (EcfTableCodes.N660PreviouslyDue, e.CsllPreviouslyDue), (EcfTableCodes.N660Payable, e.Csll));
        }

        var irpjEstimates = estimates.Sum(e => e.Irpj);
        var csllEstimates = estimates.Sum(e => e.Csll);
        foreach (var p in periods)
        {
            w.Add("N030", p.Period.Start, p.Period.End, p.Period.Code);
            Lines(w, "N500",
                (EcfTableCodes.N500BookProfit, p.BookProfit), (EcfTableCodes.N500Additions, p.IrpjAdditions),
                (EcfTableCodes.N500Exclusions, p.IrpjExclusions), (EcfTableCodes.N500AdjustedProfit, p.Irpj.AdjustedProfit),
                (EcfTableCodes.N500LossOffset, p.Irpj.OffsetApplied), (EcfTableCodes.N500TaxableIncome, p.Irpj.TaxableBase));
            // Saldo negativo (estimativas acima do devido) não sai como "a pagar": a pagar nunca negativo
            Lines(w, "N630",
                (EcfTableCodes.N630Base, p.Irpj.TaxableBase), (EcfTableCodes.N630Tax, p.Irpj.Tax), (EcfTableCodes.N630Surtax, p.Irpj.Surtax),
                (EcfTableCodes.N630Due, p.Irpj.Total), (EcfTableCodes.N630Estimates, irpjEstimates),
                (EcfTableCodes.N630Payable, Math.Max(0m, p.Irpj.Total - irpjEstimates)));
            Lines(w, "N650",
                (EcfTableCodes.N650BookProfit, p.BookProfit), (EcfTableCodes.N650Additions, p.CsllAdditions),
                (EcfTableCodes.N650Exclusions, p.CsllExclusions), (EcfTableCodes.N650AdjustedProfit, p.Csll.AdjustedProfit),
                (EcfTableCodes.N650LossOffset, p.Csll.OffsetApplied), (EcfTableCodes.N650Base, p.Csll.TaxableBase));
            Lines(w, "N670",
                (EcfTableCodes.N670Base, p.Csll.TaxableBase), (EcfTableCodes.N670Tax, p.Csll.Tax),
                (EcfTableCodes.N670Estimates, csllEstimates), (EcfTableCodes.N670Payable, Math.Max(0m, p.Csll.Total - csllEstimates)));
        }

        w.Add("N990", w.CountOfBlock('N') + 1);
    }

    /// <summary>Receita bruta do ano: créditos das contas de receita, sem a apuração de resultado.</summary>
    internal static decimal GrossRevenue(EcdData data, List<EcdPosting> postings)
    {
        var revenue = data.Accounts.Where(a => a.Type == LedgerAccountType.Revenue).Select(a => a.Code).ToHashSet();
        return postings.Where(p => !p.ResultClosing && revenue.Contains(p.Account)).Sum(p => p.Credit);
    }

    private static void WriteBlockY(SpedRecordWriter w, EcfData data, EcfRequest request, List<EcdPosting> postings)
    {
        var d = data.Ledger.Declarant!;
        w.Add("Y001", "0");
        // Y540: REG, CNPJ_ESTAB, VL_REC_ESTAB, CNAE — tudo no CNAE principal da declarante
        w.Add("Y540", Digits(d.TaxId), GrossRevenue(data.Ledger, postings), Digits(data.Settings!.MainCnae));
        foreach (var p in data.Partners)
        {
            var taxId = Digits(p.TaxId);
            // Y600: REG, DT_ALT_SOC, DT_FIM_SOC, PAIS, IND_QUALIF_SOCIO, CPF_CNPJ, NOM_EMP, QUALIF, PERC_CAP_TOT,
            //       PERC_CAP_VOT, CPF_REP_LEG, QUALIF_REP_LEG, VL_REM_TRAB, VL_LUC_DIV, VL_JUR_CAP, VL_DEM_REND, VL_IR_RET
            // DT_ALT_SOC = início do ano (o cadastro não guarda a data de entrada); valores pagos não informados (zero)
            w.Add("Y600", new DateTime(request.Year, 1, 1), "", p.CountryCode, taxId.Length == 11 ? "PF" : "PJ", taxId, p.Name,
                p.Qualification, p.TotalCapitalPercent, p.VotingCapitalPercent, "", "", 0m, 0m, 0m, 0m, 0m);
        }

        w.Add("Y990", w.CountOfBlock('Y') + 1);
    }
}
