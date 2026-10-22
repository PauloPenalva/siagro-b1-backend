# Contabilidade — Fase 3 (contabilização off-line) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** tela **Contabilização Off-line** (CTBANFE / CTBANFS / FINA370) que contabiliza, por período, filial e
módulo, os eventos que ainda não têm lançamento — o histórico desde a data de corte e o que passou com o LP
desativado — com simulação, uma transação por documento e relatório de saída.

**Architecture:** um coletor por módulo (`IOfflinePostingSource`) lê as tabelas de origem e devolve
`OfflinePostingEvent`s, cada um com uma função que enfileira o lançamento usando **os mesmos context builders e o
mesmo motor da fase 2** (`IAccountingPostingService.PostAsync`). Um `OfflinePostingService` ordena os eventos por
data e, para cada documento, enfileira + `SaveChanges` (Processar) ou enfileira + descarta (Simular), limpando o
`ChangeTracker` entre documentos. A execução é um job Hangfire de uma empresa, disparado pela tela, com status e
itens gravados em `OFFLINE_POSTING_RUNS` / `_ITEMS`.

**Tech Stack:** .NET 10, EF Core SQL Server, OData v4, Hangfire, xUnit + EF InMemory, OpenUI5 TS.

**Spec:** `docs/superpowers/specs/2026-10-22-accounting-core-protheus-design.md` — seção "Contabilização off-line
(CTBANFE / CTBANFS / FINA370)" e Anti-objetivos.

**Base:** branch novo `feature/accounting-offline` criado de `feature/accounting-postings` (fase 2, não mergeada),
nos worktrees `siagro-b1-backend-accounting` e `siagro-b1-frontend-accounting`.

## Decisões deste plano

1. **Só eventos "para frente" de documentos vivos.** O off-line contabiliza confirmação/inclusão/baixa de
   documentos que continuam valendo hoje. Documento que foi confirmado **e** cancelado (ou baixa estornada,
   lançamento de conta cancelado) sem lançamento algum tem efeito líquido zero: não contabiliza e aparece no
   relatório como `Skipped` "Cancelado depois — sem efeito líquido". Motivo: o motor da fase 2 ignora estorno
   sem original vivo (Decisão 10 da spec), e a maioria dessas tabelas não guarda a data do estorno. Cancelamento
   de documento **já** contabilizado é feito pelo on-line (estorno automático mesmo com LP inativo), então nunca
   fica pendente.
2. **CMV (670/675) fora do off-line.** O CMV nasce no fechamento do estoque fiscal; reabrir e fechar o mês é o
   reprocessamento. A tela não tem o módulo.
3. **"Já contabilizado"** = o motor já devolve `null` quando existe lançamento vivo para (código, documento,
   evento); o coletor não precisa filtrar, mas filtra por `NOT EXISTS` no banco para não montar contexto à toa.
4. **Data do evento** = a mesma que o on-line usa: entrada `FiscalStockPeriodGuard.StockDate(invoice)`, saída
   `InvoiceDate`, título `DocumentDate`, baixa `SettlementDate`, lançamento `EntryDate`, transferência
   `TransferDate`. Documento sem data (ex.: entrada sem data de estoque) usa `ApprovedAt` e o item recebe a nota
   "Data do documento usada".
5. **Status que vale:** entrada/saída `Confirmed` ou `Returned`; título `Status != Canceled` e `IsPostable`;
   baixa `Amount > 0`, sem linha negativa que a estorne, origem fora de `InvoiceOffset`/`AdvanceRefund`, título
   não provisório; lançamento/transferência `CanceledAt == null`, lançamento sem `TransferKey`.

## Global Constraints

- Só STANDALONE: controller recusa com `"Disponível somente no modo STANDALONE."`; menu `StandaloneOnly = true`.
- Sem data de corte = recusa com `"Defina a data de corte da contabilidade antes da contabilização off-line."`.
  Data "de" anterior ao corte é ajustada para o corte (o motor já ignora antes do corte; o relatório não lista).
- Nunca lançamento desbalanceado, nunca em período Fechado/Bloqueado, nunca duplicado: tudo isso já é do motor e
  do `JournalEntryPostingService` — o off-line **não** reimplementa validação; só captura a exceção por documento.
- Uma transação por documento (todos os códigos do documento juntos: 650+660, 610+620). Erro num documento não
  para os demais.
- Uma execução por empresa por vez: recusa `"Já existe uma contabilização off-line em andamento."`.
- Lançamentos gravados pelo off-line têm o mesmo `Origin = StandardPosting` do on-line e `CreatedBy` = usuário que
  disparou.
