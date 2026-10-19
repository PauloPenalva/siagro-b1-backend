# Lançamentos, Reconciliação e Extrato de Conta Financeira — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dar ao financeiro (STANDALONE e SAPB1) uma tela de lançamentos por conta financeira — baixas + lançamentos manuais —, a reconciliação desses lançamentos contra o extrato do banco e a impressão do extrato da conta.

**Architecture:** O ledger de baixas (`FINANCIAL_SETTLEMENTS`) continua sendo a fonte dos movimentos de título; uma tabela nova `FINANCIAL_ACCOUNT_ENTRIES` guarda os lançamentos manuais (tarifa, depósito, rendimento, saldo inicial…). Uma consulta única (`FinancialAccountMovementsQuery`, em Infra, para Web e Reports usarem) une as duas fontes com sinal de caixa. A reconciliação (`FINANCIAL_ACCOUNT_RECONCILIATIONS`) marca linhas das duas fontes por `ReconciliationKey` e fecha quando o saldo conciliado bate com o saldo do extrato. O extrato é um relatório FastReport no `SiagroB1.Reports`.

**Tech Stack:** .NET 10, EF Core (SQL Server), ASP.NET OData v4, FastReport, xUnit + EF InMemory; OpenUI5 TypeScript.

**Spec:** este plano (o usuário dispensou o brainstorming; as decisões abaixo são do Claude, listadas em "Decisões").

## Decisões (do Claude, para o usuário conferir)

1. **Modo:** funciona em STANDALONE e SAPB1 — o módulo financeiro vive no banco do Siagro nos dois modos; nada aqui consulta o SAP. Sem `ErpMode` gate.
2. **Sinal de caixa** de uma baixa = `(Amount + InterestAmount + FineAmount − DiscountAmount)`, positivo para `Receivable` (entrada) e negativo para `Payable` (saída). Estorno já é linha negativa, então sai com sinal invertido sozinho. Só entram baixas com `FinancialAccountCode` preenchido (as não-caixa ficam fora).
3. **Lançamento manual** tem valor assinado (+ entrada / − saída), não pode ter valor zero, nem ser lançado em conta inativa. Pode ser **excluído** enquanto não conciliado (é digitação, não baixa de título); conciliado → recusa.
4. **Reconciliação** é por conta e data de extrato: abrir (com saldo final do extrato) → marcar/desmarcar linhas com data ≤ data do extrato → fechar só se `saldo anterior conciliado + soma marcada == saldo do extrato`. Uma conta tem no máximo **uma** reconciliação aberta; a data do extrato tem de ser maior que a da última fechada. Só a **última fechada** pode ser reaberta. Linha conciliada não pode ser excluída/estornada? — estorno de baixa conciliada é permitido (o estorno é outra linha, que fica pendente); exclusão de manual conciliado é recusada.
5. **Extrato** (PDF): conta + período; saldo anterior (soma de tudo antes do início), movimentos em ordem de data, saldo acumulado por linha, coluna "Conciliado" (S/N), saldo final. Filtro opcional "somente conciliados".
6. Saldo "conciliado anterior" de uma reconciliação = soma de todas as linhas marcadas em reconciliações fechadas da conta.

## Global Constraints

