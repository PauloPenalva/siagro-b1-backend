# GAC-1226 — Vínculo do A/R Credit Memo do SAP à devolução — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Na carga, vincular (e desvincular) um A/R Credit Memo do SAP (ORIN) a um documento de devolução já confirmado, gravando só os dados fiscais.

**Architecture:** Colunas próprias `SapCreditMemoDocEntry`/`SapCreditMemoDocNum` em `SalesInvoice`; serviços novos de listagem, vínculo e desvínculo ao lado dos de venda, sem alterá-los; segundo passo no job `sap-invoice-sync`. No frontend, um diálogo novo no padrão do "Vincular NF do SAP" e uma tabela de documentos de devolução na carga.

**Tech Stack:** .NET 10, EF Core (SQL Server; InMemory nos testes), OData, xUnit; OpenUI5 + TypeScript, QUnit.

**Spec:** `docs/superpowers/specs/2026-11-17-gac-1226-sap-credit-memo-link-design.md`

## Global Constraints

- Só `Erp = SAPB1`; fora dele as três rotas respondem 400 e a tela não mostra nada novo.
- Nada é gravado no SAP nem no InvOne.
- Vínculo e desvínculo não movem saldo da carga, `ReturnedQuantity`, romaneio, liberação nem contrato.
- O fluxo de venda (OINV) em `main` não muda de comportamento; refactors ali só com os testes existentes verdes.
- Identificadores em inglês; textos de tela e mensagens em pt-BR.
- Tolerância de quantidade: `0.001`. Máximo de linhas da listagem: `200`. Modelo NF-e no SAP: `"39"`.
- Todo arquivo novo recebe `git add` na hora. Um commit por tarefa, na convenção do repo, `Refs: GAC-1226`;
  commit com migration leva `DB: AddSalesInvoiceSapCreditMemoLink`. Nunca push.
- Backend: worktree `worktrees/gac-1226-credit-memo/backend`; frontend: `.../frontend`. Branch
  `feature/gac-1226-sap-credit-memo-link` nos dois.

## Review Focus

1. DocEntry de OINV igual ao de uma ORIN vinculada: o passo de venda do job não pode tocar a devolução (teste na Tarefa 7).
2. Dois usuários vinculando o mesmo Credit Memo: o segundo recebe mensagem de negócio, não 500 (Tarefa 5).
3. SAP ou add-on fora do ar na listagem: erro de negócio (4xx), e no job nada é desvinculado (Tarefas 4 e 7).
4. Devolução com número digitado antes do vínculo: o vínculo sobrescreve e zera `WithoutTaxDocument` (Tarefa 5).
5. Campo novo em coluna de `sap.ui.table` oculta vira late property 404: tudo no `$select` explícito (Tarefa 9).

---

## Backend

### Task 1: Colunas, índice, migration e travas

