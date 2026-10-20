# Ajuste manual, recálculo de custo e fechamento do estoque fiscal — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tirar o "Acerto de saldo negativo" automático do Kardex, custear a saída sem saldo pela entrada que a cobre, e criar o ajuste manual de estoque e o fechamento/reabertura de mês com trava de período.

**Architecture:** O motor PEPS e o carregador de movimentos saem do `SiagroB1.Reports` para `SiagroB1.Infra/FiscalStock` (Infra é referenciado por Reports e por Application), para o Web validar ajuste e fechar mês com o usuário logado. Fechamento grava instantâneo das camadas; o carregador parte do último instantâneo anterior ao período consultado. Trava de período num serviço único em Infra, chamado pelos serviços que confirmam/estornam/cancelam documentos.

**Tech Stack:** .NET 10, EF Core (SQL Server), OData (Web), ASP.NET Core (Reports), xUnit + EF InMemory, OpenUI5 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-20-fiscal-stock-adjustments-and-month-closing-design.md`

## Global Constraints

- Só STANDALONE: controllers recusam SAPB1 com "Disponível somente no modo STANDALONE."; menus `StandaloneOnly = true`.
- Identificadores em inglês; texto ao usuário e comentários em pt-BR.
- Tabelas: `FISCAL_STOCK_ADJUSTMENTS`, `FISCAL_STOCK_PERIODS`, `FISCAL_STOCK_PERIOD_EVENTS`, `FISCAL_STOCK_PERIOD_LAYERS` (AppDbContext, por empresa).
- Data de estoque: entrada = `PostingDate ?? IssueDate`; saída = `InvoiceDate`; ajuste = `AdjustmentDate`; saldo inicial = `BalanceDate`.
- Mensagem da trava (texto exato): `O estoque fiscal de {MM/AAAA} da filial {X} está fechado. Use uma data de entrada no estoque a partir de {dd/MM/aaaa} ou peça a reabertura do mês.`
- Aviso da saída coberta (texto exato): `Custo pela entrada {nº interno} de {dd/MM/aaaa}` (várias: separadas por "; ").
- Rótulo do `PostingDate` na tela: `Data de entrada no estoque`.
- Commits: `tipo(escopo): descrição pt-BR`; escopo `reports` (backend fiscal) / `invoice` (trava nos documentos) / `platform` (versão); trailer `DB: <Migration>` quando houver migration. Nunca push.
- Migration por empresa: aplicar em **todas** as empresas (a Web recusa subir com pendência).

## Review Focus

1. Saída coberta por duas entradas parciais: o `ValueOut` da saída soma as duas partes e o saldo intermediário fica negativo em valor — teste no Task 3.
2. Entrada maior que o pendente: só o excedente vira camada, pelo mesmo custo unitário — teste no Task 3.
3. Kardex com "de" depois de um mês fechado: parte do instantâneo, mesmo saldo anterior que o cálculo do zero — teste no Task 4.
4. Confirmar Documento de Entrada com `PostingDate` em mês fechado e emissão também nele: recusa com a mensagem exata — teste no Task 5.
5. Fechar mês com produto quantidade 0 e valor ≠ 0 (ou complemento pendente): recusa listando o produto — teste no Task 7.

---

### Task 1: Mover o motor PEPS para Infra (refactor sem mudança de comportamento)

**Files:**
- Move: `SiagroB1.Reports/Services/Fiscal/{FifoLedger,FifoLedgerModels,BookableInvoiceLines}.cs` → `SiagroB1.Infra/FiscalStock/` (namespace `SiagroB1.Infra.FiscalStock`)
- Create: `SiagroB1.Infra/FiscalStock/FiscalStockLedgerLoader.cs` (o `LoadLedgersAsync` + `SaleMovement`/`PurchaseMovement`/`OpeningMovement`/`LineSource` de `FiscalKardexService`)
- Modify: `SiagroB1.Reports/Services/FiscalKardexService.cs`, `FiscalInventoryService.cs`, Conferência Fiscal (usa `BookableInvoiceLines`), `SiagroB1.Reports/Program.cs` (registro do loader), testes que usam os namespaces

**Interfaces:**
- Produces: `public sealed record FiscalStockLineSource(string InternalNumber, string DocumentNumber, string Partner, string Cfop, string UsageName, decimal InvoiceValue, string? ItemName, bool UnlinkedEntry = false)`; `public sealed record FiscalStockItemLedger(string ItemCode, string ItemName, string? UnitOfMeasureCode, FiscalInventoryOpeningBalance? Opening, FifoResult Result)`; `public class FiscalStockLedgerLoader(IUnitOfWork db) { Task<List<FiscalStockItemLedger>> LoadAsync(string branchCode, string? itemCode, DateTime toDate, DateTime? fromDate = null) }` (`fromDate` sem efeito até o Task 4).

- [ ] **Step 1:** Mover os arquivos, trocar namespaces, extrair o loader; `FiscalKardexService` passa a receber `FiscalStockLedgerLoader`. Registrar o loader como Scoped no Reports (e no Web, Task 6).
- [ ] **Step 2:** `dotnet build SiagroB1.sln` — 0 erros.
- [ ] **Step 3:** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Fiscal"` — todos passam (mesmo número de antes).
- [ ] **Step 4:** Commit `refactor(reports): motor PEPS do estoque fiscal vai para Infra`.