- Identificadores em inglês; texto ao usuário em pt-BR; comentários em pt-BR.
- Commits nos dois repos separados, branch `feature/financial-account-reconciliation` (worktrees `C:\Projetos\SiagroB1\worktrees\financial-reconciliation\{backend,frontend}`), mensagens no padrão `feat(financial): …` em pt-BR, com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`; commit com migration leva o trailer `DB: <NomeDaMigration>`. Nunca push/merge.
- Todo arquivo novo: `git add` logo após criar.
- Migrations com `ASPNETCORE_ENVIRONMENT` explícito (nunca o profile db-migration); menu em `CommonContext` com `ROLE_MENUS` ADMIN.
- Valores em `DECIMAL(18,2)`; datas comparadas por dia (`.Date`).

## Review Focus

1. Baixa com juros/desconto e seu estorno: o par tem de somar zero no extrato. → teste na Task 2.
2. Conta sem nenhum movimento / período vazio: extrato com saldo anterior = final, sem quebrar o FastReport. → teste na Task 6.
3. Marcar linha com data posterior à do extrato ou de outra conta → recusa. → teste na Task 4.
4. Duas reconciliações abertas na mesma conta (duplo clique) → recusa. → teste na Task 3.
5. Reabrir reconciliação que não é a última → recusa. → teste na Task 4.

---

### Task 1: Entidades, migration e EDM

**Files:**
- Create: `SiagroB1.Domain/Entities/FinancialAccountEntry.cs` (`FINANCIAL_ACCOUNT_ENTRIES`, `BaseEntity`): `FinancialAccountCode` VARCHAR(10) FK, `EntryDate` DateTime, `Amount` DECIMAL(18,2), `Description` VARCHAR(200) obrigatório, `DocumentReference` VARCHAR(50)?, `Notes` VARCHAR(500)?, `ReconciliationKey` Guid? FK. Índices: conta, data, reconciliação.
- Create: `SiagroB1.Domain/Entities/FinancialAccountReconciliation.cs` (`FINANCIAL_ACCOUNT_RECONCILIATIONS`, `BaseEntity`): `FinancialAccountCode`, `StatementDate`, `StatementBalance` DECIMAL(18,2), `OpeningBalance` DECIMAL(18,2) (gravado na abertura), `Status` (`FinancialAccountReconciliationStatus` Open=0, Closed=1), `ClosedAt` DateTime?, `Notes` VARCHAR(500)?. Índice único filtrado `(FinancialAccountCode) WHERE Status = 0`.
- Create: `SiagroB1.Domain/Enums/FinancialAccountReconciliationStatus.cs`, `SiagroB1.Domain/Enums/FinancialAccountMovementSource.cs` (Settlement=0, Manual=1).
- Modify: `FinancialSettlement.cs` — `Guid? ReconciliationKey` + FK + índice.
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs` (DbSets + índice filtrado, ver ~667 como modelo — memória: índice filtrado exige QUOTED_IDENTIFIER).
- Create: migration `AddFinancialAccountReconciliation` em `SiagroB1.Migrations/AppContext/`.
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` — EntitySets `FinancialAccountEntries`, `FinancialAccountReconciliations`.
- Test: `SiagroB1.Application.Tests/Financials/FinancialEdmModelTests.cs` — os dois entity sets existem no EDM; `FinancialSettlement` expõe `ReconciliationKey`.

- [ ] Teste EDM falhando → entidades/DbContext/EDM → teste passa → gerar migration, revisar o SQL do índice filtrado → `dotnet build SiagroB1.sln` → commit `feat(financial): tabelas de lançamento manual e reconciliação de conta`.

### Task 2: Consulta de movimentos da conta

**Files:**
- Create: `SiagroB1.Infra/Queries/FinancialAccountMovementsQuery.cs`
- Create: `SiagroB1.Infra/Queries/FinancialAccountMovementRow.cs`
- Test: `SiagroB1.Application.Tests/Financials/FinancialAccountMovementsQueryTests.cs`

**Interfaces — Produces:**
```csharp
public record FinancialAccountMovementRow(
    FinancialAccountMovementSource Source, Guid Key, DateTime Date, decimal Amount /*sinal de caixa*/,
    string Description, string? DocumentReference, string? CardCode, string? CardName,
    Guid? FinancialDocumentKey, FinancialSettlementOrigin? SettlementOrigin, Guid? ReconciliationKey);

