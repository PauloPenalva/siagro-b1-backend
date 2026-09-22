# Minutas de contrato — Fase 2 (assinatura eletrônica: provedor D4Sign, webhook e reconciliação)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** a minuta em rascunho é enviada para assinatura eletrônica no D4Sign, o sistema acompanha o progresso por webhook e por job de reconciliação, e a minuta assinada vira anexo do contrato com o `SignatureStatus` do contrato atualizado.

**Architecture:** um contrato `IESignatureProvider` no Domain isola o provedor; `D4SignProvider` (Infra) é um `HttpClient` tipado que nunca lança por falha do provedor — devolve resultado com `Succeeded`/`ErrorMessage`/`Transient`, no molde de `WhatsAppSendResult`. Duas portas de escrita: `ContractDraftsSendToSignatureService` (síncrono, o usuário espera) e `ContractDraftsApplyProviderStateService` (idempotente, usado igualmente pelo webhook e pelo job de reconciliação). O estado **nunca** vem do payload do webhook — vem sempre de `GetStateAsync`, porque o D4Sign não assina o webhook.

**Tech Stack:** .NET 10, EF Core 10 (SQL Server), ASP.NET Core OData 9, Hangfire, xUnit + EF InMemory.

**Spec:** `docs/superpowers/specs/2026-09-21-contract-drafts-esignature-design.md` — seções "Ciclo de vida da minuta", "Provedor de assinatura", "Webhook", "Reconciliação", "Configuração", "API (OData)", "Testes".

**Fase 1 (pré-requisito, concluída):** `docs/superpowers/plans/2026-09-21-contract-drafts-phase1-backend.md`, branch `feature/contract-drafts-esignature`, commits `0ddc9ea`…`186f63e`. Entidades, renderer, resolver, PDF por Chromium, serviços de CRUD da minuta, actions/functions OData e menus já existem.

## Global Constraints

- **Identificadores de código em inglês; texto que o usuário lê em pt-BR.** Mensagens de negócio, rótulos e comentários explicativos: pt-BR.
- **Todo arquivo novo é staged imediatamente** com `git add <caminho>`.
- **Nunca `git push`.** Commit por task, no branch `feature/contract-drafts-esignature`.
- **Mensagem de commit:** `tipo(escopo): descrição em pt-BR, imperativo, minúscula, sem ponto final`. Scope: `platform`. Rodapé `DB: <NomeDaMigration>` obrigatório em todo commit que contenha migration. Rodapé `Co-Authored-By:` com o modelo que escreveu o código.
- **Valor novo de enum entra sempre no fim da numeração.**
- **Ambiente explícito em todo comando de banco:** `ASPNETCORE_ENVIRONMENT=Yokotobi-Development` antes de `dotnet ef database update`.
- **Rodar o app localmente:** `dotnet run --project SiagroB1.Web --launch-profile yktb`. **Sem `--launch-profile`, o profile `dev` aponta para `129.121.53.204/MHAGRO_SIAGRO_HOM`, homologação de outro cliente.**
- **Segredos nunca entram em `LastError`, em log nem em mensagem de erro.** Mensagens do D4Sign são truncadas em **300 caracteres**; `HttpRequestException.Message` é descartado, como em `PlugZapiWhatsAppSender`.
- **Sem `IOptions<T>`:** configuração lida inline com `IConfiguration["Signature:..."]`.
- **Serviços "enqueue-only"** (`*ChangeLogService.Register`, `*SetSignatureStatusService.ExecuteAsync`) são chamados **antes** do `SaveChangesAsync` do serviço da operação.
- **A chamada HTTP ao provedor acontece ANTES do `SaveChanges`** e o resultado decide o que se grava. Não existe estado intermediário "enviando".
- **Escada de exceções nos controllers:** `NotFoundException`/`KeyNotFoundException` → 404; `DefaultException`/`BusinessException`/`ApplicationException` → 400; resto → 500.
- **`ODataActionParameters` chega nulo** quando nenhum parâmetro é enviado; enums e datas viajam como string. Reusar `ContractDraftActionParameters` da Fase 1.
- Build: `dotnet build SiagroB1.sln`. Testes: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ContractDrafts"`.

### Assinaturas exatas do que já existe (confira antes de usar)

```csharp
// Fase 1 — SiagroB1.Application/Services/ContractDrafts/
ContractDraftsLoader(AppDbContext context)
  Task<ContractDraft> RequireDraftAsync(Guid key, CancellationToken ct = default)   // Include(d => d.Signers)
  static void RequireEditable(ContractDraft draft)                                  // lança se Status != Draft

ContractDraftsGetPdfService(AppDbContext, ContractDraftsLoader, IHtmlToPdfRenderer,
    PurchaseContractsAttachmentsGetService, SalesContractsAttachmentsGetService)
  Task<(byte[] Bytes, string FileName)> ExecuteAsync(Guid key, CancellationToken ct)

ContractDraftPdfLayout.Wrap(string bodyHtml, string title) : string

// Já existentes no solution
PurchaseContractsSetSignatureStatusService(AppDbContext context, ...)
  Task ExecuteAsync(Guid key, SignatureStatus? status, string userName)             // enqueue-only
SalesContractsSetSignatureStatusService(AppDbContext context, ...)
  Task ExecuteAsync(Guid key, SignatureStatus? status, string userName)             // enqueue-only

PurchaseContractsChangeLogService(AppDbContext context)
  void Register(Guid contractKey, string field, string? oldValue, string? newValue, string userName)
SalesContractsChangeLogService(AppDbContext context)   // mesma assinatura

ContractChangeLogFields.Draft                                   // const string "Draft"
ContractChangeLogFields.DescribeDraft(int sequence, string what) // "Minuta {n} {what}"
```

> ⚠️ **Assimetria real, não a "corrija":** os dois serviços de anexo têm assinaturas diferentes.
> ```csharp
> PurchaseContractsAttachmentsCreateService(IUnitOfWork db, ILogger<...> logger)
>   Task SaveAsync(Guid contractKey, PurchaseContractAttachment attachment)              // SEM userName
> SalesContractsAttachmentsCreateService(IUnitOfWork db, ILogger<...> logger)
>   Task SaveAsync(Guid contractKey, SalesContractAttachment attachment, string userName) // COM userName
> ```
> Na compra, preencha `CreatedBy` no próprio objeto `PurchaseContractAttachment` antes de chamar.

### Enum `SignatoryRole` → código `act` do D4Sign

O enum já existe (`SiagroB1.Domain/Enums/SignatoryRole.cs`). O mapeamento é **identidade**: o valor do enum é o próprio `act`. Ainda assim o provider converte explicitamente, para que uma renumeração futura do enum não mude o contrato com o D4Sign.

| Enum | `act` | Enum | `act` |
|---|---|---|---|
| `Sign` = 1 | `"1"` | `SignAsIssuerEndorserGuarantorSurety` = 9 | `"9"` |
| `Approve` = 2 | `"2"` | `SignAsSurety` = 10 | `"10"` |
| `Acknowledge` = 3 | `"3"` | `SignAsPartyAndSurety` = 11 | `"11"` |
| `SignAsParty` = 4 | `"4"` | `SignAsJointDebtor` = 12 | `"12"` |
| `SignAsWitness` = 5 | `"5"` | `SignAsPartyAndJointDebtor` = 13 | `"13"` |
| `SignAsIntervening` = 6 | `"6"` | | |
| `AcknowledgeReceipt` = 7 | `"7"` | | |
| `SignAsIssuerEndorserGuarantor` = 8 | `"8"` | | |

---

## Ordem e dependências

1 → 2 → 3 → 4 → 5 → 6 → 7 → 8. A Task 2 (provider) depende só da 1 e pode correr em paralelo com 3/4 por outro executor, desde que a 1 esteja commitada. A 6 e a 7 dependem da 4. A 8 depende de 3, 4 e 5.

---

## Task 1: Contrato do provedor, resultados e duplas de teste

**Files:**
- Create: `SiagroB1.Domain/Interfaces/IESignatureProvider.cs` (interface + todos os records de resultado, um arquivo só, como `IWhatsAppSender.cs`)
- Create: `SiagroB1.Application.Tests/Support/FakeESignatureProvider.cs`
- Modify: `SiagroB1.Application.Tests/Support/StubHttpMessageHandler.cs` (fila de respostas)
- Test: `SiagroB1.Application.Tests/ContractDrafts/FakeESignatureProviderTests.cs`

**Interfaces:**
- Produces: `IESignatureProvider` com `SendAsync`, `CancelAsync`, `GetStateAsync`, `DownloadSignedAsync`; records `ESignatureSendRequest`, `ESignatureSigner`, `ESignatureSendResult`, `ESignatureResult`, `ESignatureDocumentState`, `ESignatureSignerState`; enum `ESignatureDocumentStatus`; `FakeESignatureProvider` com `SucceedsWith(uuid)`, `FailsTransiently(msg)`, `FailsPermanently(msg)`, `StateIs(ESignatureDocumentState)`, `SignedPdfIs(byte[])` e contadores `SendCalls`, `CancelCalls`, `StateCalls`, `DownloadCalls`; `StubHttpMessageHandler.EnqueueResponse(HttpStatusCode, string)` + `Requests`/`RequestBodies`.

- [ ] **Step 1: Interface e resultados**

```csharp
// SiagroB1.Domain/Interfaces/IESignatureProvider.cs
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Interfaces;

/// <summary>
/// Assinatura eletrônica de um documento. Abstrai o provedor (hoje D4Sign) para que a troca não
/// toque nos serviços da minuta, no webhook nem no job de reconciliação.
///
/// NENHUM método lança por falha do provedor — todos devolvem resultado. Quem decide o que fazer
/// é o serviço, olhando <c>Transient</c>: um 4xx de credencial errada não melhora com repetição,
/// um 5xx ou timeout melhora.
/// </summary>
public interface IESignatureProvider
{
    /// <summary>Nome gravado em <c>ContractDraft.Provider</c>. Ex.: "D4Sign".</summary>
    string Name { get; }

    Task<ESignatureSendResult> SendAsync(ESignatureSendRequest request, CancellationToken ct = default);
    Task<ESignatureResult> CancelAsync(string externalDocumentId, CancellationToken ct = default);
    Task<ESignatureDocumentState> GetStateAsync(string externalDocumentId, CancellationToken ct = default);

    /// <summary>PDF assinado, com certificado do provedor. Null quando ainda não há.</summary>
    Task<byte[]?> DownloadSignedAsync(string externalDocumentId, CancellationToken ct = default);
}

/// <param name="Title">Título do documento no cofre do provedor.</param>
/// <param name="FileName">Nome do arquivo enviado, com extensão.</param>
/// <param name="PdfBytes">Conteúdo do PDF renderizado da minuta.</param>
/// <param name="Signers">Signatários na ordem de envio (empresa primeiro, depois parceiro).</param>
/// <param name="WebhookUrl">URL que o provedor chama a cada evento. Vazia ⇒ não registra webhook.</param>
/// <param name="Message">Texto do e-mail de convite.</param>
public record ESignatureSendRequest(
    string Title,
    string FileName,
    byte[] PdfBytes,
    IReadOnlyList<ESignatureSigner> Signers,
    string WebhookUrl,
    string Message);

public record ESignatureSigner(string Name, string Email, SignatoryRole Role, int Order);

/// <param name="Succeeded">Provedor aceitou o documento e o envio.</param>
/// <param name="ExternalDocumentId">Identificador do documento no provedor (uuid do D4Sign).</param>
/// <param name="ErrorMessage">
/// Resumo do erro, truncado em 300 caracteres. NUNCA contém token, chave ou URL com credencial:
/// este texto é gravado em <c>ContractDraft.LastError</c> e exibido na tela.
/// </param>
/// <param name="Transient">Falha possivelmente passageira (timeout, 408, 429, 5xx).</param>
public record ESignatureSendResult(
    bool Succeeded,
    string? ExternalDocumentId,
    string? ErrorMessage,
    bool Transient)
{
    public static ESignatureSendResult Ok(string externalDocumentId) => new(true, externalDocumentId, null, false);
    public static ESignatureSendResult Fail(string message, bool transient) => new(false, null, message, transient);
}

public record ESignatureResult(bool Succeeded, string? ErrorMessage, bool Transient)
{
    public static readonly ESignatureResult Ok = new(true, null, false);
    public static ESignatureResult Fail(string message, bool transient) => new(false, message, transient);
}

/// <summary>Situação do documento no provedor, já traduzida — o serviço não conhece códigos do D4Sign.</summary>
public enum ESignatureDocumentStatus
{
    /// <summary>Não foi possível ler a situação (falha de rede, documento inacessível).</summary>
    Unknown = 0,
    Pending = 1,
    Finished = 2,
    Canceled = 3,
}

/// <param name="Status">Situação do documento.</param>
/// <param name="Signers">Um item por signatário conhecido pelo provedor, correlacionado por e-mail.</param>
/// <param name="ErrorMessage">Preenchido quando <paramref name="Status"/> é <c>Unknown</c>.</param>
public record ESignatureDocumentState(
    ESignatureDocumentStatus Status,
    IReadOnlyList<ESignatureSignerState> Signers,
    string? ErrorMessage = null)
{
    public static ESignatureDocumentState Unknown(string message) =>
        new(ESignatureDocumentStatus.Unknown, [], message);
}

/// <param name="Email">Chave de correlação com <c>CONTRACT_DRAFT_SIGNERS.Email</c>.</param>
/// <param name="Signed">Já assinou.</param>
/// <param name="SignedAt">Quando assinou, se o provedor informar.</param>
/// <param name="Message">Mensagem do provedor sobre este signatário (ex.: falha de e-mail).</param>
public record ESignatureSignerState(string Email, bool Signed, DateTime? SignedAt, string? Message);
```

