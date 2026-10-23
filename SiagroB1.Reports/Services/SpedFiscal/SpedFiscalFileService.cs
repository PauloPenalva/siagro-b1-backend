using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Sped;
using SiagroB1.Reports.Dtos;
using static SiagroB1.Reports.Services.SpedFiscal.SpedFiscalValidator;

namespace SiagroB1.Reports.Services.SpedFiscal;

/// <summary>
/// Monta o arquivo da EFD ICMS/IPI (SPED Fiscal) de uma filial e mês. Lê os tributos gravados nas
/// linhas (mesma fonte da Conferência Fiscal) — nunca recalcula. Bloco D fica fora desta entrega;
/// B, D, G e K saem abertos sem movimento.
/// </summary>
public class SpedFiscalFileService(SpedFiscalDataLoader loader, FiscalConferenceService conference)
{
    /// <summary>Regras da Conferência que aqui já são bloqueantes (não repetem como aviso).</summary>
    private static readonly HashSet<string> BlockingConferenceRules = ["Item sem CFOP", "Item sem CST de ICMS"];

    public async Task<SpedFiscalCheckDto> CheckAsync(SpedFiscalRequest request)
    {
        var data = await loader.LoadAsync(request);
        var issues = Validate(data);

        var preview = await conference.BuildAsync(new FiscalConferenceRequest
        {
            BranchCode = request.BranchCode, Year = request.Year, Month = request.Month,
        });
        issues.AddRange(preview.Issues
            .Where(i => !BlockingConferenceRules.Contains(i.Rule))
            .Select(i => new SpedFiscalIssueDto
            {
                Blocking = false,
                Message = i.ItemCode == null
                    ? $"{i.Rule}: documento {i.Number} ({i.Partner})."
                    : $"{i.Rule}: documento {i.Number}, item {i.ItemCode} ({i.Partner}).",
                Reference = i.ItemCode == null ? i.Number : $"{i.Number}/{i.ItemCode}",
            }));

        var a = Assessment(data, request);
        return new SpedFiscalCheckDto
        {
            Issues = issues,
            Totals = new SpedFiscalTotalsDto { Debits = a.Debits, Credits = a.Credits, Balance = a.Balance },
        };
    }

    /// <summary>Gera o arquivo; qualquer inconsistência bloqueante recusa com todas as mensagens juntas.</summary>
    public async Task<(byte[] Content, string FileName)> GenerateAsync(SpedFiscalRequest request)
    {
        var data = await loader.LoadAsync(request);
        var blocking = Validate(data).Where(i => i.Blocking).Select(i => i.Message).ToList();
        if (blocking.Count > 0)
            throw new BusinessException(string.Join(" ", blocking));

        return (Build(data, request), $"SPED_FISCAL_{Digits(data.Branch.TaxId)}_{request.Year:0000}{request.Month:00}.txt");
    }

    /// <summary>Monta os registros a partir dos dados já carregados e validados.</summary>
    public static byte[] Build(SpedFiscalData data, SpedFiscalRequest request)
    {
        var w = new SpedRecordWriter();
        var start = new DateTime(request.Year, request.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);

        WriteBlock0(w, data, request, start, end);

        w.Add("B001", "1");
        w.Add("B990", w.CountOfBlock('B') + 1);

        WriteBlockC(w, data);

        // Bloco D fora desta entrega (decisão do usuário): aberto sem movimento
        w.Add("D001", "1");
        w.Add("D990", w.CountOfBlock('D') + 1);

        WriteBlockE(w, data, request, start, end);

        w.Add("G001", "1");
        w.Add("G990", w.CountOfBlock('G') + 1);

        WriteBlockH(w, data, request);

        w.Add("K001", "1");
        w.Add("K990", w.CountOfBlock('K') + 1);

        // 1010 (leiaute 019/020): IND_EXP, IND_CCRF, IND_COMB, IND_USINA, IND_VA, IND_EE, IND_CART,
        // IND_FORM, IND_AER, IND_GIAF1, IND_GIAF3, IND_GIAF4, IND_REST_RESSARC_COMPL_ICMS
        w.Add("1001", "0");
        w.Add("1010", "N", "N", "N", "N", "N", "N", "N", "N", "N", "N", "N", "N", "N");
        w.Add("1990", w.CountOfBlock('1') + 1);

        return w.Finish();
    }

