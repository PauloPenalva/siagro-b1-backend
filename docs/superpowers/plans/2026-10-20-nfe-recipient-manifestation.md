# Manifestação do Destinatário (DF-e) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A CEAGUI (STANDALONE) vê as NF-e emitidas contra o CNPJ de cada filial, manifesta-se (Ciência, Confirmação, Desconhecimento, Operação não Realizada), baixa o XML e, numa nota confirmada, abre o Documento de Entrada já preenchido pelo XML.

**Architecture:** O `INfeSefazClient` (Zeus) ganha a consulta `NFeDistribuicaoDFe` (por NSU e por chave) e o evento de manifestação. A Application guarda cada documento recebido em `RECEIVED_DFES` (banco da empresa) e o último NSU por filial em `BRANCH_DFE_SYNC_STATES`; um job Hangfire consulta de hora em hora e a tela também tem "Consultar SEFAZ". A entrada reaproveita o fluxo existente de importação de XML (`PurchaseInvoicesImportXml` + `Add.controller.applyImportedXml`).

**Tech Stack:** .NET 10, EF Core (SQL Server), OData v4, Hangfire, Zeus.Net.NFe.NFCe 2026.9.24.1416; OpenUI5 + TypeScript.

**Spec:** pedido do usuário em 20/10/2026 (perguntas de brainstorming dispensadas): "visualizar as NF's emitidas contra o seu CNPJ e dar aceite ou recusa; baixar XML do documento e, em um documento aceito, fazer a entrada no Doc de entrada". As decisões abaixo são do agente.

**Worktrees:** backend `C:\Projetos\SiagroB1\wt-dfe-backend`, frontend `C:\Projetos\SiagroB1\wt-dfe-frontend`, ambos no branch `feature/nfe-recipient-manifestation`. Nunca push. Commits seguem `tipo(invoice): descrição` (pt-BR); commit com migration leva o trailer `DB: <MigrationName>`.

## Global Constraints

- Só modo STANDALONE (`ErpMode.IsStandalone` / `BranchNfeSettingsService.EnsureStandalone`); menu com `StandaloneOnly = true`.
- Certificado, ambiente e CNPJ/UF vêm da FILIAL: `BranchNfeSettingsService.OpenAsync(branchCode)` → `NfeServiceContext`; CNPJ = `Branch.TaxId`, UF = `Branch.StateCode`.
- Identificadores em inglês; textos de tela e mensagens de erro em pt-BR.
- `SiagroB1.Fiscal` não referencia DbContext nem `IConfiguration`.
- Todo arquivo novo: `git add` logo após criar.
- Regras da SEFAZ: código 137 = nenhum documento; 138 = documentos encontrados; 656 = consumo indevido (aguardar 1 hora); quando `ultNSU == maxNSU` não consultar de novo antes de 1 hora. Manifestação: 210210 Ciência, 210200 Confirmação, 210220 Desconhecimento, 210240 Operação não Realizada (justificativa obrigatória, 15 a 255 caracteres). Evento registrado = 135/136/155 (`NfeStatusCodes.IsEventRegistered` + 136); 573 = duplicado.

## Review Focus

1. **Consumo indevido (656):** consultas repetidas em menos de 1 hora → não chamar a SEFAZ, responder "Aguarde até HH:mm para consultar novamente." (teste na Task 3).
2. **Mesma chave chegando duas vezes** (resNFe e depois procNFe, ou outro NSU) → atualiza a linha existente, nunca duplica (teste na Task 3).
3. **Manifestação conclusiva repetida ou trocada** (já Confirmada e o usuário tenta Desconhecer) → recusar com mensagem; 573 da SEFAZ trata como registrado (teste na Task 4).
4. **NF-e cancelada pelo emitente** (resEvento 110111 ou cSitNFe 3) → situação "Cancelada" e "Dar entrada" bloqueado (testes nas Tasks 3 e 4).
5. **Entrada duplicada:** a chave já tem Documento de Entrada → "Dar entrada" bloqueado e a lista mostra o documento vinculado (teste na Task 5).

---

### Task 1: Cliente SEFAZ — distribuição DF-e e evento de manifestação (Fiscal)