- [ ] **Step 2: Fila de respostas no StubHttpMessageHandler**

O stub atual roteiriza **uma** resposta; o `SendAsync` do D4Sign faz **quatro** chamadas em sequência. Acrescente a fila **sem quebrar** os construtores existentes (usados pelos testes de WhatsApp):

```csharp
// SiagroB1.Application.Tests/Support/StubHttpMessageHandler.cs
// Acrescentar ao que já existe — não remova os dois construtores nem LastRequest/LastRequestBody.

    private readonly Queue<(HttpStatusCode Status, string Body)> _scripted = new();

    /// <summary>Todas as requisições recebidas, na ordem — para conferir uma sequência de chamadas.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Corpo de cada requisição, no mesmo índice de <see cref="Requests"/>.</summary>
    public List<string?> RequestBodies { get; } = [];

    /// <summary>Construtor da fila: cada chamada consome a próxima resposta enfileirada.</summary>
    public StubHttpMessageHandler()
    {
        _statusCode = HttpStatusCode.OK;
        _responseBody = "{}";
    }

    /// <summary>Enfileira a resposta da próxima chamada. Fila vazia ⇒ cai no status/corpo padrão.</summary>
    public StubHttpMessageHandler EnqueueResponse(HttpStatusCode status, string body = "{}")
    {
        _scripted.Enqueue((status, body));
        return this;
    }
```

E dentro de `SendAsync`, **depois** de `CallCount++` e da captura de `LastRequest`/`LastRequestBody`, acrescente o registro na lista e o consumo da fila:

```csharp
        Requests.Add(request);
        RequestBodies.Add(LastRequestBody);

        if (_throwOnSend is not null) throw _throwOnSend;

        var (status, body) = _scripted.Count > 0 ? _scripted.Dequeue() : (_statusCode, _responseBody);

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
```

> Leia o arquivo antes de editar: o corpo atual de `SendAsync` já lê `request.Content` para preencher `LastRequestBody`. Mantenha essa leitura e substitua **apenas** a construção da resposta.

- [ ] **Step 3: Fake do provedor**

```csharp
// SiagroB1.Application.Tests/Support/FakeESignatureProvider.cs
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Provedor de assinatura roteirizável. Começa devolvendo sucesso com um uuid fixo e documento
/// pendente sem signatários — cada teste ajusta só o que lhe interessa.
/// </summary>
public sealed class FakeESignatureProvider : IESignatureProvider
{
    private ESignatureSendResult _send = ESignatureSendResult.Ok("uuid-fake");
    private ESignatureResult _cancel = ESignatureResult.Ok;
    private ESignatureDocumentState _state = new(ESignatureDocumentStatus.Pending, []);
    private byte[]? _signedPdf = [0x25, 0x50, 0x44, 0x46];

    public string Name => "D4SignFake";

    public ESignatureSendRequest? LastSendRequest { get; private set; }
    public string? LastExternalDocumentId { get; private set; }
    public int SendCalls { get; private set; }
    public int CancelCalls { get; private set; }
    public int StateCalls { get; private set; }
    public int DownloadCalls { get; private set; }

    public FakeESignatureProvider SucceedsWith(string uuid) { _send = ESignatureSendResult.Ok(uuid); return this; }
    public FakeESignatureProvider FailsTransiently(string message = "provedor fora do ar") { _send = ESignatureSendResult.Fail(message, true); return this; }
    public FakeESignatureProvider FailsPermanently(string message = "credencial inválida") { _send = ESignatureSendResult.Fail(message, false); return this; }
    public FakeESignatureProvider CancelFails(string message = "não foi possível cancelar") { _cancel = ESignatureResult.Fail(message, false); return this; }
    public FakeESignatureProvider StateIs(ESignatureDocumentState state) { _state = state; return this; }
    public FakeESignatureProvider SignedPdfIs(byte[]? pdf) { _signedPdf = pdf; return this; }

    public Task<ESignatureSendResult> SendAsync(ESignatureSendRequest request, CancellationToken ct = default)
    {
        SendCalls++;
        LastSendRequest = request;
        return Task.FromResult(_send);
    }

    public Task<ESignatureResult> CancelAsync(string externalDocumentId, CancellationToken ct = default)
    {
        CancelCalls++;
        LastExternalDocumentId = externalDocumentId;
        return Task.FromResult(_cancel);
    }

    public Task<ESignatureDocumentState> GetStateAsync(string externalDocumentId, CancellationToken ct = default)
    {
        StateCalls++;
        LastExternalDocumentId = externalDocumentId;
        return Task.FromResult(_state);
    }

    public Task<byte[]?> DownloadSignedAsync(string externalDocumentId, CancellationToken ct = default)
    {
        DownloadCalls++;
        LastExternalDocumentId = externalDocumentId;
        return Task.FromResult(_signedPdf);
    }
}
```

- [ ] **Step 4: Teste do fake e da fila do stub**

```csharp
// SiagroB1.Application.Tests/ContractDrafts/FakeESignatureProviderTests.cs
using System.Net;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class FakeESignatureProviderTests
{
    [Fact]
    public async Task Scripted_failure_is_returned_not_thrown()
    {
        var provider = new FakeESignatureProvider().FailsTransiently("503");

        var result = await provider.SendAsync(new ESignatureSendRequest("t", "f.pdf", [1], [], "", "m"));

        Assert.False(result.Succeeded);
        Assert.True(result.Transient);
        Assert.Equal("503", result.ErrorMessage);
        Assert.Equal(1, provider.SendCalls);
    }

    [Fact]
    public async Task Stub_handler_serves_one_response_per_call_in_order()
    {
        var handler = new StubHttpMessageHandler()
            .EnqueueResponse(HttpStatusCode.OK, "{\"uuid\":\"a\"}")
            .EnqueueResponse(HttpStatusCode.BadRequest, "{\"message\":\"erro\"}");
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };

        var first = await client.GetAsync("um");
        var second = await client.GetAsync("dois");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains("uuid", await first.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
    }
}
```

- [ ] **Step 5: Build e testes**

Run: `dotnet build SiagroB1.sln`; `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FakeESignatureProviderTests|FullyQualifiedName~WhatsApp"` → PASS (os de WhatsApp provam que a fila não quebrou o stub antigo).

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Domain/Interfaces/IESignatureProvider.cs SiagroB1.Application.Tests/Support SiagroB1.Application.Tests/ContractDrafts/FakeESignatureProviderTests.cs
git commit -m "feat(platform): definir contrato do provedor de assinatura eletronica

Resultados no molde do WhatsApp: falha de provedor nunca vira excecao, vira
Succeeded/ErrorMessage/Transient. O stub HTTP ganhou fila porque o envio ao
D4Sign sao quatro chamadas em sequencia."
```

---

## Task 2: `D4SignProvider`

**Files:**
- Create: `SiagroB1.Infra/ESignature/D4Sign/D4SignProvider.cs`
- Create: `SiagroB1.Infra/ESignature/D4Sign/D4SignContracts.cs` (records de request/response do D4Sign)
- Test: `SiagroB1.Application.Tests/ContractDrafts/D4SignProviderTests.cs`

**Interfaces:**
- Consumes: Task 1.
- Produces: `D4SignProvider(HttpClient http, IConfiguration configuration, ILogger<D4SignProvider> logger) : IESignatureProvider`; constantes `TokenApiKey = "Signature:D4Sign:TokenApi"`, `CryptKeyKey = "Signature:D4Sign:CryptKey"`, `SafeIdKey = "Signature:D4Sign:SafeId"`.

**Credenciais:** no **upload** vão em header (`tokenAPI`, `cryptKey`); nas **demais** chamadas vão na query string (`?tokenAPI=...&cryptKey=...`) — é o comportamento da Tagui e da API do D4Sign.

- [ ] **Step 1: Testes do provider (falham: classe não existe)**

```csharp
// SiagroB1.Application.Tests/ContractDrafts/D4SignProviderTests.cs
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.ESignature.D4Sign;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class D4SignProviderTests
{
    private const string Token = "TOKEN-SECRETO";
    private const string Crypt = "CRYPT-SECRETO";

    private static (D4SignProvider Provider, StubHttpMessageHandler Handler) Build(StubHttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [D4SignProvider.TokenApiKey] = Token,
            [D4SignProvider.CryptKeyKey] = Crypt,
            [D4SignProvider.SafeIdKey] = "COFRE-1",
        }).Build();

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://sandbox.d4sign.com.br/api/v1/") };
        return (new D4SignProvider(client, configuration, NullLogger<D4SignProvider>.Instance), handler);
    }

    private static ESignatureSendRequest Request() => new(
        "Contrato PC-1", "PC-1.pdf", [0x25, 0x50, 0x44, 0x46],
        [new ESignatureSigner("Ana", "ana@x.com", SignatoryRole.SignAsParty, 1),
         new ESignatureSigner("Beto", "beto@x.com", SignatoryRole.SignAsWitness, 2)],
        "https://gw.example.com/hooks/d4sign/SEGREDO", "Assine, por favor");

    [Fact]
    public async Task Send_walks_upload_createlist_webhook_and_sendtosigner_in_order()
    {
        var handler = new StubHttpMessageHandler()
            .EnqueueResponse(HttpStatusCode.OK, "{\"uuid\":\"UUID-1\"}")
            .EnqueueResponse(HttpStatusCode.OK, "{\"message\":\"ok\"}")
            .EnqueueResponse(HttpStatusCode.OK, "{\"message\":\"ok\"}")
            .EnqueueResponse(HttpStatusCode.OK, "{\"message\":\"enviado\"}");
        var (provider, _) = Build(handler);

        var result = await provider.SendAsync(Request());

        Assert.True(result.Succeeded);
        Assert.Equal("UUID-1", result.ExternalDocumentId);
        Assert.Equal(4, handler.Requests.Count);
        Assert.Contains("/documents/COFRE-1/upload", handler.Requests[0].RequestUri!.ToString());
        Assert.Contains("/documents/UUID-1/createlist", handler.Requests[1].RequestUri!.ToString());
        Assert.Contains("/documents/UUID-1/webhooks", handler.Requests[2].RequestUri!.ToString());
        Assert.Contains("/documents/UUID-1/sendtosigner", handler.Requests[3].RequestUri!.ToString());
    }

    [Fact]
    public async Task Upload_carries_credentials_in_headers_and_the_rest_in_the_query()
    {
        var handler = new StubHttpMessageHandler()
            .EnqueueResponse(HttpStatusCode.OK, "{\"uuid\":\"UUID-1\"}")
            .EnqueueResponse(HttpStatusCode.OK).EnqueueResponse(HttpStatusCode.OK).EnqueueResponse(HttpStatusCode.OK);
        var (provider, _) = Build(handler);

        await provider.SendAsync(Request());

        Assert.True(handler.Requests[0].Headers.Contains("tokenAPI"));
        Assert.True(handler.Requests[0].Headers.Contains("cryptKey"));
        Assert.DoesNotContain(Token, handler.Requests[0].RequestUri!.Query);
        Assert.Contains($"tokenAPI={Token}", handler.Requests[1].RequestUri!.Query);
        Assert.Contains($"cryptKey={Crypt}", handler.Requests[1].RequestUri!.Query);
    }

    [Fact]
    public async Task Createlist_maps_the_role_to_the_d4sign_act()
    {
        var handler = new StubHttpMessageHandler()
            .EnqueueResponse(HttpStatusCode.OK, "{\"uuid\":\"UUID-1\"}")
            .EnqueueResponse(HttpStatusCode.OK).EnqueueResponse(HttpStatusCode.OK).EnqueueResponse(HttpStatusCode.OK);
        var (provider, _) = Build(handler);

        await provider.SendAsync(Request());

        var body = handler.RequestBodies[1]!;
        Assert.Contains("\"email\":\"ana@x.com\"", body);
        Assert.Contains("\"act\":\"4\"", body);   // SignAsParty
        Assert.Contains("\"act\":\"5\"", body);   // SignAsWitness
        Assert.Contains("\"embed_methodauth\":\"email\"", body);
    }

    [Fact]
    public async Task A_failure_after_the_upload_cancels_the_orphan_document()
    {
        var handler = new StubHttpMessageHandler()
            .EnqueueResponse(HttpStatusCode.OK, "{\"uuid\":\"UUID-1\"}")
            .EnqueueResponse(HttpStatusCode.BadRequest, "{\"message\":\"signatario invalido\"}")
            .EnqueueResponse(HttpStatusCode.OK, "{\"message\":\"cancelado\"}");
        var (provider, _) = Build(handler);

        var result = await provider.SendAsync(Request());

        Assert.False(result.Succeeded);
        Assert.False(result.Transient);
        Assert.Contains("/documents/UUID-1/cancel", handler.Requests[2].RequestUri!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    public async Task Transient_is_decided_by_the_status_code(HttpStatusCode status, bool transient)
    {
        var handler = new StubHttpMessageHandler().EnqueueResponse(status, "{\"message\":\"x\"}");
        var (provider, _) = Build(handler);

        var result = await provider.SendAsync(Request());

        Assert.False(result.Succeeded);
        Assert.Equal(transient, result.Transient);
    }

    [Fact]
    public async Task Network_failure_is_transient_and_never_leaks_the_secret()
    {
        var handler = new StubHttpMessageHandler(new HttpRequestException($"falha ao chamar https://x?tokenAPI={Token}"));
        var (provider, _) = Build(handler);

        var result = await provider.SendAsync(Request());

        Assert.True(result.Transient);
        Assert.DoesNotContain(Token, result.ErrorMessage);
    }

    [Fact]
    public async Task Provider_error_message_is_truncated_at_300_characters()
    {
        var handler = new StubHttpMessageHandler().EnqueueResponse(HttpStatusCode.BadRequest, new string('x', 900));
        var (provider, _) = Build(handler);

        var result = await provider.SendAsync(Request());

        Assert.NotNull(result.ErrorMessage);
        Assert.True(result.ErrorMessage!.Length <= 300, $"tinha {result.ErrorMessage.Length}");
    }

    [Fact]
    public async Task Get_state_reads_the_document_status_and_the_signer_list()
    {
        var handler = new StubHttpMessageHandler()
            .EnqueueResponse(HttpStatusCode.OK, "[{\"uuidDoc\":\"UUID-1\",\"statusId\":\"4\"}]")
            .EnqueueResponse(HttpStatusCode.OK,
                "{\"list\":[{\"email\":\"ana@x.com\",\"signed\":\"1\",\"signed_date\":\"2026-09-22 10:00:00\"}," +
                "{\"email\":\"beto@x.com\",\"signed\":\"0\",\"signed_date\":null}]}");
        var (provider, _) = Build(handler);

        var state = await provider.GetStateAsync("UUID-1");

        Assert.Equal(ESignatureDocumentStatus.Finished, state.Status);
        Assert.Equal(2, state.Signers.Count);
        Assert.True(state.Signers.Single(s => s.Email == "ana@x.com").Signed);
        Assert.False(state.Signers.Single(s => s.Email == "beto@x.com").Signed);
    }

    [Fact]
    public async Task Get_state_returns_unknown_when_the_provider_fails()
    {
        var handler = new StubHttpMessageHandler().EnqueueResponse(HttpStatusCode.InternalServerError, "boom");
        var (provider, _) = Build(handler);

        var state = await provider.GetStateAsync("UUID-1");

        Assert.Equal(ESignatureDocumentStatus.Unknown, state.Status);
        Assert.Empty(state.Signers);
        Assert.NotNull(state.ErrorMessage);
    }
}
```

- [ ] **Step 2: Contratos de payload do D4Sign**

```csharp
// SiagroB1.Infra/ESignature/D4Sign/D4SignContracts.cs
using System.Text.Json.Serialization;

namespace SiagroB1.Infra.ESignature.D4Sign;

// O D4Sign devolve strings onde caberia número ("statusId":"4", "signed":"1") — por isso tudo é string.

internal record D4SignUploadResponse([property: JsonPropertyName("uuid")] string? Uuid);

internal record D4SignSigner(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("act")] string Act,
    [property: JsonPropertyName("foreign")] string Foreign,
    [property: JsonPropertyName("certificadoicpbr")] string CertificadoIcpBr,
    [property: JsonPropertyName("assinatura_presencial")] string AssinaturaPresencial,
    [property: JsonPropertyName("docauth")] string DocAuth,
    [property: JsonPropertyName("docauthandselfie")] string DocAuthAndSelfie,
    [property: JsonPropertyName("embed_methodauth")] string EmbedMethodAuth,
    [property: JsonPropertyName("upload_allow")] string UploadAllow);