public static class FinancialAccountMovementsQuery {
    IQueryable<FinancialAccountMovementRow> Query(AppDbContext ctx, string accountCode); // não materializa
    Task<decimal> BalanceBeforeAsync(AppDbContext ctx, string accountCode, DateTime date, CancellationToken ct); // < date.Date
}
```
`Description` de baixa: `"Baixa " + DocNumber do título + " - " + CardName` (estorno: `"Estorno da baixa …"`); manual: a própria `Description`. União via `Concat` de dois `Select` (traduzível — rodar contra SQL Server em teste manual; memória: InMemory esconde LINQ intraduzível).

- [ ] Testes falhando: `Receivable_settlement_is_cash_in_with_interest_fine_minus_discount` (Amount 100, juros 5, multa 2, desconto 3 → +104); `Payable_settlement_is_cash_out` (→ −104); `Settlement_and_its_reversal_sum_to_zero`; `Settlement_without_account_is_excluded`; `Manual_entries_of_other_accounts_are_excluded`; `BalanceBefore_sums_only_rows_before_the_date`.
- [ ] Implementar → passar → commit `feat(financial): consulta de movimentos da conta financeira`.

### Task 3: Lançamento manual + abrir reconciliação

**Files:** `SiagroB1.Application/Services/Financials/FinancialAccountEntries{Create,Delete}Service.cs`, `FinancialAccountReconciliationsOpenService.cs`; controllers `SiagroB1.Web/Controllers/FinancialAccountEntriesController.cs` (POST/DELETE/GET, padrão de `FinancialAccountsController`) e ação `SiagroB1.Web/Actions/Financials/FinancialAccountReconciliationsOpenController.cs` (padrão de `FinancialDocumentsSettleController`); DI como os serviços vizinhos. Tests em `Financials/`.

**Interfaces — Produces:**
- `FinancialAccountEntriesCreateService.ExecuteAsync(FinancialAccountEntry entry)` → `FinancialAccountEntry`
- `FinancialAccountEntriesDeleteService.ExecuteAsync(Guid key)`
- `FinancialAccountReconciliationsOpenService.ExecuteAsync(string accountCode, DateTime statementDate, decimal statementBalance, string? notes)` → `FinancialAccountReconciliation`; ação OData `FinancialAccountReconciliationsOpen`.

Mensagens (`ApplicationException`): "Informe um valor diferente de zero.", "A conta financeira está inativa.", "Informe a descrição do lançamento.", "Lançamento conciliado não pode ser excluído. Reabra a reconciliação primeiro.", "Já existe uma reconciliação aberta para esta conta.", "A data do extrato deve ser posterior à da última reconciliação fechada (dd/MM/yyyy)."

- [ ] Testes (um por mensagem acima + caminho feliz; `Open_records_opening_balance_from_closed_reconciliations`) → implementar → passar → commit `feat(financial): lançamento manual e abertura de reconciliação`.

### Task 4: Marcar, fechar e reabrir reconciliação

**Files:** `FinancialAccountReconciliations{SetLines,Close,Reopen,GetSummary}Service.cs`; ações `FinancialAccountReconciliationsSetLines`, `…Close`, `…Reopen`, função `FinancialAccountReconciliationsGetLines(Key)`; tests.

**Interfaces — Produces:**
- `SetLines(Guid reconciliationKey, IReadOnlyList<MovementRef> mark, IReadOnlyList<MovementRef> unmark)` com `record MovementRef(FinancialAccountMovementSource Source, Guid Key)`.
- `GetLines(Guid key)` → `FinancialAccountReconciliationLinesDto { OpeningBalance, StatementBalance, ReconciledAmount, Difference, Rows: List<FinancialAccountMovementRow + bool Marked> }` — linhas da conta com data ≤ StatementDate e (`ReconciliationKey == null` ou `== key`).
- `Close(Guid key)`, `Reopen(Guid key)` (reabrir desfaz `ClosedAt`, Status → Open; não desmarca linhas).

Mensagens: "A reconciliação já está fechada.", "Lançamento de outra conta.", "Lançamento posterior à data do extrato.", "Lançamento já conciliado em outra reconciliação.", "Diferença de R$ {0:N2} entre o saldo conciliado e o saldo do extrato.", "Somente a última reconciliação fechada pode ser reaberta.", "Feche ou exclua a reconciliação aberta antes de reabrir outra."

- [ ] Testes por mensagem + `Close_succeeds_when_difference_is_zero`, `Reopen_last_closed_keeps_marks` → implementar → passar → commit `feat(financial): conciliação de lançamentos e fechamento da reconciliação`.

### Task 5: Função de lançamentos para a tela

**Files:** função OData `FinancialAccountsGetMovements(AccountCode, DateFrom, DateTo)` → `FinancialAccountMovementsDto { OpeningBalance, ClosingBalance, Rows: [... + RunningBalance] }` em `SiagroB1.Application/Services/Financials/FinancialAccountsGetMovementsService.cs` + controller da função (modelo `FinancialDocumentsGetTotals`). Test: running balance acumula a partir do saldo anterior; ordenação por Date, depois CreatedAt.

- [ ] Teste → implementar → passar → commit `feat(financial): função de lançamentos por conta e período`.

### Task 6: Extrato em PDF (SiagroB1.Reports)

**Files:** `Controllers/FinancialAccountStatementController.cs` (`POST /reports/FinancialAccountStatement`, body `{ AccountCode, DateFrom, DateTo, OnlyReconciled }`), `Services/FinancialAccountStatementReportService.cs`, `Dtos/FinancialAccountStatement*.cs`, `Reports/Templates/FinancialAccountStatement.frx` (modelo `FunruralWithholding`: cabeçalho da empresa via `ReportHeaderService`; parâmetros Conta, Banco/Agência/Conta, Período, Saldo anterior, Saldo final; DataBand Data | Descrição | Documento | Parceiro | Entrada | Saída | Saldo | Conc.). Tests em `SiagroB1.Application.Tests/Reports/FinancialAccountStatement{ReportService,Pdf}Tests.cs` com `RecordingFastReportService`.

Validação: "Informe a conta financeira.", "A data inicial deve ser menor ou igual à final."

- [ ] Testes: `Statement_rows_carry_running_balance`, `Empty_period_renders_with_opening_equal_closing` (PDF real), `OnlyReconciled_filters_rows` → implementar → passar → commit `feat(financial): impressão do extrato da conta financeira`.

### Task 7: Menus

**Files:** migration `CommonContext/…_AddFinancialAccountMovementsMenus.cs`: sob `financial`, `financialAccountMovements` "Lançamentos da Conta" (Order 5), `financialAccountReconciliations` "Reconciliação de Conta" (Order 6), `financialAccountStatementReport` "Extrato da Conta" (Order 7), cada um com `ROLE_MENUS` ADMIN (GUID fixo). Modelo: `20260907224917_AddFinancialMenus.cs`.

- [ ] Gerar/escrever → build → commit `feat(financial): menus de lançamentos, reconciliação e extrato`.

### Task 8: Frontend — Lançamentos da Conta

**Files (frontend):** `webapp/view/financialAccountMovements/Main.view.xml` + `controller/financialAccountMovements/Main.controller.ts`; fragment `AddEntryDialog.fragment.xml`; rota `financial-account-movements` (nome `financialAccountMovements`) no `manifest.json`; i18n.

Tela: FilterBar com Conta (value help `FinancialAccountsSelectDialog`, obrigatória), De/Até (padrão: mês corrente, datas locais — memória: não usar toISOString). Cabeçalho com Saldo anterior / Saldo final. Tabela `sap.m.Table`: Data, Origem (Baixa/Manual/Estorno…), Descrição, Documento, Parceiro, Entrada, Saída, Saldo, Conciliado (ícone). Linha de baixa navega para `financialDocumentsDetail`. Botões: "Novo lançamento" (diálogo: Data, Tipo Entrada/Saída, Valor, Descrição, Documento, Observação → POST `FinancialAccountEntries`), "Excluir" (só manual não conciliado), "Imprimir extrato" (POST para o Reports, abre o PDF como no Funrural; `ServerRoutes.financialAccountStatementReport`), Exportar Excel.

- [ ] `npm run ts-typecheck` e `npm run lint` limpos → verificar no navegador pela home (menu Financeiro) → commit.

### Task 9: Frontend — Reconciliação de Conta

**Files:** `view/financialAccountReconciliations/{Main,Detail}.view.xml` + controllers; rotas `financial-account-reconciliations` e `financial-account-reconciliations/{key}`; fragment `OpenReconciliationDialog`.

Main: lista de reconciliações (Conta, Data do extrato, Saldo do extrato, Status, Fechada em) com filtro por conta/status; "Nova reconciliação" (Conta, Data, Saldo do extrato). Detail: cabeçalho com Saldo anterior conciliado, Saldo do extrato, Conciliado, **Diferença** (vermelho ≠ 0); tabela com checkbox por linha (MultiSelect), botões "Salvar marcações" (SetLines), "Fechar" (habilitado com diferença 0), "Reabrir" (fechada), "Imprimir extrato" (período = da reconciliação anterior +1 até a data do extrato, somente conciliados).

- [ ] typecheck/lint → verificação no navegador: abrir, marcar, fechar com diferença zero, reabrir → commit.

### Task 10: Extrato pelo menu + verificação ponta a ponta

**Files:** `view/reports/financialAccountStatement/Main.view.xml` + controller (rota `financial-account-statement/report`, nome `financialAccountStatementReport`, modelo `reports/funruralWithholding`).

- [ ] Subir a stack (memória run-stack-locally), aplicar migrations no banco DEV, percorrer as três telas pela home, gerar o PDF → `superpowers:verification-before-completion` → commit; derrubar a stack pelos PIDs.