- Identificadores em inglês, textos pt-BR; commits `feat(financial): …`, com `DB: <migrations>` quando houver.

## Review Focus

1. Documento cuja data cai em período contábil Fechado → item `Error` "Período contábil fechado", os demais
   seguem e nada parcial do documento fica gravado. **[RF 1]** Task 3.
2. Rodar Processar duas vezes no mesmo período → a segunda não grava nada nem lista os já contabilizados. **[RF 2]** Task 3.
3. Simular não grava nada — nem lançamento, nem numeração de documento contábil consumida. **[RF 3]** Task 3.
4. Entrada com 650 contabilizado e 660 não (660 ativado depois) → off-line grava só o 660. **[RF 4]** Task 2.
5. Filial "todas" com documento cuja filial vem do fallback (menor filial) → entra quando filtrar pela menor
   filial também. **[RF 5]** Task 2.

---

### Task 1: Expor os context builders da fase 2

**Files:** Modify `SiagroB1.Application/Services/Accounting/Postings/FinancialDocumentPosting.cs`,
`SettlementPosting.cs`, `AccountEntryPosting.cs`. Test: os testes existentes da fase 2 (sem teste novo).

**Interfaces (Produces):**
- `FinancialDocumentPosting.BuildContextAsync(AppDbContext db, FinancialDocument document, string evt, DateTime date) : Task<PostingContext>` (hoje `ContextAsync` privado).
- `SettlementPosting.BuildContextAsync(AppDbContext db, FinancialSettlement settlement, FinancialDocument document, Guid key, string evt) : Task<PostingContext>`.
- `AccountEntryPosting.BuildEntryContextAsync(AppDbContext db, FinancialAccountEntry entry, string evt, DateTime date)` e
  `BuildTransferContextAsync(AppDbContext db, FinancialAccountTransfer transfer, string evt, DateTime date)`, ambos `Task<PostingContext>`.
- `SettlementPosting.IsPostable(FinancialSettlement s, FinancialDocument d) : bool` extraído do laço atual (regra da Decisão 5).

- [ ] Renomear/tornar `public static`, trocar as chamadas internas; sem mudança de comportamento.
- [ ] `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~Accounting` verde → commit
  `refactor(financial): context builders da contabilização públicos para o off-line`.

### Task 2: Coletores de eventos por módulo

**Files:** Create em `SiagroB1.Application/Services/Accounting/Offline/`: `OfflinePostingEvent.cs`,
`IOfflinePostingSource.cs`, `PurchaseInvoiceOfflineSource.cs`, `SalesInvoiceOfflineSource.cs`,
`FinancialOfflineSource.cs`; `SiagroB1.Domain/Enums/OfflinePostingModule.cs`. Registrar os três no DI
(`ServiceCollectionExtensions`). Test `SiagroB1.Application.Tests/Accounting/Offline/OfflinePostingSourcesTests.cs`.

**Interfaces (Produces):**
- `enum OfflinePostingModule { PurchaseInvoices = 1, SalesInvoices = 2, Financial = 3 }`.
- `sealed record OfflinePostingEvent(DateTime Date, string DocumentType, string DocumentKey, string DocumentNumber,
  string BranchCode, IReadOnlyList<string> Codes, string? Note, Func<IAccountingPostingService, string, CancellationToken, Task> EnqueueAsync)`
  — `Codes` = códigos ainda sem lançamento vivo e com LP ativo; `EnqueueAsync(engine, user, ct)` monta o contexto e
  chama `PostAsync` só desses códigos.
- `interface IOfflinePostingSource { OfflinePostingModule Module { get; } IAsyncEnumerable<OfflinePostingEvent> CollectAsync(DateTime from, DateTime to, string? branchCode, CancellationToken ct); }`
  — inclui também os documentos sem efeito líquido (Decisão 1) como evento com `Codes` vazio e `Note`
  `"Cancelado depois — sem efeito líquido"`, para o relatório.
- Códigos por coletor: entradas 650/660 (`PurchaseInvoicePostingContext.BuildAsync`, evento `Confirm`); saídas
  610/620 (`SalesInvoicePostingContext`, `Confirm`); financeiro 500/510 (títulos, `Create`), 520/530 (baixas,
  `Settle`), 562/563 (lançamentos, `Create`), 560 (transferências, `Create`).
- Filtro de filial pela filial **resolvida** (`PostingBranch.ResolveAsync`), não pela coluna crua.

