# Devolução de compra de entrada de terceiro — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dar o "Devolver" ao Documento de Entrada de terceiro na filial que emite NF-e pelo Siagro, com a tributação do fornecedor (lida do XML) como régua da conferência.

**Architecture:** Um leitor do XML do fornecedor (`SupplierNfeXmlReader`) alimenta a importação (o `nItem` vai ao rascunho) e a gravação do documento de terceiro (a tributação do `det` vai para a fotografia fiscal da linha). A devolução reaproveita todo o pipeline da devolução da entrada própria: muda a guarda da origem, a natureza vem da filial, o `nItem` pode ser digitado e a conferência só roda onde a origem tem fotografia.

**Tech Stack:** .NET 10 / EF Core 10 / OData v4 (xUnit + EF InMemory); OpenUI5 1.141 + TypeScript (QUnit).

**Spec:** `siagro-b1-backend/docs/superpowers/specs/2026-10-05-nfe-third-party-purchase-return-design.md` (commit ffa0c8a).

## Global Constraints

- Código (classes, tabelas, colunas) em inglês; texto que o usuário lê (labels, mensagens) em **pt-BR**; comentários em pt-BR.
- **Regra ativa** = `Erp == "STANDALONE"` **e** `Branch.IssuesNfe`, sempre perguntada ao `TaxCalculationGate.IsActiveAsync(branchCode)`. Sem ela (Yokotobi/SAPB1, MH Agro): comportamento idêntico ao de hoje.
- **Devolução de compra** = `InvoiceType == Return` **e** `IsNfeReturn == true` (e `IssuerType == Own`). Só `PurchaseInvoicesNfeReturnCreateService` grava `IsNfeReturn = true`.
- **Entrada de terceiro devolvível** = filial com regra ativa, `IssuerType == ThirdParty`, `InvoiceType == Normal`, `InvoiceStatus == Confirmed`, `ChaveNFe` com 44 dígitos (só dígitos). "De terceiro + Devolução" é a devolução DO CLIENTE e continua sem "Devolver".
- **Os testes de hoje não mudam de asserção** (devolução da entrada própria, venda, devolução de venda). Exceção prevista: nenhuma.
- Regras da SEFAZ: `finNFe 4` exige `tPag 90` com `vPag 0` (rejeição 871); referência num nível só (rejeição 1010); `DFeReferenciado` em cada item; o destinatário da devolução é o emitente da nota referenciada (VC02-50, rejeição 1194).
- Mensagens de guarda/negócio: `DefaultException` em pt-BR (o controller devolve 400).
- Migration única `AddThirdPartyPurchaseReturn` (aditiva, `AppDbContext`). Gerar e aplicar a partir de `siagro-b1-backend/` com `ASPNETCORE_ENVIRONMENT=Ceagui-Development` (banco `CEAGUI_SIAGRO_DEV`). **Ler a migration gerada antes de seguir.** Não aplicar em nenhum outro banco.
- Novo arquivo ⇒ `git add <arquivo>` imediato no repo dele.
- Branch nos dois repos: `feature/nfe-third-party-purchase-return` (conferir antes de cada commit). Commits por tarefa, padrão `tipo(escopo): descrição pt-BR` (tipos `feat fix refactor perf chore docs test`; escopos `invoice` e `master-data`); trailer `DB: AddThirdPartyPurchaseReturn` no commit da migration. Mensagem termina com:
  ```
  Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01BgJE3fiJjTbTPaTvZhCpnx
  ```
  **Sempre com pathspec explícito** (`git commit -F <msg> -- <arquivos>`): no backend `docs/superpowers/{specs,plans}/2026-10-01-nfe-standalone-taxation*` estão staged e **não** entram; no frontend `.vscode/.advpl/*.tlpp` estão modificados e **não** entram. Nunca push.
- Commit negado pelo classificador do auto mode: grave a mensagem num arquivo, registre o comando no relatório e siga. Nunca contornar.
- Testes backend (de `siagro-b1-backend/`): `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Classe>"`; suíte inteira `dotnet test SiagroB1.Application.Tests` e `dotnet test SiagroB1.Fiscal.Tests`. Antes de compilar, pare qualquer `SiagroB1.Web`/`Gateway`/`Reports` iniciado por você (travam as DLLs).
- Frontend (de `siagro-b1-frontend/`): `yarn ts-typecheck`, `yarn lint`, `npx ui5lint` (base **919** — não pode subir), QUnit: `npx ui5 serve --port 8081` em segundo plano e `npx ui5-test-runner --url "http://localhost:8081/test/Test.qunit.html?testsuite=test-resources/siagrob1/testsuite.qunit&test=unit/unitTests" --report-dir <pasta fora do repo>`; depois pare o `ui5 serve` pelo PID e confira que não ficou pasta `report/` no repo. `yarn test` nunca passa (gate de cobertura) — não use.
- Frontend: enum ou booleano OData em expressão usa `${path: 'X', targetType: 'any'}` e compara com `=== true`; tipo de dado em XML via `core:require`; `--` dentro de comentário XML mata o fragmento; `setProperty` em contexto de grupo diferido **não** leva `await`; propriedade que a tela edita numa entidade transiente precisa existir no `create()` inicial.

## Review Focus

1. **Linha importada cujo produto o operador trocou depois** (o `cProd` do fornecedor não existe no nosso cadastro): ao regravar, o `nItem` e a fotografia do fornecedor têm de continuar na linha — teste na Task 2 (`Update_keeps_the_supplier_item_after_the_product_changes`).
2. **Segunda devolução parcial de documento digitado:** o `nItem` digitado na primeira tem de ser reaproveitado sem pedir de novo, e um `ItemNumber` mandado na segunda é ignorado — teste na Task 4 (`Typed_item_number_is_saved_and_reused_by_the_next_return`).
3. **`nItem` digitado igual ao de outra linha do mesmo documento** (inclusive de linha que não está nesta devolução) — teste na Task 4 (`Typed_item_number_already_used_is_refused`).
4. **XML com número em formato invariante** (`18.0000`, `1500.00`) lido em máquina pt-BR — teste na Task 1 (`Reads_icms51_deferral_and_ibs_cbs`, com `CultureInfo.CurrentCulture` = pt-BR no teste).
5. **Devolução da entrada própria continua igual** depois de a action ganhar `ItemNumbers` (a tela manda zeros) — teste na Task 4 (`Own_entry_return_ignores_item_numbers`) e o QUnit da Task 8 (`buildPurchaseItemNumbers` devolve zeros quando todas as linhas têm número).

---

### Task 1: Leitor do XML do fornecedor + `nItem` no rascunho da importação

**Files:**
- Create: `siagro-b1-backend/SiagroB1.Application/Services/PurchaseInvoices/SupplierNfeXmlReader.cs`
- Create: `siagro-b1-backend/SiagroB1.Application.Tests/Support/SupplierNfeXml.cs`
- Create: `siagro-b1-backend/SiagroB1.Application.Tests/PurchaseInvoices/SupplierNfeXmlReaderTests.cs`
- Create: `siagro-b1-backend/SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesImportXmlServiceTests.cs`
- Modify: `siagro-b1-backend/SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesImportXmlService.cs`
- Modify: `siagro-b1-backend/SiagroB1.Domain/Dtos/PurchaseInvoiceDraftDto.cs` (classe `PurchaseInvoiceDraftItemDto`)

**Interfaces:**
- Produces: `SupplierNfeXmlReader.Read(byte[] xmlData) : SupplierNfe`; `record SupplierNfe(XElement InfNfe, string AccessKey, IReadOnlyList<SupplierNfeItem> Items)`; `record SupplierNfeItem(...)` (campos abaixo); `PurchaseInvoiceDraftItemDto.NfeItemNumber : int?`; test support `SupplierNfeXml` (`AccessKey`, `Build`, `Det`, `Icms51`, `Icms00`, `Icms20`, `IcmsSn101`, `Icms10WithSt`, `Ipi`, `IbsCbs`, `Bytes`).

- [ ] **Step 1: Test support com o XML de fornecedor (layout real do `det`)**

```csharp
// SiagroB1.Application.Tests/Support/SupplierNfeXml.cs
using System.Globalization;
using System.Text;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// NF-e de fornecedor para os testes, no layout real (o da entrada nº 5 autorizada em homologação). Emitente =
/// o CPF do fornecedor <see cref="PurchaseNfeTestSeed.Supplier"/>, para a importação resolver o parceiro.
/// </summary>
public static class SupplierNfeXml
{
    public const string AccessKey = "35261000052998224725550010000004561123456780";

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
```

- [ ] **Step 2: Testes do leitor (falham: a classe não existe)**

```csharp
// SiagroB1.Application.Tests/PurchaseInvoices/SupplierNfeXmlReaderTests.cs
using System.Globalization;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Leitura do det da NF-e do fornecedor: nItem e tributação, no layout real (spec terceiro §6).</summary>
public class SupplierNfeXmlReaderTests
{
    [Fact]
    public void Reads_icms51_deferral_and_ibs_cbs()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
        try
        {
            var nfe = SupplierNfeXmlReader.Read(SupplierNfeXml.Bytes(SupplierNfeXml.Build(
                SupplierNfeXml.Det(1, "TRG", "TRIGO", 1000m, 1.5m, SupplierNfeXml.Icms51(1500m), SupplierNfeXml.IbsCbs()))));

            Assert.Equal(SupplierNfeXml.AccessKey, nfe.AccessKey);
            var item = Assert.Single(nfe.Items);
            Assert.Equal((1, "TRG", "5102", "10019900", "SP053521"), (item.ItemNumber, item.ProductCode, item.Cfop, item.Ncm, item.BenefitCode));
            Assert.Equal((1000m, 1.5m), (item.Quantity, item.UnitPrice));
            Assert.Equal(("51", (byte?)0, 1500m, 18m, 0m), (item.CstIcms, item.GoodsOrigin, item.IcmsBase, item.IcmsRate, item.IcmsValue));
            Assert.Equal((100m, 270m, 270m), (item.IcmsDeferral, item.IcmsOperationValue, item.IcmsDeferredValue));
            Assert.Equal(("49", "49"), (item.CstPis, item.CstCofins));
            Assert.Equal(("200", "200036", 1500m), (item.IbsCbsCst, item.IbsCbsClassCode, item.IbsCbsBase));
            Assert.Equal((0.1m, 0m, 60m, 0.6m, 0m), (item.IbsStateRate, item.IbsMunicipalRate, item.IbsRateReduction, item.IbsStateValue, item.IbsMunicipalValue));
            Assert.Equal((0.9m, 60m, 5.4m), (item.CbsRate, item.CbsRateReduction, item.CbsValue));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Reads_icms20_reduction_and_icms00_value()
    {
        var nfe = SupplierNfeXmlReader.Read(SupplierNfeXml.Bytes(SupplierNfeXml.Build(
            SupplierNfeXml.Det(1, "A", "A", 10m, 10m, SupplierNfeXml.Icms20(100m, 33.33m, 12m)),
            SupplierNfeXml.Det(2, "B", "B", 10m, 10m, SupplierNfeXml.Icms00(100m, 18m)))));

        Assert.Equal(("20", 33.33m, 12m), (nfe.Items[0].CstIcms, nfe.Items[0].IcmsBaseReduction, nfe.Items[0].IcmsRate));
        Assert.Equal(("00", 18m, 18m), (nfe.Items[1].CstIcms, nfe.Items[1].IcmsRate, nfe.Items[1].IcmsValue));
        Assert.Equal(2, nfe.Items[1].ItemNumber);
    }

    [Fact]
    public void Reads_csosn_in_the_icms_cst()
    {
        var nfe = SupplierNfeXmlReader.Read(SupplierNfeXml.Bytes(SupplierNfeXml.Build(
            SupplierNfeXml.Det(1, "A", "A", 1m, 1m, SupplierNfeXml.IcmsSn101()))));

        Assert.Equal("101", nfe.Items[0].CstIcms);
    }

    [Fact]
    public void Reads_ipi_and_st_values()
    {
        var nfe = SupplierNfeXmlReader.Read(SupplierNfeXml.Bytes(SupplierNfeXml.Build(
            SupplierNfeXml.Det(1, "A", "A", 1m, 1m, SupplierNfeXml.Icms10WithSt(), ipi: SupplierNfeXml.Ipi(50m)))));

        Assert.Equal((36m, 50m), (nfe.Items[0].IcmsStValue, nfe.Items[0].IpiValue));
    }

    [Fact]
    public void Reads_a_plain_nfe_without_the_protocol_envelope()
    {
        var nfe = SupplierNfeXmlReader.Read(SupplierNfeXml.Bytes(SupplierNfeXml.BuildPlain(
            SupplierNfeXml.Det(7, "A", "A", 1m, 1m, SupplierNfeXml.Icms51(1m)))));

        Assert.Equal(7, Assert.Single(nfe.Items).ItemNumber);
    }

    [Fact]
    public void Refuses_a_file_that_is_not_an_nfe()
    {
        var e = Assert.Throws<DefaultException>(() => SupplierNfeXmlReader.Read(SupplierNfeXml.Bytes("<a/>")));

        Assert.Equal("XML não parece uma NF-e: elemento infNFe não encontrado.", e.Message);
    }
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SupplierNfeXmlReaderTests"` → Expected: erro de compilação (`SupplierNfeXmlReader` não existe).

