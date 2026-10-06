# Carta de Correção Eletrônica (CC-e) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enviar a CC-e (evento 110110) sobre a NF-e autorizada do documento de saída e da entrada própria, guardar
cada carta registrada num histórico por documento, importar as cartas que faltarem na consulta, e baixar o XML e
o PDF de cada carta.

**Architecture:** Molde do cancelamento (2b): `INfeSefazClient` ganha `SendCorrectionAsync` (Zeus
`RecepcaoEventoCartaCorrecao`), e o mapper passa a extrair as CC-e do `retConsSitNFe`. A Application tem um
serviço genérico `NfeCorrectionServiceBase<T>` (trava `nfe:{key}`, sequência = máx + 1) e um importador comum
usado pelo serviço (resposta 573) e pela consulta. Duas tabelas filhas (`SALES_INVOICE_NFE_CORRECTIONS`,
`PURCHASE_INVOICE_NFE_CORRECTIONS`) com o `procEventoNFe` na linha. O Reports imprime com o `NFeEvento.frx` da
Zeus, copiado sem alteração. O frontend tem um diálogo compartilhado e uma tabela compartilhada no detalhe.

**Tech Stack:** .NET 10, EF Core (SQL Server; testes com InMemory), OData v4 (ASP.NET Core OData), Zeus.Net.NFe.NFCe
2026.9.24.1416, FastReport.OpenSource 2026.1.3, OpenUI5 + TypeScript, QUnit.

**Spec:** `docs/superpowers/specs/2026-10-06-nfe-correction-letter-design.md` (backend repo, commit 0196999).

## Global Constraints

- Branch `feature/nfe-correction-letter` nos dois repos (`siagro-b1-backend`, `siagro-b1-frontend`). Conferir o
  branch antes de cada commit. **Nunca push.** Commit com pathspec explícito: o índice do backend tem
  `docs/superpowers/{plans,specs}/2026-10-01-nfe-standalone-taxation*` staged que **não** podem entrar em commit.
- Mensagem de commit: `tipo(escopo): descrição pt-BR` (escopo `invoice`; `reports` para o Reports; `docs` para docs),
  com o rodapé `Co-Authored-By`/`Claude-Session` da sessão. Commit com migration leva o trailer
  `DB: AddNfeCorrectionLetters`.
- Identificadores em inglês; tudo o que o usuário lê em pt-BR.
- Texto da correção: 15 a 1000 caracteres **depois** da normalização (§T8 do spec + Review Focus 1).
- Máximo de 20 cartas por NF-e (`nSeqEvento` 1–20).
- Só NF-e `Authorized` de documento não cancelado; entrada só com `IssuerType == Own`.
- A CC-e não muda nenhum campo do documento (inclusive `NfeStatusCode`/`NfeStatusReason`).
- Arquivos da Zeus copiados **sem alteração**, do commit `08743cd5f5b82769a26ad9ae0093b1b0ae60c74f`.
- XSD copiados de `C:\Projetos\EfisCloud\backend\schemas` (pacote oficial).
- Gates finais: `dotnet build SiagroB1.sln` 0 erros; `SiagroB1.Application.Tests` e `SiagroB1.Fiscal.Tests` verdes;
  no frontend `npm run ts-typecheck` (ou `yarn ts-typecheck`), `yarn lint` e o QUnit dos helpers. ⚠️ `yarn test`
  completo nunca passa (gate de cobertura irreal): rode só os módulos tocados.

## Desvios do spec (decididos aqui, menores)

- O envio devolve `NfeCorrectionOutcomeDto` (Sequence, Protocol, RegisteredAt), e não o `NfeIssueOutcomeDto`. O
  `nfeOutcomeMessage` do frontend trataria o cStat 135 como "Situação na SEFAZ" (aviso). A consulta continua com
  `NfeIssueOutcomeDto`, com o campo novo `ImportedCorrections`.
- Os entity sets seguem o padrão do repositório: `SalesInvoicesNfeCorrections` / `PurchaseInvoicesNfeCorrections`
  (como `SalesInvoicesChangeLogs`). Também há a rota de navegação `SalesInvoices({key})/NfeCorrections`,
  declarada à mão no controller como a do ChangeLogs, para a tabela compartilhada usar `path: 'NfeCorrections'`
  com `$$ownRequest`.
- `Protocol`, `RegisteredAt` e `ProcEventXml` são **nuláveis**. O mapper só devolve o procEvento quando o
  `ProcEventosNFe` da Zeus casa o `nProt`; sem isso, a carta registrada ainda precisa ser gravada.

## Review Focus

1. **Texto com caractere fora do Latin-1 ou quebra de linha** (aspas curvas, travessão, reticências do Word,
   Enter no TextArea). O XSD do `xCorrecao` é `[!-ÿ]{1}[ -ÿ]{0,}[!-ÿ]{1}`, e a Zeus valida antes de enviar. Sem
   tratamento, o usuário vê "Sem resposta da SEFAZ (detalhe técnico: …)". Esperado: aspas, travessões e
   reticências viram ASCII, qualquer espaço em branco vira um espaço, e o que sobrar fora do Latin-1 é recusado
   com a lista dos caracteres. → testes nas Tasks 1, 4 e 8.
2. **Resposta perdida e reenvio**: a carta nº N entrou na SEFAZ, mas a resposta não chegou. O próximo envio usa a
   mesma sequência N (o banco não a tem) e recebe 573. Esperado: a consulta importa a nº N e a tela diz que ela
   está registrada, sem perder o texto novo em silêncio. A mensagem deixa claro que o texto enviado agora **não**
   foi registrado e que é preciso reenviar. → teste na Task 4.
3. **Consulta repetida** não duplica cartas, e a consulta de NF-e cancelada (101) que traga CC-e grava as cartas
   junto com o cancelamento. → testes na Task 5.
4. **Carta nº 21**: recusada localmente sem chamar a SEFAZ. → teste na Task 4.
5. **Entrada de terceiro e documento cancelado** com NF-e autorizada: recusa sem chamar a SEFAZ, e o botão não
   aparece. → testes nas Tasks 4 e 8.

---

## File Structure

**Backend (`siagro-b1-backend`)**

| Arquivo | Responsabilidade |
|---|---|
| `SiagroB1.Fiscal/Schemas/{envCCe,leiauteCCe,CCe,procCCeNFe,retEnvCCe}_v1.00.xsd` | XSD oficiais da CC-e (novos) |
| `SiagroB1.Fiscal/Nfe/NfeCorrectionText.cs` | normalização e caracteres inválidos do `xCorrecao` (novo) |
| `SiagroB1.Fiscal/Nfe/NfeSefaz.cs` | `NfeCorrectionRequest`, campos novos em `NfeEventResult`/`NfeSefazResult`, `IsEventRegistered`, `SendCorrectionAsync`, mapper |
| `SiagroB1.Fiscal/Nfe/ZeusNfeSefazClient.cs` | `SendCorrectionAsync` |
| `SiagroB1.Domain/Interfaces/INfeCorrection.cs` | contrato comum das duas entidades (novo) |
| `SiagroB1.Domain/Entities/{Sales,Purchase}InvoiceNfeCorrection.cs` | entidades (novas) |
| `SiagroB1.Domain/Entities/{SalesInvoice,PurchaseInvoice}.cs` | coleção `NfeCorrections` |
| `SiagroB1.Domain/Dtos/Nfe/NfeCorrectionOutcomeDto.cs` | retorno do envio (novo) |
| `SiagroB1.Domain/Dtos/Nfe/NfeIssueOutcomeDto.cs` | `ImportedCorrections` |
| `SiagroB1.Infra/Context/AppDbContext.cs` | DbSets + índice único + FK Restrict |
| `SiagroB1.Migrations/AppContext/*_AddNfeCorrectionLetters.cs` | migration (gerada) |
| `SiagroB1.Application/Services/Nfe/INfeDocumentStore.cs` + 2 stores | `CorrectionSequencesAsync`, `AddCorrection`, `CorrectionXmlAsync` |
| `SiagroB1.Application/Services/Nfe/NfeCorrectionImporter.cs` | importação idempotente (novo) |
| `SiagroB1.Application/Services/Nfe/NfeCorrectionServiceBase.cs` + `{Sales,Purchase}InvoicesNfeCorrectionService.cs` | envio (novos) |
| `SiagroB1.Application/Services/Nfe/NfeCorrectionXmlDownloadServiceBase.cs` + 2 subclasses | download do XML (novos) |
| `SiagroB1.Application/Services/Nfe/{Sales,Purchase}InvoicesNfeCorrectionsGetService.cs` | leitura do histórico (novos) |
| `SiagroB1.Application/Services/Nfe/NfeConsultServiceBase.cs` | importação na consulta de autorizada |
| `SiagroB1.Web/Actions/Nfe/{Sales,Purchase}InvoicesSendNfeCorrectionController.cs` | actions (novos) |
| `SiagroB1.Web/Functions/Nfe/{Sales,Purchase}InvoicesNfeCorrectionXmlController.cs` | functions (novos) |
| `SiagroB1.Web/Controllers/{Sales,Purchase}InvoicesNfeCorrectionsController.cs` | entity sets somente leitura (novos) |
| `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` | EDM e DI |
| `SiagroB1.Reports/ThirdParty/Zeus-LGPL/NFe.Danfe.Base/NFe/NFeEvento.frx`, `.../NFe.Danfe.OpenFast/NFe/DanfeFrEvento.cs` | layout e classe da Zeus (copiados) |
| `SiagroB1.Reports/Services/DanfeReportService.cs`, `SiagroB1.Reports/Controllers/DanfeController.cs` | PDF da carta |
| Testes: `SiagroB1.Fiscal.Tests/Nfe/{NfeCorrectionTextTests,NfeCorrectionEventSchemaTests,NfeSefazResponseMapperTests}.cs`, `SiagroB1.Application.Tests/Nfe/{SalesInvoicesNfeCorrectionServiceTests,PurchaseInvoicesNfeCorrectionServiceTests,NfeCorrectionConsultImportTests,NfeCorrectionEdmModelTests,NfeCorrectionXmlDownloadTests}.cs`, `SiagroB1.Application.Tests/Reports/DanfeReportServiceTests.cs`, `SiagroB1.Application.Tests/Support/{FakeNfeSefazClient,NfeCorrectionTestServices}.cs` | |

**Frontend (`siagro-b1-frontend`)**

| Arquivo | Responsabilidade |
|---|---|
| `webapp/helpers/NfeHelpers.ts` | normalização, validação, `canSendNfeCorrection`, `correctionViewerOptions`, `ImportedCorrections` no `nfeOutcomeMessage` |
| `webapp/test/unit/helpers/NfeHelpers.qunit.ts` | testes dos helpers |
| `webapp/model/ServerRoutes.ts` | rotas |
| `webapp/dialogs/NfeCorrectionDialog.ts` | diálogo (novo) |
| `webapp/dialogs/DanfeViewer.ts` | `openNfeCorrectionViewer` |
| `webapp/fragments/NfeCorrections.fragment.xml` | tabela compartilhada (novo) |
| `webapp/view/{salesInvoices,purchaseInvoices}/Detail.view.xml` + controllers | botão, subseção, handlers |

---

### Task 1: Fiscal — XSD da CC-e e normalização do texto

**Files:**
- Create: `SiagroB1.Fiscal/Schemas/envCCe_v1.00.xsd`, `leiauteCCe_v1.00.xsd`, `CCe_v1.00.xsd`, `procCCeNFe_v1.00.xsd`, `retEnvCCe_v1.00.xsd` (cópia)
- Create: `SiagroB1.Fiscal/Nfe/NfeCorrectionText.cs`
- Test: `SiagroB1.Fiscal.Tests/Nfe/NfeCorrectionTextTests.cs`, `SiagroB1.Fiscal.Tests/Nfe/NfeCorrectionEventSchemaTests.cs`

**Interfaces:**
- Produces: `NfeCorrectionText.MinLength = 15`, `MaxLength = 1000`, `string Normalize(string?)`,
  `IReadOnlyList<char> InvalidCharacters(string normalized)`.

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Fiscal.Tests/Nfe/NfeCorrectionTextTests.cs`:

```csharp
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Fiscal.Tests.Nfe;

/// <summary>
/// O xCorrecao só aceita Latin-1 visível, com espaços simples no meio (XSD: [!-ÿ]{1}[ -ÿ]{0,}[!-ÿ]{1}).
/// O texto colado do Word chega com aspas curvas, travessão e quebra de linha.
/// </summary>
public class NfeCorrectionTextTests
{
    [Fact]
    public void Line_breaks_tabs_and_repeated_spaces_become_one_space()
    {
        Assert.Equal("Placa correta ABC1D23 no transporte",
            NfeCorrectionText.Normalize("  Placa correta\r\nABC1D23\tno   transporte \n"));
    }

    [Fact]
    public void Typographic_characters_become_ascii()
    {
        Assert.Equal("Onde se le \"X\" - leia-se 'Y'...",
            NfeCorrectionText.Normalize("Onde se le \u201CX\u201D \u2013 leia-se \u2018Y\u2019\u2026"));
        Assert.Equal("a - b", NfeCorrectionText.Normalize("a \u2014 b"));
        Assert.Equal("a b", NfeCorrectionText.Normalize("a\u00A0b"));
    }

    [Fact]
    public void Null_becomes_empty()
    {
        Assert.Equal(string.Empty, NfeCorrectionText.Normalize(null));
    }

    [Fact]
    public void Accented_latin1_text_is_valid()
    {
        Assert.Empty(NfeCorrectionText.InvalidCharacters("Correção do endereço: Avenida São João, nº 10"));
    }

    [Fact]
    public void Characters_outside_latin1_are_reported_once_each()
    {
        Assert.Equal(['€', '✓'], NfeCorrectionText.InvalidCharacters("Valor € errado ✓ e € de novo"));
    }
}
```

`SiagroB1.Fiscal.Tests/Nfe/NfeCorrectionEventSchemaTests.cs` (mesmo molde do `NfeCancelEventSchemaTests`):

```csharp
using DFe.Classes.Flags;
using NFe.Classes.Servicos.Evento;
using NFe.Classes.Servicos.Tipos;
using NFe.Utils.Evento;
using NFe.Utils.Validacao;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Tests.Support;

namespace SiagroB1.Fiscal.Tests.Nfe;

/// <summary>
/// A Zeus valida o envEvento da CC-e no XSD antes de enviar (envCCe_v1.00.xsd). Sem os XSD em Schemas/ ela
/// estoura FileNotFoundException — a armadilha que o cancelamento só pegou no E2E.
/// </summary>
public class NfeCorrectionEventSchemaTests
{
    private const string AccessKey = "35261012345678000195550010000001231481516230";

    private static string EnvEvento(string correction)
    {
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);
        var settings = new NfeServiceSettings(
            NfeEnvironment.Homologation, "SP", certificate, NfeServiceSettings.DefaultSchemasDirectory);
        var configuration = NfeZeusConfiguration.Create(settings);

