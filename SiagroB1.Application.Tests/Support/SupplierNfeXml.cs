using System.Globalization;
using System.Text;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// NF-e de fornecedor para os testes, no layout real (o da entrada nº 5 autorizada em homologação). Emitente =
/// o CPF do fornecedor <see cref="PurchaseNfeTestSeed.Supplier"/>, para a importação resolver o parceiro.
/// </summary>
public static class SupplierNfeXml
{
    public const string AccessKey = "35261000052998224725550010000004561123456782";

    private static string F2(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    private static string F4(decimal v) => v.ToString("0.0000", CultureInfo.InvariantCulture);

    public static string Build(params string[] dets) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<nfeProc xmlns=\"http://www.portalfiscal.inf.br/nfe\" versao=\"4.00\"><NFe>" +
        $"<infNFe Id=\"NFe{AccessKey}\" versao=\"4.00\"><ide><cUF>35</cUF><nNF>456</nNF><serie>1</serie>" +
        "<dhEmi>2026-10-01T08:00:00-03:00</dhEmi></ide><emit><CPF>52998224725</CPF><xNome>PRODUTOR RURAL TESTE</xNome></emit>" +
        string.Join(string.Empty, dets) +
        "<total><ICMSTot><vNF>1500.00</vNF></ICMSTot></total></infNFe></NFe></nfeProc>";

    /// <summary>Mesmo XML sem o envelope <c>nfeProc</c> (arquivo "NFe" puro).</summary>
    public static string BuildPlain(params string[] dets) =>
        Build(dets).Replace("<nfeProc xmlns=\"http://www.portalfiscal.inf.br/nfe\" versao=\"4.00\"><NFe>",
                "<NFe xmlns=\"http://www.portalfiscal.inf.br/nfe\">")
            .Replace("</NFe></nfeProc>", "</NFe>");

    public static string Det(int nItem, string code, string name, decimal quantity, decimal unitPrice, string icms,
        string? ibsCbs = null, string? ipi = null, string cfop = "5102", string ncm = "10019900", string? benefitCode = "SP053521") =>
        $"<det nItem=\"{nItem}\"><prod><cProd>{code}</cProd><cEAN>SEM GTIN</cEAN><xProd>{name}</xProd><NCM>{ncm}</NCM>" +
        (benefitCode is null ? string.Empty : $"<cBenef>{benefitCode}</cBenef>") +
        $"<CFOP>{cfop}</CFOP><uCom>KG</uCom><qCom>{F4(quantity)}</qCom><vUnCom>{unitPrice.ToString("0.0000000000", CultureInfo.InvariantCulture)}</vUnCom>" +
        $"<vProd>{F2(quantity * unitPrice)}</vProd></prod><imposto><ICMS>{icms}</ICMS>{ipi}" +
        "<PIS><PISOutr><CST>49</CST><vBC>0.00</vBC><pPIS>0.0000</pPIS><vPIS>0.00</vPIS></PISOutr></PIS>" +
        "<COFINS><COFINSOutr><CST>49</CST><vBC>0.00</vBC><pCOFINS>0.0000</pCOFINS><vCOFINS>0.00</vCOFINS></COFINSOutr></COFINS>" +
        $"{ibsCbs}</imposto></det>";

    public static string Icms51(decimal baseValue) =>
        $"<ICMS51><orig>0</orig><CST>51</CST><modBC>3</modBC><vBC>{F2(baseValue)}</vBC><pICMS>18.0000</pICMS>" +
        $"<vICMSOp>{F2(baseValue * 0.18m)}</vICMSOp><pDif>100.0000</pDif><vICMSDif>{F2(baseValue * 0.18m)}</vICMSDif>" +
        "<vICMS>0.00</vICMS></ICMS51>";

    public static string Icms00(decimal baseValue, decimal rate) =>
        $"<ICMS00><orig>0</orig><CST>00</CST><modBC>3</modBC><vBC>{F2(baseValue)}</vBC><pICMS>{F4(rate)}</pICMS>" +
        $"<vICMS>{F2(baseValue * rate / 100m)}</vICMS></ICMS00>";

    public static string Icms20(decimal baseValue, decimal reduction, decimal rate) =>
        $"<ICMS20><orig>0</orig><CST>20</CST><modBC>3</modBC><pRedBC>{F4(reduction)}</pRedBC><vBC>{F2(baseValue)}</vBC>" +
        $"<pICMS>{F4(rate)}</pICMS><vICMS>{F2(baseValue * rate / 100m)}</vICMS></ICMS20>";

    public static string IcmsSn101() =>
        "<ICMSSN101><orig>0</orig><CSOSN>101</CSOSN><pCredSN>1.2500</pCredSN><vCredICMSSN>12.50</vCredICMSSN></ICMSSN101>";

    public static string Icms10WithSt() =>
        "<ICMS10><orig>0</orig><CST>10</CST><modBC>3</modBC><vBC>1000.00</vBC><pICMS>18.0000</pICMS><vICMS>180.00</vICMS>" +
        "<modBCST>4</modBCST><vBCST>1200.00</vBCST><pICMSST>18.0000</pICMSST><vICMSST>36.00</vICMSST></ICMS10>";

    public static string Ipi(decimal value) =>
        $"<IPI><cEnq>999</cEnq><IPITrib><CST>50</CST><vBC>1000.00</vBC><pIPI>5.0000</pIPI><vIPI>{F2(value)}</vIPI></IPITrib></IPI>";

    /// <summary>IBS/CBS como na entrada nº 5: CST 200, cClassTrib 200036, redução de 60%.</summary>
    public static string IbsCbs() =>
        "<IBSCBS><CST>200</CST><cClassTrib>200036</cClassTrib><gIBSCBS><vBC>1500.00</vBC>" +
        "<gIBSUF><pIBSUF>0.1000</pIBSUF><gRed><pRedAliq>60.0000</pRedAliq><pAliqEfet>0.0400</pAliqEfet></gRed><vIBSUF>0.60</vIBSUF></gIBSUF>" +
        "<gIBSMun><pIBSMun>0.0000</pIBSMun><gRed><pRedAliq>60.0000</pRedAliq><pAliqEfet>0.0000</pAliqEfet></gRed><vIBSMun>0.00</vIBSMun></gIBSMun>" +
        "<vIBS>0.60</vIBS><gCBS><pCBS>0.9000</pCBS><gRed><pRedAliq>60.0000</pRedAliq><pAliqEfet>0.3600</pAliqEfet></gRed><vCBS>5.40</vCBS></gCBS>" +
        "</gIBSCBS></IBSCBS>";

    public static byte[] Bytes(string xml) => Encoding.UTF8.GetBytes(xml);
}