- [ ] **Step 4: Implementar o leitor**

```csharp
// SiagroB1.Application/Services/PurchaseInvoices/SupplierNfeXmlReader.cs
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>Item (det) da NF-e do fornecedor: identificação, quantidade e a tributação que o fornecedor destacou.</summary>
public sealed record SupplierNfeItem(
    int ItemNumber, string? ProductCode, string? ProductName, string? UnitOfMeasure, decimal Quantity, decimal UnitPrice,
    string? Cfop, string? Ncm, string? BenefitCode, byte? GoodsOrigin,
    string? CstIcms, decimal IcmsBase, decimal IcmsBaseReduction, decimal IcmsRate, decimal IcmsValue,
    decimal IcmsDeferral, decimal IcmsOperationValue, decimal IcmsDeferredValue, decimal IcmsStValue, decimal IpiValue,
    string? CstPis, decimal PisBase, decimal PisRate, decimal PisValue,
    string? CstCofins, decimal CofinsBase, decimal CofinsRate, decimal CofinsValue,
    string? IbsCbsCst, string? IbsCbsClassCode, decimal IbsCbsBase,
    decimal IbsStateRate, decimal IbsMunicipalRate, decimal IbsRateReduction, decimal IbsStateValue, decimal IbsMunicipalValue,
    decimal CbsRate, decimal CbsRateReduction, decimal CbsValue);

/// <summary>NF-e do fornecedor lida: o <c>infNFe</c> (para o cabeçalho), a chave e os itens.</summary>
public sealed record SupplierNfe(XElement InfNfe, string AccessKey, IReadOnlyList<SupplierNfeItem> Items);

/// <summary>
/// Lê a NF-e do fornecedor (<c>nfeProc</c> ou <c>NFe</c>) com <see cref="XDocument"/>, como a importação sempre leu —
/// a Zeus serve à emissão. Um leitor só para a importação, a gravação do documento de terceiro e o "Devolver"
/// (spec terceiro §6). Números no formato invariante do XML, qualquer que seja a cultura da máquina.
/// </summary>
public static class SupplierNfeXmlReader
{
    private static readonly XNamespace Nfe = "http://www.portalfiscal.inf.br/nfe";

    public static SupplierNfe Read(byte[] xmlData)
    {
        if (xmlData is null || xmlData.Length == 0)
            throw new DefaultException("Arquivo XML vazio.");

        XDocument document;

        try
        {
            document = XDocument.Parse(Encoding.UTF8.GetString(xmlData));
        }
        catch (Exception)
        {
            throw new DefaultException("Arquivo não é um XML válido.");
        }

        var infNfe = document.Descendants(Nfe + "infNFe").FirstOrDefault()
                     ?? throw new DefaultException("XML não parece uma NF-e: elemento infNFe não encontrado.");

        // A chave vem no atributo Id como "NFe" + 44 dígitos.
        var accessKey = (infNfe.Attribute("Id")?.Value ?? string.Empty)
            .Replace("NFe", string.Empty, StringComparison.OrdinalIgnoreCase);

        return new SupplierNfe(infNfe, accessKey, infNfe.Elements(Nfe + "det").Select(ReadItem).ToList());
    }

    private static SupplierNfeItem ReadItem(XElement det)
    {
        var prod = det.Element(Nfe + "prod");
        var imposto = det.Element(Nfe + "imposto");
        // ICMS, PIS e COFINS têm um filho só, cujo nome é o grupo (ICMS51, ICMSSN101, PISOutr...).
        var icms = imposto?.Element(Nfe + "ICMS")?.Elements().FirstOrDefault();
        var pis = imposto?.Element(Nfe + "PIS")?.Elements().FirstOrDefault();
        var cofins = imposto?.Element(Nfe + "COFINS")?.Elements().FirstOrDefault();
        var ibsCbs = imposto?.Element(Nfe + "IBSCBS");
        var gIbsCbs = ibsCbs?.Element(Nfe + "gIBSCBS");
        var gIbsUf = gIbsCbs?.Element(Nfe + "gIBSUF");
        var gIbsMun = gIbsCbs?.Element(Nfe + "gIBSMun");
        var gCbs = gIbsCbs?.Element(Nfe + "gCBS");
        var ipiValue = imposto?.Element(Nfe + "IPI")?.Descendants(Nfe + "vIPI").Select(e => Parse(e.Value)).FirstOrDefault() ?? 0m;

        return new SupplierNfeItem(
            ItemNumber: int.TryParse(det.Attribute("nItem")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0,
            ProductCode: Value(prod, "cProd"), ProductName: Value(prod, "xProd"), UnitOfMeasure: Value(prod, "uCom"),
            Quantity: Number(prod, "qCom"), UnitPrice: Number(prod, "vUnCom"),
            Cfop: Value(prod, "CFOP"), Ncm: Value(prod, "NCM"), BenefitCode: Value(prod, "cBenef"),
            GoodsOrigin: byte.TryParse(Value(icms, "orig"), NumberStyles.None, CultureInfo.InvariantCulture, out var origin) ? origin : null,
            CstIcms: Value(icms, "CST") ?? Value(icms, "CSOSN"),
            IcmsBase: Number(icms, "vBC"), IcmsBaseReduction: Number(icms, "pRedBC"), IcmsRate: Number(icms, "pICMS"),
            IcmsValue: Number(icms, "vICMS"), IcmsDeferral: Number(icms, "pDif"), IcmsOperationValue: Number(icms, "vICMSOp"),
            IcmsDeferredValue: Number(icms, "vICMSDif"), IcmsStValue: Number(icms, "vICMSST"), IpiValue: ipiValue,
            CstPis: Value(pis, "CST"), PisBase: Number(pis, "vBC"), PisRate: Number(pis, "pPIS"), PisValue: Number(pis, "vPIS"),
            CstCofins: Value(cofins, "CST"), CofinsBase: Number(cofins, "vBC"), CofinsRate: Number(cofins, "pCOFINS"),
            CofinsValue: Number(cofins, "vCOFINS"),
            IbsCbsCst: Value(ibsCbs, "CST"), IbsCbsClassCode: Value(ibsCbs, "cClassTrib"), IbsCbsBase: Number(gIbsCbs, "vBC"),
            IbsStateRate: Number(gIbsUf, "pIBSUF"), IbsMunicipalRate: Number(gIbsMun, "pIBSMun"),
            IbsRateReduction: Number(gIbsUf?.Element(Nfe + "gRed"), "pRedAliq"),
            IbsStateValue: Number(gIbsUf, "vIBSUF"), IbsMunicipalValue: Number(gIbsMun, "vIBSMun"),
            CbsRate: Number(gCbs, "pCBS"), CbsRateReduction: Number(gCbs?.Element(Nfe + "gRed"), "pRedAliq"),
            CbsValue: Number(gCbs, "vCBS"));
    }

    private static string? Value(XElement? parent, string name) => parent?.Element(Nfe + name)?.Value;

    private static decimal Number(XElement? parent, string name) => Parse(Value(parent, name));

    private static decimal Parse(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : 0m;
}
```

- [ ] **Step 5: Rodar os testes do leitor** → Expected: 6 PASS.

- [ ] **Step 6: Teste da importação com `nItem` (falha: o DTO não tem a propriedade)**

```csharp
// SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesImportXmlServiceTests.cs
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Importação do XML do fornecedor: o rascunho leva o nItem de cada linha (spec terceiro §6).</summary>
public class PurchaseInvoicesImportXmlServiceTests
{
    [Fact]
    public async Task Draft_lines_carry_the_supplier_item_number_in_xml_order()
    {
        var partners = new FakeBusinessPartnerService(
            names: new Dictionary<string, string> { [PurchaseNfeTestSeed.Supplier] = "PRODUTOR RURAL TESTE" },
            taxIds: new Dictionary<string, string> { [PurchaseNfeTestSeed.Supplier] = "529.982.247-25" });
        var xml = SupplierNfeXml.Build(
            SupplierNfeXml.Det(1, "TRG", "TRIGO", 1000m, 1.5m, SupplierNfeXml.Icms51(1500m)),
            SupplierNfeXml.Det(2, "MLH", "MILHO", 500m, 1m, SupplierNfeXml.Icms51(500m)));

        var draft = await new PurchaseInvoicesImportXmlService(partners).ExecuteAsync(SupplierNfeXml.Bytes(xml), "nota.xml");

        Assert.Equal(PurchaseNfeTestSeed.Supplier, draft.CardCode);
        Assert.Equal(SupplierNfeXml.AccessKey, draft.ChaveNFe);
        Assert.Equal(new[] { (1, "TRG", 1000m), (2, "MLH", 500m) },
            draft.Items.Select(i => (i.NfeItemNumber!.Value, i.ItemCode!, i.Quantity)).ToArray());
    }
}
```

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoicesImportXmlServiceTests"` → Expected: erro de compilação (`NfeItemNumber`).

- [ ] **Step 7: DTO e importação pelo leitor**

Em `SiagroB1.Domain/Dtos/PurchaseInvoiceDraftDto.cs`, na classe `PurchaseInvoiceDraftItemDto`, depois de `UnitPrice`:

```csharp
    /// <summary>Número do item (nItem) na NF-e do fornecedor — é o que a devolução referencia.</summary>
    public int? NfeItemNumber { get; set; }
```

Em `PurchaseInvoicesImportXmlService.ExecuteAsync`, troque do `if (xmlData is null ...)` até o laço `foreach (var det in ...)` inclusive pelo leitor (o resto — `ResolveCardCode`, `Value`, `ParseDecimal`, `ParseDate` — continua). O corpo fica:

```csharp
    public Task<PurchaseInvoiceDraftDto> ExecuteAsync(byte[] xmlData, string fileName)
    {
        var nfe = SupplierNfeXmlReader.Read(xmlData);
        var infNfe = nfe.InfNfe;
        var ide = infNfe.Element(Nfe + "ide");
        var emit = infNfe.Element(Nfe + "emit");
        var cnpj = Value(emit, "CNPJ") ?? Value(emit, "CPF");

        var draft = new PurchaseInvoiceDraftDto
        {
            ChaveNFe = nfe.AccessKey,
            TaxDocumentNumber = Value(ide, "nNF"),
            TaxDocumentSeries = Value(ide, "serie"),
            IssueDate = ParseDate(Value(ide, "dhEmi") ?? Value(ide, "dEmi")),
            TotalDocumentValue = ParseDecimal(infNfe.Descendants(Nfe + "ICMSTot").FirstOrDefault(), "vNF"),
            TaxPayerComments = Value(infNfe.Element(Nfe + "infAdic"), "infCpl"),
            CardName = Value(emit, "xNome"),
            CardCode = ResolveCardCode(cnpj),
            XmlFileName = fileName,
        };

        foreach (var item in nfe.Items)
        {
            draft.Items.Add(new PurchaseInvoiceDraftItemDto
            {
                ItemCode = item.ProductCode,
                ItemName = item.ProductName,
                UnitOfMeasureCode = item.UnitOfMeasure,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                NfeItemNumber = item.ItemNumber,
            });
        }

        if (draft.Items.Count == 0)
            throw new DefaultException("XML sem itens (det).");

        return Task.FromResult(draft);
    }
```

Atualize o `<summary>` da classe: troque a última frase ("A leitura de `ide/NFref` ... entra na Fase 2.") por "A tributação de cada item é lida por `SupplierNfeXmlReader` e gravada na linha pelo `PurchaseInvoiceSupplierTaxes` quando o documento é salvo; o rascunho só leva o `nItem`." Remova os `using` que ficarem sem uso.

- [ ] **Step 8: Rodar os dois testes novos** → Expected: PASS. Depois `dotnet build SiagroB1.sln` (0 erros).

- [ ] **Step 9: Commit**

```bash
git add SiagroB1.Application/Services/PurchaseInvoices/SupplierNfeXmlReader.cs SiagroB1.Application.Tests/Support/SupplierNfeXml.cs SiagroB1.Application.Tests/PurchaseInvoices/SupplierNfeXmlReaderTests.cs SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesImportXmlServiceTests.cs
git commit -F msg.txt -- SiagroB1.Application/Services/PurchaseInvoices/SupplierNfeXmlReader.cs SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesImportXmlService.cs SiagroB1.Domain/Dtos/PurchaseInvoiceDraftDto.cs SiagroB1.Application.Tests/Support/SupplierNfeXml.cs SiagroB1.Application.Tests/PurchaseInvoices/SupplierNfeXmlReaderTests.cs SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesImportXmlServiceTests.cs
```
Mensagem: `feat(invoice): leitor do XML do fornecedor e nItem no rascunho da importação`.

---

### Task 2: Tributação do fornecedor na linha do documento de terceiro

**Files:**
- Create: `siagro-b1-backend/SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceSupplierTaxes.cs`
- Create: `siagro-b1-backend/SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceSupplierTaxesTests.cs`
- Modify: `siagro-b1-backend/SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCreateService.cs` (antes de `await taxApply.ApplyAsync(invoice, invoice.Items);`)
- Modify: `siagro-b1-backend/SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesUpdateService.cs` (depois de `await SyncItemsAsync(existing, entity);`)
- Modify: `siagro-b1-backend/SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesItemsCreateService.cs` (depois de carregar `invoice`)

**Interfaces:**
- Consumes: `SupplierNfeXmlReader.Read`, `SupplierNfeItem` (Task 1); `SupplierNfeXml` (Task 1).
- Produces: `PurchaseInvoiceSupplierTaxes.Apply(PurchaseInvoice invoice, IEnumerable<PurchaseInvoiceItem> items)`; `PurchaseInvoiceSupplierTaxes.Clear(PurchaseInvoiceItem item)`.

- [ ] **Step 1: Testes (falham: a classe não existe)**

```csharp
// SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceSupplierTaxesTests.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Documento de terceiro com o XML guardado: cada linha com nItem recebe a tributação do det do fornecedor, lida no
/// servidor (spec terceiro D4/D5, §7). Linha sem nItem fica sem fotografia; imposto vindo da tela nunca vale.
/// </summary>
public class PurchaseInvoiceSupplierTaxesTests
{
    private static string Xml() => SupplierNfeXml.Build(
        SupplierNfeXml.Det(1, "TRG", "TRIGO", 1000m, 1.5m, SupplierNfeXml.Icms51(1500m), SupplierNfeXml.IbsCbs()),
        SupplierNfeXml.Det(2, "MLH", "MILHO", 500m, 1m, SupplierNfeXml.Icms00(500m, 12m)));

    private static PurchaseInvoice ThirdParty(string? xml, params PurchaseInvoiceItem[] lines)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), CardCode = PurchaseNfeTestSeed.Supplier, IssuerType = DocumentIssuerType.ThirdParty,
            InvoiceType = PurchaseInvoiceType.Normal, XmlData = xml is null ? null : SupplierNfeXml.Bytes(xml),
            ChaveNFe = SupplierNfeXml.AccessKey, TaxDocumentNumber = "456", TaxDocumentSeries = "1",
        };
        foreach (var line in lines)
            invoice.AddItem(line);
        return invoice;
    }

    private static PurchaseInvoiceItem Line(string code, int? nItem) =>
        new() { Key = Guid.NewGuid(), ItemCode = code, UnitOfMeasureCode = "KG", Quantity = 10m, UnitPrice = 1m, NfeItemNumber = nItem };

    [Fact]
    public void Line_with_an_item_number_gets_the_supplier_taxes()
    {
        var invoice = ThirdParty(Xml(), Line("TRIGO", 1), Line("MILHO", 2));

        PurchaseInvoiceSupplierTaxes.Apply(invoice, invoice.Items);

        var trigo = invoice.Items.Single(i => i.NfeItemNumber == 1);
        Assert.Equal(("5102", "10019900", (byte?)0, "SP053521"), (trigo.Cfop, trigo.Ncm, trigo.GoodsOrigin, trigo.IcmsBenefitCode));
        Assert.Equal(("51", 1500m, 18m, 100m, 270m, 270m), (trigo.CstIcms, trigo.IcmsBase, trigo.IcmsRate, trigo.IcmsDeferral, trigo.IcmsOperationValue, trigo.IcmsDeferredValue));
        Assert.Equal(("200", "200036", 0.9m, 60m, 0.1m, 60m), (trigo.IbsCbsCst, trigo.IbsCbsClassCode, trigo.CbsRate, trigo.CbsRateReduction, trigo.IbsStateRate, trigo.IbsRateReduction));
        var milho = invoice.Items.Single(i => i.NfeItemNumber == 2);
        Assert.Equal(("00", 12m, 60m), (milho.CstIcms, milho.IcmsRate, milho.IcmsValue));
    }

    [Fact]
    public void Line_without_an_item_number_keeps_no_taxes_even_if_the_body_sent_them()
    {
        var line = Line("TRIGO", null);
        line.CstIcms = "00";
        line.IcmsRate = 18m;
        var invoice = ThirdParty(Xml(), line);

        PurchaseInvoiceSupplierTaxes.Apply(invoice, invoice.Items);

        Assert.Equal(((string?)null, 0m, (int?)null), (line.CstIcms, line.IcmsRate, line.NfeItemNumber));
    }

    [Fact]
    public void Item_number_missing_from_the_xml_is_refused()
    {
        var invoice = ThirdParty(Xml(), Line("TRIGO", 9));

        var e = Assert.Throws<DefaultException>(() => PurchaseInvoiceSupplierTaxes.Apply(invoice, invoice.Items));

        Assert.Equal("Item TRIGO: o item 9 não existe na NF-e do fornecedor.", e.Message);
    }

    [Fact]
    public void Document_without_xml_keeps_its_item_numbers_and_no_taxes()
    {
        var line = Line("TRIGO", 3);
        line.CstIcms = "51";
        var invoice = ThirdParty(null, line);

        PurchaseInvoiceSupplierTaxes.Apply(invoice, invoice.Items);

        Assert.Equal((3, (string?)null), (line.NfeItemNumber!.Value, line.CstIcms));
    }

    [Theory]
    [InlineData(DocumentIssuerType.Own, PurchaseInvoiceType.Normal)]
    [InlineData(DocumentIssuerType.ThirdParty, PurchaseInvoiceType.Return)]
    public void Own_entry_and_customer_return_are_not_touched(DocumentIssuerType issuer, PurchaseInvoiceType type)
    {
        var line = Line("TRIGO", 1);
        line.CstIcms = "51";
        line.IcmsRate = 18m;
        var invoice = ThirdParty(Xml(), line);
        invoice.IssuerType = issuer;
        invoice.InvoiceType = type;

        PurchaseInvoiceSupplierTaxes.Apply(invoice, invoice.Items);

        Assert.Equal(("51", 18m, 0m), (line.CstIcms, line.IcmsRate, line.IcmsBase));
    }

    [Fact]
    public async Task Create_fills_the_supplier_taxes_of_a_third_party_document()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = ThirdParty(Xml(), Line("TRIGO", 1));

        await Create(db).ExecuteAsync(invoice, "tester");

        var saved = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync();
        Assert.Equal(("51", 1, 18m), (saved.CstIcms, saved.NfeItemNumber!.Value, saved.IcmsRate));
    }

    [Fact]
    public async Task Update_keeps_the_supplier_item_after_the_product_changes()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = ThirdParty(Xml(), Line("TRG", 1));
        await Create(db).ExecuteAsync(invoice, "tester");
        db.Context.ChangeTracker.Clear();
        var changed = await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync();
        changed.Items.Single().ItemCode = "TRIGO";

        await new PurchaseInvoicesUpdateService(db, new FakeBusinessPartnerService(), new FakeItemService(),
            TaxTestServices.InactivePurchaseApply(db)).ExecuteAsync(changed.Key, changed, "tester");

        var saved = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync();
        Assert.Equal(("TRIGO", 1, "51"), (saved.ItemCode, saved.NfeItemNumber!.Value, saved.CstIcms));
    }

    [Fact]
    public async Task Line_added_after_the_import_has_no_item_number()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = ThirdParty(Xml(), Line("TRIGO", 1));
        await Create(db).ExecuteAsync(invoice, "tester");
        var added = Line("MILHO", 2);
        added.PurchaseInvoiceKey = invoice.Key;
        added.CstIcms = "00";

        await new PurchaseInvoicesItemsCreateService(db, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db))
            .ExecuteAsync(added, "tester");

        var saved = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.ItemCode == "MILHO");
        Assert.Equal(((int?)null, (string?)null), (saved.NfeItemNumber, saved.CstIcms));
    }

    private static PurchaseInvoicesCreateService Create(UnitOfWork db) =>
        new(db, new FakeBusinessPartnerService(), new FakeItemService(), TaxTestServices.InactivePurchaseApply(db));
}
```

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoiceSupplierTaxesTests"` → Expected: erro de compilação.