        var evento = new evento
        {
            versao = "1.00",
            infEvento = new infEventoEnv
            {
                Id = $"ID110110{AccessKey}02",
                cOrgao = configuration.cUF,
                tpAmb = configuration.tpAmb,
                CNPJ = AccessKey.Substring(6, 14),
                chNFe = AccessKey,
                dhEvento = new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.FromHours(-3)),
                tpEvento = NFeTipoEvento.TeNfeCartaCorrecao,
                nSeqEvento = 2,
                verEvento = "1.00",
                detEvento = new detEvento
                {
                    versao = "1.00",
                    descEvento = "Carta de Correcao",
                    xCorrecao = correction,
                    xCondUso = "A Carta de Correcao e disciplinada pelo paragrafo 1o-A do art. 7o do Convenio S/N, de 15 de dezembro de 1970 e pode ser utilizada para regularizacao de erro ocorrido na emissao de documento fiscal, desde que o erro nao esteja relacionado com: I - as variaveis que determinam o valor do imposto tais como: base de calculo, aliquota, diferenca de preco, quantidade, valor da operacao ou da prestacao; II - a correcao de dados cadastrais que implique mudanca do remetente ou do destinatario; III - a data de emissao ou de saida.",
                },
            },
        };
        evento.Assina(certificate, configuration.Certificado.SignatureMethodSignedXml,
            configuration.Certificado.DigestMethodReference, false);

        var xml = new envEvento { versao = "1.00", idLote = 1, evento = [evento] }.ObterXmlString();
        Validador.Valida(ServicoNFe.RecepcaoEventoCartaCorrecao, VersaoServico.Versao100, xml, cfgServico: configuration);
        return xml;
    }

    [Fact]
    public void Correction_event_validates_against_the_official_schema()
    {
        Assert.Null(Record.Exception(() => EnvEvento(NfeCorrectionText.Normalize("Onde se le \u201CPlaca ABC1D23\u201D\r\nleia-se Placa XYZ9K87."))));
    }

    [Fact]
    public void Correction_schema_set_is_shipped_with_the_build()
    {
        foreach (var file in new[]
                 {
                     "envCCe_v1.00.xsd", "leiauteCCe_v1.00.xsd", "CCe_v1.00.xsd", "procCCeNFe_v1.00.xsd",
                     "retEnvCCe_v1.00.xsd", "tiposBasico_v1.03.xsd", "xmldsig-core-schema_v1.01.xsd",
                 })
            Assert.True(File.Exists(Path.Combine(NfeServiceSettings.DefaultSchemasDirectory, file)), $"Schema ausente: {file}");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~NfeCorrection"`
Expected: build FAIL (`NfeCorrectionText` não existe). Crie a classe vazia (`Normalize` devolvendo `value ?? ""`
e `InvalidCharacters` devolvendo `[]`) só para compilar e rode de novo. Esperado: falhas de asserção nos testes de
texto, e `FileNotFoundException` (envCCe) no teste de schema.

- [ ] **Step 3: Copy the XSDs and implement the normalizer**

```bash
cd /c/Projetos/SiagroB1/siagro-b1-backend
for f in envCCe leiauteCCe CCe procCCeNFe retEnvCCe; do cp "/c/Projetos/EfisCloud/backend/schemas/${f}_v1.00.xsd" SiagroB1.Fiscal/Schemas/; done
```

`SiagroB1.Fiscal/Nfe/NfeCorrectionText.cs`:

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace SiagroB1.Fiscal.Nfe;

/// <summary>
/// Texto da CC-e (xCorrecao). O XSD aceita só Latin-1 visível com espaços simples no meio
/// (<c>[!-ÿ]{1}[ -ÿ]{0,}[!-ÿ]{1}</c>) e 15 a 1000 caracteres. Texto colado do Word traz aspas curvas,
/// travessão e quebras de linha: viram ASCII e espaço. O que sobrar fora do Latin-1 é recusado pela Application,
/// antes de chegar à validação da Zeus (que viraria um "sem resposta" genérico).
/// </summary>
public static partial class NfeCorrectionText
{
    public const int MinLength = 15;
    public const int MaxLength = 1000;

    private static readonly Dictionary<char, string> Typographic = new()
    {
        ['\u2018'] = "'", ['\u2019'] = "'", ['\u201C'] = "\"", ['\u201D'] = "\"",
        ['\u2013'] = "-", ['\u2014'] = "-", ['\u2026'] = "...",
    };

    public static string Normalize(string? value)
    {
        var text = new StringBuilder();
        foreach (var c in value ?? string.Empty)
        {
            if (Typographic.TryGetValue(c, out var replacement))
                text.Append(replacement);
            else
                text.Append(char.IsWhiteSpace(c) ? ' ' : c);
        }

        return RepeatedSpaces().Replace(text.ToString(), " ").Trim();
    }

    /// <summary>Caracteres que o XSD recusa (fora de U+0020–U+00FF), um de cada, na ordem em que aparecem.</summary>
    public static IReadOnlyList<char> InvalidCharacters(string normalized) =>
        normalized.Where(c => c is < ' ' or > '\u00FF').Distinct().ToList();

    [GeneratedRegex(" {2,}")]
    private static partial Regex RepeatedSpaces();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~NfeCorrection"`
Expected: PASS (7 testes). Se o teste de schema acusar outro XSD ausente, copie-o do EfisCloud e acrescente-o à
lista do teste.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Fiscal/Schemas/envCCe_v1.00.xsd SiagroB1.Fiscal/Schemas/leiauteCCe_v1.00.xsd SiagroB1.Fiscal/Schemas/CCe_v1.00.xsd SiagroB1.Fiscal/Schemas/procCCeNFe_v1.00.xsd SiagroB1.Fiscal/Schemas/retEnvCCe_v1.00.xsd SiagroB1.Fiscal/Nfe/NfeCorrectionText.cs SiagroB1.Fiscal.Tests/Nfe/NfeCorrectionTextTests.cs SiagroB1.Fiscal.Tests/Nfe/NfeCorrectionEventSchemaTests.cs
git commit -m "feat(invoice): XSD da CC-e e normalização do texto da correção" -- SiagroB1.Fiscal SiagroB1.Fiscal.Tests
```

---

### Task 2: Fiscal — envio da CC-e e CC-e na consulta

**Files:**
- Modify: `SiagroB1.Fiscal/Nfe/NfeSefaz.cs`
- Modify: `SiagroB1.Fiscal/Nfe/ZeusNfeSefazClient.cs`
- Modify: `SiagroB1.Application.Tests/Support/FakeNfeSefazClient.cs` (implementar o membro novo da interface)
- Test: `SiagroB1.Fiscal.Tests/Nfe/NfeSefazResponseMapperTests.cs`

**Interfaces:**
- Consumes: nada das tarefas anteriores.
- Produces:
  - `public sealed record NfeCorrectionRequest(string AccessKey, int Sequence, string Text, string IssuerDocument, DateTimeOffset EventAt);`
  - `NfeEventResult` ganha, no fim: `int? Sequence = null, string? CorrectionText = null`.
  - `NfeSefazResult` ganha, no fim: `IReadOnlyList<NfeEventResult>? Corrections = null`.
  - `NfeStatusCodes.IsEventRegistered(int code)` (135 ou 155). O `IsCancellationRegistered` passa a delegar a ele.
  - `INfeSefazClient.SendCorrectionAsync(NfeCorrectionRequest request, NfeServiceSettings settings, CancellationToken cancellationToken = default) : Task<NfeEventResult>`.
  - Fake: `CorrectionResponses` (`Queue<Func<NfeCorrectionRequest, NfeEventResult>>`), `CorrectionRequests`,
    `CorrectionSettings`, `CorrectionRegistered(int sequence, string text, int status = 135)`,
    `CorrectionProtocol(int sequence)`, `CorrectionRegisteredAt`.

- [ ] **Step 1: Write the failing mapper tests**

Acrescente ao `NfeSefazResponseMapperTests` (os helpers `EventReturn` e o alias `ConsultaEvento` já existem no
arquivo):

```csharp
    private static ConsultaEvento CorrectionEvent(int sequence, string text, int status = 135) => new()
    {
        versao = "1.00",
        evento = new evento
        {
            versao = "1.00",
            infEvento = new infEventoEnv
            {
                chNFe = Key, tpEvento = NFeTipoEvento.TeNfeCartaCorrecao, nSeqEvento = sequence,
                detEvento = new detEvento { versao = "1.00", xCorrecao = text },
            },
        },
        retEvento = new retEvento
        {
            versao = "1.00",
            infEvento = EventReturn(status, "Evento registrado e vinculado a NF-e", $"13526000000020{sequence}"),
        },
    };

    [Fact]
    public void Registered_correction_carries_sequence_and_text()
    {
        var proc = CorrectionEvent(2, "Placa correta XYZ9K87");
        var result = NfeSefazResponseMapper.FromEvent(
            new retEnvEvento { cStat = 128, xMotivo = "Lote de evento processado", retEvento = [proc.retEvento] }, [proc]);

        Assert.Equal(135, result.StatusCode);
        Assert.Equal(2, result.Sequence);
        Assert.Equal("Placa correta XYZ9K87", result.CorrectionText);
        Assert.Equal("135260000000202", result.Protocol);
        Assert.Contains("<procEventoNFe", result.ProcEventXml);
        Assert.True(NfeStatusCodes.IsEventRegistered(result.StatusCode));
    }

    [Fact]
    public void Consult_of_authorized_note_returns_the_registered_corrections()
    {
        var result = NfeSefazResponseMapper.FromConsult(new retConsSitNFe
        {
            cStat = 100, xMotivo = "Autorizado o uso da NF-e", protNFe = Protocol(100, "Autorizado o uso da NF-e"),
            procEventoNFe = [CorrectionEvent(1, "Primeira correcao do texto"), CorrectionEvent(2, "Segunda correcao do texto"),
                CorrectionEvent(3, "Recusada pela SEFAZ aqui", status: 573)],
        });

        Assert.Equal(100, result.StatusCode);
        Assert.Equal("135260000000001", result.Protocol);
        Assert.Equal([1, 2], result.Corrections!.Select(c => c.Sequence!.Value));
        Assert.Equal("Segunda correcao do texto", result.Corrections![1].CorrectionText);
    }

    [Fact]
    public void Consult_of_cancelled_note_returns_cancellation_and_corrections()
    {
        var result = NfeSefazResponseMapper.FromConsult(new retConsSitNFe
        {
            cStat = 101, xMotivo = "Cancelamento de NF-e homologado",
            procEventoNFe = [CorrectionEvent(1, "Correcao antes de cancelar"), CancellationEvent()],
        });

        Assert.NotNull(result.CancellationEvent);
        Assert.Single(result.Corrections!);
    }

    [Fact]
    public void Consult_without_events_has_no_corrections()
    {
        var result = NfeSefazResponseMapper.FromConsult(new retConsSitNFe
        {
            cStat = 100, xMotivo = "Autorizado o uso da NF-e", protNFe = Protocol(100, "Autorizado o uso da NF-e"),
        });

        Assert.Empty(result.Corrections ?? []);
    }
```

Confira os tipos de `retEvento`/`infEventoEnv` no `CancellationEvent()` já existente no arquivo. Se o
`retEvento` da Zeus tiver outro nome de classe, use o mesmo do helper de cancelamento.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~NfeSefazResponseMapperTests"`
Expected: build FAIL (`Sequence`, `CorrectionText`, `Corrections`, `IsEventRegistered` não existem).

- [ ] **Step 3: Implement in `NfeSefaz.cs`**

Records (substituir os atuais):

```csharp
public sealed record NfeSefazResult(
    int StatusCode,
    string Reason,
    string? Protocol = null,
    DateTimeOffset? ReceivedAt = null,
    string? ProtocolXml = null,
    NfeEventResult? CancellationEvent = null,
    IReadOnlyList<NfeEventResult>? Corrections = null);

/// <summary>Pedido de CC-e (evento 110110). <see cref="EventAt"/> em Brasília; <see cref="Text"/> já normalizado.</summary>
public sealed record NfeCorrectionRequest(
    string AccessKey, int Sequence, string Text, string IssuerDocument, DateTimeOffset EventAt);

public sealed record NfeEventResult(
    int StatusCode,
    string Reason,
    string? Protocol = null,
    DateTimeOffset? RegisteredAt = null,
    string? ProcEventXml = null,
    string? Justification = null,
    int? Sequence = null,
    string? CorrectionText = null);
```

Atualize os comentários `<summary>` dos records: `Corrections` = CC-e registradas que vieram na consulta.

`NfeStatusCodes`:

```csharp
    /// <summary>Evento registrado (135) ou registrado fora de prazo (155): vale para cancelamento e CC-e.</summary>
    public static bool IsEventRegistered(int code) => code is 135 or 155;

    public static bool IsCancellationRegistered(int code) => IsEventRegistered(code);
```

Interface:

```csharp
    /// <summary>Envia a CC-e (110110). O <see cref="NfeEventResult.Sequence"/> volta preenchido.</summary>
    Task<NfeEventResult> SendCorrectionAsync(NfeCorrectionRequest request, NfeServiceSettings settings, CancellationToken cancellationToken = default);
```

Mapper — `FromConsult` (substituir):

```csharp
    public static NfeSefazResult FromConsult(retConsSitNFe response)
    {
        var corrections = response.procEventoNFe?
            .Where(e => e.evento?.infEvento?.tpEvento == NFeTipoEvento.TeNfeCartaCorrecao
                        && e.retEvento?.infEvento is { } info && NfeStatusCodes.IsEventRegistered(info.cStat))
            .Select(FromProcEvent)
            .OrderBy(e => e.Sequence)
            .ToList();

        if (response.protNFe?.infProt is not null
            && (NfeStatusCodes.IsAuthorized(response.cStat) || NfeStatusCodes.IsDenied(response.cStat)))
            return FromProtocol(response.protNFe) with { Corrections = corrections };

        var cancellation = NfeStatusCodes.IsCancelledConsult(response.cStat)
            ? response.procEventoNFe?
                .Where(e => e.evento?.infEvento?.tpEvento == NFeTipoEvento.TeNfeCancelamento
                            && e.retEvento?.infEvento is { } info && NfeStatusCodes.IsCancellationRegistered(info.cStat))
                .Select(FromProcEvent)
                .FirstOrDefault()
            : null;

        return new NfeSefazResult(response.cStat, response.xMotivo ?? string.Empty,
            CancellationEvent: cancellation, Corrections: corrections);
    }
```

`FromProcEvent` (substituir):

```csharp
    private static NfeEventResult FromProcEvent(NFe.Classes.Servicos.Consulta.procEventoNFe proc)
    {
        var info = proc.retEvento.infEvento;
        var sent = proc.evento?.infEvento;

        return new NfeEventResult(
            info.cStat, info.xMotivo ?? string.Empty, info.nProt, RegisteredAt(info),
            FuncoesXml.ClasseParaXmlString(proc), sent?.detEvento?.xJust,
            sent?.nSeqEvento, sent?.detEvento?.xCorrecao);
    }
```

O tipo de `nSeqEvento` na Zeus é `int`. Se for `int?`, o `sent?.nSeqEvento` continua compilando.

- [ ] **Step 4: Implement `ZeusNfeSefazClient.SendCorrectionAsync`**

```csharp
    public Task<NfeEventResult> SendCorrectionAsync(
        NfeCorrectionRequest request, NfeServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, services =>
        {
            // Lote de um evento só (idLote 1); a sequência é a da carta (1–20).
            var response = services.RecepcaoEventoCartaCorrecao(
                1, request.Sequence, request.AccessKey, request.Text, request.IssuerDocument, request.EventAt);

            var result = NfeSefazResponseMapper.FromEvent(response.Retorno, response.ProcEventosNFe);

            // Sem o procEvento casado, o mapper não sabe a sequência nem o texto: valem os do pedido.
            return result with
            {
                Sequence = result.Sequence ?? request.Sequence,
                CorrectionText = result.CorrectionText ?? request.Text,
            };
        }, cancellationToken);
```

- [ ] **Step 5: Implement the fake (`SiagroB1.Application.Tests/Support/FakeNfeSefazClient.cs`)**

Acrescente:

```csharp
    public Queue<Func<NfeCorrectionRequest, NfeEventResult>> CorrectionResponses { get; } = new();
    public List<NfeCorrectionRequest> CorrectionRequests { get; } = [];
    public List<NfeServiceSettings> CorrectionSettings { get; } = [];

    public Task<NfeEventResult> SendCorrectionAsync(
        NfeCorrectionRequest request, NfeServiceSettings settings, CancellationToken cancellationToken = default)
    {
        CorrectionRequests.Add(request);
        CorrectionSettings.Add(settings);
        return Task.FromResult(CorrectionResponses.Dequeue()(request));
    }

    public static string CorrectionProtocol(int sequence) => $"1352600000002{sequence:D2}";

    public static readonly DateTimeOffset CorrectionRegisteredAt = new(2026, 10, 6, 9, 15, 0, TimeSpan.FromHours(-3));

    public static NfeEventResult CorrectionRegistered(int sequence, string text, int status = 135) => new(
        status, "Evento registrado e vinculado a NF-e", CorrectionProtocol(sequence), CorrectionRegisteredAt,
        $"<procEventoNFe versao=\"1.00\"><evento><infEvento><tpEvento>110110</tpEvento><nSeqEvento>{sequence}</nSeqEvento>" +
        $"<detEvento><xCorrecao>{text}</xCorrecao></detEvento></infEvento></evento>" +
        $"<retEvento><infEvento><cStat>{status}</cStat><nProt>{CorrectionProtocol(sequence)}</nProt></infEvento></retEvento></procEventoNFe>",
        Sequence: sequence, CorrectionText: text);
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Fiscal.Tests` e `dotnet build SiagroB1.sln`
Expected: Fiscal todo verde (inclusive os testes antigos de cancelamento); solução com 0 erros.

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Fiscal/Nfe/NfeSefaz.cs SiagroB1.Fiscal/Nfe/ZeusNfeSefazClient.cs SiagroB1.Fiscal.Tests/Nfe/NfeSefazResponseMapperTests.cs SiagroB1.Application.Tests/Support/FakeNfeSefazClient.cs
git commit -m "feat(invoice): envio da CC-e na SEFAZ e cartas registradas na consulta" -- SiagroB1.Fiscal SiagroB1.Fiscal.Tests SiagroB1.Application.Tests/Support/FakeNfeSefazClient.cs
```

---

### Task 3: Domain/Infra — tabelas das cartas, migration e acesso pelo store

**Files:**
- Create: `SiagroB1.Domain/Interfaces/INfeCorrection.cs`
- Create: `SiagroB1.Domain/Entities/SalesInvoiceNfeCorrection.cs`, `SiagroB1.Domain/Entities/PurchaseInvoiceNfeCorrection.cs`
- Modify: `SiagroB1.Domain/Entities/SalesInvoice.cs` (perto da linha 188, `ChangeLogs`), `SiagroB1.Domain/Entities/PurchaseInvoice.cs`
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs` (DbSets perto da linha 58; config perto da linha 191)
- Create (gerado): `SiagroB1.Migrations/AppContext/<timestamp>_AddNfeCorrectionLetters.cs` + Designer + snapshot
- Modify: `SiagroB1.Application/Services/Nfe/INfeDocumentStore.cs`, `SalesInvoiceNfeStore.cs`, `PurchaseInvoiceNfeStore.cs`
- Test: `SiagroB1.Application.Tests/Nfe/NfeCorrectionStoreTests.cs`

**Interfaces:**
- Consumes: `NfeEventResult` (Task 2).
- Produces:
  - `INfeCorrection { Guid Key; int Sequence; string Text; string? Protocol; DateTime? RegisteredAt; int StatusCode; string Reason; string? ProcEventXml; DateTime CreatedAt; string? CreatedBy; }`
  - Entidades com `SalesInvoiceKey`/`PurchaseInvoiceKey`, navegação `SalesInvoice`/`PurchaseInvoice`.
  - `SalesInvoice.NfeCorrections` / `PurchaseInvoice.NfeCorrections` (`ICollection<…>`).
  - `AppDbContext.SalesInvoiceNfeCorrections`, `AppDbContext.PurchaseInvoiceNfeCorrections`.
  - Store: `Task<IReadOnlyList<int>> CorrectionSequencesAsync(Guid key)`,
    `void AddCorrection(TDocument document, int sequence, string text, NfeEventResult registered, string userName)`,
    `Task<string?> CorrectionXmlAsync(Guid key, int sequence)`.

- [ ] **Step 1: Write the failing test**

`SiagroB1.Application.Tests/Nfe/NfeCorrectionStoreTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>As cartas registradas ficam na tabela filha do documento, com o XML do evento.</summary>
public class NfeCorrectionStoreTests
{
    [Fact]
    public async Task Sales_store_adds_and_reads_corrections()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var store = new SalesInvoiceNfeStore(scenario.Db);
        var invoice = (await store.FindAsync(scenario.InvoiceKey))!;

        store.AddCorrection(invoice, 1, "Texto da primeira carta", FakeNfeSefazClient.CorrectionRegistered(1, "Texto da primeira carta"), "tester");
        await scenario.Db.SaveChangesAsync();

        Assert.Equal([1], await store.CorrectionSequencesAsync(scenario.InvoiceKey));
        Assert.Contains("<procEventoNFe", await store.CorrectionXmlAsync(scenario.InvoiceKey, 1));
        Assert.Null(await store.CorrectionXmlAsync(scenario.InvoiceKey, 2));
        var row = await scenario.Db.Context.SalesInvoiceNfeCorrections.AsNoTracking().SingleAsync();
        Assert.Equal(FakeNfeSefazClient.CorrectionProtocol(1), row.Protocol);
        Assert.Equal(FakeNfeSefazClient.CorrectionRegisteredAt.DateTime, row.RegisteredAt);
        Assert.Equal(135, row.StatusCode);
        Assert.Equal("tester", row.CreatedBy);
    }

    [Fact]
    public async Task Purchase_store_adds_and_reads_corrections()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var store = new PurchaseInvoiceNfeStore(scenario.Db);
        var invoice = (await store.FindAsync(scenario.InvoiceKey))!;

        store.AddCorrection(invoice, 3, "Texto da terceira carta", FakeNfeSefazClient.CorrectionRegistered(3, "Texto da terceira carta"), "tester");
        await scenario.Db.SaveChangesAsync();

        Assert.Equal([3], await store.CorrectionSequencesAsync(scenario.InvoiceKey));
        Assert.NotNull(await store.CorrectionXmlAsync(scenario.InvoiceKey, 3));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeCorrectionStoreTests"`
Expected: build FAIL (`AddCorrection` não existe).

- [ ] **Step 3: Domain**

`SiagroB1.Domain/Interfaces/INfeCorrection.cs`:

```csharp
namespace SiagroB1.Domain.Interfaces;

/// <summary>
/// Uma CC-e registrada na SEFAZ (evento 110110) — o que as tabelas de cartas do documento de saída e do de
/// entrada têm em comum. Cada carta substitui a anterior na SEFAZ; aqui todas ficam (histórico e escrituração).
/// </summary>
public interface INfeCorrection
{
    Guid Key { get; }
    int Sequence { get; }
    string Text { get; }
    string? Protocol { get; }
    DateTime? RegisteredAt { get; }
    int StatusCode { get; }
    string Reason { get; }
    string? ProcEventXml { get; }
    DateTime CreatedAt { get; }
    string? CreatedBy { get; }
}
```

`SiagroB1.Domain/Entities/SalesInvoiceNfeCorrection.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// CC-e registrada sobre a NF-e do documento de saída (spec 2026-10-06). Só nasce com a carta registrada
/// (135/155) — recusa e falta de resposta não gravam. Somente leitura no OData; o procEventoNFe sai do EDM
/// e é baixado pela function própria.
/// </summary>
[Table("SALES_INVOICE_NFE_CORRECTIONS")]
public class SalesInvoiceNfeCorrection : INfeCorrection
{
    [Key]
    public Guid Key { get; set; }

    [ForeignKey(nameof(SalesInvoice))]
    public Guid SalesInvoiceKey { get; set; }

    public virtual SalesInvoice? SalesInvoice { get; set; }

    /// <summary>nSeqEvento (1–20); único por documento.</summary>
    public int Sequence { get; set; }

    [Column(TypeName = "NVARCHAR(1000)")]
    public required string Text { get; set; }

    [Column(TypeName = "VARCHAR(20)")]
    public string? Protocol { get; set; }

    /// <summary>dhRegEvento em hora de Brasília (mesmo tratamento de NfeCancelledAt).</summary>
    public DateTime? RegisteredAt { get; set; }

    public int StatusCode { get; set; }

    [Column(TypeName = "VARCHAR(255)")]
    public required string Reason { get; set; }

    [Column(TypeName = "NVARCHAR(MAX)")]
    public string? ProcEventXml { get; set; }

    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? CreatedBy { get; set; }
}
```

`PurchaseInvoiceNfeCorrection.cs`: idêntica, com `[Table("PURCHASE_INVOICE_NFE_CORRECTIONS")]`,
`PurchaseInvoiceKey`/`PurchaseInvoice` no lugar de `SalesInvoiceKey`/`SalesInvoice`, e "documento de entrada" no
comentário.

Em `SalesInvoice.cs`, ao lado de `ChangeLogs`:

```csharp
    /// <summary>CC-e registradas da NF-e (somente leitura; rota declarada no SalesInvoicesNfeCorrectionsController).</summary>
    public ICollection<SalesInvoiceNfeCorrection> NfeCorrections { get; set; } = [];
```

Em `PurchaseInvoice.cs` faça o mesmo com `PurchaseInvoiceNfeCorrection`.

- [ ] **Step 4: Infra**

`AppDbContext.cs`, junto dos DbSets de XML:

```csharp
    public DbSet<SalesInvoiceNfeCorrection> SalesInvoiceNfeCorrections { get; set; }
    public DbSet<PurchaseInvoiceNfeCorrection> PurchaseInvoiceNfeCorrections { get; set; }
```

No `OnModelCreating`, logo após o índice de `PurchaseInvoiceNfeXml`:

```csharp
        // CC-e: uma sequência por documento. Restrict: documento com NF-e autorizada nunca é apagado
        // (NfeLock.EnsureDeletable), então o apagar do rascunho nunca encontra cartas.
        modelBuilder.Entity<SalesInvoiceNfeCorrection>()
            .HasIndex(x => new { x.SalesInvoiceKey, x.Sequence })
            .IsUnique();
        modelBuilder.Entity<SalesInvoiceNfeCorrection>()
            .HasOne(x => x.SalesInvoice)
            .WithMany(x => x.NfeCorrections)
            .HasForeignKey(x => x.SalesInvoiceKey)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PurchaseInvoiceNfeCorrection>()
            .HasIndex(x => new { x.PurchaseInvoiceKey, x.Sequence })
            .IsUnique();
        modelBuilder.Entity<PurchaseInvoiceNfeCorrection>()
            .HasOne(x => x.PurchaseInvoice)
            .WithMany(x => x.NfeCorrections)
            .HasForeignKey(x => x.PurchaseInvoiceKey)
            .OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 5: Store**

`INfeDocumentStore.cs`:

```csharp
    /// <summary>Sequências das CC-e registradas do documento.</summary>
    Task<IReadOnlyList<int>> CorrectionSequencesAsync(Guid key);

    /// <summary>Acrescenta uma CC-e registrada; grava no próximo <c>SaveChanges</c>.</summary>
    void AddCorrection(TDocument document, int sequence, string text, NfeEventResult registered, string userName);

    /// <summary>procEventoNFe da CC-e; nulo quando a carta não existe ou veio sem XML.</summary>
    Task<string?> CorrectionXmlAsync(Guid key, int sequence);
```

(acrescente `using SiagroB1.Fiscal.Nfe;`)

`SalesInvoiceNfeStore.cs`:

```csharp
    public async Task<IReadOnlyList<int>> CorrectionSequencesAsync(Guid key) =>
        await db.Context.SalesInvoiceNfeCorrections.AsNoTracking()
            .Where(x => x.SalesInvoiceKey == key)
            .OrderBy(x => x.Sequence)
            .Select(x => x.Sequence)
            .ToListAsync();

    public void AddCorrection(SalesInvoice document, int sequence, string text, NfeEventResult registered, string userName) =>
        db.Context.SalesInvoiceNfeCorrections.Add(new SalesInvoiceNfeCorrection
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = document.Key, Sequence = sequence, Text = text,
            Protocol = registered.Protocol, RegisteredAt = registered.RegisteredAt?.DateTime,
            StatusCode = registered.StatusCode, Reason = NfeStatusText.Truncate(registered.Reason),
            ProcEventXml = registered.ProcEventXml, CreatedAt = DateTime.Now, CreatedBy = userName,
        });

    public Task<string?> CorrectionXmlAsync(Guid key, int sequence) =>
        db.Context.SalesInvoiceNfeCorrections.AsNoTracking()
            .Where(x => x.SalesInvoiceKey == key && x.Sequence == sequence)
            .Select(x => x.ProcEventXml)
            .FirstOrDefaultAsync();
```

Confira se `NfeStatusText.Truncate` corta em 255. Se o limite for outro, use `registered.Reason[..Math.Min(255, registered.Reason.Length)]`.
`PurchaseInvoiceNfeStore.cs`: igual, com `PurchaseInvoiceNfeCorrections`/`PurchaseInvoiceKey`.

- [ ] **Step 6: Run tests**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeCorrectionStoreTests"`
Expected: PASS.

- [ ] **Step 7: Generate the migration**

```bash
cd /c/Projetos/SiagroB1/siagro-b1-backend
dotnet ef migrations add AddNfeCorrectionLetters --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

Leia o `Up()` gerado. Ele deve conter **só** os dois `CreateTable` e os dois índices únicos (mais os índices de FK,
se o EF criar). Se aparecer qualquer `AlterColumn`/`AddColumn` de outra tabela, é drift do snapshot: remova essas
operações do `Up()`/`Down()`, como na memória "Migrations hand-edited & baseline sync". O segundo comando deve
responder "No changes".

- [ ] **Step 8: Run the full Application suite**

Run: `dotnet test SiagroB1.Application.Tests`
Expected: verde (por volta de 3139 testes ou mais).

- [ ] **Step 9: Commit**

```bash
git add SiagroB1.Domain/Interfaces/INfeCorrection.cs SiagroB1.Domain/Entities/SalesInvoiceNfeCorrection.cs SiagroB1.Domain/Entities/PurchaseInvoiceNfeCorrection.cs SiagroB1.Application.Tests/Nfe/NfeCorrectionStoreTests.cs SiagroB1.Migrations/AppContext
git commit -m "feat(invoice): tabelas das cartas de correção da NF-e

DB: AddNfeCorrectionLetters" -- SiagroB1.Domain SiagroB1.Infra SiagroB1.Migrations SiagroB1.Application/Services/Nfe SiagroB1.Application.Tests/Nfe/NfeCorrectionStoreTests.cs
```

---

### Task 4: Application — envio da CC-e (saída e entrada)

**Files:**
- Create: `SiagroB1.Domain/Dtos/Nfe/NfeCorrectionOutcomeDto.cs`
- Create: `SiagroB1.Application/Services/Nfe/NfeCorrectionImporter.cs`
- Create: `SiagroB1.Application/Services/Nfe/NfeCorrectionServiceBase.cs`
- Create: `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeCorrectionService.cs`, `PurchaseInvoicesNfeCorrectionService.cs`
- Create: `SiagroB1.Application.Tests/Support/NfeCorrectionTestServices.cs`
- Test: `SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeCorrectionServiceTests.cs`, `PurchaseInvoicesNfeCorrectionServiceTests.cs`

**Interfaces:**
- Consumes: store (Task 3), `NfeCorrectionText` (Task 1), `SendCorrectionAsync`/`Corrections`/`IsEventRegistered` (Task 2),
  `NfeCancelTestServices.AuthorizeSaleAsync/AuthorizePurchaseAsync/AccessKey`.
- Produces:
  - `NfeCorrectionOutcomeDto { int Sequence; string? Protocol; DateTime? RegisteredAt; }`
  - `NfeCorrectionImporter.ConsultUser = "Consulta SEFAZ"`;
    `static Task<int> ImportAsync<T>(INfeDocumentStore<T> store, T document, IReadOnlyList<NfeEventResult>? corrections, string userName)`
  - `NfeCorrectionServiceBase<T>.ExecuteAsync(Guid key, string? text, string userName) : Task<NfeCorrectionOutcomeDto>`
  - Constantes: `MaxCorrections = 20`, `NoAnswerMessage`, `NotSavedMessage`.
  - Test helpers: `NfeCorrectionTestServices.SalesCorrection(UnitOfWork, FakeNfeSefazClient)`,
    `PurchaseCorrection(UnitOfWork, FakeNfeSefazClient)`.

- [ ] **Step 1: Write the test helper and the failing tests**

`SiagroB1.Application.Tests/Support/NfeCorrectionTestServices.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Monta o envio da CC-e sobre os seeds da emissão, com a SEFAZ simulada.</summary>
public static class NfeCorrectionTestServices
{
    private static BranchNfeSettingsService Settings(UnitOfWork db, FakeNfeSefazClient sefaz) =>
        new(db, new NfeOptions(NfeTestSeed.Config()), sefaz);

    public static SalesInvoicesNfeCorrectionService SalesCorrection(UnitOfWork db, FakeNfeSefazClient sefaz) =>
        new(db, Settings(db, sefaz), sefaz, new FakeNfeNumberReservationService(),
            NullLogger<SalesInvoicesNfeCorrectionService>.Instance);

    public static PurchaseInvoicesNfeCorrectionService PurchaseCorrection(UnitOfWork db, FakeNfeSefazClient sefaz) =>
        new(db, Settings(db, sefaz), sefaz, new FakeNfeNumberReservationService(),
            NullLogger<PurchaseInvoicesNfeCorrectionService>.Instance);
}
```

`SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeCorrectionServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using static SiagroB1.Application.Tests.Support.NfeCancelTestServices;
using static SiagroB1.Application.Tests.Support.NfeCorrectionTestServices;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>CC-e do documento de saída (spec 2026-10-06 §7.1).</summary>
public class SalesInvoicesNfeCorrectionServiceTests
{
    private const string Text = "Onde se le placa ABC1D23, leia-se placa XYZ9K87.";

    private static async Task<(NfeScenario Scenario, FakeNfeSefazClient Sefaz)> AuthorizedAsync()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await AuthorizeSaleAsync(scenario.Db, scenario.InvoiceKey);
        return (scenario, new FakeNfeSefazClient());
    }

    private static void Registers(FakeNfeSefazClient sefaz) =>
        sefaz.CorrectionResponses.Enqueue(r => FakeNfeSefazClient.CorrectionRegistered(r.Sequence, r.Text));

    [Fact]
    public async Task First_correction_is_sequence_1_and_is_saved_with_the_event_xml()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        Registers(sefaz);

        var outcome = await SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester");

        Assert.Equal(1, outcome.Sequence);
        Assert.Equal(FakeNfeSefazClient.CorrectionProtocol(1), outcome.Protocol);
        var row = await scenario.Db.Context.SalesInvoiceNfeCorrections.AsNoTracking().SingleAsync();
        Assert.Equal(Text, row.Text);
        Assert.Equal("tester", row.CreatedBy);
        Assert.Contains("<procEventoNFe", row.ProcEventXml);
    }

    [Fact]
    public async Task Request_carries_key_sequence_issuer_and_brasilia_time()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        Registers(sefaz);

        await SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester");

        var request = Assert.Single(sefaz.CorrectionRequests);
        Assert.Equal(AccessKey, request.AccessKey);
        Assert.Equal(1, request.Sequence);
        Assert.Equal("12345678000195", request.IssuerDocument);
        Assert.Equal(TimeSpan.FromHours(-3), request.EventAt.Offset);
        Assert.Equal(NfeEnvironment.Homologation, Assert.Single(sefaz.CorrectionSettings).Environment);
    }

    [Fact]
    public async Task Second_correction_takes_the_next_sequence()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        Registers(sefaz);
        Registers(sefaz);
        var service = SalesCorrection(scenario.Db, sefaz);

        await service.ExecuteAsync(scenario.InvoiceKey, Text, "tester");
        var second = await service.ExecuteAsync(scenario.InvoiceKey, Text + " Corrige tambem o volume.", "tester");

        Assert.Equal(2, second.Sequence);
        Assert.Equal([1, 2], sefaz.CorrectionRequests.Select(r => r.Sequence));
    }

    [Fact]
    public async Task Correction_does_not_touch_the_document()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        Registers(sefaz);

        await SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester");

        var saved = await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(NfeStatus.Authorized, saved.NfeStatus);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        Assert.Equal("100", saved.NfeStatusCode);
        Assert.Equal("Autorizado o uso da NF-e", saved.NfeStatusReason);
    }

    [Fact]
    public async Task Text_is_normalized_before_sending_and_saving()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        Registers(sefaz);

        await SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, "  Onde se le \u201CABC\u201D\r\nleia-se XYZ  ", "tester");

        Assert.Equal("Onde se le \"ABC\" leia-se XYZ", Assert.Single(sefaz.CorrectionRequests).Text);
        Assert.Equal("Onde se le \"ABC\" leia-se XYZ", (await scenario.Db.Context.SalesInvoiceNfeCorrections.AsNoTracking().SingleAsync()).Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   curta    \r\n ")]
    public async Task Short_text_is_refused_without_calling_sefaz(string? text)
    {
        var (scenario, sefaz) = await AuthorizedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, text, "tester"));

        Assert.Equal("O texto da correção deve ter entre 15 e 1000 caracteres.", ex.Message);
        Assert.Empty(sefaz.CorrectionRequests);
    }

    [Fact]
    public async Task Long_text_is_refused()
    {
        var (scenario, sefaz) = await AuthorizedAsync();

        await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, new string('x', 1001), "tester"));

        Assert.Empty(sefaz.CorrectionRequests);
    }

    [Fact]
    public async Task Characters_the_sefaz_refuses_are_named()
    {
        var (scenario, sefaz) = await AuthorizedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, "Valor em € corrigido ✓ aqui", "tester"));

        Assert.Equal("O texto da correção tem caracteres que a SEFAZ não aceita: € ✓", ex.Message);
        Assert.Empty(sefaz.CorrectionRequests);
    }

    [Theory]
    [InlineData(NfeStatus.None)]
    [InlineData(NfeStatus.Processing)]
    [InlineData(NfeStatus.Rejected)]
    [InlineData(NfeStatus.Cancelled)]
    public async Task Only_authorized_nfe_receives_a_correction(NfeStatus status)
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        (await scenario.Db.Context.SalesInvoices.SingleAsync()).NfeStatus = status;
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester"));

        Assert.Equal("Só NF-e autorizada recebe carta de correção.", ex.Message);
        Assert.Empty(sefaz.CorrectionRequests);
    }

    [Fact]
    public async Task Cancelled_document_is_refused()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        (await scenario.Db.Context.SalesInvoices.SingleAsync()).InvoiceStatus = InvoiceStatus.Cancelled;
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester"));

        Assert.Equal("Documento cancelado não recebe carta de correção.", ex.Message);
        Assert.Empty(sefaz.CorrectionRequests);
    }

    [Fact]
    public async Task Missing_document_is_not_found()
    {
        var (scenario, sefaz) = await AuthorizedAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(Guid.NewGuid(), Text, "tester"));
    }

    [Fact]
    public async Task Twenty_first_correction_is_refused_without_calling_sefaz()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        var store = new SiagroB1.Application.Services.Nfe.SalesInvoiceNfeStore(scenario.Db);
        var invoice = (await store.FindAsync(scenario.InvoiceKey))!;
        for (var sequence = 1; sequence <= 20; sequence++)
            store.AddCorrection(invoice, sequence, Text, FakeNfeSefazClient.CorrectionRegistered(sequence, Text), "tester");
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester"));

        Assert.Equal("Limite de 20 cartas de correção atingido para esta NF-e.", ex.Message);
        Assert.Empty(sefaz.CorrectionRequests);
    }

    [Fact]
    public async Task Refusal_saves_nothing_and_shows_the_sefaz_message()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CorrectionResponses.Enqueue(_ => new NfeEventResult(501, "Rejeição: Prazo do evento expirado"));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester"));

        Assert.Equal("Carta de correção recusada pela SEFAZ: 501 - Rejeição: Prazo do evento expirado", ex.Message);
        Assert.False(await scenario.Db.Context.SalesInvoiceNfeCorrections.AnyAsync());
    }

    [Fact]
    public async Task No_answer_saves_nothing_and_asks_to_consult()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CorrectionResponses.Enqueue(_ => throw new NfeCommunicationException("Tempo esgotado."));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester"));

        Assert.Equal("Sem resposta da SEFAZ na carta de correção: use Consultar situação antes de tentar de novo.", ex.Message);
        Assert.False(await scenario.Db.Context.SalesInvoiceNfeCorrections.AnyAsync());
    }

    [Fact]
    public async Task Unexpected_failure_reports_the_technical_detail()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CorrectionResponses.Enqueue(_ => throw new FileNotFoundException("envCCe_v1.00.xsd"));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester"));

        Assert.Contains("detalhe técnico: envCCe_v1.00.xsd", ex.Message);
    }

    [Fact]
    public async Task Duplicate_event_imports_the_lost_correction_from_the_consult()
    {
        // A nº 1 entrou na SEFAZ, mas a resposta se perdeu: o próximo envio repete a sequência 1.
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CorrectionResponses.Enqueue(_ => new NfeEventResult(573, "Rejeição: Duplicidade de evento"));
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key) with
        {
            Corrections = [FakeNfeSefazClient.CorrectionRegistered(1, "Texto que entrou antes")],
        });

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester"));

        Assert.Equal(
            "A carta de correção nº 1 já estava registrada na SEFAZ com outro envio e foi importada. " +
            "O texto enviado agora não foi registrado: envie de novo.", ex.Message);
        var row = await scenario.Db.Context.SalesInvoiceNfeCorrections.AsNoTracking().SingleAsync();
        Assert.Equal(1, row.Sequence);
        Assert.Equal("Texto que entrou antes", row.Text);
    }

    [Fact]
    public async Task Duplicate_event_with_the_same_text_is_a_success()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CorrectionResponses.Enqueue(_ => new NfeEventResult(573, "Rejeição: Duplicidade de evento"));
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key) with
        {
            Corrections = [FakeNfeSefazClient.CorrectionRegistered(1, Text)],
        });

        var outcome = await SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester");

        Assert.Equal(1, outcome.Sequence);
        Assert.Single(await scenario.Db.Context.SalesInvoiceNfeCorrections.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Duplicate_event_not_confirmed_by_the_consult_is_refused()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CorrectionResponses.Enqueue(_ => new NfeEventResult(573, "Rejeição: Duplicidade de evento"));
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester"));

        Assert.StartsWith("A SEFAZ informou evento duplicado, mas a consulta não trouxe a carta nº 1:", ex.Message);
        Assert.False(await scenario.Db.Context.SalesInvoiceNfeCorrections.AnyAsync());
    }
}
```

`PurchaseInvoicesNfeCorrectionServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Support.NfeCancelTestServices;
using static SiagroB1.Application.Tests.Support.NfeCorrectionTestServices;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>CC-e da entrada própria (spec 2026-10-06 §7.1, T6).</summary>
public class PurchaseInvoicesNfeCorrectionServiceTests
{
    private const string Text = "Onde se le placa ABC1D23, leia-se placa XYZ9K87.";

