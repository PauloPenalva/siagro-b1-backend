# Inutilização do número da NF-e rejeitada — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ação "Inutilizar numeração" no documento de saída e na entrada própria cancelados com NF-e rejeitada, que pede a inutilização do número à SEFAZ (`NfeInutilizacao4`) e guarda o comprovante.

**Architecture:** segue o molde do cancelamento da NF-e. O `SiagroB1.Fiscal` ganha o pedido/retorno e a chamada à Zeus; a `Application` ganha uma base genérica (`NfeVoidNumberServiceBase<TDocument>`) com duas classes finas e dois downloads; o `Web` expõe duas actions e duas functions. No frontend, um helper decide a visibilidade e um diálogo reaproveitado pede a justificativa, nas telas de Detail e lista dos dois documentos.

**Tech Stack:** .NET 10, EF Core (InMemory nos testes), xUnit, Zeus.Net.NFe.NFCe 2026.9.24.1416, OData; OpenUI5 + TypeScript, QUnit.

**Spec:** `docs/superpowers/specs/2026-10-08-nfe-number-void-design.md` (backend repo).

## Global Constraints

- Dois repos, dois branches com o MESMO nome `feature/nfe-number-void`: `siagro-b1-backend` (já existe, com a spec) e `siagro-b1-frontend` (criar a partir do `main`). Nunca push; merge é do usuário.
- Commits: `tipo(escopo): descrição em pt-BR` — tipo/escopo fechados (`feat`/`test`/`docs`…, escopo `invoice`); terminar com `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`. Sem migration ⇒ sem trailer `DB:`.
- Todo arquivo novo: `git add` logo depois de criado.
- Identificadores em inglês; texto que o usuário lê em pt-BR.
- Justificativa: 15 a 255 caracteres depois do `Trim` — mensagem "A justificativa deve ter entre 15 e 255 caracteres." (a mesma do cancelamento).
- Enums persistidos como inteiro: `NfeStatus.Voided = 6`, `NfeXmlKind.NumberVoid = 5`. **Sem migration.**
- Códigos SEFAZ: 102 = homologada; 256 e 563 = já inutilizada / pedido repetido; qualquer outro = recusa.
- Backend: `dotnet test SiagroB1.Fiscal.Tests` e `dotnet test SiagroB1.Application.Tests` verdes ao fim de cada task. Frontend: `yarn ts-typecheck` e `yarn lint` limpos (o `yarn test` nunca passa pelo gate de cobertura — ver memória `frontend-yarn-test-coverage-gate`; os QUnit rodam pelo comando da Task 6).

## Review Focus

1. **NF-e rejeitada na validação local** (nada foi enviado): `ChaveNFe` e `NfeEnvironment` nulos, número reservado → o pedido sai com ano corrente de Brasília, CNPJ da filial e ambiente atual da filial. Teste na Task 3.
2. **Documento que perdeu o número no 539** (`TaxDocumentNumber` nulo) → recusa "não tem número e série", sem chamar a SEFAZ. Teste na Task 3.
3. **Segundo clique depois de inutilizada** → recusa "já foi inutilizada", sem chamar a SEFAZ. Teste na Task 3.
4. **Edição/exclusão de documento Inutilizado pela API** → travado como o cancelado (`IsFrozen`, `EnsureDeletable`). Teste na Task 2.
5. **Falha inesperada na Zeus (XSD ausente, certificado)** → documento intacto e mensagem com detalhe técnico. Teste na Task 3; o XSD é provado na Task 1.

---

## File Structure

Backend (`C:\Projetos\SiagroB1\siagro-b1-backend`):