- [ ] **Step 2: Implementar**

```csharp
// SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceSupplierTaxes.cs
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Tributação da NF-e do fornecedor na linha do Documento de Entrada de terceiro (spec terceiro D4/D5, §7). O servidor
/// lê o XML guardado (<see cref="PurchaseInvoice.XmlData"/>) e copia o det do mesmo nItem para a fotografia fiscal da
/// linha — é a régua da conferência da devolução. Nada disso é cálculo: é o registro da nota do fornecedor, e imposto
/// vindo da tela nunca vale. Entrada própria e devolução do cliente não passam por aqui.
/// </summary>
public static class PurchaseInvoiceSupplierTaxes
{
    public static void Apply(PurchaseInvoice invoice, IEnumerable<PurchaseInvoiceItem> items)
    {
        if (invoice.IssuerType != DocumentIssuerType.ThirdParty || invoice.InvoiceType != PurchaseInvoiceType.Normal)
            return;

        var nfe = invoice.XmlData is { Length: > 0 } ? SupplierNfeXmlReader.Read(invoice.XmlData) : null;

        foreach (var item in items)
        {
            if (nfe is null || item.NfeItemNumber is not { } number)
            {
                // Sem XML (documento digitado) ou linha sem nItem (incluída depois da importação): sem fotografia.
                // O nItem digitado no "Devolver" fica (D6).
                Clear(item);
                continue;
            }

            var det = nfe.Items.FirstOrDefault(d => d.ItemNumber == number)
                      ?? throw new DefaultException($"Item {item.ItemCode}: o item {number} não existe na NF-e do fornecedor.");

            Copy(det, item);
        }
    }

    public static void Clear(PurchaseInvoiceItem item)
    {
        item.Cfop = null;
        item.Ncm = null;
        item.GoodsOrigin = null;
        item.IcmsBenefitCode = null;
        item.CstIcms = null;
        item.IcmsBase = 0m;
        item.IcmsBaseReduction = 0m;
        item.IcmsRate = 0m;
        item.IcmsValue = 0m;
        item.IcmsDeferral = 0m;
        item.IcmsOperationValue = 0m;
        item.IcmsDeferredValue = 0m;
        item.CstPis = null;
        item.PisBase = 0m;
        item.PisRate = 0m;
        item.PisValue = 0m;
        item.CstCofins = null;
        item.CofinsBase = 0m;
        item.CofinsRate = 0m;
        item.CofinsValue = 0m;
        item.IbsCbsCst = null;
        item.IbsCbsClassCode = null;
        item.IbsCbsBase = 0m;
        item.IbsStateRate = 0m;
        item.IbsMunicipalRate = 0m;
        item.IbsRateReduction = 0m;
        item.IbsStateValue = 0m;
        item.IbsMunicipalValue = 0m;
        item.CbsRate = 0m;
        item.CbsRateReduction = 0m;
        item.CbsValue = 0m;
    }

    private static void Copy(SupplierNfeItem det, PurchaseInvoiceItem item)
    {
        item.Cfop = det.Cfop;
        item.Ncm = det.Ncm;
        item.GoodsOrigin = det.GoodsOrigin;
        item.IcmsBenefitCode = det.BenefitCode;
        item.CstIcms = det.CstIcms;
        item.IcmsBase = det.IcmsBase;
        item.IcmsBaseReduction = det.IcmsBaseReduction;
        item.IcmsRate = det.IcmsRate;
        item.IcmsValue = det.IcmsValue;
        item.IcmsDeferral = det.IcmsDeferral;
        item.IcmsOperationValue = det.IcmsOperationValue;
        item.IcmsDeferredValue = det.IcmsDeferredValue;
        item.CstPis = det.CstPis;
        item.PisBase = det.PisBase;
        item.PisRate = det.PisRate;
        item.PisValue = det.PisValue;
        item.CstCofins = det.CstCofins;
        item.CofinsBase = det.CofinsBase;
        item.CofinsRate = det.CofinsRate;
        item.CofinsValue = det.CofinsValue;
        item.IbsCbsCst = det.IbsCbsCst;
        item.IbsCbsClassCode = det.IbsCbsClassCode;
        item.IbsCbsBase = det.IbsCbsBase;
        item.IbsStateRate = det.IbsStateRate;
        item.IbsMunicipalRate = det.IbsMunicipalRate;
        item.IbsRateReduction = det.IbsRateReduction;
        item.IbsStateValue = det.IbsStateValue;
        item.IbsMunicipalValue = det.IbsMunicipalValue;
        item.CbsRate = det.CbsRate;
        item.CbsRateReduction = det.CbsRateReduction;
        item.CbsValue = det.CbsValue;
    }
}
```

Ligações:
- `PurchaseInvoicesCreateService.ExecuteAsync`, logo antes de `// Tributos pela natureza, ANTES de gravar...`:
  ```csharp
          // Documento de terceiro com o XML: a tributação de cada linha é a da nota do fornecedor (spec terceiro §7).
          PurchaseInvoiceSupplierTaxes.Apply(invoice, invoice.Items);
  ```
- `PurchaseInvoicesUpdateService.ExecuteAsync`, logo depois de `await SyncItemsAsync(existing, entity);`:
  ```csharp
          PurchaseInvoiceSupplierTaxes.Apply(existing, existing.Items);
  ```
- `PurchaseInvoicesItemsCreateService.ExecuteAsync`, logo depois de `PurchaseInvoiceNfeLock.EnsureLineCanBeAdded(invoice);`:
  ```csharp
          // Linha incluída depois da importação não é item da nota do fornecedor: sem nItem e sem fotografia.
          if (invoice.IssuerType == DocumentIssuerType.ThirdParty)
          {
              item.NfeItemNumber = null;
              PurchaseInvoiceSupplierTaxes.Clear(item);
          }
  ```
  (adicione `using SiagroB1.Domain.Enums;` se faltar).

- [ ] **Step 3: Rodar** os testes da classe → Expected: PASS (9). Depois a suíte `PurchaseInvoices`: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoices"` → verde.

- [ ] **Step 4: Commit** (`git add` dos 2 arquivos novos; pathspec com os 5 arquivos). Mensagem: `feat(invoice): tributação da NF-e do fornecedor na linha do documento de terceiro`.

---

### Task 3: Natureza de devolução de compra de terceiro na filial (+ migration)

**Files:**
- Modify: `siagro-b1-backend/SiagroB1.Domain/Entities/Branch.cs` (depois de `IssuesNfe`)
- Modify: `siagro-b1-backend/SiagroB1.Application/Services/BranchService.cs` (`ValidateNfeIssuance` + chamada no Create e no Update)
- Create: `siagro-b1-backend/SiagroB1.Application.Tests/Branches/BranchThirdPartyReturnUsageTests.cs`
- Create (gerado): `siagro-b1-backend/SiagroB1.Migrations/AppContext/<timestamp>_AddThirdPartyPurchaseReturn.cs` + `.Designer.cs`; Modify: `AppDbContextModelSnapshot.cs`

**Interfaces:**
- Produces: `Branch.ThirdPartyPurchaseReturnUsageCode : int?`.

- [ ] **Step 1: Teste (falha: propriedade inexistente)** — mesmo padrão de `BranchServiceIssuesNfeTests`.

```csharp
// SiagroB1.Application.Tests/Branches/BranchThirdPartyReturnUsageTests.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Branches;

/// <summary>Natureza de devolução de compra de terceiro da filial (spec terceiro D3): de Saída e ativa.</summary>
public class BranchThirdPartyReturnUsageTests
{
    private static BranchService Service(UnitOfWork db) => new(db.Context, TaxTestServices.Config("STANDALONE"));

    private static async Task<int> UsageAsync(UnitOfWork db, UsageDirection direction, bool inactive = false)
    {
        var usage = new Usage { Name = "DEVOLUCAO DE COMPRA", Direction = direction, Inactive = inactive };
        db.Context.Usages.Add(usage);
        await db.SaveChangesAsync();
        return usage.Code;
    }

    private static Branch NewBranch(int? usageCode) => new()
    {
        Code = "01", BranchName = "MATRIZ", ShortName = "MTZ", TaxId = "68583898000101",
        IssuesNfe = true, TaxRegime = TaxRegime.Normal, StateCode = "SP", ThirdPartyPurchaseReturnUsageCode = usageCode,
    };

    [Theory]
    [InlineData(UsageDirection.Incoming, false)]
    [InlineData(UsageDirection.Outgoing, true)]
    public async Task Incoming_or_inactive_usage_is_refused(UsageDirection direction, bool inactive)
    {
        var db = TestDb.CreateUnitOfWork();
        var code = await UsageAsync(db, direction, inactive);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(db).CreateAsync(NewBranch(code)));

        Assert.Equal("A natureza de devolução de compra de terceiro precisa ser de Saída e ativa.", e.Message);
    }

    [Fact]
    public async Task Outgoing_active_usage_is_saved()
    {
        var db = TestDb.CreateUnitOfWork();
        var code = await UsageAsync(db, UsageDirection.Outgoing);

        await Service(db).CreateAsync(NewBranch(code));

        Assert.Equal(code, (await db.Context.Branchs.AsNoTracking().SingleAsync()).ThirdPartyPurchaseReturnUsageCode);
    }

    [Fact]
    public async Task Empty_usage_is_accepted()
    {
        var db = TestDb.CreateUnitOfWork();

        await Service(db).CreateAsync(NewBranch(null));

        Assert.Null((await db.Context.Branchs.AsNoTracking().SingleAsync()).ThirdPartyPurchaseReturnUsageCode);
    }

    [Fact]
    public async Task Update_validates_the_usage_too()
    {
        var db = TestDb.CreateUnitOfWork();
        await Service(db).CreateAsync(NewBranch(null));
        db.Context.ChangeTracker.Clear();
        var code = await UsageAsync(db, UsageDirection.Incoming);
        db.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<DefaultException>(() => Service(db).UpdateAsync("01", NewBranch(code)));
    }
}
```

(Se `Usage` exigir outras propriedades obrigatórias, preencha-as como em `PurchaseNfeTestSeed`.)

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~BranchThirdPartyReturnUsageTests"` → Expected: erro de compilação.

- [ ] **Step 2: Implementar**

`Branch.cs`, depois de `IssuesNfe`:

```csharp
        /// <summary>
        /// Natureza de devolução de compra de terceiro (spec terceiro D3): aplicada sozinha a todas as linhas da devolução
        /// de uma entrada de terceiro. De Saída e ativa. Sem FK, como as demais referências a natureza.
        /// </summary>
        public int? ThirdPartyPurchaseReturnUsageCode { get; set; }
```

`BranchService`: novo método chamado logo depois de `ValidateNfeIssuance(entity);` no Create e no Update:

```csharp
    private async Task ValidateThirdPartyReturnUsageAsync(Branch entity)
    {
        if (entity.ThirdPartyPurchaseReturnUsageCode is not { } code)
            return;

        var usage = await context.Usages.AsNoTracking().FirstOrDefaultAsync(u => u.Code == code);

        if (usage is null || usage.Inactive || usage.Direction != UsageDirection.Outgoing)
            throw new DefaultException("A natureza de devolução de compra de terceiro precisa ser de Saída e ativa.");
    }
```

(O Create chama `ValidateNfeIssuance` na linha ~23 e o Update na ~61: o novo método entra logo depois das duas, com `await`. `using SiagroB1.Domain.Enums;` se faltar.)