    [Fact]
    public async Task Own_entry_receives_a_correction()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await AuthorizePurchaseAsync(scenario.Db, scenario.InvoiceKey);
        var sefaz = new FakeNfeSefazClient();
        sefaz.CorrectionResponses.Enqueue(r => FakeNfeSefazClient.CorrectionRegistered(r.Sequence, r.Text));

        var outcome = await PurchaseCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester");

        Assert.Equal(1, outcome.Sequence);
        Assert.True(await scenario.Db.Context.PurchaseInvoiceNfeCorrections.AnyAsync(x =>
            x.PurchaseInvoiceKey == scenario.InvoiceKey && x.Sequence == 1));
    }

    [Fact]
    public async Task Third_party_entry_is_refused_without_calling_sefaz()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await AuthorizePurchaseAsync(scenario.Db, scenario.InvoiceKey);
        (await scenario.Db.Context.PurchaseInvoices.SingleAsync(x => x.Key == scenario.InvoiceKey)).IssuerType = DocumentIssuerType.ThirdParty;
        await scenario.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester"));

        Assert.Equal("NF-e de terceiro não recebe carta de correção pelo Siagro.", ex.Message);
        Assert.Empty(sefaz.CorrectionRequests);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeCorrectionServiceTests"`
Expected: build FAIL (serviços não existem).