| Arquivo | Responsabilidade |
|---|---|
| `SiagroB1.Fiscal/Schemas/{inutNFe,leiauteInutNFe,retInutNFe,procInutNFe}_v4.00.xsd` (novos) | XSDs oficiais; a Zeus valida o pedido antes de enviar |
| `SiagroB1.Fiscal/Nfe/NfeSefaz.cs` | records `NfeVoidNumberRequest`/`NfeVoidNumberResult`, códigos, método da interface, mapper |
| `SiagroB1.Fiscal/Nfe/ZeusNfeSefazClient.cs` | `VoidNumberAsync` com `ServicosNFe.NfeInutilizacao` |
| `SiagroB1.Domain/Enums/NfeStatus.cs`, `NfeXmlKind.cs` | `Voided`, `NumberVoid` |
| `SiagroB1.Application/Services/Nfe/NfeLockRules.cs` | `Voided` congela o documento |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeLock.cs`, `.../PurchaseInvoices/PurchaseInvoiceNfeLock.cs` | `EnsureDeletable` recusa `Voided` |
| `SiagroB1.Application/Services/Nfe/NfeVoidNumberServiceBase.cs` (novo) | regras + pedido + gravação |
| `SiagroB1.Application/Services/Nfe/{Sales,Purchase}InvoicesNfeVoidNumberService.cs` (novos) | classes finas; a de entrada recusa terceiro |
| `SiagroB1.Application/Services/Nfe/{Sales,Purchase}InvoicesNfeVoidNumberXmlDownloadService.cs` (novos) | download do procInutNFe |
| `SiagroB1.Web/Actions/Nfe/{Sales,Purchase}InvoicesVoidNfeNumberController.cs` (novos) | actions |
| `SiagroB1.Web/Functions/Nfe/{Sales,Purchase}InvoicesNfeVoidNumberXmlController.cs` (novos) | functions de download |
| `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` | registro |
| `SiagroB1.Fiscal.Tests/Nfe/NfeVoidNumberSchemaTests.cs`, `NfeSefazResponseMapperTests.cs` | Fiscal |
| `SiagroB1.Application.Tests/Support/FakeNfeSefazClient.cs`, `NfeVoidNumberTestServices.cs` (novo) | fake e cenário |
| `SiagroB1.Application.Tests/Nfe/{Sales,Purchase}InvoicesNfeVoidNumberServiceTests.cs`, `NfeVoidNumberXmlDownloadTests.cs`, `NfeVoidNumberEdmModelTests.cs`, `NfeVoidedLockTests.cs` (novos) | Application/Web |

Frontend (`C:\Projetos\SiagroB1\siagro-b1-frontend`):

| Arquivo | Responsabilidade |
|---|---|
| `webapp/helpers/NfeHelpers.ts` | `canVoidNfeNumber`, desfecho `Voided` |
| `webapp/model/formatter.ts` | "Inutilizada" na situação da NF-e |
| `webapp/dialogs/NfeCancelDialog.ts` | textos opcionais (título, botão, aviso) |
| `webapp/dialogs/NfeVoidNumberAction.ts` (novo) | diálogo + POST + mensagem, comum às 4 telas |
| `webapp/model/ServerRoutes.ts` | 4 rotas |
| `webapp/view/{salesInvoices,purchaseInvoices}/{Detail,Main}.view.xml` + controllers | botões |
| `webapp/test/unit/helpers/NfeHelpers.qunit.ts` | QUnit |

---

## Task 1: Fiscal — pedido, retorno, XSD e Zeus

**Files:**
- Create: `SiagroB1.Fiscal/Schemas/inutNFe_v4.00.xsd`, `leiauteInutNFe_v4.00.xsd`, `retInutNFe_v4.00.xsd`, `procInutNFe_v4.00.xsd`
- Modify: `SiagroB1.Fiscal/Nfe/NfeSefaz.cs`, `SiagroB1.Fiscal/Nfe/ZeusNfeSefazClient.cs`
- Modify: `SiagroB1.Application.Tests/Support/FakeNfeSefazClient.cs` (a interface muda: sem isto os testes da Application não compilam)
- Test: `SiagroB1.Fiscal.Tests/Nfe/NfeVoidNumberSchemaTests.cs` (novo), `SiagroB1.Fiscal.Tests/Nfe/NfeSefazResponseMapperTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record NfeVoidNumberRequest(int Year, string TaxId, int Series, int Number, string Justification);` — `Year` com 2 dígitos (26), `TaxId` só dígitos.
  - `public sealed record NfeVoidNumberResult(int StatusCode, string Reason, string? Protocol = null, string? ProcXml = null);`
  - `NfeStatusCodes.NumberVoided = 102`, `NfeStatusCodes.IsNumberAlreadyVoided(int code)` (256 ou 563).
  - `INfeSefazClient.VoidNumberAsync(NfeVoidNumberRequest request, NfeServiceSettings settings, CancellationToken cancellationToken = default) : Task<NfeVoidNumberResult>`
  - `NfeSefazResponseMapper.FromVoidNumber(string? requestXml, retInutNFe response) : NfeVoidNumberResult` (internal; Fiscal.Tests já enxerga internos — confira `InternalsVisibleTo` no csproj, como o mapper existente é testado).
  - Fake: `FakeNfeSefazClient.VoidNumberResponses` (`Queue<Func<NfeVoidNumberRequest, NfeVoidNumberResult>>`), `VoidNumberRequests`, `VoidNumberSettings`, `FakeNfeSefazClient.VoidNumberProtocol = "135260000000777"`, `FakeNfeSefazClient.NumberVoided(NfeVoidNumberRequest r)`.

- [ ] **Step 1: Copiar os XSDs** (PL oficial, já usados pelo EfisCloud; o `tiposBasico_v4.00.xsd` e o `xmldsig-core-schema_v1.01.xsd` que eles importam JÁ existem em `Schemas/` — NÃO sobrescrever)

```bash
cd /c/Projetos/SiagroB1/siagro-b1-backend
for f in inutNFe leiauteInutNFe retInutNFe procInutNFe; do cp "/c/Projetos/EfisCloud/backend/schemas/${f}_v4.00.xsd" SiagroB1.Fiscal/Schemas/; done
git add SiagroB1.Fiscal/Schemas/*Inut*_v4.00.xsd SiagroB1.Fiscal/Schemas/inutNFe_v4.00.xsd
```

O csproj já publica `Schemas\**\*.xsd` (`CopyToOutputDirectory`/`CopyToPublishDirectory`).

- [ ] **Step 2: Escrever o teste de XSD (falha: tipos inexistentes até o Step 4; o teste do pedido valida a montagem igual à da Zeus)**

`SiagroB1.Fiscal.Tests/Nfe/NfeVoidNumberSchemaTests.cs`:

```csharp
using DFe.Classes.Flags;
using NFe.Classes.Servicos.Inutilizacao;
using NFe.Classes.Servicos.Tipos;
using NFe.Utils.Inutilizacao;
using NFe.Utils.Validacao;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Tests.Support;

namespace SiagroB1.Fiscal.Tests.Nfe;

/// <summary>
/// O <c>ZeusNfeSefazClient.VoidNumberAsync</c> valida o inutNFe no XSD antes de enviar
/// (<c>ValidarSchemas = true</c>). Sem os XSD da inutilização em <c>Schemas/</c> a Zeus estoura
/// <see cref="FileNotFoundException"/> — o mesmo achado do cancelamento. Monta o pedido como a Zeus
/// monta e passa pela mesma validação, sem tocar na SEFAZ.
/// </summary>
public class NfeVoidNumberSchemaTests
{
    [Fact]
    public void Void_number_request_validates_against_the_official_schema()
    {
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);
        var settings = new NfeServiceSettings(
            NfeEnvironment.Homologation, "SP", certificate, NfeServiceSettings.DefaultSchemasDirectory);
        var configuration = NfeZeusConfiguration.Create(settings);

        var request = new inutNFe
        {
            versao = "4.00",
            infInut = new infInutEnv
            {
                Id = ExtinutNFe.ObterId(configuration.cUF, 26, "12345678000195", ModeloDocumento.NFe, 1, 233, 233),
                tpAmb = configuration.tpAmb,
                xServ = "INUTILIZAR",
                cUF = configuration.cUF,
                ano = 26,
                CNPJ = "12345678000195",
                mod = ModeloDocumento.NFe,
                serie = 1,
                nNFIni = 233,
                nNFFin = 233,
                xJust = "NF-e rejeitada e documento cancelado",
            },
        };
        request.Assina(certificate, configuration.Certificado.SignatureMethodSignedXml,
            configuration.Certificado.DigestMethodReference, false);

        var error = Record.Exception(() =>
            Validador.Valida(ServicoNFe.NfeInutilizacao, VersaoServico.Versao400, request.ObterXmlString(), cfgServico: configuration));

        Assert.Null(error);
    }

    [Fact]
    public void Void_number_schema_set_is_shipped_with_the_build()
    {
        var directory = NfeServiceSettings.DefaultSchemasDirectory;

        foreach (var file in new[]
                 {
                     "inutNFe_v4.00.xsd", "leiauteInutNFe_v4.00.xsd", "retInutNFe_v4.00.xsd", "procInutNFe_v4.00.xsd",
                     "tiposBasico_v4.00.xsd", "xmldsig-core-schema_v1.01.xsd",
                 })
            Assert.True(File.Exists(Path.Combine(directory, file)), $"Schema ausente: {file}");
    }
}
```

> Se `ExtinutNFe.ObterId` ou o enum `ServicoNFe.NfeInutilizacao` tiverem outro nome/assinatura na versão 2026.9.24.1416, ajuste pelo IntelliSense — confirmados por reflexão em 08/10: `ObterId(Estado cUF, int ano, string cnpj, ModeloDocumento modelo, int serie, int numeroInicial, int numeroFinal)`, `Assina(inutNFe, X509Certificate2, string signatureMethodSignedXml, string digestMethodReference, bool removerAcentos)`, enum `ServicoNFe.NfeInutilizacao`.

- [ ] **Step 3: Escrever os testes do mapper** — acrescentar em `SiagroB1.Fiscal.Tests/Nfe/NfeSefazResponseMapperTests.cs` (manter os `using` existentes; acrescentar `using NFe.Classes.Servicos.Inutilizacao;` e `using DFe.Classes.Flags;` se faltarem):

```csharp
    private const string VoidRequestXml =
        "<inutNFe versao=\"4.00\" xmlns=\"http://www.portalfiscal.inf.br/nfe\"><infInut Id=\"ID35261234567800019555001000000233000000233\">" +
        "<tpAmb>2</tpAmb><xServ>INUTILIZAR</xServ><cUF>35</cUF><ano>26</ano><CNPJ>12345678000195</CNPJ><mod>55</mod>" +
        "<serie>1</serie><nNFIni>233</nNFIni><nNFFin>233</nNFFin><xJust>NF-e rejeitada e documento cancelado</xJust></infInut></inutNFe>";

    private static retInutNFe VoidResponse(int status, string? protocol) => new()
    {
        versao = "4.00",
        infInut = new infInutRet
        {
            tpAmb = TipoAmbiente.Homologacao, verAplic = "SP_NFE_PL009", cStat = status,
            xMotivo = status == 102 ? "Inutilização de número homologado" : "Rejeição: NF-e já está inutilizada na Base de dados da SEFAZ",
            nProt = protocol,
        },
    };

    [Fact]
    public void Homologated_void_number_carries_protocol_and_proc_xml()
    {
        var result = NfeSefazResponseMapper.FromVoidNumber(VoidRequestXml, VoidResponse(102, "135260000000777"));

        Assert.Equal(102, result.StatusCode);
        Assert.Equal("135260000000777", result.Protocol);
        Assert.Contains("<procInutNFe", result.ProcXml);
        Assert.Contains("<nNFIni>233</nNFIni>", result.ProcXml);
        Assert.Contains("<nProt>135260000000777</nProt>", result.ProcXml);
    }

    [Fact]
    public void Void_number_refusal_has_no_proc_xml()
    {
        var result = NfeSefazResponseMapper.FromVoidNumber(VoidRequestXml, VoidResponse(256, null));

        Assert.Equal(256, result.StatusCode);
        Assert.Null(result.Protocol);
        Assert.Null(result.ProcXml);
    }

    [Theory]
    [InlineData(256, true)]
    [InlineData(563, true)]
    [InlineData(102, false)]
    [InlineData(241, false)]
    public void Already_voided_codes(int code, bool expected) =>
        Assert.Equal(expected, NfeStatusCodes.IsNumberAlreadyVoided(code));
```

- [ ] **Step 4: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~VoidNumber|FullyQualifiedName~Already_voided"`
Expected: FAIL de compilação (`NfeVoidNumberRequest`/`FromVoidNumber`/`IsNumberAlreadyVoided` inexistentes).

- [ ] **Step 5: Implementar no `NfeSefaz.cs`**

Depois do record `NfeCancelRequest`:

```csharp
/// <summary>
/// Pedido de inutilização (NfeInutilizacao4) de UM número: <see cref="Year"/> com 2 dígitos,
/// <see cref="TaxId"/> só dígitos. UF, modelo e ambiente vêm da configuração do serviço.
/// </summary>
public sealed record NfeVoidNumberRequest(int Year, string TaxId, int Series, int Number, string Justification);

/// <summary>Retorno da inutilização. <see cref="ProcXml"/> é o <c>procInutNFe</c> (pedido assinado + retorno), só na homologação (102).</summary>
public sealed record NfeVoidNumberResult(int StatusCode, string Reason, string? Protocol = null, string? ProcXml = null);
```

Em `NfeStatusCodes` (depois de `IsCancellationRegistered`):

```csharp
    /// <summary>Inutilização de número homologada.</summary>
    public const int NumberVoided = 102;

    /// <summary>256 = número já inutilizado na SEFAZ; 563 = pedido repetido para a mesma faixa (envio anterior sem resposta).</summary>
    public static bool IsNumberAlreadyVoided(int code) => code is 256 or 563;
```

Na interface `INfeSefazClient`:

```csharp
    /// <summary>Inutiliza UM número da série (faixa inicial = final).</summary>
    Task<NfeVoidNumberResult> VoidNumberAsync(NfeVoidNumberRequest request, NfeServiceSettings settings, CancellationToken cancellationToken = default);
```

No `NfeSefazResponseMapper` (acrescentar `using NFe.Classes.Servicos.Inutilizacao;` no topo):

```csharp
    /// <summary>
    /// Com 102 o comprovante é o <c>procInutNFe</c>: o pedido ASSINADO que foi enviado + o retorno.
    /// Pedido ilegível não impede a homologação — só fica sem comprovante.
    /// </summary>
    public static NfeVoidNumberResult FromVoidNumber(string? requestXml, retInutNFe response)
    {
        var info = response.infInut;
        var code = info?.cStat ?? 0;
        var reason = info?.xMotivo ?? string.Empty;

        if (code != NfeStatusCodes.NumberVoided)
            return new NfeVoidNumberResult(code, reason);

        string? proc = null;
        if (!string.IsNullOrWhiteSpace(requestXml))
        {
            try
            {
                var request = FuncoesXml.XmlStringParaClasse<inutNFe>(requestXml);
                proc = FuncoesXml.ClasseParaXmlString(new procInutNFe { versao = "4.00", inutNFe = request, retInutNFe = response });
            }
            catch (InvalidOperationException)
            {
                proc = null;
            }
        }

        return new NfeVoidNumberResult(code, reason, info!.nProt, proc);
    }
```

- [ ] **Step 6: Implementar no `ZeusNfeSefazClient.cs`** (depois de `SendCorrectionAsync`; acrescentar `using NFe.Classes.Informacoes.Identificacao.Tipos;` se `ModeloDocumento` não estiver visível):

```csharp
    public Task<NfeVoidNumberResult> VoidNumberAsync(
        NfeVoidNumberRequest request, NfeServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, services =>
        {
            // UF, ambiente e modelo vêm da configuração; a Zeus monta, assina e valida no XSD.
            var response = services.NfeInutilizacao(
                request.TaxId, request.Year, ModeloDocumento.NFe, request.Series, request.Number, request.Number,
                request.Justification);

            // ⚠️ EnvioStr = o inutNFe assinado que foi enviado (vira o procInutNFe). Se o E2E mostrar que é o
            // envelope SOAP, extraia o nó <inutNFe> antes de passar — o mapper só perde o comprovante, não a homologação.
            return NfeSefazResponseMapper.FromVoidNumber(response.EnvioStr, response.Retorno);
        }, cancellationToken);
```

- [ ] **Step 7: Fake da Application** — em `SiagroB1.Application.Tests/Support/FakeNfeSefazClient.cs`, depois do bloco de CC-e:

```csharp
    public Queue<Func<NfeVoidNumberRequest, NfeVoidNumberResult>> VoidNumberResponses { get; } = new();
    public List<NfeVoidNumberRequest> VoidNumberRequests { get; } = [];
    public List<NfeServiceSettings> VoidNumberSettings { get; } = [];

    public Task<NfeVoidNumberResult> VoidNumberAsync(
        NfeVoidNumberRequest request, NfeServiceSettings settings, CancellationToken cancellationToken = default)
    {
        VoidNumberRequests.Add(request);
        VoidNumberSettings.Add(settings);
        return Task.FromResult(VoidNumberResponses.Dequeue()(request));
    }

    public const string VoidNumberProtocol = "135260000000777";

    public static NfeVoidNumberResult NumberVoided(NfeVoidNumberRequest r) => new(
        102, "Inutilização de número homologado", VoidNumberProtocol,
        $"<procInutNFe versao=\"4.00\"><inutNFe><infInut><serie>{r.Series}</serie><nNFIni>{r.Number}</nNFIni></infInut></inutNFe>" +
        $"<retInutNFe><infInut><cStat>102</cStat><nProt>{VoidNumberProtocol}</nProt></infInut></retInutNFe></procInutNFe>");
```

- [ ] **Step 8: Rodar e ver passar**

Run: `dotnet test SiagroB1.Fiscal.Tests` → PASS (224 + 6 novos).
Run: `dotnet build SiagroB1.Application.Tests` → compila.
Se `Void_number_request_validates_against_the_official_schema` falhar por tipo do `tiposBasico` (os XSD vieram do EfisCloud, o `tiposBasico_v4.00.xsd` local é outro PL), compare o tipo citado na mensagem e traga só o `tiposBasico` compatível — sem quebrar `NfeSignerTests` (rode a suíte inteira).

- [ ] **Step 9: Commit**

```bash
git add SiagroB1.Fiscal SiagroB1.Fiscal.Tests SiagroB1.Application.Tests/Support/FakeNfeSefazClient.cs
git commit -m "feat(invoice): pedido de inutilização de número da NF-e na SEFAZ" -m "Base fiscal da inutilização (NfeInutilizacao4) de um número só: pedido, retorno com o procInutNFe como comprovante e os XSD oficiais, que a Zeus exige para validar antes de enviar." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Domain — `Voided`, `NumberVoid` e as travas

**Files:**
- Modify: `SiagroB1.Domain/Enums/NfeStatus.cs`, `SiagroB1.Domain/Enums/NfeXmlKind.cs`
- Modify: `SiagroB1.Application/Services/Nfe/NfeLockRules.cs`
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeLock.cs:147-150`, `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceNfeLock.cs:153-156`
- Test: `SiagroB1.Application.Tests/Nfe/NfeVoidedLockTests.cs` (novo)

**Interfaces:**
- Produces: `NfeStatus.Voided = 6`, `NfeXmlKind.NumberVoid = 5`, `NfeLockRules.VoidedMessage`.

- [ ] **Step 1: Teste**

`SiagroB1.Application.Tests/Nfe/NfeVoidedLockTests.cs`:

```csharp
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>NF-e com a numeração inutilizada: o documento fica congelado como o da NF-e cancelada.</summary>
public class NfeVoidedLockTests
{
    [Fact]
    public void Voided_is_frozen_with_its_own_message()
    {
        Assert.True(NfeLockRules.IsFrozen(NfeStatus.Voided));
        Assert.Equal(NfeLockRules.VoidedMessage, NfeLockRules.FrozenMessage(NfeStatus.Voided));
        Assert.Equal("A numeração da NF-e deste documento foi inutilizada na SEFAZ: o documento não pode mudar.",
            NfeLockRules.VoidedMessage);
    }

    [Fact]
    public void Voided_sales_invoice_is_not_deletable()
    {
        var ex = Assert.Throws<DefaultException>(() =>
            SalesInvoiceNfeLock.EnsureDeletable(new SalesInvoice { NfeStatus = NfeStatus.Voided }));

        Assert.Contains("inutilizada", ex.Message);
    }

    [Fact]
    public void Voided_purchase_invoice_is_not_deletable()
    {
        var ex = Assert.Throws<DefaultException>(() =>
            PurchaseInvoiceNfeLock.EnsureDeletable(new PurchaseInvoice { NfeStatus = NfeStatus.Voided }));

        Assert.Contains("inutilizada", ex.Message);
    }
}
```

> Se `new SalesInvoice { … }`/`new PurchaseInvoice { … }` exigir membros `required`, preencha-os com valores mínimos (veja como `NfeTestSeed`/`PurchaseNfeTestSeed` criam). Confira a assinatura real de `PurchaseInvoiceNfeLock.EnsureDeletable` (linha ~151) antes.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeVoidedLockTests"`
Expected: FAIL de compilação (`NfeStatus.Voided` inexistente).

- [ ] **Step 3: Implementar**

`NfeStatus.cs`, depois de `Cancelled = 5,`:

```csharp
    /// <summary>
    /// Numeração inutilizada na SEFAZ (NfeInutilizacao4): a NF-e rejeitada de um documento cancelado
    /// nunca foi autorizada e o número foi queimado. Documento congelado como o cancelado.
    /// </summary>
    Voided = 6,
```

`NfeXmlKind.cs`, depois de `CancellationEvent = 4,`:

```csharp
    /// <summary>procInutNFe da inutilização do número (pedido assinado + retorno da SEFAZ).</summary>
    NumberVoid = 5,
```

`NfeLockRules.cs`:

```csharp
    public const string VoidedMessage =
        "A numeração da NF-e deste documento foi inutilizada na SEFAZ: o documento não pode mudar.";

    /// <summary>Autorizada, cancelada ou inutilizada: o que foi para a nota não muda mais.</summary>
    public static bool IsFrozen(NfeStatus status) => status is NfeStatus.Authorized or NfeStatus.Cancelled or NfeStatus.Voided;

    public static string FrozenMessage(NfeStatus status) => status switch
    {
        NfeStatus.Cancelled => CancelledMessage,
        NfeStatus.Voided => VoidedMessage,
        _ => AuthorizedMessage,
    };
```

`SalesInvoiceNfeLock.EnsureDeletable` e `PurchaseInvoiceNfeLock.EnsureDeletable`: acrescentar `or NfeStatus.Voided` à condição e trocar o texto para
`"Documento com NF-e em processamento, autorizada, denegada, cancelada ou inutilizada não pode ser excluído."` (na entrada, mantenha o começo da frase que já existe e só acrescente ", cancelada ou inutilizada" no mesmo ponto).

- [ ] **Step 4: Rodar a suíte**

Run: `dotnet test SiagroB1.Application.Tests`
Expected: PASS. Se algum teste existente comparar a mensagem antiga de `EnsureDeletable` (`grep -rn "denegada ou cancelada não pode ser excluído" SiagroB1.Application.Tests`), atualize a string esperada para a nova.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Domain/Enums SiagroB1.Application/Services/Nfe/NfeLockRules.cs SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeLock.cs SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceNfeLock.cs SiagroB1.Application.Tests
git commit -m "feat(invoice): situação Inutilizada da NF-e congela o documento" -m "Documento com a numeração inutilizada se comporta como o da NF-e cancelada: não muda nem é excluído, para o comprovante da inutilização não ficar órfão." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Application — base + documento de saída

**Files:**
- Create: `SiagroB1.Application/Services/Nfe/NfeVoidNumberServiceBase.cs`, `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeVoidNumberService.cs`
- Create: `SiagroB1.Application.Tests/Support/NfeVoidNumberTestServices.cs`
- Test: `SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeVoidNumberServiceTests.cs`

**Interfaces:**
- Consumes: Task 1 (`NfeVoidNumberRequest`, `NfeVoidNumberResult`, `INfeSefazClient.VoidNumberAsync`, `NfeStatusCodes.NumberVoided/IsNumberAlreadyVoided`, fake), Task 2 (`NfeStatus.Voided`, `NfeXmlKind.NumberVoid`).
- Produces:
  - `NfeVoidNumberServiceBase<TDocument>.ExecuteAsync(Guid key, string? justification, string userName) : Task<NfeIssueOutcomeDto>`
  - `protected virtual void EnsureIssuer(TDocument document)`
  - Constantes públicas: `NoAnswerMessage`, `NotCancelledMessage`, `NotRejectedMessage`, `AlreadyVoidedMessage`, `NoNumberMessage`.
  - `SalesInvoicesNfeVoidNumberService(IUnitOfWork db, BranchNfeSettingsService settingsService, INfeSefazClient sefaz, NfeNumberReservationService reservation, ILogger<SalesInvoicesNfeVoidNumberService> logger)`
  - Test support: `NfeVoidNumberTestServices.RejectAndCancelSaleAsync(UnitOfWork db, Guid key, bool sentToSefaz = true)`, `NfeVoidNumberTestServices.SalesVoid(UnitOfWork db, FakeNfeSefazClient sefaz)`, `NfeVoidNumberTestServices.Settings(UnitOfWork db, FakeNfeSefazClient sefaz)`.

- [ ] **Step 1: Suporte de teste**

`SiagroB1.Application.Tests/Support/NfeVoidNumberTestServices.cs` (`git add` em seguida):

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Inutilização da numeração sobre os seeds da emissão, com a SEFAZ simulada.</summary>
public static class NfeVoidNumberTestServices
{
    /// <summary>Chave da tentativa rejeitada: ano 26, CNPJ 12345678000195, série 001, número 000000233.</summary>
    public const string RejectedAccessKey = "35261012345678000195550010000002331481516230";

    /// <summary>
    /// Documento cancelado com a NF-e rejeitada. <paramref name="sentToSefaz"/> = false é a rejeição na
    /// validação local: número reservado, mas sem chave nem ambiente.
    /// </summary>
    public static async Task RejectAndCancelSaleAsync(UnitOfWork db, Guid key, bool sentToSefaz = true)
    {
        var invoice = await db.Context.SalesInvoices.SingleAsync(x => x.Key == key);
        invoice.InvoiceStatus = InvoiceStatus.Cancelled;
        Reject(invoice, sentToSefaz);
        await db.SaveChangesAsync();
    }

    public static async Task RejectAndCancelPurchaseAsync(UnitOfWork db, Guid key, bool sentToSefaz = true)
    {
        var invoice = await db.Context.PurchaseInvoices.SingleAsync(x => x.Key == key);
        invoice.InvoiceStatus = InvoiceStatus.Cancelled;
        invoice.IssuerType = DocumentIssuerType.Own;
        Reject(invoice, sentToSefaz);
        await db.SaveChangesAsync();
    }

    /// <summary>Campos da NF-e comuns aos dois documentos (o InvoiceStatus não tem setter na interface).</summary>
    private static void Reject(Domain.Interfaces.INfeDocument invoice, bool sentToSefaz)
    {
        invoice.NfeStatus = NfeStatus.Rejected;
        invoice.TaxDocumentNumber = "000000233";
        invoice.TaxDocumentSeries = "1";
        invoice.NfeRandomCode = "48151623";
        invoice.NfeStatusCode = sentToSefaz ? "929" : null;
        invoice.NfeStatusReason = sentToSefaz
            ? "Rejeição: Informado CST de diferimento sem as informações de diferimento [nItem: 1]"
            : "Rejeitada na validação local, nada foi enviado: XSD";
        invoice.ChaveNFe = sentToSefaz ? RejectedAccessKey : null;
        invoice.NfeEnvironment = sentToSefaz ? NfeEnvironment.Homologation : null;
    }

    public static BranchNfeSettingsService Settings(UnitOfWork db, FakeNfeSefazClient sefaz) =>
        new(db, new NfeOptions(NfeTestSeed.Config()), sefaz);

    public static SalesInvoicesNfeVoidNumberService SalesVoid(UnitOfWork db, FakeNfeSefazClient sefaz) =>
        new(db, Settings(db, sefaz), sefaz, new FakeNfeNumberReservationService(),
            NullLogger<SalesInvoicesNfeVoidNumberService>.Instance);
}
```

> Confira em `NfeTestSeed` qual ambiente a Configuração da NF-e semeada tem (o fallback do teste de rejeição local compara com ele).

- [ ] **Step 2: Testes do serviço (saída)**

`SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeVoidNumberServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using static SiagroB1.Application.Tests.Support.NfeVoidNumberTestServices;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>Inutilização da numeração da NF-e rejeitada do documento de saída (spec 2026-10-08).</summary>
public class SalesInvoicesNfeVoidNumberServiceTests
{
    private const string Reason = "NF-e rejeitada e documento cancelado";

    private static async Task<(NfeScenario Scenario, FakeNfeSefazClient Sefaz)> RejectedAsync(bool sentToSefaz = true)
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await RejectAndCancelSaleAsync(scenario.Db, scenario.InvoiceKey, sentToSefaz);
        return (scenario, new FakeNfeSefazClient());
    }

    [Fact]
    public async Task Homologated_void_marks_the_nfe_voided_and_keeps_the_proc_xml()
    {
        var (scenario, sefaz) = await RejectedAsync();
        sefaz.VoidNumberResponses.Enqueue(FakeNfeSefazClient.NumberVoided);

        var outcome = await SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        Assert.Equal(NfeStatus.Voided, outcome.NfeStatus);
        Assert.Equal("102", outcome.StatusCode);
        var saved = await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(NfeStatus.Voided, saved.NfeStatus);
        Assert.Equal(FakeNfeSefazClient.VoidNumberProtocol, saved.NfeProtocol);
        Assert.Equal(InvoiceStatus.Cancelled, saved.InvoiceStatus);
        Assert.Equal(RejectedAccessKey, saved.ChaveNFe);
        var xml = await scenario.Db.Context.SalesInvoiceNfeXmls.AsNoTracking().SingleAsync(x => x.Kind == NfeXmlKind.NumberVoid);
        Assert.Contains("<procInutNFe", xml.Xml);
    }

    [Fact]
    public async Task Request_takes_year_from_the_key_and_number_from_the_document()
    {
        var (scenario, sefaz) = await RejectedAsync();
        sefaz.VoidNumberResponses.Enqueue(FakeNfeSefazClient.NumberVoided);

        await SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, "  " + Reason + "  ", "tester");

        var request = Assert.Single(sefaz.VoidNumberRequests);
        Assert.Equal(new NfeVoidNumberRequest(26, "12345678000195", 1, 233, Reason), request);
        Assert.Equal(NfeEnvironment.Homologation, Assert.Single(sefaz.VoidNumberSettings).Environment);
    }

    [Fact]
    public async Task Locally_rejected_nfe_uses_the_current_brasilia_year_and_the_branch()
    {
        var (scenario, sefaz) = await RejectedAsync(sentToSefaz: false);
        sefaz.VoidNumberResponses.Enqueue(FakeNfeSefazClient.NumberVoided);

        await SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        var request = Assert.Single(sefaz.VoidNumberRequests);
        var year = TimeZoneInfo.ConvertTime(DateTimeOffset.Now, NfeIssueInputAssembler.BrasiliaZone).Year % 100;
        Assert.Equal(year, request.Year);
        Assert.Equal("12345678000195", request.TaxId);
    }

    [Theory]
    [InlineData(256)]
    [InlineData(563)]
    public async Task Already_voided_at_sefaz_marks_voided_without_proc_xml(int code)
    {
        var (scenario, sefaz) = await RejectedAsync();
        sefaz.VoidNumberResponses.Enqueue(_ => new NfeVoidNumberResult(code, "Rejeição: já inutilizada"));

        var outcome = await SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        Assert.Equal(NfeStatus.Voided, outcome.NfeStatus);
        Assert.Equal(code.ToString(), outcome.StatusCode);
        Assert.False(await scenario.Db.Context.SalesInvoiceNfeXmls.AnyAsync(x => x.Kind == NfeXmlKind.NumberVoid));
    }

    [Fact]
    public async Task Sefaz_refusal_changes_nothing()
    {
        var (scenario, sefaz) = await RejectedAsync();
        sefaz.VoidNumberResponses.Enqueue(_ => new NfeVoidNumberResult(241, "Rejeição: Um número da faixa já foi utilizado"));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal("Inutilização recusada pela SEFAZ: 241 - Rejeição: Um número da faixa já foi utilizado", ex.Message);
        Assert.Equal(NfeStatus.Rejected, (await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync()).NfeStatus);
    }

    [Fact]
    public async Task No_answer_changes_nothing()
    {
        var (scenario, sefaz) = await RejectedAsync();
        sefaz.VoidNumberResponses.Enqueue(_ => throw new NfeCommunicationException("Tempo esgotado."));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal(NfeVoidNumberServiceBase<Domain.Entities.SalesInvoice>.NoAnswerMessage, ex.Message);
        Assert.Equal(NfeStatus.Rejected, (await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync()).NfeStatus);
    }

    [Fact]
    public async Task Unexpected_failure_changes_nothing_and_shows_the_detail()
    {
        var (scenario, sefaz) = await RejectedAsync();
        sefaz.VoidNumberResponses.Enqueue(_ => throw new FileNotFoundException("inutNFe_v4.00.xsd"));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.StartsWith(NfeVoidNumberServiceBase<Domain.Entities.SalesInvoice>.NoAnswerMessage, ex.Message);
        Assert.Contains("inutNFe_v4.00.xsd", ex.Message);
        Assert.Equal(NfeStatus.Rejected, (await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync()).NfeStatus);
    }

    [Theory]
    [InlineData("curta demais")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Justification_must_have_15_to_255_characters(string? justification)
    {
        var (scenario, sefaz) = await RejectedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, justification, "tester"));

        Assert.Equal("A justificativa deve ter entre 15 e 255 caracteres.", ex.Message);
        Assert.Empty(sefaz.VoidNumberRequests);
    }

    [Fact]
    public async Task Document_must_be_cancelled()
    {
        var (scenario, sefaz) = await RejectedAsync();
        (await scenario.Db.Context.SalesInvoices.SingleAsync()).InvoiceStatus = InvoiceStatus.Confirmed;
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal(NfeVoidNumberServiceBase<Domain.Entities.SalesInvoice>.NotCancelledMessage, ex.Message);
        Assert.Empty(sefaz.VoidNumberRequests);
    }

    [Theory]
    [InlineData(NfeStatus.None)]
    [InlineData(NfeStatus.Processing)]
    [InlineData(NfeStatus.Authorized)]
    [InlineData(NfeStatus.Denied)]
    [InlineData(NfeStatus.Cancelled)]
    public async Task Only_rejected_nfe_is_voided(NfeStatus status)
    {
        var (scenario, sefaz) = await RejectedAsync();
        (await scenario.Db.Context.SalesInvoices.SingleAsync()).NfeStatus = status;
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal(NfeVoidNumberServiceBase<Domain.Entities.SalesInvoice>.NotRejectedMessage, ex.Message);
        Assert.Empty(sefaz.VoidNumberRequests);
    }

    [Fact]
    public async Task Second_void_is_refused_before_sefaz()
    {
        var (scenario, sefaz) = await RejectedAsync();
        sefaz.VoidNumberResponses.Enqueue(FakeNfeSefazClient.NumberVoided);
        await SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal(NfeVoidNumberServiceBase<Domain.Entities.SalesInvoice>.AlreadyVoidedMessage, ex.Message);
        Assert.Single(sefaz.VoidNumberRequests);
    }

    [Fact]
    public async Task Document_without_number_is_refused()
    {
        var (scenario, sefaz) = await RejectedAsync();
        var invoice = await scenario.Db.Context.SalesInvoices.SingleAsync();
        invoice.TaxDocumentNumber = null;
        invoice.TaxDocumentSeries = null;
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal(NfeVoidNumberServiceBase<Domain.Entities.SalesInvoice>.NoNumberMessage, ex.Message);
        Assert.Empty(sefaz.VoidNumberRequests);
    }

    [Fact]
    public async Task Missing_document_is_not_found()
    {
        var (scenario, sefaz) = await RejectedAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            SalesVoid(scenario.Db, sefaz).ExecuteAsync(Guid.NewGuid(), Reason, "tester"));
    }
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoicesNfeVoidNumberServiceTests"`
Expected: FAIL de compilação (`SalesInvoicesNfeVoidNumberService` inexistente).

- [ ] **Step 4: Implementar a base**

`SiagroB1.Application/Services/Nfe/NfeVoidNumberServiceBase.cs` (`git add`):

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// "Inutilizar numeração" (spec 2026-10-08), comum aos documentos: a NF-e rejeitada de um documento já
/// cancelado nunca foi autorizada, e o número dela precisa ser inutilizado na SEFAZ (NfeInutilizacao4).
/// 102 grava o procInutNFe; 256/563 (já inutilizada) marcam sem comprovante; recusa e falta de resposta
/// não mudam o documento — reenviar é seguro.
/// </summary>
public abstract class NfeVoidNumberServiceBase<TDocument>(
    IUnitOfWork db,
    INfeDocumentStore<TDocument> store,
    BranchNfeSettingsService settingsService,
    INfeSefazClient sefaz,
    NfeNumberReservationService reservation,
    ILogger logger)
    where TDocument : class, INfeDocument
{
    public const string NoAnswerMessage = "Sem resposta da SEFAZ na inutilização: tente de novo.";
    public const string NotCancelledMessage = "Só o documento cancelado tem a numeração inutilizada: cancele o documento antes.";
    public const string NotRejectedMessage = "Só a NF-e rejeitada tem a numeração inutilizada.";
    public const string AlreadyVoidedMessage = "A numeração da NF-e deste documento já foi inutilizada.";
    public const string NoNumberMessage = "Este documento não tem número e série de NF-e para inutilizar.";

    public async Task<NfeIssueOutcomeDto> ExecuteAsync(Guid key, string? justification, string userName)
    {
        var reason = (justification ?? string.Empty).Trim();
        if (reason.Length is < NfeCancelServiceBase<TDocument>.MinJustificationLength
            or > NfeCancelServiceBase<TDocument>.MaxJustificationLength)
            throw new DefaultException("A justificativa deve ter entre 15 e 255 caracteres.");

        // A mesma trava da emissão, da consulta e do cancelamento.
        await using var emissionLock = await reservation.AcquireEmissionLockAsync(key);

        var invoice = await store.FindAsync(key) ?? throw new NotFoundException(store.NotFoundMessage);

        EnsureIssuer(invoice);

        if (invoice.NfeStatus == NfeStatus.Voided)
            throw new DefaultException(AlreadyVoidedMessage);

        if (invoice.InvoiceStatus != InvoiceStatus.Cancelled)
            throw new DefaultException(NotCancelledMessage);

        if (invoice.NfeStatus != NfeStatus.Rejected)
            throw new DefaultException(NotRejectedMessage);

        if (!int.TryParse(invoice.TaxDocumentNumber, out var number) || !int.TryParse(invoice.TaxDocumentSeries, out var series))
            throw new DefaultException(NoNumberMessage);

        var branch = await db.Context.Branchs.AsNoTracking().FirstOrDefaultAsync(b => b.Code == invoice.BranchCode)
                     ?? throw new DefaultException($"A filial {invoice.BranchCode} do documento não existe.");

        // Ano da tentativa que foi à SEFAZ (AA da chave); a rejeição local não tem chave: ano corrente de Brasília.
        var year = invoice.ChaveNFe is { Length: 44 } accessKey
            ? int.Parse(accessKey.Substring(2, 2), CultureInfo.InvariantCulture)
            : TimeZoneInfo.ConvertTime(DateTimeOffset.Now, NfeIssueInputAssembler.BrasiliaZone).Year % 100;

        // Ambiente da EMISSÃO; nulo (rejeição local) = o atual da filial.
        using var service = await settingsService.OpenAsync(invoice.BranchCode!, invoice.NfeEnvironment);

        var request = new NfeVoidNumberRequest(year, NfeText.Digits(branch.TaxId), series, number, reason);

        NfeVoidNumberResult result;
        try
        {
            result = await sefaz.VoidNumberAsync(request, service.Settings);
        }
        catch (NfeCommunicationException)
        {
            throw new DefaultException(NoAnswerMessage);
        }
        catch (Exception e) when (e is not DefaultException)
        {
            logger.LogError(e, "Falha inesperada ao inutilizar a numeração da NF-e do documento {InvoiceKey}.", key);
            throw new DefaultException(NfeStatusText.Truncate($"{NoAnswerMessage} (detalhe técnico: {e.Message})"));
        }

        var homologated = result.StatusCode == NfeStatusCodes.NumberVoided;
        if (!homologated && !NfeStatusCodes.IsNumberAlreadyVoided(result.StatusCode))
            throw new DefaultException($"Inutilização recusada pela SEFAZ: {result.StatusCode} - {result.Reason}");

        if (homologated && result.ProcXml is not null)
            store.AddXml(invoice, NfeXmlKind.NumberVoid, result.ProcXml);

        invoice.NfeStatus = NfeStatus.Voided;
        invoice.NfeStatusCode = result.StatusCode.ToString(CultureInfo.InvariantCulture);
        invoice.NfeStatusReason = NfeStatusText.Truncate(result.Reason);
        if (result.Protocol is not null)
            invoice.NfeProtocol = result.Protocol;

        await db.SaveChangesAsync();

        logger.LogInformation("Numeração {Series}/{Number} inutilizada ({StatusCode}) por {User}.",
            series, number, result.StatusCode, userName);

        return NfeIssueOutcomeDto.From(invoice);
    }

    /// <summary>Quem emitiu a nota: a entrada de terceiro não tem numeração do Siagro.</summary>
    protected virtual void EnsureIssuer(TDocument document) { }
}
```

> Confirme os nomes reais: `db.Context.Branchs` e `Branch.Code`/`Branch.TaxId` (vistos no `NfeTestSeed`); `NfeText` está em `SiagroB1.Fiscal.Nfe` (`NfeText.Digits(string?)`). `NfeCancelServiceBase<TDocument>.MinJustificationLength` é `const` genérico — se o compilador recusar o acesso via tipo genérico aberto, declare `private const int MinLength = 15, MaxLength = 255;` na própria base.

- [ ] **Step 5: Implementar a classe fina**

`SiagroB1.Application/Services/Nfe/SalesInvoicesNfeVoidNumberService.cs` (`git add`):

```csharp
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>"Inutilizar numeração" do documento de saída (venda e devolução própria).</summary>
public class SalesInvoicesNfeVoidNumberService(
    IUnitOfWork db,
    BranchNfeSettingsService settingsService,
    INfeSefazClient sefaz,
    NfeNumberReservationService reservation,
    ILogger<SalesInvoicesNfeVoidNumberService> logger)
    : NfeVoidNumberServiceBase<SalesInvoice>(db, new SalesInvoiceNfeStore(db), settingsService, sefaz, reservation, logger);
```

- [ ] **Step 6: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoicesNfeVoidNumberServiceTests"` → PASS.
Run: `dotnet test SiagroB1.Application.Tests` → PASS.

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Application SiagroB1.Application.Tests
git commit -m "feat(invoice): inutiliza a numeração da NF-e rejeitada do documento de saída" -m "A NF-e rejeitada de um documento cancelado nunca foi autorizada e o número precisa ser inutilizado na SEFAZ até o dia 10 do mês seguinte. 102 guarda o procInutNFe; 256/563 (já inutilizada) marcam sem comprovante; recusa e falta de resposta não mudam nada." -m "Atenção: a rejeição na validação local não tem chave nem ambiente — o ano é o corrente de Brasília e o ambiente o atual da filial." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Application — entrada própria

**Files:**
- Create: `SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeVoidNumberService.cs`
- Modify: `SiagroB1.Application.Tests/Support/NfeVoidNumberTestServices.cs`
- Test: `SiagroB1.Application.Tests/Nfe/PurchaseInvoicesNfeVoidNumberServiceTests.cs`

**Interfaces:**
- Consumes: Task 3 (`NfeVoidNumberServiceBase<TDocument>`, `RejectAndCancelPurchaseAsync`, `Settings`).
- Produces: `PurchaseInvoicesNfeVoidNumberService(IUnitOfWork db, BranchNfeSettingsService settingsService, INfeSefazClient sefaz, NfeNumberReservationService reservation, ILogger<PurchaseInvoicesNfeVoidNumberService> logger)`; `NfeVoidNumberTestServices.PurchaseVoid(UnitOfWork db, FakeNfeSefazClient sefaz)`; mensagem `PurchaseInvoicesNfeVoidNumberService.ThirdPartyMessage`.

- [ ] **Step 1: Suporte** — em `NfeVoidNumberTestServices`:

```csharp
    public static PurchaseInvoicesNfeVoidNumberService PurchaseVoid(UnitOfWork db, FakeNfeSefazClient sefaz) =>
        new(db, Settings(db, sefaz), sefaz, new FakeNfeNumberReservationService(),
            NullLogger<PurchaseInvoicesNfeVoidNumberService>.Instance);
```

- [ ] **Step 2: Testes**

`SiagroB1.Application.Tests/Nfe/PurchaseInvoicesNfeVoidNumberServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Support.NfeVoidNumberTestServices;

namespace SiagroB1.Application.Tests.Nfe;

public class PurchaseInvoicesNfeVoidNumberServiceTests
{
    private const string Reason = "Entrada propria rejeitada e cancelada";

    [Fact]
    public async Task Own_entry_with_rejected_nfe_is_voided()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await RejectAndCancelPurchaseAsync(scenario.Db, scenario.InvoiceKey);
        var sefaz = new FakeNfeSefazClient();
        sefaz.VoidNumberResponses.Enqueue(FakeNfeSefazClient.NumberVoided);

        var outcome = await PurchaseVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        Assert.Equal(NfeStatus.Voided, outcome.NfeStatus);
        var saved = await scenario.Db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(x => x.Key == scenario.InvoiceKey);
        Assert.Equal(FakeNfeSefazClient.VoidNumberProtocol, saved.NfeProtocol);
        Assert.True(await scenario.Db.Context.PurchaseInvoiceNfeXmls.AnyAsync(x =>
            x.PurchaseInvoiceKey == scenario.InvoiceKey && x.Kind == NfeXmlKind.NumberVoid));
        Assert.Equal(233, Assert.Single(sefaz.VoidNumberRequests).Number);
    }

    [Fact]
    public async Task Third_party_entry_is_not_voided()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await RejectAndCancelPurchaseAsync(scenario.Db, scenario.InvoiceKey);
        (await scenario.Db.Context.PurchaseInvoices.SingleAsync(x => x.Key == scenario.InvoiceKey)).IssuerType = DocumentIssuerType.ThirdParty;
        await scenario.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal(PurchaseInvoicesNfeVoidNumberService.ThirdPartyMessage, ex.Message);
        Assert.Empty(sefaz.VoidNumberRequests);
    }
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoicesNfeVoidNumberServiceTests"` → FAIL de compilação.

- [ ] **Step 4: Implementar**

`SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeVoidNumberService.cs` (`git add`):

```csharp
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>"Inutilizar numeração" do documento de entrada própria.</summary>
public class PurchaseInvoicesNfeVoidNumberService(
    IUnitOfWork db,
    BranchNfeSettingsService settingsService,
    INfeSefazClient sefaz,
    NfeNumberReservationService reservation,
    ILogger<PurchaseInvoicesNfeVoidNumberService> logger)
    : NfeVoidNumberServiceBase<PurchaseInvoice>(db, new PurchaseInvoiceNfeStore(db), settingsService, sefaz, reservation, logger)
{
    public const string ThirdPartyMessage = "NF-e de terceiro não tem numeração do Siagro para inutilizar.";

    protected override void EnsureIssuer(PurchaseInvoice document)
    {
        if (document.IssuerType != DocumentIssuerType.Own)
            throw new DefaultException(ThirdPartyMessage);
    }
}
```

- [ ] **Step 5: Rodar** — `dotnet test SiagroB1.Application.Tests` → PASS.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Application SiagroB1.Application.Tests
git commit -m "feat(invoice): inutiliza a numeração da NF-e rejeitada da entrada própria" -m "A entrada própria divide a numeração da filial com a saída e deixa o mesmo furo quando a NF-e é rejeitada e o documento cancelado. A entrada de terceiro não tem número do Siagro." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Download, controllers, OData e DI

**Files:**
- Create: `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeVoidNumberXmlDownloadService.cs`, `PurchaseInvoicesNfeVoidNumberXmlDownloadService.cs`
- Create: `SiagroB1.Web/Actions/Nfe/SalesInvoicesVoidNfeNumberController.cs`, `PurchaseInvoicesVoidNfeNumberController.cs`
- Create: `SiagroB1.Web/Functions/Nfe/SalesInvoicesNfeVoidNumberXmlController.cs`, `PurchaseInvoicesNfeVoidNumberXmlController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (bloco do cancelamento ~linha 114 e das functions ~linha 1484), `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (~linha 371)
- Test: `SiagroB1.Application.Tests/Nfe/NfeVoidNumberXmlDownloadTests.cs`, `SiagroB1.Application.Tests/Nfe/NfeVoidNumberEdmModelTests.cs`

**Interfaces:**
- Consumes: Tasks 3–4.
- Produces (rotas que o frontend chama): `POST /odata/SalesInvoicesVoidNfeNumber` e `POST /odata/PurchaseInvoicesVoidNfeNumber` com `{ Key, Justification }` → `NfeIssueOutcomeDto`; `GET /odata/SalesInvoicesNfeVoidNumberXml(Key=…)` e `GET /odata/PurchaseInvoicesNfeVoidNumberXml(Key=…)` → `application/xml`, arquivo `<serie>-<numero>-procInutNFe.xml`; 404 com "Esta inutilização não tem comprovante.".

- [ ] **Step 1: Testes**

`SiagroB1.Application.Tests/Nfe/NfeVoidNumberXmlDownloadTests.cs`:

```csharp
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Support.NfeVoidNumberTestServices;

namespace SiagroB1.Application.Tests.Nfe;

public class NfeVoidNumberXmlDownloadTests
{
    [Fact]
    public async Task Void_number_xml_is_downloaded_with_series_and_number()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await RejectAndCancelSaleAsync(scenario.Db, scenario.InvoiceKey);
        var sefaz = new FakeNfeSefazClient();
        sefaz.VoidNumberResponses.Enqueue(FakeNfeSefazClient.NumberVoided);
        await SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, "NF-e rejeitada e documento cancelado", "tester");

        var (bytes, fileName) = await new SalesInvoicesNfeVoidNumberXmlDownloadService(scenario.Db).ExecuteAsync(scenario.InvoiceKey);

        Assert.Equal("1-000000233-procInutNFe.xml", fileName);
        Assert.Contains("<procInutNFe", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task Void_without_proc_xml_is_not_found()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            new SalesInvoicesNfeVoidNumberXmlDownloadService(scenario.Db).ExecuteAsync(scenario.InvoiceKey));

        Assert.Equal("Esta inutilização não tem comprovante.", ex.Message);
    }

    [Fact]
    public async Task Purchase_void_number_xml_is_downloaded()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await RejectAndCancelPurchaseAsync(scenario.Db, scenario.InvoiceKey);
        var sefaz = new FakeNfeSefazClient();
        sefaz.VoidNumberResponses.Enqueue(FakeNfeSefazClient.NumberVoided);
        await PurchaseVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, "Entrada propria rejeitada e cancelada", "tester");

        var (_, fileName) = await new PurchaseInvoicesNfeVoidNumberXmlDownloadService(scenario.Db).ExecuteAsync(scenario.InvoiceKey);

        Assert.Equal("1-000000233-procInutNFe.xml", fileName);
    }
}
```

`SiagroB1.Application.Tests/Nfe/NfeVoidNumberEdmModelTests.cs`:

```csharp
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Nfe;

public class NfeVoidNumberEdmModelTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("SalesInvoicesVoidNfeNumber")]
    [InlineData("PurchaseInvoicesVoidNfeNumber")]
    public void Void_number_takes_key_and_justification(string name)
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == name);

        Assert.Equal("Edm.Guid", action.Parameters.Single(p => p.Name == "Key").Type.FullName());
        Assert.Equal("Edm.String", action.Parameters.Single(p => p.Name == "Justification").Type.FullName());
        Assert.EndsWith("NfeIssueOutcomeDto", action.ReturnType.FullName());
    }

    [Theory]
    [InlineData("SalesInvoicesNfeVoidNumberXml")]
    [InlineData("PurchaseInvoicesNfeVoidNumberXml")]
    public void Void_number_xml_is_a_function_with_the_key(string name)
    {
        var function = Model().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == name);

        Assert.Equal("Edm.Guid", function.Parameters.Single(p => p.Name == "Key").Type.FullName());
    }
}
```

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeVoidNumberXml|FullyQualifiedName~NfeVoidNumberEdm"` → FAIL.

- [ ] **Step 3: Downloads** (`git add` cada arquivo)

`SalesInvoicesNfeVoidNumberXmlDownloadService.cs`:

```csharp
using System.Text;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Comprovante da inutilização (procInutNFe) do documento de saída: <c>&lt;serie&gt;-&lt;numero&gt;-procInutNFe.xml</c>.</summary>
public class SalesInvoicesNfeVoidNumberXmlDownloadService(IUnitOfWork db)
{
    public async Task<(byte[] Bytes, string FileName)> ExecuteAsync(Guid invoiceKey)
    {
        var store = new SalesInvoiceNfeStore(db);
        var xml = await store.LatestXmlAsync(invoiceKey, NfeXmlKind.NumberVoid)
                  ?? throw new NotFoundException("Esta inutilização não tem comprovante.");
        var invoice = await store.FindReadOnlyAsync(invoiceKey);

        return (Encoding.UTF8.GetBytes(xml), $"{invoice?.TaxDocumentSeries}-{invoice?.TaxDocumentNumber}-procInutNFe.xml");
    }
}
```

`PurchaseInvoicesNfeVoidNumberXmlDownloadService.cs`: idêntico, com `PurchaseInvoiceNfeStore` e o summary "do documento de entrada".

- [ ] **Step 4: Controllers** (`git add` cada arquivo)

`SiagroB1.Web/Actions/Nfe/SalesInvoicesVoidNfeNumberController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Nfe;

public class SalesInvoicesVoidNfeNumberController(SalesInvoicesNfeVoidNumberService service) : ODataController
{
    [HttpPost("odata/SalesInvoicesVoidNfeNumber")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || !Guid.TryParse(keyObj?.ToString(), out var key))
            return BadRequest("Parâmetro obrigatório: Key.");

        // Parâmetro string ausente/nulo: não chamar ToString() sobre nulo.
        parameters.TryGetValue("Justification", out var justificationObj);

        try
        {
            return Ok(await service.ExecuteAsync(key, justificationObj as string, User.Identity?.Name ?? "Unknown"));
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
```

`PurchaseInvoicesVoidNfeNumberController.cs`: idêntico, com `PurchaseInvoicesNfeVoidNumberService` e a rota `odata/PurchaseInvoicesVoidNfeNumber`.

`SiagroB1.Web/Functions/Nfe/SalesInvoicesNfeVoidNumberXmlController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.Nfe;

public class SalesInvoicesNfeVoidNumberXmlController(SalesInvoicesNfeVoidNumberXmlDownloadService service) : ODataController
{
    [HttpGet("odata/SalesInvoicesNfeVoidNumberXml(Key={key})")]
    public async Task<ActionResult> Download([FromRoute] Guid key)
    {
        try
        {
            var (bytes, fileName) = await service.ExecuteAsync(key);
            return File(bytes, "application/xml", fileName);
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
    }
}
```

`PurchaseInvoicesNfeVoidNumberXmlController.cs`: idêntico, com `PurchaseInvoicesNfeVoidNumberXmlDownloadService` e `odata/PurchaseInvoicesNfeVoidNumberXml(Key={key})`.

- [ ] **Step 5: Registro**

`ODataConfigurations.cs`, dentro do `foreach (var prefix in new[] { "SalesInvoices", "PurchaseInvoices" })` das actions de cancelamento (~linha 115), depois de `completeCancellation`:

```csharp
            // Inutilização da numeração (spec 2026-10-08): NF-e rejeitada de documento cancelado.
            var voidNumber = modelBuilder.Action($"{prefix}VoidNfeNumber");
            voidNumber.Parameter<Guid>("Key");
            voidNumber.Parameter<string>("Justification");
            voidNumber.Returns<NfeIssueOutcomeDto>();
```

e no `foreach` das functions de CC-e (~linha 1488), depois de `correctionXml`:

```csharp
            var voidNumberXml = modelBuilder.Function($"{prefix}NfeVoidNumberXml");
            voidNumberXml.Parameter<Guid>("Key");
            voidNumberXml.Returns<IActionResult>();
```

`ServiceCollectionExtensions.cs`, junto dos serviços de cancelamento (~linha 371):

```csharp
        services.AddScoped<SalesInvoicesNfeVoidNumberService>();
        services.AddScoped<SalesInvoicesNfeVoidNumberXmlDownloadService>();
        services.AddScoped<PurchaseInvoicesNfeVoidNumberService>();
        services.AddScoped<PurchaseInvoicesNfeVoidNumberXmlDownloadService>();
```

- [ ] **Step 6: Rodar**

Run: `dotnet build SiagroB1.sln` → sem erro.
Run: `dotnet test SiagroB1.Application.Tests` e `dotnet test SiagroB1.Fiscal.Tests` → PASS.

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Application SiagroB1.Web SiagroB1.Application.Tests
git commit -m "feat(invoice): expõe a inutilização da numeração e o download do comprovante" -m "Actions SalesInvoicesVoidNfeNumber/PurchaseInvoicesVoidNfeNumber e functions do procInutNFe, no mesmo molde do cancelamento da NF-e (mesma permissão)." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: Frontend — regra, desfecho e situação

**Files:**
- Modify: `webapp/helpers/NfeHelpers.ts`, `webapp/model/formatter.ts:920-940`
- Test: `webapp/test/unit/helpers/NfeHelpers.qunit.ts`

**Interfaces:**
- Produces: `canVoidNfeNumber(nfeStatus?: string, invoiceStatus?: string, issuerType?: string): boolean`; `nfeOutcomeMessage` trata `NfeStatus === "Voided"`.

- [ ] **Step 1: Branch**

```bash
cd /c/Projetos/SiagroB1/siagro-b1-frontend && git status --short && git switch main && git switch -c feature/nfe-number-void
```

- [ ] **Step 2: QUnit** — no `import` do topo de `NfeHelpers.qunit.ts`, acrescente `canVoidNfeNumber`; no fim do arquivo:

```ts
QUnit.module("NfeHelpers - inutilização da numeração");

QUnit.test("só documento cancelado com NF-e rejeitada (e entrada própria)", function (assert) {
	assert.strictEqual(canVoidNfeNumber("Rejected", "Cancelled"), true);
	assert.strictEqual(canVoidNfeNumber("Rejected", "Cancelled", "Own"), true);
	assert.strictEqual(canVoidNfeNumber("Rejected", "Cancelled", "ThirdParty"), false);
	assert.strictEqual(canVoidNfeNumber("Rejected", "Confirmed"), false);
	assert.strictEqual(canVoidNfeNumber("Processing", "Cancelled"), false);
	assert.strictEqual(canVoidNfeNumber("Denied", "Cancelled"), false);
	assert.strictEqual(canVoidNfeNumber("Voided", "Cancelled"), false);
	assert.strictEqual(canVoidNfeNumber(undefined, undefined), false);
});

QUnit.test("desfecho da inutilização: 102 é sucesso, 256/563 é aviso sem comprovante", function (assert) {
	assert.deepEqual(nfeOutcomeMessage({ NfeStatus: "Voided", StatusCode: "102", Reason: "Inutilização de número homologado" }),
		{ type: "success", text: "Numeração da NF-e inutilizada." });
	assert.deepEqual(nfeOutcomeMessage({ NfeStatus: "Voided", StatusCode: "256", Reason: "Rejeição: já inutilizada" }),
		{ type: "warning", text: "Numeração já inutilizada na SEFAZ (256 - Rejeição: já inutilizada): o comprovante não está disponível." });
});
```

- [ ] **Step 3: Rodar e ver falhar**

Com `npx ui5 serve --port 8080` de pé (pare antes qualquer dev server na 8080):
`npx ui5-test-runner --url "http://localhost:8080/test/Test.qunit.html?testsuite=test-resources/siagrob1/testsuite.qunit&test=unit/unitTests" --report-dir C:/Users/Penalva/AppData/Local/Temp/claude/C--Projetos-SiagroB1/53dd81e9-7f81-4a8b-964f-da349b9a5a43/scratchpad/qunit`
Expected: FAIL (`canVoidNfeNumber` não exportado / desfecho cai no `default`).

- [ ] **Step 4: Implementar**

`NfeHelpers.ts`, no `switch` de `baseNfeOutcomeMessage`, antes do `default`:

```ts
    case "Voided":
      return outcome.StatusCode === "102"
        ? { type: "success", text: "Numeração da NF-e inutilizada." }
        : {
          type: "warning",
          text: `Numeração já inutilizada na SEFAZ (${codeAndReason}): o comprovante não está disponível.`,
        };
```

Depois de `needsNfeCancellationCompletion`:

```ts
/**
 * "Inutilizar numeração": a NF-e rejeitada de um documento já cancelado nunca foi autorizada e o número
 * fica sem uso. Entrada de terceiro (issuerType "ThirdParty") não tem número do Siagro; a saída não informa issuerType.
 */
