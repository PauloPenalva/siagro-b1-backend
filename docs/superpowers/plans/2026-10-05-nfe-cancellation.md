# Cancelamento de NF-e (saída e entrada) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cancelar na SEFAZ (evento 110111, com justificativa) a NF-e autorizada do Documento de Saída e do Documento de Entrada próprio — inclusive devoluções —, cancelando o documento com os estornos de hoje e guardando tudo o que a escrituração futura vai precisar.

**Architecture:** O cliente Fiscal ganha o evento de cancelamento (`CancelAsync`) e passa a devolver o evento de cancelamento na consulta. Na Application, um `NfeCancellationHandlerBase<T>` faz a fase 1 (grava a NF-e cancelada e o `procEventoNFe`, salva) e a fase 2 (cancela o documento pelo serviço de cancelamento existente, com captura do erro para o "Concluir cancelamento"); o `NfeCancelServiceBase<T>` valida, roda o ensaio das travas locais, chama a SEFAZ e entrega o resultado ao handler; a consulta reconhece o cStat 101. As travas passam a tratar `Cancelled` como `Authorized`, e o Estornar recusa documento com NF-e emitida.

**Tech Stack:** .NET 10 / EF Core 10 / OData v4 / Zeus.Net.NFe.NFCe 2026.9.24.1416 (xUnit + EF InMemory); OpenUI5 1.141 + TypeScript (QUnit).

**Spec:** `siagro-b1-backend/docs/superpowers/specs/2026-10-05-nfe-cancellation-design.md`.

## Global Constraints

- Código (classes, tabelas, colunas) em inglês; texto que o usuário lê (labels, mensagens) em **pt-BR**; comentários em pt-BR.
- O fluxo novo só vale para `NfeStatus = Authorized` (só existe na filial que emite NF-e pelo Siagro). Documento com `NfeStatus = None` (Yokotobi/SAPB1, MH Agro, terceiro): **cancelamento comum idêntico ao de hoje**.
- Justificativa: `Trim()`, 15 a 255 caracteres — "A justificativa deve ter entre 15 e 255 caracteres."
- Evento: `idLote = 1`, `nSeqEvento = 1`, `dhEvento` em Brasília (−03:00), CNPJ do emitente = `ChaveNFe.Substring(6, 14)`.
- cStat: 135/155 = cancelamento registrado; 573 = duplicidade de evento (resolve pela consulta); 101 na consulta = NF-e cancelada.
- `NfeStatus.Cancelled = 5`; `NfeXmlKind.CancellationEvent = 4`; colunas `NfeCancellationProtocol` VARCHAR(20), `NfeCancelledAt` datetime2, `NfeCancellationReason` VARCHAR(255), `NfeCancellationError` VARCHAR(500), nas duas tabelas.
- **Os testes de hoje não mudam de asserção.** Exceção prevista: nenhuma (só construtores de `*NfeConsultService` ganham um parâmetro nos testes que os criam).
- Mensagens de guarda/negócio: `DefaultException` em pt-BR (o controller devolve 400). O cancelamento de saída de hoje lança `ApplicationException`: o caminho novo converte em `DefaultException`.
- Migration única `AddNfeCancellation` (aditiva, `AppDbContext`). Gerar e aplicar a partir de `siagro-b1-backend/` com `ASPNETCORE_ENVIRONMENT=Ceagui-Development` (banco `CEAGUI_SIAGRO_DEV`). **Ler a migration gerada antes de seguir** (snapshot pode estar dessincronizado: só as 8 colunas novas podem aparecer). Não aplicar em nenhum outro banco.
- Novo arquivo ⇒ `git add <arquivo>` imediato no repo dele.
- Branch nos dois repos: `feature/nfe-cancellation` (backend já criado em 8781bd9; frontend criar de `main` na Task 8). Conferir o branch antes de cada commit. Commits por tarefa, padrão `tipo(escopo): descrição pt-BR` (tipos `feat fix refactor perf chore docs test`; escopo `invoice`); trailer `DB: AddNfeCancellation` no commit da migration. Mensagem termina com:
  ```
  Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01BgJE3fiJjTbTPaTvZhCpnx
  ```
  **Sempre com pathspec explícito** (`git commit -F <msg> -- <arquivos>`): no backend `docs/superpowers/{specs,plans}/2026-10-01-nfe-standalone-taxation*` estão staged e **não** entram; no frontend `.vscode/.advpl/*.tlpp` estão modificados e **não** entram. Nunca push, nunca merge.
- Commit negado pelo classificador do auto mode: grave a mensagem num arquivo, registre o comando no relatório e siga. Nunca contornar.
- Testes backend (de `siagro-b1-backend/`): `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Classe>"`; suíte inteira `dotnet test SiagroB1.Application.Tests` e `dotnet test SiagroB1.Fiscal.Tests`. Antes de compilar, pare qualquer `SiagroB1.Web`/`Gateway`/`Reports` iniciado por você (travam as DLLs).
- Frontend (de `siagro-b1-frontend/`): `yarn ts-typecheck`, `yarn lint`, `npx ui5lint` (medir a base no início da Task 8 — não pode subir), QUnit: `npx ui5 serve --port 8081` em segundo plano e `npx ui5-test-runner --url "http://localhost:8081/test/Test.qunit.html?testsuite=test-resources/siagrob1/testsuite.qunit&test=unit/unitTests" --report-dir <pasta fora do repo>`; depois pare o `ui5 serve` pelo PID e confira que não ficou pasta `report/` no repo. `yarn test` nunca passa (gate de cobertura) — não use.
- Frontend: enum OData em expressão usa `${path: 'X', targetType: 'any'}`; `--` dentro de comentário XML mata o fragmento; propriedade nova lida na lista com `$select` explícito precisa entrar no `$select`.
- Envio real à SEFAZ (homologação) só na Task 11 e **só com confirmação do usuário a cada envio**.

## Review Focus

1. **Cancelar a devolução própria de venda** tem de devolver a venda de origem a Confirmada (a origem está `Returned`), passando pela checagem "retorno confirmado não pode ser cancelado", que hoje barraria a devolução — teste na Task 5 (`Cancelling_an_authorized_sales_return_restores_the_origin`).
2. **Documento com NF-e cancelada e fase 2 pendente** (`NfeStatus = Cancelled`, documento Pendente/Confirmado): não pode ser reemitido, editado, estornado nem cancelado pelo caminho comum — testes na Task 3 (`Cancelled_nfe_cannot_be_issued_again`, `Reverse_is_refused_for_emitted_nfe`, `Cancelled_nfe_document_cannot_change_the_customer`, `Common_cancel_of_emitted_nfe_points_to_the_right_path`).
3. **Recusa local descoberta só depois de a SEFAZ aceitar** (devolução criada entre o ensaio e a fase 2): a NF-e fica Cancelada, o erro vai para `NfeCancellationError`, e o "Concluir" resolve depois — teste na Task 5 (`Local_failure_after_sefaz_keeps_the_nfe_cancelled_and_records_the_error` + `Complete_cancellation_runs_only_the_local_phase`).
4. **Justificativa com espaços nas pontas** (`"   motivo curto  "`): conta depois do trim e grava sem os espaços — teste na Task 5 (`Justification_is_trimmed_before_counting_and_saving`).
5. **Timeout no envio do evento que entrou na SEFAZ**: a próxima "Consultar situação" (agora visível na autorizada) faz o cancelamento completo — teste na Task 6 (`Consult_of_authorized_nfe_cancelled_at_sefaz_cancels_the_document`); e repetir o cancelamento (573) também — teste na Task 5 (`Duplicate_event_is_resolved_by_the_consult`).

---

### Task 1: Evento de cancelamento no cliente Fiscal

**Files:**
- Modify: `SiagroB1.Fiscal/Nfe/NfeSefaz.cs`
- Modify: `SiagroB1.Fiscal/Nfe/ZeusNfeSefazClient.cs`
- Modify: `SiagroB1.Fiscal.Tests/Nfe/NfeSefazResponseMapperTests.cs`
- Modify: `SiagroB1.Application.Tests/Support/FakeNfeSefazClient.cs`

**Interfaces:**
- Produces:
  - `record NfeCancelRequest(string AccessKey, string AuthorizationProtocol, string Justification, string IssuerDocument, DateTimeOffset EventAt)`
  - `record NfeEventResult(int StatusCode, string Reason, string? Protocol = null, DateTimeOffset? RegisteredAt = null, string? ProcEventXml = null, string? Justification = null)`
  - `NfeSefazResult` ganha o último parâmetro posicional `NfeEventResult? CancellationEvent = null`
  - `INfeSefazClient.CancelAsync(NfeCancelRequest request, NfeServiceSettings settings, CancellationToken cancellationToken = default) : Task<NfeEventResult>`
  - `NfeStatusCodes.Cancelled = 101`, `DuplicateEvent = 573`, `IsCancellationRegistered(int) => code is 135 or 155`
  - `NfeSefazResponseMapper.FromEvent(retEnvEvento response, IReadOnlyList<NFe.Classes.Servicos.Consulta.procEventoNFe>? events) : NfeEventResult`
  - Fake: `FakeNfeSefazClient.CancelResponses : Queue<Func<NfeCancelRequest, NfeEventResult>>`, `CancelRequests : List<NfeCancelRequest>`, `CancelSettings : List<NfeServiceSettings>`, `static NfeEventResult CancellationRegistered(string accessKey, int status = 135)`, `static NfeSefazResult ConsultCancelled(string accessKey, bool withEvent = true)`.

- [ ] **Step 1: Testes do mapeamento (Fiscal)** — acrescentar em `NfeSefazResponseMapperTests.cs` (usings novos: `NFe.Classes.Servicos.Evento`, `NFe.Classes.Servicos.Tipos`, `ConsultaEvento = NFe.Classes.Servicos.Consulta.procEventoNFe`):

```csharp
    private static infEventoRet EventReturn(int status, string reason, string? protocol = "135260000000099") => new()
    {
        tpAmb = TipoAmbiente.Homologacao, cStat = status, xMotivo = reason, chNFe = Key,
        tpEvento = NFeTipoEvento.TeNfeCancelamento, nSeqEvento = 1, nProt = protocol,
        ProxydhRegEvento = "2026-10-05T10:00:05-03:00",
    };

    private static ConsultaEvento CancellationEvent(int status = 135) => new()
    {
        versao = "1.00",
        evento = new evento
        {
            versao = "1.00",
            infEvento = new infEventoEnv
            {
                tpAmb = TipoAmbiente.Homologacao, chNFe = Key, tpEvento = NFeTipoEvento.TeNfeCancelamento, nSeqEvento = 1,
                verEvento = "1.00", detEvento = new detEvento { versao = "1.00", descEvento = "Cancelamento", nProt = "135260000000001", xJust = "Venda desfeita pelo cliente" },
            },
        },
        retEvento = new retEvento { versao = "1.00", infEvento = EventReturn(status, "Evento registrado e vinculado a NF-e") },
    };

    [Fact]
    public void Registered_event_uses_the_event_status_not_the_batch_status()
    {
        var result = NfeSefazResponseMapper.FromEvent(
            new retEnvEvento { cStat = 128, xMotivo = "Lote de Evento Processado",
                retEvento = [new retEvento { infEvento = EventReturn(135, "Evento registrado e vinculado a NF-e") }] },
            [CancellationEvent()]);

        Assert.Equal(135, result.StatusCode);
        Assert.True(NfeStatusCodes.IsCancellationRegistered(result.StatusCode));
        Assert.Equal("135260000000099", result.Protocol);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 10, 0, 5, TimeSpan.FromHours(-3)), result.RegisteredAt);
        Assert.Contains("<procEventoNFe", result.ProcEventXml);
        Assert.Contains("<xJust>Venda desfeita pelo cliente</xJust>", result.ProcEventXml);
    }

    [Fact]
    public void Out_of_time_registration_155_is_also_registered()
    {
        Assert.True(NfeStatusCodes.IsCancellationRegistered(155));
        Assert.False(NfeStatusCodes.IsCancellationRegistered(573));
    }

    [Fact]
    public void Rejected_event_has_no_protocol_or_xml()
    {
        var result = NfeSefazResponseMapper.FromEvent(
            new retEnvEvento { cStat = 128, xMotivo = "Lote de Evento Processado",
                retEvento = [new retEvento { infEvento = EventReturn(501, "Rejeição: Prazo de cancelamento superior ao previsto na Legislação", protocol: null) }] },
            []);

        Assert.Equal(501, result.StatusCode);
        Assert.Contains("Prazo de cancelamento", result.Reason);
        Assert.Null(result.Protocol);
        Assert.Null(result.ProcEventXml);
    }

    [Fact]
    public void Batch_level_rejection_of_the_event_keeps_the_batch_status()
    {
        var result = NfeSefazResponseMapper.FromEvent(new retEnvEvento { cStat = 215, xMotivo = "Rejeição: Falha no schema XML" }, null);

        Assert.Equal(215, result.StatusCode);
        Assert.Null(result.Protocol);
    }

    [Fact]
    public void Consult_of_cancelled_nfe_returns_101_with_the_cancellation_event()
    {
        var result = NfeSefazResponseMapper.FromConsult(new retConsSitNFe
        {
            cStat = 101, xMotivo = "Cancelamento de NF-e homologado", protNFe = Protocol(100, "Autorizado o uso da NF-e"),
            procEventoNFe = [CancellationEvent()],
        });

        Assert.Equal(NfeStatusCodes.Cancelled, result.StatusCode);
        Assert.Null(result.ProtocolXml);
        Assert.NotNull(result.CancellationEvent);
        Assert.Equal("135260000000099", result.CancellationEvent!.Protocol);
        Assert.Equal("Venda desfeita pelo cliente", result.CancellationEvent.Justification);
        Assert.Contains("<procEventoNFe", result.CancellationEvent.ProcEventXml);
    }

    [Fact]
    public void Consult_of_cancelled_nfe_without_the_event_has_no_cancellation_event()
    {
        var result = NfeSefazResponseMapper.FromConsult(new retConsSitNFe { cStat = 101, xMotivo = "Cancelamento de NF-e homologado" });

        Assert.Equal(101, result.StatusCode);
        Assert.Null(result.CancellationEvent);
    }
```

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~NfeSefazResponseMapperTests"` → erro de compilação (`FromEvent`, `IsCancellationRegistered`, `CancellationEvent` não existem).

- [ ] **Step 3: Implementar em `NfeSefaz.cs`** — usings novos `System.Globalization`, `NFe.Classes.Servicos.Evento`, `NFe.Classes.Servicos.Tipos`. Trocar o record e acrescentar os tipos:

```csharp
/// <summary>
/// Resposta da SEFAZ sem tipos da Zeus. <see cref="ProtocolXml"/> é o <c>protNFe</c> serializado
/// (entra no procNFe); só vem quando há protocolo. <see cref="CancellationEvent"/> só vem na consulta
/// de NF-e cancelada (cStat 101) que trouxe o evento.
/// </summary>
public sealed record NfeSefazResult(
    int StatusCode,
    string Reason,
    string? Protocol = null,
    DateTimeOffset? ReceivedAt = null,
    string? ProtocolXml = null,
    NfeEventResult? CancellationEvent = null);

/// <summary>Pedido de cancelamento (evento 110111). <see cref="EventAt"/> em Brasília.</summary>
public sealed record NfeCancelRequest(
    string AccessKey, string AuthorizationProtocol, string Justification, string IssuerDocument, DateTimeOffset EventAt);

/// <summary>
/// Retorno de um evento: o status é o do <c>retEvento</c> (o do lote vem 128 quando processado).
/// <see cref="ProcEventXml"/> é o <c>procEventoNFe</c> (evento assinado + retorno), guardado com o documento.
/// </summary>
public sealed record NfeEventResult(
    int StatusCode,
    string Reason,
    string? Protocol = null,
    DateTimeOffset? RegisteredAt = null,
    string? ProcEventXml = null,
    string? Justification = null);
```

Em `NfeStatusCodes`:

```csharp
    /// <summary>Consulta: NF-e cancelada (o topo é 101; o protNFe ainda traz a autorização original).</summary>
    public const int Cancelled = 101;

    /// <summary>573: o evento já está registrado na SEFAZ — resolver pela consulta.</summary>
    public const int DuplicateEvent = 573;

    /// <summary>135: evento registrado e vinculado; 155: cancelamento homologado fora de prazo.</summary>
    public static bool IsCancellationRegistered(int code) => code is 135 or 155;
```

Em `INfeSefazClient`:

```csharp
    Task<NfeEventResult> CancelAsync(NfeCancelRequest request, NfeServiceSettings settings, CancellationToken cancellationToken = default);
```

Em `NfeSefazResponseMapper`, trocar `FromConsult` e acrescentar:

```csharp
    /// <summary>
    /// O protocolo só vale quando o status do topo é de autorização/denegação: numa nota cancelada
    /// o topo é 101 e o protNFe ainda traz a autorização original (100) — aí vale o evento de cancelamento.
    /// </summary>
    public static NfeSefazResult FromConsult(retConsSitNFe response)
    {
        if (response.protNFe?.infProt is not null
            && (NfeStatusCodes.IsAuthorized(response.cStat) || NfeStatusCodes.IsDenied(response.cStat)))
            return FromProtocol(response.protNFe);

        var cancellation = response.cStat == NfeStatusCodes.Cancelled
            ? response.procEventoNFe?
                .Where(e => e.evento?.infEvento?.tpEvento == NFeTipoEvento.TeNfeCancelamento
                            && e.retEvento?.infEvento is { } info && NfeStatusCodes.IsCancellationRegistered(info.cStat))
                .Select(FromProcEvent)
                .FirstOrDefault()
            : null;

        return new NfeSefazResult(response.cStat, response.xMotivo ?? string.Empty, CancellationEvent: cancellation);
    }

    /// <summary>Retorno do envio do evento: vale o <c>retEvento</c>; sem ele, o status do lote.</summary>
    public static NfeEventResult FromEvent(
        retEnvEvento response, IReadOnlyList<NFe.Classes.Servicos.Consulta.procEventoNFe>? events)
    {
        var info = response.retEvento?.FirstOrDefault()?.infEvento;

        if (info is null)
            return new NfeEventResult(response.cStat, response.xMotivo ?? string.Empty);

        var proc = info.nProt is null ? null : events?.FirstOrDefault(e => e.retEvento?.infEvento?.nProt == info.nProt);

        return proc is not null
            ? FromProcEvent(proc)
            : new NfeEventResult(info.cStat, info.xMotivo ?? string.Empty, info.nProt, RegisteredAt(info));
    }

    private static NfeEventResult FromProcEvent(NFe.Classes.Servicos.Consulta.procEventoNFe proc)
    {
        var info = proc.retEvento.infEvento;

        return new NfeEventResult(
            info.cStat, info.xMotivo ?? string.Empty, info.nProt, RegisteredAt(info),
            FuncoesXml.ClasseParaXmlString(proc), proc.evento?.infEvento?.detEvento?.xJust);
    }

    /// <summary>
    /// <c>dhRegEvento</c> com o fuso que a SEFAZ mandou (o <c>DateTime</c> da Zeus perde o fuso).
    /// ⚠️ Se o <c>ProxydhRegEvento</c> da Zeus não devolver o texto original, o teste
    /// <c>Registered_event_uses_the_event_status_not_the_batch_status</c> acusa — ajustar aqui.
    /// </summary>
    private static DateTimeOffset? RegisteredAt(infEventoRet info) =>
        DateTimeOffset.TryParse(info.ProxydhRegEvento, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? at
            : info.dhRegEvento == default ? null : new DateTimeOffset(info.dhRegEvento);
```

- [ ] **Step 4: Implementar em `ZeusNfeSefazClient.cs`** — tornar o `RunAsync` genérico (`private static Task<T> RunAsync<T>(NfeServiceSettings settings, Func<ServicosNFe, T> call, CancellationToken cancellationToken)`, corpo igual) e acrescentar:

```csharp
    public Task<NfeEventResult> CancelAsync(
        NfeCancelRequest request, NfeServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, services =>
        {
            // Lote de um evento só; cancelamento é evento único por NF-e (nSeqEvento 1).
            var response = services.RecepcaoEventoCancelamento(
                1, 1, request.AuthorizationProtocol, request.AccessKey, request.Justification, request.IssuerDocument,
                request.EventAt);

            return NfeSefazResponseMapper.FromEvent(response.Retorno, response.ProcEventosNFe);
        }, cancellationToken);
```

- [ ] **Step 5: Fake da Application** — em `FakeNfeSefazClient.cs` (usings `NFe.Classes.Servicos.Tipos` não são necessários; o XML do evento é literal):

```csharp
    public Queue<Func<NfeCancelRequest, NfeEventResult>> CancelResponses { get; } = new();
    public List<NfeCancelRequest> CancelRequests { get; } = [];
    public List<NfeServiceSettings> CancelSettings { get; } = [];

    public Task<NfeEventResult> CancelAsync(
        NfeCancelRequest request, NfeServiceSettings settings, CancellationToken cancellationToken = default)
    {
        CancelRequests.Add(request);
        CancelSettings.Add(settings);
        return Task.FromResult(CancelResponses.Dequeue()(request));
    }

    public const string CancellationProtocol = "135260000000099";

    public static readonly DateTimeOffset CancelledAt = new(2026, 10, 2, 15, 30, 0, TimeSpan.FromHours(-3));

    public static NfeEventResult CancellationRegistered(string accessKey, int status = 135) => new(
        status, "Evento registrado e vinculado a NF-e", CancellationProtocol, CancelledAt,
        $"<procEventoNFe versao=\"1.00\"><evento><infEvento><chNFe>{accessKey}</chNFe><tpEvento>110111</tpEvento></infEvento></evento>" +
        $"<retEvento><infEvento><cStat>{status}</cStat><nProt>{CancellationProtocol}</nProt></infEvento></retEvento></procEventoNFe>");

    public static NfeSefazResult ConsultCancelled(string accessKey, bool withEvent = true) => new(
        101, "Cancelamento de NF-e homologado",
        CancellationEvent: withEvent
            ? CancellationRegistered(accessKey) with { Justification = "Cancelada direto no portal da SEFAZ" }
            : null);
```

- [ ] **Step 6: Rodar e ver passar** — `dotnet test SiagroB1.Fiscal.Tests` (tudo verde) e `dotnet build SiagroB1.sln` (0 erros; a Application.Tests compila com o fake novo).

- [ ] **Step 7: Commit** (backend, branch `feature/nfe-cancellation`):

```bash
git add SiagroB1.Fiscal/Nfe/NfeSefaz.cs SiagroB1.Fiscal/Nfe/ZeusNfeSefazClient.cs SiagroB1.Fiscal.Tests/Nfe/NfeSefazResponseMapperTests.cs SiagroB1.Application.Tests/Support/FakeNfeSefazClient.cs
git commit -F msg.txt -- SiagroB1.Fiscal/Nfe/NfeSefaz.cs SiagroB1.Fiscal/Nfe/ZeusNfeSefazClient.cs SiagroB1.Fiscal.Tests/Nfe/NfeSefazResponseMapperTests.cs SiagroB1.Application.Tests/Support/FakeNfeSefazClient.cs
```
Mensagem: `feat(invoice): evento de cancelamento da NF-e no cliente da SEFAZ` (corpo: status vem do retEvento, 101 na consulta traz o evento).

---

### Task 2: Estado da NF-e cancelada (enums, colunas, migration, PATCH protegido)

**Files:**
- Modify: `SiagroB1.Domain/Enums/NfeStatus.cs`, `SiagroB1.Domain/Enums/NfeXmlKind.cs`
- Modify: `SiagroB1.Domain/Interfaces/INfeDocument.cs`
- Modify: `SiagroB1.Domain/Entities/SalesInvoice.cs` (depois de `NfeConfirmationError`), `SiagroB1.Domain/Entities/PurchaseInvoice.cs` (idem)
- Modify: `SiagroB1.Domain/Dtos/Nfe/NfeIssueOutcomeDto.cs`
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeLock.cs`, `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceNfeLock.cs` (`IssuanceFields`, `ResetIssuanceFields`)
- Create: `SiagroB1.Migrations/AppContext/<timestamp>_AddNfeCancellation.cs` (+ Designer, snapshot)
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoiceNfeLockTests.cs`, `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceNfeLockTests.cs`

**Interfaces:**
- Produces: `NfeStatus.Cancelled = 5`; `NfeXmlKind.CancellationEvent = 4`; em `INfeDocument` (e nas duas entidades): `string? NfeCancellationProtocol`, `DateTime? NfeCancelledAt`, `string? NfeCancellationReason`, `string? NfeCancellationError` (get/set); `NfeIssueOutcomeDto.CancellationError : string?`.

- [ ] **Step 1: Teste de PATCH (saída)** — em `SalesInvoiceNfeLockTests.cs`, no estilo dos testes que usam `HeaderUpdate(db)`:

```csharp
    [Fact]
    public async Task Patch_cannot_write_the_cancellation_fields()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized);
        invoice.NfeCancellationProtocol = "999";
        invoice.NfeCancelledAt = DateTime.Now;
        invoice.NfeCancellationReason = "escrito pela API indevidamente";
        invoice.NfeCancellationError = "x";

        await HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester");

        var saved = await db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Null(saved.NfeCancellationProtocol);
        Assert.Null(saved.NfeCancelledAt);
        Assert.Null(saved.NfeCancellationReason);
        Assert.Null(saved.NfeCancellationError);
    }
```

E o equivalente em `PurchaseInvoiceNfeLockTests.cs` (`Patch_cannot_write_the_cancellation_fields`), usando o seed e o serviço de update que esse arquivo já usa para os testes de `RestoreIssuanceFields` (procure `NfeProtocol` no arquivo e copie a montagem).

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeLockTests"` → erro de compilação (propriedades não existem).

- [ ] **Step 3: Enums e interface**

`NfeStatus.cs` (doc comment passa a citar os dois documentos):

```csharp
/// <summary>Situação da NF-e STANDALONE do documento (saída ou entrada). None = nunca emitida.</summary>
public enum NfeStatus
{
    None = 0,
    Processing = 1,
    Authorized = 2,
    Rejected = 3,
    Denied = 4,
    /// <summary>Cancelamento (evento 110111) registrado na SEFAZ. Número e chave ficam queimados.</summary>
    Cancelled = 5,
}
```

`NfeXmlKind.cs`:

```csharp
    /// <summary>procEventoNFe do cancelamento (evento assinado + retorno da SEFAZ).</summary>
    CancellationEvent = 4,
```

`INfeDocument.cs`, depois de `NfeConfirmationError`:

```csharp
    string? NfeCancellationProtocol { get; set; }
    DateTime? NfeCancelledAt { get; set; }
    string? NfeCancellationReason { get; set; }
    string? NfeCancellationError { get; set; }
```

- [ ] **Step 4: Entidades** — em `SalesInvoice.cs` e `PurchaseInvoice.cs`, logo depois de `NfeConfirmationError`:

```csharp
    /// <summary>Protocolo do evento de cancelamento (evento 110111).</summary>
    [Column(TypeName = "VARCHAR(20)")]
    public string? NfeCancellationProtocol { get; set; }

    /// <summary><c>dhRegEvento</c> do cancelamento, hora de Brasília.</summary>
    public DateTime? NfeCancelledAt { get; set; }

    /// <summary>Justificativa (<c>xJust</c>) do cancelamento, 15 a 255 caracteres.</summary>
    [Column(TypeName = "VARCHAR(255)")]
    public string? NfeCancellationReason { get; set; }

    /// <summary>NF-e cancelada na SEFAZ, mas o cancelamento do documento falhou — "Concluir cancelamento" refaz.</summary>
    [Column(TypeName = "VARCHAR(500)")]
    public string? NfeCancellationError { get; set; }
```

- [ ] **Step 5: DTO** — em `NfeIssueOutcomeDto`: propriedade `public string? CancellationError { get; set; }` e `CancellationError = invoice.NfeCancellationError,` no `From`.

- [ ] **Step 6: Locks** — nos dois `*NfeLock.cs`, acrescentar ao fim de `IssuanceFields`:

```csharp
        nameof(SalesInvoice.NfeCancellationProtocol), nameof(SalesInvoice.NfeCancelledAt),
        nameof(SalesInvoice.NfeCancellationReason), nameof(SalesInvoice.NfeCancellationError),
```
(com `PurchaseInvoice` no arquivo da entrada) e em `ResetIssuanceFields`:

```csharp
        invoice.NfeCancellationProtocol = null;
        invoice.NfeCancelledAt = null;
        invoice.NfeCancellationReason = null;
        invoice.NfeCancellationError = null;
```

- [ ] **Step 7: Rodar e ver passar** — `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeLockTests"`.

- [ ] **Step 8: Migration** — `dotnet build SiagroB1.sln` e:

```bash
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add AddNfeCancellation --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --no-build
```
Ler o `Up()`: só 8 `AddColumn` (4 em `SALES_INVOICES`, 4 em `PURCHASE_INVOICES`, todas anuláveis, `varchar(20)`, `datetime2`, `varchar(255)`, `varchar(500)`). Qualquer outra operação = snapshot dessincronizado: remova-a do `Up()`/`Down()` e do snapshot (ver memória "Migrations hand-edited & baseline sync") e confirme com `dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web` → "No changes". `git add` dos 3 arquivos.

- [ ] **Step 9: Suíte** — `dotnet test SiagroB1.Application.Tests` verde.

- [ ] **Step 10: Commit** — `feat(invoice): situação Cancelada e dados do cancelamento da NF-e` com trailer `DB: AddNfeCancellation`; pathspec com os arquivos desta tarefa.

---

### Task 3: Travas — `Cancelled` congela como `Authorized`, emissão recusa, Estornar recusa NF-e emitida

**Files:**
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeLock.cs`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceNfeLock.cs`
- Modify: `SiagroB1.Application/Services/Nfe/NfeLockRules.cs`
- Modify: `SiagroB1.Application/Services/Nfe/NfeIssueServiceBase.cs` (`EnsurePreconditionsAsync`)
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesReverseConfirmService.cs`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesReverseConfirmService.cs`
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoiceNfeLockTests.cs`, `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceNfeLockTests.cs`, `SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeIssueServiceTests.cs`

**Interfaces:**
- Produces: `NfeLockRules.CancelledMessage`, `NfeLockRules.EmittedReverseMessage`, `NfeLockRules.IsFrozen(NfeStatus) => status is Authorized or Cancelled`. `EnsureCancellable` (comum) recusa `Authorized` e `Cancelled` com mensagens novas.

- [ ] **Step 1: Testes** — em `SalesInvoiceNfeLockTests.cs` (o `SeedAsync(NfeStatus, InvoiceStatus)` já existe; o cancelamento comum é montado como no teste da linha ~301):

```csharp
    [Fact]
    public async Task Cancelled_nfe_document_cannot_change_the_customer()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Cancelled);
        invoice.CardCode = "C2";

        var ex = await Assert.ThrowsAsync<DefaultException>(() => HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester"));

        Assert.Contains("cancelada", ex.Message);
    }

    [Fact]
    public void Cancelled_nfe_document_cannot_be_deleted()
    {
        var ex = Assert.Throws<DefaultException>(() =>
            SalesInvoiceNfeLock.EnsureDeletable(new SalesInvoice { NfeStatus = NfeStatus.Cancelled }));

        Assert.Contains("cancelada", ex.Message);
    }

    [Fact]
    public void Lines_of_cancelled_nfe_document_cannot_change()
    {
        Assert.Throws<DefaultException>(() => SalesInvoiceNfeLock.EnsureLinesChangeable(NfeStatus.Cancelled));
    }

    [Theory]
    [InlineData(NfeStatus.Authorized, "justificativa")]
    [InlineData(NfeStatus.Cancelled, "Concluir cancelamento")]
    public void Common_cancel_of_emitted_nfe_points_to_the_right_path(NfeStatus status, string expected)
    {
        var ex = Assert.Throws<DefaultException>(() =>
            SalesInvoiceNfeLock.EnsureCancellable(new SalesInvoice { NfeStatus = status }));

        Assert.Contains(expected, ex.Message);
    }

    [Theory]
    [InlineData(NfeStatus.Processing)]
    [InlineData(NfeStatus.Authorized)]
    [InlineData(NfeStatus.Cancelled)]
    public async Task Reverse_is_refused_for_emitted_nfe(NfeStatus status)
    {
        var (db, invoice) = await SeedAsync(status, InvoiceStatus.Confirmed);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => new SalesInvoicesReverseConfirmService(
                db, null!, null!, null!, null!, new FakeStringLocalizer<Resource>())
            .ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal(NfeLockRules.EmittedReverseMessage, ex.Message);
    }
