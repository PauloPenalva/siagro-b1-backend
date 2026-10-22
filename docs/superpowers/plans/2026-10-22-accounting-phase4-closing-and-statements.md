# Contabilidade — Fase 4 (apuração, fechamento, visão gerencial, DRE, Balanço, Diário) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** fechar o ciclo contábil: apuração de resultado (CTBA211) e seu estorno, travas do fechamento do
período (CTBA012), visão gerencial (CTS) com DRE e Balanço semeados, e os relatórios DRE (CTBR510), Balanço
(CTBR500) e Diário (CTBR110) em PDF + Excel.

**Architecture:** a apuração é um serviço do Application que monta um `JournalEntryDraft` por filial
(`Origin = ResultClosing`, lote `008860`) e grava pelo `JournalEntryPostingService` da fase 1 — mesmas
invariantes. O fechamento ganha duas regras no `AccountingPeriodsService`. A visão gerencial é cadastro
(`MANAGEMENT_VIEWS`/`_LINES`, upsert como o Lançamento Padrão) e um calculador puro no Reports que, sobre as
partidas do `AccountPostingReader`, resolve linhas Contas/Total/Fórmula. DRE, Balanço e Diário seguem o padrão
do Balancete (controller `POST` PDF + `POST data` JSON, service, DTO, `.frx`).

**Tech Stack:** .NET 10, EF Core SQL Server, OData v4, FastReport.OpenSource, xUnit + EF InMemory, OpenUI5 TS.

**Spec:** `docs/superpowers/specs/2026-10-22-accounting-core-protheus-design.md` — seções 2 (Fechado × Bloqueado),
6 (Apuração e fechamento), 7 (Relatórios + Visão gerencial), Menus, Decisões 2, 7, 13.

**Base:** branch novo `feature/accounting-closing` de `feature/accounting-offline` (fase 3, não mergeada), nos
worktrees `siagro-b1-backend-accounting` e `siagro-b1-frontend-accounting`.

## Decisões deste plano

1. **Saldo de resultado a apurar** = soma (débito − crédito) de todas as partidas da conta de resultado
   (`Type` Revenue/Expense) com `PostingDate <= data`, por filial + conta + CC + Item. Como os saldos são
   cumulativos desde a origem (Decisão 2) e cada apuração zera, isso é o "saldo desde a última apuração".
2. **Forma do lançamento de apuração:** linhas simples (tipo 1/2). Para cada (conta, CC, Item) com saldo ≠ 0,
   uma linha na conta de resultado com o lado que zera e o CC/Item dela; e, por conta de apuração, **uma** linha
   de contrapartida com a soma, sem CC/Item (conta de apuração costuma não aceitar CC). Histórico padrão
   `"Apuração de resultado {MM/yyyy}"` (editável na tela).
3. **Conta de apuração:** `LedgerAccount.ResultClosingAccountCode` da conta; vazia → `AccountingSettings.DefaultResultClosingAccountCode`;
   as duas vazias → recusa `"Conta {code}: informe a conta de apuração (na conta ou nas Configurações Contábeis)."`.
4. **Uma apuração por filial e data:** apurar de novo na mesma data, sem estornar, não gera nada (o saldo já é
   zero) e devolve "Nada a apurar". O rastro usa `SourceDocumentType = "ResultClosing"`,
   `SourceDocumentKey = "{yyyy-MM-dd}"`, `SourceEvent = "Close"` (o índice único da fase 1 impede duplicar).
5. **Estornar apuração:** estorna, por filial, o lançamento de apuração vivo **mais recente** (`JournalEntriesService.AddReversalAsync`,
   na data da própria apuração). Período fechado recusa pela guarda.
6. **DRE ignora lançamentos de apuração** (`Origin == ResultClosing`, e os estornos deles); o Balanço **não**
   ignora (o resultado apurado aparece no PL pela conta de apuração). Antes da apuração o Balanço mostra uma linha
   calculada "Resultado do período não apurado" no PL = saldo das contas de resultado, para o Balanço fechar.
7. **Fechar período, regra nova:** `ClosingFrequency == Monthly` e, em alguma filial, saldo de resultado ≠ 0 em
   `EndDate` → recusa `"Há resultado não apurado em {MM/yyyy} (filial {f}). Faça a apuração antes de fechar."`.
   `Yearly` só exige isso em dezembro. O aviso de off-line pendente é uma função à parte que a tela consulta antes
   de fechar (não bloqueia).