**Files:**
- Create: `SiagroB1.Fiscal/Nfe/NfeDistribution.cs` (records, enum, parser)
- Modify: `SiagroB1.Fiscal/Nfe/NfeSefaz.cs` (interface + `NfeStatusCodes`)
- Modify: `SiagroB1.Fiscal/Nfe/ZeusNfeSefazClient.cs`
- Modify: `SiagroB1.Application.Tests/Support/FakeNfeSefazClient.cs`
- Test: `SiagroB1.Fiscal.Tests/Nfe/NfeDistributionDocumentParserTests.cs`, `SiagroB1.Fiscal.Tests/Nfe/NfeManifestationEventSchemaTests.cs`

**Interfaces — Produces:**
```csharp
public enum NfeManifestationType { Confirmation = 210200, Acknowledgement = 210210, Unknown = 210220, NotPerformed = 210240 }
public sealed record NfeManifestationRequest(string AccessKey, NfeManifestationType Type, string RecipientTaxId, string? Justification, DateTimeOffset EventAt);
public sealed record NfeDistributionDocument(long Nsu, string Schema, string Xml); // Xml já descompactado (gzip+base64 do docZip)
public sealed record NfeDistributionResult(int StatusCode, string Reason, long LastNsu, long MaxNsu, IReadOnlyList<NfeDistributionDocument> Documents);
public enum DfeKind { Summary, FullInvoice, Event, Other }
public sealed record DfeParsed(DfeKind Kind, string? AccessKey, string? IssuerTaxId, string? IssuerName, DateTimeOffset? IssueDate,
    decimal? TotalValue, int? InvoiceSituation /*cSitNFe 1/2/3*/, int? EventType, string? FullXml);
public static class NfeDistributionDocumentParser { public static DfeParsed Parse(NfeDistributionDocument doc); }
// INfeSefazClient:
Task<NfeDistributionResult> DistributionByNsuAsync(string recipientTaxId, long lastNsu, NfeServiceSettings settings, CancellationToken ct = default);
Task<NfeDistributionResult> DistributionByKeyAsync(string recipientTaxId, string accessKey, NfeServiceSettings settings, CancellationToken ct = default);
Task<NfeEventResult> SendManifestationAsync(NfeManifestationRequest request, NfeServiceSettings settings, CancellationToken ct = default);
// NfeStatusCodes:
public const int NoDocuments = 137; public const int DocumentsFound = 138; public const int ExcessiveUse = 656;
```

- [ ] **Step 1: Testes do parser** — `Parse_ResNFe_ReadsSummary` (chave, CNPJ, xNome, dhEmi, vNF, cSitNFe=1 → `Kind=Summary`), `Parse_ProcNFe_ReturnsFullXmlAndKey` (`Kind=FullInvoice`, `FullXml` = o XML inteiro, valor e emitente lidos de `emit`/`ICMSTot`), `Parse_ResEventoCancellation_ReturnsEvent` (`Kind=Event`, `EventType=110111`), `Parse_UnknownSchema_ReturnsOther`. Use XMLs mínimos no namespace `http://www.portalfiscal.inf.br/nfe`.
- [ ] **Step 2: Teste do evento** — `ManifestationEvent_NotPerformed_CarriesJustification_AndValidatesSchema`, no molde de `NfeCorrectionEventSchemaTests` (evento montado pela mesma rotina que o client usa, `tpEvento=210240`, `xJust` presente; `cOrgao=91`).
- [ ] **Step 3: Rodar** `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~NfeDistribution|FullyQualifiedName~NfeManifestation"` → FAIL (tipos inexistentes).
- [ ] **Step 4: Implementar.** No Zeus: `services.NfeDistDFeInteresse(ufAutor, documento, ultNSU: lastNsu.ToString("D15"))` e a sobrecarga por `chNFE`; `docZip` descompactado (a Zeus expõe o conteúdo; senão gzip de `Convert.FromBase64String`). `services.RecepcaoEventoManifestacaoDestinatario(idLote, sequencia 1, [chave], TipoEventoManifestacaoDestinatario, cpfCnpj, justificativa)`. Conferir assinaturas exatas com context7 ou pelo metadata da DLL; respostas via `RunAsync` e `NfeSefazResponseMapper.FromEvent`. Fake: filas configuráveis por teste (`EnqueueDistribution`, `EnqueueManifestation`) e registro das chamadas.
- [ ] **Step 5: Rodar** os testes de Fiscal inteiros → PASS; `dotnet build SiagroB1.sln` sem erros.
- [ ] **Step 6: Commit** `feat(invoice): consulta DF-e e evento de manifestação no cliente da SEFAZ`.

### Task 2: Entidades, migrations e menu