```
(usings: `SiagroB1.Commons.Resources`, `SiagroB1.Application.Services.Nfe`.)

E o espelho em `PurchaseInvoiceNfeLockTests.cs`: `Cancelled_nfe_document_cannot_be_deleted`, `Common_cancel_of_emitted_nfe_points_to_the_right_path` (com `PurchaseInvoice`), e

```csharp
    [Theory]
    [InlineData(NfeStatus.Processing)]
    [InlineData(NfeStatus.Authorized)]
    [InlineData(NfeStatus.Cancelled)]
    public async Task Reverse_is_refused_for_emitted_nfe(NfeStatus status)
    {
        // Montar o documento Confirmado com o NfeStatus como os outros testes deste arquivo montam.
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesReverseConfirmService(db).ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal(NfeLockRules.EmittedReverseMessage, ex.Message);
    }
```

Em `SalesInvoicesNfeIssueServiceTests.cs` (o arquivo já tem o montador do serviço e um teste "já autorizada" — copie a montagem dele):

```csharp
    [Fact]
    public async Task Cancelled_nfe_cannot_be_issued_again()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var invoice = await scenario.Db.Context.SalesInvoices.SingleAsync();
        invoice.NfeStatus = NfeStatus.Cancelled;
        await scenario.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Issue(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal("A NF-e deste documento foi cancelada: o número não pode ser reutilizado.", ex.Message);
        Assert.Empty(sefaz.Sent);
    }
```
(`Issue(...)` = o montador que o arquivo já usa; se tiver outro nome, use-o.)

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeLockTests|FullyQualifiedName~SalesInvoicesNfeIssueServiceTests"`.

- [ ] **Step 3: `NfeLockRules`**:

```csharp
    public const string CancelledMessage =
        "A NF-e deste documento foi cancelada na SEFAZ: o documento não pode mudar.";

    public const string EmittedReverseMessage =
        "Documento com NF-e emitida não pode ser estornado; use o cancelamento.";

    public const string AuthorizedCancelMessage =
        "NF-e autorizada: cancele pela SEFAZ informando a justificativa.";

    public const string CancelledPendingMessage =
        "NF-e já cancelada na SEFAZ: use Concluir cancelamento.";

    /// <summary>Autorizada ou cancelada: o que foi para a nota não muda mais.</summary>
    public static bool IsFrozen(NfeStatus status) => status is NfeStatus.Authorized or NfeStatus.Cancelled;

    public static string FrozenMessage(NfeStatus status) =>
        status == NfeStatus.Cancelled ? CancelledMessage : AuthorizedMessage;
```
(using `SiagroB1.Domain.Enums`.)

- [ ] **Step 4: Locks** — nos dois arquivos:
  - `EnsureHeaderEditable` e `EnsureItemEditable`: `if (NfeLockRules.IsFrozen(status) && NfeLockRules.AnyChanged(entry, …)) throw new DefaultException(NfeLockRules.FrozenMessage(status));`
  - `EnsureLinesChangeable`: `if (NfeLockRules.IsFrozen(invoiceStatus)) throw new DefaultException(NfeLockRules.FrozenMessage(invoiceStatus));`
  - `EnsureDeletable`: `if (invoice.NfeStatus is NfeStatus.Processing or NfeStatus.Authorized or NfeStatus.Denied or NfeStatus.Cancelled) throw new DefaultException("Documento com NF-e em processamento, autorizada, denegada ou cancelada não pode ser excluído.");`
  - `EnsureCancellable`:

```csharp
    public static void EnsureCancellable(SalesInvoice invoice)
    {
        switch (invoice.NfeStatus)
        {
            case NfeStatus.Processing:
                throw new DefaultException(NfeLockRules.ProcessingMessage);
            case NfeStatus.Authorized:
                throw new DefaultException(NfeLockRules.AuthorizedCancelMessage);
            case NfeStatus.Cancelled:
                throw new DefaultException(NfeLockRules.CancelledPendingMessage);
        }
    }
```
  ⚠️ Os testes de hoje que procuram "SEFAZ" na recusa do cancelamento continuam passando ("cancele pela SEFAZ"). Rode-os.

- [ ] **Step 5: Emissão** — em `NfeIssueServiceBase.EnsurePreconditionsAsync`, no `switch`:

```csharp
            case NfeStatus.Cancelled:
                throw new DefaultException("A NF-e deste documento foi cancelada: o número não pode ser reutilizado.");
```

- [ ] **Step 6: Estornar** — em `SalesInvoicesReverseConfirmService.ExecuteAsync`, logo depois do bloco `IsNfeReturn` (antes do teste de Confirmed):

```csharp
        // NF-e emitida continua valendo na SEFAZ: desfazer saldo e alocação dela deixaria a nota
        // sem efeito no sistema. O caminho é o cancelamento da NF-e.
        if (invoice.NfeStatus is NfeStatus.Processing or NfeStatus.Authorized or NfeStatus.Cancelled)
            throw new DefaultException(NfeLockRules.EmittedReverseMessage);
```
E o mesmo em `PurchaseInvoicesReverseConfirmService`, logo depois do `FirstOrDefaultAsync` (using `SiagroB1.Application.Services.Nfe`).

- [ ] **Step 7: Rodar e ver passar** — o filtro do Step 2; depois a suíte inteira (`dotnet test SiagroB1.Application.Tests`). Se algum teste de estorno de hoje usava documento com NF-e autorizada, **pare e reporte** (não mude a asserção).

- [ ] **Step 8: Commit** — `feat(invoice): NF-e cancelada trava o documento e estorno recusa NF-e emitida`.

---

### Task 4: Serviços de cancelamento divididos em checagem + efeitos

**Files:**
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCancelService.cs`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCancelService.cs`
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesCancelServiceTests.cs` (criar), `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesCancelServiceTests.cs` (criar)

**Interfaces:**
- Produces (nos dois serviços):
  - `public Task ExecuteAsync(Guid key, string userName)` — comportamento de hoje (trava da NF-e + checagens + efeitos).
  - `public Task EnsureCanCancelAsync(Guid key)` — só as checagens de negócio (sem trava da NF-e); lança `DefaultException`.
  - `public virtual Task CancelAfterNfeAsync(Guid key, string userName)` — exige `NfeStatus = Cancelled`; checagens de negócio + efeitos (sem a trava da NF-e).
  - Saída: as checagens de negócio passam a lançar `DefaultException` (antes `ApplicationException`; o controller de hoje devolve 400 para qualquer exceção — sem mudança visível).

- [ ] **Step 1: Testes** — `SalesInvoicesCancelServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>Cancelamento do documento de saída dividido em checagem (ensaio) e efeitos (spec 2026-10-05 §7.1).</summary>
public class SalesInvoicesCancelServiceTests
{
    public static SalesInvoicesCancelService Service(UnitOfWork db) => new(db,
        new SalesShipmentReleasesRecalculateShippedService(db.Context),
        new SalesContractsAllocationDeleteForInvoiceService(db),
        new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
        NullLogger<SalesInvoicesCancelService>.Instance);

    [Fact]
    public async Task Common_cancel_records_who_and_when()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        await Service(scenario.Db).ExecuteAsync(scenario.InvoiceKey, "tester");

        var saved = await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(InvoiceStatus.Cancelled, saved.InvoiceStatus);
        Assert.Equal("tester", saved.CanceledBy);
        Assert.NotNull(saved.CanceledAt);
    }

    [Fact]
    public async Task Ensure_refuses_origin_with_an_active_nfe_return_without_changing_anything()
    {
        var scenario = await NfeReturnTestSeed.SeedAsync();
        await NfeReturnTestSeed.CreateReturnAsync(scenario, 10m);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(scenario.Sale.Db).EnsureCanCancelAsync(scenario.Sale.InvoiceKey));

        Assert.Equal("Documento de saída possui retorno.", ex.Message);
    }

    [Fact]
    public async Task Ensure_lets_an_authorized_nfe_return_through()
    {
        var scenario = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(scenario, 10m);
        var nfeReturn = await scenario.Sale.Db.Context.SalesInvoices.SingleAsync(x => x.Key == created.Key);
        nfeReturn.InvoiceStatus = InvoiceStatus.Confirmed;
        nfeReturn.NfeStatus = NfeStatus.Authorized;
        await scenario.Sale.Db.SaveChangesAsync();

        await Service(scenario.Sale.Db).EnsureCanCancelAsync(created.Key);
    }

    [Fact]
    public async Task Cancel_after_nfe_requires_the_nfe_cancelled()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(scenario.Db).CancelAfterNfeAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal("A NF-e deste documento não está cancelada na SEFAZ.", ex.Message);
    }

    [Fact]
    public async Task Cancel_after_nfe_skips_the_nfe_lock()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var invoice = await scenario.Db.Context.SalesInvoices.SingleAsync();
        invoice.NfeStatus = NfeStatus.Cancelled;
        await scenario.Db.SaveChangesAsync();

        await Service(scenario.Db).CancelAfterNfeAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(InvoiceStatus.Cancelled, (await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync()).InvoiceStatus);
    }
}
```
(Se `CreateReturnAsync` precisar de outro valor de quantidade, use o que os testes de `SalesInvoicesNfeReturnCreateServiceTests` usam.)

`PurchaseInvoicesCancelServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

public class PurchaseInvoicesCancelServiceTests
{
    [Fact]
    public async Task Origin_with_an_active_purchase_return_cannot_be_cancelled()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var origin = await scenario.Db.Context.PurchaseInvoices.SingleAsync(x => x.Key == scenario.InvoiceKey);
        scenario.Db.Context.PurchaseInvoices.Add(new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = origin.BranchCode, CardCode = origin.CardCode, InvoiceType = PurchaseInvoiceType.Return,
            IssuerType = DocumentIssuerType.Own, IsNfeReturn = true, InvoiceStatus = InvoiceStatus.Pending,
            PurchaseInvoiceOriginKey = origin.Key,
        });
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesCancelService(scenario.Db).ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal("Documento de entrada possui devolução.", ex.Message);
    }

    [Fact]
    public async Task Cancelled_return_does_not_block_the_origin()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var origin = await scenario.Db.Context.PurchaseInvoices.SingleAsync(x => x.Key == scenario.InvoiceKey);
        scenario.Db.Context.PurchaseInvoices.Add(new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = origin.BranchCode, CardCode = origin.CardCode, InvoiceType = PurchaseInvoiceType.Return,
            IssuerType = DocumentIssuerType.Own, IsNfeReturn = true, InvoiceStatus = InvoiceStatus.Cancelled,
            PurchaseInvoiceOriginKey = origin.Key,
        });
        await scenario.Db.SaveChangesAsync();

        await new PurchaseInvoicesCancelService(scenario.Db).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(InvoiceStatus.Cancelled,
            (await scenario.Db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(x => x.Key == scenario.InvoiceKey)).InvoiceStatus);
    }

    [Fact]
    public async Task Cancel_after_nfe_requires_the_nfe_cancelled_and_skips_the_lock()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var service = new PurchaseInvoicesCancelService(scenario.Db);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CancelAfterNfeAsync(scenario.InvoiceKey, "tester"));
        Assert.Equal("A NF-e deste documento não está cancelada na SEFAZ.", ex.Message);

        var invoice = await scenario.Db.Context.PurchaseInvoices.SingleAsync(x => x.Key == scenario.InvoiceKey);
        invoice.NfeStatus = NfeStatus.Cancelled;
        await scenario.Db.SaveChangesAsync();

        await service.CancelAfterNfeAsync(scenario.InvoiceKey, "tester");

        var saved = await scenario.Db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(x => x.Key == scenario.InvoiceKey);
        Assert.Equal(InvoiceStatus.Cancelled, saved.InvoiceStatus);
        Assert.Equal("tester", saved.CanceledBy);
    }
}
```
Se a entidade exigir mais campos obrigatórios no InMemory, copie-os do seed.

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~CancelServiceTests"`.

- [ ] **Step 3: `SalesInvoicesCancelService`** — reorganizar sem mudar os efeitos:

```csharp
    public Task ExecuteAsync(Guid key, string userName) => CancelAsync(key, userName, afterNfe: false);

    /// <summary>Fase 2 do cancelamento da NF-e: a SEFAZ já cancelou; a trava da NF-e não se aplica.</summary>
    public virtual Task CancelAfterNfeAsync(Guid key, string userName) => CancelAsync(key, userName, afterNfe: true);

    /// <summary>Ensaio antes de falar com a SEFAZ: só as regras de negócio, nada é alterado.</summary>
    public async Task EnsureCanCancelAsync(Guid key)
    {
        var invoice = await db.Context.SalesInvoices.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key)
                      ?? throw new NotFoundException("Documento de saída não encontrado.");

        EnsureBusinessRules(invoice);
    }

    private async Task CancelAsync(Guid key, string userName, bool afterNfe)
    {
        var existingInvoice = await db.Context.SalesInvoices
                                  .Include(e => e.SalesTransactions)
                                  .FirstOrDefaultAsync(x => x.Key == key) ??
                                    throw new KeyNotFoundException($"Key {key} not found");

        if (existingInvoice.InvoiceStatus == InvoiceStatus.Cancelled)
            throw new DefaultException("Documento já está cancelado.");

        if (afterNfe)
        {
            if (existingInvoice.NfeStatus != NfeStatus.Cancelled)
                throw new DefaultException("A NF-e deste documento não está cancelada na SEFAZ.");
        }
        else
        {
            SalesInvoiceNfeLock.EnsureCancellable(existingInvoice);
        }

        EnsureBusinessRules(existingInvoice);

        // … daqui para baixo o corpo de hoje, sem mudança, acrescentando junto de
        // `existingInvoice.InvoiceStatus = InvoiceStatus.Cancelled;`:
        //     existingInvoice.CanceledAt = DateTime.Now;
        //     existingInvoice.CanceledBy = userName;
    }

    private void EnsureBusinessRules(SalesInvoice invoice)
    {
        if (invoice.InvoiceStatus == InvoiceStatus.Cancelled)
            throw new DefaultException("Documento já está cancelado.");

        // A devolução com NF-e confirmada só sai pelo cancelamento da NF-e — que é justamente este caminho.
        if (invoice.InvoiceType == SalesInvoiceType.Return && invoice.InvoiceStatus == InvoiceStatus.Confirmed
            && !invoice.IsNfeReturn)
            throw new DefaultException("Documento do tipo retorno já está confirmado. Não é possivel cancelar.");

        if (HasReturn(invoice))
            throw new DefaultException("Documento de saída possui retorno.");
    }
```
⚠️ A devolução **comum** (não `IsNfeReturn`) confirmada continua barrada. A devolução com NF-e **sem** NF-e autorizada (Pendente) já passava; a confirmada só chega aqui com a NF-e cancelada, porque o caminho comum é barrado antes pela trava da NF-e (`Authorized`).
(`using SiagroB1.Domain.Exceptions;`.) O `catch` de hoje continua convertendo erro dos efeitos em `ApplicationException`.

- [ ] **Step 4: `PurchaseInvoicesCancelService`**:

```csharp
public class PurchaseInvoicesCancelService(IUnitOfWork db)
{
    public Task ExecuteAsync(Guid key, string userName) => CancelAsync(key, userName, afterNfe: false);

    /// <summary>Fase 2 do cancelamento da NF-e: a SEFAZ já cancelou; a trava da NF-e não se aplica.</summary>
    public virtual Task CancelAfterNfeAsync(Guid key, string userName) => CancelAsync(key, userName, afterNfe: true);

    /// <summary>Ensaio antes de falar com a SEFAZ: só as regras de negócio, nada é alterado.</summary>
    public async Task EnsureCanCancelAsync(Guid key)
    {
        var invoice = await db.Context.PurchaseInvoices.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key)
                      ?? throw new NotFoundException("Documento de entrada não encontrado.");

        await EnsureBusinessRulesAsync(invoice.Key, invoice.InvoiceStatus);
    }

    private async Task CancelAsync(Guid key, string userName, bool afterNfe)
    {
        var invoice = await db.Context.PurchaseInvoices.FirstOrDefaultAsync(x => x.Key == key)
                      ?? throw new NotFoundException("Documento de entrada não encontrado.");

        if (afterNfe)
        {
            if (invoice.NfeStatus != NfeStatus.Cancelled)
                throw new DefaultException("A NF-e deste documento não está cancelada na SEFAZ.");
        }
        else
        {
            PurchaseInvoiceNfeLock.EnsureCancellable(invoice);
        }

        await EnsureBusinessRulesAsync(invoice.Key, invoice.InvoiceStatus);

        invoice.InvoiceStatus = InvoiceStatus.Cancelled;
        invoice.CanceledAt = DateTime.Now;
        invoice.CanceledBy = userName;

        await db.SaveChangesAsync();
    }

    private async Task EnsureBusinessRulesAsync(Guid key, InvoiceStatus status)
    {
        if (status == InvoiceStatus.Cancelled)
            throw new DefaultException("Documento de entrada já está cancelado.");

        // A devolução referencia a chave desta nota (refNFe): cancelar a origem a deixaria apontando
        // para uma NF-e cancelada. Mesma regra que a saída já tem.
        if (await db.Context.PurchaseInvoices.AnyAsync(x =>
                x.PurchaseInvoiceOriginKey == key && x.IsNfeReturn && x.InvoiceStatus != InvoiceStatus.Cancelled))
            throw new DefaultException("Documento de entrada possui devolução.");
    }
}
```
⚠️ Ordem: hoje "já cancelado" vem ANTES da trava da NF-e. Mantenha: no `CancelAsync`, teste `invoice.InvoiceStatus == InvoiceStatus.Cancelled` primeiro (a checagem de negócio repete — inofensivo). Atualize o doc comment da classe: a chave da NF-e de **terceiro** volta a ficar livre; a da NF-e própria nunca é reaproveitada (número da reserva).

