# Contabilidade — Fase 1 (núcleo manual) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** contabilidade manual utilizável em STANDALONE: plano de contas ampliado, calendário com
fechar/bloquear/reabrir, configurações contábeis (data de corte, conta de apuração), lançamento contábil
no molde da CT2 com inclusão manual e estorno, guarda de período, Razão e Balancete (tela + PDF + Excel).

**Architecture:** entidades novas no `AppDbContext` (banco da empresa); um único serviço de gravação
(`JournalEntryPostingService`) concentra os invariantes e será reutilizado pela contabilização automática
nas fases 2 e 3; saldos calculados das linhas (sem tabela de saldo); relatórios no `SiagroB1.Reports`
seguindo o padrão do extrato da conta financeira.

**Tech Stack:** .NET 10, EF Core SQL Server, OData v4 (`ODataConfigurations.cs` + controllers
`Actions/`), xUnit + EF InMemory (`TestDb`), FastReport OpenSource, OpenUI5 TS.

**Spec:** `docs/superpowers/specs/2026-10-22-accounting-core-protheus-design.md` (seções 1–4, 7 parcial,
Decisões 2, 9, 11, 13). As fases 2–4 terão planos próprios depois que esta for mergeada.

**Worktrees:** backend `C:\Projetos\SiagroB1\siagro-b1-backend-accounting` (branch
`feature/accounting-core`, já existe). Frontend: criar `C:\Projetos\SiagroB1\siagro-b1-frontend-accounting`
com `git -C C:/Projetos/SiagroB1/siagro-b1-frontend worktree add ../siagro-b1-frontend-accounting -b feature/accounting-core main`
e rodar `yarn install` **nele** (não usar junction de node_modules — ver memória
worktree-remove-follows-node-modules-junction). Copiar `SiagroB1.Reports/wwwroot` do repo principal para o
worktree antes de rodar testes de relatório (memória worktree-missing-untracked-report-assets).

## Global Constraints

- Só STANDALONE: controllers recusam com `"Disponível somente no modo STANDALONE."` quando
  `!ErpMode.IsStandalone(configuration)`; menus com `StandaloneOnly = true`.
- Identificadores em inglês; textos de tela, mensagens de erro e comentários em pt-BR.
- Migrations aditivas, uma `App` e uma `Common`, aplicadas em todas as empresas; commit com trailer
  `DB: <NomeDaMigration>`.
- Commits `tipo(escopo): ...` em pt-BR, escopo `financial` (backend) — sem push, sem merge.
- `AllowsPosting` **é** a classe da conta (true = analítica, CT1_CLASSE 2; false = sintética). Não criar
  coluna `Class` nova (decisão do plano: a coluna já existe com essa semântica).
- Valores monetários `decimal(19,2)`; histórico por linha `VARCHAR(40)`.
- Toda gravação de lançamento passa por `JournalEntryPostingService`; não há PATCH/DELETE de lançamento.
- Sem período cadastrado para a data = aberto (Decisão 9).
- Mensagem padrão de período: `"Período contábil {MM/yyyy} fechado."` / `"... bloqueado."`.

## Review Focus

1. Conta pai que gera ciclo (A→B→A) ou pai analítico — o cadastro deve recusar; teste na Task 1.
2. Lançamento cujo débito ≠ crédito por 0,01 por arredondamento na tela — servidor recusa; teste na Task 4.
3. Estornar um lançamento já estornado, ou estornar um estorno — recusa; teste na Task 5.
4. Razão de conta sintética ou de faixa de contas com saldo anterior antes da data de corte — saldo
   anterior soma tudo antes da data inicial, incluindo o lançamento de abertura; teste na Task 7.
5. Reabrir período por usuário sem papel ADMIN (via perfil) — recusa usando `HasRoleAsync`, não
   `IsAdmin`; teste na Task 3.

---

### Task 1: Plano de contas ampliado

**Files:**
- Modify: `SiagroB1.Domain/Entities/LedgerAccount.cs`, `SiagroB1.Domain/Enums/LedgerAccountType.cs`,
  `SiagroB1.Domain/Models/LedgerAccountModel.cs`, `SiagroB1.Application/Services/LedgerAccountService.cs`
- Create: `SiagroB1.Domain/Enums/LedgerAccountNature.cs`
- Test: `SiagroB1.Application.Tests/Accounting/LedgerAccountHierarchyTests.cs`