export function canVoidNfeNumber(nfeStatus?: string, invoiceStatus?: string, issuerType?: string): boolean {
  return invoiceStatus === "Cancelled" && nfeStatus === "Rejected" && (issuerType === undefined || issuerType === "Own");
}
```

`formatter.ts`: em `formatNfeStatus` acrescente `["Voided", "Inutilizada"],`; em `stateNfeStatus`, `["Voided", "None"],`.

- [ ] **Step 5: Rodar e ver passar** — mesmo comando do Step 3 → todos verdes. Depois `yarn ts-typecheck` e `yarn lint` limpos.

- [ ] **Step 6: Commit**

```bash
git add webapp/helpers/NfeHelpers.ts webapp/model/formatter.ts webapp/test/unit/helpers/NfeHelpers.qunit.ts
git commit -m "feat(invoice): regra e desfecho da inutilização da numeração da NF-e" -m "Situação Inutilizada e a regra de quando o documento pode ter a numeração inutilizada: cancelado, NF-e rejeitada, e na entrada só a própria." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: Frontend — diálogo, botões e rotas

**Files:**
- Modify: `webapp/dialogs/NfeCancelDialog.ts`
- Create: `webapp/dialogs/NfeVoidNumberAction.ts`
- Modify: `webapp/model/ServerRoutes.ts:186-191`
- Modify: `webapp/view/salesInvoices/Detail.view.xml:54-55`, `webapp/controller/salesInvoices/Detail.controller.ts`
- Modify: `webapp/view/purchaseInvoices/Detail.view.xml:122-123`, `webapp/controller/purchaseInvoices/Detail.controller.ts`
- Modify: `webapp/view/salesInvoices/Main.view.xml:65`, `webapp/controller/salesInvoices/Main.controller.ts`
- Modify: `webapp/view/purchaseInvoices/Main.view.xml:60`, `webapp/controller/purchaseInvoices/Main.controller.ts`

