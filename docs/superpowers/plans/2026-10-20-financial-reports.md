# Relatórios financeiros (carteira, baixas, extrato do parceiro) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cinco relatórios PDF (Contas a Receber, Contas a Pagar, Baixas de Recebíveis, Baixas de Pagáveis, Extrato do
Parceiro) com posição na data-base e parâmetro Firmes × Provisórios, cada um com menu próprio sob "Financeiro".

**Architecture:** Um cálculo puro e único (`FinancialPositionCalculator`, em `SiagroB1.Infra/Queries`) recebe títulos,
baixas e composições já carregados e devolve, para uma data-base e um modo, os títulos com saldo e a lista de "baixas
efetivas" (já com as fatias distribuídas de R5). Um loader (`FinancialPositionQuery`) faz o I/O no EF. Os três serviços
do `SiagroB1.Reports` só formatam esse resultado para `.frx`. Frontend: três telas de filtro (carteira e baixas servem
duas rotas cada, com a direção pela rota).

**Tech Stack:** .NET 10, EF Core (SQL Server; testes em InMemory), FastReport.OpenSource, xUnit; OpenUI5 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-20-financial-reports-design.md` (R1–R8, C1–C3).

## Global Constraints

- Identificadores em inglês; textos ao usuário (labels, mensagens, cabeçalhos do PDF, menus) em pt-BR.
- Funciona em STANDALONE e SAPB1: lê só `FINANCIAL_DOCUMENTS`, `FINANCIAL_SETTLEMENTS`, `FINANCIAL_DOCUMENT_COMPOSITIONS`
  do AppDbContext; nome do parceiro = `FinancialDocument.CardName` (fallback `CardCode`).
- Datas comparadas sem hora (`.Date`) — C3.
- Títulos `Canceled` sempre fora.
- Nada altera o ledger, a composição ou `SettledAmount`/`ComposedAmount` gravados — leitura pura.
- Menus: migration CommonContext, `StandaloneOnly = false`, `ROLE_MENUS` ADMIN, Key = name da rota no `manifest.json`,
  Orders 10–14 sob `financial` (1–9 ocupados).
- Commits: `tipo(escopo): ...` pt-BR (`financial` / `reports`), trailer `DB:` no commit da migration, `Co-Authored-By`.
  Backend em `C:\Projetos\SiagroB1\worktrees\financial-reports\backend`, frontend em `...\financial-reports\frontend`,
  branch `feature/financial-reports` nos dois. Nunca push. `git add` todo arquivo novo na hora.
- Novo serviço de Reports registrado onde `FinancialAccountStatementReportService` está registrado.

## Review Focus

1. **Baixa estornada depois da data-base** — baixa em 05/10 estornada em 12/10: no relatório de 10/10 o título aparece
   baixado. Teste em Task 1 (`Reversal_after_base_date_is_ignored`).
2. **Pagamento parcial do provisório com vários firmes** — R$ 150 pagos num provisório com firmes de 100 (venc. 01/10)
   e 100 (venc. 05/10): primeiro quitado, segundo com saldo 50. Teste em Task 2.
3. **Composição desfeita depois da data-base** — composto em 03/10, desfeito em 15/10: na data-base 10/10 ainda conta
   como composto (R5 e R8). Testes em Task 2 e Task 3.
4. **Títulos em USD misturados com BRL** — totais por moeda, nunca somados. Teste em Task 4 (DTO de totais por moeda).
5. **Parceiro com títulos a receber e a pagar no extrato "Ambos"** — sinal do saldo conforme C2. Teste em Task 6.

---

### Task 1: Cálculo da posição na data-base (R1, R2, R4, sem composição)

**Files:**
- Create: `SiagroB1.Infra/Queries/FinancialPositionCalculator.cs`, `SiagroB1.Infra/Queries/FinancialPositionModels.cs`
- Test: `SiagroB1.Application.Tests/Financials/FinancialPositionCalculatorTests.cs`

**Interfaces — Produces:**
```csharp
public enum FinancialPositionMode { Firm, Provisional }
public sealed record FinancialPositionInput(
    IReadOnlyList<FinancialDocument> Documents,      // já filtrados por direção/parceiro; podem incluir Canceled
    IReadOnlyList<FinancialSettlement> Settlements,  // todas as linhas desses documentos
    IReadOnlyList<FinancialDocumentComposition> Compositions); // vigentes, envolvendo esses documentos