- [ ] **Step 3: Rodar** → Expected: PASS (4).

- [ ] **Step 4: Migration** (de `siagro-b1-backend/`, sem Web/Gateway rodando):

```bash
dotnet build SiagroB1.sln
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add AddThirdPartyPurchaseReturn --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --no-build
```

Leia a migration: o `Up` só pode ter `AddColumn<int>("ThirdPartyPurchaseReturnUsageCode", "BRANCHS", nullable: true)`; o `Down` só o `DropColumn`. Qualquer outra operação = pare e reporte. `git add` dos dois arquivos gerados. **Não** aplique no banco nesta tarefa (a Task 9 aplica).

- [ ] **Step 5: Commit** (pathspec: `Branch.cs`, `BranchService.cs`, o teste, os 2 arquivos da migration, `AppDbContextModelSnapshot.cs`). Mensagem `feat(master-data): natureza de devolução de compra de terceiro na filial`, com o trailer `DB: AddThirdPartyPurchaseReturn`.

---

### Task 4: "Devolver" aceita a entrada de terceiro (backend)

**Files:**
- Modify: `siagro-b1-backend/SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeReturnCreateService.cs`
- Modify: `siagro-b1-backend/SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesTaxApplyService.cs` (conferência)
- Modify: `siagro-b1-backend/SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeReturnableItemsService.cs`
- Modify: `siagro-b1-backend/SiagroB1.Domain/Dtos/PurchaseInvoiceNfeReturnableItemDto.cs`
- Modify: `siagro-b1-backend/SiagroB1.Web/Actions/Nfe/PurchaseInvoicesCreateNfeReturnController.cs`
- Modify: `siagro-b1-backend/SiagroB1.Web/Actions/Nfe/NfeReturnActionParameters.cs`
- Modify: `siagro-b1-backend/SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (bloco `purchaseInvoicesCreateNfeReturn`, ~linha 130)
- Create: `siagro-b1-backend/SiagroB1.Application.Tests/Support/ThirdPartyPurchaseSeed.cs`
- Create: `siagro-b1-backend/SiagroB1.Application.Tests/Nfe/PurchaseInvoicesThirdPartyReturnTests.cs`

**Interfaces:**
- Consumes: `Branch.ThirdPartyPurchaseReturnUsageCode` (Task 3); `PurchaseInvoiceSupplierTaxes.Apply` (Task 2); `SupplierNfeXmlReader`, `SupplierNfeXml` (Task 1); `PurchaseNfeTestSeed.SeedAsync` (existente: filial "01" SP com NF-e, fornecedor `F-SP` de SP, itens `TRIGO`/`MILHO`, natureza de devolução `scenario.ReturnUsage` de Saída: CST 51 18% dif. 100 cBenef SP053521 dentro do estado, CFOP 5202/6202).
- Produces: `record PurchaseInvoiceNfeReturnItem(Guid OriginItemKey, decimal Quantity, int? ItemNumber = null)`; DTO `PurchaseInvoiceNfeReturnableItemDto.ItemNumber : int?`; parâmetro de action `ItemNumbers` (`Collection(Edm.Int32)`, 0 = não informado, sempre enviado); `ThirdPartyPurchaseSeed.SeedAsync(bool withXml = true, bool configureUsage = true, string icms = null)` → `(PurchaseNfeScenario Scenario, PurchaseInvoice Origin)`.

- [ ] **Step 1: Seed da entrada de terceiro**

```csharp
// SiagroB1.Application.Tests/Support/ThirdPartyPurchaseSeed.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Entrada de TERCEIRO confirmada do fornecedor F-SP (SP → filial SP), com a chave e, por padrão, o XML guardado:
/// TRIGO no item 1 e MILHO no item 2, ICMS 51 18% diferido 100% com cBenef SP053521 — a mesma tributação que a
/// natureza de devolução do <see cref="PurchaseNfeTestSeed"/> produz dentro do estado (a conferência passa).
/// </summary>
public static class ThirdPartyPurchaseSeed
{
    public static async Task<(PurchaseNfeScenario Scenario, PurchaseInvoice Origin)> SeedAsync(
        bool withXml = true, bool configureUsage = true, string? icms = null)
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var context = scenario.Db.Context;

        if (configureUsage)
        {
            var branch = await context.Branchs.SingleAsync(b => b.Code == "01");
            branch.ThirdPartyPurchaseReturnUsageCode = scenario.ReturnUsage;
        }

        var xml = SupplierNfeXml.Build(
            SupplierNfeXml.Det(1, "TRIGO", "TRIGO EM GRAOS", 1000m, 1.5m, icms ?? SupplierNfeXml.Icms51(1500m)),
            SupplierNfeXml.Det(2, "MILHO", "MILHO EM GRAOS", 500m, 1m, SupplierNfeXml.Icms51(500m)));

        var origin = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = PurchaseNfeTestSeed.Supplier, CardName = "PRODUTOR RURAL TESTE",
            IssuerType = DocumentIssuerType.ThirdParty, InvoiceType = PurchaseInvoiceType.Normal, InvoiceStatus = InvoiceStatus.Confirmed,
            IssueDate = new DateTime(2026, 10, 1), PostingDate = new DateTime(2026, 10, 2), ChaveNFe = SupplierNfeXml.AccessKey,
            TaxDocumentNumber = "456", TaxDocumentSeries = "1", GrossWeight = 1500m, NetWeight = 1500m, FreightTerms = FreightTerms.None,
            XmlData = withXml ? SupplierNfeXml.Bytes(xml) : null, XmlFileName = withXml ? "nota.xml" : null,
        };
        origin.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", ItemName = "TRIGO EM GRAOS", UnitOfMeasureCode = "KG", Quantity = 1000m,
            UnitPrice = 1.5m, NfeItemNumber = withXml ? 1 : null,
        });
        origin.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "MILHO", ItemName = "MILHO EM GRAOS", UnitOfMeasureCode = "KG", Quantity = 500m,
            UnitPrice = 1m, NfeItemNumber = withXml ? 2 : null,
        });
        PurchaseInvoiceSupplierTaxes.Apply(origin, origin.Items);

        context.PurchaseInvoices.Add(origin);
        await scenario.Db.SaveChangesAsync();
        context.ChangeTracker.Clear();

        return (scenario, await context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == origin.Key));
    }
}
```

- [ ] **Step 2: Testes (falham)**

```csharp
// SiagroB1.Application.Tests/Nfe/PurchaseInvoicesThirdPartyReturnTests.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>"Devolver" da entrada de terceiro (spec terceiro §8): guardas, nItem digitado, natureza da filial, conferência.</summary>
public class PurchaseInvoicesThirdPartyReturnTests
{
    private static FakeBusinessPartnerService Partners() => new(
        names: new Dictionary<string, string> { [PurchaseNfeTestSeed.Supplier] = "PRODUTOR RURAL TESTE" },
        states: new Dictionary<string, string> { [PurchaseNfeTestSeed.Supplier] = "SP" });

    internal static PurchaseInvoicesNfeReturnCreateService Returns(PurchaseNfeScenario scenario) =>
        new(scenario.Db, new TaxCalculationGate(scenario.Db, NfeTestSeed.Config()),
            new PurchaseInvoicesCreateService(scenario.Db, Partners(), new FakeItemService(), TaxTestServices.PurchaseApply(scenario.Db, Partners())),
            NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone);

    private static PurchaseInvoiceNfeReturnRequest Request(PurchaseInvoice origin, params (string Code, decimal Quantity, int? ItemNumber)[] lines) =>
        new(origin.Key, lines.Select(l => new PurchaseInvoiceNfeReturnItem(
            origin.Items.Single(i => i.ItemCode == l.Code).Key!.Value, l.Quantity, l.ItemNumber)).ToList(), "grão fora do padrão");

    [Fact]
    public async Task Imported_entry_returns_with_the_branch_usage_and_the_supplier_item_numbers()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync();

        var created = await Returns(scenario).ExecuteAsync(Request(origin, ("MILHO", 200m, null)), "tester");

        var saved = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key);
        Assert.Equal((PurchaseInvoiceType.Return, DocumentIssuerType.Own, true, origin.Key),
            (saved.InvoiceType, saved.IssuerType, saved.IsNfeReturn, saved.PurchaseInvoiceOriginKey!.Value));
        var line = saved.Items.Single();
        Assert.Equal((200m, scenario.ReturnUsage, "5202", "51"), (line.Quantity, line.UsageCode!.Value, line.Cfop, line.CstIcms));
        Assert.StartsWith("Devolução da NF-e 456 série 1. Motivo: grão fora do padrão", saved.Comments);
    }

    [Fact]
    public async Task Branch_without_the_usage_is_refused()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync(configureUsage: false);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 1m, null)), "tester"));

        Assert.Equal("Configure a natureza de devolução de compra de terceiro na filial 01.", e.Message);
    }

    [Fact]
    public async Task Pending_or_keyless_third_party_entry_is_refused()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync();
        var stored = await scenario.Db.Context.PurchaseInvoices.SingleAsync(i => i.Key == origin.Key);
        stored.ChaveNFe = null;
        await scenario.Db.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 1m, null)), "tester"));

        Assert.Equal("A devolução de entrada de terceiro parte de um documento Normal, confirmado e com a chave da NF-e do fornecedor (44 dígitos).", e.Message);
    }

    [Fact]
    public async Task Typed_entry_requires_the_item_number()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync(withXml: false);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 1m, null)), "tester"));

        Assert.Equal("Item TRIGO: informe o número do item na NF-e do fornecedor.", e.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(991)]
    public async Task Item_number_out_of_range_is_refused(int typed)
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync(withXml: false);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 1m, typed)), "tester"));

        Assert.Equal("Item TRIGO: informe o número do item na NF-e do fornecedor.", e.Message);
    }

    [Fact]
    public async Task Typed_item_number_already_used_is_refused()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync(withXml: false);
        var milho = await scenario.Db.Context.PurchaseInvoicesItems.SingleAsync(i => i.ItemCode == "MILHO" && i.PurchaseInvoiceKey == origin.Key);
        milho.NfeItemNumber = 2;
        await scenario.Db.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();
        origin = await scenario.Db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == origin.Key);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 1m, 2)), "tester"));

        Assert.Equal("Item TRIGO: o número 2 já é de outro item desta NF-e do fornecedor.", e.Message);
    }

    [Fact]
    public async Task Typed_item_number_is_saved_and_reused_by_the_next_return()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync(withXml: false);

        await Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 100m, 3)), "tester");
        scenario.Db.Context.ChangeTracker.Clear();
        var reloaded = await scenario.Db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == origin.Key);
        Assert.Equal(3, reloaded.Items.Single(i => i.ItemCode == "TRIGO").NfeItemNumber);

        // Na segunda o número já está na linha: um ItemNumber enviado é ignorado.
        var second = await Returns(scenario).ExecuteAsync(Request(reloaded, ("TRIGO", 100m, 7)), "tester");

        Assert.NotEqual(Guid.Empty, second.Key);
        scenario.Db.Context.ChangeTracker.Clear();
        Assert.Equal(3, (await scenario.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.ItemCode == "TRIGO" && i.PurchaseInvoiceKey == origin.Key)).NfeItemNumber);
    }

    [Fact]
    public async Task Typed_line_returns_without_the_conference()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync(withXml: false);

        var created = await Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 100m, 1)), "tester");

        Assert.Equal("51", (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key)).Items.Single().CstIcms);
    }

    [Fact]
    public async Task Supplier_taxation_the_usage_does_not_mirror_is_refused()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync(icms: SupplierNfeXml.Icms00(1500m, 18m));

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 1m, null)), "tester"));

        Assert.Contains("Item TRIGO: a natureza de devolução DEVOLUCAO DE COMPRA não reproduz a tributação da compra — CST do ICMS: compra 00, devolução 51", e.Message);
    }

    [Theory]
    [InlineData("st")]
    [InlineData("ipi")]
    public async Task Item_with_ipi_or_st_is_refused(string kind)
    {
        var icms = kind == "st" ? SupplierNfeXml.Icms10WithSt() : SupplierNfeXml.Icms51(1500m);
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync(icms: icms);
        if (kind == "ipi")
        {
            var stored = await scenario.Db.Context.PurchaseInvoices.SingleAsync(i => i.Key == origin.Key);
            stored.XmlData = SupplierNfeXml.Bytes(SupplierNfeXml.Build(
                SupplierNfeXml.Det(1, "TRIGO", "TRIGO EM GRAOS", 1000m, 1.5m, SupplierNfeXml.Icms51(1500m), ipi: SupplierNfeXml.Ipi(10m)),
                SupplierNfeXml.Det(2, "MILHO", "MILHO EM GRAOS", 500m, 1m, SupplierNfeXml.Icms51(500m))));
            await scenario.Db.SaveChangesAsync();
            scenario.Db.Context.ChangeTracker.Clear();
        }

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 1m, null)), "tester"));

        Assert.Equal("Item TRIGO: a nota do fornecedor tem IPI/ICMS-ST neste item, que o Siagro ainda não devolve.", e.Message);
    }

    [Fact]
    public async Task Line_whose_product_is_not_registered_is_refused()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync();
        var line = await scenario.Db.Context.PurchaseInvoicesItems.SingleAsync(i => i.ItemCode == "TRIGO" && i.PurchaseInvoiceKey == origin.Key);
        line.ItemCode = "TRG-FORNECEDOR";
        await scenario.Db.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();
        origin = await scenario.Db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == origin.Key);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, ("TRG-FORNECEDOR", 1m, null)), "tester"));

        Assert.Equal("Item TRG-FORNECEDOR: o produto não está cadastrado; ajuste o produto na entrada antes de devolver.", e.Message);
    }

    [Fact]
    public async Task Returnable_items_show_the_supplier_item_number()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync();

        var items = await new PurchaseInvoicesNfeReturnableItemsService(scenario.Db).ExecuteAsync(origin.Key);

        Assert.Equal(new[] { 1, 2 }, items.Select(i => i.ItemNumber!.Value).ToArray());
    }

    [Fact]
    public async Task Own_entry_return_ignores_item_numbers()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        await PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");
        scenario.Db.Context.ChangeTracker.Clear();
        var origin = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario);

        var created = await Returns(scenario).ExecuteAsync(
            new PurchaseInvoiceNfeReturnRequest(origin.Key, [new PurchaseInvoiceNfeReturnItem(origin.Items.Single().Key!.Value, 400m, 77)], "x"), "tester");

        Assert.Equal(400m, (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key)).Items.Single().Quantity);
        Assert.Equal(1, (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario)).Items.Single().NfeItemNumber);
    }
}
```

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoicesThirdPartyReturnTests"` → Expected: erro de compilação (`ItemNumber`).