- [ ] **Step 5: Rodar e ver passar** — o filtro do Step 2 e a suíte inteira.

- [ ] **Step 6: Commit** — `refactor(invoice): cancelamento de saída e entrada separa checagem e efeitos` (corpo: entrada ganha a trava de devolução ativa; saída grava quem cancelou).

---

### Task 5: Cancelamento da NF-e — handler (duas fases), serviço e "Concluir cancelamento"

**Files:**
- Create: `SiagroB1.Application/Services/Nfe/NfeCancellationHandlerBase.cs`
- Create: `SiagroB1.Application/Services/Nfe/SalesInvoiceNfeCancellationHandler.cs`
- Create: `SiagroB1.Application/Services/Nfe/PurchaseInvoiceNfeCancellationHandler.cs`
- Create: `SiagroB1.Application/Services/Nfe/NfeCancelServiceBase.cs`
- Create: `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeCancelService.cs`
- Create: `SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeCancelService.cs`
- Create: `SiagroB1.Application/Services/Nfe/NfeCompleteCancellationServiceBase.cs`
- Create: `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeCompleteCancellationService.cs`
- Create: `SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeCompleteCancellationService.cs`
- Create: `SiagroB1.Application.Tests/Support/NfeCancelTestServices.cs`
- Create: `SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeCancelServiceTests.cs`
- Create: `SiagroB1.Application.Tests/Nfe/PurchaseInvoicesNfeCancelServiceTests.cs`

**Interfaces:**
- Consumes: Task 1 (`NfeCancelRequest`, `NfeEventResult`, `CancelAsync`, `NfeStatusCodes.*`, fake), Task 2 (campos), Task 4 (`EnsureCanCancelAsync`, `CancelAfterNfeAsync`).
- Produces:
  - `abstract class NfeCancellationHandlerBase<TDocument>(IUnitOfWork db, INfeDocumentStore<TDocument> store, ILogger logger)`: `const string UnknownReason = "Cancelada fora do Siagro"`; `Task<NfeIssueOutcomeDto> ApplyRegisteredAsync(TDocument document, NfeEventResult result, string? justification, string userName)`; `Task<NfeIssueOutcomeDto> CompleteLocalAsync(Guid key, string userName)`; `abstract Task EnsureCanCancelAsync(Guid key)`; `protected abstract Task CancelDocumentAsync(Guid key, string userName)`.
  - `SalesInvoiceNfeCancellationHandler(IUnitOfWork db, SalesInvoicesCancelService cancel, ILogger<SalesInvoiceNfeCancellationHandler> logger)`; `PurchaseInvoiceNfeCancellationHandler(IUnitOfWork db, PurchaseInvoicesCancelService cancel, ILogger<PurchaseInvoiceNfeCancellationHandler> logger)`.
  - `abstract class NfeCancelServiceBase<TDocument>(INfeDocumentStore<TDocument> store, BranchNfeSettingsService settingsService, INfeSefazClient sefaz, NfeCancellationHandlerBase<TDocument> handler, NfeNumberReservationService reservation)`: `Task<NfeIssueOutcomeDto> ExecuteAsync(Guid key, string? justification, string userName)`; `protected virtual void EnsureIssuer(TDocument document) { }`.
  - `SalesInvoicesNfeCancelService(IUnitOfWork db, BranchNfeSettingsService settingsService, INfeSefazClient sefaz, SalesInvoiceNfeCancellationHandler handler, NfeNumberReservationService reservation)`; `PurchaseInvoicesNfeCancelService(... PurchaseInvoiceNfeCancellationHandler handler ...)`.
  - `abstract class NfeCompleteCancellationServiceBase<TDocument>(INfeDocumentStore<TDocument> store, NfeCancellationHandlerBase<TDocument> handler, NfeNumberReservationService reservation)`: `Task<NfeIssueOutcomeDto> ExecuteAsync(Guid key, string userName)`; `SalesInvoicesNfeCompleteCancellationService(IUnitOfWork db, SalesInvoiceNfeCancellationHandler handler, NfeNumberReservationService reservation)`; idem `Purchase…`.
  - Test support `NfeCancelTestServices`: `const string AccessKey`, `const string AuthorizationProtocol`, `Task AuthorizeSaleAsync(UnitOfWork db, Guid key, InvoiceStatus status = InvoiceStatus.Confirmed)`, `Task AuthorizePurchaseAsync(UnitOfWork db, Guid key)`, `SalesInvoicesNfeCancelService SalesCancel(UnitOfWork db, FakeNfeSefazClient sefaz, SalesInvoicesCancelService? cancel = null, FakeNfeNumberReservationService? reservation = null)`, `SalesInvoicesNfeCompleteCancellationService SalesComplete(UnitOfWork db, SalesInvoicesCancelService? cancel = null)`, `PurchaseInvoicesNfeCancelService PurchaseCancel(UnitOfWork db, FakeNfeSefazClient sefaz)`, `SalesInvoiceNfeCancellationHandler SalesHandler(UnitOfWork db, SalesInvoicesCancelService? cancel = null)`, `PurchaseInvoiceNfeCancellationHandler PurchaseHandler(UnitOfWork db)`.

- [ ] **Step 1: Test support** — `NfeCancelTestServices.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.SalesInvoices;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Monta o cancelamento da NF-e sobre os seeds da emissão, com a SEFAZ simulada.</summary>
public static class NfeCancelTestServices
{
    /// <summary>Chave da filial semeada (CNPJ 12345678000195 nas posições 7–20).</summary>
    public const string AccessKey = "35261012345678000195550010000000011481516230";

    public const string AuthorizationProtocol = "135260000000001";

    public static async Task AuthorizeSaleAsync(UnitOfWork db, Guid key, InvoiceStatus status = InvoiceStatus.Confirmed)
    {
        var invoice = await db.Context.SalesInvoices.SingleAsync(x => x.Key == key);
        invoice.InvoiceStatus = status;
        invoice.NfeStatus = NfeStatus.Authorized;
        invoice.NfeEnvironment = NfeEnvironment.Homologation;
        invoice.ChaveNFe = AccessKey;
        invoice.NfeProtocol = AuthorizationProtocol;
        invoice.NfeStatusCode = "100";
        invoice.NfeStatusReason = "Autorizado o uso da NF-e";
        await db.SaveChangesAsync();
    }

    public static async Task AuthorizePurchaseAsync(UnitOfWork db, Guid key)
    {
        var invoice = await db.Context.PurchaseInvoices.SingleAsync(x => x.Key == key);
        invoice.InvoiceStatus = InvoiceStatus.Confirmed;
        invoice.IssuerType = DocumentIssuerType.Own;
        invoice.NfeStatus = NfeStatus.Authorized;
        invoice.NfeEnvironment = NfeEnvironment.Homologation;
        invoice.ChaveNFe = AccessKey;
        invoice.NfeProtocol = AuthorizationProtocol;
        await db.SaveChangesAsync();
    }

    private static BranchNfeSettingsService Settings(UnitOfWork db, FakeNfeSefazClient sefaz) =>
        new(db, new NfeOptions(NfeTestSeed.Config()), sefaz);

    public static SalesInvoiceNfeCancellationHandler SalesHandler(UnitOfWork db, SalesInvoicesCancelService? cancel = null) =>
        new(db, cancel ?? SalesInvoicesCancelServiceTests.Service(db), NullLogger<SalesInvoiceNfeCancellationHandler>.Instance);

    public static PurchaseInvoiceNfeCancellationHandler PurchaseHandler(UnitOfWork db) =>
        new(db, new PurchaseInvoicesCancelService(db), NullLogger<PurchaseInvoiceNfeCancellationHandler>.Instance);

    public static SalesInvoicesNfeCancelService SalesCancel(
        UnitOfWork db, FakeNfeSefazClient sefaz, SalesInvoicesCancelService? cancel = null,
        FakeNfeNumberReservationService? reservation = null) =>
        new(db, Settings(db, sefaz), sefaz, SalesHandler(db, cancel), reservation ?? new FakeNfeNumberReservationService());

    public static SalesInvoicesNfeCompleteCancellationService SalesComplete(UnitOfWork db, SalesInvoicesCancelService? cancel = null) =>
        new(db, SalesHandler(db, cancel), new FakeNfeNumberReservationService());

    public static PurchaseInvoicesNfeCancelService PurchaseCancel(UnitOfWork db, FakeNfeSefazClient sefaz) =>
        new(db, Settings(db, sefaz), sefaz, PurchaseHandler(db), new FakeNfeNumberReservationService());
}
```
(Confira o construtor de `BranchNfeSettingsService` usado em `SalesInvoicesNfeConsultServiceTests.Services` e o de `FakeNfeNumberReservationService`; ajuste se diferirem.)

- [ ] **Step 2: Testes da saída** — `SalesInvoicesNfeCancelServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Support.NfeCancelTestServices;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>Cancelamento da NF-e do documento de saída (spec 2026-10-05 §7.2–§7.5).</summary>
public class SalesInvoicesNfeCancelServiceTests
{
    private const string Reason = "Venda desfeita a pedido do cliente";

    /// <summary>Cancelamento que estoura DEPOIS de mexer no documento — prova que a falha não grava pela metade.</summary>
    private sealed class FailingCancel(UnitOfWork db) : SalesInvoicesCancelService(db,
        new SalesShipmentReleasesRecalculateShippedService(db.Context), new SalesContractsAllocationDeleteForInvoiceService(db),
        new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
        NullLogger<SalesInvoicesCancelService>.Instance)
    {
        public override async Task CancelAfterNfeAsync(Guid key, string userName)
        {
            (await db.Context.SalesInvoices.SingleAsync(x => x.Key == key)).InvoiceStatus = InvoiceStatus.Cancelled;
            throw new ApplicationException("Liberação travada por outro usuário.");
        }
    }

    private static async Task<(NfeScenario Scenario, FakeNfeSefazClient Sefaz)> AuthorizedAsync()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await AuthorizeSaleAsync(scenario.Db, scenario.InvoiceKey);
        return (scenario, new FakeNfeSefazClient());
    }

    [Fact]
    public async Task Registered_cancellation_cancels_the_nfe_and_the_document()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));

        var outcome = await SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        Assert.Equal(NfeStatus.Cancelled, outcome.NfeStatus);
        Assert.Equal(InvoiceStatus.Cancelled, outcome.InvoiceStatus);
        var saved = await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(FakeNfeSefazClient.CancellationProtocol, saved.NfeCancellationProtocol);
        Assert.Equal(FakeNfeSefazClient.CancelledAt.DateTime, saved.NfeCancelledAt);
        Assert.Equal(Reason, saved.NfeCancellationReason);
        Assert.Equal("135", saved.NfeStatusCode);
        Assert.Null(saved.NfeCancellationError);
        Assert.Equal("tester", saved.CanceledBy);
        Assert.Equal(AccessKey, saved.ChaveNFe);
        Assert.Equal(AuthorizationProtocol, saved.NfeProtocol);
        var xml = await scenario.Db.Context.SalesInvoiceNfeXmls.AsNoTracking().SingleAsync(x => x.Kind == NfeXmlKind.CancellationEvent);
        Assert.Contains("<procEventoNFe", xml.Xml);
    }

    [Fact]
    public async Task Request_carries_key_protocol_issuer_and_brasilia_time()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));

        await SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        var request = Assert.Single(sefaz.CancelRequests);
        Assert.Equal(AccessKey, request.AccessKey);
        Assert.Equal(AuthorizationProtocol, request.AuthorizationProtocol);
        Assert.Equal("12345678000195", request.IssuerDocument);
        Assert.Equal(Reason, request.Justification);
        Assert.Equal(TimeSpan.FromHours(-3), request.EventAt.Offset);
        Assert.Equal(NfeEnvironment.Homologation, Assert.Single(sefaz.CancelSettings).Environment);
    }

    [Fact]
    public async Task Out_of_time_registration_155_also_cancels()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey, 155));

        var outcome = await SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        Assert.Equal(NfeStatus.Cancelled, outcome.NfeStatus);
    }

    [Theory]
    [InlineData("curta demais")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Short_justification_is_refused_without_calling_sefaz(string? justification)
    {
        var (scenario, sefaz) = await AuthorizedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, justification, "tester"));

        Assert.Equal("A justificativa deve ter entre 15 e 255 caracteres.", ex.Message);
        Assert.Empty(sefaz.CancelRequests);
    }

    [Fact]
    public async Task Long_justification_is_refused()
    {
        var (scenario, sefaz) = await AuthorizedAsync();

        await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, new string('x', 256), "tester"));

        Assert.Empty(sefaz.CancelRequests);
    }

    [Fact]
    public async Task Justification_is_trimmed_before_counting_and_saving()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));

        await SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, $"   {Reason}  ", "tester");

        Assert.Equal(Reason, Assert.Single(sefaz.CancelRequests).Justification);
        Assert.Equal(Reason, (await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync()).NfeCancellationReason);

        var (other, sefaz2) = await AuthorizedAsync();
        await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCancel(other.Db, sefaz2).ExecuteAsync(other.InvoiceKey, "   curta     ", "tester"));
    }

    [Theory]
    [InlineData(NfeStatus.None, "Só NF-e autorizada pode ser cancelada.")]
    [InlineData(NfeStatus.Processing, "Só NF-e autorizada pode ser cancelada.")]
    [InlineData(NfeStatus.Rejected, "Só NF-e autorizada pode ser cancelada.")]
    [InlineData(NfeStatus.Cancelled, "A NF-e deste documento já foi cancelada: use Concluir cancelamento.")]
    public async Task Only_authorized_nfe_is_cancelled(NfeStatus status, string message)
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        (await scenario.Db.Context.SalesInvoices.SingleAsync()).NfeStatus = status;
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal(message, ex.Message);
        Assert.Empty(sefaz.CancelRequests);
    }

    [Fact]
    public async Task Local_rule_refuses_before_calling_sefaz()
    {
        var returnScenario = await NfeReturnTestSeed.SeedAsync();
        await NfeReturnTestSeed.CreateReturnAsync(returnScenario, 10m);
        var sefaz = new FakeNfeSefazClient();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCancel(returnScenario.Sale.Db, sefaz).ExecuteAsync(returnScenario.Sale.InvoiceKey, Reason, "tester"));

        Assert.Equal("Documento de saída possui retorno.", ex.Message);
        Assert.Empty(sefaz.CancelRequests);
    }

    [Fact]
    public async Task Sefaz_rejection_changes_nothing()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(_ => new NfeEventResult(501, "Rejeição: Prazo de cancelamento superior ao previsto na Legislação"));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal("Cancelamento recusado pela SEFAZ: 501 - Rejeição: Prazo de cancelamento superior ao previsto na Legislação", ex.Message);
        var saved = await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(NfeStatus.Authorized, saved.NfeStatus);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        Assert.Equal("100", saved.NfeStatusCode);
    }

    [Fact]
    public async Task Communication_failure_changes_nothing()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(_ => throw new NfeCommunicationException("Tempo esgotado."));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal("Sem resposta da SEFAZ no cancelamento: use Consultar situação antes de tentar de novo.", ex.Message);
        Assert.Equal(NfeStatus.Authorized, (await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync()).NfeStatus);
    }

    [Fact]
    public async Task Duplicate_event_is_resolved_by_the_consult()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(_ => new NfeEventResult(573, "Rejeição: Duplicidade de evento"));
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.ConsultCancelled(key));

        var outcome = await SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        Assert.Equal(NfeStatus.Cancelled, outcome.NfeStatus);
        Assert.Equal(InvoiceStatus.Cancelled, outcome.InvoiceStatus);
        var saved = await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal("Cancelada direto no portal da SEFAZ", saved.NfeCancellationReason);
    }

    [Fact]
    public async Task Duplicate_event_not_confirmed_by_the_consult_changes_nothing()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(_ => new NfeEventResult(573, "Rejeição: Duplicidade de evento"));
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal(NfeStatus.Authorized, (await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync()).NfeStatus);
    }

    [Fact]
    public async Task Local_failure_after_sefaz_keeps_the_nfe_cancelled_and_records_the_error()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));

        var outcome = await SalesCancel(scenario.Db, sefaz, new FailingCancel(scenario.Db))
            .ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        Assert.Equal(NfeStatus.Cancelled, outcome.NfeStatus);
        Assert.Equal("Liberação travada por outro usuário.", outcome.CancellationError);
        var saved = await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(NfeStatus.Cancelled, saved.NfeStatus);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        Assert.Equal("Liberação travada por outro usuário.", saved.NfeCancellationError);
        Assert.Equal(FakeNfeSefazClient.CancellationProtocol, saved.NfeCancellationProtocol);
    }

    [Fact]
    public async Task Complete_cancellation_runs_only_the_local_phase()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));
        await SalesCancel(scenario.Db, sefaz, new FailingCancel(scenario.Db)).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");
        scenario.Db.Context.ChangeTracker.Clear();

        var outcome = await SalesComplete(scenario.Db).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(InvoiceStatus.Cancelled, outcome.InvoiceStatus);
        Assert.Null(outcome.CancellationError);
        Assert.Single(sefaz.CancelRequests);
        Assert.Null((await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync()).NfeCancellationError);
    }

    [Fact]
    public async Task Complete_cancellation_requires_cancelled_nfe_and_active_document()
    {
        var (scenario, _) = await AuthorizedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => SalesComplete(scenario.Db).ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal("Só documento com NF-e cancelada e ainda ativo tem cancelamento a concluir.", ex.Message);
    }

    [Fact]
    public async Task Cancel_while_an_emission_lock_is_held_is_refused_without_calling_sefaz()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        var reservation = new FakeNfeNumberReservationService { EmissionBusy = true };

        await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCancel(scenario.Db, sefaz, reservation: reservation).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Empty(sefaz.CancelRequests);
    }

    [Fact]
    public async Task Cancelling_an_authorized_sales_return_restores_the_origin()
    {
        var returnScenario = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(returnScenario, 10m);
        var db = returnScenario.Sale.Db;
        var origin = await db.Context.SalesInvoices.SingleAsync(x => x.Key == returnScenario.Sale.InvoiceKey);
        origin.InvoiceStatus = InvoiceStatus.Returned;
        await db.SaveChangesAsync();
        await AuthorizeSaleAsync(db, created.Key);
        var sefaz = new FakeNfeSefazClient();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));

        var outcome = await SalesCancel(db, sefaz).ExecuteAsync(created.Key, Reason, "tester");

        Assert.Equal(InvoiceStatus.Cancelled, outcome.InvoiceStatus);
        Assert.Equal(InvoiceStatus.Confirmed,
            (await db.Context.SalesInvoices.AsNoTracking().SingleAsync(x => x.Key == returnScenario.Sale.InvoiceKey)).InvoiceStatus);
    }

    [Fact]
    public async Task Pending_document_with_authorized_nfe_is_cancelled_too()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await AuthorizeSaleAsync(scenario.Db, scenario.InvoiceKey, InvoiceStatus.Pending);
        var sefaz = new FakeNfeSefazClient();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));

        var outcome = await SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        Assert.Equal(InvoiceStatus.Cancelled, outcome.InvoiceStatus);
    }
}
```
(A origem do `NfeReturnTestSeed` já tem a chave `SaleAccessKey`; `AuthorizeSaleAsync` sobrescreve a chave da devolução — ok, são documentos diferentes. `FakeNfeNumberReservationService.EmissionBusy` já existe: confira a mensagem que ele lança.)