- [ ] **Step 3: Implement the DTO, the importer and the service**

`SiagroB1.Domain/Dtos/Nfe/NfeCorrectionOutcomeDto.cs`:

```csharp
namespace SiagroB1.Domain.Dtos.Nfe;

/// <summary>CC-e registrada: a tela mostra "Carta de correção nº {Sequence} registrada na SEFAZ."</summary>
public class NfeCorrectionOutcomeDto
{
    public int Sequence { get; set; }
    public string? Protocol { get; set; }
    public DateTime? RegisteredAt { get; set; }
}
```

`SiagroB1.Application/Services/Nfe/NfeCorrectionImporter.cs`:

```csharp
using SiagroB1.Domain.Interfaces;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Grava as CC-e registradas na SEFAZ que ainda não estão no documento (resposta perdida, carta emitida fora do
/// Siagro). Idempotente: sequência já gravada é ignorada. Não salva — quem chama decide o SaveChanges.
/// </summary>
public static class NfeCorrectionImporter
{
    /// <summary>Autor gravado nas cartas que vieram da consulta.</summary>
    public const string ConsultUser = "Consulta SEFAZ";

    public static async Task<int> ImportAsync<TDocument>(
        INfeDocumentStore<TDocument> store, TDocument document, IReadOnlyList<NfeEventResult>? corrections, string userName)
        where TDocument : class, INfeDocument
    {
        if (corrections is null || corrections.Count == 0)
            return 0;

        var existing = (await store.CorrectionSequencesAsync(document.Key)).ToHashSet();
        var imported = 0;

        foreach (var correction in corrections.Where(c => c.Sequence is not null).OrderBy(c => c.Sequence))
        {
            if (!existing.Add(correction.Sequence!.Value))
                continue;

            store.AddCorrection(document, correction.Sequence.Value, correction.CorrectionText ?? string.Empty, correction, userName);
            imported++;
        }

        return imported;
    }
}
```

`SiagroB1.Application/Services/Nfe/NfeCorrectionServiceBase.cs`:

```csharp
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// "Carta de Correção" (spec 2026-10-06 §7.1), comum aos documentos: valida, envia o evento 110110 no ambiente da
/// EMISSÃO com a próxima sequência e grava a carta registrada. Recusa e falta de resposta não gravam nada. A CC-e
/// não muda nenhum campo do documento.
/// </summary>
public abstract class NfeCorrectionServiceBase<TDocument>(
    IUnitOfWork db,
    INfeDocumentStore<TDocument> store,
    BranchNfeSettingsService settingsService,
    INfeSefazClient sefaz,
    NfeNumberReservationService reservation,
    ILogger logger)
    where TDocument : class, INfeDocument
{
    public const int MaxCorrections = 20;

    public const string NoAnswerMessage =
        "Sem resposta da SEFAZ na carta de correção: use Consultar situação antes de tentar de novo.";

    public const string NotSavedMessage =
        "A SEFAZ pode ter registrado a carta de correção, mas ela não foi gravada: use Consultar situação.";

    public async Task<NfeCorrectionOutcomeDto> ExecuteAsync(Guid key, string? text, string userName)
    {
        var correction = NfeCorrectionText.Normalize(text);
        if (correction.Length is < NfeCorrectionText.MinLength or > NfeCorrectionText.MaxLength)
            throw new DefaultException("O texto da correção deve ter entre 15 e 1000 caracteres.");

        var invalid = NfeCorrectionText.InvalidCharacters(correction);
        if (invalid.Count > 0)
            throw new DefaultException($"O texto da correção tem caracteres que a SEFAZ não aceita: {string.Join(' ', invalid)}");

        // A mesma trava da emissão, da consulta e do cancelamento.
        await using var emissionLock = await reservation.AcquireEmissionLockAsync(key);

        var invoice = await store.FindAsync(key) ?? throw new NotFoundException(store.NotFoundMessage);

        EnsureIssuer(invoice);

        if (invoice.InvoiceStatus == InvoiceStatus.Cancelled)
            throw new DefaultException("Documento cancelado não recebe carta de correção.");

        if (invoice.NfeStatus != NfeStatus.Authorized)
            throw new DefaultException("Só NF-e autorizada recebe carta de correção.");

        if (invoice.ChaveNFe is not { Length: 44 } accessKey)
            throw new DefaultException("A NF-e deste documento está sem chave de acesso.");

        var sequences = await store.CorrectionSequencesAsync(key);
        var sequence = (sequences.Count == 0 ? 0 : sequences.Max()) + 1;
        if (sequence > MaxCorrections)
            throw new DefaultException("Limite de 20 cartas de correção atingido para esta NF-e.");

        using var service = await settingsService.OpenAsync(invoice.BranchCode!, invoice.NfeEnvironment);

        var request = new NfeCorrectionRequest(
            accessKey, sequence, correction, accessKey.Substring(6, 14),
            TimeZoneInfo.ConvertTime(DateTimeOffset.Now, NfeIssueInputAssembler.BrasiliaZone));

        NfeEventResult result;
        try
        {
            result = await sefaz.SendCorrectionAsync(request, service.Settings);
        }
        catch (NfeCommunicationException)
        {
            throw new DefaultException(NoAnswerMessage);
        }
        catch (Exception e) when (e is not DefaultException)
        {
            // Certificado, TLS, XSD ausente: a situação real na SEFAZ é desconhecida, como no "sem resposta".
            logger.LogError(e, "Falha inesperada ao enviar a carta de correção da NF-e do documento {InvoiceKey}.", key);
            throw new DefaultException(NfeStatusText.Truncate($"{NoAnswerMessage} (detalhe técnico: {e.Message})"));
        }

        if (NfeStatusCodes.IsEventRegistered(result.StatusCode))
        {
            store.AddCorrection(invoice, sequence, correction, result, userName);
            await SaveAsync(key);
            return Outcome(sequence, result);
        }

        if (result.StatusCode == NfeStatusCodes.DuplicateEvent)
            return await RecoverDuplicateAsync(invoice, accessKey, sequence, correction, service.Settings, userName);

        throw new DefaultException($"Carta de correção recusada pela SEFAZ: {result.StatusCode} - {result.Reason}");
    }

    /// <summary>
    /// 573: a sequência já está na SEFAZ (envio anterior sem resposta). A consulta traz as cartas; se a desta
    /// sequência tem o MESMO texto, é sucesso; com outro texto, ela é importada e o usuário reenvia o texto novo.
    /// </summary>
    private async Task<NfeCorrectionOutcomeDto> RecoverDuplicateAsync(
        TDocument invoice, string accessKey, int sequence, string correction, NfeServiceSettings settings, string userName)
    {
        NfeSefazResult consult;
        try
        {
            consult = await sefaz.ConsultProtocolAsync(accessKey, settings);
        }
        catch (NfeCommunicationException)
        {
            throw new DefaultException(NoAnswerMessage);
        }

        var registered = consult.Corrections?.FirstOrDefault(c => c.Sequence == sequence);
        if (registered is null)
            throw new DefaultException(
                $"A SEFAZ informou evento duplicado, mas a consulta não trouxe a carta nº {sequence}: {consult.StatusCode} - {consult.Reason}");

        await NfeCorrectionImporter.ImportAsync(store, invoice, consult.Corrections, userName);
        await SaveAsync(invoice.Key);

        if (registered.CorrectionText != correction)
            throw new DefaultException(
                $"A carta de correção nº {sequence} já estava registrada na SEFAZ com outro envio e foi importada. " +
                "O texto enviado agora não foi registrado: envie de novo.");

        return Outcome(sequence, registered);
    }

    private async Task SaveAsync(Guid key)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Falha ao gravar a carta de correção registrada na SEFAZ do documento {InvoiceKey}.", key);
            db.Context.ChangeTracker.Clear();
            throw new DefaultException(NotSavedMessage);
        }
    }

    private static NfeCorrectionOutcomeDto Outcome(int sequence, NfeEventResult result) => new()
    {
        Sequence = sequence, Protocol = result.Protocol, RegisteredAt = result.RegisteredAt?.DateTime,
    };

    /// <summary>Quem emitiu a nota: a entrada de terceiro não recebe CC-e pelo Siagro.</summary>
    protected virtual void EnsureIssuer(TDocument document) { }
}
```

`SalesInvoicesNfeCorrectionService.cs`:

```csharp
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>"Carta de Correção" do documento de saída (venda e devolução própria).</summary>
public class SalesInvoicesNfeCorrectionService(
    IUnitOfWork db,
    BranchNfeSettingsService settingsService,
    INfeSefazClient sefaz,
    NfeNumberReservationService reservation,
    ILogger<SalesInvoicesNfeCorrectionService> logger)
    : NfeCorrectionServiceBase<SalesInvoice>(db, new SalesInvoiceNfeStore(db), settingsService, sefaz, reservation, logger);
```

`PurchaseInvoicesNfeCorrectionService.cs`:

```csharp
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>"Carta de Correção" do documento de entrada (entrada própria e devolução de compra).</summary>
public class PurchaseInvoicesNfeCorrectionService(
    IUnitOfWork db,
    BranchNfeSettingsService settingsService,
    INfeSefazClient sefaz,
    NfeNumberReservationService reservation,
    ILogger<PurchaseInvoicesNfeCorrectionService> logger)
    : NfeCorrectionServiceBase<PurchaseInvoice>(db, new PurchaseInvoiceNfeStore(db), settingsService, sefaz, reservation, logger)
{
    protected override void EnsureIssuer(PurchaseInvoice document)
    {
        if (document.IssuerType != DocumentIssuerType.Own)
            throw new DefaultException("NF-e de terceiro não recebe carta de correção pelo Siagro.");
    }
}
```

Se `NfeStatusText.Truncate` não for o nome real do método (é o usado no `NfeCancelServiceBase`, linha "detalhe
técnico"), use o mesmo que o cancelamento usa.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeCorrectionServiceTests"`
Expected: PASS (todos).

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Domain/Dtos/Nfe/NfeCorrectionOutcomeDto.cs SiagroB1.Application/Services/Nfe/NfeCorrectionImporter.cs SiagroB1.Application/Services/Nfe/NfeCorrectionServiceBase.cs SiagroB1.Application/Services/Nfe/SalesInvoicesNfeCorrectionService.cs SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeCorrectionService.cs SiagroB1.Application.Tests/Support/NfeCorrectionTestServices.cs SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeCorrectionServiceTests.cs SiagroB1.Application.Tests/Nfe/PurchaseInvoicesNfeCorrectionServiceTests.cs
git commit -m "feat(invoice): envio da carta de correção da NF-e de saída e de entrada própria" -- SiagroB1.Domain/Dtos/Nfe SiagroB1.Application/Services/Nfe SiagroB1.Application.Tests
```

---

### Task 5: Application — consulta importa as cartas

**Files:**
- Modify: `SiagroB1.Domain/Dtos/Nfe/NfeIssueOutcomeDto.cs`
- Modify: `SiagroB1.Application/Services/Nfe/NfeConsultServiceBase.cs` (`ConsultAuthorizedAsync`)
- Test: `SiagroB1.Application.Tests/Nfe/NfeCorrectionConsultImportTests.cs`

**Interfaces:**
- Consumes: `NfeCorrectionImporter` (Task 4), `NfeSefazResult.Corrections` (Task 2), `NfeCancelTestServices.SalesHandler`.
- Produces: `NfeIssueOutcomeDto.ImportedCorrections` (`int`, 0 por padrão).

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/Nfe/NfeCorrectionConsultImportTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using static SiagroB1.Application.Tests.Support.NfeCancelTestServices;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>"Consultar situação" de NF-e autorizada importa as CC-e que faltam (spec 2026-10-06 §7.2, T5).</summary>
public class NfeCorrectionConsultImportTests
{
    private static SalesInvoicesNfeConsultService Consult(NfeScenario scenario, FakeNfeSefazClient sefaz) =>
        new(scenario.Db, new BranchNfeSettingsService(scenario.Db, new NfeOptions(NfeTestSeed.Config()), sefaz), sefaz,
            new SalesInvoiceNfeResultHandler(scenario.Db, new RecordingConfirmService(scenario.Db),
                NullLogger<SalesInvoiceNfeResultHandler>.Instance),
            SalesHandler(scenario.Db), new FakeNfeNumberReservationService(),
            NullLogger<SalesInvoicesNfeConsultService>.Instance);

    private static async Task<(NfeScenario Scenario, FakeNfeSefazClient Sefaz)> AuthorizedAsync()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await AuthorizeSaleAsync(scenario.Db, scenario.InvoiceKey);
        return (scenario, new FakeNfeSefazClient());
    }

    [Fact]
    public async Task Consult_of_authorized_nfe_imports_missing_corrections_once()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        for (var i = 0; i < 2; i++)
            sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key) with
            {
                Corrections = [FakeNfeSefazClient.CorrectionRegistered(1, "Primeira carta fora do Siagro"),
                    FakeNfeSefazClient.CorrectionRegistered(2, "Segunda carta fora do Siagro")],
            });

        var first = await Consult(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");
        var second = await Consult(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(2, first.ImportedCorrections);
        Assert.Equal(0, second.ImportedCorrections);
        var rows = await scenario.Db.Context.SalesInvoiceNfeCorrections.AsNoTracking().OrderBy(x => x.Sequence).ToListAsync();
        Assert.Equal([1, 2], rows.Select(x => x.Sequence));
        Assert.All(rows, r => Assert.Equal(NfeCorrectionImporter.ConsultUser, r.CreatedBy));
        Assert.Equal(NfeStatus.Authorized, first.NfeStatus);
    }

    [Fact]
    public async Task Consult_of_cancelled_nfe_saves_corrections_with_the_cancellation()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.ConsultCancelled(key) with
        {
            Corrections = [FakeNfeSefazClient.CorrectionRegistered(1, "Carta antes do cancelamento")],
        });

        var outcome = await Consult(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Cancelled, outcome.NfeStatus);
        Assert.Equal(1, outcome.ImportedCorrections);
        Assert.True(await scenario.Db.Context.SalesInvoiceNfeCorrections.AnyAsync());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeCorrectionConsultImportTests"`
Expected: build FAIL (`ImportedCorrections` não existe).

- [ ] **Step 3: Implement**

`NfeIssueOutcomeDto`: acrescente

```csharp
    /// <summary>CC-e registradas na SEFAZ e gravadas agora pela consulta (só a consulta preenche).</summary>
    public int ImportedCorrections { get; set; }
```

`NfeConsultServiceBase.ConsultAuthorizedAsync`, depois do `try/catch` da consulta (substitua o restante do método):

```csharp
        // CC-e registradas na SEFAZ que o banco não tem (resposta perdida, carta emitida por fora).
        var imported = await NfeCorrectionImporter.ImportAsync(
            store, invoice, result.Corrections, NfeCorrectionImporter.ConsultUser);

        if (NfeStatusCodes.IsCancelledConsult(result.StatusCode))
        {
            // O handler salva a fase 1: as cartas importadas vão no mesmo SaveChanges.
            var cancelled = await cancellationHandler.ApplyRegisteredAsync(
                invoice, result.CancellationEvent ?? new NfeEventResult(result.StatusCode, result.Reason), null, userName);
            cancelled.ImportedCorrections = imported;
            return cancelled;
        }

        if (imported > 0)
            await db.SaveChangesAsync();

        var outcome = NfeIssueOutcomeDto.From(invoice);
        outcome.StatusCode = result.StatusCode.ToString(CultureInfo.InvariantCulture);
        outcome.Reason = result.Reason;
        outcome.ImportedCorrections = imported;
        return outcome;
```

Atualize o `<summary>` da classe: autorizada também importa as CC-e.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Consult"`
Expected: PASS, inclusive os testes antigos de consulta e cancelamento.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application.Tests/Nfe/NfeCorrectionConsultImportTests.cs
git commit -m "feat(invoice): consultar situação importa as cartas de correção registradas" -- SiagroB1.Domain/Dtos/Nfe/NfeIssueOutcomeDto.cs SiagroB1.Application/Services/Nfe/NfeConsultServiceBase.cs SiagroB1.Application.Tests/Nfe/NfeCorrectionConsultImportTests.cs
```

---

### Task 6: Web — actions, XML, histórico no OData e DI

**Files:**
- Create: `SiagroB1.Application/Services/Nfe/NfeCorrectionXmlDownloadServiceBase.cs`, `SalesInvoicesNfeCorrectionXmlDownloadService.cs`, `PurchaseInvoicesNfeCorrectionXmlDownloadService.cs`
- Create: `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeCorrectionsGetService.cs`, `PurchaseInvoicesNfeCorrectionsGetService.cs`
- Create: `SiagroB1.Web/Actions/Nfe/SalesInvoicesSendNfeCorrectionController.cs`, `PurchaseInvoicesSendNfeCorrectionController.cs`
- Create: `SiagroB1.Web/Functions/Nfe/SalesInvoicesNfeCorrectionXmlController.cs`, `PurchaseInvoicesNfeCorrectionXmlController.cs`
- Create: `SiagroB1.Web/Controllers/SalesInvoicesNfeCorrectionsController.cs`, `PurchaseInvoicesNfeCorrectionsController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (bloco do cancelamento, perto da linha 115; entity sets perto da linha 308/316; functions perto da linha 1443)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (perto da linha 363)
- Test: `SiagroB1.Application.Tests/Nfe/NfeCorrectionEdmModelTests.cs`, `NfeCorrectionXmlDownloadTests.cs`

**Interfaces:**
- Consumes: serviços das Tasks 4–5, store (Task 3).
- Produces (OData): actions `SalesInvoicesSendNfeCorrection(Key: Guid, Text: String) → NfeCorrectionOutcomeDto` (idem
  `PurchaseInvoices…`); functions `SalesInvoicesNfeCorrectionXml(Key: Guid, Sequence: Int32)`; entity sets
  `SalesInvoicesNfeCorrections`, `PurchaseInvoicesNfeCorrections` (sem `ProcEventXml`); navegação
  `SalesInvoices({key})/NfeCorrections`, `PurchaseInvoices({key})/NfeCorrections`.

- [ ] **Step 1: Write the failing tests**

`NfeCorrectionEdmModelTests.cs`:

```csharp
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Nfe;

public class NfeCorrectionEdmModelTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("SalesInvoicesSendNfeCorrection")]
    [InlineData("PurchaseInvoicesSendNfeCorrection")]
    public void Send_correction_takes_key_and_text(string name)
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == name);

        Assert.Equal("Edm.Guid", action.Parameters.Single(p => p.Name == "Key").Type.FullName());
        Assert.Equal("Edm.String", action.Parameters.Single(p => p.Name == "Text").Type.FullName());
        Assert.EndsWith("NfeCorrectionOutcomeDto", action.ReturnType.FullName());
    }

    [Theory]
    [InlineData("SalesInvoicesNfeCorrectionXml")]
    [InlineData("PurchaseInvoicesNfeCorrectionXml")]
    public void Correction_xml_is_a_function_with_key_and_sequence(string name)
    {
        var function = Model().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == name);

        Assert.Equal("Edm.Guid", function.Parameters.Single(p => p.Name == "Key").Type.FullName());
        Assert.Equal("Edm.Int32", function.Parameters.Single(p => p.Name == "Sequence").Type.FullName());
    }

    [Theory]
    [InlineData("SalesInvoicesNfeCorrections", "SalesInvoiceKey")]
    [InlineData("PurchaseInvoicesNfeCorrections", "PurchaseInvoiceKey")]
    public void Correction_history_is_an_entity_set_without_the_xml(string set, string foreignKey)
    {
        var type = Model().EntityContainer.FindEntitySet(set).EntityType;

        Assert.NotNull(type.FindProperty(foreignKey));
        Assert.NotNull(type.FindProperty("Sequence"));
        Assert.NotNull(type.FindProperty("Text"));
        Assert.Null(type.FindProperty("ProcEventXml"));
    }

    [Theory]
    [InlineData("SalesInvoices")]
    [InlineData("PurchaseInvoices")]
    public void Document_navigates_to_its_corrections(string set)
    {
        Assert.NotNull(Model().EntityContainer.FindEntitySet(set).EntityType.FindProperty("NfeCorrections"));
    }
}
```

Confira no `NfeCancellationEdmModelTests` como o teste existente acha actions e functions (`FindDeclaredOperations`
ou `SchemaElements`) e use a mesma forma.

`NfeCorrectionXmlDownloadTests.cs`:

```csharp
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Support.NfeCancelTestServices;

namespace SiagroB1.Application.Tests.Nfe;

public class NfeCorrectionXmlDownloadTests
{
    [Fact]
    public async Task Correction_xml_is_downloaded_with_key_and_sequence_in_the_name()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await AuthorizeSaleAsync(scenario.Db, scenario.InvoiceKey);
        var store = new SalesInvoiceNfeStore(scenario.Db);
        store.AddCorrection((await store.FindAsync(scenario.InvoiceKey))!, 2, "Texto da segunda carta",
            FakeNfeSefazClient.CorrectionRegistered(2, "Texto da segunda carta"), "tester");
        await scenario.Db.SaveChangesAsync();

        var (bytes, fileName) = await new SalesInvoicesNfeCorrectionXmlDownloadService(scenario.Db)
            .ExecuteAsync(scenario.InvoiceKey, 2);

        Assert.Equal($"{AccessKey}-cce-2-procEventoNFe.xml", fileName);
        Assert.Contains("<procEventoNFe", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task Missing_correction_is_not_found()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            new SalesInvoicesNfeCorrectionXmlDownloadService(scenario.Db).ExecuteAsync(scenario.InvoiceKey, 1));

        Assert.Equal("Carta de correção não encontrada ou sem o XML do evento.", ex.Message);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeCorrectionEdmModelTests|FullyQualifiedName~NfeCorrectionXmlDownloadTests"`