**Files:**
- Modify: `SiagroB1.Domain/Entities/SalesInvoice.cs`, `SiagroB1.Infra/Context/AppDbContext.cs` (junto do índice de `SapDocEntry`)
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeLock.cs`, `SalesInvoicesItemsUpdateService.cs` (~137-143), `SalesInvoicesItemsDeleteService.cs` (~51-66), `SalesInvoicesReverseConfirmService.cs` (após a guarda de `IsNfeReturn`)
- Create: migration `AddSalesInvoiceSapCreditMemoLink` em `SiagroB1.Migrations/AppContext/`
- Test: `SiagroB1.Application.Tests/Infra/AppDbContextModelTests.cs`, `SalesInvoices/SalesInvoiceSapCreditMemoLockTests.cs` (novo; padrão dos testes de trava do vínculo SAP existentes)

**Interfaces — Produces:**
- `SalesInvoice.SapCreditMemoDocEntry : int?`, `SalesInvoice.SapCreditMemoDocNum : int?`
- `SalesInvoice.IsSapLinked => SapDocEntry != null || SapCreditMemoDocEntry != null`
- `SalesInvoicesReverseConfirmService.SapCreditMemoLinkedMessage = "Desvincule a NF de devolução do SAP antes de estornar."`

- [ ] Testes (falhando): índice único de `SapCreditMemoDocEntry` com filtro `[SapCreditMemoDocEntry] IS NOT NULL AND [InvoiceStatus] <> 2 AND [InvoiceStatus] <> 3` (valores vindos do enum, como no índice vizinho); `ResetIssuanceFields` zera as duas colunas; `RestoreIssuanceFields` restaura as duas colunas e, em devolução vinculada, número/série/chave; `EnsureHeaderEditable` recusa mudar campo de `SapLinkedHeaderFields` em devolução vinculada; `EnsureManualTaxDocument` recusa; estorno de devolução vinculada lança `DefaultException` com `SapCreditMemoLinkedMessage`.
- [ ] Implementar: colunas; índice; `IsSapLinked`; nas duas checagens `OriginalValues[SapDocEntry] is not null` do lock passar a valer "uma das duas colunas"; as duas colunas em `SapLinkFields` e `ResetIssuanceFields`; projeção e `sapLinked` dos dois serviços de item; guarda no estorno.
- [ ] `dotnet ef migrations add AddSalesInvoiceSapCreditMemoLink --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --output-dir AppContext` (conferir como as migrations vizinhas foram geradas); a migration só adiciona 2 colunas e 1 índice.
- [ ] Rodar os testes da tarefa + `~SalesInvoiceSapLink` + `~AppDbContextModel`. Commit.

### Task 2: Entidades ORIN/RIN1

**Files:**
- Create: `SiagroB1.Domain/Entities/SAP/SapCreditMemo.cs` (`[Table("ORIN")]`), `SapCreditMemoLine.cs` (`[Table("RIN1")]`)
- Modify: `SiagroB1.Infra/Context/SapErpDbContext.cs`

**Interfaces — Produces:**
- `SapCreditMemo`: mesmas propriedades e tipos de `SapInvoice` (sem `Transport`), `Lines : ICollection<SapCreditMemoLine>`.
- `SapCreditMemoLine`: as de `SapInvoiceLine` + `BaseType : int?` (`[Column("BaseType")]`), `BaseEntry : int?`, navegação `CreditMemo`.
- `SapErpDbContext.SapCreditMemos`, `SapErpDbContext.SapCreditMemoLines`; chave composta `(DocEntry, LineNum)`.

- [ ] Sem teste próprio (cobertas pelo teste de tradução da Tarefa 4). Build. Commit.

### Task 3: Add-on fiscal para Credit Memo

**Files:**
- Modify: `SiagroB1.Domain/Interfaces/ISapFiscalDocumentSource.cs`, `SiagroB1.Application/Services/SAP/InvOneFiscalDocumentSource.cs`
- Test: arquivo de testes existente do `InvOneFiscalDocumentSource` (localizar por `grep -rl InvOneFiscalDocumentSource SiagroB1.Application.Tests`)

**Interfaces — Produces:**
- `Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> ISapFiscalDocumentSource.GetForCreditMemosAsync(IReadOnlyCollection<SapInvoiceRef> creditMemos, CancellationToken ct = default)` — método com implementação padrão na interface devolvendo dicionário vazio.
- `InvOneFiscalDocumentSource.ArCreditMemoDocType = 14` (`internal const`, comentário: hipótese a confirmar na base da Yokotobi).
- `BuildQuery(db, docEntries, branches, int docType = ArInvoiceDocType)`.

- [ ] Testes (falhando): linha `DocType 14` autorizada é devolvida por `GetForCreditMemosAsync`; linha `DocType 13` com o mesmo DocEntry não vaza para o Credit Memo, nem a 14 para `GetForInvoicesAsync`.
- [ ] Implementar extraindo o corpo de `GetForInvoicesAsync` para um método privado com `docType`.
- [ ] Rodar `~InvOneFiscalDocumentSource`. Commit.

### Task 4: Listagem dos Credit Memos vinculáveis

**Files:**
- Create: `SiagroB1.Domain/Dtos/SapLinkableCreditMemoDto.cs`, `SiagroB1.Application/Services/SAP/SapLinkableCreditMemosService.cs`, `SiagroB1.Application/Services/SAP/SapTaxDocumentFormat.cs`
- Modify: `SapLinkableInvoicesService.cs` (só trocar a formatação inline pelo helper)
- Test: `SiagroB1.Application.Tests/Sap/SapLinkableCreditMemosServiceTests.cs` (padrão de `SapLinkableInvoicesServiceTests.cs`)

**Interfaces — Produces:**
- `SapTaxDocumentFormat.Number(int serial) : string` (`D9`), `SapTaxDocumentFormat.Series(string? seriesStr) : string` (trim + `PadLeft(3,'0')`).
- `SapLinkableCreditMemoDto` (todas com `[JsonPropertyName]` em PascalCase): `DocEntry, DocNum, DocDate, CardCode, CardName, TaxDocumentNumber, TaxDocumentSeries, Quantity, UnitPrice, LineTotal, Cfop, UsageCode, UsageName, AccessKey, BaseInvoiceDocEntry (int?), MatchesOrigin (bool), QuantityMatches (bool)`.
- `SapLinkableCreditMemosService(AppDbContext context, SapErpDbContext sap, ISapFiscalDocumentSource fiscal)`:
  - `Task<List<SapLinkableCreditMemoDto>> ListAsync(Guid loadKey, Guid returnInvoiceKey, DateTime dateFrom, DateTime dateTo, CancellationToken ct = default)`
  - `Task<SapLinkableCreditMemoDto?> FindAsync(Guid loadKey, Guid returnInvoiceKey, int docEntry, CancellationToken ct = default)`
  - `static IQueryable<SapCreditMemoProjection> BuildSapQuery(SapErpDbContext sap, string itemCode, int branchId, string cardCode, DateTime? from, DateTime? toExclusive, int? docEntry)`
  - `protected virtual Task<List<SapCreditMemoProjection>> ExecuteSapQueryAsync(...)` (costura de teste)
  - `const string NotReturnDocumentMessage = "O documento informado não é uma devolução confirmada desta carga."`

Regras: as da spec, seção "Credit Memo vinculável". Documento de devolução inválido (outra carga, não Return, não Confirmed, `IsNfeReturn`) → `BusinessException(NotReturnDocumentMessage)`. Quantidade esperada = soma de `Items.Quantity` do documento. `MatchesOrigin` = `BaseType == 13 && BaseEntry == origem.SapDocEntry` (origem por `SalesInvoiceOriginKey`). Ordem: `MatchesOrigin` desc, `QuantityMatches` desc, `DocDate` desc, `DocEntry` desc. Exclusão das já vinculadas por `SapCreditMemoDocEntry` em lotes de 1000 (status ≠ Cancelled/Returned), ignorando o próprio documento. SAP/add-on fora → `BusinessException` com as mesmas frases da listagem de venda.

- [ ] Testes (falhando), um por regra estragada: cancelado, modelo ≠ 39, sem Serial, outra filial, outro cliente, outro item, duas linhas, já vinculado a documento ativo (some) e a documento cancelado (aparece), não autorizado/sem info no add-on, add-on indisponível (`IsAvailable=false`) não filtra; quantidade diferente aparece com `QuantityMatches=false`; `MatchesOrigin` e ordenação; período inclui o dia final; SAP fora do ar → `BusinessException`; `BuildSapQuery(...).ToQueryString()` no provider SqlServer não lança.
- [ ] Implementar; trocar a formatação em `SapLinkableInvoicesService.ToDto`/`FindTaxDocumentNumberAsync` pelo helper.
- [ ] Rodar `~SapLinkableCreditMemos` e `~SapLinkableInvoices`. Commit.

### Task 5: Vincular e desvincular

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsLinkSapCreditMemoService.cs`, `ShipmentLoadsUnlinkSapCreditMemoService.cs`
- Modify: `SiagroB1.Application/Services/ShipmentBilling/ShipmentBillingCreateSalesInvoiceService.cs` (extrair de `ApplySapLink`)
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsLinkSapCreditMemoServiceTests.cs`, `ShipmentLoadsUnlinkSapCreditMemoServiceTests.cs`

**Interfaces:**
- Consumes: `SapLinkableCreditMemosService.FindAsync`; `SalesInvoiceTaxDocumentDuplicateGuard.EnsureUniqueAsync(branchCode, number, series, accessKey, ignoreInvoiceKey)`; `SalesInvoicesChangeLogService.Register` (ver assinatura no arquivo).
- Produces:
  - `internal static void ShipmentBillingCreateSalesInvoiceService.ApplySapFiscalFields(SalesInvoice invoice, string number, string series, string? accessKey, string? cfop, int? usageCode, string? usageName)` — `ApplySapLink` passa a gravar DocEntry/DocNum e chamar este.
  - `ShipmentLoadsLinkSapCreditMemoService.ExecuteAsync(Guid loadKey, Guid returnInvoiceKey, int sapDocEntry, string username) : Task<SalesInvoice>`
  - `ShipmentLoadsUnlinkSapCreditMemoService.ExecuteAsync(Guid loadKey, Guid returnInvoiceKey, string username, string? reason = null) : Task<SalesInvoice>`
  - Mensagens: `"Disponível somente com integração SAP Business One."`; `"Esta devolução já está vinculada a uma NF de devolução do SAP."`; `"A NF de devolução do SAP não está mais disponível para vínculo."`; `"A quantidade da NF de devolução ({0}) é diferente da quantidade da devolução ({1})."`; `"A NF de devolução do SAP já foi vinculada a outro documento."`; `"Esta devolução não está vinculada a uma NF de devolução do SAP."`

- [ ] Testes (falhando) do vínculo: grava as 2 colunas, número, série, chave, CFOP, natureza e `WithoutTaxDocument=false`; sobrescreve número digitado; registra no change log; recusa fora de SAPB1, devolução já vinculada, `FindAsync` nulo, quantidade diferente, duplicidade do guard; carga (`AvailableQuantity`, `InvoicedQuantity`, `ReturnedToWarehouseQuantity`, `Status`), `ReturnedQuantity` da origem e romaneios da carga idênticos antes e depois; `DbUpdateException` no save vira a mensagem "já foi vinculada a outro documento" (subclasse de teste do serviço ou contexto que lança).
- [ ] Testes (falhando) do desvínculo: limpa as 2 colunas, número, série e chave; mantém CFOP/natureza; log com o motivo; recusa não vinculada e fora de SAPB1; documento de outra carga → `NotFoundException`.
- [ ] Implementar (uma transação cada; `ErpMode.IsSapB1(configuration)` como no vínculo de venda).
- [ ] Rodar `~SapCreditMemo` e `~ShipmentBillingApplySapLink`. Commit.

### Task 6: Rotas OData e DI

**Files:**
- Create: `SiagroB1.Web/Functions/ShipmentLoads/ShipmentLoadsGetLinkableSapCreditMemosController.cs`, `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsLinkSapCreditMemoController.cs`, `ShipmentLoadsUnlinkSapCreditMemoController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (junto de 1671 e 1888), `SiagroB1.Web/Program.cs` (ramo SAPB1, junto da linha 133)
- Test: novo teste de EDM no padrão de `Sap/SapIntegrationSettingsEdmTests.cs`