**Interfaces:**
- Consumes: Task 5 (rotas), Task 6 (`canVoidNfeNumber`, desfecho).
- Produces: `openNfeCancelDialog(owner: Control, texts?: NfeJustificationTexts): Promise<string>`; `runNfeVoidNumber(owner: Control, url: string, key: string, setBusy: (busy: boolean) => void): Promise<boolean>`.

- [ ] **Step 1: Diálogo com textos opcionais** — em `NfeCancelDialog.ts`:

```ts
/** Textos do diálogo de justificativa; o padrão é o do cancelamento. */
export type NfeJustificationTexts = { title: string; confirm: string; warning: string };

const CANCEL_TEXTS: NfeJustificationTexts = {
  title: "Cancelar NF-e",
  confirm: "Cancelar NF-e",
  warning: "O cancelamento é enviado à SEFAZ e não pode ser desfeito. O documento será cancelado e os saldos estornados.",
};

export function openNfeCancelDialog(owner: Control, texts: NfeJustificationTexts = CANCEL_TEXTS): Promise<string> {
```

e troque os três literais do corpo por `texts.confirm` (texto do `Button`), `texts.title` (título do `Dialog`) e `texts.warning` (texto do `MessageStrip`). As chamadas existentes não mudam.

- [ ] **Step 2: Ação comum** — `webapp/dialogs/NfeVoidNumberAction.ts` (`git add`):