- [ ] **Step 3: Serviço de criação**

Em `PurchaseInvoicesNfeReturnCreateService.cs`:

1. `public sealed record PurchaseInvoiceNfeReturnItem(Guid OriginItemKey, decimal Quantity, int? ItemNumber = null);`
2. Atualize o `<summary>` da classe: "a partir de uma entrada própria autorizada pelo Siagro **ou de uma entrada de terceiro confirmada (NF-e do fornecedor, spec terceiro §8)**".
3. Em `ExecuteAsync`, troque o miolo de validação por:

```csharp
        // TODA a validação antes de qualquer escrita.
        var thirdParty = await ValidateOriginAsync(origin);

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new DefaultException("Informe o motivo da devolução.");

        var quantities = await ResolveQuantitiesAsync(origin, request.Items);
        var returnUsages = thirdParty
            ? await ResolveThirdPartyUsageAsync(origin, quantities.Keys)
            : await ResolveReturnUsagesAsync(origin, quantities.Keys);
        var typedNumbers = thirdParty
            ? await ResolveThirdPartyItemNumbersAsync(origin, request.Items, quantities.Keys)
            : new Dictionary<Guid, int>();

        if (!thirdParty)
            EnsureEntryItemNumbers(origin, quantities.Keys);

        // O nItem digitado é identidade da linha na nota do fornecedor: fica na linha de origem para as próximas
        // devoluções (D6). Única escrita na origem; gravada no mesmo SaveChanges do create.
        foreach (var (key, number) in typedNumbers)
            (await db.Context.PurchaseInvoicesItems.FirstAsync(i => i.Key == key)).NfeItemNumber = number;
```

4. `ValidateOriginAsync` passa a devolver `bool` (é de terceiro?):

```csharp
    private async Task<bool> ValidateOriginAsync(PurchaseInvoice origin)
    {
        if (!await gate.IsActiveAsync(origin.BranchCode))
            throw new DefaultException($"A filial {origin.BranchCode} não emite NF-e pelo Siagro.");

        if (origin.IssuerType == DocumentIssuerType.ThirdParty)
        {
            if (origin.InvoiceType != PurchaseInvoiceType.Normal || origin.InvoiceStatus != InvoiceStatus.Confirmed ||
                origin.ChaveNFe is not { Length: 44 } key || !key.All(char.IsDigit))
                throw new DefaultException(
                    "A devolução de entrada de terceiro parte de um documento Normal, confirmado e com a chave da NF-e do fornecedor (44 dígitos).");

            return true;
        }

        if (origin.IssuerType != DocumentIssuerType.Own || origin.InvoiceType != PurchaseInvoiceType.Normal ||
            origin.InvoiceStatus != InvoiceStatus.Confirmed || origin.NfeStatus != NfeStatus.Authorized ||
            origin.ChaveNFe is not { Length: 44 })
            throw new DefaultException(
                "O documento de entrada não tem NF-e própria autorizada pelo Siagro: " +
                "a devolução de compra parte de uma entrada própria autorizada.");

        return false;
    }
```

5. Métodos novos:

```csharp
    /// <summary>Natureza padrão da filial (spec terceiro D3), a mesma para todas as linhas.</summary>
    private async Task<Dictionary<Guid, int>> ResolveThirdPartyUsageAsync(PurchaseInvoice origin, IEnumerable<Guid> originItemKeys)
    {
        var code = await db.Context.Branchs.AsNoTracking()
            .Where(b => b.Code == origin.BranchCode)
            .Select(b => b.ThirdPartyPurchaseReturnUsageCode)
            .FirstOrDefaultAsync();
        var usage = code is { } value
            ? await db.Context.Usages.AsNoTracking().FirstOrDefaultAsync(u => u.Code == value)
            : null;

        if (usage is null || usage.Inactive || usage.Direction != UsageDirection.Outgoing)
            throw new DefaultException($"Configure a natureza de devolução de compra de terceiro na filial {origin.BranchCode}.");

        return originItemKeys.ToDictionary(key => key, _ => usage.Code);
    }

    /// <summary>
    /// nItem de cada linha devolvida (spec terceiro §8.3): o da linha de origem, ou o digitado quando falta (1–990, único no
    /// documento); recusa item com IPI/ICMS-ST na nota do fornecedor e produto fora do cadastro. Devolve só os digitados.
    /// </summary>
    private async Task<Dictionary<Guid, int>> ResolveThirdPartyItemNumbersAsync(
        PurchaseInvoice origin, IReadOnlyList<PurchaseInvoiceNfeReturnItem> requested, IEnumerable<Guid> originItemKeys)
    {
        var supplierNfe = origin.XmlData is { Length: > 0 } ? SupplierNfeXmlReader.Read(origin.XmlData) : null;
        var used = origin.Items.Where(i => i.NfeItemNumber != null).Select(i => i.NfeItemNumber!.Value).ToHashSet();
        var typed = new Dictionary<Guid, int>();

        foreach (var key in originItemKeys)
        {
            var bought = origin.Items.First(i => i.Key == key);
            var number = bought.NfeItemNumber;

            if (number is null)
            {
                var informed = requested.First(i => i.OriginItemKey == key).ItemNumber;

                if (informed is not (>= 1 and <= 990))
                    throw new DefaultException($"Item {bought.ItemCode}: informe o número do item na NF-e do fornecedor.");

                if (!used.Add(informed.Value))
                    throw new DefaultException($"Item {bought.ItemCode}: o número {informed} já é de outro item desta NF-e do fornecedor.");

                typed[key] = informed.Value;
                number = informed;
            }

            var det = supplierNfe?.Items.FirstOrDefault(d => d.ItemNumber == number);
            if (det is not null && (det.IpiValue > 0m || det.IcmsStValue > 0m))
                throw new DefaultException($"Item {bought.ItemCode}: a nota do fornecedor tem IPI/ICMS-ST neste item, que o Siagro ainda não devolve.");

            if (!await db.Context.Items.AsNoTracking().AnyAsync(i => i.ItemCode == bought.ItemCode))
                throw new DefaultException($"Item {bought.ItemCode}: o produto não está cadastrado; ajuste o produto na entrada antes de devolver.");
        }

        return typed;
    }
```

(`using SiagroB1.Application.Services.PurchaseInvoices;` já existe.)

- [ ] **Step 4: Conferência só onde há fotografia** — em `PurchaseInvoicesTaxApplyService.ApplyAsync`, troque

```csharp
            if (ownReturn)
                NfeReturnConference.Ensure(item, OriginOf(item, origins), usage.Name, "compra");
```

por

```csharp
            if (ownReturn)
            {
                var origin = OriginOf(item, origins);

                // Linha de origem sem fotografia (entrada de terceiro digitada, ou linha incluída depois da importação):
                // não há com o que conferir (spec terceiro D2).
                if (!string.IsNullOrWhiteSpace(origin.CstIcms))
                    NfeReturnConference.Ensure(item, origin, usage.Name, "compra");
            }
```

- [ ] **Step 5: Itens devolvíveis com o número** — DTO: `public int? ItemNumber { get; set; }` com `<summary>nItem da linha na NF-e de origem; nulo na entrada de terceiro sem o número (o diálogo pede).</summary>`. No serviço, dentro do `new PurchaseInvoiceNfeReturnableItemDto { ... }`:

```csharp
                    ItemNumber = origin.IssuerType == DocumentIssuerType.ThirdParty
                        ? item.NfeItemNumber
                        : NfeItemNumbering.OriginNumber(item, origin.Items.Count),
```

(`using SiagroB1.Domain.Enums;`.)

- [ ] **Step 6: Action** — `ODataConfigurations.cs`, depois de `purchaseInvoicesCreateNfeReturn.CollectionParameter<double>("Quantities");`:

```csharp
        // nItem digitado da entrada de terceiro, paralelo a OriginItemKeys (0 = não informado). Sempre enviado pela tela.
        purchaseInvoicesCreateNfeReturn.CollectionParameter<int>("ItemNumbers");
```

`NfeReturnActionParameters` — método novo:

```csharp
    public static List<int> ItemNumbers(ODataActionParameters parameters)
    {
        if (!parameters.TryGetValue("ItemNumbers", out var value) || value is not IEnumerable sequence)
            return [];

        var result = new List<int>();

        foreach (var item in sequence)
        {
            result.Add(item switch
            {
                int i => i,
                long l => (int)l,
                double d => (int)d,
                _ => int.TryParse(item?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : throw new ApplicationException($"Número de item inválido: {item}"),
            });
        }

        return result;
    }
```

Controller — depois de `var quantities = ...`:

```csharp
        var itemNumbers = NfeReturnActionParameters.ItemNumbers(parameters);
```

depois do teste de `quantities.Count`:

```csharp
        if (itemNumbers.Count != itemKeys.Count)
            return BadRequest("A lista de itens e a de números de item têm tamanhos diferentes.");
```

e o `new PurchaseInvoiceNfeReturnItem(itemKey, quantities[i])` vira `new PurchaseInvoiceNfeReturnItem(itemKey, quantities[i], itemNumbers[i] == 0 ? null : itemNumbers[i])`. Atualize o `<summary>` do controller: "`Quantities` e `ItemNumbers` são arrays PARALELOS a `OriginItemKeys`".

- [ ] **Step 7: Rodar** a classe nova → PASS (15). Depois `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Nfe"` → verde, sem mudar asserção dos testes da devolução própria.

- [ ] **Step 8: Commit** (`git add` dos 2 arquivos novos; pathspec com todos os arquivos da tarefa). Mensagem `feat(invoice): devolver entrada de terceiro com a natureza da filial e o nItem do fornecedor`.

---

### Task 5: Prontidão e emissão da devolução de terceiro

**Files:**
- Modify: `siagro-b1-backend/SiagroB1.Application/Services/Nfe/NfeReadinessValidator.cs` (`LoadPurchaseReturnOriginAsync`, ~linhas 263-299)
- Modify: `siagro-b1-backend/SiagroB1.Application.Tests/Nfe/PurchaseInvoicesThirdPartyReturnTests.cs` (testes de emissão)

**Interfaces:**
- Consumes: `ThirdPartyPurchaseSeed` e `Returns(...)` (Task 4); `PurchaseInvoicesNfeIssueServiceTests.Issue/ReloadAsync` (existentes); `FakeNfeSefazClient` (`AuthorizeResponses`, `Sent`, `Authorized`), `FakeNfeNumberReservationService(int)` (`Calls`).

- [ ] **Step 1: Testes (falham na prontidão: "NF-e não autorizada")** — acrescente à classe da Task 4:

```csharp
    [Theory]
    [InlineData("TRIGO", 1, "5202")]
    [InlineData("MILHO", 2, "5202")]
    public async Task Third_party_return_is_issued_referencing_the_supplier_item(string code, int nItem, string cfop)
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync();
        var created = await Returns(scenario).ExecuteAsync(Request(origin, (code, 100m, null)), "tester");
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var outcome = await PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, sefaz).ExecuteAsync(created.Key, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        var signed = Assert.Single(sefaz.Sent).Xml;
        Assert.Contains("<tpNF>1</tpNF>", signed);
        Assert.Contains("<finNFe>4</finNFe>", signed);
        Assert.Contains($"<CFOP>{cfop}</CFOP>", signed);
        Assert.Contains($"<chaveAcesso>{SupplierNfeXml.AccessKey}</chaveAcesso>", signed);
        Assert.Contains($"<nItem>{nItem}</nItem>", signed);
        Assert.DoesNotContain("<NFref>", signed);
        Assert.Contains("<tPag>90</tPag>", signed);
        Assert.Contains("<CPF>52998224725</CPF>", signed);
        Assert.Contains("Devolução da NF-e nº 456, série 1", signed);
    }

    [Fact]
    public async Task Return_whose_origin_lost_its_key_is_refused_before_the_number()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync();
        var created = await Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 100m, null)), "tester");
        var context = TestDb.CreateUnitOfWork(scenario.DatabaseName).Context;
        (await context.PurchaseInvoices.SingleAsync(i => i.Key == origin.Key)).ChaveNFe = null;
        await context.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();
        var reservation = new FakeNfeNumberReservationService(10);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, new FakeNfeSefazClient(), reservation).ExecuteAsync(created.Key, "tester"));

        Assert.Contains("Entrada de origem 456: sem a chave da NF-e do fornecedor", e.Message);
        Assert.Equal(0, reservation.Calls);
    }
```

Antes de escrever o assert do `infCpl`, confira o texto real em `NfeIssueInputAssembler` (`ReturnReference`): o formato é "Devolução da NF-e nº {nNF}, série {serie}, de {data}, chave {chave}." — ajuste a substring se o nNF sair sem zeros à esquerda diferente de "456".

Run → Expected: FAIL ("Entrada de origem 456: NF-e não autorizada").

- [ ] **Step 2: Prontidão** — em `LoadPurchaseReturnOriginAsync`, troque o bloco do `NfeStatus` e o cálculo do número:

```csharp
        // Entrada de terceiro: a NF-e é do fornecedor (o NfeStatus fica None para sempre); basta a chave dele.
        var thirdParty = origin.IssuerType == DocumentIssuerType.ThirdParty;

        if (thirdParty ? origin.ChaveNFe is not { Length: 44 } : origin.NfeStatus != NfeStatus.Authorized || origin.ChaveNFe is not { Length: 44 })
        {
            problems.Add(thirdParty
                ? $"Entrada de origem {origin.TaxDocumentNumber}: sem a chave da NF-e do fornecedor"
                : $"Entrada de origem {origin.TaxDocumentNumber}: NF-e não autorizada");
            return null;
        }
```

e

```csharp
            // Terceiro: só o nItem gravado (da importação ou digitado no Devolver); o "uma linha só = item 1" é da origem própria.
            var number = bought is null ? null : thirdParty ? bought.NfeItemNumber : NfeItemNumbering.OriginNumber(bought, origin.Items.Count);
```

(`using SiagroB1.Domain.Enums;` se faltar.)

- [ ] **Step 3: Rodar** a classe → PASS. Suíte inteira: `dotnet test SiagroB1.Application.Tests` e `dotnet test SiagroB1.Fiscal.Tests` → verdes (contagens no relatório).

- [ ] **Step 4: Commit** (pathspec: o validador e o teste). Mensagem `feat(invoice): emissão da devolução de entrada de terceiro referencia o item do fornecedor`.

---

### Task 6: `nItem` do rascunho até o `create()` (frontend)

**Files:**
- Modify: `siagro-b1-frontend/webapp/helpers/PurchaseInvoiceDraftHelpers.ts`
- Modify: `siagro-b1-frontend/webapp/test/unit/helpers/PurchaseInvoiceDraftHelpers.qunit.ts`

**Interfaces:**
- Produces: `InvoiceItemPayload.NfeItemNumber: number` (nulo na linha em branco); `ImportedInvoiceItem.NfeItemNumber?: number`.

- [ ] **Step 1: QUnit (falha)** — no teste "a linha em branco já traz as chaves...", acrescente `NfeItemNumber: null` ao objeto esperado; acrescente:

```ts
QUnit.test("XML importado leva o nItem do fornecedor em cada linha", function (assert) {
	const rows = draftItemRows([
		{ ItemCode: "TRIGO", ItemName: "TRIGO", UnitOfMeasureCode: "KG", Quantity: 1, UnitPrice: 1, NfeItemNumber: 1 },
		{ ItemCode: "MILHO", ItemName: "MILHO", UnitOfMeasureCode: "KG", Quantity: 1, UnitPrice: 1, NfeItemNumber: 2 },
	]);

	assert.deepEqual(rows.map((row) => row.NfeItemNumber), [1, 2]);
});
```

- [ ] **Step 2: Implementar** — em `InvoiceItemPayload`, depois de `UsageName`:

```ts
  /** nItem na NF-e do fornecedor (importação do XML); nulo na digitação. É o que a devolução referencia. */
  NfeItemNumber: number;
```

em `ImportedInvoiceItem`: `NfeItemNumber?: number;`; em `blankItemRow()` acrescente `NfeItemNumber: null`; em `draftItemRows`, dentro do objeto do `map`, `NfeItemNumber: item.NfeItemNumber ?? null,`.

- [ ] **Step 3: Gates** — `yarn ts-typecheck`, `yarn lint`, `npx ui5lint` (≤ 919), QUnit (contagem no relatório).

- [ ] **Step 4: Commit** (pathspec: os 2 arquivos). Mensagem `feat(invoice): nItem do fornecedor no rascunho da entrada importada`.

---

### Task 7: Natureza de devolução de terceiro no cadastro de filiais (frontend)

**Files:**
- Modify: `siagro-b1-frontend/webapp/view/branchs/fragments/Form.fragment.xml` (depois do `CheckBox` de `IssuesNfe`)
- Modify: `siagro-b1-frontend/webapp/controller/branchs/Add.controller.ts` (payload do `create()`)
- Modify: `siagro-b1-frontend/webapp/controller/common/CommonController.ts` (value help)

- [ ] **Step 1: Value help** — em `CommonController`, junto dos outros `open...ValueHelp`:

```ts
  /**
   * Natureza de devolução de compra de terceiro da filial (spec terceiro D3): só naturezas de Saída ativas ($filter
   * estático do enum). setProperty sem await: o grupo é diferido.
   */
  async openThirdPartyReturnUsageValueHelp(ev: Input$ValueHelpRequestEvent) {
    const oTarget = ev.getSource().getBindingContext() as Context;

    const oSelected = await DialogHelper.openTableSelectDialog(
      this, "UsagesSelectDialog", ["Name", "Description"],
      [new Filter("Inactive", FilterOperator.EQ, false)], undefined, "Direction eq 'Outgoing'");

    if (!oSelected) {
      return;
    }

    void oTarget.setProperty("ThirdPartyPurchaseReturnUsageCode", oSelected.getProperty("Code"));
  }
```

(confira os imports de `DialogHelper`, `Context` v4 e `Input$ValueHelpRequestEvent`; acrescente os que faltarem.)

- [ ] **Step 2: Formulário** — depois de `<CheckBox selected="{IssuesNfe}" visible="{ui>/standalone}" />`:

```xml
          <!-- Natureza aplicada à devolução de compra de uma entrada de terceiro (spec terceiro D3). Só faz sentido
               na filial que emite NF-e pelo Siagro. -->
          <Label text="Natureza de devolução de compra de terceiro"
                 visible="{= ${ui>/standalone} === true &amp;&amp; ${path: 'IssuesNfe', targetType: 'any'} === true }" />
          <!-- ui5lint-disable no-deprecated-api -->
          <Input
            value="{ThirdPartyPurchaseReturnUsageCode}"
            showValueHelp="true"
            valueHelpOnly="true"
            valueHelpRequest=".openThirdPartyReturnUsageValueHelp"
            visible="{= ${ui>/standalone} === true &amp;&amp; ${path: 'IssuesNfe', targetType: 'any'} === true }" />
          <!-- ui5lint-enable no-deprecated-api -->
```

- [ ] **Step 3: Payload** — no `oBinding.create({...})` de `branchs/Add.controller.ts`, depois de `IssuesNfe: false,`, acrescente `ThirdPartyPurchaseReturnUsageCode: null,`.

- [ ] **Step 4: Gates** (como na Task 6) e **Commit** (pathspec: os 3 arquivos). Mensagem `feat(master-data): natureza de devolução de compra de terceiro no cadastro de filiais`.

