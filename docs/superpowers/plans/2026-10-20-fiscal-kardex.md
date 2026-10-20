# Kardex Fiscal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Estoque fiscal por filial + produto valorizado a FIFO sem ICMS creditado: cadastro de Saldo Inicial e consulta Kardex Fiscal, ambos no menu Fiscal (só STANDALONE).

**Architecture:** Um motor FIFO puro (sem EF) recebe movimentos normalizados e devolve linhas valorizadas + camadas em aberto. Um carregador em `SiagroB1.Reports` lê saldo inicial e linhas de nota (filtro "escriturável" extraído da Conferência Fiscal) e alimenta o motor; `POST /reports/FiscalKardex` devolve JSON. Saldo inicial é entidade nova com CRUD OData no `SiagroB1.Web` (modelo: `RuralContributionRates`).

**Tech Stack:** .NET 10, EF Core (InMemory nos testes), xUnit; OpenUI5 TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-20-fiscal-kardex-design.md` — as regras numeradas em "Detalhes resolvidos" são vinculantes.

## Global Constraints

- Identificadores em inglês; textos ao usuário em pt-BR.
- Só STANDALONE: endpoints recusam SAPB1 com `"Disponível somente no modo STANDALONE."`; menus `StandaloneOnly = true`.
- Custo de entrada = `GrandTotal − IcmsValue`; quantidade = `Quantity`; direção pelo CFOP (1/2/3 entra, 5/6/7 sai; vazio → direção do documento).
- Valores em `decimal`; custo unitário arredondado a 6 casas, valores a 2 casas (`MidpointRounding.ToEven`); a soma das baixas de uma camada nunca pode deixar valor residual com quantidade zero (última baixa leva o resto do valor).
- Branch `feature/fiscal-kardex` (be) e `feature/fiscal-kardex` (fe, a partir de `feature/fiscal-conference`); commit por tarefa; migration exige trailer `DB:`; nunca push.

## Review Focus

1. Camada consumida em várias saídas com quantidades "quebradas" (ex.: 3 saídas de 1/3) — a soma das baixas deve igualar o valor da camada, sem centavo sobrando. Teste na Task 2.
2. Saída maior que o saldo seguida de entrada — linha "Acerto de saldo negativo" e saldo = soma das camadas. Teste na Task 2.
3. Período "de" anterior à data do saldo inicial — saldo anterior zero, saldo inicial como primeira linha. Teste na Task 3.
4. Produto sem movimento no período mas com saldo — aparece com saldo anterior = saldo final. Teste na Task 3.
5. Nota cancelada/não autorizada depois de ter entrado — some do Kardex na próxima consulta (recalculado). Teste na Task 3.

---

### Task 1: Filtro "escriturável" compartilhado

**Files:**
- Create: `SiagroB1.Reports/Services/Fiscal/BookableInvoiceLines.cs`
- Modify: `SiagroB1.Reports/Services/FiscalConferenceService.cs` (usar o helper em `LoadLinesAsync` e no R-2055)
- Test: existentes `FiscalConferenceServiceTests` (sem mudança de comportamento) + `SiagroB1.Application.Tests/Reports/BookableInvoiceLinesTests.cs`

**Interfaces:**
- Produces (static class `BookableInvoiceLines`):
  - `IQueryable<SalesInvoiceItem> Sales(IQueryable<SalesInvoiceItem> source)` — Confirmado/Retornado, `TaxDocumentKind` Nfe/Other, NF-e não void, sem `WithoutTaxDocument`, NF-e própria só Authorized.
  - `IQueryable<PurchaseInvoiceItem> Purchases(IQueryable<PurchaseInvoiceItem> source)` — idem; "própria" = `IssuerType.Own` + Nfe.
  - `IQueryable<SalesInvoiceItem> UnauthorizedOwnSales(...)` / `UnauthorizedOwnPurchases(...)` — as que a Conferência lista na regra 6.
- [ ] **Step 1:** testes `Sales_KeepsOnlyBookable` e `Purchases_KeepsOnlyBookable` (um caso por exclusão do spec da Conferência).
- [ ] **Step 2:** rodar `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~BookableInvoiceLines"` → FAIL.
- [ ] **Step 3:** implementar e trocar a Conferência para usá-lo (refactor puro).
- [ ] **Step 4:** `--filter "FullyQualifiedName~BookableInvoiceLines|FullyQualifiedName~FiscalConference"` → PASS.
- [ ] **Step 5:** commit `refactor(reports): extrai o filtro de notas escrituráveis`.