```ts
import MessageBox from "sap/m/MessageBox";
import MessageToast from "sap/m/MessageToast";
import Control from "sap/ui/core/Control";
import { openNfeCancelDialog } from "siagrob1/dialogs/NfeCancelDialog";
import { nfeOutcomeMessage, NfeOutcome } from "siagrob1/helpers/NfeHelpers";
import { sendJson, odataValue } from "siagrob1/helpers/FetchHelpers";

const VOID_TEXTS = {
  title: "Inutilizar numeração da NF-e",
  confirm: "Inutilizar",
  warning: "A inutilização é enviada à SEFAZ e não pode ser desfeita: o número desta NF-e rejeitada não poderá mais ser usado.",
};

/**
 * "Inutilizar numeração" das quatro telas (Detail e lista, saída e entrada): pede a justificativa, envia e
 * mostra o desfecho. Devolve true quando o pedido chegou ao servidor (a tela relê o documento).
 */
export async function runNfeVoidNumber(
  owner: Control, url: string, key: string, setBusy: (busy: boolean) => void
): Promise<boolean> {
  const justification = await openNfeCancelDialog(owner, VOID_TEXTS);
  if (justification === null) {
    return false;
  }

  setBusy(true);
  try {
    const result = await sendJson("POST", url, { Key: key, Justification: justification });

    if (!result.ok) {
      MessageBox.error(result.message);
      return true;
    }

    const message = nfeOutcomeMessage(odataValue<NfeOutcome>(result.data));
    if (message.type === "success") {
      MessageToast.show(message.text);
    } else {
      MessageBox.warning(message.text);
    }
    return true;
  } finally {
    setBusy(false);
  }
}
```