8. **Visão gerencial:** cadastro upsert por código (como `StandardPostings`). Sinal: valor da linha Contas =
   Σ(saldo débito − crédito) × (Nature Credit ? −1 : +1) × (Sign Minus ? −1 : +1) — receita (natureza credora)
   sai positiva. Total = soma das filhas (já com sinal). Fórmula = `Lxx ± Lyy …` sobre códigos da mesma visão;
   referência inexistente ou ciclo = recusa na gravação.
9. **Diário:** lançamentos do período em ordem data/lote/sublote/documento/linha; termos de abertura e
   encerramento como páginas próprias no `.frx`; folha inicial informada (`StartPage`, padrão 1) somada ao número
   de página do FastReport.

## Global Constraints

- Só STANDALONE: controllers recusam com `"Disponível somente no modo STANDALONE."`; menus `StandaloneOnly = true`, ADMIN.
- Todo lançamento passa pelo `JournalEntryPostingService.AddAsync` (balanceado, conta analítica ativa, CC/Item, período aberto).
- Apuração e estorno gravam numa transação por chamada; erro numa filial recusa a chamada inteira.
- Datas em `DATE` viram `Edm.Date` (mande `yyyy-MM-dd`); actions com data usam a data no próprio offset.
- Navegação OData dá 404 neste projeto: detalhe por `$expand` + JSONModel; enum em expression binding com `%{…}`.
- Identificadores em inglês, textos pt-BR; commits `feat(financial)` / `feat(reports)` com `DB:` quando houver migration.

## Review Focus

1. Conta de resultado com saldo em dois CCs e a conta de apuração sem aceitar CC → linhas de resultado com CC,
   contrapartida sem CC, débito = crédito. **[RF 1]** Task 1.
2. Apuração em período fechado → recusa "Período contábil … fechado" sem lançamento parcial. **[RF 2]** Task 1.
3. DRE do mês depois da apuração continua mostrando a receita (não sai zerada); Balanço fecha Ativo = Passivo + PL
   antes e depois da apuração. **[RF 3]** Tasks 4/5.
4. Fórmula com referência a linha inexistente ou ciclo (`L01 = L02`, `L02 = L01`) → recusa ao gravar a visão.
   **[RF 4]** Task 3.
5. Fechar novembro numa empresa Yearly com resultado não apurado → permite; dezembro → recusa. **[RF 5]** Task 2.

---

### Task 1: Apuração de resultado e estorno

**Files:** Create `SiagroB1.Application/Services/Accounting/ResultClosingService.cs`; Create
`SiagroB1.Web/Controllers/AccountingResultClosingController.cs`; Modify `ODataConfigurations.cs`,
`ServiceCollectionExtensions.cs`. Test `SiagroB1.Application.Tests/Accounting/ResultClosingServiceTests.cs`, EDM em `AccountingCoreEdmTests`.

**Interfaces (Produces):**
- `public sealed record ResultBalance(string BranchCode, string AccountCode, string? CostCenterCode, string? AccountingItemCode, decimal Balance)` (débito − crédito).
- `IResultClosingService`:
  - `Task<List<ResultBalance>> UnclosedAsync(DateTime date, string? branchCode, CancellationToken ct)` — saldos ≠ 0 das contas Revenue/Expense até a data (usada também pela Task 2).
  - `Task<List<JournalEntry>> CloseAsync(DateTime date, string? branchCode, string? history, string user, CancellationToken ct)` — um lançamento por filial com saldo; `[]` = nada a apurar.
  - `Task<List<JournalEntry>> ReverseLatestAsync(string? branchCode, string user, CancellationToken ct)` — estorna o mais recente vivo de cada filial; nenhum → `DefaultException("Não há apuração para estornar.")`.
- Constantes: `Batch = "008860"`, `DocumentType = "ResultClosing"`, `Event = "Close"`.
- OData actions: `AccountingResultClosingRun(Date: DateTime, BranchCode: string, History: string)` e
  `AccountingResultClosingReverse(BranchCode: string)`, ambas devolvendo coleção de `JournalEntries`; função
  `AccountingResultClosingPreview(Date, BranchCode)` devolvendo a lista de `ResultBalance` (complexo) para a tela.

- [ ] Testes (InMemory, plano com 3.1.01 Revenue Credit, 4.1.01 Expense Debit, 2.4.01 Equity de apuração, settings com Default 2.4.01):
  `Close_zeroes_each_result_account_against_closing_account`, `Close_keeps_cost_center_on_result_lines` **[RF 1]**,
  `Close_uses_account_specific_closing_account`, `Close_without_closing_account_is_refused`,
  `Close_creates_one_entry_per_branch`, `Close_again_same_date_returns_nothing`,
  `Close_in_closed_period_is_refused` **[RF 2]**, `Reverse_latest_reverses_most_recent_per_branch`,
  `Reverse_without_closing_is_refused`, `Close_ignores_balance_sheet_accounts`, EDM tem as 3 operações.