Expected: build FAIL.

- [ ] **Step 3: Download and get services**

`NfeCorrectionXmlDownloadServiceBase.cs`:

```csharp
using System.Text;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>XML (procEventoNFe) de uma CC-e: <c>&lt;chave&gt;-cce-&lt;seq&gt;-procEventoNFe.xml</c>.</summary>
public abstract class NfeCorrectionXmlDownloadServiceBase<TDocument>(INfeDocumentStore<TDocument> store)
    where TDocument : class, INfeDocument
{
    public async Task<(byte[] Bytes, string FileName)> ExecuteAsync(Guid invoiceKey, int sequence)
    {
        var xml = await store.CorrectionXmlAsync(invoiceKey, sequence)
                  ?? throw new NotFoundException("Carta de correção não encontrada ou sem o XML do evento.");
        var invoice = await store.FindReadOnlyAsync(invoiceKey);

        return (Encoding.UTF8.GetBytes(xml), $"{invoice?.ChaveNFe}-cce-{sequence}-procEventoNFe.xml");
    }
}
```

`SalesInvoicesNfeCorrectionXmlDownloadService.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

public class SalesInvoicesNfeCorrectionXmlDownloadService(IUnitOfWork db)
    : NfeCorrectionXmlDownloadServiceBase<SalesInvoice>(new SalesInvoiceNfeStore(db));
```

`PurchaseInvoicesNfeCorrectionXmlDownloadService.cs`: igual, com `PurchaseInvoice`/`PurchaseInvoiceNfeStore`.

`SalesInvoicesNfeCorrectionsGetService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Histórico de CC-e do documento de saída (somente leitura).</summary>
public class SalesInvoicesNfeCorrectionsGetService(AppDbContext context)
{
    public IQueryable<SalesInvoiceNfeCorrection> QueryAll() => context.SalesInvoiceNfeCorrections.AsNoTracking();

    public IQueryable<SalesInvoiceNfeCorrection> QueryAll(Guid salesInvoiceKey) =>
        QueryAll().Where(x => x.SalesInvoiceKey == salesInvoiceKey).OrderByDescending(x => x.Sequence);
}
```

`PurchaseInvoicesNfeCorrectionsGetService.cs`: igual, com `PurchaseInvoiceNfeCorrections`/`PurchaseInvoiceKey`.

- [ ] **Step 4: Controllers**

`SiagroB1.Web/Actions/Nfe/SalesInvoicesSendNfeCorrectionController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Nfe;

public class SalesInvoicesSendNfeCorrectionController(SalesInvoicesNfeCorrectionService service) : ODataController
{
    [HttpPost("odata/SalesInvoicesSendNfeCorrection")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || !Guid.TryParse(keyObj?.ToString(), out var key))
            return BadRequest("Parâmetro obrigatório: Key.");

        // Parâmetro string ausente/nulo: não chamar ToString() sobre nulo.
        parameters.TryGetValue("Text", out var textObj);

        try
        {
            return Ok(await service.ExecuteAsync(key, textObj as string, User.Identity?.Name ?? "Unknown"));
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

`PurchaseInvoicesSendNfeCorrectionController.cs`: igual, com `PurchaseInvoicesNfeCorrectionService` e a rota
`odata/PurchaseInvoicesSendNfeCorrection`.

`SiagroB1.Web/Functions/Nfe/SalesInvoicesNfeCorrectionXmlController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.Nfe;

public class SalesInvoicesNfeCorrectionXmlController(SalesInvoicesNfeCorrectionXmlDownloadService service) : ODataController
{
    [HttpGet("odata/SalesInvoicesNfeCorrectionXml(Key={key},Sequence={sequence})")]
    public async Task<ActionResult> Download([FromRoute] Guid key, [FromRoute] int sequence)
    {
        try
        {
            var (bytes, fileName) = await service.ExecuteAsync(key, sequence);
            return File(bytes, "application/xml", fileName);
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
    }
}
```

`PurchaseInvoicesNfeCorrectionXmlController.cs`: igual, com o serviço de entrada e a rota
`odata/PurchaseInvoicesNfeCorrectionXml(Key={key},Sequence={sequence})`.

`SiagroB1.Web/Controllers/SalesInvoicesNfeCorrectionsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// CC-e do documento de saída: só leitura (as linhas nascem no envio e na consulta). A rota aninhada é declarada
/// aqui porque a navegação OData não ganha rota sozinha — é a que a tabela do detalhe usa.
/// </summary>
public class SalesInvoicesNfeCorrectionsController(SalesInvoicesNfeCorrectionsGetService getService) : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<SalesInvoiceNfeCorrection>> Get() => Ok(getService.QueryAll());

    [HttpGet("odata/SalesInvoices({key:guid})/NfeCorrections")]
    [HttpGet("odata/SalesInvoices/{key:guid}/NfeCorrections")]
    [EnableQuery]
    public ActionResult<IEnumerable<SalesInvoiceNfeCorrection>> Get([FromRoute] Guid key) => Ok(getService.QueryAll(key));
}
```

`PurchaseInvoicesNfeCorrectionsController.cs`: igual, com `PurchaseInvoices`.

⚠️ Confira se `SalesInvoicesController`/`PurchaseInvoicesController` já tratam navegação genérica
(`GetNavigation`/`{navigationProperty}`). Se tratarem, a rota declarada aqui pode dar conflito de rota ambígua.
Nesse caso, remova as duas `[HttpGet]` aninhadas e faça a tabela usar o entity set com `$filter` (ver Task 9).

- [ ] **Step 5: EDM and DI**

`ODataConfigurations.cs`, dentro do `foreach (var prefix in new[] { "SalesInvoices", "PurchaseInvoices" })` do
cancelamento:

```csharp
            // CC-e (spec 2026-10-06): evento 110110 com o texto da correção.
            var sendCorrection = modelBuilder.Action($"{prefix}SendNfeCorrection");
            sendCorrection.Parameter<Guid>("Key");
            sendCorrection.Parameter<string>("Text");
            sendCorrection.Returns<NfeCorrectionOutcomeDto>();

            var correctionXml = modelBuilder.Function($"{prefix}NfeCorrectionXml");
            correctionXml.Parameter<Guid>("Key");
            correctionXml.Parameter<int>("Sequence");
            correctionXml.Returns<IActionResult>();
```

Junto de `SalesInvoicesChangeLogs`/`PurchaseInvoicesChangeLogs`:

```csharp
        // Histórico de CC-e: somente leitura; o XML do evento sai do EDM (download pela function).
        modelBuilder.EntitySet<SalesInvoiceNfeCorrection>("SalesInvoicesNfeCorrections");
        modelBuilder.EntityType<SalesInvoiceNfeCorrection>().Ignore(x => x.ProcEventXml);
        modelBuilder.EntitySet<PurchaseInvoiceNfeCorrection>("PurchaseInvoicesNfeCorrections");
        modelBuilder.EntityType<PurchaseInvoiceNfeCorrection>().Ignore(x => x.ProcEventXml);