public sealed record PositionTitle(FinancialDocument Document, decimal Amount, decimal SettledAtBase, decimal OpenAtBase);
// Amount: NetAmount; negativo para PurchaseReturn/SalesReturn (R7); provisório em modo Provisional = composto na data (R8)
public sealed record EffectiveSettlement(FinancialDocument Document, FinancialSettlement Source, DateTime Date,
    decimal Amount, decimal Interest, decimal Fine, decimal Discount, bool IsDistributed);
public sealed record FinancialPosition(IReadOnlyList<PositionTitle> Titles, IReadOnlyList<EffectiveSettlement> Settlements);
public static class FinancialPositionCalculator {
    public static FinancialPosition Compute(FinancialPositionInput input, DateTime baseDate, FinancialPositionMode mode);
}
```
`Titles` = documentos existentes na data (`DocumentDate.Date <= baseDate`, não cancelados) incluídos pelo modo (R4),
com qualquer saldo (filtro de saldo > 0 e de vencimento fica nos relatórios). `Settlements` = baixas efetivas com
`Date <= baseDate` dos títulos incluídos (estornos como linhas negativas).

- [ ] **Step 1: Testes que falham** — helpers de fábrica no próprio arquivo. Casos (datas em 2026-10):
  - `Settlement_after_base_date_is_ignored`: título 100, baixa 40 em 05/10, baixa 60 em 11/10, base 10/10 → `OpenAtBase == 60`.
  - `Reversal_after_base_date_is_ignored`: baixa 100 em 05/10, estorno −100 em 12/10, base 10/10 → `OpenAtBase == 0`; base 12/10 → 100.
  - `Document_created_after_base_date_is_absent`: `DocumentDate` 15/10, base 10/10 → não está em `Titles`.
  - `Time_of_day_is_ignored`: `DocumentDate` 10/10 18:30, base 10/10 00:00 → presente.
  - `Canceled_is_absent`.
  - `Firm_mode_excludes_provisional` / `Provisional_mode_excludes_contract_firm_and_returns`
    (firme com `OriginType=PurchaseFiscalReconciliation`) / `Both_modes_include_advance_manual_and_no_contract`
    (Advance, Manual, `OtherPurchaseInvoice`, `PurchaseContractWashout`).
- [ ] **Step 2:** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialPositionCalculatorTests"` → FAIL (não compila).
- [ ] **Step 3:** Implementar. Inclusão no modo Firm: tudo exceto `Nature == Provisional`. Modo Provisional: tudo exceto
  `IsContractFirmDocument`. Neste task, linhas `InvoiceOffset` com `CompositionKey != null` já ficam **fora** das baixas
  efetivas no modo Firm (R5) — a distribuição vem no Task 2.
- [ ] **Step 4:** mesmo comando → PASS.
- [ ] **Step 5:** commit `feat(financial): cálculo da posição financeira na data-base`.

### Task 2: Distribuição do pagamento do provisório aos firmes compostos (R5, R7, C1)

**Files:** Modify `FinancialPositionCalculator.cs`; Test: mesmo arquivo de testes.

**Interfaces:** Consumes Task 1. Produces fatias `EffectiveSettlement { IsDistributed = true, Source = baixa do provisório, Document = firme }`.