- [ ] Testes (InMemory, corte 01/01/2026, LPs ativos semeados como nos testes da fase 2):
  `Purchase_source_lists_confirmed_invoice_without_entry`, `Purchase_source_skips_invoice_already_posted`,
  `Purchase_source_returns_only_missing_code` **[RF 4]** (650 vivo, 660 ativo → `Codes = ["660"]`),
  `Purchase_source_reports_cancelled_invoice_as_no_net_effect`, `Sales_source_includes_returned_status`,
  `Financial_source_skips_titles_from_invoices_and_provisional`, `Financial_source_skips_reversed_settlement`,
  `Financial_source_skips_transfer_legs`, `Source_ignores_inactive_lp_code`,
  `Branch_filter_uses_resolved_branch` **[RF 5]**, `Event_without_date_uses_approved_at_with_note`.
- [ ] Rodar os testes falhando, implementar, rodar verde → commit `feat(financial): coletores da contabilização off-line`.

### Task 3: Execução (`OfflinePostingService`) + tabelas de execução

**Files:** Create `SiagroB1.Domain/Entities/OfflinePostingRun.cs`, `OfflinePostingRunItem.cs`,
`SiagroB1.Domain/Enums/OfflinePostingRunStatus.cs`, `OfflinePostingItemResult.cs`;
`SiagroB1.Application/Services/Accounting/Offline/OfflinePostingService.cs`; DbSets no `AppDbContext`; migration App
`AddOfflinePostingRuns`. Test `Accounting/Offline/OfflinePostingServiceTests.cs`.

**Interfaces (Produces):**
- `OfflinePostingRun` (`OFFLINE_POSTING_RUNS`): `Key Guid` PK, `DateFrom DATE`, `DateTo DATE`, `BranchCode VARCHAR(20)?`
  (null = todas), `Modules VARCHAR(20)` (ex. `"1,2,3"`), `Simulation bool`, `Status`, `RequestedBy VARCHAR(100)`,
  `RequestedAt`, `StartedAt?`, `FinishedAt?`, `PostedCount int`, `ErrorCount int`, `SkippedCount int`,
  `FailureMessage VARCHAR(2000)?`, `Items`.
- `OfflinePostingRunItem` (`OFFLINE_POSTING_RUN_ITEMS`): `Id`, `RunKey` FK cascade, `Sequence int` (ordem
  cronológica), `EventDate DATE`, `DocumentType VARCHAR(40)`, `DocumentKey VARCHAR(40)`, `DocumentNumber VARCHAR(60)`,
  `BranchCode`, `Codes VARCHAR(40)`, `Result`, `Message VARCHAR(2000)?`, `JournalEntryDocEntries VARCHAR(200)?`.
- `enum OfflinePostingRunStatus { Queued = 1, Running = 2, Completed = 3, Failed = 4 }`;
  `enum OfflinePostingItemResult { Posted = 1, WouldPost = 2, Error = 3, Skipped = 4 }`.
- `IOfflinePostingService`:
  - `Task<OfflinePostingRun> RequestAsync(DateTime from, DateTime to, string? branchCode, IReadOnlyList<OfflinePostingModule> modules, bool simulation, string user, CancellationToken ct)`
    — valida (STANDALONE, corte, `from <= to`, ≥1 módulo, nenhuma execução `Queued/Running`), grava `Queued` e
    enfileira o job (Task 4) **depois** do SaveChanges.
  - `Task ExecuteAsync(Guid runKey, CancellationToken ct)` — `Running`; junta os eventos de todos os módulos pedidos,
    ordena por `Date`, `DocumentType`, `DocumentNumber`; para cada um com `Codes` não vazio: `EnqueueAsync`, depois
    `SaveChangesAsync` (Processar) ou nada (Simular), e sempre `ChangeTracker.Clear()` (no erro também). Item
    `Posted`/`WouldPost` com os `DocEntry`s (Processar) ou `Error` com a mensagem da exceção (`DefaultException`
    e afins; outra exceção vira item `Error` com a mensagem, não derruba a execução). Os itens são gravados em
    lotes de 100 num contexto que só toca `OFFLINE_POSTING_*`. Ao fim `Completed` com contagens; exceção fora do
    laço → `Failed` + `FailureMessage`.
- A numeração do lançamento contábil (fase 1) precisa ser atribuída só no SaveChanges ou descartada com o
  `Clear()`; conferir em `JournalEntryPostingService.AddAsync` e registrar no ledger se precisar ajustar **[RF 3]**.

- [ ] Testes: `Process_posts_each_document_in_own_transaction`, `Simulation_writes_no_journal_entries` **[RF 3]**
  (contagem de `JournalEntries` e do contador de numeração iguais antes/depois), `Closed_period_document_is_error_others_continue` **[RF 1]**,
  `Second_process_run_finds_nothing` **[RF 2]**, `Unbalanced_lp_reports_error_with_lp_code`,
  `Request_rejects_without_cutover`, `Request_rejects_when_run_in_progress`, `Request_clamps_from_to_cutover`,
  `Items_are_in_chronological_order`, `Unexpected_exception_marks_item_error_not_run_failed`.