> Confirme de onde `sendJson`/`odataValue` são importados hoje no `salesInvoices/Detail.controller.ts` e use o mesmo módulo.

- [ ] **Step 3: Rotas** — em `ServerRoutes.ts`, depois de `purchaseInvoicesNfeCancellationXml`:

```ts
  salesInvoicesVoidNfeNumber: '/odata/SalesInvoicesVoidNfeNumber',
  salesInvoicesNfeVoidNumberXml: '/odata/SalesInvoicesNfeVoidNumberXml',
  purchaseInvoicesVoidNfeNumber: '/odata/PurchaseInvoicesVoidNfeNumber',
  purchaseInvoicesNfeVoidNumberXml: '/odata/PurchaseInvoicesNfeVoidNumberXml',
```

- [ ] **Step 4: Detail da saída** — `Detail.view.xml`, logo depois do botão "XML do cancelamento":

```xml
          <Button text="Inutilizar numeração" icon="sap-icon://locked" press=".onVoidNfeNumber"
            visible="{= ${path: 'InvoiceStatus', targetType: 'any'} === 'Cancelled' &amp;&amp; ${path: 'NfeStatus', targetType: 'any'} === 'Rejected' }" />
          <Button text="XML da inutilização" icon="sap-icon://download" press=".onNfeVoidNumberXml"
            visible="{= ${path: 'NfeStatus', targetType: 'any'} === 'Voided' &amp;&amp; ${NfeStatusCode} === '102' }" />
```