Algoritmo (modo Firm, por provisório):
```
composto_na_data(firme) = Σ InvoiceOffset/Reversal com CompositionKey, no firme, Date <= base   (devolução: negativo)
pool = Σ baixas do provisório (todas as origens, C1) com Date <= base, em ordem de data
fila = devoluções primeiro (abatem a capacidade), depois firmes por DueDate, DocumentDate, Code
capacidade líquida: devolução composta −D reduz a capacidade dos primeiros firmes da fila em D
cada baixa do pool (na ordem) preenche a fila; cada pedaço vira uma EffectiveSettlement no firme com Date/conta/origem
da baixa (juros/multa/desconto rateados pela proporção do pedaço); estorno do pool desfaz do fim da fila para trás.
devolução composta: some (OpenAtBase = 0) quando o pool cobrir o líquido composto (Σfirmes − Σdevoluções); senão Amount negativo e OpenAtBase = −D.
```
- [ ] **Step 1: Testes que falham:**
  - `Composed_firm_stays_open_until_provisional_is_paid`: firme 100 composto 03/10, sem pagamento → `OpenAtBase == 100`.
  - `Provisional_payment_distributes_by_due_date`: firmes 100 (venc. 01/10) e 100 (05/10), pagamento 150 em 08/10 →
    abertos 0 e 50; duas fatias com `Date == 08/10` e `FinancialAccountCode` da baixa.
  - `Payment_after_base_is_not_distributed`: pagamento em 11/10, base 10/10 → 100 e 100.
  - `Return_is_netted_first`: firmes 100+100, devolução −30, pagamento 120 → firme1 0, firme2 50, devolução ainda −30;
    pagamento 170 → todos 0.
  - `Uncomposed_after_base_still_counts`: composto 03/10, desfeito 15/10 (Reversal com CompositionKey), base 10/10,
    pagamento 100 em 08/10 → firme 0.
  - `Advance_application_on_provisional_is_distributed` (C1): baixa `AdvanceApplication` 100 → fatia com essa origem.
  - `Dation_on_firm_counts_directly`: Dation 20 no firme não composto → aberto 80.
- [ ] **Step 2:** rodar → FAIL. **Step 3:** implementar. **Step 4:** rodar Task 1+2 → PASS.
- [ ] **Step 5:** commit `feat(financial): distribuir o pagamento do provisório aos firmes compostos`.

### Task 3: Provisório em modo Provisórios mostra só o composto na data (R8)

**Files:** Modify `FinancialPositionCalculator.cs`; Test: mesmo arquivo.

- [ ] **Step 1: Testes:** `Provisional_amount_is_composed_at_base` (firmes 100 composto 03/10 e 50 composto 12/10;
  `NetAmount` do provisório 1000; base 10/10 → `Amount == 100`); `Provisional_with_nothing_composed_is_absent`;
  `Provisional_open_is_composed_minus_its_settlements` (pago 40 em 05/10 → 60); `Return_reduces_composed_amount`.
  Composto do provisório = Σ dos `composto_na_data` dos documentos compostos nele (via `Compositions` e `CompositionKey`).
- [ ] **Step 2–4:** FAIL → implementar → PASS (Tasks 1–3).
- [ ] **Step 5:** commit `feat(financial): provisório mostra só o composto na data-base`.

### Task 4: Loader EF + serviço e PDF da carteira (Contas a Receber / a Pagar)

**Files:**
- Create: `SiagroB1.Infra/Queries/FinancialPositionQuery.cs`
- Create: `SiagroB1.Reports/Dtos/FinancialPortfolioDtos.cs`, `SiagroB1.Reports/Services/FinancialPortfolioReportService.cs`,
  `SiagroB1.Reports/Controllers/FinancialPortfolioController.cs`, `SiagroB1.Reports/Reports/Templates/FinancialPortfolio.frx`
- Test: `SiagroB1.Application.Tests/Financials/FinancialPositionQueryTests.cs`, `SiagroB1.Application.Tests/Reports/FinancialPortfolioReportServiceTests.cs`
  (setup copiado de `FinancialAccountStatementReportServiceTests`)