**Files:**
- Create: `SiagroB1.Domain/Entities/ReceivedDfe.cs`, `SiagroB1.Domain/Entities/BranchDfeSyncState.cs`, `SiagroB1.Domain/Enums/DfeManifestationStatus.cs`
- Modify: DbContext da App (DbSets + configuração)
- Create: migration AppContext `AddReceivedDfes`; migration CommonContext `AddReceivedDfesMenu` (molde `20261020120000_AddFinancialAccountTransfersMenu.cs`)

**Interfaces — Produces:**
```csharp
public enum DfeManifestationStatus { None = 0, Acknowledged = 1, Confirmed = 2, Unknown = 3, NotPerformed = 4 }
public enum DfeInvoiceSituation { Authorized = 1, Denied = 2, Cancelled = 3 }
public class ReceivedDfe { int Id; string BranchCode; string AccessKey /*44*/; long Nsu; string? IssuerTaxId; string? IssuerName;
  DateTimeOffset? IssueDate; decimal? TotalValue; DfeInvoiceSituation Situation; DfeManifestationStatus ManifestationStatus;
  DateTimeOffset? ManifestedAt; string? ManifestationProtocol; string? ManifestationJustification; string? ManifestationProcEventXml;
  string? Xml; DateTimeOffset? XmlDownloadedAt; int? PurchaseInvoiceDocEntry; DateTimeOffset CreatedAt; }
public class BranchDfeSyncState { string BranchCode /*PK*/; long LastNsu; long MaxNsu; DateTimeOffset? LastQueryAt; DateTimeOffset? NextQueryAllowedAt; }
```
Tabelas `RECEIVED_DFES` (índice único `BranchCode + AccessKey`, índice em `IssueDate`) e `BRANCH_DFE_SYNC_STATES`; colunas em UPPER_SNAKE como o resto do contexto. Menu: `Key = "receivedDfes"`, `Title = "Manifestação do Destinatário"`, `Icon = "sap-icon://inbox"`, `ParentKey` = chave do menu Fiscal de `20261019193006_AddFiscalMenu.cs`, `StandaloneOnly = true`, mais a linha em `ROLE_MENUS` para `ADMIN`.

- [ ] **Step 1:** Criar entidades/config; `dotnet ef migrations add AddReceivedDfes` e `AddReceivedDfesMenu` com `ASPNETCORE_ENVIRONMENT` explícito (Development) — nunca o profile `db-migration`.
- [ ] **Step 2:** Revisar o SQL gerado (`dotnet ef migrations script` da migration) — só CREATE TABLE/INDEX e o INSERT do menu.
- [ ] **Step 3:** `dotnet build` → OK. Commit `feat(invoice): tabelas e menu da manifestação do destinatário` com trailers `DB: AddReceivedDfes` e `DB: AddReceivedDfesMenu`.

### Task 3: Sincronização com a SEFAZ (serviço + job)

**Files:**
- Create: `SiagroB1.Application/Services/Nfe/ReceivedDfeSyncService.cs`, `SiagroB1.Application/Jobs/ReceivedDfeSyncJob.cs`
- Modify: `CompanyScopedJobRunner` (constante do job), `SiagroB1.Web/Program.cs` (recorrente de hora em hora, só STANDALONE, `RemoveIfExists` senão), `ServiceCollectionExtensions.AddApplicationServices`
- Test: `SiagroB1.Application.Tests/Nfe/ReceivedDfeSyncServiceTests.cs`

**Interfaces — Produces:**
```csharp
public sealed record DfeSyncResult(int NewDocuments, int UpdatedDocuments, DateTimeOffset? NextQueryAllowedAt, string Message);
public class ReceivedDfeSyncService { Task<DfeSyncResult> SyncBranchAsync(string branchCode, CancellationToken ct = default);
                                     Task SyncAllBranchesAsync(CancellationToken ct = default); /* filiais com certificado */ }
```
Laço: chama `DistributionByNsuAsync(Branch.TaxId, state.LastNsu)` até `LastNsu >= MaxNsu` ou 137, no máximo 20 chamadas por execução. Grava `LastNsu`/`MaxNsu` a cada lote (salvar por lote: queda no meio não repete). Ao terminar alcançado (ou 137/656), `NextQueryAllowedAt = agora + 1h`. Upsert por `(BranchCode, AccessKey)`: Summary preenche cabeçalho; FullInvoice preenche cabeçalho + `Xml` + `XmlDownloadedAt`; Event 110111 → `Situation = Cancelled`; Event 210200/210210/210220/210240 vindo da SEFAZ atualiza `ManifestationStatus` se estiver atrás.