**Interfaces — Produces:**
- `GET odata/ShipmentLoadsGetLinkableSapCreditMemos(Key={key},SalesInvoiceKey={salesInvoiceKey},DateFrom={dateFrom},DateTo={dateTo})` → coleção de `SapLinkableCreditMemoDto`.
- `POST odata/ShipmentLoadsLinkSapCreditMemo` `{ Key: Guid, SalesInvoiceKey: Guid, SapDocEntry: int }` → `{ key, invoiceNumber }`.
- `POST odata/ShipmentLoadsUnlinkSapCreditMemo` `{ Key: Guid, SalesInvoiceKey: Guid }` → `{ key, invoiceNumber }`.
- Sem serviço registrado → 400 `"Disponível somente com integração SAP Business One."`; mapeamento de exceções igual ao `ShipmentLoadsLinkSapInvoiceController`.

- [ ] Teste de EDM (falhando): as três operações existem com esses parâmetros. Implementar. `dotnet build SiagroB1.sln`. Commit.

### Task 7: Sincronização

**Files:**
- Modify: `SiagroB1.Application/Services/SAP/SapInvoiceSyncService.cs`
- Test: `SiagroB1.Application.Tests/Sap/SapInvoiceSyncServiceTests.cs`

**Interfaces — Produces:**
- `protected virtual Task<List<SapInvoiceStatus>> ReadSapCreditMemoChunkAsync(int[] docEntries, CancellationToken ct)`.
- Motivos gravados no log pelo desvínculo: `"NF de devolução cancelada no SAP."`.