- [ ] Ver falhar, implementar, verde → commit `feat(financial): apuração de resultado e estorno`.

### Task 2: Travas do fechamento do período + aviso de off-line pendente

**Files:** Modify `SiagroB1.Application/Services/Accounting/AccountingPeriodsService.cs` (caso `Closed`);
Create função `AccountingPeriodsCloseWarnings(Id)` no controller dos períodos. Test `AccountingPeriodsServiceTests`.

**Interfaces (Consumes):** `IResultClosingService.UnclosedAsync` (Task 1), `IAccountingSettingsService`, os
`IOfflinePostingSource` da fase 3.
**Produces:** `IAccountingPeriodsService.CloseWarningsAsync(int id, CancellationToken ct) : Task<List<string>>` —
hoje uma mensagem só: `"A contabilização off-line encontraria {n} documento(s) não contabilizado(s) em {MM/yyyy}."`
(conta eventos com `Codes` não vazio dos três coletores no intervalo do período).

- [ ] Testes: `Close_refused_with_unclosed_result_monthly`, `Close_allowed_after_result_closing`,
  `Yearly_allows_november_refuses_december` **[RF 5]**, `Block_has_no_result_requirement`,
  `Warnings_count_pending_offline_documents`, `Warnings_empty_when_nothing_pending`.
- [ ] Commit `feat(financial): fechamento exige apuração e avisa off-line pendente`.

### Task 3: Visão gerencial (cadastro + semente)

**Files:** Create `SiagroB1.Domain/Entities/ManagementView.cs`, `ManagementViewLine.cs`,
`SiagroB1.Domain/Enums/ManagementViewType.cs` (`IncomeStatement = 1, BalanceSheet = 2`),
`ManagementViewLineKind.cs` (`Total = 1, Accounts = 2, Formula = 3`), `ManagementViewLineSign.cs` (`Plus = 1, Minus = 2`);
`SiagroB1.Application/Services/Accounting/ManagementViewsService.cs`; `SiagroB1.Web/Controllers/ManagementViewsController.cs`;
migrations App `AddManagementViews` (tabelas + semente `DRE` e `BP`) e Common `AddAccountingClosingMenus`
(`resultClosing` "Apuração de Resultado" 12, `managementViews` "Visão Gerencial" 13, `accountingJournalReport` "Diário" 14,
`incomeStatementReport` "DRE" 15, `balanceSheetReport` "Balanço" 16; ADMIN). Test `Accounting/ManagementViewsServiceTests.cs`.

**Interfaces (Produces):**
- `ManagementView` (`MANAGEMENT_VIEWS`): `Code VARCHAR(10)` PK, `Name VARCHAR(100)`, `Type`, `UpdatedAt`, `UpdatedBy`, `Lines`.
- `ManagementViewLine` (`MANAGEMENT_VIEW_LINES`): `Id Guid`, `ViewCode` FK cascade, `Order int`, `Code VARCHAR(10)`,
  `Description VARCHAR(100)`, `ParentCode VARCHAR(10)?`, `Kind`, `AccountFrom VARCHAR(20)?`, `AccountTo VARCHAR(20)?`,
  `Sign`, `Formula VARCHAR(200)?`, `Bold bool`, `Hidden bool`. Único (ViewCode, Code).
- `IManagementViewsService.QueryAll()`, `SaveAsync(ManagementView view, string user) : Task<ManagementView>` (substitui as linhas). Valida:
  código de linha único; `ParentCode` existe na visão e é `Total`; `Accounts` exige de/até; `Formula` só aceita
  `^\s*[A-Za-z0-9.]+(\s*[+-]\s*[A-Za-z0-9.]+)*\s*$`, referências existentes e sem ciclo **[RF 4]**.