    /// <summary>Leiaute 019 até 12/2025; 020 a partir de 01/2026.</summary>
    internal static string LayoutVersion(DateTime start) => start >= new DateTime(2026, 1, 1) ? "020" : "019";

    /// <summary>
    /// CST_ICMS de 3 dígitos (origem + tributação). A linha grava só os 2 da tributação: a origem vem do
    /// cadastro do produto (nula = 0, nacional). Com 3 dígitos, já traz a origem.
    /// </summary>
    internal static string IcmsCst(string cst, SpedItem? item)
    {
        var digits = Digits(cst);
        return digits.Length == 2 ? $"{item?.GoodsOrigin ?? 0}{digits}" : digits;
    }

    private static void WriteBlock0(SpedRecordWriter w, SpedFiscalData data, SpedFiscalRequest request, DateTime start, DateTime end)
    {
        var b = data.Branch;
        var s = data.Settings!;

        // 0000: REG, COD_VER, COD_FIN, DT_INI, DT_FIN, NOME, CNPJ, CPF, UF, IE, COD_MUN, IM, SUFRAMA, IND_PERFIL, IND_ATIV
        w.Add("0000", LayoutVersion(start), request.Purpose, start, end, b.LegalName ?? b.BranchName, Digits(b.TaxId), "",
            b.StateCode, Digits(b.StateRegistration), b.MunicipalityCode, "", "", s.Profile, s.ActivityType);
        w.Add("0001", "0");

        // 0005: REG, FANTASIA, CEP, END, NUM, COMPL, BAIRRO, FONE, FAX, EMAIL
        w.Add("0005", b.TradeName ?? b.BranchName, Digits(b.ZipCode), b.Street, b.StreetNumber, b.Complement, b.District,
            Digits(b.Phone), "", "");

        // 0100: REG, NOME, CPF, CRC, CNPJ, CEP, END, NUM, COMPL, BAIRRO, FONE, FAX, EMAIL, COD_MUN
        w.Add("0100", s.AccountantName, Digits(s.AccountantCpf), s.AccountantCrc, Digits(s.AccountantCnpj), Digits(s.AccountantZipCode),
            s.AccountantStreet, s.AccountantNumber, s.AccountantComplement, s.AccountantDistrict, Digits(s.AccountantPhone), "",
            s.AccountantEmail, s.AccountantMunicipalityCode);

        // 0150: REG, COD_PART, NOME, COD_PAIS, CNPJ, CPF, IE, COD_MUN, SUFRAMA, END, NUM, COMPL, BAIRRO
        foreach (var p in data.Participants)
            w.Add("0150", p.Code, p.Name, p.CountryCode, p.Cnpj, p.Cpf, p.StateRegistration, p.MunicipalityCode, "",
                p.Street, p.Number, p.Complement, p.District);

        // 0190: REG, UNID, DESCR
        foreach (var u in data.Units)
            w.Add("0190", u.Code, u.Description);

        foreach (var i in data.Items)
        {
            // 0200: REG, COD_ITEM, DESCR_ITEM, COD_BARRA, COD_ANT_ITEM, UNID_INV, TIPO_ITEM, COD_NCM, EX_IPI, COD_GEN,
            //       COD_LST, ALIQ_ICMS, CEST
            w.Add("0200", i.Code, i.Description, "", "", i.UnitCode, i.ItemType, i.Ncm, "", "", "", "", i.Cest);

            // 0220 (filho do 0200): REG, UNID_CONV, FAT_CONV, COD_BARRA
            foreach (var c in data.Conversions.Where(c => c.ItemCode == i.Code))
                w.Add("0220", c.UnitCode, new SpedDecimal(c.Factor, 6), "");
        }

        w.Add("0990", w.CountOfBlock('0') + 1);
    }