`Detail.controller.ts` (importar `runNfeVoidNumber` de `siagrob1/dialogs/NfeVoidNumberAction`), depois de `onNfeCancellationXml`:

```ts
  async onVoidNfeNumber() {
    const ctx = this.getView().getBindingContext() as Context;
    if (!ctx) {
      return;
    }

    if (await runNfeVoidNumber(this.getView(), ServerRoutes.salesInvoicesVoidNfeNumber,
      ctx.getProperty("Key") as string, (busy) => this.setBusy(busy))) {
      try {
        await ctx.requestRefresh();
      } catch {
        // a releitura falhar não pode travar a tela
      }
    }
  }

  async onNfeVoidNumberXml() {
    const ctx = this.getView().getBindingContext() as Context;
    await this.downloadNfeXml(`${ServerRoutes.salesInvoicesNfeVoidNumberXml}(Key=${ctx.getProperty("Key") as string})`,
      `${ctx.getProperty("TaxDocumentSeries") as string}-${ctx.getProperty("TaxDocumentNumber") as string}-procInutNFe.xml`);
  }
```

- [ ] **Step 5: Detail da entrada** — `purchaseInvoices/Detail.view.xml`, depois do "XML do cancelamento" (mesma indentação dos vizinhos):

```xml
        <Button text="Inutilizar numeração" icon="sap-icon://locked"
                visible="{= ${path: 'InvoiceStatus', targetType: 'any'} === 'Cancelled' &amp;&amp; ${path: 'NfeStatus', targetType: 'any'} === 'Rejected' &amp;&amp; ${path: 'IssuerType', targetType: 'any'} === 'Own' }"
                press=".onVoidNfeNumber" />
        <Button text="XML da inutilização" icon="sap-icon://download"
                visible="{= ${path: 'NfeStatus', targetType: 'any'} === 'Voided' &amp;&amp; ${NfeStatusCode} === '102' }"
                press=".onNfeVoidNumberXml" />
```