### Task 2: Entidades e migration

**Files:**
- Create: `SiagroB1.Domain/Entities/FiscalStockAdjustment.cs`, `FiscalStockPeriod.cs`, `FiscalStockPeriodEvent.cs`, `FiscalStockPeriodLayer.cs`; `SiagroB1.Domain/Enums/FiscalStockAdjustmentDirection.cs` (`In = 1, Out = 2`), `FiscalStockPeriodStatus.cs` (`Open = 1, Closed = 2`), `FiscalStockPeriodEventType.cs` (`Closed = 1, Reopened = 2`)
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs`
- Create: migration App `AddFiscalStockAdjustmentsAndPeriods`

**Interfaces:**
- Produces (todas `BaseEntity`, `Key` Guid):
  - `FiscalStockAdjustment`: `BranchCode` VARCHAR(10), `DocNumber` VARCHAR(20) (formato `AJ000001`, sequência por filial, gerada no create), `AdjustmentDate` DateTime, `ItemCode` VARCHAR(50), `ItemName` VARCHAR(200)?, `UnitOfMeasureCode` VARCHAR(4)?, `Direction`, `Quantity` DECIMAL(19,6), `TotalValue` DECIMAL(19,2), `Reason` VARCHAR(500), `CreatedBy` VARCHAR(100). Índice único (`BranchCode`,`DocNumber`).
  - `FiscalStockPeriod`: `BranchCode`, `Year` int, `Month` int, `Status`, `ClosedAt`/`ClosedBy`, `ReopenedAt`/`ReopenedBy`, `ReopenReason` VARCHAR(500)?. Índice único (`BranchCode`,`Year`,`Month`).
  - `FiscalStockPeriodEvent`: `PeriodKey` (FK Cascade), `EventType`, `At`, `By`, `Reason`?.
  - `FiscalStockPeriodLayer`: `PeriodKey` (FK Cascade), `ItemCode`, `ItemName`?, `UnitOfMeasureCode`?, `LayerDate`, `SourceLineKey` Guid, `Quantity` DECIMAL(19,6), `Value` DECIMAL(19,2), `Sequence` int.

- [ ] **Step 1:** Criar entidades + DbSets + índices/FKs.
- [ ] **Step 2:** `dotnet ef migrations add AddFiscalStockAdjustmentsAndPeriods --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web` e revisar o arquivo (só CreateTable/CreateIndex).
- [ ] **Step 3:** `dotnet build SiagroB1.sln` — 0 erros. `git add` dos arquivos novos.
- [ ] **Step 4:** Commit `feat(reports): tabelas de ajuste e fechamento do estoque fiscal` com `DB: AddFiscalStockAdjustmentsAndPeriods`.

### Task 3: Motor PEPS — sem acerto, saída custeada pela entrada que a cobre, ajuste e semente

**Files:**
- Modify: `SiagroB1.Infra/FiscalStock/FifoLedger.cs`, `FifoLedgerModels.cs`
- Test: `SiagroB1.Application.Tests/Reports/FifoLedgerTests.cs` (existente; ajustar os que esperam a linha de acerto)

**Interfaces:**
- Produces: `InventoryMovementKind` ganha `AdjustmentIn`, `AdjustmentOut`; `ValuedMovement` perde `IsNegativeBalanceAdjustment` e ganha `IReadOnlyList<Guid> CoveredByLineKeys` (entradas que custearam a saída, vazio se nenhuma); `FifoLedger.Run(IEnumerable<InventoryMovement> movements, IEnumerable<FifoLayer>? seed = null)` — `seed` são as camadas iniciais (instantâneo), em ordem.

Regras:
- Saída/devolução de compra/`AdjustmentOut` sem camada: o restante entra numa fila de pendências `(índice da linha, quantidade)`; a linha sai com `InsufficientBalance = true`.
- Entrada (`Opening`, `Entry`, `SalesReturn`, `AdjustmentIn`): cobre a fila da mais antiga para a mais nova pelo custo unitário da entrada (`Money(cover * Cost(value, qty))`, a última parte leva o resto do valor); soma no `ValueOut` da linha pendente, acrescenta a chave da entrada em `CoveredByLineKeys`; se a pendência zerar, `InsufficientBalance = false`. O excedente vira camada com o valor restante.
- `AdjustmentOut`: quantidade sai das camadas FIFO; o valor que sai é o `Value` informado no movimento, rateado pelas camadas consumidas na proporção da quantidade (última camada leva o resto). Sem camada suficiente, comporta-se como saída (pendência) — a validação do Task 6 impede isso na criação.
- Depois do laço, recalcular `BalanceValue` de todas as linhas como soma acumulada `ValueIn − ValueOut` (mais o valor da semente). `BalanceQuantity` continua acumulado.
- `FifoResult.Deficit` = soma das pendências restantes.

- [ ] **Step 1: Testes que falham** (`FifoLedgerTests`):
  - `Exit_without_balance_is_costed_by_next_entry`: saída 10 (dia 1), entrada 10 por 1000 (dia 2) → 2 linhas; saída `ValueOut == 1000`, `InsufficientBalance == false`, `CoveredByLineKeys == [entrada]`; saldo da saída `(-10, -1000)`; saldo final `(0, 0)`.
  - `Exit_covered_by_two_entries`: saída 10; entrada 4 por 400; entrada 6 por 900 → saída `ValueOut == 1300`, cobertura das duas; saldos `(-10,-1300)`, `(-6,-900)`, `(0,0)`.
  - `Entry_larger_than_pending_creates_layer_with_rest`: saída 4; entrada 10 por 1000 → saída 400; camada aberta 6 / 600.
  - `Still_pending_exit_keeps_warning`: saída 5 sem entrada → `InsufficientBalance`, `ValueOut == 0`, `Deficit == 5`.
  - `Adjustment_out_uses_informed_value`: entrada 10/1000; `AdjustmentOut` 4 valor 500 → `ValueOut 500`, camada 6/500.
  - `Seed_layers_start_balance`: seed `[10 / 1000]`, saída 3 → `ValueOut 300`, saldo `(7, 700)`.
  - Nenhuma linha com kind de acerto em nenhum cenário (remover/ajustar os testes antigos do acerto).
- [ ] **Step 2:** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FifoLedger"` → FAIL (compilação/asserts).
- [ ] **Step 3:** Implementar as regras acima.
- [ ] **Step 4:** Mesmo comando → PASS; depois `--filter "FullyQualifiedName~Fiscal"` todo verde (ajustar asserts do Kardex/Inventário que esperavam o acerto).
- [ ] **Step 5:** Commit `feat(reports): saída sem saldo recebe o custo da entrada que a cobre`.