- [ ] **Step 3: Testes da entrada** — `PurchaseInvoicesNfeCancelServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Support.NfeCancelTestServices;

namespace SiagroB1.Application.Tests.Nfe;

public class PurchaseInvoicesNfeCancelServiceTests
{
    private const string Reason = "Entrada lancada em duplicidade";

    [Fact]
    public async Task Own_entry_with_authorized_nfe_is_cancelled()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await AuthorizePurchaseAsync(scenario.Db, scenario.InvoiceKey);
        var sefaz = new FakeNfeSefazClient();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));

        var outcome = await PurchaseCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        Assert.Equal(NfeStatus.Cancelled, outcome.NfeStatus);
        Assert.Equal(InvoiceStatus.Cancelled, outcome.InvoiceStatus);
        var saved = await scenario.Db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(x => x.Key == scenario.InvoiceKey);
        Assert.Equal(Reason, saved.NfeCancellationReason);
        Assert.Equal("tester", saved.CanceledBy);
        Assert.True(await scenario.Db.Context.PurchaseInvoiceNfeXmls.AnyAsync(x =>
            x.PurchaseInvoiceKey == scenario.InvoiceKey && x.Kind == NfeXmlKind.CancellationEvent));
    }

    [Fact]
    public async Task Third_party_entry_is_not_cancelled_at_sefaz()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await AuthorizePurchaseAsync(scenario.Db, scenario.InvoiceKey);
        (await scenario.Db.Context.PurchaseInvoices.SingleAsync(x => x.Key == scenario.InvoiceKey)).IssuerType = DocumentIssuerType.ThirdParty;
        await scenario.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal("NF-e de terceiro não é cancelada pelo Siagro: use o Cancelar do documento.", ex.Message);
        Assert.Empty(sefaz.CancelRequests);
    }

    [Fact]
    public async Task Entry_with_an_active_return_is_refused_before_calling_sefaz()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await AuthorizePurchaseAsync(scenario.Db, scenario.InvoiceKey);
        var origin = await scenario.Db.Context.PurchaseInvoices.SingleAsync(x => x.Key == scenario.InvoiceKey);
        scenario.Db.Context.PurchaseInvoices.Add(new Domain.Entities.PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = origin.BranchCode, CardCode = origin.CardCode, InvoiceType = PurchaseInvoiceType.Return,
            IssuerType = DocumentIssuerType.Own, IsNfeReturn = true, InvoiceStatus = InvoiceStatus.Pending,
            PurchaseInvoiceOriginKey = origin.Key,
        });
        await scenario.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal("Documento de entrada possui devolução.", ex.Message);
        Assert.Empty(sefaz.CancelRequests);
    }
}
```

- [ ] **Step 4: Rodar e ver falhar** — `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeCancelServiceTests"` → erro de compilação.

- [ ] **Step 5: `NfeCancellationHandlerBase.cs`**:

```csharp
using System.Globalization;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Aplica o cancelamento registrado na SEFAZ (spec 2026-10-05 §7.3), vindo do envio do evento ou da consulta.
/// Fase 1: grava a NF-e cancelada e o procEventoNFe e SALVA. Fase 2: cancela o documento com os estornos
/// do cancelamento comum; se falhar, a NF-e (já gravada) é a verdade e o erro vai para
/// <c>NfeCancellationError</c> — "Concluir cancelamento" refaz só a fase 2.
/// </summary>
public abstract class NfeCancellationHandlerBase<TDocument>(IUnitOfWork db, INfeDocumentStore<TDocument> store, ILogger logger)
    where TDocument : class, INfeDocument
{
    /// <summary>Justificativa gravada quando a nota foi cancelada fora do Siagro e a consulta não trouxe o evento.</summary>
    public const string UnknownReason = "Cancelada fora do Siagro";

    public async Task<NfeIssueOutcomeDto> ApplyRegisteredAsync(
        TDocument document, NfeEventResult result, string? justification, string userName)
    {
        if (result.ProcEventXml is not null)
            store.AddXml(document, NfeXmlKind.CancellationEvent, result.ProcEventXml);

        document.NfeStatus = NfeStatus.Cancelled;
        document.NfeCancellationProtocol = result.Protocol;
        document.NfeCancelledAt = result.RegisteredAt?.DateTime;
        document.NfeCancellationReason = justification ?? result.Justification ?? UnknownReason;
        document.NfeStatusCode = result.StatusCode.ToString(CultureInfo.InvariantCulture);
        document.NfeStatusReason = NfeStatusText.Truncate(result.Reason);
        document.NfeCancellationError = null;

        // A NF-e cancelada vai para o banco ANTES do cancelamento do documento.
        await db.SaveChangesAsync();

        return await CompleteLocalAsync(document.Key, userName);
    }

    public async Task<NfeIssueOutcomeDto> CompleteLocalAsync(Guid key, string userName)
    {
        try
        {
            await CancelDocumentAsync(key, userName);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Falha ao cancelar o documento {InvoiceKey} depois da NF-e cancelada.", key);

            // ⚠️ Mesmo cuidado do ConfirmAsync: o rollback não desfaz o rastreador. Descarta tudo e
            // grava só o erro, relendo o documento.
            db.Context.ChangeTracker.Clear();

            var failed = await store.FindAsync(key) ?? throw new NotFoundException(store.NotFoundMessage);
            failed.NfeCancellationError = NfeStatusText.Truncate(e.Message);
            await db.SaveChangesAsync();

            return NfeIssueOutcomeDto.From(failed);
        }

        var cancelled = await store.FindAsync(key) ?? throw new NotFoundException(store.NotFoundMessage);
        if (cancelled.NfeCancellationError is not null)
        {
            cancelled.NfeCancellationError = null;
            await db.SaveChangesAsync();
        }

        return NfeIssueOutcomeDto.From(cancelled);
    }

    /// <summary>Ensaio das regras de negócio do cancelamento, antes de falar com a SEFAZ.</summary>
    public abstract Task EnsureCanCancelAsync(Guid key);

    /// <summary>O cancelamento do documento (estornos) — o de cada tipo de documento.</summary>
    protected abstract Task CancelDocumentAsync(Guid key, string userName);
}
```

`SalesInvoiceNfeCancellationHandler.cs`:

```csharp
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Cancelamento registrado no documento de saída; estorna pelo <see cref="SalesInvoicesCancelService"/>.</summary>
public class SalesInvoiceNfeCancellationHandler(
    IUnitOfWork db, SalesInvoicesCancelService cancel, ILogger<SalesInvoiceNfeCancellationHandler> logger)
    : NfeCancellationHandlerBase<SalesInvoice>(db, new SalesInvoiceNfeStore(db), logger)
{
    public override Task EnsureCanCancelAsync(Guid key) => cancel.EnsureCanCancelAsync(key);

    protected override Task CancelDocumentAsync(Guid key, string userName) => cancel.CancelAfterNfeAsync(key, userName);
}
```
`PurchaseInvoiceNfeCancellationHandler.cs`: idem com `PurchaseInvoice`, `PurchaseInvoiceNfeStore`, `PurchaseInvoicesCancelService` (using `SiagroB1.Application.Services.PurchaseInvoices`).

- [ ] **Step 6: `NfeCancelServiceBase.cs`**:

```csharp
using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// "Cancelar NF-e" (spec 2026-10-05 §7.2), comum aos documentos: valida, faz o ensaio das regras locais,
/// envia o evento 110111 no ambiente da EMISSÃO e entrega o registro ao handler (fase 1 + fase 2).
/// Recusa da SEFAZ ou falta de resposta não mudam o documento.
/// </summary>
public abstract class NfeCancelServiceBase<TDocument>(
    INfeDocumentStore<TDocument> store,
    BranchNfeSettingsService settingsService,
    INfeSefazClient sefaz,
    NfeCancellationHandlerBase<TDocument> handler,
    NfeNumberReservationService reservation)
    where TDocument : class, INfeDocument
{
    public const int MinJustificationLength = 15;
    public const int MaxJustificationLength = 255;

    public async Task<NfeIssueOutcomeDto> ExecuteAsync(Guid key, string? justification, string userName)
    {
        var reason = (justification ?? string.Empty).Trim();
        if (reason.Length is < MinJustificationLength or > MaxJustificationLength)
            throw new DefaultException("A justificativa deve ter entre 15 e 255 caracteres.");

        // A mesma trava da emissão e da consulta.
        await using var emissionLock = await reservation.AcquireEmissionLockAsync(key);

        var invoice = await store.FindAsync(key) ?? throw new NotFoundException(store.NotFoundMessage);

        EnsureIssuer(invoice);

        if (invoice.InvoiceStatus == InvoiceStatus.Cancelled)
            throw new DefaultException("Documento já está cancelado.");

        if (invoice.NfeStatus == NfeStatus.Cancelled)
            throw new DefaultException("A NF-e deste documento já foi cancelada: use Concluir cancelamento.");

        if (invoice.NfeStatus != NfeStatus.Authorized)
            throw new DefaultException("Só NF-e autorizada pode ser cancelada.");

        if (invoice.ChaveNFe is not { Length: 44 } accessKey || string.IsNullOrWhiteSpace(invoice.NfeProtocol))
            throw new DefaultException("A NF-e deste documento está sem chave ou protocolo de autorização.");

        // Ensaio: uma regra local que recusaria o cancelamento recusa ANTES do evento.
        try
        {
            await handler.EnsureCanCancelAsync(key);
        }
        catch (ApplicationException e)
        {
            throw new DefaultException(e.Message);
        }

        using var service = await settingsService.OpenAsync(invoice.BranchCode!, invoice.NfeEnvironment);

        var request = new NfeCancelRequest(
            accessKey, invoice.NfeProtocol, reason, accessKey.Substring(6, 14),
            TimeZoneInfo.ConvertTime(DateTimeOffset.Now, NfeIssueInputAssembler.BrasiliaZone));

        NfeEventResult result;
        try
        {
            result = await sefaz.CancelAsync(request, service.Settings);
        }
        catch (NfeCommunicationException)
        {
            throw new DefaultException("Sem resposta da SEFAZ no cancelamento: use Consultar situação antes de tentar de novo.");
        }

        if (NfeStatusCodes.IsCancellationRegistered(result.StatusCode))
            return await handler.ApplyRegisteredAsync(invoice, result, reason, userName);

        if (result.StatusCode == NfeStatusCodes.DuplicateEvent)
        {
            // O evento já está na SEFAZ (envio anterior sem resposta): a consulta traz o registrado.
            var consult = await sefaz.ConsultProtocolAsync(accessKey, service.Settings);

            if (consult.StatusCode == NfeStatusCodes.Cancelled)
                return await handler.ApplyRegisteredAsync(
                    invoice, consult.CancellationEvent ?? new NfeEventResult(consult.StatusCode, consult.Reason), null, userName);

            throw new DefaultException(
                $"A SEFAZ informou evento duplicado, mas a consulta não confirmou o cancelamento: {consult.StatusCode} - {consult.Reason}");
        }

        throw new DefaultException($"Cancelamento recusado pela SEFAZ: {result.StatusCode} - {result.Reason}");
    }

    /// <summary>Quem emitiu a nota: a entrada de terceiro não é cancelada pelo Siagro.</summary>
    protected virtual void EnsureIssuer(TDocument document) { }
}
```
⚠️ No 573 o `ApplyRegisteredAsync` recebe `justification: null` de propósito: vale o `xJust` que a SEFAZ registrou (o teste `Duplicate_event_is_resolved_by_the_consult` espera o texto do evento). Confira o nome de `NfeIssueInputAssembler.BrasiliaZone` (usado em `NfeIssueServiceBase`).

`SalesInvoicesNfeCancelService.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>"Cancelar NF-e" do documento de saída (venda e devolução própria).</summary>
public class SalesInvoicesNfeCancelService(
    IUnitOfWork db,
    BranchNfeSettingsService settingsService,
    INfeSefazClient sefaz,
    SalesInvoiceNfeCancellationHandler handler,
    NfeNumberReservationService reservation)
    : NfeCancelServiceBase<SalesInvoice>(new SalesInvoiceNfeStore(db), settingsService, sefaz, handler, reservation);
```

`PurchaseInvoicesNfeCancelService.cs`: idem com `PurchaseInvoice`/`PurchaseInvoiceNfeStore`/`PurchaseInvoiceNfeCancellationHandler`, mais:

```csharp
{
    protected override void EnsureIssuer(PurchaseInvoice document)
    {
        if (document.IssuerType != DocumentIssuerType.Own)
            throw new DefaultException("NF-e de terceiro não é cancelada pelo Siagro: use o Cancelar do documento.");
    }
}
```

- [ ] **Step 7: "Concluir cancelamento"** — `NfeCompleteCancellationServiceBase.cs`:

```csharp
using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>"Concluir cancelamento": refaz o cancelamento do documento cuja NF-e já foi cancelada na SEFAZ.</summary>
public abstract class NfeCompleteCancellationServiceBase<TDocument>(
    INfeDocumentStore<TDocument> store, NfeCancellationHandlerBase<TDocument> handler, NfeNumberReservationService reservation)
    where TDocument : class, INfeDocument
{
    public async Task<NfeIssueOutcomeDto> ExecuteAsync(Guid key, string userName)
    {
        await using var emissionLock = await reservation.AcquireEmissionLockAsync(key);

        var invoice = await store.FindReadOnlyAsync(key) ?? throw new NotFoundException(store.NotFoundMessage);

        if (invoice.NfeStatus != NfeStatus.Cancelled || invoice.InvoiceStatus == InvoiceStatus.Cancelled)
            throw new DefaultException("Só documento com NF-e cancelada e ainda ativo tem cancelamento a concluir.");

        return await handler.CompleteLocalAsync(key, userName);
    }
}
```
`SalesInvoicesNfeCompleteCancellationService(IUnitOfWork db, SalesInvoiceNfeCancellationHandler handler, NfeNumberReservationService reservation) : NfeCompleteCancellationServiceBase<SalesInvoice>(new SalesInvoiceNfeStore(db), handler, reservation);` e o equivalente `Purchase…`.

- [ ] **Step 8: Rodar e ver passar** — o filtro do Step 4; depois a suíte inteira. `git add` dos arquivos novos.

- [ ] **Step 9: Commit** — `feat(invoice): cancelamento da NF-e na SEFAZ em duas fases com concluir cancelamento`.

---

### Task 6: Consulta reconhece a NF-e cancelada (cStat 101)

**Files:**
- Modify: `SiagroB1.Application/Services/Nfe/NfeConsultServiceBase.cs`
- Modify: `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeConsultService.cs`, `PurchaseInvoicesNfeConsultService.cs`
- Modify (construtores): `SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeConsultServiceTests.cs:31`, `SiagroB1.Application.Tests/Nfe/PurchaseInvoicesNfeIssueServiceTests.cs:218`
- Test: `SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeConsultServiceTests.cs`

**Interfaces:**
- Consumes: Task 5 (`NfeCancellationHandlerBase<T>.ApplyRegisteredAsync`, handlers), Task 1 (`ConsultCancelled`).
- Produces: `NfeConsultServiceBase<TDocument>(IUnitOfWork db, INfeDocumentStore<TDocument> store, BranchNfeSettingsService settingsService, INfeSefazClient sefaz, NfeResultHandlerBase<TDocument> resultHandler, NfeCancellationHandlerBase<TDocument> cancellationHandler, NfeNumberReservationService reservation, ILogger logger)`; subclasses ganham `SalesInvoiceNfeCancellationHandler cancellationHandler` / `PurchaseInvoiceNfeCancellationHandler cancellationHandler` logo depois do `resultHandler`.

- [ ] **Step 1: Testes** — em `SalesInvoicesNfeConsultServiceTests.cs`, mudar `Services(...)` para passar `NfeCancelTestServices.SalesHandler(scenario.Db)` ao `SalesInvoicesNfeConsultService` (posição logo depois do `handler`), e acrescentar:

```csharp
    [Fact]
    public async Task Consult_of_authorized_nfe_cancelled_at_sefaz_cancels_the_document()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await NfeCancelTestServices.AuthorizeSaleAsync(scenario.Db, scenario.InvoiceKey);
        var sefaz = new FakeNfeSefazClient();
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.ConsultCancelled(key));
        var (_, consult) = Services(scenario, sefaz, new RecordingConfirmService(scenario.Db));

        var outcome = await consult.ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Cancelled, outcome.NfeStatus);
        Assert.Equal(InvoiceStatus.Cancelled, outcome.InvoiceStatus);
        var saved = await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal("Cancelada direto no portal da SEFAZ", saved.NfeCancellationReason);
        Assert.Equal(FakeNfeSefazClient.CancellationProtocol, saved.NfeCancellationProtocol);
    }

    [Fact]
    public async Task Consult_of_cancelled_nfe_without_the_event_records_the_unknown_reason()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await NfeCancelTestServices.AuthorizeSaleAsync(scenario.Db, scenario.InvoiceKey);
        var sefaz = new FakeNfeSefazClient();
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.ConsultCancelled(key, withEvent: false));
        var (_, consult) = Services(scenario, sefaz, new RecordingConfirmService(scenario.Db));

        await consult.ExecuteAsync(scenario.InvoiceKey, "tester");

        var saved = await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(NfeStatus.Cancelled, saved.NfeStatus);
        Assert.Equal(NfeCancellationHandlerBase<SalesInvoice>.UnknownReason, saved.NfeCancellationReason);
        Assert.Null(saved.NfeCancellationProtocol);
        Assert.Equal("101", saved.NfeStatusCode);
    }

    [Fact]
    public async Task Consult_of_authorized_nfe_still_authorized_changes_nothing()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await NfeCancelTestServices.AuthorizeSaleAsync(scenario.Db, scenario.InvoiceKey);
        var sefaz = new FakeNfeSefazClient();
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        var (_, consult) = Services(scenario, sefaz, new RecordingConfirmService(scenario.Db));

        var outcome = await consult.ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        Assert.Equal("100", outcome.StatusCode);
        Assert.False(await scenario.Db.Context.SalesInvoiceNfeXmls.AnyAsync());
    }

    [Fact]
    public async Task Consult_of_authorized_nfe_without_answer_keeps_the_last_return()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await NfeCancelTestServices.AuthorizeSaleAsync(scenario.Db, scenario.InvoiceKey);
        var sefaz = new FakeNfeSefazClient();
        sefaz.ConsultResponses.Enqueue(_ => throw new NfeCommunicationException("Tempo esgotado."));
        var (_, consult) = Services(scenario, sefaz, new RecordingConfirmService(scenario.Db));

        var ex = await Assert.ThrowsAsync<DefaultException>(() => consult.ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal("Sem resposta da SEFAZ na consulta — tente de novo em instantes.", ex.Message);
        var saved = await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal("100", saved.NfeStatusCode);
        Assert.Equal("Autorizado o uso da NF-e", saved.NfeStatusReason);
    }
```
(⚠️ O teste existente `Only_processing_documents_are_consulted` usa `Rejected` e continua esperando "Só a NF-e em processamento é consultada." — **não mude**.) No `PurchaseInvoicesNfeIssueServiceTests.cs:218`, passe `NfeCancelTestServices.PurchaseHandler(scenario.Db)` ao construtor.

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoicesNfeConsultServiceTests"`.

- [ ] **Step 3: Implementar** — em `NfeConsultServiceBase`, novo parâmetro `NfeCancellationHandlerBase<TDocument> cancellationHandler` (depois de `resultHandler`), e no `ExecuteAsync` logo depois do `FindAsync`:

```csharp
        // Autorizada: a consulta só serve para descobrir um cancelamento feito por fora ou cujo
        // envio ficou sem resposta (cStat 101). Nada mais é gravado.
        if (invoice.NfeStatus == NfeStatus.Authorized)
            return await ConsultAuthorizedAsync(invoice, userName);
```
e o método:

```csharp
    private async Task<NfeIssueOutcomeDto> ConsultAuthorizedAsync(TDocument invoice, string userName)
    {
        using var service = await settingsService.OpenAsync(invoice.BranchCode!, invoice.NfeEnvironment);

        NfeSefazResult result;
        try
        {
            result = await sefaz.ConsultProtocolAsync(invoice.ChaveNFe!, service.Settings);
        }
        catch (Exception e)
        {
            // O "último retorno" da autorização não é sobrescrito: a falha volta como recusa.
            logger.LogError(e, "Falha ao consultar a NF-e autorizada do documento {InvoiceKey}.", invoice.Key);
            throw new DefaultException("Sem resposta da SEFAZ na consulta — tente de novo em instantes.");
        }

        if (result.StatusCode == NfeStatusCodes.Cancelled)
            return await cancellationHandler.ApplyRegisteredAsync(
                invoice, result.CancellationEvent ?? new NfeEventResult(result.StatusCode, result.Reason), null, userName);

        var outcome = NfeIssueOutcomeDto.From(invoice);
        outcome.StatusCode = result.StatusCode.ToString(CultureInfo.InvariantCulture);
        outcome.Reason = result.Reason;
        return outcome;
    }
```
(using `System.Globalization`.) Atualizar as duas subclasses e o doc comment ("Consultar situação" vale para processamento e autorizada).

- [ ] **Step 4: Rodar e ver passar** — filtro do Step 2, `PurchaseInvoicesNfeIssueServiceTests`, suíte inteira.

- [ ] **Step 5: Commit** — `feat(invoice): consultar situação reconhece a NF-e cancelada na SEFAZ`.

---

### Task 7: Web (actions, XML do evento, DI) e DANFE com tarja

**Files:**
- Create: `SiagroB1.Web/Actions/Nfe/SalesInvoicesCancelNfeController.cs`, `PurchaseInvoicesCancelNfeController.cs`, `SalesInvoicesCompleteNfeCancellationController.cs`, `PurchaseInvoicesCompleteNfeCancellationController.cs`
- Create: `SiagroB1.Web/Functions/Nfe/SalesInvoicesNfeCancellationXmlController.cs`, `PurchaseInvoicesNfeCancellationXmlController.cs`
- Create: `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeCancellationXmlDownloadService.cs`, `PurchaseInvoicesNfeCancellationXmlDownloadService.cs`
- Modify: `SiagroB1.Application/Services/Nfe/INfeDocumentStore.cs` (+ `SalesInvoiceNfeStore.cs`, `PurchaseInvoiceNfeStore.cs`)
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (junto das actions de NF-e ~linha 93 e das functions ~linha 1422)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (junto da linha 350 e 359)
- Modify: `SiagroB1.Reports/Services/DanfeReportService.cs`
- Test: `SiagroB1.Application.Tests/Nfe/NfeCancellationEdmModelTests.cs` (criar), `SiagroB1.Application.Tests/Nfe/NfeCancellationXmlDownloadTests.cs` (criar), `SiagroB1.Application.Tests/Reports/DanfeReportServiceTests.cs`

**Interfaces:**
- Produces: OData actions `SalesInvoicesCancelNfe(Key: Guid, Justification: String) → NfeIssueOutcomeDto`, `PurchaseInvoicesCancelNfe(...)`, `SalesInvoicesCompleteNfeCancellation(Key) → NfeIssueOutcomeDto`, `PurchaseInvoicesCompleteNfeCancellation(Key)`; functions `SalesInvoicesNfeCancellationXml(Key)`, `PurchaseInvoicesNfeCancellationXml(Key)` (arquivo `{chave}-procEventoNFe.xml`); `INfeDocumentStore.LatestXmlAsync(Guid key, NfeXmlKind kind) : Task<string?>`; `DanfeReportService` lê `NfeStatus` do documento.

- [ ] **Step 1: Testes** — `NfeCancellationEdmModelTests.cs` (mesmo `Model()` de `PurchaseInvoiceNfeEdmModelTests`):

```csharp
    [Theory]
    [InlineData("SalesInvoicesCancelNfe")]
    [InlineData("PurchaseInvoicesCancelNfe")]
    public void Cancel_nfe_takes_key_and_justification(string name)
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == name);

        Assert.Equal("Edm.Guid", action.Parameters.Single(p => p.Name == "Key").Type.FullName());
        Assert.Equal("Edm.String", action.Parameters.Single(p => p.Name == "Justification").Type.FullName());
        Assert.EndsWith("NfeIssueOutcomeDto", action.ReturnType.FullName());
    }

    [Theory]
    [InlineData("SalesInvoicesCompleteNfeCancellation")]
    [InlineData("PurchaseInvoicesCompleteNfeCancellation")]
    public void Complete_cancellation_takes_the_key(string name)
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == name);

        Assert.Equal("Edm.Guid", action.Parameters.Single(p => p.Name == "Key").Type.FullName());
        Assert.EndsWith("NfeIssueOutcomeDto", action.ReturnType.FullName());
    }

    [Theory]
    [InlineData("SalesInvoicesNfeCancellationXml")]
    [InlineData("PurchaseInvoicesNfeCancellationXml")]
    public void Cancellation_xml_is_a_function_with_the_key(string name)
    {
        var function = Model().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == name);

        Assert.Equal("Edm.Guid", function.Parameters.Single(p => p.Name == "Key").Type.FullName());
    }

    [Theory]
    [InlineData("SalesInvoice")]
    [InlineData("PurchaseInvoice")]
    public void Cancellation_fields_are_in_the_entity(string entity)
    {
        var type = Model().SchemaElements.OfType<IEdmEntityType>().Single(t => t.Name == entity);

        foreach (var property in new[] { "NfeCancellationProtocol", "NfeCancelledAt", "NfeCancellationReason", "NfeCancellationError" })
            Assert.NotNull(type.FindProperty(property));
    }
```

`NfeCancellationXmlDownloadTests.cs`:

```csharp
    [Fact]
    public async Task Cancellation_xml_is_downloaded_with_the_event_name()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await NfeCancelTestServices.AuthorizeSaleAsync(scenario.Db, scenario.InvoiceKey);
        var sefaz = new FakeNfeSefazClient();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));
        await NfeCancelTestServices.SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, "Venda desfeita pelo cliente", "tester");

        var (bytes, fileName) = await new SalesInvoicesNfeCancellationXmlDownloadService(scenario.Db).ExecuteAsync(scenario.InvoiceKey);

        Assert.Equal($"{NfeCancelTestServices.AccessKey}-procEventoNFe.xml", fileName);
        Assert.Contains("<procEventoNFe", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task Missing_cancellation_xml_is_not_found()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            new SalesInvoicesNfeCancellationXmlDownloadService(scenario.Db).ExecuteAsync(scenario.InvoiceKey));

        Assert.Equal("Este documento não tem o XML do cancelamento da NF-e.", ex.Message);
    }
```

DANFE: em `DanfeReportServiceTests.cs`, ver como o teste atual gera o PDF a partir de um procNFe gravado; acrescentar um teste `Cancelled_nfe_danfe_is_generated` que grava o mesmo procNFe, marca o documento `NfeStatus = Cancelled` e verifica que `GeneratePdfAsync` devolve PDF (`%PDF`) — a tarja é visual e é conferida no E2E (Task 11). Se o teste existente não semear `SalesInvoices` (só o XML), semeie o documento com a mesma `Key`.

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeCancellationEdmModelTests|FullyQualifiedName~NfeCancellationXmlDownloadTests|FullyQualifiedName~DanfeReportServiceTests"`.

- [ ] **Step 3: Store e download** — em `INfeDocumentStore`:

```csharp
    /// <summary>XML mais recente do tipo pedido; nulo quando não há.</summary>
    Task<string?> LatestXmlAsync(Guid key, NfeXmlKind kind);
```
`SalesInvoiceNfeStore`:

```csharp
    public Task<string?> LatestXmlAsync(Guid key, NfeXmlKind kind) =>
        db.Context.SalesInvoiceNfeXmls.AsNoTracking()
            .Where(x => x.SalesInvoiceKey == key && x.Kind == kind)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.Xml)
            .FirstOrDefaultAsync();
```
(idem `PurchaseInvoiceNfeStore` com `PurchaseInvoiceNfeXmls`/`PurchaseInvoiceKey`).

`SalesInvoicesNfeCancellationXmlDownloadService.cs`:

```csharp
using System.Text;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>XML do evento de cancelamento (procEventoNFe) do documento de saída: <c>&lt;chave&gt;-procEventoNFe.xml</c>.</summary>
public class SalesInvoicesNfeCancellationXmlDownloadService(IUnitOfWork db)
{
    public async Task<(byte[] Bytes, string FileName)> ExecuteAsync(Guid invoiceKey)
    {
        var store = new SalesInvoiceNfeStore(db);
        var xml = await store.LatestXmlAsync(invoiceKey, NfeXmlKind.CancellationEvent)
                  ?? throw new NotFoundException("Este documento não tem o XML do cancelamento da NF-e.");
        var invoice = await store.FindReadOnlyAsync(invoiceKey);

        return (Encoding.UTF8.GetBytes(xml), $"{invoice?.ChaveNFe}-procEventoNFe.xml");
    }
}
```
(idem `PurchaseInvoicesNfeCancellationXmlDownloadService` com `PurchaseInvoiceNfeStore`.)