**Interfaces:**
- Produces: `LedgerAccount.ParentCode string? (VARCHAR 20)`, `Nature LedgerAccountNature? {Debit=1, Credit=2}`,
  `ResultClosingAccountCode string?`, `ReferentialCode string? (VARCHAR 30)`, `BlockedFrom/BlockedTo DateTime?`;
  `LedgerAccountType.Equity = 5` (manter os valores numéricos atuais);
  `static bool LedgerAccountRules.IsPostable(LedgerAccount a, DateTime date)` em
  `SiagroB1.Application/Services/Accounting/LedgerAccountRules.cs` (analítica, ativa, fora do bloqueio).

- [ ] **Step 1: testes falhando** — `Rejects_parent_cycle`, `Rejects_analytic_parent` (pai com
  `AllowsPosting=true` → "A conta superior {X} precisa ser sintética."), `Rejects_turning_synthetic_with_children_into_analytic`,
  `Nature_defaults_from_type` (Asset/Expense→Debit, Liability/Equity/Revenue→Credit quando não informada),
  `ResultClosingAccount_only_on_revenue_or_expense`, `IsPostable_false_inside_block_range`.
- [ ] **Step 2:** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~LedgerAccountHierarchyTests"` → FAIL (compilação).
- [ ] **Step 3:** implementar campos, enum, `LedgerAccountRules` e as validações no `CreateAsync/UpdateAsync`
  do `LedgerAccountService`; propagar no `LedgerAccountModel` e no `QueryAll`.
- [ ] **Step 4:** testes PASS + `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Accounting"` sem regressão.
- [ ] **Step 5:** commit (migration vem na Task 2).

### Task 2: Calendário, configurações, lançamento — entidades e migration