internal record D4SignCreateListRequest([property: JsonPropertyName("signers")] IReadOnlyList<D4SignSigner> Signers);

internal record D4SignWebhookRequest([property: JsonPropertyName("url")] string Url);

internal record D4SignSendToSignerRequest(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("skip_email")] string SkipEmail,
    [property: JsonPropertyName("workflow")] string Workflow,
    [property: JsonPropertyName("tokenAPI")] string TokenApi);

internal record D4SignDocument(
    [property: JsonPropertyName("uuidDoc")] string? UuidDoc,
    [property: JsonPropertyName("statusId")] string? StatusId);

internal record D4SignSignerState(
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("signed")] string? Signed,
    [property: JsonPropertyName("signed_date")] string? SignedDate,
    [property: JsonPropertyName("message")] string? Message);

internal record D4SignListResponse([property: JsonPropertyName("list")] IReadOnlyList<D4SignSignerState>? List);

internal record D4SignDownloadRequest(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("language")] string Language);

internal record D4SignDownloadResponse([property: JsonPropertyName("url")] string? Url);
```

- [ ] **Step 3: O provider**

```csharp
// SiagroB1.Infra/ESignature/D4Sign/D4SignProvider.cs
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Infra.ESignature.D4Sign;

/// <summary>
/// Assinatura eletrônica pelo D4Sign. HttpClient tipado; BaseAddress e credenciais vêm da seção
/// <c>Signature:D4Sign</c>, lidas a cada chamada (sem IOptions, como o resto do solution).
///
/// Credenciais: no upload vão em HEADER; nas demais chamadas vão na QUERY. É assim que a API do
/// D4Sign funciona e é o que a Tagui faz hoje.
///
/// Nenhum método lança por falha do provedor. Mensagens são truncadas em 300 caracteres e a
/// mensagem de HttpRequestException é descartada: ela costuma carregar a URL, e a URL carrega o
/// token.
/// </summary>
public sealed class D4SignProvider(
    HttpClient http,
    IConfiguration configuration,
    ILogger<D4SignProvider> logger) : IESignatureProvider
{
    public const string TokenApiKey = "Signature:D4Sign:TokenApi";
    public const string CryptKeyKey = "Signature:D4Sign:CryptKey";
    public const string SafeIdKey = "Signature:D4Sign:SafeId";

    private const int MaxErrorLength = 300;
    private const string NetworkFailure = "Não foi possível falar com o D4Sign.";

    public string Name => "D4Sign";

    private string Token => configuration[TokenApiKey] ?? "";
    private string Crypt => configuration[CryptKeyKey] ?? "";
    private string SafeId => configuration[SafeIdKey] ?? "";

    /// <summary>Query com as credenciais — usada em tudo, menos no upload.</summary>
    private string Credentials => $"?tokenAPI={Uri.EscapeDataString(Token)}&cryptKey={Uri.EscapeDataString(Crypt)}";

    public async Task<ESignatureSendResult> SendAsync(ESignatureSendRequest request, CancellationToken ct = default)
    {
        string? uuid = null;
        try
        {
            // 1. upload — credenciais em header, multipart
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(request.PdfBytes);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            form.Add(file, "file", request.FileName);

            using var upload = new HttpRequestMessage(HttpMethod.Post, $"documents/{SafeId}/upload") { Content = form };
            upload.Headers.Add("tokenAPI", Token);
            upload.Headers.Add("cryptKey", Crypt);

            var uploadResponse = await http.SendAsync(upload, ct);
            if (!uploadResponse.IsSuccessStatusCode)
                return ESignatureSendResult.Fail(await DescribeAsync(uploadResponse, ct), IsTransient(uploadResponse.StatusCode));

            uuid = (await ReadAsync<D4SignUploadResponse>(uploadResponse, ct))?.Uuid;
            if (string.IsNullOrWhiteSpace(uuid))
                return ESignatureSendResult.Fail("O D4Sign não devolveu o identificador do documento.", false);

            // 2. signatários
            var signers = request.Signers
                .OrderBy(s => s.Order)
                .Select(s => new D4SignSigner(s.Email, ActOf(s.Role), "0", "0", "0", "0", "0", "email", "0"))
                .ToList();

            var createList = await http.PostAsJsonAsync($"documents/{uuid}/createlist{Credentials}",
                new D4SignCreateListRequest(signers), ct);
            if (!createList.IsSuccessStatusCode)
                return await AbortAsync(uuid, createList, ct);

            // 3. webhook — opcional: sem URL pública configurada, só a reconciliação atualiza
            if (!string.IsNullOrWhiteSpace(request.WebhookUrl))
            {
                var webhook = await http.PostAsJsonAsync($"documents/{uuid}/webhooks{Credentials}",
                    new D4SignWebhookRequest(request.WebhookUrl), ct);
                if (!webhook.IsSuccessStatusCode)
                    return await AbortAsync(uuid, webhook, ct);
            }

            // 4. enviar — workflow "0" = sem ordem obrigatória
            var send = await http.PostAsJsonAsync($"documents/{uuid}/sendtosigner{Credentials}",
                new D4SignSendToSignerRequest(request.Message, "0", "0", Token), ct);
            if (!send.IsSuccessStatusCode)
                return await AbortAsync(uuid, send, ct);

            return ESignatureSendResult.Ok(uuid);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Falha de rede ao enviar minuta ao D4Sign. Documento: {Uuid}", uuid ?? "(nenhum)");
            if (uuid is not null) await TryCancelAsync(uuid, ct);
            return ESignatureSendResult.Fail(NetworkFailure, true);
        }
    }

    public async Task<ESignatureResult> CancelAsync(string externalDocumentId, CancellationToken ct = default)
    {
        try
        {
            var response = await http.PostAsync($"documents/{externalDocumentId}/cancel{Credentials}", null, ct);
            return response.IsSuccessStatusCode
                ? ESignatureResult.Ok
                : ESignatureResult.Fail(await DescribeAsync(response, ct), IsTransient(response.StatusCode));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return ESignatureResult.Fail(NetworkFailure, true);
        }
    }

    public async Task<ESignatureDocumentState> GetStateAsync(string externalDocumentId, CancellationToken ct = default)
    {
        try
        {
            var documentResponse = await http.GetAsync($"documents/{externalDocumentId}{Credentials}", ct);
            if (!documentResponse.IsSuccessStatusCode)
                return ESignatureDocumentState.Unknown(await DescribeAsync(documentResponse, ct));

            // O endpoint devolve uma LISTA com um documento.
            var documents = await ReadAsync<List<D4SignDocument>>(documentResponse, ct);
            var status = StatusOf(documents?.FirstOrDefault()?.StatusId);

            var listResponse = await http.GetAsync($"documents/{externalDocumentId}/list{Credentials}", ct);
            if (!listResponse.IsSuccessStatusCode)
                return new ESignatureDocumentState(status, []);

            var list = await ReadAsync<D4SignListResponse>(listResponse, ct);
            var signers = (list?.List ?? [])
                .Where(s => !string.IsNullOrWhiteSpace(s.Email))
                .Select(s => new ESignatureSignerState(s.Email!, s.Signed == "1", ParseDate(s.SignedDate), Truncate(s.Message)))
                .ToList();

            return new ESignatureDocumentState(status, signers);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return ESignatureDocumentState.Unknown(NetworkFailure);
        }
    }

    public async Task<byte[]?> DownloadSignedAsync(string externalDocumentId, CancellationToken ct = default)
    {
        try
        {
            var post = await http.PostAsJsonAsync($"documents/{externalDocumentId}/download{Credentials}",
                new D4SignDownloadRequest("PDF", "pt"), ct);
            if (!post.IsSuccessStatusCode) return null;

            var url = (await ReadAsync<D4SignDownloadResponse>(post, ct))?.Url;
            if (string.IsNullOrWhiteSpace(url)) return null;

            // A URL do download é absoluta e não leva credencial.
            var file = await http.GetAsync(url, ct);
            return file.IsSuccessStatusCode ? await file.Content.ReadAsByteArrayAsync(ct) : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Falha de rede ao baixar PDF assinado do documento {Uuid}", externalDocumentId);
            return null;
        }
    }

    /// <summary>Cancela o documento órfão e devolve a falha do passo que quebrou.</summary>
    private async Task<ESignatureSendResult> AbortAsync(string uuid, HttpResponseMessage failed, CancellationToken ct)
    {
        var message = await DescribeAsync(failed, ct);
        var transient = IsTransient(failed.StatusCode);
        logger.LogWarning("Envio ao D4Sign falhou depois do upload; cancelando documento {Uuid}", uuid);
        await TryCancelAsync(uuid, ct);
        return ESignatureSendResult.Fail(message, transient);
    }

    private async Task TryCancelAsync(string uuid, CancellationToken ct)
    {
        try { await http.PostAsync($"documents/{uuid}/cancel{Credentials}", null, ct); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Documento {Uuid} ficou órfão no cofre do D4Sign", uuid);
        }
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try { return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct); }
        catch (JsonException) { return default; }
    }

    /// <summary>Corpo da resposta de erro, truncado. Nunca inclui a URL — ela carrega o token.</summary>
    private static async Task<string> DescribeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string body;
        try { body = await response.Content.ReadAsStringAsync(ct); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { body = ""; }

        var text = string.IsNullOrWhiteSpace(body) ? "sem detalhe" : body.Trim();
        return Truncate($"D4Sign respondeu {(int)response.StatusCode}: {text}")!;
    }

    private static string? Truncate(string? text) =>
        text is null ? null : text.Length <= MaxErrorLength ? text : text[..MaxErrorLength];

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;

    /// <summary>4 = finalizado, 6 = cancelado; o resto é pendente.</summary>
    private static ESignatureDocumentStatus StatusOf(string? statusId) => statusId switch
    {
        "4" => ESignatureDocumentStatus.Finished,
        "6" => ESignatureDocumentStatus.Canceled,
        null or "" => ESignatureDocumentStatus.Unknown,
        _ => ESignatureDocumentStatus.Pending,
    };

    private static DateTime? ParseDate(string? text) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;

    /// <summary>
    /// O valor do enum já é o código do ato no D4Sign; a conversão é explícita para que uma
    /// renumeração futura do enum não mude, em silêncio, o que o D4Sign recebe.
    /// </summary>
    private static string ActOf(SignatoryRole role) => role switch
    {
        SignatoryRole.Sign => "1",
        SignatoryRole.Approve => "2",
        SignatoryRole.Acknowledge => "3",
        SignatoryRole.SignAsParty => "4",
        SignatoryRole.SignAsWitness => "5",
        SignatoryRole.SignAsIntervening => "6",
        SignatoryRole.AcknowledgeReceipt => "7",
        SignatoryRole.SignAsIssuerEndorserGuarantor => "8",
        SignatoryRole.SignAsIssuerEndorserGuarantorSurety => "9",
        SignatoryRole.SignAsSurety => "10",
        SignatoryRole.SignAsPartyAndSurety => "11",
        SignatoryRole.SignAsJointDebtor => "12",
        SignatoryRole.SignAsPartyAndJointDebtor => "13",
        _ => "1",
    };
}
```

- [ ] **Step 4: Rodar os testes do provider** → PASS (8 métodos, 13 casos — o `[Theory]` de `Transient` vale por 6).

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Infra/ESignature SiagroB1.Application.Tests/ContractDrafts/D4SignProviderTests.cs
git commit -m "feat(platform): implementar provedor de assinatura D4Sign

Upload, lista de signatarios, webhook e envio em quatro chamadas; falha depois
do upload cancela o documento orfao. Credencial em header so no upload, query
nas demais. Erro do provedor truncado em 300 e sem segredo."
```