- [ ] **Step 4: OData** — em `ODataConfigurations.cs`, depois de `purchaseInvoicesCompleteNfeConfirmation`:

```csharp
        // Cancelamento da NF-e (spec 2026-10-05): evento 110111 com justificativa + conclusão da fase local.
        foreach (var prefix in new[] { "SalesInvoices", "PurchaseInvoices" })
        {
            var cancelNfe = modelBuilder.Action($"{prefix}CancelNfe");
            cancelNfe.Parameter<Guid>("Key");
            cancelNfe.Parameter<string>("Justification");
            cancelNfe.Returns<NfeIssueOutcomeDto>();

            var completeCancellation = modelBuilder.Action($"{prefix}CompleteNfeCancellation");
            completeCancellation.Parameter<Guid>("Key");
            completeCancellation.Returns<NfeIssueOutcomeDto>();
        }
```
e depois de `purchaseInvoicesNfeXml`:

```csharp
        var salesInvoicesNfeCancellationXml = modelBuilder.Function("SalesInvoicesNfeCancellationXml");
        salesInvoicesNfeCancellationXml.Parameter<Guid>("Key");
        salesInvoicesNfeCancellationXml.Returns<IActionResult>();

        var purchaseInvoicesNfeCancellationXml = modelBuilder.Function("PurchaseInvoicesNfeCancellationXml");
        purchaseInvoicesNfeCancellationXml.Parameter<Guid>("Key");
        purchaseInvoicesNfeCancellationXml.Returns<IActionResult>();
```

- [ ] **Step 5: Controllers** — `SalesInvoicesCancelNfeController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Nfe;

public class SalesInvoicesCancelNfeController(SalesInvoicesNfeCancelService service) : ODataController
{
    [HttpPost("odata/SalesInvoicesCancelNfe")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || !Guid.TryParse(keyObj?.ToString(), out var key))
            return BadRequest("Parâmetro obrigatório: Key.");

        // ⚠️ Parâmetro string ausente/nulo: não chamar ToString() sobre nulo.
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
`PurchaseInvoicesCancelNfeController` idem (`PurchaseInvoicesNfeCancelService`, rota `odata/PurchaseInvoicesCancelNfe`). `SalesInvoicesCompleteNfeCancellationController`/`Purchase…`: copiar `SalesInvoicesConsultNfeController` trocando serviço e rota (`odata/SalesInvoicesCompleteNfeCancellation`). XML: copiar `SalesInvoicesNfeXmlController` com rota `odata/SalesInvoicesNfeCancellationXml(Key={key})` e o serviço novo (idem entrada).

- [ ] **Step 6: DI** — em `ServiceCollectionExtensions.cs`, junto das linhas 350–361:

```csharp
        services.AddScoped<SalesInvoiceNfeCancellationHandler>();
        services.AddScoped<SalesInvoicesNfeCancelService>();
        services.AddScoped<SalesInvoicesNfeCompleteCancellationService>();
        services.AddScoped<SalesInvoicesNfeCancellationXmlDownloadService>();
        services.AddScoped<PurchaseInvoiceNfeCancellationHandler>();
        services.AddScoped<PurchaseInvoicesNfeCancelService>();
        services.AddScoped<PurchaseInvoicesNfeCompleteCancellationService>();
        services.AddScoped<PurchaseInvoicesNfeCancellationXmlDownloadService>();
```
Se existir teste de resolução do container (procure `ServiceProvider`/`BuildServiceProvider` em `SiagroB1.Application.Tests/Startup`), confira que resolve os serviços novos.

- [ ] **Step 7: DANFE** — em `DanfeReportService`:

```csharp
    public async Task<(byte[] Pdf, string FileName)> GeneratePdfAsync(Guid invoiceKey)
    {
        var xml = await db.Context.SalesInvoiceNfeXmls.LatestAuthorizedXmlAsync(invoiceKey);
        var cancelled = await db.Context.SalesInvoices.AsNoTracking()
            .AnyAsync(x => x.Key == invoiceKey && x.NfeStatus == NfeStatus.Cancelled);
        return Render(xml, cancelled);
    }
```
(idem `GeneratePurchasePdfAsync` com `PurchaseInvoices`), e em `Render(string xml, bool cancelled)`:

```csharp
        // NF-e cancelada: a Zeus estampa "DOCUMENTO CANCELADO" no layout.
        var danfe = new DanfeFrNfe(proc, new ConfiguracaoDanfeNfe(header.LogoBytes(), documentoCancelado: cancelled),
            desenvolvedor: "IDX Consultoria e Sistemas", arquivoRelatorio: template);
```
(usings `Microsoft.EntityFrameworkCore`, `SiagroB1.Domain.Enums`.)

- [ ] **Step 8: Rodar e ver passar** — filtro do Step 2; `dotnet build SiagroB1.sln`; suítes inteiras (Application e Fiscal).

- [ ] **Step 9: Commit** — `feat(invoice): ações de cancelamento da NF-e, XML do evento e DANFE cancelado`.

---

### Task 8: Frontend — helpers, formatter, rotas e diálogo de justificativa

**Files:**
- Modify: `siagro-b1-frontend/webapp/helpers/NfeHelpers.ts`
- Modify: `siagro-b1-frontend/webapp/model/formatter.ts` (~linha 920)
- Modify: `siagro-b1-frontend/webapp/model/ServerRoutes.ts` (~linha 166)
- Create: `siagro-b1-frontend/webapp/dialogs/NfeCancelDialog.ts`
- Test: `siagro-b1-frontend/webapp/test/unit/helpers/NfeHelpers.qunit.ts`

**Interfaces:**
- Produces:
  - `NfeOutcome.CancellationError?: string`; `nfeOutcomeMessage` trata `"Cancelled"`.
  - `NFE_CANCEL_MIN = 15`, `NFE_CANCEL_MAX = 255`, `isValidNfeCancelJustification(text: string): boolean`.
  - `canCancelNfe(nfeStatus?: string, invoiceStatus?: string): boolean` (Authorized e documento não cancelado); `needsNfeCancellationCompletion(nfeStatus?: string, invoiceStatus?: string): boolean` (Cancelled e documento ativo); `isReversibleNfeStatus(nfeStatus?: string): boolean` (não Processing/Authorized/Cancelled).
  - `openNfeCancelDialog(owner: Control): Promise<string | null>` — devolve a justificativa (já com trim) ou `null` se fechou.
  - Rotas: `salesInvoicesCancelNfe`, `salesInvoicesCompleteNfeCancellation`, `salesInvoicesNfeCancellationXml`, `purchaseInvoicesCancelNfe`, `purchaseInvoicesCompleteNfeCancellation`, `purchaseInvoicesNfeCancellationXml` (todas `'/odata/…'`).

- [ ] **Step 0: Branch e base** — `git -C siagro-b1-frontend switch -c feature/nfe-cancellation` (de `main`; os `.tlpp` modificados vão junto e não entram em commit). Medir `npx ui5lint` e anotar a contagem (base).

- [ ] **Step 1: QUnit** — acrescentar em `NfeHelpers.qunit.ts` (e importar os nomes novos):

```ts
QUnit.module("NfeHelpers - cancelamento");

QUnit.test("justificativa conta depois do trim, de 15 a 255", function (assert) {
	assert.false(isValidNfeCancelJustification("   curta demais "));
	assert.true(isValidNfeCancelJustification("  Venda desfeita pelo cliente  "));
	assert.true(isValidNfeCancelJustification("x".repeat(255)));
	assert.false(isValidNfeCancelJustification("x".repeat(256)));
});

QUnit.test("só NF-e autorizada de documento ativo é cancelável", function (assert) {
	assert.true(canCancelNfe("Authorized", "Confirmed"));
	assert.true(canCancelNfe("Authorized", "Pending"));
	assert.false(canCancelNfe("Authorized", "Cancelled"));
	assert.false(canCancelNfe("Processing", "Pending"));
	assert.false(canCancelNfe("Cancelled", "Confirmed"));
});

QUnit.test("concluir cancelamento só com NF-e cancelada e documento ativo", function (assert) {
	assert.true(needsNfeCancellationCompletion("Cancelled", "Confirmed"));
	assert.false(needsNfeCancellationCompletion("Cancelled", "Cancelled"));
	assert.false(needsNfeCancellationCompletion("Authorized", "Confirmed"));
});

QUnit.test("estorno só sem NF-e emitida", function (assert) {
	assert.true(isReversibleNfeStatus(undefined));
	assert.true(isReversibleNfeStatus("None"));
	assert.true(isReversibleNfeStatus("Rejected"));
	assert.false(isReversibleNfeStatus("Processing"));
	assert.false(isReversibleNfeStatus("Authorized"));
	assert.false(isReversibleNfeStatus("Cancelled"));
});

QUnit.test("desfecho do cancelamento", function (assert) {
	assert.deepEqual(nfeOutcomeMessage({ NfeStatus: "Cancelled", InvoiceStatus: "Cancelled" }),
		{ type: "success", text: "NF-e cancelada e documento cancelado." });
	assert.deepEqual(nfeOutcomeMessage({ NfeStatus: "Cancelled", InvoiceStatus: "Confirmed", CancellationError: "Liberação travada." }),
		{ type: "warning", text: "NF-e cancelada na SEFAZ, mas o cancelamento do documento falhou: Liberação travada. Corrija e use Concluir cancelamento." });
});
```

- [ ] **Step 2: Rodar e ver falhar** — `yarn ts-typecheck` (nomes inexistentes).

- [ ] **Step 3: Helpers** — em `NfeHelpers.ts`: `CancellationError?: string;` no tipo `NfeOutcome`; no `switch` de `nfeOutcomeMessage`, antes do `default`:

```ts
    case "Cancelled":
      return outcome.CancellationError
        ? {
          type: "warning",
          text: `NF-e cancelada na SEFAZ, mas o cancelamento do documento falhou: ${outcome.CancellationError} ` +
            "Corrija e use Concluir cancelamento.",
        }
        : { type: "success", text: "NF-e cancelada e documento cancelado." };
```
e no fim do arquivo:

```ts
export const NFE_CANCEL_MIN = 15;
export const NFE_CANCEL_MAX = 255;

/** Justificativa do cancelamento (xJust): 15 a 255 caracteres depois do trim, como o servidor conta. */
export function isValidNfeCancelJustification(text: string): boolean {
  const length = (text ?? "").trim().length;
  return length >= NFE_CANCEL_MIN && length <= NFE_CANCEL_MAX;
}

/** "Cancelar" vai pela SEFAZ (com justificativa): NF-e autorizada de documento ainda ativo. */
export function canCancelNfe(nfeStatus?: string, invoiceStatus?: string): boolean {
  return nfeStatus === "Authorized" && invoiceStatus !== "Cancelled";
}

/** NF-e cancelada na SEFAZ e documento ainda ativo: falta a fase local. */
export function needsNfeCancellationCompletion(nfeStatus?: string, invoiceStatus?: string): boolean {
  return nfeStatus === "Cancelled" && invoiceStatus !== "Cancelled";
}

/** Estorno de confirmação: recusado com NF-e em processamento, autorizada ou cancelada. */
export function isReversibleNfeStatus(nfeStatus?: string): boolean {
  return nfeStatus !== "Processing" && nfeStatus !== "Authorized" && nfeStatus !== "Cancelled";
}
```
Atualizar o doc comment de `isEmittedNfeStatus` (inclui cancelada).

- [ ] **Step 4: Formatter e rotas** — em `formatNfeStatus`: `["Cancelled", "Cancelada"]`; em `stateNfeStatus`: `["Cancelled", "Error"]`. Em `ServerRoutes.ts`, junto das rotas de NF-e:

```ts
  // Cancelamento da NF-e (evento 110111): ação com justificativa, conclusão da fase local e XML do evento.
  salesInvoicesCancelNfe: '/odata/SalesInvoicesCancelNfe',
  salesInvoicesCompleteNfeCancellation: '/odata/SalesInvoicesCompleteNfeCancellation',
  salesInvoicesNfeCancellationXml: '/odata/SalesInvoicesNfeCancellationXml',
  purchaseInvoicesCancelNfe: '/odata/PurchaseInvoicesCancelNfe',
  purchaseInvoicesCompleteNfeCancellation: '/odata/PurchaseInvoicesCompleteNfeCancellation',
  purchaseInvoicesNfeCancellationXml: '/odata/PurchaseInvoicesNfeCancellationXml',
```

- [ ] **Step 5: Diálogo** — `webapp/dialogs/NfeCancelDialog.ts` (sem fragmento XML; controles criados em código, como os diálogos programáticos do projeto — confira `DanfeViewer.ts` para o estilo de criação/destruição):

```ts
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import TextArea from "sap/m/TextArea";
import MessageStrip from "sap/m/MessageStrip";
import VBox from "sap/m/VBox";
import Label from "sap/m/Label";
import Control from "sap/ui/core/Control";
import { NFE_CANCEL_MAX, isValidNfeCancelJustification } from "siagrob1/helpers/NfeHelpers";

/**
 * Pede a justificativa do cancelamento da NF-e. Devolve o texto (com trim) ou `null` se o usuário
 * desistiu. O botão só habilita com 15 a 255 caracteres — a mesma conta do servidor.
 */
export function openNfeCancelDialog(owner: Control): Promise<string | null> {
  return new Promise((resolve) => {
    let result: string | null = null;

    const text = new TextArea({
      width: "100%", rows: 4, maxLength: NFE_CANCEL_MAX, showExceededText: false,
      placeholder: "Mínimo de 15 caracteres", liveChange: () => confirm.setEnabled(isValidNfeCancelJustification(text.getValue())),
    });
    // Contador nativo do TextArea (maxLength + showExceededText=false mostra "n caracteres restantes").

    const confirm = new Button({
      text: "Cancelar NF-e", type: "Reject", enabled: false,
      press: () => { result = text.getValue().trim(); dialog.close(); },
    });

    const dialog = new Dialog({
      title: "Cancelar NF-e",
      contentWidth: "32rem",
      content: new VBox({
        items: [
          new MessageStrip({
            type: "Warning", showIcon: true,
            text: "O cancelamento é enviado à SEFAZ e não pode ser desfeito. O documento será cancelado e os saldos estornados.",
          }),
          new Label({ text: "Justificativa", required: true, labelFor: text }),
          text,
        ],
      }).addStyleClass("sapUiSmallMargin"),
      beginButton: confirm,
      endButton: new Button({ text: "Voltar", press: () => dialog.close() }),
      afterClose: () => { dialog.destroy(); resolve(result); },
    });

    owner.addDependent(dialog);
    dialog.open();
  });
}
```
⚠️ `confirm` é usado no `liveChange` antes da declaração: mover a criação do `Button` para antes do `TextArea` se o ESLint (`no-use-before-define`) reclamar. `git add` o arquivo.

- [ ] **Step 6: Rodar** — `yarn ts-typecheck`, `yarn lint`, QUnit (comando dos Global Constraints) → os testes novos passam; `npx ui5lint` ≤ base.

- [ ] **Step 7: Commit** (frontend) — `feat(invoice): diálogo e regras do cancelamento da NF-e`.

---

### Task 9: Frontend — detalhe da saída e da entrada

**Files:**
- Modify: `siagro-b1-frontend/webapp/view/salesInvoices/Detail.view.xml` (`uxap:actions`, linhas ~31–51)
- Modify: `siagro-b1-frontend/webapp/controller/salesInvoices/Detail.controller.ts` (`onConsultNfe`, `onNfeXml`, `onCancel`, `cancelAction`)
- Modify: `siagro-b1-frontend/webapp/view/purchaseInvoices/Detail.view.xml` (footer, linhas ~102–132)
- Modify: `siagro-b1-frontend/webapp/controller/purchaseInvoices/Detail.controller.ts` (`onCancelInvoice`, `onNfeXml`)
- Modify: `siagro-b1-frontend/webapp/view/salesInvoices/fragments/NfePanel.fragment.xml` (compartilhado)

**Interfaces:**
- Consumes: Task 8 (`openNfeCancelDialog`, `canCancelNfe`, rotas); Task 7 (actions).

- [ ] **Step 1: Saída — view** — no `uxap:actions`:
  - "Consultar situação": `visible="{= ${path: 'NfeStatus', targetType: 'any'} === 'Processing' || ${path: 'NfeStatus', targetType: 'any'} === 'Authorized' }"`.
  - Depois de "Concluir confirmação":
    ```xml
          <Button text="Concluir cancelamento" type="Attention" press=".onCompleteNfeCancellation"
            visible="{= ${path: 'NfeStatus', targetType: 'any'} === 'Cancelled' &amp;&amp; ${path: 'InvoiceStatus', targetType: 'any'} !== 'Cancelled' }" />
    ```
  - "DANFE": visível com `Authorized` **ou** `Cancelled`. "XML": idem.
  - Depois de "XML":
    ```xml
          <Button text="XML do cancelamento" icon="sap-icon://download" press=".onNfeCancellationXml"
            visible="{= ${path: 'NfeStatus', targetType: 'any'} === 'Cancelled' }" />
    ```
  - "Estornar": `enabled="{= ${path: 'InvoiceStatus', targetType: 'any'} === 'Confirmed' &amp;&amp; ${path: 'NfeStatus', targetType: 'any'} !== 'Processing' &amp;&amp; ${path: 'NfeStatus', targetType: 'any'} !== 'Authorized' &amp;&amp; ${path: 'NfeStatus', targetType: 'any'} !== 'Cancelled' }"`.
  - "Cancelar": `enabled="{= (${path: 'InvoiceStatus', targetType: 'any'} === 'Pending' || ${path: 'InvoiceStatus', targetType: 'any'} === 'Confirmed') &amp;&amp; ${path: 'NfeStatus', targetType: 'any'} !== 'Processing' &amp;&amp; ${path: 'NfeStatus', targetType: 'any'} !== 'Cancelled' }"`.

- [ ] **Step 2: Saída — controller**:

```ts
  async onCancel() {
    const ctx = this.getView().getBindingContext() as Context;
    if (!ctx) {
      MessageBox.error("Contexto inválido.")
      return;
    }

    // NF-e autorizada: o cancelamento vai pela SEFAZ, com justificativa.
    if (canCancelNfe(ctx.getProperty("NfeStatus") as string, ctx.getProperty("InvoiceStatus") as string)) {
      const justification = await openNfeCancelDialog(this.getView());
      if (justification !== null) {
        await this.runNfeAction(ServerRoutes.salesInvoicesCancelNfe, ctx, { Justification: justification });
      }
      return;
    }

    if (await DialogHelper.confirmDialog("Cancelar documento de saída ?")) {
      this.cancelAction(ctx);
    }
  }

  async onCompleteNfeCancellation() {
    const ctx = this.getView().getBindingContext() as Context;
    if (ctx) {
      await this.runNfeAction(ServerRoutes.salesInvoicesCompleteNfeCancellation, ctx);
    }
  }

  async onNfeCancellationXml() {
    await this.downloadNfeXml(ServerRoutes.salesInvoicesNfeCancellationXml, "procEventoNFe");
  }