- `public static class ManagementViewFormula { IReadOnlyList<(int Sign, string Code)> Parse(string formula); }` — usado aqui e na Task 4.
- Semente (faixas vazias, `Kind` Accounts nas folhas, Total nos grupos, Formula nos resultados):
  DRE: `01 Receita Bruta`, `02 Deduções` (Minus), `03 Receita Líquida = 01 - 02`, `04 CMV/CPV` (Minus),
  `05 Lucro Bruto = 03 - 04`, `06 Despesas Operacionais` (Minus), `07 Resultado Financeiro`,
  `08 Resultado antes do IR/CS = 05 - 06 + 07`, `09 IR/CS` (Minus), `10 Lucro Líquido = 08 - 09`.
  BP: `1 Ativo` (Total) → `1.1 Ativo Circulante`, `1.2 Ativo Não Circulante`; `2 Passivo e PL` (Total) →
  `2.1 Passivo Circulante`, `2.2 Passivo Não Circulante`, `2.3 Patrimônio Líquido`.
  (Na DRE o sinal Minus já transforma a despesa em positiva para exibição e a fórmula subtrai — ver Task 4.)

- [ ] Testes: `Save_replaces_lines`, `Rejects_formula_with_unknown_line` **[RF 4]**, `Rejects_formula_cycle` **[RF 4]**,
  `Rejects_parent_that_is_not_total`, `Accounts_line_requires_range`, `Seed_has_income_statement_and_balance_sheet`
  (lê a migration via modelo semeado), EDM tem `ManagementViews`.
- [ ] Migrations ensaiadas nas cópias `_ACC`; commit `feat(financial): visão gerencial` + `DB: AddManagementViews, AddAccountingClosingMenus`.

### Task 4: Calculador da visão + DRE

**Files:** Create `SiagroB1.Reports/Services/Accounting/ManagementViewCalculator.cs`,
`SiagroB1.Reports/Services/IncomeStatementReportService.cs`, `SiagroB1.Reports/Controllers/AccountingIncomeStatementController.cs`
(`/reports/AccountingIncomeStatement` + `/data`), DTOs em `AccountingReportDtos.cs`, template
`Reports/Templates/AccountingIncomeStatement.frx` (copiar estrutura do `AccountingTrialBalance.frx`); registrar no
`AddReportServices`. Modify `AccountPostingReader` para expor `JournalEntryOrigin`/`ReversalOfDocEntry` da partida
(campo `bool ResultClosing` em `AccountPosting`: origem ResultClosing, ou estorno de uma). Test
`SiagroB1.Application.Tests/Reports/AccountingStatementsTests.cs`.

**Interfaces (Produces):**
- `ManagementViewCalculator.Compute(IReadOnlyList<ManagementViewLine> lines, IReadOnlyDictionary<string, AccountInfo> accounts, Func<string, decimal> signedBalanceOfAnalytic) : Dictionary<string, decimal>`
  — `AccountInfo(string Code, bool Analytic, LedgerAccountNature? Nature)`; regras da Decisão 8; faixa Contas compara
  código ordinal (de ≤ código ≤ até) só nas analíticas.
- `IncomeStatementReportRequest { DateFrom, DateTo, ViewCode, BranchCode?, ByCostCenter bool }`;
  linha `IncomeStatementRowDto { Code, Description, Level, Bold, Period, YearToDate, PercentOfRevenue, Dictionary<string, decimal>? ByCostCenter }`.
  Acumulado = 01/01 do ano de `DateTo` até `DateTo`. % = Period / valor da primeira linha `Accounts`/`Total` de nível 1
  (a "Receita Bruta" da semente; 0 → vazio). Por CC: colunas por CC com movimento + "Sem centro" + "Total"; no PDF o
  modo por CC usa as mesmas colunas até 6 CCs (além disso só o Excel).
- Linhas `Hidden` não saem no relatório mas entram nas fórmulas.

- [ ] Testes: `Calculator_signs_revenue_positive_and_expense_by_sign`, `Calculator_total_sums_children`,
  `Calculator_formula_resolves`, `Income_statement_ignores_result_closing` **[RF 3]**,
  `Income_statement_year_to_date_and_percent`, `Income_statement_by_cost_center_has_no_center_column`,
  PDF gera (padrão de `AccountingReportsPdfTests`).
- [ ] Commit `feat(reports): DRE pela visão gerencial`.

### Task 5: Balanço Patrimonial

**Files:** Create `SiagroB1.Reports/Services/BalanceSheetReportService.cs`, `Controllers/AccountingBalanceSheetController.cs`
(`/reports/AccountingBalanceSheet` + `/data`), DTO, `Reports/Templates/AccountingBalanceSheet.frx`. Test no mesmo arquivo da Task 4.