---

## Task 3: `ContractDraftsSendToSignatureService`

**Files:**
- Create: `SiagroB1.Application/Services/ContractDrafts/ContractDraftsSendToSignatureService.cs`
- Modify: `SiagroB1.Application.Tests/ContractDrafts/ContractDraftsTestContext.cs` (fábricas novas + semente de signatários)
- Test: `SiagroB1.Application.Tests/ContractDrafts/ContractDraftsSendToSignatureServiceTests.cs`

**Interfaces:**
- Consumes: Tasks 1 e 2; Fase 1 (`ContractDraftsLoader`, `ContractDraftsGetPdfService`, `ContractDraftPdfLayout`).
- Produces: `ContractDraftsSendToSignatureService.ExecuteAsync(Guid key, string userName, CancellationToken ct) : Task`.

**Regras (spec, "Ciclo de vida"):** só `Draft`; exige `Signature:Enabled`; signatários ativos da empresa (`BranchCode == contrato.BranchCode || BranchCode == null`) **e** do parceiro (`CardCode`), ambos não vazios, com `BusinessException` dizendo **qual lado** falta; grava snapshot em `CONTRACT_DRAFT_SIGNERS` (empresa primeiro, depois parceiro, por `Order`); chama o provider; sucesso grava `Provider`/`ExternalDocumentId`/`SentAt`/`Status = AwaitingSignature`, move `SignatureStatus` do contrato e registra "Minuta N enviada para assinatura"; falha grava `LastError`, mantém `Draft`, **descarta o snapshot de signers** e lança `BusinessException` com a mensagem.

- [ ] **Step 1: Ampliar o contexto de teste**

Acrescente ao `ContractDraftsTestContext` (não remova nada do que já existe):

```csharp
    public FakeESignatureProvider Signature { get; } = new();

    /// <summary>Configuração do serviço. Enabled=true por padrão; o teste de "desligado" sobrescreve.</summary>
    public IConfiguration Configuration { get; set; } = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Signature:Enabled"] = "true",
            ["Signature:D4Sign:WebhookBaseUrl"] = "https://gw.example.com",
            ["Signature:D4Sign:WebhookSecret"] = "SEGREDO",
        }).Build();

    public PurchaseContractsSetSignatureStatusService PurchaseSignatureStatus() => new(Db.Context);
    public SalesContractsSetSignatureStatusService SalesSignatureStatus() => new(Db.Context);

    public ContractDraftsSendToSignatureService SendToSignature() => new(
        Db.Context, Loader(), GetPdf(), Signature, Configuration,
        PurchaseLog(), SalesLog(), PurchaseSignatureStatus(), SalesSignatureStatus(),
        NullLogger<ContractDraftsSendToSignatureService>.Instance);

    /// <summary>Um signatário de cada lado, o mínimo que o envio exige.</summary>
    public async Task SeedSignatoriesAsync(string cardCode = "F0001", string branchCode = "01")
    {
        Db.Context.CompanySignatories.Add(new CompanySignatory
        {
            Name = "Diretor Tagui", TaxId = "11122233344", Email = "diretor@tagui.com",
            Role = SignatoryRole.SignAsParty, Order = 1, BranchCode = branchCode, Active = true,
        });
        Db.Context.BusinessPartnerSignatories.Add(new BusinessPartnerSignatory
        {
            CardCode = cardCode, Name = "Produtor", TaxId = "55566677788", Email = "produtor@x.com",
            Role = SignatoryRole.SignAsParty, Order = 1, Active = true,
        });
        await Db.Context.SaveChangesAsync();
    }
```

> Confirme os nomes reais das propriedades de `CompanySignatory`/`BusinessPartnerSignatory` em `SiagroB1.Domain/Entities/` antes de colar — a Task 1 da Fase 1 os criou e este trecho assume `Name`, `TaxId`, `Email`, `Role`, `Order`, `Active`, `BranchCode`, `CardCode`. Se a propriedade de filial não existir em `CompanySignatory`, remova o filtro por filial do serviço e ajuste este seed.
> Usings a acrescentar no arquivo: `Microsoft.Extensions.Configuration`, `SiagroB1.Domain.Interfaces`.

- [ ] **Step 2: Testes do envio (falham: serviço não existe)**

```csharp
// SiagroB1.Application.Tests/ContractDrafts/ContractDraftsSendToSignatureServiceTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class ContractDraftsSendToSignatureServiceTests
{
    private readonly ContractDraftsTestContext _ctx = new();

    private async Task<ContractDraft> DraftAsync()
    {
        var template = await _ctx.SeedTemplateAsync();
        var contract = await _ctx.SeedPurchaseAsync();
        return await _ctx.Create().ExecuteAsync(ContractDraftContractType.Purchase, contract.Key, template.Key,
            ContractDraftType.Contract, "m", "t", default);
    }

    [Fact]
    public async Task Sends_stores_the_snapshot_and_moves_the_contract_signature_status()
    {
        var draft = await DraftAsync();
        await _ctx.SeedSignatoriesAsync();
        _ctx.Signature.SucceedsWith("UUID-9");

        await _ctx.SendToSignature().ExecuteAsync(draft.Key, "tester", default);

        var saved = await _ctx.Db.Context.ContractDrafts.Include(d => d.Signers).SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.AwaitingSignature, saved.Status);
        Assert.Equal("UUID-9", saved.ExternalDocumentId);
        Assert.Equal("D4SignFake", saved.Provider);
        Assert.NotNull(saved.SentAt);
        Assert.Null(saved.LastError);
        Assert.Equal(2, saved.Signers.Count);
        Assert.Equal(SignerSide.Company, saved.Signers.OrderBy(s => s.Side).First().Side);
        Assert.All(saved.Signers, s => Assert.Equal(SignerStatus.Pending, s.Status));

        var contract = await _ctx.Db.Context.PurchaseContracts.SingleAsync(c => c.Key == saved.PurchaseContractKey);
        Assert.Equal(SignatureStatus.AwaitingSignature, contract.SignatureStatus);

        var log = await _ctx.Db.Context.PurchaseContractsChangeLogs
            .Where(l => l.Field == ContractChangeLogFields.Draft).ToListAsync();
        Assert.Contains(log, l => l.NewValue == "Minuta 1 enviada para assinatura");
    }

    [Fact]
    public async Task The_pdf_and_the_webhook_url_reach_the_provider()
    {
        var draft = await DraftAsync();
        await _ctx.SeedSignatoriesAsync();

        await _ctx.SendToSignature().ExecuteAsync(draft.Key, "tester", default);

        var request = _ctx.Signature.LastSendRequest!;
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(request.PdfBytes));
        Assert.Equal("PC-000123-minuta-1.pdf", request.FileName);
        Assert.Equal("https://gw.example.com/hooks/d4sign/SEGREDO", request.WebhookUrl);
        Assert.Equal(2, request.Signers.Count);
    }

    [Fact]
    public async Task Provider_failure_keeps_the_draft_records_the_error_and_discards_the_signers()
    {
        var draft = await DraftAsync();
        await _ctx.SeedSignatoriesAsync();
        _ctx.Signature.FailsTransiently("D4Sign respondeu 503");

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _ctx.SendToSignature().ExecuteAsync(draft.Key, "tester", default));

        Assert.Contains("503", ex.Message);
        var saved = await _ctx.Db.Context.ContractDrafts.Include(d => d.Signers).SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.Draft, saved.Status);
        Assert.Contains("503", saved.LastError);
        Assert.Empty(saved.Signers);
        Assert.Empty(_ctx.Db.Context.ContractDraftSigners);
    }

    [Fact]
    public async Task Refuses_to_send_twice()
    {
        var draft = await DraftAsync();
        await _ctx.SeedSignatoriesAsync();
        await _ctx.SendToSignature().ExecuteAsync(draft.Key, "tester", default);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _ctx.SendToSignature().ExecuteAsync(draft.Key, "tester", default));

        Assert.Equal("Minuta já enviada para assinatura não pode ser alterada.", ex.Message);
        Assert.Equal(1, _ctx.Signature.SendCalls);
    }

    [Fact]
    public async Task Refuses_without_a_company_signatory()
    {
        var draft = await DraftAsync();
        _ctx.Db.Context.BusinessPartnerSignatories.Add(new BusinessPartnerSignatory
        {
            CardCode = "F0001", Name = "Produtor", TaxId = "55566677788", Email = "produtor@x.com",
            Role = SignatoryRole.SignAsParty, Order = 1, Active = true,
        });
        await _ctx.Db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _ctx.SendToSignature().ExecuteAsync(draft.Key, "tester", default));

        Assert.Equal("Nenhum signatário ativo da empresa para esta filial.", ex.Message);
        Assert.Equal(0, _ctx.Signature.SendCalls);
    }

    [Fact]
    public async Task Refuses_without_a_partner_signatory()
    {
        var draft = await DraftAsync();
        _ctx.Db.Context.CompanySignatories.Add(new CompanySignatory
        {
            Name = "Diretor", TaxId = "11122233344", Email = "diretor@tagui.com",
            Role = SignatoryRole.SignAsParty, Order = 1, BranchCode = "01", Active = true,
        });
        await _ctx.Db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _ctx.SendToSignature().ExecuteAsync(draft.Key, "tester", default));

        Assert.Equal("Nenhum signatário ativo cadastrado para o parceiro.", ex.Message);
    }

    [Fact]
    public async Task Refuses_when_signature_is_disabled_in_this_environment()
    {
        var draft = await DraftAsync();
        await _ctx.SeedSignatoriesAsync();
        _ctx.Configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Signature:Enabled"] = "false" }).Build();

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _ctx.SendToSignature().ExecuteAsync(draft.Key, "tester", default));

        Assert.Equal("Assinatura eletrônica não está habilitada neste ambiente.", ex.Message);
        Assert.Equal(0, _ctx.Signature.SendCalls);
    }
}
```

- [ ] **Step 3: O serviço**