### Task 2: Motor FIFO

**Files:**
- Create: `SiagroB1.Reports/Services/Fiscal/FifoLedger.cs`, `SiagroB1.Reports/Services/Fiscal/FifoLedgerModels.cs`
- Test: `SiagroB1.Application.Tests/Reports/FifoLedgerTests.cs`

**Interfaces:**
- Produces:
  - `enum InventoryMovementKind { Opening, Entry, Exit, PurchaseReturn, SalesReturn, ValueComplement }`
  - `record InventoryMovement(Guid LineKey, DateTime Date, int DaySequence, InventoryMovementKind Kind, decimal Quantity, decimal Value /* entradas/complemento: custo; saídas: 0 */, Guid? OriginLineKey, string? UnitOfMeasure, object? Source /* opaco p/ o chamador */)`
  - `record ValuedMovement(InventoryMovement Movement, decimal QuantityIn, decimal QuantityOut, decimal UnitCost, decimal ValueIn, decimal ValueOut, decimal BalanceQuantity, decimal BalanceValue, bool InsufficientBalance, bool IsNegativeBalanceAdjustment)`
  - `record FifoLayer(Guid LineKey, DateTime Date, decimal Quantity, decimal Value)`
  - `static FifoResult FifoLedger.Run(IEnumerable<InventoryMovement> movements)` com `FifoResult(List<ValuedMovement> Lines, List<FifoLayer> OpenLayers)`; ordena por Date, entradas (Opening, Entry, SalesReturn, ValueComplement) antes de saídas, depois DaySequence.
- Regras (spec itens 4–6): saída consome camadas mais antigas; falta → resto a custo 0 e `InsufficientBalance`; entrada seguinte cobre o déficit gerando um `ValuedMovement` extra com `IsNegativeBalanceAdjustment` (saída pelo custo da entrada) antes de criar a camada com o restante; `PurchaseReturn` baixa da camada `OriginLineKey` pelo custo unitário dela até o saldo dela, excedente FIFO; `SalesReturn` cria camada pelo custo unitário com que a saída `OriginLineKey` foi baixada (ValueOut ÷ QuantityOut; 0 se não achar); `ValueComplement` soma o valor à camada de origem se ainda aberta, senão à camada mais antiga aberta, senão fica pendente e é somado à próxima camada criada (nunca existe camada com quantidade 0). Teste: `ValueComplement_WithNoOpenLayer_GoesToNextLayer` (entrada A 10 @ 100,00; saída 10; complemento de A 30,00; entrada B 10 @ 200,00 → camada B 10 / 230,00).
- [ ] **Step 1: testes** (valores exatos):
  - `Exit_ConsumesOldestLayerFirst`: entradas 10 @ 100,00 e 10 @ 120,00 (valor total 1000 e 1200); saída 15 → ValueOut 1600,00; saldo 5 / 600,00; OpenLayers = [(2ª, 5, 600,00)].
  - `Exit_SplitsLastCentInLastConsumption`: entrada 3 @ total 100,00; três saídas de 1 → 33,33; 33,33; 33,34; saldo 0 / 0,00.
  - `Exit_WithoutBalance_GoesOutAtZeroAndFlags`: saída 5 sem saldo → ValueOut 0, InsufficientBalance, saldo −5 / 0.
  - `Entry_AfterDeficit_CreatesAdjustment`: saída 5 (sem saldo), entrada 10 @ 200,00 → linha ajuste QuantityOut 5 ValueOut 100,00 IsNegativeBalanceAdjustment; saldo final 5 / 100,00; OpenLayers soma = saldo.
  - `PurchaseReturn_UsesOriginLayerCost`: entradas A 10 @ 100,00, B 10 @ 200,00; devolução de B, 4 → ValueOut 80,00; camada B fica 6 / 120,00; A intacta.
  - `SalesReturn_UsesOriginExitCost`: entrada 10 @ 100,00; saída S 4 (40,00); devolução de S 2 → ValueIn 20,00.
  - `ValueComplement_AddsToOriginLayer`: entrada A 10 @ 100,00; complemento de A 50,00 → camada A 10 / 150,00; saldo 150,00.
  - `Opening_IsOldestLayer`: Opening 5 / 50,00 + entrada 5 @ 100,00 + saída 5 → ValueOut 50,00.