```

Se a function com `Returns<IActionResult>()` precisar ficar no bloco das outras functions (perto da linha 1443),
mova as duas linhas para lá, mantendo o padrão do arquivo.

`ServiceCollectionExtensions.cs`, junto de `SalesInvoicesNfeCancelService`:

```csharp
        services.AddScoped<SalesInvoicesNfeCorrectionService>();
        services.AddScoped<PurchaseInvoicesNfeCorrectionService>();
        services.AddScoped<SalesInvoicesNfeCorrectionXmlDownloadService>();
        services.AddScoped<PurchaseInvoicesNfeCorrectionXmlDownloadService>();
        services.AddScoped<SalesInvoicesNfeCorrectionsGetService>();
        services.AddScoped<PurchaseInvoicesNfeCorrectionsGetService>();
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet build SiagroB1.sln` e `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeCorrection|FullyQualifiedName~EdmModel"`
Expected: 0 erros; PASS.

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Application/Services/Nfe/NfeCorrectionXmlDownloadServiceBase.cs SiagroB1.Application/Services/Nfe/SalesInvoicesNfeCorrectionXmlDownloadService.cs SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeCorrectionXmlDownloadService.cs SiagroB1.Application/Services/Nfe/SalesInvoicesNfeCorrectionsGetService.cs SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeCorrectionsGetService.cs SiagroB1.Web/Actions/Nfe/SalesInvoicesSendNfeCorrectionController.cs SiagroB1.Web/Actions/Nfe/PurchaseInvoicesSendNfeCorrectionController.cs SiagroB1.Web/Functions/Nfe/SalesInvoicesNfeCorrectionXmlController.cs SiagroB1.Web/Functions/Nfe/PurchaseInvoicesNfeCorrectionXmlController.cs SiagroB1.Web/Controllers/SalesInvoicesNfeCorrectionsController.cs SiagroB1.Web/Controllers/PurchaseInvoicesNfeCorrectionsController.cs SiagroB1.Application.Tests/Nfe/NfeCorrectionEdmModelTests.cs SiagroB1.Application.Tests/Nfe/NfeCorrectionXmlDownloadTests.cs
git commit -m "feat(invoice): ações, XML e histórico das cartas de correção no OData" -- SiagroB1.Application/Services/Nfe SiagroB1.Web SiagroB1.Application.Tests/Nfe
```

---

### Task 7: Reports — PDF da carta de correção

**Files:**
- Create (cópia): `SiagroB1.Reports/ThirdParty/Zeus-LGPL/NFe.Danfe.Base/NFe/NFeEvento.frx`, `SiagroB1.Reports/ThirdParty/Zeus-LGPL/NFe.Danfe.OpenFast/NFe/DanfeFrEvento.cs`
- Modify: `SiagroB1.Reports/ThirdParty/Zeus-LGPL/README.md` (lista de arquivos copiados, se houver)
- Modify: `SiagroB1.Reports/Services/DanfeReportService.cs`
- Modify: `SiagroB1.Reports/Controllers/DanfeController.cs`
- Test: `SiagroB1.Application.Tests/Reports/DanfeReportServiceTests.cs`

**Interfaces:**
- Consumes: entidades e DbSets (Task 3), `FakeNfeSefazClient.CorrectionRegistered` (Task 2).
- Produces: `DanfeReportService.GenerateCorrectionPdfAsync(Guid invoiceKey, int sequence)` e
  `GeneratePurchaseCorrectionPdfAsync(Guid invoiceKey, int sequence)` → `(byte[] Pdf, string FileName)`; rotas
  `POST /reports/Danfe/{key}/cce/{sequence}/print` e `POST /reports/Danfe/purchase-invoices/{key}/cce/{sequence}/print`.

- [ ] **Step 1: Copy the Zeus files (unchanged)**

```bash
cd /c/Projetos/SiagroB1/siagro-b1-backend/SiagroB1.Reports/ThirdParty/Zeus-LGPL
C=08743cd5f5b82769a26ad9ae0093b1b0ae60c74f
curl -fsS "https://raw.githubusercontent.com/ZeusAutomacao/DFe.NET/$C/NFe.Danfe.Base/NFe/NFeEvento.frx" -o NFe.Danfe.Base/NFe/NFeEvento.frx
curl -fsS "https://raw.githubusercontent.com/ZeusAutomacao/DFe.NET/$C/NFe.Danfe.OpenFast/NFe/DanfeFrEvento.cs" -o NFe.Danfe.OpenFast/NFe/DanfeFrEvento.cs
git diff --no-index --stat /dev/null NFe.Danfe.Base/NFe/NFeEvento.frx | tail -1
```

O `.frx` já sai no build pelo glob `ThirdParty\Zeus-LGPL\**\*.frx` do `SiagroB1.Reports.csproj`. O `.cs` é compilado
pelo `NFe.Danfe.Base.csproj` (confira se o glob dele inclui `NFe.Danfe.OpenFast/**`; o `DanfeFrNfe.cs` vizinho já é
compilado assim). Se o README da pasta listar os arquivos, acrescente os dois sem mudar o commit registrado.

- [ ] **Step 2: Write the failing tests**

Acrescente ao `DanfeReportServiceTests`:

```csharp
    [Fact]
    public async Task Registered_correction_renders_a_pdf()
    {
        var db = TestDb.CreateUnitOfWork();
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);
        var settings = new NfeServiceSettings(NfeEnvironment.Homologation, "SP", certificate, NfeServiceSettings.DefaultSchemasDirectory);
        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.Input(), settings);
        var invoiceKey = Guid.NewGuid();
        db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoiceKey, Kind = NfeXmlKind.Authorized, CreatedAt = DateTime.Now,
            Xml = NfeProcComposer.Compose(signed.Xml, FakeNfeSefazClient.Authorized(signed.AccessKey).ProtocolXml!),
        });
        db.Context.SalesInvoiceNfeCorrections.Add(new SalesInvoiceNfeCorrection
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoiceKey, Sequence = 1, Text = "Placa correta XYZ9K87",
            StatusCode = 135, Reason = "Evento registrado e vinculado a NF-e", CreatedAt = DateTime.Now,
            ProcEventXml = CorrectionProcEvent(signed.AccessKey),
        });
        await db.SaveChangesAsync();

        var (pdf, fileName) = await Service(db).GenerateCorrectionPdfAsync(invoiceKey, 1);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal($"{signed.AccessKey}-cce-1.pdf", fileName);
    }

    [Fact]
    public async Task Missing_correction_is_not_found()
    {
        var db = TestDb.CreateUnitOfWork();

        await Assert.ThrowsAsync<NotFoundException>(() => Service(db).GenerateCorrectionPdfAsync(Guid.NewGuid(), 1));
    }

    private static DanfeReportService Service(UnitOfWork db)
    {
        var environment = new TestWebHostEnvironment(Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CompanyLogoPath"] = "wwwroot/images/logo.png" })
            .Build();
        return new DanfeReportService(db, environment,
            new ReportHeaderService(environment, configuration, NullLogger<ReportHeaderService>.Instance));
    }

    /// <summary>procEventoNFe real de CC-e (serializado pela Zeus), como o envio grava.</summary>
    private static string CorrectionProcEvent(string accessKey) => DFe.Utils.FuncoesXml.ClasseParaXmlString(
        new NFe.Classes.Servicos.Consulta.procEventoNFe
        {
            versao = "1.00",
            evento = new NFe.Classes.Servicos.Evento.evento
            {
                versao = "1.00",
                infEvento = new NFe.Classes.Servicos.Evento.infEventoEnv
                {
                    Id = $"ID110110{accessKey}01", cOrgao = DFe.Classes.Entidades.Estado.SP,
                    tpAmb = DFe.Classes.Flags.TipoAmbiente.Homologacao, CNPJ = accessKey.Substring(6, 14), chNFe = accessKey,
                    dhEvento = new DateTimeOffset(2026, 10, 6, 9, 15, 0, TimeSpan.FromHours(-3)),
                    tpEvento = NFe.Classes.Servicos.Tipos.NFeTipoEvento.TeNfeCartaCorrecao, nSeqEvento = 1, verEvento = "1.00",
                    detEvento = new NFe.Classes.Servicos.Evento.detEvento
                    {
                        versao = "1.00", descEvento = "Carta de Correcao", xCorrecao = "Placa correta XYZ9K87",
                        xCondUso = "A Carta de Correcao e disciplinada pelo paragrafo 1o-A do art. 7o do Convenio S/N",
                    },
                },
            },
            retEvento = new NFe.Classes.Servicos.Evento.retEvento
            {
                versao = "1.00",
                infEvento = new NFe.Classes.Servicos.Evento.infEventoRet
                {
                    tpAmb = DFe.Classes.Flags.TipoAmbiente.Homologacao, cOrgao = DFe.Classes.Entidades.Estado.SP,
                    cStat = 135, xMotivo = "Evento registrado e vinculado a NF-e", chNFe = accessKey,
                    tpEvento = NFe.Classes.Servicos.Tipos.NFeTipoEvento.TeNfeCartaCorrecao, nSeqEvento = 1,
                    dhRegEvento = new DateTimeOffset(2026, 10, 6, 9, 15, 5, TimeSpan.FromHours(-3)), nProt = "135260000000201",
                },
            },
        });
```

Faltando `using`, acrescente `using SiagroB1.Infra;`. Se os nomes `retEvento`/`infEventoRet` da Zeus forem outros,
copie os do helper `CancellationEvent()` do `NfeSefazResponseMapperTests`.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~DanfeReportServiceTests"`
Expected: build FAIL (`GenerateCorrectionPdfAsync` não existe).

- [ ] **Step 4: Implement**

`DanfeReportService.cs` (acrescente `using DFe.Utils;` e `using NFe.Classes.Servicos.Consulta;`; mantenha o
restante):

```csharp
    /// <summary>PDF de uma CC-e do documento de saída: procNFe autorizado + procEventoNFe da carta (NFeEvento.frx da Zeus).</summary>
    public async Task<(byte[] Pdf, string FileName)> GenerateCorrectionPdfAsync(Guid invoiceKey, int sequence)
    {
        var eventXml = await db.Context.SalesInvoiceNfeCorrections.AsNoTracking()
            .Where(x => x.SalesInvoiceKey == invoiceKey && x.Sequence == sequence)
            .Select(x => x.ProcEventXml)
            .FirstOrDefaultAsync() ?? throw new NotFoundException("Carta de correção não encontrada ou sem o XML do evento.");
        var xml = await db.Context.SalesInvoiceNfeXmls.LatestAuthorizedXmlAsync(invoiceKey);
        return RenderCorrection(xml, eventXml, sequence);
    }

    /// <summary>PDF de uma CC-e do documento de entrada.</summary>
    public async Task<(byte[] Pdf, string FileName)> GeneratePurchaseCorrectionPdfAsync(Guid invoiceKey, int sequence)
    {
        var eventXml = await db.Context.PurchaseInvoiceNfeCorrections.AsNoTracking()
            .Where(x => x.PurchaseInvoiceKey == invoiceKey && x.Sequence == sequence)
            .Select(x => x.ProcEventXml)
            .FirstOrDefaultAsync() ?? throw new NotFoundException("Carta de correção não encontrada ou sem o XML do evento.");
        var xml = await db.Context.PurchaseInvoiceNfeXmls.LatestAuthorizedXmlAsync(invoiceKey);
        return RenderCorrection(xml, eventXml, sequence);
    }

    private (byte[] Pdf, string FileName) RenderCorrection(string xml, string eventXml, int sequence)
    {
        var proc = new nfeProc().CarregarDeXmlString(xml);
        var procEvent = FuncoesXml.XmlStringParaClasse<procEventoNFe>(eventXml);
        var template = Path.Combine(env.ContentRootPath, "ThirdParty", "Zeus-LGPL", "NFe.Danfe.Base", "NFe", "NFeEvento.frx");

        FastReport.Utils.Config.WebMode = true;

        var report = new DanfeFrEvento(proc, procEvent, new ConfiguracaoDanfeNfe(header.LogoBytes()),
            desenvolvedor: "IDX Consultoria e Sistemas", arquivoRelatorio: template);
        using var relatorio = report.Relatorio;

        return (report.ExportarPdf(), $"{proc.protNFe.infProt.chNFe}-cce-{sequence}.pdf");
    }
```

Atualize o `<summary>` da classe para citar a CC-e.

⚠️ `NotFoundException` em `SiagroB1.Domain.Exceptions` (o controller já trata). O "sem XML autorizado" já lança
`NotFoundException` dentro de `LatestAuthorizedXmlAsync`.

`DanfeController.cs`:

```csharp
    [HttpPost("{key:guid}/cce/{sequence:int}/print")]
    public async Task<IActionResult> CorrectionReport(Guid key, int sequence) =>
        await ExecuteReportAsync(() => service.GenerateCorrectionPdfAsync(key, sequence));

    [HttpPost("purchase-invoices/{key:guid}/cce/{sequence:int}/print")]
    public async Task<IActionResult> PurchaseCorrectionReport(Guid key, int sequence) =>
        await ExecuteReportAsync(() => service.GeneratePurchaseCorrectionPdfAsync(key, sequence));
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~DanfeReportServiceTests"`
Expected: PASS. Se o FastReport recusar o script do `NFeEvento.frx` (CS0117 e afins), confirme que o módulo de
inicialização dos testes chama `DanfeReportService.DisableScriptStubs()` (`Support/FastReportScriptConfig.cs`). Se o
erro for outro, use superpowers:systematic-debugging, **sem editar o .frx** (LGPL, regra do README).

- [ ] **Step 6: Render once and look at it**

Salve o PDF do teste num arquivo do scratchpad (teste temporário ou `dotnet run` do `render.cs`, ver a memória
"Ver um .frx renderizado localmente"). Converta para PNG e confira que aparecem a chave, a sequência 1, o texto da
correção e o protocolo.

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Reports/ThirdParty/Zeus-LGPL/NFe.Danfe.Base/NFe/NFeEvento.frx SiagroB1.Reports/ThirdParty/Zeus-LGPL/NFe.Danfe.OpenFast/NFe/DanfeFrEvento.cs
git commit -m "feat(reports): PDF da carta de correção pelo layout de evento da Zeus" -- SiagroB1.Reports SiagroB1.Application.Tests/Reports/DanfeReportServiceTests.cs
```

---

### Task 8: Frontend — helpers, rotas e testes

Repo: `siagro-b1-frontend` (branch `feature/nfe-correction-letter`).

**Files:**
- Modify: `webapp/helpers/NfeHelpers.ts`
- Modify: `webapp/model/ServerRoutes.ts`
- Test: `webapp/test/unit/helpers/NfeHelpers.qunit.ts`

**Interfaces:**
- Produces: `NFE_CORRECTION_MIN = 15`, `NFE_CORRECTION_MAX = 1000`, `normalizeNfeCorrectionText(text: string): string`,
  `invalidNfeCorrectionChars(text: string): string[]`, `isValidNfeCorrectionText(text: string): boolean`,
  `canSendNfeCorrection(nfeStatus?: string, invoiceStatus?: string, issuerType?: string): boolean`,
  `correctionViewerOptions(doc: DanfeDocument, sequence: number): { title: string; fileName: string }`;
  `NfeOutcome.ImportedCorrections?: number`. Rotas: `salesInvoicesSendNfeCorrection`, `salesInvoicesNfeCorrectionXml`,
  `salesInvoicesNfeCorrections`, `purchaseInvoicesSendNfeCorrection`, `purchaseInvoicesNfeCorrectionXml`,
  `purchaseInvoicesNfeCorrections`.

- [ ] **Step 1: Write the failing tests**

No `NfeHelpers.qunit.ts`, acrescente os nomes novos ao `import` existente e, no fim:

```ts
QUnit.module("NfeHelpers - carta de correção");

QUnit.test("texto é normalizado como no servidor", function (assert) {
	assert.strictEqual(normalizeNfeCorrectionText("  Placa\r\nABC\t  1  "), "Placa ABC 1");
	assert.strictEqual(normalizeNfeCorrectionText("\u201CX\u201D \u2013 \u2018Y\u2019\u2026"), "\"X\" - 'Y'...");
	assert.strictEqual(normalizeNfeCorrectionText(null), "");
});

QUnit.test("caracteres fora do Latin-1 são apontados uma vez", function (assert) {
	assert.deepEqual(invalidNfeCorrectionChars("Valor € errado ✓ e € de novo"), ["€", "✓"]);
	assert.deepEqual(invalidNfeCorrectionChars("Correção do endereço nº 10"), []);
});

QUnit.test("texto válido tem 15 a 1000 caracteres normalizados e nada fora do Latin-1", function (assert) {
	assert.notOk(isValidNfeCorrectionText("curta    \n   "));
	assert.ok(isValidNfeCorrectionText("x".repeat(15)));
	assert.ok(isValidNfeCorrectionText("x".repeat(1000)));
	assert.notOk(isValidNfeCorrectionText("x".repeat(1001)));
	assert.notOk(isValidNfeCorrectionText("Texto longo o bastante €"));
});

QUnit.test("carta só para NF-e autorizada de documento ativo e emissão própria", function (assert) {
	assert.ok(canSendNfeCorrection("Authorized", "Confirmed"));
	assert.ok(canSendNfeCorrection("Authorized", "Pending", "Own"));
	assert.notOk(canSendNfeCorrection("Authorized", "Confirmed", "ThirdParty"));
	assert.notOk(canSendNfeCorrection("Authorized", "Cancelled"));
	assert.notOk(canSendNfeCorrection("Cancelled", "Cancelled"));
	assert.notOk(canSendNfeCorrection("Processing", "Pending"));
});

QUnit.test("título e arquivo do PDF da carta", function (assert) {
	assert.deepEqual(
		correctionViewerOptions({ ChaveNFe: "3526", TaxDocumentNumber: "000000009", TaxDocumentSeries: "9" }, 2),
		{ title: "Carta de Correção nº 2 – NF-e nº 9 série 9", fileName: "3526-cce-2.pdf" });
});

QUnit.test("consulta avisa as cartas importadas", function (assert) {
	const message = nfeOutcomeMessage({ NfeStatus: "Authorized", StatusCode: "100", InvoiceStatus: "Confirmed", ImportedCorrections: 2 } as NfeOutcome);
	assert.strictEqual(message.type, "success");
	assert.ok(message.text.endsWith(" 2 carta(s) de correção importada(s) da SEFAZ."));
});
```

Se o `NfeOutcome` não for exportado como tipo, importe-o com `import type`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `yarn ts-typecheck`
Expected: FAIL (nomes não exportados). Para rodar só este módulo de QUnit, use o mesmo comando dos outros helpers
(confira em `package.json`/`ui5-test-runner`; por exemplo `yarn ui5-test-runner --url http://localhost:8080/test/unit/unitTests.qunit.html`
com `yarn start` em outro terminal). ⚠️ `yarn test` completo não serve (gate de cobertura).

- [ ] **Step 3: Implement in `NfeHelpers.ts`**

No tipo `NfeOutcome`, acrescente `ImportedCorrections?: number;`.

No `nfeOutcomeMessage`, renomeie o corpo atual para `function baseNfeOutcomeMessage(outcome)` (privada) e exporte:

```ts
export function nfeOutcomeMessage(outcome: NfeOutcome): { type: "success" | "warning" | "error"; text: string } {
  const message = baseNfeOutcomeMessage(outcome);
  return outcome.ImportedCorrections > 0
    ? { ...message, text: `${message.text} ${outcome.ImportedCorrections} carta(s) de correção importada(s) da SEFAZ.` }
    : message;
}
```

No fim do arquivo:

```ts
export const NFE_CORRECTION_MIN = 15;
export const NFE_CORRECTION_MAX = 1000;

const TYPOGRAPHIC: Record<string, string> = {
  "\u2018": "'", "\u2019": "'", "\u201C": "\"", "\u201D": "\"", "\u2013": "-", "\u2014": "-", "\u2026": "...",
};

/** xCorrecao como o servidor envia: aspas/travessão/reticências em ASCII, todo espaço em branco vira um espaço. */
export function normalizeNfeCorrectionText(text: string): string {
  return Array.from(text ?? "")
    .map((c) => TYPOGRAPHIC[c] ?? (/\s/.test(c) ? " " : c))
    .join("")
    .replace(/ {2,}/g, " ")
    .trim();
}

/** Caracteres que a SEFAZ recusa (fora de U+0020–U+00FF), um de cada, na ordem em que aparecem. */
export function invalidNfeCorrectionChars(text: string): string[] {
  return [...new Set(Array.from(normalizeNfeCorrectionText(text)).filter((c) => c < " " || c > "\u00FF"))];
}

/** Texto da CC-e: 15 a 1000 caracteres depois da normalização e nada fora do Latin-1 — a mesma conta do servidor. */
export function isValidNfeCorrectionText(text: string): boolean {
  const length = normalizeNfeCorrectionText(text).length;
  return length >= NFE_CORRECTION_MIN && length <= NFE_CORRECTION_MAX && invalidNfeCorrectionChars(text).length === 0;
}

/** "Carta de Correção": NF-e autorizada, documento ativo e, na entrada, emissão própria. */
export function canSendNfeCorrection(nfeStatus?: string, invoiceStatus?: string, issuerType?: string): boolean {
  return nfeStatus === "Authorized" && invoiceStatus !== "Cancelled" && (issuerType === undefined || issuerType === "Own");
}

/** Título e arquivo do PDF da CC-e no visualizador (mesmo padrão do DANFE). */
export function correctionViewerOptions(doc: DanfeDocument, sequence: number): { title: string; fileName: string } {
  const danfe = danfeViewerOptions(doc);
  const key = (doc.ChaveNFe ?? "").trim();
  return {
    title: danfe.title.replace(/^DANFE/, `Carta de Correção nº ${sequence}`),
    fileName: key ? `${key}-cce-${sequence}.pdf` : `cce-${sequence}.pdf`,
  };
}
```

`ServerRoutes.ts`, depois do bloco do cancelamento:

```ts
  // CC-e (evento 110110): envio com o texto, XML de cada carta e histórico (entity set somente leitura).
  salesInvoicesSendNfeCorrection: '/odata/SalesInvoicesSendNfeCorrection',
  salesInvoicesNfeCorrectionXml: '/odata/SalesInvoicesNfeCorrectionXml',
  salesInvoicesNfeCorrections: '/odata/SalesInvoicesNfeCorrections',
  purchaseInvoicesSendNfeCorrection: '/odata/PurchaseInvoicesSendNfeCorrection',
  purchaseInvoicesNfeCorrectionXml: '/odata/PurchaseInvoicesNfeCorrectionXml',
  purchaseInvoicesNfeCorrections: '/odata/PurchaseInvoicesNfeCorrections',
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `yarn ts-typecheck`, `yarn lint` e o QUnit de `NfeHelpers`.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add webapp/helpers/NfeHelpers.ts webapp/model/ServerRoutes.ts webapp/test/unit/helpers/NfeHelpers.qunit.ts
git commit -m "feat(invoice): regras do texto da carta de correção e rotas da CC-e" -- webapp/helpers/NfeHelpers.ts webapp/model/ServerRoutes.ts webapp/test/unit/helpers/NfeHelpers.qunit.ts
```

(Confira a convenção de mensagem do frontend no `siagro-b1-frontend/CLAUDE.md` antes do primeiro commit e ajuste
tipo/escopo se ela for diferente.)

---

### Task 9: Frontend — diálogo, tabela e detalhe da saída e da entrada

**Files:**
- Create: `webapp/dialogs/NfeCorrectionDialog.ts`
- Modify: `webapp/dialogs/DanfeViewer.ts`
- Create: `webapp/fragments/NfeCorrections.fragment.xml`
- Modify: `webapp/view/salesInvoices/Detail.view.xml`, `webapp/controller/salesInvoices/Detail.controller.ts`
- Modify: `webapp/view/purchaseInvoices/Detail.view.xml`, `webapp/controller/purchaseInvoices/Detail.controller.ts`

**Interfaces:**
- Consumes: helpers e rotas (Task 8); OData da Task 6; rotas do Reports da Task 7.
- Produces: `openNfeCorrectionDialog(owner: Control, previousText: string): Promise<string>` (texto normalizado ou
  `null`); `openNfeCorrectionViewer(printRoute: string, ctx: Context, sequence: number)`; handlers
  `onNfeCorrection`, `onNfeCorrectionXml`, `onNfeCorrectionPdf` nos dois controllers; tabela id `nfeCorrectionsTable`.

- [ ] **Step 1: Dialog**

`webapp/dialogs/NfeCorrectionDialog.ts`:

```ts
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import TextArea from "sap/m/TextArea";
import MessageStrip from "sap/m/MessageStrip";
import VBox from "sap/m/VBox";
import Label from "sap/m/Label";
import Control from "sap/ui/core/Control";
import { ValueState } from "sap/ui/core/library";
import {
  NFE_CORRECTION_MAX, invalidNfeCorrectionChars, isValidNfeCorrectionText, normalizeNfeCorrectionText,
} from "siagrob1/helpers/NfeHelpers";