```csharp
// SiagroB1.Application/Services/ContractDrafts/ContractDraftsSendToSignatureService.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>
/// Envia a minuta para assinatura. Síncrono de propósito: o usuário aperta o botão e espera a
/// resposta do provedor, como na Tagui.
///
/// A chamada HTTP acontece ANTES do SaveChanges e o resultado decide o que se grava — não existe
/// estado intermediário "enviando" que um processo morto deixaria para trás.
/// </summary>
public class ContractDraftsSendToSignatureService(
    AppDbContext context,
    ContractDraftsLoader loader,
    ContractDraftsGetPdfService pdfService,
    IESignatureProvider provider,
    IConfiguration configuration,
    PurchaseContractsChangeLogService purchaseLog,
    SalesContractsChangeLogService salesLog,
    PurchaseContractsSetSignatureStatusService purchaseSignatureStatus,
    SalesContractsSetSignatureStatusService salesSignatureStatus,
    ILogger<ContractDraftsSendToSignatureService> logger)
{
    public const string DisabledMessage = "Assinatura eletrônica não está habilitada neste ambiente.";
    public const string NoCompanySignatoryMessage = "Nenhum signatário ativo da empresa para esta filial.";
    public const string NoPartnerSignatoryMessage = "Nenhum signatário ativo cadastrado para o parceiro.";

    public async Task ExecuteAsync(Guid key, string userName, CancellationToken ct = default)
    {
        if (!configuration.GetValue("Signature:Enabled", false))
            throw new BusinessException(DisabledMessage);

        var draft = await loader.RequireDraftAsync(key, ct);
        ContractDraftsLoader.RequireEditable(draft);

        var cardCode = await ResolveCardCodeAsync(draft, ct);

        var company = await context.CompanySignatories.AsNoTracking()
            .Where(s => s.Active && (s.BranchCode == draft.BranchCode || s.BranchCode == null))
            .OrderBy(s => s.Order).ToListAsync(ct);
        if (company.Count == 0) throw new BusinessException(NoCompanySignatoryMessage);

        var partner = await context.BusinessPartnerSignatories.AsNoTracking()
            .Where(s => s.Active && s.CardCode == cardCode)
            .OrderBy(s => s.Order).ToListAsync(ct);
        if (partner.Count == 0) throw new BusinessException(NoPartnerSignatoryMessage);

        var (pdf, fileName) = await pdfService.ExecuteAsync(draft.Key, ct);

        // Empresa primeiro, parceiro depois; Order contínuo, que é o que vai ao provedor.
        var order = 0;
        var signers = new List<ContractDraftSigner>();
        foreach (var s in company)
            signers.Add(new ContractDraftSigner { DraftKey = draft.Key, Side = SignerSide.Company, Name = s.Name, TaxId = s.TaxId, Email = s.Email, Role = s.Role, Order = ++order });
        foreach (var s in partner)
            signers.Add(new ContractDraftSigner { DraftKey = draft.Key, Side = SignerSide.Partner, Name = s.Name, TaxId = s.TaxId, Email = s.Email, Role = s.Role, Order = ++order });

        var request = new ESignatureSendRequest(
            Title: draft.Description,
            FileName: fileName,
            PdfBytes: pdf,
            Signers: signers.Select(s => new ESignatureSigner(s.Name, s.Email, s.Role, s.Order)).ToList(),
            WebhookUrl: WebhookUrl(),
            Message: $"Contrato {draft.ContractCode} — minuta {draft.Sequence}. Por favor, assine.");

        var result = await provider.SendAsync(request, ct);

        if (!result.Succeeded)
        {
            // Só o erro é gravado: o snapshot de signers nunca chega ao contexto.
            draft.LastError = result.ErrorMessage;
            draft.UpdatedAt = DateTime.Now;
            draft.UpdatedBy = userName;
            await context.SaveChangesAsync(ct);

            logger.LogWarning("Minuta {Key} não foi enviada ao provedor. Transitório: {Transient}", key, result.Transient);
            throw new BusinessException(result.ErrorMessage ?? "Não foi possível enviar a minuta para assinatura.");
        }

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        try
        {
            context.ContractDraftSigners.AddRange(signers);

            draft.Provider = provider.Name;
            draft.ExternalDocumentId = result.ExternalDocumentId;
            draft.SentAt = DateTime.Now;
            draft.Status = ContractDraftStatus.AwaitingSignature;
            draft.LastError = null;
            draft.UpdatedAt = DateTime.Now;
            draft.UpdatedBy = userName;

            var what = ContractChangeLogFields.DescribeDraft(draft.Sequence, "enviada para assinatura");
            if (draft.PurchaseContractKey is { } pk)
            {
                purchaseLog.Register(pk, ContractChangeLogFields.Draft, null, what, userName);
                await purchaseSignatureStatus.ExecuteAsync(pk, SignatureStatus.AwaitingSignature, userName);
            }
            if (draft.SalesContractKey is { } sk)
            {
                salesLog.Register(sk, ContractChangeLogFields.Draft, null, what, userName);
                await salesSignatureStatus.ExecuteAsync(sk, SignatureStatus.AwaitingSignature, userName);
            }

            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync(ct);
            logger.LogError(e, "Minuta {Key} foi aceita pelo provedor mas não pôde ser gravada. Documento: {Uuid}",
                key, result.ExternalDocumentId);
            throw;
        }
    }

    private async Task<string?> ResolveCardCodeAsync(ContractDraft draft, CancellationToken ct) =>
        draft.PurchaseContractKey is { } pk
            ? await context.PurchaseContracts.Where(c => c.Key == pk).Select(c => c.CardCode).FirstOrDefaultAsync(ct)
            : await context.SalesContracts.Where(c => c.Key == draft.SalesContractKey).Select(c => c.CardCode).FirstOrDefaultAsync(ct);

    /// <summary>Vazia quando não há URL pública configurada: aí só a reconciliação atualiza o estado.</summary>
    private string WebhookUrl()
    {
        var baseUrl = configuration["Signature:D4Sign:WebhookBaseUrl"];
        var secret = configuration["Signature:D4Sign:WebhookSecret"];
        return string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(secret)
            ? ""
            : $"{baseUrl.TrimEnd('/')}/hooks/d4sign/{secret}";
    }
}
```

- [ ] **Step 4: Rodar os testes** → PASS (7). Se `SignatureStatus` do contrato não mudar, confirme que `*SetSignatureStatusService` é enqueue-only e que o `SaveChangesAsync` do serviço é o que persiste.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application/Services/ContractDrafts/ContractDraftsSendToSignatureService.cs SiagroB1.Application.Tests/ContractDrafts
git commit -m "feat(platform): enviar minuta para assinatura eletronica

HTTP antes do SaveChanges: sucesso grava snapshot de signatarios, uuid e move o
contrato para AwaitingSignature; falha deixa a minuta em rascunho com LastError
e sem signatarios. Sem estado intermediario 'enviando'."
```

---

## Task 4: `ContractDraftsApplyProviderStateService`

**Files:**
- Create: `SiagroB1.Application/Services/ContractDrafts/ContractDraftsApplyProviderStateService.cs`
- Modify: `SiagroB1.Application.Tests/ContractDrafts/ContractDraftsTestContext.cs` (fábrica nova)
- Test: `SiagroB1.Application.Tests/ContractDrafts/ContractDraftsApplyProviderStateServiceTests.cs`

**Interfaces:**
- Consumes: Tasks 1 e 3.
- Produces: `ContractDraftsApplyProviderStateService.ExecuteAsync(Guid draftKey, ESignatureDocumentState state, string userName, CancellationToken ct) : Task<bool>` — `true` quando gravou algo.

**Porta única de escrita a partir do provedor.** Webhook e job de reconciliação usam exatamente este serviço. **Idempotente:** estado igual não grava nada, não duplica anexo nem log.

- [ ] **Step 1: Fábrica no contexto de teste**

```csharp
    public ContractDraftsApplyProviderStateService ApplyState() => new(
        Db.Context, Loader(), Signature,
        new PurchaseContractsAttachmentsCreateService(Db, NullLogger<PurchaseContractsAttachmentsCreateService>.Instance),
        new SalesContractsAttachmentsCreateService(Db, NullLogger<SalesContractsAttachmentsCreateService>.Instance),
        PurchaseLog(), SalesLog(), PurchaseSignatureStatus(), SalesSignatureStatus(),
        NullLogger<ContractDraftsApplyProviderStateService>.Instance);