- [ ] **Step 2:** `--filter FullyQualifiedName~FifoLedger` → FAIL. **Step 3:** implementar. **Step 4:** PASS.
- [ ] **Step 5:** commit `feat(reports): motor FIFO do estoque fiscal`.

### Task 3: Saldo inicial (entidade) + serviço Kardex + endpoint

**Files:**
- Create: `SiagroB1.Domain/Entities/FiscalInventoryOpeningBalance.cs` (Key Guid via `BaseEntity`, `BranchCode` string(10) req, `ItemCode` string(50) req, `ItemName` string?, `BalanceDate` DateTime, `Quantity` decimal(19,6), `TotalValue` decimal(19,2), `UnitOfMeasureCode` string?), mapeamento em `SiagroB1.Infra/Context/AppDbContext.cs` (tabela `FISCAL_INVENTORY_OPENING_BALANCES`, índice único BranchCode+ItemCode), migration `AddFiscalInventoryOpeningBalances` (AppContext).
- Create: `SiagroB1.Reports/Dtos/FiscalKardexDtos.cs`, `SiagroB1.Reports/Services/FiscalKardexService.cs`, `SiagroB1.Reports/Controllers/FiscalKardexController.cs`
- Test: `SiagroB1.Application.Tests/Reports/FiscalKardexServiceTests.cs`, `FiscalKardexControllerTests.cs`

**Interfaces:**
- Consumes: Task 1 helpers; `FifoLedger.Run`.
- Produces:
  - `FiscalKardexRequest { string BranchCode; string? ItemCode; DateTime FromDate; DateTime ToDate; }`
  - `FiscalKardexData { List<FiscalKardexItemDto> Items; }`; `FiscalKardexItemDto { string ItemCode; string ItemName; string? UnitOfMeasureCode; decimal OpeningQuantity; decimal OpeningValue; List<FiscalKardexLineDto> Lines; decimal ClosingQuantity; decimal ClosingValue; }`
  - `FiscalKardexLineDto { DateTime Date; string Direction /* "E"|"S" */; string Kind /* pt-BR: "Saldo inicial","Entrada","Saída","Devolução de compra","Devolução de venda","Complemento de valor","Acerto de saldo negativo" */; string InternalNumber; string DocumentNumber; string Partner; string Cfop; string UsageName; decimal QuantityIn; decimal QuantityOut; decimal UnitCost; decimal ValueIn; decimal ValueOut; decimal BalanceQuantity; decimal BalanceValue; decimal InvoiceValue /* GrandTotal da nota, conferência */; string? Warning /* "Saldo insuficiente" | "Unidade diferente" */; }`
  - `FiscalKardexService(IUnitOfWork db)`: `Task<FiscalKardexData> BuildAsync(FiscalKardexRequest)` e `Task<List<FifoLayer>> OpenLayersAsync(string branchCode, string? itemCode, DateTime atDate)` (para o Inventário futuro).
  - Controller `POST /reports/FiscalKardex` → `Ok(FiscalKardexData)`; BadRequest SAPB1; BadRequest `"Informe a filial e um período válido."` quando BranchCode vazio, FromDate > ToDate ou ano fora de 2000–9999.