Passo novo dentro do mesmo `try` de leitura (falha de SAP continua adiando a rodada inteira, sem carimbo) e laço próprio depois do de venda. Documentos: `SapCreditMemoDocEntry != null && InvoiceStatus == Confirmed`. Cancelado no SAP ou no add-on → `ShipmentLoadsUnlinkSapCreditMemoService` em `RunInCompanyScopeAsync`, usuário `SyncUser`. Ausente ou de outra filial → só aviso. Sem chave e add-on com chave → `BackfillAccessKeyAsync` (generalizar o record para servir aos dois).

- [ ] Testes (falhando): ORIN cancelada desvincula e a devolução segue `Confirmed`; cancelada só no add-on idem; ausente → intacta; outra filial → intacta; chave preenchida depois; OINV cancelada com o mesmo DocEntry de uma ORIN vinculada não toca a devolução; SAP fora do ar no passo do Credit Memo não carimba `InvoiceSyncLastRunAt`.
- [ ] Implementar. Rodar `~SapInvoiceSync`. Depois a suíte inteira de `SiagroB1.Application.Tests`. Commit.

## Frontend

### Task 8: Helpers e diálogo

**Files:**
- Create: `webapp/helpers/SapCreditMemoLinkHelpers.ts`, `webapp/test/unit/helpers/SapCreditMemoLinkHelpers.qunit.ts`, `webapp/helpers/SapCreditMemoLinkDialog.ts`, `webapp/view/shipmentLoads/fragments/SapCreditMemoLinkDialog.fragment.xml`
- Modify: o agregador de testes unitários que registra `SapInvoiceLinkHelpers.qunit`