```

- [ ] **Step 2: Testes (falham: serviço não existe)**

```csharp
// SiagroB1.Application.Tests/ContractDrafts/ContractDraftsApplyProviderStateServiceTests.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class ContractDraftsApplyProviderStateServiceTests
{
    private readonly ContractDraftsTestContext _ctx = new();

    /// <summary>Minuta já enviada, com os dois signatários no snapshot.</summary>
    private async Task<ContractDraft> SentDraftAsync()
    {
        var template = await _ctx.SeedTemplateAsync();
        var contract = await _ctx.SeedPurchaseAsync();
        var draft = await _ctx.Create().ExecuteAsync(ContractDraftContractType.Purchase, contract.Key, template.Key,
            ContractDraftType.Contract, "m", "t", default);
        await _ctx.SeedSignatoriesAsync();
        await _ctx.SendToSignature().ExecuteAsync(draft.Key, "tester", default);
        return draft;
    }

    private static ESignatureDocumentState State(ESignatureDocumentStatus status, params (string Email, bool Signed)[] signers) =>
        new(status, signers.Select(s => new ESignatureSignerState(s.Email, s.Signed, s.Signed ? DateTime.Now : null, null)).ToList());

    [Fact]
    public async Task One_signature_moves_the_draft_to_partially_signed()
    {
        var draft = await SentDraftAsync();

        var changed = await _ctx.ApplyState().ExecuteAsync(draft.Key,
            State(ESignatureDocumentStatus.Pending, ("diretor@tagui.com", true), ("produtor@x.com", false)),
            "webhook", default);

        Assert.True(changed);
        var saved = await _ctx.Db.Context.ContractDrafts.Include(d => d.Signers).SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.PartiallySigned, saved.Status);
        Assert.Equal(SignerStatus.Signed, saved.Signers.Single(s => s.Email == "diretor@tagui.com").Status);
        Assert.Equal(SignerStatus.Pending, saved.Signers.Single(s => s.Email == "produtor@x.com").Status);
        Assert.Equal(0, _ctx.Signature.DownloadCalls);
    }

    [Fact]
    public async Task Finished_attaches_the_signed_pdf_and_marks_the_contract_signed()
    {
        var draft = await SentDraftAsync();
        _ctx.Signature.SignedPdfIs([9, 9, 9]);

        await _ctx.ApplyState().ExecuteAsync(draft.Key,
            State(ESignatureDocumentStatus.Finished, ("diretor@tagui.com", true), ("produtor@x.com", true)),
            "webhook", default);

        var saved = await _ctx.Db.Context.ContractDrafts.SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.Signed, saved.Status);
        Assert.NotNull(saved.SignedAt);
        Assert.NotNull(saved.SignedAttachmentKey);

        var attachment = await _ctx.Db.Context.PurchaseContractAttachments.SingleAsync();
        Assert.Equal(saved.SignedAttachmentKey, attachment.Key);
        Assert.Equal([9, 9, 9], attachment.FileData);
        Assert.Equal("Minuta 1 assinada", attachment.Description);
        Assert.Equal("application/pdf", attachment.ContentType);

        var contract = await _ctx.Db.Context.PurchaseContracts.SingleAsync(c => c.Key == saved.PurchaseContractKey);
        Assert.Equal(SignatureStatus.Signed, contract.SignatureStatus);
    }

    [Fact]
    public async Task Applying_the_same_state_twice_changes_nothing()
    {
        var draft = await SentDraftAsync();
        var state = State(ESignatureDocumentStatus.Finished, ("diretor@tagui.com", true), ("produtor@x.com", true));

        await _ctx.ApplyState().ExecuteAsync(draft.Key, state, "webhook", default);
        var changedAgain = await _ctx.ApplyState().ExecuteAsync(draft.Key, state, "webhook", default);

        Assert.False(changedAgain);
        Assert.Equal(1, await _ctx.Db.Context.PurchaseContractAttachments.CountAsync());
        Assert.Equal(1, _ctx.Signature.DownloadCalls);
        var logs = await _ctx.Db.Context.PurchaseContractsChangeLogs
            .Where(l => l.NewValue == "Minuta 1 assinada").CountAsync();
        Assert.Equal(1, logs);
    }

    [Fact]
    public async Task Canceled_at_the_provider_cancels_the_draft_without_touching_the_contract()
    {
        var draft = await SentDraftAsync();

        await _ctx.ApplyState().ExecuteAsync(draft.Key, State(ESignatureDocumentStatus.Canceled), "webhook", default);

        var saved = await _ctx.Db.Context.ContractDrafts.SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.Canceled, saved.Status);

        // O usuário pode ter assinado em papel: o contrato continua como estava.
        var contract = await _ctx.Db.Context.PurchaseContracts.SingleAsync(c => c.Key == saved.PurchaseContractKey);
        Assert.Equal(SignatureStatus.AwaitingSignature, contract.SignatureStatus);
    }

    [Fact]
    public async Task Unknown_state_is_ignored()
    {
        var draft = await SentDraftAsync();

        var changed = await _ctx.ApplyState().ExecuteAsync(draft.Key,
            ESignatureDocumentState.Unknown("provedor fora do ar"), "job", default);

        Assert.False(changed);
        var saved = await _ctx.Db.Context.ContractDrafts.SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.AwaitingSignature, saved.Status);
    }

    [Fact]
    public async Task Finished_without_a_signed_pdf_still_marks_signed_and_records_the_reason()
    {
        var draft = await SentDraftAsync();
        _ctx.Signature.SignedPdfIs(null);

        await _ctx.ApplyState().ExecuteAsync(draft.Key,
            State(ESignatureDocumentStatus.Finished, ("diretor@tagui.com", true), ("produtor@x.com", true)),
            "webhook", default);

        var saved = await _ctx.Db.Context.ContractDrafts.SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.Signed, saved.Status);
        Assert.Null(saved.SignedAttachmentKey);
        Assert.Contains("PDF assinado", saved.LastError);
        Assert.Empty(_ctx.Db.Context.PurchaseContractAttachments);
    }
}
```

- [ ] **Step 3: O serviço**

```csharp
// SiagroB1.Application/Services/ContractDrafts/ContractDraftsApplyProviderStateService.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>
/// Porta ÚNICA de escrita a partir do provedor: o webhook e o job de reconciliação chamam este
/// mesmo serviço. Idempotente — aplicar o mesmo estado duas vezes não duplica anexo nem log,
/// porque o webhook do D4Sign reenvia (imediato, 3× em 1 h, 2× em 6 h, 1× em 12 h).
/// </summary>
public class ContractDraftsApplyProviderStateService(
    AppDbContext context,
    ContractDraftsLoader loader,
    IESignatureProvider provider,
    PurchaseContractsAttachmentsCreateService purchaseAttachments,
    SalesContractsAttachmentsCreateService salesAttachments,
    PurchaseContractsChangeLogService purchaseLog,
    SalesContractsChangeLogService salesLog,
    PurchaseContractsSetSignatureStatusService purchaseSignatureStatus,
    SalesContractsSetSignatureStatusService salesSignatureStatus,
    ILogger<ContractDraftsApplyProviderStateService> logger)
{
    /// <returns><c>true</c> quando algo mudou; <c>false</c> quando o estado já era o mesmo.</returns>
    public async Task<bool> ExecuteAsync(Guid draftKey, ESignatureDocumentState state, string userName, CancellationToken ct = default)
    {
        if (state.Status == ESignatureDocumentStatus.Unknown)
        {
            logger.LogWarning("Estado desconhecido do provedor para a minuta {Key}: {Message}", draftKey, state.ErrorMessage);
            return false;
        }

        var draft = await loader.RequireDraftAsync(draftKey, ct);

        // Terminal: nada do provedor reabre uma minuta.
        if (draft.Status is ContractDraftStatus.Signed or ContractDraftStatus.Canceled)
            return false;

        var changed = ApplySigners(draft, state);
        var target = NextStatus(draft, state);

        if (target == draft.Status && !changed)
            return false;

        byte[]? signedPdf = null;
        if (target == ContractDraftStatus.Signed)
        {
            signedPdf = await provider.DownloadSignedAsync(draft.ExternalDocumentId ?? "", ct);
            if (signedPdf is null || signedPdf.Length == 0)
                logger.LogWarning("Minuta {Key} finalizada sem PDF assinado disponível", draftKey);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        try
        {
            if (target != draft.Status)
            {
                draft.Status = target;

                if (target == ContractDraftStatus.Signed)
                {
                    draft.SignedAt = DateTime.Now;

                    if (signedPdf is { Length: > 0 })
                    {
                        var description = ContractChangeLogFields.DescribeDraft(draft.Sequence, "assinada");
                        draft.SignedAttachmentKey = await AttachAsync(draft, signedPdf, description, userName);
                        draft.LastError = null;
                    }
                    else
                    {
                        draft.LastError = "Documento finalizado, mas o PDF assinado não pôde ser baixado do provedor.";
                    }

                    var what = ContractChangeLogFields.DescribeDraft(draft.Sequence, "assinada");
                    if (draft.PurchaseContractKey is { } pk)
                    {
                        purchaseLog.Register(pk, ContractChangeLogFields.Draft, null, what, userName);
                        await purchaseSignatureStatus.ExecuteAsync(pk, SignatureStatus.Signed, userName);
                    }
                    if (draft.SalesContractKey is { } sk)
                    {
                        salesLog.Register(sk, ContractChangeLogFields.Draft, null, what, userName);
                        await salesSignatureStatus.ExecuteAsync(sk, SignatureStatus.Signed, userName);
                    }
                }
                else if (target == ContractDraftStatus.Canceled)
                {
                    draft.CanceledAt = DateTime.Now;
                    draft.CanceledBy = userName;

                    // NÃO toca o SignatureStatus do contrato: pode ter sido assinado em papel.
                    var what = ContractChangeLogFields.DescribeDraft(draft.Sequence, "cancelada no provedor");
                    if (draft.PurchaseContractKey is { } pk) purchaseLog.Register(pk, ContractChangeLogFields.Draft, null, what, userName);
                    if (draft.SalesContractKey is { } sk) salesLog.Register(sk, ContractChangeLogFields.Draft, null, what, userName);
                }
            }

            draft.UpdatedAt = DateTime.Now;
            draft.UpdatedBy = userName;

            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync(ct);
            logger.LogError(e, "Falha ao aplicar estado do provedor na minuta {Key}", draftKey);
            throw;
        }
    }

    /// <summary>Correlaciona por e-mail. Devolve true se algum signatário mudou.</summary>
    private static bool ApplySigners(ContractDraft draft, ESignatureDocumentState state)
    {
        var changed = false;

        foreach (var incoming in state.Signers)
        {
            var signer = draft.Signers.FirstOrDefault(s =>
                string.Equals(s.Email, incoming.Email, StringComparison.OrdinalIgnoreCase));
            if (signer is null) continue;

            var status = incoming.Signed ? SignerStatus.Signed : signer.Status;
            if (signer.Status != status)
            {
                signer.Status = status;
                signer.SignedAt = incoming.SignedAt ?? (incoming.Signed ? DateTime.Now : null);
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(incoming.Message) && signer.LastMessage != incoming.Message)
            {
                signer.LastMessage = incoming.Message;
                changed = true;
            }
        }

        return changed;
    }

    private static ContractDraftStatus NextStatus(ContractDraft draft, ESignatureDocumentState state)
    {
        if (state.Status == ESignatureDocumentStatus.Canceled) return ContractDraftStatus.Canceled;
        if (state.Status == ESignatureDocumentStatus.Finished) return ContractDraftStatus.Signed;

        var any = draft.Signers.Any(s => s.Status == SignerStatus.Signed);
        var all = draft.Signers.Count > 0 && draft.Signers.All(s => s.Status == SignerStatus.Signed);

        if (all) return ContractDraftStatus.Signed;
        return any ? ContractDraftStatus.PartiallySigned : draft.Status;
    }

    /// <summary>
    /// Anexa ao contrato. Atenção à assimetria real dos dois serviços: o de compra NÃO recebe
    /// userName (preenche-se CreatedBy no objeto), o de venda recebe.
    /// </summary>
    private async Task<Guid> AttachAsync(ContractDraft draft, byte[] pdf, string description, string userName)
    {
        var fileName = $"{draft.ContractCode}-minuta-{draft.Sequence}-assinada.pdf";

        if (draft.PurchaseContractKey is { } pk)
        {
            var attachment = new PurchaseContractAttachment
            {
                Key = Guid.NewGuid(), PurchaseContractKey = pk, Description = description, FileName = fileName,
                ContentType = "application/pdf", FileData = pdf, CreatedAt = DateTime.Now, CreatedBy = userName,
            };
            await purchaseAttachments.SaveAsync(pk, attachment);
            return attachment.Key;
        }

        var sk = draft.SalesContractKey!.Value;
        var salesAttachment = new SalesContractAttachment
        {
            Key = Guid.NewGuid(), SalesContractKey = sk, Description = description, FileName = fileName,
            ContentType = "application/pdf", FileData = pdf, CreatedAt = DateTime.Now, CreatedBy = userName,
        };
        await salesAttachments.SaveAsync(sk, salesAttachment, userName);
        return salesAttachment.Key;
    }
}
```

> Se `*AttachmentsCreateService.SaveAsync` chamar `SaveChangesAsync` por dentro, o anexo é gravado fora da transação do serviço — confirme lendo os dois arquivos. Se chamar, mantenha assim (é o padrão da casa) e garanta que a idempotência do Step 2 continua verde; se não chamar, nada muda.

- [ ] **Step 4: Rodar os testes** → PASS (6).

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application/Services/ContractDrafts/ContractDraftsApplyProviderStateService.cs SiagroB1.Application.Tests/ContractDrafts
git commit -m "feat(platform): aplicar estado do provedor na minuta

Porta unica de escrita: webhook e reconciliacao chamam o mesmo servico.
Idempotente porque o D4Sign reenvia o webhook sete vezes. Assinada vira anexo
do contrato; cancelada no provedor nao mexe no contrato."
```

---

## Task 5: Cancelar e atualizar situação sob demanda

**Files:**
- Create: `SiagroB1.Application/Services/ContractDrafts/ContractDraftsCancelService.cs`
- Create: `SiagroB1.Application/Services/ContractDrafts/ContractDraftsRefreshStateService.cs`
- Test: `SiagroB1.Application.Tests/ContractDrafts/ContractDraftsCancelAndRefreshServiceTests.cs`

**Interfaces:**
- Consumes: Tasks 1, 3 e 4.
- Produces: `ContractDraftsCancelService.ExecuteAsync(Guid key, string userName, CancellationToken ct)`; `ContractDraftsRefreshStateService.ExecuteAsync(Guid key, string userName, CancellationToken ct) : Task<bool>`.

- [ ] **Step 1: Testes**

```csharp
// SiagroB1.Application.Tests/ContractDrafts/ContractDraftsCancelAndRefreshServiceTests.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class ContractDraftsCancelAndRefreshServiceTests
{
    private readonly ContractDraftsTestContext _ctx = new();

    private async Task<ContractDraft> SentDraftAsync()
    {
        var template = await _ctx.SeedTemplateAsync();
        var contract = await _ctx.SeedPurchaseAsync();
        var draft = await _ctx.Create().ExecuteAsync(ContractDraftContractType.Purchase, contract.Key, template.Key,
            ContractDraftType.Contract, "m", "t", default);
        await _ctx.SeedSignatoriesAsync();
        await _ctx.SendToSignature().ExecuteAsync(draft.Key, "tester", default);
        return draft;
    }

    [Fact]
    public async Task Cancel_cancels_at_the_provider_and_leaves_the_contract_alone()
    {
        var draft = await SentDraftAsync();

        await _ctx.Cancel().ExecuteAsync(draft.Key, "tester", default);

        Assert.Equal(1, _ctx.Signature.CancelCalls);
        var saved = await _ctx.Db.Context.ContractDrafts.SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.Canceled, saved.Status);
        Assert.Equal("tester", saved.CanceledBy);

        var contract = await _ctx.Db.Context.PurchaseContracts.SingleAsync(c => c.Key == saved.PurchaseContractKey);
        Assert.Equal(SignatureStatus.AwaitingSignature, contract.SignatureStatus);

        var log = await _ctx.Db.Context.PurchaseContractsChangeLogs.ToListAsync();
        Assert.Contains(log, l => l.NewValue == "Minuta 1 cancelada");
    }

    [Fact]
    public async Task Cancel_is_refused_on_a_draft_that_was_never_sent()
    {
        var template = await _ctx.SeedTemplateAsync();
        var contract = await _ctx.SeedPurchaseAsync();
        var draft = await _ctx.Create().ExecuteAsync(ContractDraftContractType.Purchase, contract.Key, template.Key,
            ContractDraftType.Contract, "m", "t", default);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _ctx.Cancel().ExecuteAsync(draft.Key, "t", default));

        Assert.Equal("Só é possível cancelar minuta enviada para assinatura.", ex.Message);
        Assert.Equal(0, _ctx.Signature.CancelCalls);
    }

    [Fact]
    public async Task Cancel_fails_loudly_when_the_provider_refuses()
    {
        var draft = await SentDraftAsync();
        _ctx.Signature.CancelFails("documento já finalizado");

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _ctx.Cancel().ExecuteAsync(draft.Key, "t", default));

        Assert.Contains("já finalizado", ex.Message);
        var saved = await _ctx.Db.Context.ContractDrafts.SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.AwaitingSignature, saved.Status);
    }

    [Fact]
    public async Task Refresh_pulls_the_state_from_the_provider_and_applies_it()
    {
        var draft = await SentDraftAsync();
        _ctx.Signature.StateIs(new ESignatureDocumentState(ESignatureDocumentStatus.Pending,
            [new ESignatureSignerState("diretor@tagui.com", true, DateTime.Now, null)]));

        var changed = await _ctx.RefreshState().ExecuteAsync(draft.Key, "tester", default);

        Assert.True(changed);
        Assert.Equal(1, _ctx.Signature.StateCalls);
        var saved = await _ctx.Db.Context.ContractDrafts.SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.PartiallySigned, saved.Status);
    }

    [Fact]
    public async Task Refresh_on_a_draft_that_was_never_sent_does_nothing()
    {
        var template = await _ctx.SeedTemplateAsync();
        var contract = await _ctx.SeedPurchaseAsync();
        var draft = await _ctx.Create().ExecuteAsync(ContractDraftContractType.Purchase, contract.Key, template.Key,
            ContractDraftType.Contract, "m", "t", default);

        var changed = await _ctx.RefreshState().ExecuteAsync(draft.Key, "tester", default);

        Assert.False(changed);
        Assert.Equal(0, _ctx.Signature.StateCalls);
    }
}
```

Fábricas a acrescentar no `ContractDraftsTestContext`:

```csharp
    public ContractDraftsCancelService Cancel() => new(
        Db.Context, Loader(), Signature, PurchaseLog(), SalesLog(),
        NullLogger<ContractDraftsCancelService>.Instance);

    public ContractDraftsRefreshStateService RefreshState() => new(Loader(), Signature, ApplyState());
```

- [ ] **Step 2: Os serviços**

```csharp
// SiagroB1.Application/Services/ContractDrafts/ContractDraftsCancelService.cs
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>
/// Cancela a minuta no provedor e aqui. NÃO mexe no <c>SignatureStatus</c> do contrato: o
/// usuário pode estar cancelando a via eletrônica justamente porque assinou em papel.
/// </summary>
public class ContractDraftsCancelService(
    AppDbContext context,
    ContractDraftsLoader loader,
    IESignatureProvider provider,
    PurchaseContractsChangeLogService purchaseLog,
    SalesContractsChangeLogService salesLog,
    ILogger<ContractDraftsCancelService> logger)
{
    public const string NotSentMessage = "Só é possível cancelar minuta enviada para assinatura.";

    public async Task ExecuteAsync(Guid key, string userName, CancellationToken ct = default)
    {
        var draft = await loader.RequireDraftAsync(key, ct);

        if (draft.Status is not (ContractDraftStatus.AwaitingSignature or ContractDraftStatus.PartiallySigned))
            throw new BusinessException(NotSentMessage);

        var result = await provider.CancelAsync(draft.ExternalDocumentId ?? "", ct);
        if (!result.Succeeded)
        {
            logger.LogWarning("Provedor recusou cancelar a minuta {Key}", key);
            throw new BusinessException(result.ErrorMessage ?? "Não foi possível cancelar a minuta no provedor.");
        }

        draft.Status = ContractDraftStatus.Canceled;
        draft.CanceledAt = DateTime.Now;
        draft.CanceledBy = userName;
        draft.UpdatedAt = DateTime.Now;
        draft.UpdatedBy = userName;

        var what = ContractChangeLogFields.DescribeDraft(draft.Sequence, "cancelada");
        if (draft.PurchaseContractKey is { } pk) purchaseLog.Register(pk, ContractChangeLogFields.Draft, null, what, userName);
        if (draft.SalesContractKey is { } sk) salesLog.Register(sk, ContractChangeLogFields.Draft, null, what, userName);

        await context.SaveChangesAsync(ct);
    }
}
```

```csharp
// SiagroB1.Application/Services/ContractDrafts/ContractDraftsRefreshStateService.cs
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>
/// Busca a situação no provedor e aplica. É o botão "Atualizar situação" da tela e o que o job
/// de reconciliação faz por minuta — a mesma porta de escrita, nunca uma segunda.
/// </summary>
public class ContractDraftsRefreshStateService(
    ContractDraftsLoader loader,
    IESignatureProvider provider,
    ContractDraftsApplyProviderStateService apply)
{
    public async Task<bool> ExecuteAsync(Guid key, string userName, CancellationToken ct = default)
    {
        var draft = await loader.RequireDraftAsync(key, ct);

        if (draft.Status is not (ContractDraftStatus.AwaitingSignature or ContractDraftStatus.PartiallySigned)
            || string.IsNullOrWhiteSpace(draft.ExternalDocumentId))
            return false;

        var state = await provider.GetStateAsync(draft.ExternalDocumentId, ct);
        return await apply.ExecuteAsync(key, state, userName, ct);
    }
}
```

- [ ] **Step 3: Rodar os testes** → PASS (5).

- [ ] **Step 4: Commit**

```bash
git add SiagroB1.Application/Services/ContractDrafts SiagroB1.Application.Tests/ContractDrafts
git commit -m "feat(platform): cancelar minuta e atualizar situacao sob demanda

Cancelar nao mexe no SignatureStatus do contrato: pode ter sido assinado em
papel. Atualizar situacao busca no provedor e passa pela mesma porta de escrita
do webhook."
```

---

## Task 6: Webhook do D4Sign e rota no Gateway

**Files:**
- Create: `SiagroB1.Web/Hooks/D4SignWebhookEndpoint.cs`
- Modify: `SiagroB1.Web/Program.cs` (`MapD4SignWebhook(app)` + aviso de boot)
- Modify: `SiagroB1.Gateway/appsettings.json`, `appsettings.Development.json`, `appsettings.Yokotobi-Development.json` (e demais variantes que existirem) — rota `hooks-route`
- Test: `SiagroB1.Application.Tests/ContractDrafts/D4SignWebhookSecretTests.cs`

**Interfaces:**
- Consumes: Task 4.
- Produces: `D4SignWebhookEndpoint.MapD4SignWebhook(this WebApplication app)`; `D4SignWebhookEndpoint.SecretMatches(string? configured, string? received) : bool`; `D4SignWebhookEndpoint.SecretKey = "Signature:D4Sign:WebhookSecret"`.

**Fail-closed:** segredo **não configurado ⇒ 401 sempre**, ao contrário do `ScaleClientAuth`, que é permissivo por padrão. Um webhook aberto deixa qualquer um marcar contrato como assinado.

- [ ] **Step 1: Teste da comparação do segredo**

```csharp
// SiagroB1.Application.Tests/ContractDrafts/D4SignWebhookSecretTests.cs
using SiagroB1.Web.Hooks;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class D4SignWebhookSecretTests
{
    [Theory]
    [InlineData(null, "qualquer")]      // não configurado: fail-closed
    [InlineData("", "qualquer")]
    [InlineData("SEGREDO", null)]
    [InlineData("SEGREDO", "")]
    [InlineData("SEGREDO", "segredo")]  // diferencia maiúsculas
    [InlineData("SEGREDO", "SEGRED")]   // tamanhos diferentes
    public void Rejects(string? configured, string? received) =>
        Assert.False(D4SignWebhookEndpoint.SecretMatches(configured, received));

    [Fact]
    public void Accepts_the_exact_secret() =>
        Assert.True(D4SignWebhookEndpoint.SecretMatches("SEGREDO", "SEGREDO"));
}
```

- [ ] **Step 2: O endpoint**

```csharp
// SiagroB1.Web/Hooks/D4SignWebhookEndpoint.cs
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Web.Hooks;

/// <summary>
/// Recebe os eventos do D4Sign. O payload NÃO é fonte de verdade: o D4Sign não assina o webhook,
/// então qualquer um que descubra a URL poderia postar "assinado". Do form só se usa o uuid, para
/// achar a minuta; a situação vem sempre de <see cref="IESignatureProvider.GetStateAsync"/>.
///
/// Fail-closed: segredo não configurado ⇒ 401 sempre.
/// </summary>
public static class D4SignWebhookEndpoint
{
    public const string SecretKey = "Signature:D4Sign:WebhookSecret";

    /// <summary>Comparação em tempo fixo. Segredo vazio dos dois lados NÃO é igualdade.</summary>
    public static bool SecretMatches(string? configured, string? received)
    {
        if (string.IsNullOrWhiteSpace(configured) || string.IsNullOrWhiteSpace(received))
            return false;

        var a = Encoding.UTF8.GetBytes(configured);
        var b = Encoding.UTF8.GetBytes(received);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static void MapD4SignWebhook(this WebApplication app)
    {
        app.MapPost("/hooks/d4sign/{secret}", async (
            string secret,
            HttpRequest request,
            IConfiguration configuration,
            AppDbContext context,
            IESignatureProvider provider,
            ContractDraftsApplyProviderStateService apply,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("D4SignWebhook");

            if (!SecretMatches(configuration[SecretKey], secret))
            {
                logger.LogWarning("Webhook do D4Sign recusado: segredo inválido ou não configurado");
                return Results.Unauthorized();
            }

            string? uuid = null;
            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync(ct);
                uuid = form["uuid"].ToString();
            }

            if (string.IsNullOrWhiteSpace(uuid))
                return Results.Ok();

            var draftKey = await context.ContractDrafts
                .Where(d => d.ExternalDocumentId == uuid)
                .Select(d => (Guid?)d.Key)
                .FirstOrDefaultAsync(ct);

            if (draftKey is null)
            {
                // Pode ser documento criado à mão no cofre. 200 para não gastar as 7 tentativas do D4Sign.
                logger.LogWarning("Webhook do D4Sign para documento desconhecido {Uuid}", uuid);
                return Results.Ok();
            }

            var state = await provider.GetStateAsync(uuid, ct);
            await apply.ExecuteAsync(draftKey.Value, state, "d4sign-webhook", ct);

            return Results.Ok();
        }).AllowAnonymous();
    }
}
```

- [ ] **Step 3: `Program.cs`**

Ao lado de `MapTruckScaleWebSocket()` (procure a chamada; se não existir com esse nome, coloque junto dos outros `Map*` depois de `app.UseAuthorization()`):

```csharp
app.MapD4SignWebhook();
```

Junto dos outros avisos de boot, antes de `await app.RunAsync();`:

```csharp
WarnIfD4SignWebhookIsUnprotected(app);
```

E ao lado das funções existentes:

```csharp
/// <summary>
/// Avisa que o webhook do D4Sign vai recusar tudo. Sem segredo configurado o endpoint é
/// fail-closed (401), então o estado das minutas só avança pelo job de reconciliação — que
/// roda a cada 30 min. Não derruba o serviço.
/// </summary>
static void WarnIfD4SignWebhookIsUnprotected(WebApplication app)
{
    if (!app.Configuration.GetValue("Signature:Enabled", false))
        return;
    if (!string.IsNullOrWhiteSpace(app.Configuration[D4SignWebhookEndpoint.SecretKey]))
        return;

    app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("D4SignWebhook")
        .LogWarning(
            "WEBHOOK DO D4SIGN SEM SEGREDO ({Key} vazia) com assinatura habilitada. O endpoint " +
            "recusa tudo com 401; as minutas só avançam pela reconciliação a cada 30 minutos.",
            D4SignWebhookEndpoint.SecretKey);
}
```

Acrescente `using SiagroB1.Web.Hooks;` aos usings do `Program.cs`.

- [ ] **Step 4: Rota no Gateway**

Em **cada** `SiagroB1.Gateway/appsettings*.json`, dentro de `ReverseProxy:Routes`, acrescente — copiando a forma exata das rotas vizinhas do arquivo, que podem diferir deste esqueleto:

```json
"hooks-route": {
  "ClusterId": "backend",
  "AuthorizationPolicy": null,
  "Match": { "Path": "/hooks/{**catch-all}" }
}
```

Só o endpoint do webhook responde sob `/hooks` no Web — nada mais fica exposto por essa rota.

- [ ] **Step 5: Build e testes**

Run: `dotnet build SiagroB1.sln`; `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~D4SignWebhookSecretTests"` → PASS (7).

Fumaça manual: suba o Web com `--launch-profile yktb` e
`curl -s -o /dev/null -w "%{http_code}" -X POST http://localhost:50000/hooks/d4sign/errado` → **401**.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Web SiagroB1.Gateway SiagroB1.Application.Tests/ContractDrafts/D4SignWebhookSecretTests.cs
git commit -m "feat(platform): receber eventos de assinatura do D4Sign

Segredo na rota comparado em tempo fixo e fail-closed: sem segredo configurado
o endpoint recusa tudo. Do payload so se usa o uuid; a situacao vem sempre do
GetState, porque o D4Sign nao assina o webhook."
```

---

## Task 7: Job de reconciliação

**Files:**
- Create: `SiagroB1.Application/Jobs/ContractDraftsReconcileJob.cs`
- Modify: `SiagroB1.Web/Program.cs` (registro condicional do recurring job)
- Test: `SiagroB1.Application.Tests/ContractDrafts/ContractDraftsReconcileJobTests.cs`

**Interfaces:**
- Consumes: Tasks 1, 4 e 5.
- Produces: `ContractDraftsReconcileJob.ExecuteAsync(CancellationToken ct) : Task<int>` (quantas minutas foram atualizadas); `ContractDraftsReconcileJob.RecurringJobId = "contract-drafts-reconcile"`.

**Regras (spec):** `AwaitingSignature`/`PartiallySigned` com `UpdatedAt < now − 6 h`, no máximo **50** por execução, `[AutomaticRetry(Attempts = 0)]`.

- [ ] **Step 1: Testes**

```csharp
// SiagroB1.Application.Tests/ContractDrafts/ContractDraftsReconcileJobTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Jobs;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class ContractDraftsReconcileJobTests
{
    private readonly ContractDraftsTestContext _ctx = new();

    private ContractDraftsReconcileJob Job() => new(
        _ctx.Db.Context, _ctx.RefreshState(), NullLogger<ContractDraftsReconcileJob>.Instance);

    private async Task<ContractDraft> SentDraftAsync(DateTime updatedAt)
    {
        var template = await _ctx.SeedTemplateAsync();
        var contract = await _ctx.SeedPurchaseAsync();
        var draft = await _ctx.Create().ExecuteAsync(ContractDraftContractType.Purchase, contract.Key, template.Key,
            ContractDraftType.Contract, "m", "t", default);
        await _ctx.SeedSignatoriesAsync();
        await _ctx.SendToSignature().ExecuteAsync(draft.Key, "tester", default);

        draft.UpdatedAt = updatedAt;
        await _ctx.Db.Context.SaveChangesAsync();
        return draft;
    }

    [Fact]
    public async Task Picks_up_drafts_untouched_for_more_than_six_hours()
    {
        var draft = await SentDraftAsync(DateTime.Now.AddHours(-7));
        _ctx.Signature.StateIs(new ESignatureDocumentState(ESignatureDocumentStatus.Pending,
            [new ESignatureSignerState("diretor@tagui.com", true, DateTime.Now, null)]));

        var updated = await Job().ExecuteAsync(default);

        Assert.Equal(1, updated);
        var saved = await _ctx.Db.Context.ContractDrafts.SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.PartiallySigned, saved.Status);
    }

    [Fact]
    public async Task Skips_drafts_touched_recently()
    {
        await SentDraftAsync(DateTime.Now.AddMinutes(-10));

        var updated = await Job().ExecuteAsync(default);

        Assert.Equal(0, updated);
        Assert.Equal(0, _ctx.Signature.StateCalls);
    }

    [Fact]
    public async Task Skips_drafts_in_a_terminal_status()
    {
        var draft = await SentDraftAsync(DateTime.Now.AddHours(-7));
        draft.Status = ContractDraftStatus.Signed;
        await _ctx.Db.Context.SaveChangesAsync();

        var updated = await Job().ExecuteAsync(default);

        Assert.Equal(0, updated);
        Assert.Equal(0, _ctx.Signature.StateCalls);
    }
}
```

- [ ] **Step 2: O job**

```csharp
// SiagroB1.Application/Jobs/ContractDraftsReconcileJob.cs
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Jobs;

