using System.Globalization;
using System.Reflection;
using NFe.Classes;
using NFe.Classes.Informacoes.Detalhe;
using NFe.Classes.Informacoes.Emitente;

namespace SiagroB1.Reports.Services;

/// <summary>Um item da grade: cada propriedade é o texto de uma célula, com as linhas separadas por "\n".</summary>
internal sealed record DanfeRtcItem(
    string ClassTrib,      // cClassTrib ou ""
    string Bases,          // 4 linhas: ICMS, IBS/CBS, IS, IPI
    string RatesLeft,      // 3 linhas: ICMS, IBS UF, IBS MUN
    string RatesRight,     // 3 linhas: CBS, IPI, IS
    string ValuesLeft,     // 3 linhas: ICMS, IBS UF, IBS MUN
    string ValuesRight);   // 3 linhas: CBS, IPI, IS

/// <summary>
/// Valores da Reforma Tributária (IBS/CBS/IS) e do bloco de tributos do DANFE da NT 2026.010, já como texto
/// pronto para imprimir, lidos do procNFe autorizado. Campo ausente no XML sai como "".
/// </summary>
internal sealed record DanfeRtcData(
    string Crt,
    string TotalCbs, string TotalIbsUf, string TotalIbsMun, string TotalIs,
    bool ShowIbsCbsMonoLine,
    string TotalIbsMono, string TotalCbsMono, string TotalIbsMonoReten, string TotalCbsMonoReten,
    bool ShowFcpLine,
    string TotalFcp, string TotalFcpSt, string TotalDifal, string TotalFcpUfDest,
    bool ShowIcmsMonoLine,
    string TotalIcmsMonoBase, string TotalIcmsMono, string TotalIcmsMonoRetenBase, string TotalIcmsMonoReten,
    IReadOnlyList<DanfeRtcItem> Items)
{
    // Cultura fixa: o texto não pode mudar conforme a cultura da thread do servidor.
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static DanfeRtcData From(nfeProc proc)
    {
        var inf = proc.NFe.infNFe;
        var total = inf.total;
        var icmsTot = total?.ICMSTot;
        var ibsCbsTot = total?.IBSCBSTot;

        // A presença se decide pelo grupo: os campos internos são decimal não anulável.
        var cbs = ibsCbsTot?.gCBS is { } c ? Money(c.vCBS) : "";
        var ibsUf = ibsCbsTot?.gIBS?.gIBSUF is { } u ? Money(u.vIBSUF) : "";
        var ibsMun = ibsCbsTot?.gIBS?.gIBSMun is { } m ? Money(m.vIBSMun) : "";
        var isTotal = total?.ISTot is { } s ? Money(s.vIS) : "";

        var mono = ibsCbsTot?.gMono;
        var showIbsCbsMono = mono is not null
            && (mono.vIBSMono > 0 || mono.vCBSMono > 0 || mono.vIBSMonoReten > 0 || mono.vCBSMonoReten > 0);

        var showFcp = Positive(icmsTot?.vFCP) || Positive(icmsTot?.vFCPST)
            || Positive(icmsTot?.vICMSUFDest) || Positive(icmsTot?.vFCPUFDest);

        var showIcmsMono = Positive(icmsTot?.qBCMono) || Positive(icmsTot?.vICMSMono)
            || Positive(icmsTot?.qBCMonoReten) || Positive(icmsTot?.vICMSMonoReten);

        return new DanfeRtcData(
            CrtText(inf.emit?.CRT),
            cbs, ibsUf, ibsMun, isTotal,
            showIbsCbsMono,
            showIbsCbsMono ? Money(mono!.vIBSMono) : "", showIbsCbsMono ? Money(mono!.vCBSMono) : "",
            showIbsCbsMono ? Money(mono!.vIBSMonoReten) : "", showIbsCbsMono ? Money(mono!.vCBSMonoReten) : "",
            showFcp,
            showFcp ? Money(icmsTot!.vFCP) : "", showFcp ? Money(icmsTot!.vFCPST) : "",
            showFcp ? Money(icmsTot!.vICMSUFDest) : "", showFcp ? Money(icmsTot!.vFCPUFDest) : "",
            showIcmsMono,
            showIcmsMono ? Money(icmsTot!.qBCMono) : "", showIcmsMono ? Money(icmsTot!.vICMSMono) : "",
            showIcmsMono ? Money(icmsTot!.qBCMonoReten) : "", showIcmsMono ? Money(icmsTot!.vICMSMonoReten) : "",
            inf.det.Select(BuildItem).ToList());
    }

    /// <summary>Define um parâmetro "Rtc…" por propriedade e um string[] por coluna de item (índice = posição em det).</summary>
    public void ApplyTo(FastReport.Report report)
    {
        report.SetParameterValue("RtcCrt", Crt);
        report.SetParameterValue("RtcTotalCbs", TotalCbs);
        report.SetParameterValue("RtcTotalIbsUf", TotalIbsUf);
        report.SetParameterValue("RtcTotalIbsMun", TotalIbsMun);
        report.SetParameterValue("RtcTotalIs", TotalIs);
        report.SetParameterValue("RtcShowIbsCbsMonoLine", ShowIbsCbsMonoLine);
        report.SetParameterValue("RtcTotalIbsMono", TotalIbsMono);
        report.SetParameterValue("RtcTotalCbsMono", TotalCbsMono);
        report.SetParameterValue("RtcTotalIbsMonoReten", TotalIbsMonoReten);
        report.SetParameterValue("RtcTotalCbsMonoReten", TotalCbsMonoReten);
        report.SetParameterValue("RtcShowFcpLine", ShowFcpLine);
        report.SetParameterValue("RtcTotalFcp", TotalFcp);
        report.SetParameterValue("RtcTotalFcpSt", TotalFcpSt);
        report.SetParameterValue("RtcTotalDifal", TotalDifal);
        report.SetParameterValue("RtcTotalFcpUfDest", TotalFcpUfDest);
        report.SetParameterValue("RtcShowIcmsMonoLine", ShowIcmsMonoLine);
        report.SetParameterValue("RtcTotalIcmsMonoBase", TotalIcmsMonoBase);
        report.SetParameterValue("RtcTotalIcmsMono", TotalIcmsMono);
        report.SetParameterValue("RtcTotalIcmsMonoRetenBase", TotalIcmsMonoRetenBase);
        report.SetParameterValue("RtcTotalIcmsMonoReten", TotalIcmsMonoReten);
        report.SetParameterValue("RtcItemClassTrib", Items.Select(i => i.ClassTrib).ToArray());
        report.SetParameterValue("RtcItemBases", Items.Select(i => i.Bases).ToArray());
        report.SetParameterValue("RtcItemRatesLeft", Items.Select(i => i.RatesLeft).ToArray());
        report.SetParameterValue("RtcItemRatesRight", Items.Select(i => i.RatesRight).ToArray());
        report.SetParameterValue("RtcItemValuesLeft", Items.Select(i => i.ValuesLeft).ToArray());
        report.SetParameterValue("RtcItemValuesRight", Items.Select(i => i.ValuesRight).ToArray());
    }

    private static DanfeRtcItem BuildItem(det d)
    {
        var tax = d.imposto;
        var ibsCbs = tax?.IBSCBS;
        var group = ibsCbs?.gIBSCBS;
        var selective = tax?.IS;

        var icms = tax?.ICMS?.TipoICMS;
        var ipi = tax?.IPI?.TipoIPI;

        var ibsUf = group?.gIBSUF;
        var ibsMun = group?.gIBSMun;
        var cbs = group?.gCBS;

        // Alíquota efetiva: com redução (gRed) vale pAliqEfet, inclusive zero; sem redução, a alíquota cheia.
        var ibsUfRate = ibsUf is null ? "" : Rate(ibsUf.gRed is { } r1 ? r1.pAliqEfet : ibsUf.pIBSUF);
        var ibsMunRate = ibsMun is null ? "" : Rate(ibsMun.gRed is { } r2 ? r2.pAliqEfet : ibsMun.pIBSMun);
        var cbsRate = cbs is null ? "" : Rate(cbs.gRed is { } r3 ? r3.pAliqEfet : cbs.pCBS);

        var bases = new[]
        {
            ReflectedMoney(icms, "vBC"),
            group is null ? "" : Money(group.vBC),
            selective is null ? "" : Money(selective.vBCIS),
            ReflectedMoney(ipi, "vBC"),
        };
        var ratesLeft = new[] { ReflectedRate(icms, "pICMS"), ibsUfRate, ibsMunRate };
        var ratesRight = new[] { cbsRate, ReflectedRate(ipi, "pIPI"), selective is null ? "" : Rate(selective.pIS) };
        var valuesLeft = new[]
        {
            ReflectedMoney(icms, "vICMS"),
            ibsUf is null ? "" : Money(ibsUf.vIBSUF),
            ibsMun is null ? "" : Money(ibsMun.vIBSMun),
        };
        var valuesRight = new[]
        {
            cbs is null ? "" : Money(cbs.vCBS),
            ReflectedMoney(ipi, "vIPI"),
            selective is null ? "" : Money(selective.vIS),
        };

        return new DanfeRtcItem(
            ibsCbs?.cClassTrib ?? "",
            string.Join('\n', bases), string.Join('\n', ratesLeft), string.Join('\n', ratesRight),
            string.Join('\n', valuesLeft), string.Join('\n', valuesRight));
    }

    private static string CrtText(CRT? crt) => crt switch
    {
        CRT.SimplesNacional => "1 - SIMPLES NACIONAL",
        CRT.SimplesNacionalExcessoSublimite => "2 - SIMPLES NACIONAL, EXCESSO DE SUBLIMITE DE RECEITA BRUTA",
        CRT.RegimeNormal => "3 - REGIME NORMAL",
        CRT.SimplesNacionalMei => "4 - SIMPLES NACIONAL - MEI",
        _ => "",
    };

    private static string Money(decimal value) => value.ToString("N2", PtBr);

    /// <summary>Campo ausente no XML sai em branco, nunca "0,00".</summary>
    private static string Money(decimal? value) => value is { } v ? Money(v) : "";

    private static string Rate(decimal value) => value.ToString("0.00##", PtBr) + "%";

    private static bool Positive(decimal? value) => value is > 0;

    /// <summary>As classes de ICMS/IPI variam por CST: propriedade inexistente ou nula vira "", sem lançar.</summary>
    private static decimal? Reflected(object? source, string property)
    {
        try
        {
            return source?.GetType().GetProperty(property)?.GetValue(source) switch
            {
                decimal value => value,
                _ => null,
            };
        }
        catch (Exception ex) when (ex is AmbiguousMatchException or TargetInvocationException)
        {
            return null;
        }
    }

    private static string ReflectedMoney(object? source, string property) =>
        Reflected(source, property) is { } value ? Money(value) : "";

    private static string ReflectedRate(object? source, string property) =>
        Reflected(source, property) is { } value ? Rate(value) : "";
}