**Interfaces — Produces:**
- `normalizeLinkableCreditMemos(result: unknown): LinkableCreditMemo[]` (aceita array ou `{ value: [...] }`)
- `isAwaitingSapReturnInvoice(row: ReturnDocumentRow, sapMode: boolean): boolean` — regra da spec
- `canLinkCreditMemo(selected: LinkableCreditMemo | null): boolean` — `QuantityMatches === true`
- `defaultReturnLinkPeriod(invoiceDate: Date | string | null, today: Date): { from: string; to: string }` (yyyy-MM-dd; sem data → 30 dias atrás)
- `sapDocNumText(row: { SapDocNum?: number | null; SapCreditMemoDocNum?: number | null }): string`
- `class SapCreditMemoLinkDialog { constructor(view: View, onLinked: () => void); open(args: { loadKey: string; returnInvoice: ReturnDocumentRow }): Promise<void> }`

Diálogo: model JSON `sapReturnLink`; uma `sap.ui.table.Table` de seleção única; `registerTableLayouts`; seleção limpa ao filtrar/ordenar; colunas e textos da spec, seção "Tela".

- [ ] QUnit (falhando) dos helpers, depois implementar; depois o diálogo. `yarn ts-typecheck`, `yarn lint`. Commit.

### Task 9: Carga e documentos de saída

**Files:**
- Modify: `webapp/view/shipmentLoads/Detail.view.xml`, `webapp/controller/shipmentLoads/Detail.controller.ts`, `webapp/view/salesInvoices/Main.view.xml`, `webapp/view/salesInvoices/fragments/Form.fragment.xml`

- [ ] Tabela `loadReturnDocumentsTable` na seção "Devoluções e Quebras da Recusa", acima dos romaneios, visível só com `ui>/sapMode`: binding `Invoices` com `$filter=InvoiceType eq 'Return' and InvoiceStatus ne 'Cancelled'` (conferir a sintaxe de enum usada nos filtros vizinhos) e `$select` explícito de todos os campos usados. Colunas: Documento, NF, Doc. SAP, Situação (selo Warning "Aguardando NF do SAP"), Emissão, Qtde, ações "Vincular NF de devolução" / "Desvincular NF de devolução" (confirmação por `MessageBox.confirm`).
- [ ] Controller: `onLinkSapCreditMemo`, `onUnlinkSapCreditMemo`, tabela nova no `refreshAll`; após vincular/desvincular, `refreshAll()` e `MessageToast`.
- [ ] `loadInvoicesTable` e lista de documentos de saída: "Doc. SAP" mostra `SapDocNum` ou `SapCreditMemoDocNum`; campos novos no `$select`. `Form.fragment.xml`: as travas `!${SapDocEntry}` passam a considerar também `SapCreditMemoDocEntry`.
- [ ] `yarn ts-typecheck`, `yarn lint`, `yarn test`. Commit.

## Verificação final

- Backend: `dotnet build SiagroB1.sln`; suíte inteira de `SiagroB1.Application.Tests`.
- Frontend: `yarn ts-typecheck`, `yarn lint`, `yarn test`.
- Navegador, nas cópias `IDX_SIAGRO_DEV_GAC1226` + `IDX_SIAGRO_COMMON_GAC1226` com a migration aplicada, roteiro da spec/plan mode (recusa → selo → vincular → travas → desvincular → cancelamento simulado → STANDALONE). Depende de o SAP da Yokotobi estar alcançável; se não estiver, registrar o que não pôde ser conferido.