`purchaseInvoices/Detail.controller.ts`: os mesmos dois handlers do Step 4, trocando para `ServerRoutes.purchaseInvoicesVoidNfeNumber` e `ServerRoutes.purchaseInvoicesNfeVoidNumberXml` (o `downloadNfeXml` privado já existe nesse controller, linha ~252).

- [ ] **Step 6: Listas**

`salesInvoices/Main.view.xml`, depois de `Cancelar Doc.`:

```xml
          	<Button type="Transparent" text="Inutilizar numeração" press=".onVoidNfeNumber"/>
```

`salesInvoices/Main.controller.ts` (importar `runNfeVoidNumber` e `canVoidNfeNumber`):

```ts
  async onVoidNfeNumber() {
    const table = this.byId("salesInvoicesTable") as Table;
    const selected = table.getSelectedIndices();
    if (selected.length !== 1) {
      MessageBox.warning("Selecione um registro.");
      return;
    }

    const ctx = table.getContextByIndex(selected[0]) as Context;
    if (!canVoidNfeNumber(ctx.getProperty("NfeStatus") as string, ctx.getProperty("InvoiceStatus") as string)) {
      MessageBox.warning("Só o documento cancelado com NF-e rejeitada tem a numeração inutilizada.");
      return;
    }

    if (await runNfeVoidNumber(this.getView(), ServerRoutes.salesInvoicesVoidNfeNumber,
      ctx.getProperty("Key") as string, (busy) => this.setBusy(busy))) {
      this.refreshData();
    }
  }
```

> Use o MESMO id de tabela e o mesmo jeito de pegar a seleção que o `onCancel` desta tela usa (linhas ~190–207); o `"salesInvoicesTable"` acima é ilustrativo.

`purchaseInvoices/Main.view.xml`, depois de `Cancelar Documento`:

```xml
						<Button type="Transparent" text="Inutilizar numeração" press=".onVoidNfeNumber"/>
```

`purchaseInvoices/Main.controller.ts`:

```ts
  async onVoidNfeNumber() {
    const ctx = this.selectedContext();
    if (!ctx) {
      return;
    }

    if (!canVoidNfeNumber(ctx.getProperty("NfeStatus") as string, ctx.getProperty("InvoiceStatus") as string,
      ctx.getProperty("IssuerType") as string)) {
      MessageBox.warning("Só o documento cancelado com NF-e própria rejeitada tem a numeração inutilizada.");
      return;
    }

    if (await runNfeVoidNumber(this.getView(), ServerRoutes.purchaseInvoicesVoidNfeNumber,
      ctx.getProperty("Key") as string, (busy) => this.setBusy(busy))) {
      this.onRefresh();
    }
  }
```

(As duas listas já trazem `NfeStatus`, `InvoiceStatus` e — na entrada — `IssuerType` no `$select`.)

- [ ] **Step 7: Gates** — `yarn ts-typecheck`, `yarn lint` e o QUnit do Task 6 Step 3: limpos/verdes.

- [ ] **Step 8: Commit**

```bash
git add webapp
git commit -m "feat(invoice): ação de inutilizar a numeração da NF-e rejeitada" -m "Botão no Detail e na lista do documento de saída e de entrada, com o diálogo de justificativa do cancelamento, e o download do comprovante (procInutNFe)." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: Verificação de ponta a ponta em homologação

Sem código novo. Ambiente local CEAGUI (memória `run-stack-locally`): backend `feature/nfe-number-void` com o profile `ceagui` (Gateway primeiro, depois Web), frontend `yarn start:dev`, login `admin/1234`, banco `CEAGUI_SIAGRO_DEV` (filial 01, homologação, série 9). **Navegar pelo menu**, não por URL com hash. **Derrubar a stack no fim** (matar por porta 50000/5246/8080 e conferir de novo).

- [ ] **Step 1: Provocar uma rejeição na SEFAZ (saída).** O cliente de teste é `CTESTE01`. Guarde a IE atual e troque por uma de dígito inválido:

```bash
sqlcmd -S localhost -E -d CEAGUI_SIAGRO_DEV -Q "SELECT CardCode, StateRegistration FROM <tabela de parceiro/endereço onde a IE do CTESTE01 está>"
```

(localize a coluna com `grep -rn "StateRegistration" SiagroB1.Domain/Entities` — no STANDALONE o parceiro é local). Troque para `111111111111`, crie um documento de saída igual ao DS000166 (o roteiro da sessão: GET do DS000165 com `$expand=Items`, POST sem chaves/totais, `SalesInvoicesConfirm`, `SalesInvoicesIssueNfe`) e confira `NfeStatus = Rejected` com cStat de IE do destinatário. **Restaure a IE.**

- [ ] **Step 2: Cancelar e inutilizar pela tela.** Documento de Saída → Detail do documento rejeitado → Cancelar → botão "Inutilizar numeração" aparece → justificativa ≥ 15 → esperado: toast "Numeração da NF-e inutilizada.", situação "Inutilizada", botão "XML da inutilização" baixa `9-0000000NN-procInutNFe.xml` com `<procInutNFe` e `<cStat>102</cStat>`.
  - Se o comprovante não vier (`ProcXml` nulo com 102), o `EnvioStr` da Zeus não é o `inutNFe` puro: corrija no `ZeusNfeSefazClient` (extrair o nó `inutNFe`), com teste no mapper, e repita.

- [ ] **Step 3: Reenvio (256/563).** Volte o documento para rejeitado (`UPDATE SALES_INVOICES SET NfeStatus = 3 WHERE [Key] = '<key>'`) e inutilize de novo pela tela → esperado: aviso "Numeração já inutilizada na SEFAZ (256 …)" ou (563 …), situação "Inutilizada", sem botão de XML novo (o comprovante do Step 2 continua lá).

- [ ] **Step 4: Entrada própria.** Repita 1–2 com uma entrada própria (fornecedor de teste `FTESTE01`, mesma troca de IE e restauração) pela tela de Documento de Entrada, inclusive a ação da lista.

- [ ] **Step 5: Lista.** Na lista de saída, selecione um documento NÃO elegível e clique "Inutilizar numeração" → aviso, nada enviado.

- [ ] **Step 6: Derrubar a stack** e conferir as portas livres.

- [ ] **Step 7: Registrar** na memória do projeto (arquivo novo `nfe-number-void-feature.md` + linha no `MEMORY.md`): branches e commits, números de homologação usados, o que o `EnvioStr` contém, e o pendente — merge, deploy (Web + Gateway com o frontend; o `main` do frontend já tem commits além do `5721402` que está na VPS) e a inutilização da 233 em produção pelo usuário.