### Task 4: Carregador — ajustes, instantâneo e linhas do Kardex

**Files:**
- Modify: `SiagroB1.Infra/FiscalStock/FiscalStockLedgerLoader.cs`, `SiagroB1.Reports/Services/FiscalKardexService.cs` (`Line`, `KindText`), `SiagroB1.Reports/Dtos/FiscalKardexDtos.cs` (comentário do `Kind`)
- Test: `SiagroB1.Application.Tests/Reports/FiscalKardexServiceTests.cs`

**Interfaces:**
- Consumes: Task 2 entidades; Task 3 `Run(movements, seed)`.
- Produces: `LoadAsync(branch, item, toDate, fromDate)`: base = último `FiscalStockPeriod` `Closed` da filial cujo último dia `< (fromDate ?? toDate)`; se existir, semente = `FiscalStockPeriodLayer` do item e só movimentos com data `>` fim do período; saldo inicial com `BalanceDate` ≤ fim do período é ignorado. Ajustes viram `AdjustmentIn/Out` (`LineKey = Key`, `Value = TotalValue`, source com `InternalNumber = DocNumber`, `UsageName = Reason`).
- Kardex: `KindText` → `AdjustmentIn` "Ajuste manual — entrada", `AdjustmentOut` "Ajuste manual — saída"; aviso `Custo pela entrada {nº} de {dd/MM/aaaa}` a partir de `CoveredByLineKeys` (nº e data da linha de entrada coberta). Direção `S` para `AdjustmentOut`.