**Files:**
- Create: `SiagroB1.Domain/Entities/AccountingPeriod.cs`, `AccountingSettings.cs`, `JournalEntry.cs`,
  `JournalEntryLine.cs`; `SiagroB1.Domain/Enums/AccountingPeriodStatus.cs`, `JournalEntryOrigin.cs`,
  `JournalEntryLineType.cs`
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs`
- Create: migration App `AddAccountingCore` (`dotnet ef migrations add AddAccountingCore --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`)
- Test: `SiagroB1.Application.Tests/Accounting/AccountingCoreSchemaTests.cs`

**Interfaces (Produces):**
- `AccountingPeriod` (`ACCOUNTING_PERIODS`): `Id int`, `Year`, `Month`, `StartDate`, `EndDate`,
  `Status AccountingPeriodStatus {Open=1, Closed=2, Blocked=4}`, `ClosedAt/ClosedBy`, `ReopenedAt/ReopenedBy`,
  `ReopenReason VARCHAR(500)`. Único (Year, Month).
- `AccountingSettings` (`ACCOUNTING_SETTINGS`, `Key=1`, modelo `EmailSettings`): `CutoverDate DateTime?`,
  `DefaultResultClosingAccountCode string?`, `ClosingFrequency {Monthly=1, Yearly=2}` (default Monthly),
  `UpdatedAt/UpdatedBy`.
- `JournalEntry` (`JOURNAL_ENTRIES`): `DocEntry int identity PK`, `PostingDate date`, `BranchCode VARCHAR(10)`,
  `Batch VARCHAR(6)`, `SubBatch VARCHAR(3)` default "001", `DocumentNumber VARCHAR(6)`,
  `Origin JournalEntryOrigin {Manual=1, StandardPosting=2, ResultClosing=3, Reversal=4}`,
  `StandardPostingCode VARCHAR(3)?`, `SourceDocumentType VARCHAR(40)?`, `SourceDocumentKey VARCHAR(60)?`,
  `SourceEvent VARCHAR(20)?`, `ReversalOfDocEntry int?`, `ReversedByDocEntry int?`, `Comments VARCHAR(250)?`,
  `CreatedAt`, `CreatedBy`, `Lines List<JournalEntryLine>`.
  Índices: único (PostingDate, Batch, SubBatch, DocumentNumber); único filtrado
  (StandardPostingCode, SourceDocumentType, SourceDocumentKey, SourceEvent) `WHERE SourceDocumentKey IS NOT NULL AND ReversalOfDocEntry IS NULL`
  (usar `HasFilter` com a string SQL; lembrar QUOTED_IDENTIFIER — memória filtered-index-requires-quoted-identifier;
  sem `NOT IN` no filtro).
- `JournalEntryLine` (`JOURNAL_ENTRY_LINES`): `Id`, `DocEntry` FK cascade, `LineNumber`,
  `EntryType JournalEntryLineType {Debit=1, Credit=2, Double=3, HistoryContinuation=4}`,
  `DebitAccountCode/CreditAccountCode VARCHAR(20)?` (FK LEDGER_ACCOUNTS, NoAction),
  `Amount decimal(19,2)`, `History VARCHAR(40)`, `DebitCostCenterCode/CreditCostCenterCode`,
  `DebitAccountingItemCode/CreditAccountingItemCode`, `StandardPostingSequence VARCHAR(3)?`,
  `BusinessPartnerCode VARCHAR(50)?`. Índices (DebitAccountCode), (CreditAccountCode) com include
  de Amount; o filtro por data vem pelo join com o cabeçalho.
- Migration também adiciona as colunas da Task 1 em `LEDGER_ACCOUNTS` e preenche `Nature` pelo `Type`
  (`UPDATE ... SET Nature = CASE Type WHEN ... END`), sem mexer em `AllowsPosting`.

- [ ] **Step 1:** teste `Model_has_unique_document_index_and_source_index` (lê `db.Context.Model` e confere
  os índices e o tipo `decimal(19,2)`), padrão de `AccountingSchemaTests.cs`.
- [ ] **Step 2:** FAIL. **Step 3:** entidades + `DbSet`s + configuração fluente + migration; conferir o SQL
  gerado (`dotnet ef migrations script --idempotent` só da migration) — índice filtrado, FKs NoAction.
- [ ] **Step 4:** PASS; `dotnet build SiagroB1.sln` sem warnings novos; `dotnet ef migrations has-pending-model-changes`
  → nenhuma.
- [ ] **Step 5:** ensaiar `database update` numa **cópia** de `CEAGUI_SIAGRO_DEV` (ex.: `CEAGUI_SIAGRO_DEV_ACC`
  via backup/restore) com `ConnectionStrings__SiagroDB` apontando para a cópia; nunca no banco do usuário.
- [ ] **Step 6:** commit com `DB: AddAccountingCore`.

### Task 3: Calendário contábil — serviço, guarda e actions

**Files:**
- Create: `SiagroB1.Application/Services/Accounting/AccountingPeriodsService.cs` (+ `IAccountingPeriodsService.cs`),
  `SiagroB1.Infra/Accounting/AccountingPeriodGuard.cs`, `SiagroB1.Web/Controllers/AccountingPeriodsController.cs`,
  `SiagroB1.Web/Actions/Accounting/AccountingPeriodsActionsController.cs`
- Modify: `ODataConfigurations.cs`, `ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/Accounting/AccountingPeriodsServiceTests.cs`, `AccountingPeriodGuardTests.cs`

**Interfaces (Produces):**
- `AccountingPeriodGuard(IUnitOfWork db)`: `Task EnsureOpenAsync(DateTime date, CancellationToken ct = default)`
  (lança `DefaultException` com a mensagem global); `Task<DateTime?> FirstOpenDateOnOrAfterAsync(DateTime date, CancellationToken ct = default)`
  (usada na fase 2 pela exceção de eventos externos).
- `IAccountingPeriodsService`: `IQueryable<AccountingPeriod> QueryAll()`;
  `Task<List<AccountingPeriod>> GenerateYearAsync(int year, string user)` (cria os 12 meses que faltam, Open);
  `Task<AccountingPeriod> SetStatusAsync(int id, AccountingPeriodStatus status, string user, string? reason)`.
- Actions OData: `AccountingPeriodsGenerateYear(Year)`, `AccountingPeriodsClose(Id)`, `AccountingPeriodsBlock(Id)`,
  `AccountingPeriodsReopen(Id, Reason)`.

Regras: fechar exige mês anterior (se existir no calendário) já Fechado — "Feche antes o período {MM/yyyy}.";
reabrir exige `reason` não vazio, só papel ADMIN via `IUserPermissions.HasRoleAsync(user, "ADMIN")`
(conferir o nome exato do método na memória admin-role-vs-isadmin-flag), e recusa se o mês seguinte estiver
Fechado; Blocked → Open não exige motivo. (Pendência de apuração no fechamento entra na fase 4.)

- [ ] **Step 1:** testes `Guard_allows_date_without_period`, `Guard_rejects_closed`, `Guard_rejects_blocked`,
  `Close_requires_previous_closed`, `Reopen_requires_reason_and_admin_role`, `Reopen_rejects_when_next_closed`,
  `GenerateYear_is_idempotent`.
- [ ] **Step 2:** FAIL. **Step 3:** implementar; registrar no DI e no EDM. **Step 4:** PASS. **Step 5:** commit.

### Task 4: `JournalEntryPostingService` — gravação com invariantes + configurações

**Files:**
- Create: `SiagroB1.Application/Services/Accounting/JournalEntryPostingService.cs` (+ interface),
  `JournalEntryDraft.cs` (DTO de entrada), `JournalEntryNumbering.cs`,
  `AccountingSettingsService.cs` (+ interface), `SiagroB1.Web/Controllers/AccountingSettingsController.cs`
- Test: `SiagroB1.Application.Tests/Accounting/JournalEntryPostingServiceTests.cs`, `AccountingSettingsServiceTests.cs`

**Interfaces (Produces):**
- `JournalEntryDraft { DateTime PostingDate; string BranchCode; string Batch; string SubBatch = "001";
  JournalEntryOrigin Origin; string? StandardPostingCode; string? SourceDocumentType; string? SourceDocumentKey;
  string? SourceEvent; int? ReversalOfDocEntry; string? Comments; List<JournalEntryLineDraft> Lines }`;
  `JournalEntryLineDraft { JournalEntryLineType EntryType; string? DebitAccountCode; string? CreditAccountCode;
  decimal Amount; string History; string? DebitCostCenterCode; ... ; string? StandardPostingSequence; string? BusinessPartnerCode }`
  — `History` pode ter mais de 40 caracteres: o serviço quebra em linhas tipo 4.
- `IJournalEntryPostingService.Task<JournalEntry> AddAsync(JournalEntryDraft draft, string user, CancellationToken ct = default)`
  — **enqueue-only**: valida, numera, faz `Add`, **não** chama `SaveChanges` (o chamador grava; isso é o que
  a fase 2 precisa). Invariantes da spec §3, mensagens pt-BR citando a linha (`"Linha {n}: ..."`) e, quando
  presente, `"LP {code} seq. {seq}: ..."`.
- `JournalEntryNumbering.NextAsync(DateTime date, string batch, string subBatch)` → `"000001"`… máximo
  existente + 1 incluindo os `Added` do ChangeTracker; colisão protegida pelo índice único (o chamador
  repete uma vez em `DbUpdateException` de chave duplicada).
- `IAccountingSettingsService`: `Task<AccountingSettings> GetAsync()`, `Task<AccountingSettings> SaveAsync(AccountingSettings s, string user)`
  (valida conta de apuração analítica e do tipo Equity ou Liability).

- [ ] **Step 1:** testes: `Rejects_unbalanced_by_one_cent`, `Rejects_synthetic_account`, `Rejects_blocked_account_on_date`,
  `Rejects_cost_center_on_account_that_does_not_accept`, `Requires_cost_center_when_account_requires`
  (reusar `AccountingEntityRules` para vigência/classe de CC e Item), `Rejects_zero_amount_on_value_line`,
  `Splits_long_history_into_type4_lines`, `Double_entry_line_counts_both_sides`, `Rejects_closed_period`,
  `Numbers_sequentially_per_date_batch_subbatch`, `Does_not_save_changes` (usar `CountingUnitOfWork`).
- [ ] **Step 2:** FAIL. **Step 3:** implementar. **Step 4:** PASS. **Step 5:** commit.

### Task 5: Lançamento manual e estorno — serviços e API

**Files:**
- Create: `SiagroB1.Application/Services/Accounting/JournalEntriesService.cs` (+ interface),
  `SiagroB1.Web/Controllers/JournalEntriesController.cs`, `SiagroB1.Web/Actions/Accounting/JournalEntriesActionsController.cs`
- Test: `SiagroB1.Application.Tests/Accounting/JournalEntriesServiceTests.cs`

**Interfaces (Produces):**
- `IJournalEntriesService`: `IQueryable<JournalEntry> QueryAll()` (com `Lines` expansível),
  `Task<JournalEntry> CreateManualAsync(JournalEntryDraft draft, string user)` (força `Origin=Manual`, lote
  padrão `"008890"` quando vazio, `SaveChanges`),
  `Task<JournalEntry> ReverseAsync(int docEntry, DateTime date, string user)` (cria `Origin=Reversal`, lados
  e CC/Item trocados, mesmo lote, histórico `"Estorno doc {Batch}/{DocumentNumber}"`, vincula os dois).
- `Task<JournalEntry> ReverseAsync` também será chamado pela fase 2 (estorno automático) — por isso a
  versão enqueue-only `Task<JournalEntry> AddReversalAsync(JournalEntry original, DateTime date, string user)`
  fica pública no mesmo serviço e o `ReverseAsync` só a envolve com `SaveChanges`.
- OData: `JournalEntries` (GET com `$expand=Lines`, POST = manual); action `JournalEntriesReverse(DocEntry, Date)`.
  Sem PATCH/DELETE (controller não expõe; teste de EDM confere).

- [ ] **Step 1:** testes `Manual_entry_saved_with_default_batch`, `Reverse_swaps_sides_and_links`,
  `Cannot_reverse_twice`, `Cannot_reverse_a_reversal`, `Reverse_rejects_closed_period_on_reversal_date`,
  `Edm_has_no_patch_or_delete_for_JournalEntries`.
- [ ] **Step 2:** FAIL. **Step 3:** implementar. **Step 4:** PASS. **Step 5:** commit.

### Task 6: Menus do módulo (Common)

**Files:**
- Create: migration Common `AddAccountingCoreMenus` (padrão de `20261021223654_AddAccountingModuleMenus.cs`)
- Test: `SiagroB1.Application.Tests/Accounting/AccountingMenusMigrationTests.cs` (se existir teste análogo para
  menus; senão conferir via script SQL no ensaio)

Itens sob `accounting`, `StandaloneOnly=true`, concedidos ao ADMIN: `accountingPeriods` "Calendário Contábil",
`accountingSettings` "Configurações Contábeis", `journalEntries` "Lançamentos Contábeis",
`ledgerReport` "Razão", `trialBalanceReport` "Balancete". (Fases seguintes acrescentam os demais.)

- [ ] **Step 1–4:** gerar a migration, ensaiar na cópia `CEAGUI_SIAGRO_COMMON_DEV_ACC`, conferir `SELECT` dos itens.
- [ ] **Step 5:** commit com `DB: AddAccountingCoreMenus`.

### Task 7: Saldos, Razão e Balancete (Reports)

**Files:**
- Create: `SiagroB1.Reports/Services/Accounting/AccountBalanceReader.cs`,
  `SiagroB1.Reports/Dtos/LedgerReportDtos.cs`, `TrialBalanceReportDtos.cs`,
  `SiagroB1.Reports/Services/LedgerReportService.cs`, `TrialBalanceReportService.cs`,
  `SiagroB1.Reports/Controllers/LedgerReportController.cs` (`/reports/AccountingLedger`),
  `TrialBalanceReportController.cs` (`/reports/AccountingTrialBalance`),
  `SiagroB1.Reports/Templates/AccountingLedger.frx`, `AccountingTrialBalance.frx`
- Test: `SiagroB1.Application.Tests/Reports/AccountingLedgerReportTests.cs`, `AccountingTrialBalanceReportTests.cs`
  (padrão `RecordingFastReportService`)

**Interfaces (Produces):**
- `AccountBalanceReader.MovementsAsync(DateTime from, DateTime to, string? branch, string? accountFrom, string? accountTo, string? costCenter, string? item)`
  → linhas por (conta, lado) com débito/crédito; `OpeningAsync(DateTime before, ...)` → saldo anterior por
  conta. Linha tipo 3 conta no débito da conta de débito e no crédito da conta de crédito; tipo 4 ignorada.
  Lançamentos `Origin=ResultClosing` entram no Razão/Balancete (só a DRE os ignora, fase 4).
- Saldo com sinal pela `Nature` da conta: exibido como valor absoluto + "D"/"C".
- Balancete: parâmetros `level int?` (nível = quantidade de segmentos do código separados por `.`; sintéticas
  somam as filhas via `ParentCode`, não pelo prefixo), `onlyWithMovement bool`. Totais de débito e crédito
  do período iguais (assert no teste).
- Excel: o frontend gera a partir dos mesmos endpoints com `format=json` (DTO de resposta) — padrão do
  relatório que já exporta Excel no projeto (conferir `contract-list-balance-column-and-excel-export`);
  se o padrão for exportar no cliente a partir da lista, o endpoint aceita `?format=json`.

- [ ] **Step 1:** testes: `Ledger_opening_includes_everything_before_start`, `Ledger_running_balance_sign_by_nature`,
  `Ledger_double_entry_line_shows_counterpart`, `TrialBalance_totals_debit_equals_credit`,
  `TrialBalance_synthetic_sums_children_by_parent`, `TrialBalance_level_filter`, `Filters_by_branch_and_cost_center`.
- [ ] **Step 2:** FAIL. **Step 3:** implementar serviços e `.frx` (abrir um `.frx` existente como base;
  conferir render local — memória render-report-template-locally). **Step 4:** PASS. **Step 5:** commit.

### Task 8: Frontend — Plano de Contas ampliado

**Files (frontend worktree, `webapp/`):** `view/ledgerAccounts/{Main,Add,Edit}.view.xml` e controllers;
`i18n/i18n.properties`.

Campos novos no formulário (Conta superior por value help filtrado a sintéticas; Natureza Select;
Tipo com "Patrimônio Líquido"; Conta de apuração só visível para Receita/Despesa; Código referencial;
Bloqueio de/até). Lista ganha colunas Natureza e Conta superior e alterna para `sap.ui.table.TreeTable`
por `ParentCode` (botão "Árvore/Lista"). Incluir no `create()` inicial **todas** as propriedades editadas
(`Nature: null`, `ParentCode: null`…) e `targetType: 'any'` nos formatters de enum/booleano (memória
cost-center-ledger-account-dual-mode-feature).

- [ ] **Step 1:** `yarn ts-typecheck` e `yarn lint` limpos. **Step 2:** verificação no navegador (Task 11).
- [ ] **Step 3:** commit `feat(financial): plano de contas com conta superior, natureza e apuração` no repo do frontend.

### Task 9: Frontend — Calendário Contábil e Configurações Contábeis

Rotas `accounting-periods` e `accounting-settings` no `manifest.json`; Calendário = lista por exercício
(Select de ano + botão "Gerar exercício"), colunas Mês/Início/Fim/Situação (ObjectStatus), ações
Fechar/Bloquear/Reabrir (diálogo com motivo). Configurações = formulário único (Data de corte, Conta de
apuração padrão com value help analítica, Periodicidade da apuração Mensal/Anual).

- [ ] typecheck + lint; commit.

### Task 10: Frontend — Lançamentos Contábeis, Razão e Balancete

- **Lista** `journal-entries` com FilterBar (data de/até, lote, documento, origem, conta, filial) e
  colunas Data, Lote, Doc, Origem, Histórico (1ª linha), Valor (soma dos débitos), Estornado.
- **Detalhe** ObjectPage `journal-entries/{docEntry}`: cabeçalho, tabela de linhas (tipo 4 renderizada como
  continuação do histórico), ação Estornar (diálogo com data; padrão hoje).
- **Inclusão** `journal-entries/new`: grade editável (tipo, débito, crédito, valor, histórico, CC D/C,
  Item D/C) com value helps existentes; rodapé Débito/Crédito/Diferença em JSONModel; Gravar desabilitado
  com diferença ≠ 0; POST único com `Lines` (deep insert) ou action — o que o controller da Task 5 expuser.
- **Razão** e **Balancete** em `reports/accountingLedger` e `reports/accountingTrialBalance`, padrão de
  `controller/reports/financialAccountStatement/Main.controller.ts` (filtros + PDF) + botão Excel.
- [ ] typecheck + lint; commit.

### Task 11: Verificação ponta a ponta

- [ ] Subir a stack `ceagui` **apontando para as cópias** `_ACC` (env `ConnectionStrings__SiagroDB` /
  `__SiagroCommon`; a COMPANIES da cópia Common precisa apontar para a cópia App — ajustar a linha 0001 e
  remover a 0002 na cópia) com `yarn start:dev` do worktree do frontend. Gateway antes do Web.
- [ ] Pelo **menu** (não por URL): cadastrar 1.1 (sintética) → 1.1.01 Caixa (analítica, devedora);
  3.1 Receita (credora); 2.4.01 Resultado do exercício (PL); configurar corte e conta de apuração;
  gerar 2026; lançar manual 1.1.01 × 3.1 R$ 100,00 com CC; tentar gravar com diferença (botão desabilitado);
  estornar; tentar estornar de novo (erro); fechar 09/2026 e tentar lançar em 09/2026 (erro); Razão e
  Balancete em PDF e Excel batendo.
- [ ] Derrubar a stack por PID nas portas 50000/5246/8081/8080; apagar as cópias `_ACC` só se o usuário pedir.
- [ ] `dotnet test SiagroB1.Application.Tests` completo verde; registrar contagem.