- Classificação da linha (spec 3): venda `SalesInvoiceType.Return` com `SalesInvoiceItemOriginKey` → SalesReturn; compra `PurchaseInvoiceType.Return` com `PurchaseInvoiceItemOriginKey` → PurchaseReturn; `PurchaseInvoiceType.ValueComplement` com `ComplementedItemKey` → ValueComplement (quantidade 0); `IcmsComplement` (venda ou compra) → ignorado; demais pela direção do CFOP → Entry/Exit; só `MovesFiscalInventory = true` e `ItemCode` não vazio. Movimentos até a `BalanceDate` do saldo inicial (inclusive) são descartados.
- [ ] **Step 1: testes** `Build_OpeningPlusEntryAndSale` (saldo inicial 10 / 1.000,00 em 30/09; compra 10 @ 1.200,00 em 05/10 com ICMS 0; venda 15 em 10/10 → saída 1.600,00; saldo final 5 / 600,00); `Build_EntryCostExcludesIcms` (GrandTotal 1.000, ICMS 120 → custo 880,00); `Build_IgnoresLinesNotMovingInventory`; `Build_IgnoresMovementsUpToOpeningDate`; `Build_FromBeforeOpeningShowsOpeningAsFirstLine`; `Build_ItemWithoutMovementInPeriodShowsBalance`; `Build_CancelledNfeDisappears`; `Build_SalesReturnWithEntryCfopIsEntry`; `Build_IcmsComplementIsIgnored`; `Build_DifferentUnitIsFlagged`; `Build_PreviousBalanceComputedBeforeFrom` (movimentos antes do "de" entram no saldo anterior, não nas linhas). Controller: `Post_RefusesInSapb1Mode`, `Post_RejectsInvalidRequest` (theory), `Post_ReturnsDataInStandalone`.
- [ ] **Step 2:** `--filter FullyQualifiedName~FiscalKardex` → FAIL. **Step 3:** implementar (migration gerada com `ASPNETCORE_ENVIRONMENT=Development` explícito, `--context AppDbContext --output-dir AppContext`; não aplicar em banco). **Step 4:** PASS + suíte inteira + `dotnet build SiagroB1.sln`.
- [ ] **Step 5:** commit `feat(reports): Kardex fiscal valorizado a FIFO` com `DB: AddFiscalInventoryOpeningBalances`.

### Task 4: CRUD OData do saldo inicial + menus

**Files:**
- Create: `SiagroB1.Application/Services/FiscalInventory/FiscalInventoryOpeningBalancesService.cs` (+ interface), `SiagroB1.Web/Controllers/FiscalInventoryOpeningBalancesController.cs`; Modify `ODataConfigurations.cs`, `ServiceCollectionExtensions.cs` (registro à mão). Modelo exato: `RuralContributionRates*`.
- Create: migration CommonContext `AddFiscalKardexMenus`: `fiscalInventoryOpeningBalances` "Saldo Inicial do Estoque" icon `sap-icon://inventory` Order 6; `fiscalKardex` "Kardex Fiscal" icon `sap-icon://list` Order 7; ParentKey `fiscal`, StandaloneOnly true, ROLE_MENUS ADMIN (Guids novos fixos).
- Test: `SiagroB1.Application.Tests/FiscalInventory/FiscalInventoryOpeningBalancesServiceTests.cs`
- Regras: Quantity > 0 (`"Informe uma quantidade maior que zero."`), TotalValue ≥ 0 (`"O valor não pode ser negativo."`), par filial+produto único (`"Já existe saldo inicial para este produto nesta filial."`), filial e produto obrigatórios; ItemName/UoM preenchidos a partir do cadastro do produto; recusa em SAPB1.
- [ ] Steps: testes de cada regra → FAIL → implementar → PASS (+ suíte) → commit `feat(reports): cadastro do saldo inicial do estoque fiscal` com `DB: AddFiscalKardexMenus`.

### Task 5: Telas (frontend, worktree `wt-fiscal-kardex-fe`)

**Files:** `webapp/view|controller/fiscalInventoryOpeningBalances/` (lista + diálogo incluir/editar, excluir com confirmação — modelo `ruralContributionRates`), `webapp/view|controller/fiscalKardex/Main.*` (modelo `fiscalConference`: filtros Filial obrigatória, Produto opcional com value help, De/Até; grade `sap.ui.table.Table` com uma linha de cabeçalho por produto "Saldo anterior" e "Saldo final", colunas do `FiscalKardexLineDto`, linha com `Warning` destacada; Exportar Excel), `ServerRoutes.ts` (`fiscalKardex: '/reports/FiscalKardex'`), `manifest.json` rotas `fiscal-inventory-opening-balances` / `fiscal-kardex` com names = keys do menu.
- [ ] Steps: implementar; `tsc --noEmit` e lint limpos; commit `feat(reports): telas do Kardex fiscal e do saldo inicial`. Verificação no navegador fica com o controlador (CEAGUI, pelo menu, a partir da home).