- [ ] **Step 1: Testes** — `Sync_InsertsSummaries_AndAdvancesNsu`; `Sync_SameKeyTwice_UpdatesInsteadOfDuplicating` (resNFe e depois procNFe → 1 linha, com Xml); `Sync_CancellationEvent_MarksCancelled`; `Sync_BeforeNextAllowedTime_DoesNotCallSefaz` (mensagem "Aguarde até HH:mm para consultar novamente."); `Sync_656_SetsOneHourWait`; `Sync_StopsAfterMaxNsuReached`.
- [ ] **Step 2:** Rodar `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~ReceivedDfeSync` → FAIL.
- [ ] **Step 3:** Implementar serviço, job e registro.
- [ ] **Step 4:** Rodar → PASS. Commit `feat(invoice): consulta periódica dos DF-e emitidos contra a filial`.

### Task 4: Manifestação e download do XML

**Files:**
- Create: `SiagroB1.Application/Services/Nfe/ReceivedDfeManifestService.cs`, `SiagroB1.Application/Services/Nfe/ReceivedDfeXmlService.cs`
- Test: `SiagroB1.Application.Tests/Nfe/ReceivedDfeManifestServiceTests.cs`, `ReceivedDfeXmlServiceTests.cs`

**Interfaces — Produces:**
```csharp
public class ReceivedDfeManifestService { Task<ReceivedDfe> ExecuteAsync(int id, NfeManifestationType type, string? justification, CancellationToken ct = default); }
public class ReceivedDfeXmlService { Task<(byte[] Content, string FileName)> GetAsync(int id, CancellationToken ct = default); } // FileName = "<chave>-procNFe.xml"
```
Regras (mensagens pt-BR, `BusinessException` do projeto): Ciência só de `None`; Confirmação/Desconhecimento/Não Realizada de `None` ou `Acknowledged` (são conclusivas — depois disso nada); NotPerformed exige justificativa 15–255 ("Informe uma justificativa entre 15 e 255 caracteres."); NF-e cancelada não aceita Confirmação. Registrado (135/136/155) ou 573 → grava status, protocolo, data e procEvento; outro código → erro com `"{cStat} - {xMotivo}"` sem mudar o status. Após Ciência ou Confirmação sem `Xml`, tenta `DistributionByKeyAsync` (falha aqui não desfaz a manifestação). XML: se `Xml` nulo e status `None`, envia Ciência automaticamente, depois baixa por chave; se ainda não houver XML → "O XML ainda não está disponível na SEFAZ. Tente novamente mais tarde."

- [ ] **Step 1: Testes** — `Acknowledge_FromNone_Registers`; `Confirm_AfterConfirmed_IsRejected`; `Unknown_AfterConfirmed_IsRejected`; `NotPerformed_WithoutJustification_IsRejected`; `Duplicate573_IsTreatedAsRegistered`; `Rejection_KeepsStatus_AndThrowsWithReason`; `Confirm_CancelledInvoice_IsRejected`; `Confirm_DownloadsXml_WhenMissing`; `GetXml_FromNone_SendsAcknowledgementThenDownloads`; `GetXml_NotAvailable_Throws`.
- [ ] **Step 2:** FAIL → implementar → PASS (`--filter FullyQualifiedName~ReceivedDfe`).
- [ ] **Step 3:** Commit `feat(invoice): manifestação do destinatário e download do XML`.

### Task 5: API OData e vínculo com o Documento de Entrada

**Files:**
- Create: `SiagroB1.Web/Controllers/ReceivedDfesController.cs` (somente leitura, `[EnableQuery]`, filtrado pela filial/empresa como os demais), `SiagroB1.Web/Actions/Nfe/ReceivedDfesActionsController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `PurchaseInvoicesCreateService`
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesCreateReceivedDfeLinkTests.cs`

**Interfaces — Produces (consumidas pelo frontend):**
- `GET odata/ReceivedDfes` (entity set; enums como string).
- `POST odata/ReceivedDfesSync` `{ BranchCode }` → `DfeSyncResult`.
- `POST odata/ReceivedDfesManifest` `{ Id, Type: "Confirmation"|"Acknowledgement"|"Unknown"|"NotPerformed", Justification }` → `ReceivedDfe`.
- `GET odata/ReceivedDfesXml(Id=…)` função → `{ XmlContent, FileName }` (string; o frontend usa tanto no download quanto no `applyImportedXml`). Não usar navegação/late property (dão 404 neste projeto).
- `PurchaseInvoicesCreateService`: depois de gravar, se `ChaveNFe` casa com `ReceivedDfe.AccessKey` da filial, preenche `PurchaseInvoiceDocEntry`. Se a chave já tem entrada vinculada, o create existente de duplicidade já recusa — confirmar no teste.