**Interfaces — Produces:**
```csharp
public sealed record FinancialPositionFilter(FinancialDirection? Direction, string? CardCode, DateTime BaseDate);
public static class FinancialPositionQuery {
    public static Task<FinancialPosition> LoadAsync(AppDbContext ctx, FinancialPositionFilter f, FinancialPositionMode mode, CancellationToken ct);
} // carrega documentos (inclui os provisórios dos firmes filtrados, necessários para R5), baixas e composições; chama o Calculator
public class FinancialPortfolioRequest { FinancialDirection Direction; DateTime DueDateFrom; DateTime DueDateTo; FinancialPositionMode Mode; string? CardCode; }
// rota POST /reports/FinancialPortfolio, devolve application/pdf
```
- [ ] **Step 1: Testes:** Query — `Loads_only_direction_and_partner` e `Brings_provisional_of_composed_firms_even_when_filtered_by_partner`
  (InMemory). Serviço — `Validate`: "Informe o período." / "A data inicial deve ser menor ou igual à final.";
  `Filters_by_due_date_and_open_amount` (venc. fora do período ou saldo 0 → fora); `Days_overdue_is_base_minus_due_when_positive`;
  `Totals_are_per_currency` (BRL e USD → duas linhas de total); `Generates_pdf` (bytes começam com `%PDF`).
- [ ] **Step 2–4:** FAIL → implementar → PASS.
  Linhas: Parceiro, Documento (`Code`), Origem (`OriginDocNumber` ou código do contrato ou "Manual"), Emissão,
  Vencimento, Moeda, Valor, Baixado até a data-base, Saldo, Dias em atraso; agrupado por parceiro com subtotal.
  Cabeçalho: "Contas a Receber"/"Contas a Pagar", "Vencimento: dd/MM/yyyy a dd/MM/yyyy · Posição em dd/MM/yyyy (data-base)",
  "Títulos: Firmes"/"Títulos: Provisórios". Registrar o serviço no DI dos Reports.
- [ ] **Step 5:** commit `feat(reports): relatório de contas a receber e a pagar na data-base`.

### Task 5: Baixas de Recebíveis / Pagáveis

**Files:** Create `SiagroB1.Reports/Dtos/FinancialSettlementsReportDtos.cs`, `Services/FinancialSettlementsReportService.cs`,
`Controllers/FinancialSettlementsReportController.cs` (rota `/reports/FinancialSettlements`), `Templates/FinancialSettlements.frx`;
Test `SiagroB1.Application.Tests/Reports/FinancialSettlementsReportServiceTests.cs`.

**Interfaces:** Consumes `FinancialPositionQuery.LoadAsync` com `BaseDate = DateTo`; usa `Settlements` com `Date >= DateFrom`.
Request: `Direction, DateFrom, DateTo, Mode, CardCode?, AccountCode?`.

- [ ] **Step 1: Testes:** `Filters_by_settlement_date`; `Distributed_slice_shows_on_firm_with_provisional_account`;
  `Reversal_shows_negative`; `Origin_labels_pt_BR` (Manual→"Baixa", Reversal→"Estorno", InvoiceOffset→"Abatimento",
  AdvanceApplication→"Aplicação de adiantamento", Netting→"Encontro de contas", AdvanceRefund→"Devolução de adiantamento",
  Dation→"Dação", AdvanceTransfer→"Transferência de adiantamento"); `Filters_by_account`; `Generates_pdf`.
- [ ] **Step 2–4:** FAIL → implementar → PASS. Agrupado por data; colunas Parceiro, Título, Conta, Forma, Valor, Juros,
  Multa, Desconto; total por dia e geral (por moeda).
- [ ] **Step 5:** commit `feat(reports): relatório de baixas de recebíveis e pagáveis`.

### Task 6: Extrato do Parceiro

**Files:** Create `Dtos/PartnerStatementDtos.cs`, `Services/PartnerStatementReportService.cs`,
`Controllers/PartnerStatementController.cs` (rota `/reports/PartnerStatement`), `Templates/PartnerStatement.frx`;
Test `SiagroB1.Application.Tests/Reports/PartnerStatementReportServiceTests.cs`.

**Interfaces:** Request `CardCode (obrigatório), DateFrom, DateTo, Direction? (null = Ambos), Mode`.
Saldo anterior = posição em `DateFrom − 1 dia` (Σ OpenAtBase com sinal C2). Movimento = emissões com
`DocumentDate` no período + `Settlements` da posição em `DateTo` com `Date` no período. Ordem: data, emissões antes de baixas.