```
- `runNfeAction(url, ctx, extra: Record<string, unknown> = {})` passa a mandar `{ Key: …, ...extra }`.
- Extrair o corpo de `onNfeXml` para `private async downloadNfeXml(route: string, suffix: string)` (o `onNfeXml` chama `downloadNfeXml(ServerRoutes.salesInvoicesNfeXml, "procNFe")`), sem mudar o comportamento atual.
- Imports: `canCancelNfe` de `NfeHelpers`, `openNfeCancelDialog` de `siagrob1/dialogs/NfeCancelDialog`.

- [ ] **Step 3: Entrada — view** — no footer, as mesmas regras: "Consultar situação" com `Processing` ou `Authorized`; "Concluir cancelamento" (press `.onCompleteNfeCancellation`); DANFE/XML com `Authorized` ou `Cancelled`; "XML do cancelamento"; "Estornar" `visible` só com `Confirmed` e NF-e não `Processing`/`Authorized`/`Cancelled`; "Cancelar Documento" `visible="{= ${path: 'InvoiceStatus', targetType: 'any'} !== 'Cancelled' &amp;&amp; ${path: 'NfeStatus', targetType: 'any'} !== 'Processing' &amp;&amp; ${path: 'NfeStatus', targetType: 'any'} !== 'Cancelled' }"`.

- [ ] **Step 4: Entrada — controller** — `onCancelInvoice`:

```ts
  async onCancelInvoice() {
    const ctx = this.getView().getBindingContext() as Context;

    if (!ctx) {
      return;
    }

    // NF-e própria autorizada: o cancelamento vai pela SEFAZ, com justificativa.
    if (canCancelNfe(ctx.getProperty("NfeStatus") as string, ctx.getProperty("InvoiceStatus") as string)) {
      const justification = await openNfeCancelDialog(this.getView());
      if (justification !== null) {
        await this.runNfeAction(ServerRoutes.purchaseInvoicesCancelNfe, ctx, { Justification: justification });
      }
      return;
    }

    if (!await confirmDialog(
      "Cancelar este documento ? A chave da NF-e de terceiro volta a ficar livre.",
      "Cancelar documento")) {
      return;
    }

    await this.invokeAction("/PurchaseInvoicesCancel(...)", ctx, "Documento cancelado.");
  }
```
+ `onCompleteNfeCancellation`, `onNfeCancellationXml`, `runNfeAction` com `extra` e `downloadNfeXml`, como na saída (rotas `purchaseInvoices…`).

- [ ] **Step 5: Painel NF-e** — em `NfePanel.fragment.xml`, depois de "Autorizada em":

```xml
      <Label text="Protocolo do cancelamento" visible="{= ${path: 'NfeStatus', targetType: 'any'} === 'Cancelled' }" />
      <Text text="{NfeCancellationProtocol}" visible="{= ${path: 'NfeStatus', targetType: 'any'} === 'Cancelled' }" />
      <Label text="Cancelada em" visible="{= ${path: 'NfeStatus', targetType: 'any'} === 'Cancelled' }" />
      <Text text="{ path: 'NfeCancelledAt', targetType: 'any', formatter: '.formatter.formatDateTime' }"
            visible="{= ${path: 'NfeStatus', targetType: 'any'} === 'Cancelled' }" />
      <Label text="Justificativa" visible="{= ${path: 'NfeStatus', targetType: 'any'} === 'Cancelled' }" />
      <Text text="{NfeCancellationReason}" visible="{= ${path: 'NfeStatus', targetType: 'any'} === 'Cancelled' }" />
```
e depois do bloco "Confirmação":

```xml
      <Label text="Cancelamento" visible="{= !!${path: 'NfeCancellationError', targetType: 'any'} }" />
      <MessageStrip type="Warning" showIcon="true" text="{NfeCancellationError}" visible="{= !!${path: 'NfeCancellationError', targetType: 'any'} }" />
```
⚠️ Nada de `--` dentro de comentário XML. A tela da entrada usa este fragmento: conferir que ela também mostra os campos (o `$select` do detalhe da entrada é só das linhas; o cabeçalho vem inteiro — se não vier, acrescente os 4 campos onde o cabeçalho é selecionado).

- [ ] **Step 6: Rodar** — `yarn ts-typecheck`, `yarn lint`, `npx ui5lint` (≤ base), QUnit.

- [ ] **Step 7: Commit** (frontend) — `feat(invoice): cancelar NF-e pelo detalhe da saída e da entrada`.

---

### Task 10: Frontend — listas (cancelar pela SEFAZ, filtro e coluna "Cancelada em")

**Files:**
- Modify: `siagro-b1-frontend/webapp/view/salesInvoices/Main.view.xml` (`$select` ~linha 49; coluna nova depois de "Situação NF-e" ~linha 254)
- Modify: `siagro-b1-frontend/webapp/controller/salesInvoices/Main.controller.ts` (`onCancel` ~linha 162, `applyFilters` ~linha 84)
- Modify: `siagro-b1-frontend/webapp/controller/salesInvoices/BaseController.ts` (`createColumnConfig` ~linha 440)
- Modify: `siagro-b1-frontend/webapp/view/salesInvoices/fragments/Filterbar.fragment.xml` (depois do `status`)
- Modify: `siagro-b1-frontend/webapp/view/purchaseInvoices/Main.view.xml` (`$select` ~linha 44; coluna depois de "Situação NF-e" ~linha 105)
- Modify: `siagro-b1-frontend/webapp/controller/purchaseInvoices/Main.controller.ts` (`onCancelInvoice` ~linha 120, `applyFilters`, `createColumnConfig`)
- Modify: `siagro-b1-frontend/webapp/view/purchaseInvoices/fragments/Filterbar.fragment.xml`

**Interfaces:**
- Consumes: Task 8.
- Produces: chave de filtro `filter>/NfeStatus` nas duas listas (enum → `NfeStatus eq 'Cancelled'`).

- [ ] **Step 1: Filtro** — nos dois `Filterbar.fragment.xml`, um `FilterGroupItem` no molde do `status` (saída) — visível só onde há NF-e:

```xml
      <fb:FilterGroupItem name="nfeStatus" label="Situação NF-e" groupName="GroupStatus" visibleInFilterBar="true"
        visible="{= ${ui>/standalone} === true &amp;&amp; ${ui>/anyBranchIssuesNfe} === true }">
        <fb:control>
            <Select forceSelection="false" selectedKey="{ path: 'filter>/NfeStatus', type: 'sap.ui.model.type.String' }">
              <core:ListItem key="" text="Todas"/>
              <core:ListItem key="Processing" text="Em processamento"/>
              <core:ListItem key="Authorized" text="Autorizada"/>
              <core:ListItem key="Rejected" text="Rejeitada"/>
              <core:ListItem key="Denied" text="Denegada"/>
              <core:ListItem key="Cancelled" text="Cancelada"/>
            </Select>
        </fb:control>
      </fb:FilterGroupItem>
```
(Na entrada, use o `groupName` que o filtro de status de lá usa.) Nos dois `applyFilters`, `NfeStatus` entra na lista de enums em string crua: saída `if (filterKey == "InvoiceStatus" || filterKey == "InvoiceType" || filterKey == "NfeStatus")`; entrada `if (key === "InvoiceType" || key === "IssuerType" || key === "InvoiceStatus" || key === "NfeStatus")`. Se o modelo `filter` é criado com chaves fixas (`createFilterModel`/`clearFilters`), acrescente `NfeStatus: ""`.

- [ ] **Step 2: Coluna "Cancelada em"** — nas duas `Main.view.xml`, depois da coluna "Situação NF-e", com a mesma visibilidade:

```xml
          <t:Column label="Cancelada em" width="10rem" sortProperty="NfeCancelledAt" visible="{= ${ui>/standalone} === true &amp;&amp; ${ui>/anyBranchIssuesNfe} === true }">
            <t:template>
              <Text text="{ path: 'NfeCancelledAt', targetType: 'any', formatter: '.formatter.formatDateTime' }" wrapping="false" />
            </t:template>
          </t:Column>
```
e `NfeCancelledAt` (+ `InvoiceStatus` se não estiver) no `$select` dos dois bindings (saída: `'ShipmentLoadKey,WithoutTaxDocument,NfeStatus,NfeCancelledAt,…'`; entrada: `'NfeStatus,NfeCancelledAt,IssuerType,IsNfeReturn'`).

- [ ] **Step 3: Exportação** — nos dois `createColumnConfig`, antes de "Chave NF-e":

```ts
    aCols.push({
      label: "Situação NF-e",
      property: "NfeStatus",
      type: EdmType.Enumeration,
      valueMap: {
        "None": "", "Processing": "Em processamento", "Authorized": "Autorizada",
        "Rejected": "Rejeitada", "Denied": "Denegada", "Cancelled": "Cancelada",
      },
    });

    aCols.push({ label: "Cancelada em", property: "NfeCancelledAt", type: EdmType.DateTime });
```
(Se a lista da saída já exporta "Situação NF-e", acrescente só "Cancelada em".)

- [ ] **Step 4: Cancelar pela lista** — saída `Main.controller.ts` `onCancel`, depois de validar a seleção:

```ts
    const ctx = table.getContextByIndex(selectedInvoice[0]) as Context;

    // NF-e autorizada: o cancelamento vai pela SEFAZ, com justificativa.
    if (canCancelNfe(ctx.getProperty("NfeStatus") as string, ctx.getProperty("InvoiceStatus") as string)) {
      const justification = await openNfeCancelDialog(this.getView());
      if (justification === null) {
        return;
      }

      this.setBusy(true);
      try {
        const result = await sendJson("POST", ServerRoutes.salesInvoicesCancelNfe,
          { Key: ctx.getProperty("Key") as string, Justification: justification });

        if (!result.ok) {
          MessageBox.error(result.message);
          return;
        }

        const message = nfeOutcomeMessage(odataValue<NfeOutcome>(result.data));
        if (message.type === "success") {
          MessageToast.show(message.text);
        } else {
          MessageBox.warning(message.text);
        }
      } finally {
        this.refreshData();
        this.setBusy(false);
      }
      return;
    }
```
(o restante — confirmação e `SalesInvoicesCancel` — continua para documento sem NF-e). `InvoiceStatus` precisa estar no binding da lista (já está numa coluna). Imports: `sendJson`, `odataValue` (`FetchHelpers`), `nfeOutcomeMessage`, `NfeOutcome`, `canCancelNfe`, `openNfeCancelDialog`, `ServerRoutes`, `Context`. Entrada `onCancelInvoice`: o mesmo bloco com `ServerRoutes.purchaseInvoicesCancelNfe` e o `onRefresh()` da lista. O bloco fica em cada controller (cada lista tem seu refresh); a mensagem vem sempre de `nfeOutcomeMessage`.

- [ ] **Step 5: Rodar** — `yarn ts-typecheck`, `yarn lint`, `npx ui5lint` (≤ base), QUnit.

- [ ] **Step 6: Commit** (frontend) — `feat(invoice): listas filtram e exportam NF-e cancelada e cancelam pela SEFAZ`.

---

### Task 11: Verificação ponta a ponta (homologação, CEAGUI)

**Files:** nenhum código. Evidências fora dos repos (`C:\Projetos\SiagroB1\nfe-homologacao-testes\`).

- [ ] **Step 1: Gates** — backend: `dotnet build SiagroB1.sln` (0 erros), `dotnet test SiagroB1.Application.Tests`, `dotnet test SiagroB1.Fiscal.Tests` (anotar as contagens). Frontend: `yarn ts-typecheck`, `yarn lint`, QUnit, `npx ui5lint` (≤ base).
- [ ] **Step 2: Migration no dev** — `ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`; conferir as 8 colunas e o `__EFMigrationsHistory` no `CEAGUI_SIAGRO_DEV`.
- [ ] **Step 3: Subir a stack** — perfil `ceagui` do Web/Gateway/Reports + `yarn start:dev` (memória "Subir a stack local"); `Nfe__CertificateKey` vem da variável de ambiente do usuário.
- [ ] **Step 4: Saída — pedir confirmação ao usuário**, então emitir uma saída nova (data de hoje, pesos preenchidos) pela tela até "Autorizada"; **pedir confirmação**, e cancelar pela tela com justificativa. Conferir: tela (situação Cancelada, painel com protocolo/data/justificativa, Estornar desabilitado, "XML do cancelamento" baixa o `procEventoNFe`, DANFE com tarja "DOCUMENTO CANCELADO"); banco (`NfeStatus = 5`, `InvoiceStatus` Cancelado, `NfeCancellationProtocol`, XML kind 4).
- [ ] **Step 5: Entrada própria** — com confirmação do usuário a cada envio: emitir uma entrada própria nova e cancelá-la (a nº 5 da série 9 só se ainda estiver dentro de 24 h da autorização). Mesmas conferências.
- [ ] **Step 6: Devolução de venda** — com confirmação: emitir uma venda, devolver, emitir a devolução, tentar cancelar a venda (recusa "possui retorno" **sem** chamar a SEFAZ), cancelar a devolução e conferir a venda voltando a Confirmada.
- [ ] **Step 7: Listas** — filtro "Situação NF-e = Cancelada" nas duas listas, coluna "Cancelada em", exportação Excel com as duas colunas.
- [ ] **Step 8: Recusa da SEFAZ** — se houver nota autorizada há mais de 24 h, tentar cancelar e ver a mensagem 501 sem mudança no documento (sem nota assim, registrar como não verificado).
- [ ] **Step 9: Relatório** — XML/DANFE dos testes em `nfe-homologacao-testes\`, números usados da série 9 anotados, o que foi e o que não foi verificado. Atualizar a memória do projeto (`nfe-standalone-emission-feature` → sub-projeto 2b cancelamento) — sem commit de memória.