    private static void WriteBlockC(SpedRecordWriter w, SpedFiscalData data)
    {
        var items = data.Items.ToDictionary(i => i.Code);
        var documents = data.Lines
            .GroupBy(l => l.DocumentKey)
            .Select(g => g.ToList())
            .OrderBy(d => d[0].Date)
            .ThenBy(d => d[0].Number, StringComparer.Ordinal)
            .ThenBy(d => d[0].DocumentType, StringComparer.Ordinal)
            .ToList();

        w.Add("C001", documents.Count > 0 || data.VoidDocuments.Count > 0 ? "0" : "1");

        foreach (var doc in documents)
        {
            var h = doc[0];
            var model55 = h.Model == "55";

            // C100: REG, IND_OPER, IND_EMIT, COD_PART, COD_MOD, COD_SIT, SER, NUM_DOC, CHV_NFE, DT_DOC, DT_E_S, VL_DOC,
            //       IND_PGTO, VL_DESC, VL_ABAT_NT, VL_MERC, IND_FRT, VL_FRT, VL_SEG, VL_OUT_DA, VL_BC_ICMS, VL_ICMS,
            //       VL_BC_ICMS_ST, VL_ICMS_ST, VL_IPI, VL_PIS, VL_COFINS, VL_PIS_ST, VL_COFINS_ST
            w.Add("C100",
                // Sentido da operação (CFOP, mesma fonte do E110): devolução de compra própria é saída
                h.Direction == "E" ? "0" : "1",
                h.IssuerType,
                h.CardCode,
                h.Model,
                doc.Any(l => l.IsComplement) ? "06" : "00",
                h.Series,
                h.Number,
                model55 ? h.AccessKey : "",
                h.IssueDate ?? h.Date,
                h.Date,
                doc.Sum(l => l.OperationValue),
                "2",
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

            // NF-e própria não informa C170 (Guia Prático): só documento de terceiro
            if (h.IssuerType == "1")
            {
                var sequence = 0;
                foreach (var l in doc)
                {
                    sequence++;
                    // C170: REG, NUM_ITEM, COD_ITEM, DESCR_COMPL, QTD, UNID, VL_ITEM, VL_DESC, IND_MOV, CST_ICMS, CFOP,
                    //       COD_NAT, VL_BC_ICMS, ALIQ_ICMS, VL_ICMS, VL_BC_ICMS_ST, ALIQ_ST, VL_ICMS_ST, IND_APUR, CST_IPI,
                    //       COD_ENQ, VL_BC_IPI, ALIQ_IPI, VL_IPI, CST_PIS, VL_BC_PIS, ALIQ_PIS, QUANT_BC_PIS,
                    //       ALIQ_PIS_QUANT, VL_PIS, CST_COFINS, VL_BC_COFINS, ALIQ_COFINS, QUANT_BC_COFINS,
                    //       ALIQ_COFINS_QUANT, VL_COFINS, COD_CTA, VL_ABAT_NT
                    w.Add("C170",
                        l.NfeItemNumber ?? sequence,
                        l.ItemCode,
                        "",
                        new SpedDecimal(l.Quantity, 5),
                        l.UnitOfMeasureCode,
                        ProductValue(l),
                        l.DiscountValue,
                        "0",
                        IcmsCst(l.CstIcms, items.GetValueOrDefault(l.ItemCode)),
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
                        "",
                        0m);
                }
            }

            // C190: REG, CST_ICMS, CFOP, ALIQ_ICMS, VL_OPR, VL_BC_ICMS, VL_ICMS, VL_BC_ICMS_ST, VL_ICMS_ST, VL_RED_BC,
            //       VL_IPI, COD_OBS
            var analytic = doc
                .GroupBy(l => (Cst: IcmsCst(l.CstIcms, items.GetValueOrDefault(l.ItemCode)), l.Cfop, l.IcmsRate))
                .OrderBy(g => g.Key.Cst, StringComparer.Ordinal)
                .ThenBy(g => g.Key.Cfop, StringComparer.Ordinal)
                .ThenBy(g => g.Key.IcmsRate);
            foreach (var g in analytic)
                w.Add("C190", g.Key.Cst, g.Key.Cfop, g.Key.IcmsRate, g.Sum(l => l.OperationValue), g.Sum(l => l.IcmsBase),
                    g.Sum(l => l.IcmsValue), 0m, 0m, 0m, 0m, "");
        }

        // NF-e própria cancelada/denegada/inutilizada: C100 só com os campos 02 a 09 (sem participante);
        // a numeração inutilizada (COD_SIT 05) não tem chave de acesso
        foreach (var v in data.VoidDocuments)
            w.Add("C100", [v.Direction, "0", "", v.Model, v.Situation, v.Series, v.Number, v.Situation == "05" ? "" : v.AccessKey,
                .. Enumerable.Repeat<object?>(null, 20)]);

        w.Add("C990", w.CountOfBlock('C') + 1);
    }

    private static void WriteBlockE(SpedRecordWriter w, SpedFiscalData data, SpedFiscalRequest request, DateTime start, DateTime end)
    {
        var a = Assessment(data, request);
        var payable = Math.Max(a.Balance, 0m);
        var carryForward = Math.Max(-a.Balance, 0m);

        w.Add("E001", "0");
        // E100: REG, DT_INI, DT_FIN
        w.Add("E100", start, end);
        // E110: REG, VL_TOT_DEBITOS, VL_AJ_DEBITOS, VL_TOT_AJ_DEBITOS, VL_ESTORNOS_CRED, VL_TOT_CREDITOS, VL_AJ_CREDITOS,
        //       VL_TOT_AJ_CREDITOS, VL_ESTORNOS_DEB, VL_SLD_CREDOR_ANT, VL_SLD_APURADO, VL_TOT_DED, VL_ICMS_RECOLHER,
        //       VL_SLD_CREDOR_TRANSPORTAR, DEB_ESP
        w.Add("E110", a.Debits, 0m, 0m, 0m, a.Credits, 0m, 0m, 0m, request.PreviousCreditBalance, payable, 0m, payable,
            carryForward, 0m);

        if (payable > 0)
        {
            var s = data.Settings!;
            var next = start.AddMonths(1);
            var due = new DateTime(next.Year, next.Month, Math.Clamp(s.IcmsDueDay, 1, DateTime.DaysInMonth(next.Year, next.Month)));
            // E116: REG, COD_OR, VL_OR, DT_VCTO, COD_REC, NUM_PROC, IND_PROC, PROC, TXT_COMPL, MES_REF
            w.Add("E116", "000", payable, due, s.IcmsRevenueCode, "", "", "", "", start.ToString("MMyyyy", System.Globalization.CultureInfo.InvariantCulture));
        }

        w.Add("E990", w.CountOfBlock('E') + 1);
    }

    private static void WriteBlockH(SpedRecordWriter w, SpedFiscalData data, SpedFiscalRequest request)
    {
        if (!request.IncludeInventory || data.Inventory == null)
        {
            w.Add("H001", "1");
            w.Add("H990", w.CountOfBlock('H') + 1);
            return;
        }

        var rows = InventoryRows(data).OrderBy(r => r.ItemCode, StringComparer.Ordinal).ToList();
        var units = data.Items.ToDictionary(i => i.Code, i => i.UnitCode);
        w.Add("H001", "0");
        // H005: REG, DT_INV, VL_INV, MOT_INV
        w.Add("H005", data.Inventory.Date, rows.Sum(r => r.Value), request.InventoryReason);
        // H010: REG, COD_ITEM, UNID, QTD, VL_UNIT, VL_ITEM, IND_PROP, COD_PART, TXT_COMPL, COD_CTA, VL_ITEM_IR
        foreach (var r in rows)
            w.Add("H010", r.ItemCode, r.UnitOfMeasureCode ?? units.GetValueOrDefault(r.ItemCode), new SpedDecimal(r.Quantity, 3),
                new SpedDecimal(r.UnitCost ?? (r.Quantity != 0 ? r.Value / r.Quantity : 0m), 6), r.Value, "0", "", "", "", "");
        w.Add("H990", w.CountOfBlock('H') + 1);
    }

    /// <summary>Saída ("S") debita, entrada ("E") credita — mesma conta da Conferência; abate o saldo credor anterior.</summary>
    private static (decimal Debits, decimal Credits, decimal Balance) Assessment(SpedFiscalData data, SpedFiscalRequest request)
    {
        var debits = data.Lines.Where(l => l.Direction == "S").Sum(l => l.IcmsValue);
        var credits = data.Lines.Where(l => l.Direction == "E").Sum(l => l.IcmsValue);
        return (debits, credits, debits - credits - request.PreviousCreditBalance);
    }

    /// <summary>
    /// Valor do produto da linha (vProd): o total da linha sem frete, seguro, outras e desconto. É
    /// quantidade × preço na linha comum e o valor complementado na nota de complemento (quantidade 0).
    /// </summary>
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