- [ ] **Step 1: Testes que falham:**
  - `Adjustment_lines_show_in_kardex`: ajuste entrada AJ000001 → linha "Ajuste manual — entrada", nº `AJ000001`, natureza = motivo.
  - `Covered_exit_shows_cost_warning`: saída sem saldo + entrada DE000007 de 25/10/2026 → aviso `Custo pela entrada DE000007 de 25/10/2026`.
  - `Closed_period_snapshot_seeds_later_query`: período 09/2026 fechado com camada 10/1000 e um movimento de 08/2026 que não está no instantâneo; consulta de 01/10 → saldo anterior `(10, 1000)`, o movimento de agosto não influi.
  - `From_after_closed_period_matches_full_recalculation`: mesmo dado coerente com/sem período fechado → mesmos saldos.
- [ ] **Step 2:** `dotnet test ... --filter "FullyQualifiedName~FiscalKardex"` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** `--filter "FullyQualifiedName~Fiscal"` → PASS.
- [ ] **Step 5:** Commit `feat(reports): Kardex com ajuste manual e partida do mês fechado`.

### Task 5: Trava de período fechado

**Files:**
- Create: `SiagroB1.Infra/FiscalStock/FiscalStockPeriodGuard.cs`
- Modify (chamar a trava): `PurchaseInvoicesConfirmService`, `PurchaseInvoicesReverseConfirmService`, `PurchaseInvoicesCancelService`, `PurchaseInvoicesDeleteService`, `PurchaseInvoicesUpdateService` (troca de `PostingDate` em documento confirmado), `PurchaseInvoicesNfeCancelService`, `SalesInvoicesConfirmService`, `SalesInvoicesReverseConfirmService`, `SalesInvoicesCancelService`, `SalesInvoicesDeleteService`, `SalesInvoicesNfeCancelService`, `FiscalInventoryOpeningBalancesService` (create/update/delete, data antiga e nova); registro DI em `ServiceCollectionExtensions` e no Reports
- Test: `SiagroB1.Application.Tests/FiscalStock/FiscalStockPeriodGuardTests.cs` + um teste por serviço tocado (confirmar entrada e cancelar saída no mínimo)

**Interfaces:**
- Produces: `public class FiscalStockPeriodGuard(IUnitOfWork db) { Task EnsureOpenAsync(string branchCode, DateTime stockDate, CancellationToken ct = default); Task<DateTime?> FirstOpenDateAsync(string branchCode, CancellationToken ct = default); Task<bool> IsClosedAsync(string branchCode, int year, int month, CancellationToken ct = default) }` — `EnsureOpenAsync` lança `DefaultException` com a mensagem exata das Global Constraints (`{dd/MM/aaaa}` = 1º dia do mês seguinte ao último fechado).
- A trava só se aplica a documento que movimenta estoque fiscal (alguma linha `MovesFiscalInventory`) e em status que conta (confirmar → data do documento; estorno/cancelamento/exclusão de confirmado → data gravada).