---

### Task 8: "Devolver" na entrada de terceiro (frontend)

**Files:**
- Modify: `siagro-b1-frontend/webapp/helpers/PurchaseInvoiceNfeHelpers.ts`
- Modify: `siagro-b1-frontend/webapp/test/unit/helpers/PurchaseInvoiceNfeHelpers.qunit.ts`
- Modify: `siagro-b1-frontend/webapp/controller/purchaseInvoices/Detail.controller.ts`
- Modify: `siagro-b1-frontend/webapp/view/purchaseInvoices/fragments/NfeReturnDialog.fragment.xml`

**Interfaces:**
- Consumes: função `PurchaseInvoicesNfeReturnableItems` com `ItemNumber` por linha; action `PurchaseInvoicesCreateNfeReturn` com `ItemNumbers` (Task 4).
- Produces: `canReturnThirdPartyPurchase(doc: PurchaseNfeState, taxLocked: boolean): boolean`; `type PurchaseReturnItemRow`; `buildPurchaseItemNumbers(rows, keys)`.

- [ ] **Step 1: QUnit (falha)** — em `PurchaseInvoiceNfeHelpers.qunit.ts` importe `canReturnThirdPartyPurchase, buildPurchaseItemNumbers` e acrescente:

```ts
const thirdParty: PurchaseNfeState = {
	InvoiceStatus: "Confirmed", InvoiceType: "Normal", IssuerType: "ThirdParty", IsNfeReturn: false, NfeStatus: "None",
	ChaveNFe: "35261000052998224725550010000004561123456780",
};

QUnit.test("devolve a entrada de terceiro confirmada, com chave, na filial que emite NF-e", function (assert) {
	assert.strictEqual(canReturnThirdPartyPurchase(thirdParty, true), true);
	assert.strictEqual(canReturnThirdPartyPurchase(thirdParty, false), false, "filial sem NF-e");
	assert.strictEqual(canReturnThirdPartyPurchase({ ...thirdParty, InvoiceStatus: "Pending" }, true), false);
	assert.strictEqual(canReturnThirdPartyPurchase({ ...thirdParty, ChaveNFe: "123" }, true), false);
	assert.strictEqual(canReturnThirdPartyPurchase({ ...thirdParty, InvoiceType: "Return" }, true), false, "devolução do cliente");
	assert.strictEqual(canReturnThirdPartyPurchase({ ...thirdParty, IssuerType: "Own" }, true), false);
});

QUnit.test("número do item: zero quando a linha já tem, o digitado quando falta", function (assert) {
	const rows = [
		{ OriginItemKey: "a", ItemCode: "TRIGO", ItemNumber: 1, TypedItemNumber: null },
		{ OriginItemKey: "b", ItemCode: "MILHO", ItemNumber: null, TypedItemNumber: 3 },
	];

	assert.deepEqual(buildPurchaseItemNumbers(rows, ["a", "b"]), { ok: true, itemNumbers: [0, 3] });
	assert.deepEqual(buildPurchaseItemNumbers(rows.slice(0, 1), ["a"]), { ok: true, itemNumbers: [0] }, "entrada própria: só zeros");
});

QUnit.test("número do item faltando, fora de 1 a 990 ou repetido é recusado", function (assert) {
	const row = (typed: number | null) => [
		{ OriginItemKey: "a", ItemCode: "TRIGO", ItemNumber: 1, TypedItemNumber: null },
		{ OriginItemKey: "b", ItemCode: "MILHO", ItemNumber: null, TypedItemNumber: typed },
	];

	assert.deepEqual(buildPurchaseItemNumbers(row(null), ["b"]), { ok: false, message: "Item MILHO: informe o número do item na NF-e do fornecedor." });
	assert.deepEqual(buildPurchaseItemNumbers(row(991), ["b"]), { ok: false, message: "Item MILHO: informe o número do item na NF-e do fornecedor." });
	assert.deepEqual(buildPurchaseItemNumbers(row(1), ["b"]), { ok: false, message: "Item MILHO: o número 1 já é de outro item desta NF-e do fornecedor." });
});
```

- [ ] **Step 2: Helpers** — em `PurchaseInvoiceNfeHelpers.ts`, em `PurchaseNfeState` acrescente `ChaveNFe?: string;` e, depois de `canReturnPurchase`:

```ts
/** "Devolver" da entrada de TERCEIRO (NF-e do fornecedor): filial que emite pelo Siagro, Normal, confirmada, com a chave. */
export function canReturnThirdPartyPurchase(doc: PurchaseNfeState, taxLocked: boolean): boolean {
	return taxLocked === true && doc.IssuerType === "ThirdParty" && doc.InvoiceType === "Normal" &&
		doc.InvoiceStatus === "Confirmed" && /^\d{44}$/.test(doc.ChaveNFe ?? "");
}

/** Linha do diálogo "Devolver" no que toca ao nItem da nota de origem. */
export type PurchaseReturnItemRow = {
	OriginItemKey: string;
	ItemCode: string;
	/** nItem já gravado na linha de origem; nulo na entrada de terceiro sem o número. */
	// eslint-disable-next-line @typescript-eslint/no-redundant-type-constituents -- strictNullChecks desligado; null é intencional
	ItemNumber: number | null;
	/** O que o usuário digitou quando falta o número. */
	// eslint-disable-next-line @typescript-eslint/no-redundant-type-constituents -- strictNullChecks desligado; null é intencional
	TypedItemNumber: number | null;
};

/**
 * `ItemNumbers` da action, paralelo a `keys`: 0 quando a linha de origem já tem o número (o servidor usa o dela), o
 * digitado quando falta (1 a 990, sem repetir no documento). Quem decide de verdade é o servidor.
 */
export function buildPurchaseItemNumbers(
	rows: PurchaseReturnItemRow[], keys: string[]
): { ok: true; itemNumbers: number[] } | { ok: false; message: string } {
	const present = (value: number | null) => value !== null && value !== undefined;
	const used = new Set(rows.filter((r) => present(r.ItemNumber)).map((r) => Number(r.ItemNumber)));
	const itemNumbers: number[] = [];

	for (const key of keys) {
		const row = rows.find((r) => r.OriginItemKey === key);

		if (!row || present(row.ItemNumber)) {
			itemNumbers.push(0);
			continue;
		}

		const typed = present(row.TypedItemNumber) ? Number(row.TypedItemNumber) : NaN;

		if (!Number.isInteger(typed) || typed < 1 || typed > 990) {
			return { ok: false, message: `Item ${row.ItemCode}: informe o número do item na NF-e do fornecedor.` };
		}

		if (used.has(typed)) {
			return { ok: false, message: `Item ${row.ItemCode}: o número ${typed} já é de outro item desta NF-e do fornecedor.` };
		}

		used.add(typed);
		itemNumbers.push(typed);
	}

	return { ok: true, itemNumbers };
}
```

- [ ] **Step 3: Detail** (`Detail.controller.ts`):
  - import `canReturnThirdPartyPurchase, buildPurchaseItemNumbers` do helper;
  - `type PurchaseReturnRow = ReturnableRow & { ItemName: string; PurchasedQuantity: number; ReturnedQuantity: number; ItemNumber: number | null; TypedItemNumber: number | null };` (com o mesmo `eslint-disable-next-line` dos `| null`);
  - em `refreshDetailNfe`: `uiModel.setProperty("/canReturn", !!state && (canReturnPurchase(state, nfeMode) || canReturnThirdPartyPurchase(state, uiModel.getProperty("/taxLocked") === true)));`
  - em `onNfeReturn`, o modelo do diálogo vira:
    ```ts
    this.getView().setModel(new JSONModel({
      rows: prefillNfeReturnRows(rows).map((row) => ({ ...row, TypedItemNumber: null })),
      reason: "",
      busy: false,
      // A coluna "Item na NF" só existe na entrada de terceiro (a própria sempre tem o nItem da nossa emissão).
      thirdParty: ctx.getProperty("IssuerType") === "ThirdParty",
    }), "nfeReturn");
    ```
  - em `onConfirmNfeReturn`, depois do `if (built.ok === false) {...}`:
    ```ts
    const numbers = buildPurchaseItemNumbers(
      model.getProperty("/rows") as PurchaseReturnRow[], built.payload.OriginItemKeys);

    if (numbers.ok === false) {
      MessageBox.warning(numbers.message);
      return;
    }
    ```
    e, depois de `action.setParameter("Quantities", ...)`: `action.setParameter("ItemNumbers", numbers.itemNumbers);` (sempre: parâmetro declarado que falta faz a action chegar nula no servidor).

- [ ] **Step 4: Diálogo** — no `core:require` do `Dialog` acrescente `Integer: 'sap/ui/model/type/Integer'`; antes da coluna "A devolver":

```xml
					<Column hAlign="End" width="8rem" visible="{= ${nfeReturn>/thirdParty} === true }"><Text text="Item na NF" /></Column>
```

e a célula correspondente (antes do `Input` da quantidade):

```xml
							<VBox alignItems="End">
								<!-- O nItem da importação vem pronto; onde falta (documento digitado), o operador informa. -->
								<Text text="{nfeReturn>ItemNumber}" wrapping="false"
									visible="{= ${nfeReturn>ItemNumber} !== null &amp;&amp; ${nfeReturn>ItemNumber} !== undefined }" />
								<Input width="5rem" textAlign="End"
									visible="{= ${nfeReturn>ItemNumber} === null || ${nfeReturn>ItemNumber} === undefined }"
									value="{ path: 'nfeReturn>TypedItemNumber', type: 'Integer' }" />
							</VBox>
```

- [ ] **Step 5: Gates** (como na Task 6) e **Commit** (pathspec: os 4 arquivos). Mensagem `feat(invoice): devolver entrada de terceiro, com o número do item do fornecedor`.

---

### Task 9: Verificação fim a fim (controlador, não subagente)

Executada pelo controlador da sessão (precisa de login do usuário e de emissão em homologação).

- [ ] Aplicar a migration no `CEAGUI_SIAGRO_DEV`: `ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`; conferir a coluna e o `__EFMigrationsHistory`.
- [ ] Subir Web + Gateway (`ceagui`, `--no-build` depois de `dotnet build`) e o dev server; login pelo usuário.
- [ ] Filial 01: natureza de devolução de compra de terceiro = a natureza 7 (DEVOLUCAO DE COMPRA, Saída) pela tela.
- [ ] Fornecedor de teste pessoa jurídica (CNPJ fictício com DV válido, IE, endereço em SP) pela tela ou API; XML fictício de NF-e dele (chave com o CNPJ dele e DV válido, um `det` de TRIGO com ICMS 51 18% dif. 100 cBenef SP053521 e o IBS/CBS que a natureza 7 produz), guardado fora dos repos em `C:\Projetos\SiagroB1\nfe-homologacao-testes\`.
- [ ] Importar o XML pela tela, ajustar o produto para `1` TRIGO, salvar, confirmar; "Devolver" parcial; conferir a devolução (CFOP 5202, natureza 7); emitir em **homologação**; conferir no XML `DFeReferenciado` (chave do fornecedor + nItem 1), destinatário = fornecedor. Se a SEFAZ recusar por nota referenciada inexistente, registrar o retorno e parar no XSD.
- [ ] Documento digitado: entrada de terceiro sem XML, com chave; "Devolver" pede o "Item na NF"; conferir que o número ficou na linha de origem.
- [ ] Parar a stack; memória do projeto; relatório com as decisões.