- [ ] **Step 1: Testes:** `Validate` ("Informe o parceiro.", período); `Opening_balance_is_position_on_day_before`;
  `Receivable_issue_is_debit_settlement_is_credit`; `Payable_issue_is_credit_settlement_is_debit`;
  `Both_directions_balance_sign` (a receber 100, a pagar 30 → saldo 70); `Return_goes_to_opposite_column`;
  `Closing_equals_opening_plus_movements_equals_position_at_end` (invariante, com R5 ativo); `Generates_pdf`.
- [ ] **Step 2–4:** FAIL → implementar → PASS. Colunas Data, Histórico ("Emissão <Code>"/"<forma> <Code>"), Documento,
  Débito, Crédito, Saldo. Em modo Provisional, a "emissão" do provisório é cada composição com data (R8).
- [ ] **Step 5:** commit `feat(reports): extrato financeiro do parceiro`.

### Task 7: Menus (migration CommonContext)

**Files:** Create `SiagroB1.Migrations/CommonContext/<timestamp>_AddFinancialReportsMenus.cs` (+ Designer via `dotnet ef migrations add`
com `--context CommonDbContext`, editada à mão como `AddFinancialAccountMovementsMenus`).

Menus (`sap-icon://print`, ParentKey `financial`): `receivablesReport` "Relatório de Contas a Receber" 10,
`payablesReport` "Relatório de Contas a Pagar" 11, `receivableSettlementsReport` "Baixas de Recebíveis" 12,
`payableSettlementsReport` "Baixas de Pagáveis" 13, `partnerStatementReport` "Extrato do Parceiro" 14; ROLE_MENUS ADMIN
com GUIDs novos; Down apaga ROLE_MENUS antes de MENU_ITEMS.

- [ ] **Step 1:** gerar e editar. **Step 2:** `dotnet build SiagroB1.sln` → 0 erros; `dotnet test SiagroB1.Application.Tests` → tudo verde.
- [ ] **Step 3:** commit `feat(financial): menus dos relatórios financeiros` com `DB: AddFinancialReportsMenus`.

### Task 8: Frontend — três telas de filtro

Criar worktree: `git -C C:/Projetos/SiagroB1/siagro-b1-frontend worktree add -b feature/financial-reports ../worktrees/financial-reports/frontend main`
e rodar `yarn install` nele (NÃO usar junction de node_modules).

**Files (frontend):** `webapp/view/reports/financialPortfolio/Main.view.xml` + `controller/.../Main.controller.ts`
(rotas `receivablesReport` e `payablesReport`, direção pela rota); `reports/financialSettlements/*` (rotas
`receivableSettlementsReport`, `payableSettlementsReport`); `reports/partnerStatement/*` (rota `partnerStatementReport`);
`webapp/manifest.json` (5 rotas + 3 targets); `webapp/model/ServerRoutes.ts` (`financialPortfolioReport`,
`financialSettlementsReport`, `partnerStatementReport`).

Padrão: copiar `reports/financialAccountStatement` (JSONModel `params`, `openPdfReport`, `monthRange`). Campos:
período (DatePicker De/Até), `SegmentedButton` "Firmes"/"Provisórios" (padrão Firmes), parceiro (value help de PN,
opcional; obrigatório no extrato), conta (só Baixas), direção A Receber/A Pagar/Ambos (só Extrato, padrão Ambos).
Título da página = título do menu. Ao trocar entre as duas rotas da mesma view, redefinir título e direção.

- [ ] **Step 1:** implementar. **Step 2:** `yarn ts-typecheck` e `yarn lint` → sem erros.
- [ ] **Step 3:** commit `feat(financial): telas dos relatórios financeiros`.

### Task 9: Verificação ponta a ponta

- [ ] Subir a stack do worktree (profile e regras da memória "Subir a stack local"; matar só pelo PID) contra banco
  de dev com a migration aplicada (em banco de dev, com autorização do usuário).
- [ ] Pela home → Financeiro, abrir os 5 menus, gerar cada PDF em Firmes e Provisórios, data-base passada; conferir um
  título baixado depois da data-base aparecendo em aberto.
- [ ] Registrar o resultado; derrubar a stack.