- [ ] **Step 1: Testes que falham:** guard com 09/2026 fechado: `EnsureOpenAsync("01", 2026-09-30)` lança a mensagem `O estoque fiscal de 09/2026 da filial 01 está fechado. Use uma data de entrada no estoque a partir de 01/10/2026 ou peça a reabertura do mês.`; 2026-10-01 passa; `Confirm_purchase_with_posting_date_in_closed_month_is_refused`; `Confirm_purchase_issued_in_closed_month_with_posting_date_in_open_month_passes`; `Cancel_sales_nfe_in_closed_month_is_refused`; `Opening_balance_in_closed_month_cannot_be_edited`.
- [ ] **Step 2:** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FiscalStock|FullyQualifiedName~PurchaseInvoicesConfirm|FullyQualifiedName~SalesInvoicesNfeCancel"` → FAIL.
- [ ] **Step 3:** Implementar a trava e as chamadas.
- [ ] **Step 4:** Mesmo comando → PASS; suíte completa `dotnet test SiagroB1.Application.Tests` sem regressão.
- [ ] **Step 5:** Commit `feat(invoice): documento não mexe no estoque fiscal de mês fechado`.

### Task 6: Ajuste manual — serviço e OData

**Files:**
- Create: `SiagroB1.Application/Services/FiscalStock/FiscalStockAdjustmentsService.cs` (+ interface), `SiagroB1.Web/Controllers/FiscalStockAdjustmentsController.cs` (copiar `FiscalInventoryOpeningBalancesController`, usuário por `User.Identity?.Name ?? "Unknown"`)
- Modify: `ODataConfigurations.cs` (`FiscalStockAdjustments`), `ServiceCollectionExtensions.cs` (serviço + `FiscalStockLedgerLoader` + guard)
- Test: `SiagroB1.Application.Tests/FiscalStock/FiscalStockAdjustmentsServiceTests.cs`, teste de EDM

**Interfaces:**
- Consumes: Task 1 loader, Task 5 guard.
- Produces: `QueryAll()`, `GetByKeyAsync`, `CreateAsync(entity, userName)`, `UpdateAsync(key, entity)`, `DeleteAsync(key)`.
- Validações (mensagens): filial, produto existente, data, `Quantity > 0` ("Informe uma quantidade maior que zero."), `TotalValue >= 0`, motivo obrigatório ("Informe o motivo do ajuste."), trava de período (data nova e antiga). Saída: carregar o ledger até a data e recusar se a quantidade passa do saldo ("Saldo insuficiente para o ajuste de saída: disponível {q}.") ou o valor passa do valor das camadas ("O valor do ajuste passa do valor em estoque: disponível R$ {v}."). `DocNumber` = próximo `AJ` da filial (6 dígitos). Nome/unidade como o saldo inicial.

- [ ] **Step 1: Testes que falham:** criar entrada gera `AJ000001` e depois `AJ000002`; motivo vazio recusa; saída maior que o saldo recusa com a mensagem; saída com valor acima do estoque recusa; data em mês fechado recusa; EDM expõe `FiscalStockAdjustments`.
- [ ] **Step 2:** `--filter "FullyQualifiedName~FiscalStockAdjustments"` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** Commit `feat(reports): ajuste manual do estoque fiscal`.

### Task 7: Fechar e reabrir mês

**Files:**
- Create: `SiagroB1.Application/Services/FiscalStock/FiscalStockPeriodsService.cs`, `SiagroB1.Web/Controllers/FiscalStockPeriodsController.cs` (GET OData) + `SiagroB1.Web/Actions/FiscalStock/FiscalStockPeriodsActionsController.cs` (`POST odata/FiscalStockPeriodsClose` {BranchCode, Year, Month}; `POST odata/FiscalStockPeriodsReopen` {BranchCode, Year, Month, Reason})
- Modify: `ODataConfigurations.cs`, `ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/FiscalStock/FiscalStockPeriodsServiceTests.cs`