**Interfaces:** `BalanceSheetReportRequest { Date, CompareDate?, ViewCode, BranchCode? }`; linha
`BalanceSheetRowDto { Code, Description, Level, Bold, Balance, CompareBalance? }`. Saldo até a data (cumulativo,
inclui apuração). Linha extra, não cadastrada, `"Resultado do período não apurado"` (Decisão 6): só quando ≠ 0,
valor = saldo das contas de resultado com sinal credor (lucro positivo), exibida como última filha da **segunda**
linha Total de nível 1 (na semente, `2 Passivo e PL`), cujo total passa a incluí-la.

- [ ] Testes: `Balance_sheet_balances_before_and_after_result_closing` **[RF 3]** (ativo = passivo + PL nos dois
  momentos), `Balance_sheet_compare_column`, `Unclosed_result_line_only_when_not_zero`, PDF gera.
- [ ] Commit `feat(reports): Balanço Patrimonial pela visão gerencial`.

### Task 6: Diário

**Files:** Create `SiagroB1.Reports/Services/JournalReportService.cs`, `Controllers/AccountingJournalController.cs`
(`/reports/AccountingJournal` + `/data`), DTO, `Reports/Templates/AccountingJournal.frx` (página de termo de abertura,
página de dados, página de termo de encerramento). Test no mesmo arquivo.

**Interfaces:** `JournalReportRequest { DateFrom, DateTo, BranchCode?, StartPage int = 1, BookNumber int = 1 }`; linha
`JournalRowDto { Date, Batch, SubBatch, DocumentNumber, LineNumber, Account, AccountName, Debit, Credit, History, CostCenter, Item }`
(linha tipo 3 vira duas linhas, débito e crédito; tipo 4 junta no histórico). Termos: empresa (nome, CNPJ da filial ou
matriz), período, número do livro e "Este livro contém {n} folhas numeradas de {StartPage} a {StartPage+n−1}" (n = total
de páginas do relatório preparado: gerar duas vezes ou usar `[TotalPages#]` do FastReport — o construtor escolhe e
registra a escolha). Totais do período: Σ débito = Σ crédito.

- [ ] Testes: `Journal_orders_by_date_batch_document_line`, `Journal_double_line_becomes_two_rows`,
  `Journal_totals_balance`, `Journal_start_page_offsets_numbering` (parâmetro passado ao relatório), PDF gera.
- [ ] Commit `feat(reports): Diário com termos de abertura e encerramento`.

### Task 7: Frontend

Repo `siagro-b1-frontend-accounting`, branch `feature/accounting-closing`. Seguir as telas existentes de relatório
contábil (`reports/accountingTrialBalance` + fragmento `AccountingReportFilters`) e do Lançamento Padrão.
- **Apuração de Resultado** (`result-closing`): data (padrão último dia do mês anterior), filial (Todas), histórico;
  botão **Prévia** (tabela da `AccountingResultClosingPreview`), **Apurar** (confirmação), **Estornar última apuração**
  (confirmação); mostra os lançamentos gerados (DocEntry, filial, data, documento).
- **Visão Gerencial** (`management-views` lista; `management-views/{code}` edição): grade JSONModel das linhas com Ordem,
  Código, Descrição, Superior, Tipo (Select Total/Contas/Fórmula), Conta de/até (value help de conta), Sinal, Fórmula,
  Negrito, Oculta; gravar = POST upsert.
- **DRE** (`income-statement-report`), **Balanço** (`balance-sheet-report`), **Diário** (`journal-report`): filtros, Imprimir
  (PDF) e Excel (de `/data`).
- **Calendário Contábil**: antes de Fechar, chamar `AccountingPeriodsCloseWarnings`; havendo aviso, `MessageBox.confirm` com
  o texto; mensagens de recusa do backend aparecem como erro.
- Rotas + targets no `manifest.json`, rotas no `ServerRoutes.ts`. Keys das rotas = keys dos menus da Task 3.
- [ ] `yarn ts-typecheck` e `yarn lint` limpos → commit `feat(financial): telas de apuração, visão gerencial, DRE, Balanço e Diário`.

### Task 8: Verificação ponta a ponta

Stack `ceagui` nas cópias `_ACC` com Web + Gateway + **Reports**. Pelo menu: preencher as faixas da DRE semeada
com as contas do plano de teste; ver a prévia da apuração de outubro; tentar fechar outubro (recusa por resultado não
apurado); apurar; DRE de outubro (receita continua); Balanço fecha; fechar outubro (aviso de off-line, se houver);
estornar apuração com o mês fechado (recusa); Diário de outubro em PDF; Excel de cada relatório. Derrubar a stack
por PID. Revisão final do branch.