/// <summary>
/// Rede de segurança do webhook: se um evento do D4Sign se perder (URL fora do ar, sete tentativas
/// esgotadas), a minuta ficaria parada para sempre. A cada 30 min, olha as que não mudam há mais de
/// 6 h e busca a situação no provedor.
///
/// Sem retry do Hangfire: a próxima execução já é a retentativa, e uma falha do provedor não
/// melhora em segundos.
/// </summary>
[AutomaticRetry(Attempts = 0)]
public class ContractDraftsReconcileJob(
    AppDbContext context,
    ContractDraftsRefreshStateService refreshState,
    ILogger<ContractDraftsReconcileJob> logger)
{
    public const string RecurringJobId = "contract-drafts-reconcile";
    public const string CronExpression = "*/30 * * * *";

    private const int BatchSize = 50;
    private static readonly TimeSpan Staleness = TimeSpan.FromHours(6);

    /// <returns>Quantas minutas mudaram de estado.</returns>
    public async Task<int> ExecuteAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.Now - Staleness;

        var keys = await context.ContractDrafts.AsNoTracking()
            .Where(d => (d.Status == ContractDraftStatus.AwaitingSignature || d.Status == ContractDraftStatus.PartiallySigned)
                        && d.ExternalDocumentId != null
                        && d.UpdatedAt < cutoff)
            .OrderBy(d => d.UpdatedAt)
            .Take(BatchSize)
            .Select(d => d.Key)
            .ToListAsync(ct);

        if (keys.Count == 0) return 0;

        var changed = 0;
        foreach (var key in keys)
        {
            try
            {
                if (await refreshState.ExecuteAsync(key, "reconciliacao", ct)) changed++;
            }
            catch (Exception e)
            {
                // Uma minuta problemática não pode parar as outras 49.
                logger.LogError(e, "Falha ao reconciliar a minuta {Key}", key);
            }
        }

        logger.LogInformation("Reconciliação de minutas: {Changed} de {Total} atualizadas", changed, keys.Count);
        return changed;
    }
}
```

- [ ] **Step 3: Registro no `Program.cs`**

Junto do bloco que registra/remove `sap-user-sync` (mesmo padrão de registrar só quando ligado):

```csharp
if (app.Configuration.GetValue("Signature:Enabled", false))
{
    RecurringJob.AddOrUpdate<ContractDraftsReconcileJob>(
        ContractDraftsReconcileJob.RecurringJobId,
        job => job.ExecuteAsync(CancellationToken.None),
        ContractDraftsReconcileJob.CronExpression);
}
else
{
    // Desligar a assinatura não pode deixar job órfão chamando um provedor não configurado.
    RecurringJob.RemoveIfExists(ContractDraftsReconcileJob.RecurringJobId);
}
```

- [ ] **Step 4: Rodar os testes** → PASS (3).

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application/Jobs/ContractDraftsReconcileJob.cs SiagroB1.Web/Program.cs SiagroB1.Application.Tests/ContractDrafts/ContractDraftsReconcileJobTests.cs
git commit -m "feat(platform): reconciliar minutas paradas com o provedor

Rede de seguranca do webhook: a cada 30 min, minutas sem mexer ha 6 h buscam a
situacao no D4Sign. Sem retry do Hangfire e registrado so quando a assinatura
esta habilitada."
```