**Interfaces:**
- Produces: `CloseAsync(branch, year, month, userName)` → `FiscalStockPeriod`; `ReopenAsync(branch, year, month, reason, userName)`; `QueryAll()`.
- Fechar: mês anterior fechado, ou o mês é o do `BalanceDate` mais antigo dos saldos iniciais da filial ("Feche antes {MM/AAAA}." / "Lance o Saldo Inicial do Estoque antes de fechar."); ledger até o último dia: recusa se algum produto tem `Deficit > 0` ou saldo quantidade 0 com valor ≠ 0 ou `PendingValue ≠ 0`, mensagem listando `{ItemCode} {ItemName}: saldo {q} / R$ {v}` (máx. 20 linhas). Grava status, evento, e uma `FiscalStockPeriodLayer` por camada aberta.
- Reabrir: mês seguinte não pode estar fechado ("Reabra antes {MM/AAAA}."); motivo obrigatório; apaga as camadas do instantâneo, status `Open`, evento.

- [ ] **Step 1: Testes que falham:** fechar sem saldo inicial recusa; fechar com saída pendente recusa listando o item; fechar com quantidade 0 e valor ≠ 0 recusa; fechar grava instantâneo igual às camadas; fechar 10 sem 09 fechado recusa; reabrir 09 com 10 fechado recusa; reabrir apaga instantâneo e grava evento.
- [ ] **Step 2:** `--filter "FullyQualifiedName~FiscalStockPeriods"` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** PASS; suíte completa verde.
- [ ] **Step 5:** Commit `feat(reports): fechamento e reabertura do mês do estoque fiscal`.

### Task 8: Menus

**Files:**
- Create: migration Common `AddFiscalStockAdjustmentAndClosingMenus` (modelo `AddFiscalKardexMenus`): keys `fiscalStockAdjustments` "Ajustes de Estoque Fiscal" (`sap-icon://edit`) e `fiscalStockPeriods` "Fechamento do Estoque Fiscal" (`sap-icon://locked`), pai `fiscal`, Orders logo depois do Kardex (conferir a ordem atual após `ReorderFiscalMenus`), `StandaloneOnly`, `ROLE_MENUS` ADMIN com Guids novos.

- [ ] **Step 1:** Criar a migration e revisar.
- [ ] **Step 2:** `dotnet build SiagroB1.sln` — 0 erros.
- [ ] **Step 3:** Commit `feat(reports): menus de ajuste e fechamento do estoque fiscal` com `DB: AddFiscalStockAdjustmentAndClosingMenus`.

### Task 9: Frontend

**Files (repo `siagro-b1-frontend`, branch `feature/fiscal-stock-adjustments-closing`):**
- Create: `webapp/view/fiscalStockAdjustments/Main.view.xml` + `webapp/controller/fiscalStockAdjustments/Main.controller.ts` (copiar `fiscalInventoryOpeningBalances`: + Tipo Entrada/Saída, Motivo, nº AJ na lista); `webapp/view/fiscalStockPeriods/Main.view.xml` + controller (filial obrigatória, lista de meses com status/quem/quando, botões **Fechar mês** e **Reabrir** com diálogo de motivo; erro do servidor mostrado em `MessageBox.error`)
- Modify: `webapp/manifest.json` (rotas/targets com o mesmo name das keys do menu), `webapp/model/ServerRoutes.ts`, rótulo de `PostingDate` no Documento de Entrada (todas as views/fragments que o mostram) para "Data de entrada no estoque", `package.json` versão +1.

- [ ] **Step 1:** Implementar as duas telas e o rótulo.
- [ ] **Step 2:** `yarn ts-typecheck` e `yarn lint` — sem erro.
- [ ] **Step 3:** Subir a stack local (profile `ceagui`) e, pela home → menu Fiscal: lançar saldo inicial, ajuste de saída, fechar o mês, tentar confirmar entrada com data no mês fechado (mensagem exata), reabrir. Conferir no Kardex que não há "Acerto de saldo negativo".
- [ ] **Step 4:** Commits `feat(reports): telas de ajuste e fechamento do estoque fiscal` e `chore(platform): versão 1.1.x`.