- [ ] Migration gerada e ensaiada nas cópias `CEAGUI_SIAGRO_DEV_ACC` (com `ASPNETCORE_ENVIRONMENT` explícito);
  commit `feat(financial): execução da contabilização off-line` + `DB: AddOfflinePostingRuns`.

### Task 4: Job Hangfire + API OData + menu

**Files:** Create `SiagroB1.Application/Jobs/OfflinePostingJob.cs`; `SiagroB1.Web/Controllers/OfflinePostingRunsController.cs`;
EDM no builder OData; migration Common `AddOfflinePostingMenu`. Test `Accounting/Offline/OfflinePostingJobTests.cs` e
`AccountingCoreEdmTests`.

**Interfaces (Produces):**
- `OfflinePostingJob.ExecuteAsync(string companyCode, Guid runKey, CancellationToken ct)` com
  `[AutomaticRetry(Attempts = 0)]` e `[DisplayName("Contabilização off-line {0}")]` — primeiro parâmetro
  `companyCode` (convenção do `CompanyJobFilter`); chama `IOfflinePostingService.ExecuteAsync`.
  Enfileirado por `IBackgroundJobClient.Enqueue<OfflinePostingJob>(j => j.ExecuteAsync(companyCode, key, CancellationToken.None))`.
- OData: `OfflinePostingRuns` GET (lista, `$orderby=RequestedAt desc`, `$expand=Items`); ação não vinculada
  `OfflinePostingRequest(DateFrom: Edm.Date, DateTo: Edm.Date, BranchCode: string?, Modules: [Edm.Int32], Simulation: bool)`
  devolvendo a execução. Datas como `DateOnly`/`.Date` (armadilha Edm.Date da fase 1). Checagem `ErpMode.IsStandalone`.
- Menu `offlinePosting` "Contabilização Off-line" no módulo Contabilidade, depois de "Lançamento Padrão",
  `StandaloneOnly`, perfil ADMIN.

- [ ] Testes: `Job_runs_in_company_scope` (o `companyCode` é extraído pelo filtro), EDM tem `OfflinePostingRuns` e
  `OfflinePostingRequest`, controller recusa em SAPB1.
- [ ] Migration Common ensaiada na cópia `CEAGUI_SIAGRO_COMMON_DEV_ACC`; commit `DB: AddOfflinePostingMenu`.

### Task 5: Frontend — tela Contabilização Off-line

**Files (repo `siagro-b1-frontend-accounting`):** rota/target `offline-posting` no `manifest.json`; Create
`webapp/view/offlinePosting/OfflinePosting.view.xml`, `webapp/controller/offlinePosting/OfflinePosting.controller.ts`,
textos no `i18n.properties`.

- Cabeçalho: De/Até (`DatePicker`, padrão 1º dia do mês corrente → hoje, valor enviado `yyyy-MM-dd`), Filial
  (Select com "Todas"), módulos (`CheckBox` Entradas, Saídas, Financeiro, todos marcados), botões **Simular** e
  **Processar** (Processar pede confirmação `MessageBox.confirm`).
- Lista das últimas execuções (tabela: data, usuário, período, Simulação/Processamento, situação, contabilizados,
  erros, ignorados). Selecionar uma mostra os itens (data, documento, nº, filial, códigos, resultado com
  `ObjectStatus` Success/Information/Error/None, mensagem) e botão **Excel** (`sap.ui.export.Spreadsheet`).
- Enquanto houver execução `Queued`/`Running`: botões desabilitados e refresh a cada 3 s (parar no `onExit` e ao
  sair da rota). Itens via fetch + JSONModel (sem navegação OData, armadilha da fase 1); enum em expressão com `%{…}`.

- [ ] `yarn ts-typecheck` e `yarn lint` limpos → commit `feat(financial): tela de contabilização off-line`.

### Task 6: Verificação ponta a ponta

Stack `ceagui` nas cópias `_ACC` (migrations aplicadas; conferir que a data de corte continua definida). Com LP 650
**inativo**, confirmar uma entrada; ativar o 650; pela tela: Simular (lista a entrada como "Contabilizaria", nenhum
lançamento novo no Razão), Processar (lançamento aparece no Razão com o histórico do LP), Processar de novo (nada).
Fechar o período do mês de um documento pendente e conferir o item com erro "Período contábil fechado". Exportar o
Excel. Derrubar a stack por PID. Revisão final do branch (`superpowers:requesting-code-review`).