/**
 * Pede o texto da CC-e, pré-preenchido com a última carta (a nova substitui as anteriores). Devolve o texto
 * normalizado ou `null` se o usuário desistiu. O botão segue a mesma conta do servidor (15 a 1000, Latin-1).
 */
export function openNfeCorrectionDialog(owner: Control, previousText: string): Promise<string> {
  return new Promise((resolve) => {
    let result: string = null;

    const confirm = new Button({
      text: "Enviar carta", type: "Emphasized", enabled: false,
      press: () => { result = normalizeNfeCorrectionText(text.getValue()); dialog.close(); },
    });

    const validate = () => {
      const invalid = invalidNfeCorrectionChars(text.getValue());
      text.setValueState(invalid.length > 0 ? ValueState.Error : ValueState.None);
      text.setValueStateText(invalid.length > 0 ? `Caracteres não aceitos pela SEFAZ: ${invalid.join(" ")}` : "");
      confirm.setEnabled(isValidNfeCorrectionText(text.getValue()));
    };

    // Mesmo contador do cancelamento: showExceededText mostra o restante e o excesso; o limite fica no botão.
    const text = new TextArea({
      width: "100%", rows: 8, maxLength: NFE_CORRECTION_MAX, showExceededText: true,
      value: previousText ?? "", placeholder: "Mínimo de 15 caracteres",
      liveChange: validate,
    });

    const dialog = new Dialog({
      title: "Carta de Correção",
      contentWidth: "40rem",
      content: new VBox({
        items: [
          new MessageStrip({
            type: "Warning", showIcon: true,
            text: "A nova carta substitui as anteriores: repita todas as correções. Não corrige valores, impostos, " +
              "quantidades, emitente/destinatário nem datas. O envio à SEFAZ não pode ser desfeito.",
          }),
          new Label({ text: "Correção", required: true, labelFor: text }),
          text,
        ],
      }).addStyleClass("sapUiSmallMargin"),
      beginButton: confirm,
      endButton: new Button({ text: "Voltar", press: () => dialog.close() }),
      afterOpen: validate,
      afterClose: () => { dialog.destroy(); resolve(result); },
    });

    owner.addDependent(dialog);
    dialog.open();
  });
}
```

`DanfeViewer.ts`, acrescente (e importe `correctionViewerOptions`):

```ts
/** PDF de uma CC-e no mesmo visualizador do DANFE. `printRoute` é a mesma do DANFE do documento. */
export async function openNfeCorrectionViewer(printRoute: string, ctx: Context, sequence: number): Promise<void> {
  await openAttachmentViewer({
    url: `${printRoute}/${ctx.getProperty("Key") as string}/cce/${sequence}/print`,
    method: "POST",
    errorMessage: "Falha ao gerar a carta de correção.",
    ...correctionViewerOptions({
      ChaveNFe: ctx.getProperty("ChaveNFe") as string,
      TaxDocumentNumber: ctx.getProperty("TaxDocumentNumber") as string,
      TaxDocumentSeries: ctx.getProperty("TaxDocumentSeries") as string,
    }, sequence),
  });
}
```

- [ ] **Step 2: Shared table fragment**

`webapp/fragments/NfeCorrections.fragment.xml`:

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:t="sap.ui.table"
    xmlns:core="sap.ui.core"
>
  <!--
    CC-e registradas da NF-e (saída e entrada). Somente leitura: nascem no envio e na consulta.
    `$$ownRequest`: sem ele vira $expand no GET do documento (o get service não inclui NfeCorrections) e a
    tabela vem vazia; com ele, a tabela é refrescada sozinha depois do envio. O sorter manda $orderby
    explícito, que sobrevive à paginação da sap.ui.table.
  -->
  <t:Table
    id="nfeCorrectionsTable"
    class="sapUiSizeCondensed"
    alternateRowColors="true"
    selectionMode="None"
    enableBusyIndicator="true"
    visibleRowCountMode="Auto"
    minAutoRowCount="2"
    rows="{
      path: 'NfeCorrections',
      parameters: { '$$ownRequest': true, '$select': 'Key,Sequence,Text,Protocol,RegisteredAt,CreatedBy' },
      sorter: { path: 'Sequence', descending: true }
    }"
    noData="Nenhuma carta de correção registrada."
    >
    <t:extension>
      <OverflowToolbar>
        <content>
          <Title text="Cartas de correção" />
        </content>
      </OverflowToolbar>
    </t:extension>
    <t:columns>
      <t:Column label="Seq." width="4rem" hAlign="End">
        <t:template><Text wrapping="false" text="{Sequence}" /></t:template>
      </t:Column>
      <t:Column label="Registrada em" width="10rem">
        <t:template>
          <Text wrapping="false" text="{ path: 'RegisteredAt', targetType: 'any', formatter: '.formatter.formatDateTime' }" />
        </t:template>
      </t:Column>
      <t:Column label="Protocolo" width="10rem">
        <t:template><Text wrapping="false" text="{Protocol}" /></t:template>
      </t:Column>
      <t:Column label="Correção">
        <t:template><Text wrapping="false" text="{Text}" tooltip="{Text}" /></t:template>
      </t:Column>
      <t:Column label="Usuário" width="9rem">
        <t:template><Text wrapping="false" text="{CreatedBy}" /></t:template>
      </t:Column>
      <t:Column width="7rem" hAlign="Center">
        <t:template>
          <HBox>
            <Button icon="sap-icon://download" type="Transparent" tooltip="XML da carta" press=".onNfeCorrectionXml" />
            <Button icon="sap-icon://print" type="Transparent" tooltip="Imprimir carta" press=".onNfeCorrectionPdf" />
          </HBox>
        </t:template>
      </t:Column>
    </t:columns>
  </t:Table>
</core:FragmentDefinition>
```

⚠️ Se a Task 6 tiver removido a rota aninhada (conflito), troque o `path` por `/SalesInvoicesNfeCorrections` com
`filters` pela chave. Nesse caso o fragmento deixa de ser compartilhável sem parâmetro, e é preciso aplicar o filtro
no controller com `binding.filter(...)` no `routeMatched`. Prefira manter a rota aninhada.

- [ ] **Step 3: Sales detail (view + controller)**

`webapp/view/salesInvoices/Detail.view.xml` — em `<uxap:actions>`, depois do botão "Consultar situação":

```xml
          <Button text="Carta de Correção" icon="sap-icon://edit-outside" press=".onNfeCorrection"
            visible="{= ${path: 'NfeStatus', targetType: 'any'} === 'Authorized' &amp;&amp; ${path: 'InvoiceStatus', targetType: 'any'} !== 'Cancelled' }" />
```

Na seção "NF-e", depois da subseção do `NfePanel`:

```xml
					<uxap:ObjectPageSubSection title=" " titleUppercase="false"
            visible="{= ${path: 'NfeStatus', targetType: 'any'} === 'Authorized' || ${path: 'NfeStatus', targetType: 'any'} === 'Cancelled' }">
						<core:Fragment fragmentName="siagrob1.fragments.NfeCorrections" type="XML" />
					</uxap:ObjectPageSubSection>
```

`webapp/controller/salesInvoices/Detail.controller.ts`:
- imports: `openNfeCorrectionDialog` de `siagrob1/dialogs/NfeCorrectionDialog`, `openNfeCorrectionViewer` de
  `siagrob1/dialogs/DanfeViewer`, `canSendNfeCorrection` de `NfeHelpers`, `Button$PressEvent` de `sap/m/Button`.
- Generalize o download: troque `downloadNfeXml(route: string, suffix: string)` por
  `downloadNfeXml(url: string, fileName: string)`. O corpo usa `fetch(url)` e `link.download = fileName`. Atualize os
  dois chamadores existentes:

```ts
  async onNfeXml() {
    const ctx = this.getView().getBindingContext() as Context;
    await this.downloadNfeXml(`${ServerRoutes.salesInvoicesNfeXml}(Key=${ctx.getProperty("Key") as string})`,
      `${ctx.getProperty("ChaveNFe") as string}-procNFe.xml`);
  }

  async onNfeCancellationXml() {
    const ctx = this.getView().getBindingContext() as Context;
    await this.downloadNfeXml(`${ServerRoutes.salesInvoicesNfeCancellationXml}(Key=${ctx.getProperty("Key") as string})`,
      `${ctx.getProperty("ChaveNFe") as string}-procEventoNFe.xml`);
  }
```

- Handlers novos:

```ts
  /** "Carta de Correção": diálogo pré-preenchido com a última carta; 200 traz a sequência registrada. */
  async onNfeCorrection() {
    const ctx = this.getView().getBindingContext() as Context;
    if (!ctx || !canSendNfeCorrection(ctx.getProperty("NfeStatus") as string, ctx.getProperty("InvoiceStatus") as string)) {
      return;
    }

    const key = ctx.getProperty("Key") as string;
    const last = await sendJson("GET",
      `${ServerRoutes.salesInvoicesNfeCorrections}?$filter=SalesInvoiceKey eq ${key}&$orderby=Sequence desc&$top=1&$select=Text`);
    const previous = last.ok ? (odataValue<{ Text: string }[]>(last.data) ?? [])[0]?.Text ?? "" : "";

    const text = await openNfeCorrectionDialog(this.getView(), previous);
    if (text === null) {
      return;
    }

    this.setBusy(true);
    try {
      const result = await sendJson("POST", ServerRoutes.salesInvoicesSendNfeCorrection, { Key: key, Text: text });
      if (!result.ok) {
        MessageBox.error(result.message);
        return;
      }

      const outcome = odataValue<{ Sequence: number }>(result.data);
      MessageToast.show(`Carta de correção nº ${outcome.Sequence} registrada na SEFAZ.`);
    } finally {
      this.refreshNfeCorrections();
      this.setBusy(false);
    }
  }

  async onNfeCorrectionXml(event: Button$PressEvent) {
    const row = (event.getSource() as Control).getBindingContext() as Context;
    const ctx = this.getView().getBindingContext() as Context;
    const sequence = row.getProperty("Sequence") as number;
    await this.downloadNfeXml(
      `${ServerRoutes.salesInvoicesNfeCorrectionXml}(Key=${ctx.getProperty("Key") as string},Sequence=${sequence})`,
      `${ctx.getProperty("ChaveNFe") as string}-cce-${sequence}-procEventoNFe.xml`);
  }

  async onNfeCorrectionPdf(event: Button$PressEvent) {
    const row = (event.getSource() as Control).getBindingContext() as Context;
    await openNfeCorrectionViewer(ServerRoutes.danfeReport, this.getView().getBindingContext() as Context,
      row.getProperty("Sequence") as number);
  }

  private refreshNfeCorrections() {
    try {
      (this.byId("nfeCorrectionsTable") as Table)?.getBinding("rows")?.refresh();
    } catch {
      // tabela ainda não carregada: o próximo bind traz as cartas
    }
  }
```

- No `onConsultNfe` existente, chame `this.refreshNfeCorrections()` depois do `runNfeAction` (a consulta pode
  importar cartas).
- Se `Control`/`Table` ainda não estiverem importados, importe `Control` de `sap/ui/core/Control` e `Table` de
  `sap/ui/table/Table`.

- [ ] **Step 4: Purchase detail (view + controller)**

`webapp/view/purchaseInvoices/Detail.view.xml` — no toolbar, depois do botão "Consultar situação":

```xml
        <Button text="Carta de Correção" icon="sap-icon://edit-outside" press=".onNfeCorrection"
                visible="{= ${path: 'NfeStatus', targetType: 'any'} === 'Authorized' &amp;&amp; ${path: 'InvoiceStatus', targetType: 'any'} !== 'Cancelled' &amp;&amp; ${path: 'IssuerType', targetType: 'any'} === 'Own' }" />
```

Na seção "NF-e", depois da subseção do `NfePanel`, a mesma subseção da saída com
`siagrob1.fragments.NfeCorrections`.

`webapp/controller/purchaseInvoices/Detail.controller.ts`: os mesmos handlers e a mesma generalização do
`downloadNfeXml`, trocando:
- `ServerRoutes.salesInvoices*` → `ServerRoutes.purchaseInvoices*`;
- `SalesInvoiceKey` → `PurchaseInvoiceKey` no `$filter`;
- `ServerRoutes.danfeReport` → `ServerRoutes.purchaseInvoicesDanfeReport`;
- a guarda: `canSendNfeCorrection(NfeStatus, InvoiceStatus, ctx.getProperty("IssuerType") as string)`.

- [ ] **Step 5: Gates**

Run: `yarn ts-typecheck`, `yarn lint`, e o ui5lint com o MCP `run_ui5_linter` (ou `npx @ui5/linter`) nos arquivos
tocados.
Expected: sem erros novos. Compare a contagem do ui5lint com o `main` (referência: 920).

- [ ] **Step 6: Commit**

```bash
git add webapp/dialogs/NfeCorrectionDialog.ts webapp/fragments/NfeCorrections.fragment.xml
git commit -m "feat(invoice): carta de correção no detalhe da saída e da entrada" -- webapp/dialogs webapp/fragments/NfeCorrections.fragment.xml webapp/view/salesInvoices/Detail.view.xml webapp/view/purchaseInvoices/Detail.view.xml webapp/controller/salesInvoices/Detail.controller.ts webapp/controller/purchaseInvoices/Detail.controller.ts
```

---

### Task 10: Verificação ponta a ponta (homologação)

**Files:** nenhum código, salvo correções achadas (cada uma com teste e commit próprios).

- [ ] **Step 1: Full gates**

Backend: `dotnet build SiagroB1.sln` (0 erros), `dotnet test SiagroB1.Fiscal.Tests`, `dotnet test SiagroB1.Application.Tests`.
Frontend: `yarn ts-typecheck`, `yarn lint`, QUnit dos helpers.
Registre as contagens. Referência no main: Application 3139, Fiscal 205.

- [ ] **Step 2: Apply the migration to `CEAGUI_SIAGRO_DEV` only**

```bash
cd /c/Projetos/SiagroB1/siagro-b1-backend
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

⚠️ Sempre com o ambiente explícito (a memória "db-migration profile é perigoso"). Confira no SQL as duas tabelas e
os dois índices únicos.

- [ ] **Step 3: Start the stack (profile `ceagui`)**

Gateway primeiro; espere `Loading proxy data from config`; depois Web e Reports, todos com `--launch-profile ceagui`.
Frontend com `yarn start:dev`. Login `admin`/`1234`. Navegue pelo **menu** (não por URL com hash).

- [ ] **Step 4: E2E (Playwright) — ⚠️ pedir confirmação ao usuário antes de CADA envio à SEFAZ de homologação**

1. Saída autorizada (emitir uma nova se não houver, série 9; próximo número livre: confira na configuração):
   "Carta de Correção" → texto com aspas curvas e Enter → "Enviar carta" → toast "nº 1"; a tabela mostra a carta.
2. Reabrir o diálogo: pré-preenchido com o texto da nº 1. Enviar a nº 2.
3. "XML" e "Imprimir" da nº 2: XML com `tpEvento 110110` e `nSeqEvento 2`; PDF com o texto e o protocolo.
4. "Consultar situação": nenhuma carta duplicada; anote se a SEFAZ-SP devolveu os `procEventoNFe` das cartas (risco
   §12 do spec). Se não devolveu, registre no ledger: o caminho 573 não se resolve sozinho.
5. Entrada própria autorizada: uma carta; a entrada de terceiro não mostra o botão.
6. No banco: linhas em `SALES_INVOICE_NFE_CORRECTIONS`/`PURCHASE_INVOICE_NFE_CORRECTIONS` com protocolo e XML; o
   `NfeStatusCode` do documento continua `100`.

Guarde XML e PDF em `C:\Projetos\SiagroB1\nfe-homologacao-testes\cce-2026-10-06\`.

- [ ] **Step 5: Stop the stack**

Mate os processos das portas 50000, 5246, 8081/58000 e 8080 por PID e confira de novo no fim (a memória "Subir a
stack local": o file watcher ressuscita os processos).

- [ ] **Step 6: Final review and report**

superpowers:verification-before-completion. Revisão do branch inteiro (requesting-code-review). Atualize a memória
do projeto (nova `nfe-correction-letter-feature.md` + linha no `MEMORY.md`). Relate ao usuário: commits por repo,
contagens de teste, o que foi visto na homologação e o que ficou de fora. **Sem merge e sem push.**