---

## Task 8: Actions e functions OData da Fase 2, DI e EDM

**Files:**
- Create: `SiagroB1.Web/Actions/ContractDrafts/ContractDraftsSendToSignatureController.cs`, `ContractDraftsCancelController.cs`
- Create: `SiagroB1.Web/Functions/ContractDrafts/ContractDraftsRefreshStateController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, `SiagroB1.Web/Program.cs` (HttpClient tipado do D4Sign)
- Modify: `SiagroB1.Application.Tests/ContractDrafts/ContractDraftEdmModelTests.cs`

**Interfaces:**
- Consumes: Tasks 2, 3, 4 e 5.
- Produces: actions `ContractDraftsSendToSignature(Key)`, `ContractDraftsCancel(Key)`; function `ContractDraftsRefreshState(Key)`.

- [ ] **Step 1: Ampliar o teste de EDM**

Acrescente aos `[Theory]` existentes de `ContractDraftEdmModelTests` (não crie arquivo novo):

```csharp
    [InlineData("ContractDraftsSendToSignature", "Key")]
    [InlineData("ContractDraftsCancel", "Key")]
```

no teste `The_actions_declare_their_parameters`, e

```csharp
    [InlineData("ContractDraftsRefreshState", "Key")]
```

no teste `The_functions_declare_their_parameters`.

- [ ] **Step 2: Controllers**

```csharp
// SiagroB1.Web/Actions/ContractDrafts/ContractDraftsSendToSignatureController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ContractDrafts;

public class ContractDraftsSendToSignatureController(ContractDraftsSendToSignatureService service) : ODataController
{
    [HttpPost("odata/ContractDraftsSendToSignature")]
    public async Task<IActionResult> Send([FromBody] ODataActionParameters parameters, CancellationToken ct)
    {
        if (!ContractDraftActionParameters.TryGetGuid(parameters, "Key", out var key))
            return BadRequest("Minuta é obrigatória.");

        try
        {
            await service.ExecuteAsync(key, User.Identity?.Name ?? "Unknown", ct);
            return Ok();
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException) return NotFound(e.Message);
            if (e is DefaultException or BusinessException or ApplicationException) return BadRequest(e.Message);
            return StatusCode(500, e.Message);
        }
    }
}
```

```csharp
// SiagroB1.Web/Actions/ContractDrafts/ContractDraftsCancelController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ContractDrafts;

public class ContractDraftsCancelController(ContractDraftsCancelService service) : ODataController
{
    [HttpPost("odata/ContractDraftsCancel")]
    public async Task<IActionResult> Cancel([FromBody] ODataActionParameters parameters, CancellationToken ct)
    {
        if (!ContractDraftActionParameters.TryGetGuid(parameters, "Key", out var key))
            return BadRequest("Minuta é obrigatória.");

        try
        {
            await service.ExecuteAsync(key, User.Identity?.Name ?? "Unknown", ct);
            return Ok();
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException) return NotFound(e.Message);
            if (e is DefaultException or BusinessException or ApplicationException) return BadRequest(e.Message);
            return StatusCode(500, e.Message);
        }
    }
}
```

```csharp
// SiagroB1.Web/Functions/ContractDrafts/ContractDraftsRefreshStateController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.ContractDrafts;

public class ContractDraftsRefreshStateController(ContractDraftsRefreshStateService service) : ODataController
{
    [HttpGet("odata/ContractDraftsRefreshState(Key={key})")]
    public async Task<ActionResult<bool>> Refresh([FromRoute] Guid key, CancellationToken ct)
    {
        try { return Ok(await service.ExecuteAsync(key, User.Identity?.Name ?? "Unknown", ct)); }
        catch (NotFoundException e) { return NotFound(e.Message); }
        catch (BusinessException e) { return BadRequest(e.Message); }
    }
}
```

- [ ] **Step 3: EDM, DI e HttpClient**

Em `ODataConfigurations.cs`, logo após o bloco `contractDraftsDownloadPdf` da Fase 1:

```csharp
        var contractDraftsSendToSignature = modelBuilder.Action("ContractDraftsSendToSignature");
        contractDraftsSendToSignature.Parameter<Guid>("Key");
        contractDraftsSendToSignature.Returns<IActionResult>();

        var contractDraftsCancel = modelBuilder.Action("ContractDraftsCancel");
        contractDraftsCancel.Parameter<Guid>("Key");
        contractDraftsCancel.Returns<IActionResult>();

        var contractDraftsRefreshState = modelBuilder.Function("ContractDraftsRefreshState");
        contractDraftsRefreshState.Parameter<Guid>("Key");
        contractDraftsRefreshState.Returns<bool>();
```

Em `ServiceCollectionExtensions.cs`, no bloco `// contract drafts (minutas — fase 1)`:

```csharp
        services.AddScoped<ContractDraftsSendToSignatureService>();
        services.AddScoped<ContractDraftsApplyProviderStateService>();
        services.AddScoped<ContractDraftsCancelService>();
        services.AddScoped<ContractDraftsRefreshStateService>();
        services.AddScoped<ContractDraftsReconcileJob>();
```

Em `Program.cs`, ao lado do `AddHttpClient` do WhatsApp:

```csharp
// Assinatura eletrônica. HttpClient tipado como o do WhatsApp; credenciais são lidas a cada
// chamada pelo provider, por isso só o endereço e o timeout ficam aqui.
builder.Services.AddHttpClient<IESignatureProvider, D4SignProvider>(client =>
{
    var baseUrl = builder.Configuration["Signature:D4Sign:BaseUrl"]
                  ?? "https://sandbox.d4sign.com.br/api/v1";

    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
```

Usings novos no `Program.cs`: `SiagroB1.Application.Jobs;` (se ainda não houver) e `SiagroB1.Infra.ESignature.D4Sign;`.

- [ ] **Step 4: Build, testes e fumaça**

Run: `dotnet build SiagroB1.sln`; `dotnet test SiagroB1.Application.Tests` (suíte inteira) → PASS.

Suba o Web com `--launch-profile yktb` e confira no `$metadata` servido:

```bash
curl -s "http://localhost:50000/odata/\$metadata" | grep -oE '(Action|Function) Name="ContractDrafts(SendToSignature|Cancel|RefreshState)"'
```

Espera-se as três linhas.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Web SiagroB1.Application.Tests/ContractDrafts/ContractDraftEdmModelTests.cs
git commit -m "feat(platform): expor envio, cancelamento e atualizacao de minuta por OData

Fecha a fase 2: a tela envia para assinatura, cancela e forca a leitura da
situacao no provedor pelas mesmas portas que o webhook usa."
```

---

## Verificação final da fase

1. `dotnet build SiagroB1.sln` sem erros; `dotnet test SiagroB1.Application.Tests` inteira verde.
2. **Fumaça manual no sandbox do D4Sign** — é o único jeito de provar a fase, e nenhum teste substitui:
   - `Signature:Enabled=true`, `TokenApi`/`CryptKey`/`SafeId` do sandbox e `Signature:Pdf:ChromiumPath` apontando para o Chrome local, tudo no `appsettings.Development.json` da sua máquina. **Não commite este arquivo com segredo.**
   - `WebhookBaseUrl` precisa ser alcançável pelo D4Sign: túnel (ngrok/cloudflared) apontando para o Gateway, ou a homologação da Yokotobi.
   - Fluxo: criar minuta → enviar para assinatura → conferir o documento no cofre do D4Sign com os dois signatários e os `act` corretos → assinar com um dos dois → webhook chega e a minuta vira `PartiallySigned` → assinar com o outro → minuta vira `Signed`, PDF assinado aparece como anexo do contrato e o contrato fica `Signed`.
   - Repetir o webhook do mesmo evento (o D4Sign reenvia) e confirmar que **não** duplica anexo nem log.
   - Cancelar uma minuta enviada e confirmar que o contrato **não** muda de `SignatureStatus`.
3. `curl -X POST <gateway>/hooks/d4sign/errado` → **401**.
4. `git log --oneline main..feature/contract-drafts-esignature` mostra os 8 commits da Fase 1 mais os 8 desta fase, cada um com o rodapé exigido.
5. Só então o plano do **frontend** (tela de minutas, editor, botões de enviar/cancelar/atualizar) e, depois, o merge em `main` — que é decisão do Paulo.

## Pontos que podem exigir ajuste na execução

Declarados aqui porque só aparecem com o código na frente:

- **Propriedades de `CompanySignatory`/`BusinessPartnerSignatory`**: a Task 3 assume `Name`, `TaxId`, `Email`, `Role`, `Order`, `Active`, e `BranchCode` só na empresa. Confirme antes de escrever o seed e o filtro.
- **`*AttachmentsCreateService.SaveAsync` pode salvar por dentro.** Se salvar, o anexo é gravado fora da transação do `ApplyProviderState`; é o padrão da casa e o teste de idempotência é quem protege.
- **Formato real das respostas do D4Sign.** Os contratos da Task 2 seguem a documentação e o uso da Tagui, mas `GET /documents/{uuid}` pode devolver objeto em vez de lista em algumas versões da API. O teste `Get_state_reads_the_document_status_and_the_signer_list` fixa o formato esperado: se o sandbox devolver outro, ajuste o record **e** o teste juntos.
- **Rotas do Gateway** variam entre os `appsettings*.json`. Copie a forma das vizinhas em cada arquivo em vez de colar o esqueleto deste plano.