- [ ] **Step 1: Testes** — `Create_WithKeyOfReceivedDfe_LinksDocEntry`; `Create_SecondEntryForSameKey_IsRejected`.
- [ ] **Step 2:** FAIL → implementar → PASS; `dotnet build`; subir Web+Gateway (profile `ceagui`) e conferir `GET /odata/$metadata` com `ReceivedDfe` e as 3 ações/função. Derrubar pelo PID.
- [ ] **Step 3:** Commit `feat(invoice): API da manifestação do destinatário`.

### Task 6: Tela "Manifestação do Destinatário" (frontend)

**Files:**
- Create: `webapp/view/receivedDfes/Main.view.xml`, `webapp/controller/receivedDfes/Main.controller.ts`, `webapp/view/receivedDfes/fragments/ManifestJustificationDialog.fragment.xml`
- Modify: `webapp/manifest.json` (rota/target `receivedDfes`), `webapp/i18n/i18n.properties` se a tela usar i18n

Tela no padrão das listas do projeto (DynamicPage + FilterBar + `sap.m.Table` com seleção múltipla). Filtros: Filial, Emissão de/até (data "até" inclui o dia), Emitente, Manifestação, Situação. Colunas: Emissão, Emitente (CNPJ + nome), Número (extraído da chave: posições 26–34), Valor, Situação (Autorizada/Denegada/Cancelada), Manifestação (Sem manifestação/Ciência/Confirmada/Desconhecida/Não realizada, com `ObjectStatus`), XML (ícone quando disponível), Entrada (link para `purchaseInvoicesDetail` quando `PurchaseInvoiceDocEntry`). Barra: "Consultar SEFAZ" (filial do filtro; mostra `Message`), "Ciência", "Confirmar operação", "Desconhecer", "Operação não realizada" (diálogo com justificativa 15–255, contador), "Baixar XML", "Dar entrada". Botões habilitam pela regra da Task 4; "Dar entrada" só com uma linha `Confirmed`, não cancelada e sem entrada. Confirmações (`MessageBox.confirm`) antes de Desconhecer/Não realizada, avisando que é definitivo.

- [ ] **Step 1:** Implementar; `yarn ts-typecheck` e `yarn lint` sem erros novos.
- [ ] **Step 2:** Commit no frontend `feat(invoice): tela de manifestação do destinatário`.

### Task 7: Dar entrada a partir do DF-e

**Files:**
- Modify: `webapp/controller/receivedDfes/Main.controller.ts`, `webapp/controller/purchaseInvoices/Add.controller.ts`, `webapp/manifest.json` (padrão da rota `purchaseInvoicesAdd` aceitar `:?query:`)

"Dar entrada" navega `purchaseInvoicesAdd` com `query: { dfe: <Id> }`. No `Add.controller`, no route-matched, se houver `dfe`: chama `ReceivedDfesXml(Id=…)` e passa o resultado para o `applyImportedXml(xmlContent, fileName)` existente (avisos e mapeamento de parceiro iguais aos do upload). Sem `dfe`, comportamento atual inalterado.

- [ ] **Step 1:** Implementar; `yarn ts-typecheck` OK.
- [ ] **Step 2:** Commit `feat(invoice): entrada pelo XML da manifestação do destinatário`.

### Task 8: Verificação ponta a ponta

- [ ] Subir a stack local (backend profile `ceagui` + `yarn start:dev` do worktree, com `yarn install` próprio — nunca junction), aplicar migrations no banco DEV da CEAGUI (conferir antes `shared-local-db` — banco compartilhado com outros branches).
- [ ] Pela home → menu Fiscal → "Manifestação do Destinatário": "Consultar SEFAZ" (homologação com o certificado da filial). Se a homologação não devolver notas, verificar com o fake/seed e registrar isso.
- [ ] Manifestar Ciência e Confirmação numa nota, baixar o XML, "Dar entrada" e conferir o Documento de Entrada preenchido; voltar à lista e ver o vínculo.
- [ ] `dotnet test` das duas suítes de teste afetadas → PASS. Derrubar processos pelo PID.
