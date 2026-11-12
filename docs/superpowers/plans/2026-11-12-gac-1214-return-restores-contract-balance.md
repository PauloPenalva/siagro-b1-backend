# GAC-1214 rodada 3 — devolução devolve saldo ao contrato — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A devolução de carga ao armazém passa a devolver saldo ao contrato de compra e toda saída pela liberação de devolução baixa o contrato de novo, com as duas linhas visíveis na aba Entregas.

**Architecture:** Um sincronizador estático e idempotente deriva, do estado das liberações `ReleaseOrigin.SalesReturn`, as linhas do ledger `PURCHASE_CONTRACTS_ALLOCATIONS` (negativa no romaneio gerador, com sinal nos romaneios que consomem a liberação) e regrava `PurchaseContract.AllocatedVolume`. Ele é chamado no gargalo `ShipmentReleasesRecalculateShippedService.RecalculateAsync` e nos poucos fluxos que mudam estado sem passar por lá. A aba Entregas passa a ler só o ledger; uma migration de dados faz o retroativo.

**Tech Stack:** .NET 10, EF Core (SQL Server; testes em EF InMemory + xUnit), Dapper na consulta da aba Entregas, OpenUI5 + TypeScript no frontend.

**Spec:** `docs/superpowers/specs/2026-11-12-gac-1214-return-restores-contract-balance-design.md` — leia antes de qualquer tarefa.

## Global Constraints

- Identificadores em inglês; mensagens ao usuário, rótulos e comentários explicativos em pt-BR.
- Dois repositórios: `siagro-b1-backend` (branch `feature/gac-1214-return-restores-contract-balance`, já criado) e `siagro-b1-frontend` (criar branch de mesmo nome na Task 7). Conferir o branch antes de cada commit. **Nunca push, nunca merge em `main`.**
- Todo arquivo novo recebe `git add` logo após ser criado. Commits com pathspec explícito.
- Mensagem de commit: `tipo(escopo): descrição pt-BR imperativa minúscula` + corpo com o porquê + `Refs: GAC-1214`; commit com migration leva `DB: <NomeDaMigration>`; rodapé `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`. Escopos válidos: `purchase-contract`, `shipment`, `storage`, `invoice`, `reports`.
- Só `ReleaseOrigin.SalesReturn` entra na regra. `Transshipment`, `WarehouseGain`, `OwnershipTransfer` e `Standard` não mudam.
- Liberação "viva" = `Status` ∉ {`Cancelled`, `Completed`}. Romaneio "vivo" = `TransactionStatus != Cancelled` (`Pending` conta).
- Não usar o banco local compartilhado para verificar migration: usar cópia (Task 8). Nenhuma escrita em produção.
- SQL Server 2016: sem `STRING_AGG`; scripts com `SET QUOTED_IDENTIFIER ON`.
- Testes: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Classe>"`. InMemory não enxerga entidades `Add`-adas e não salvas, e o fixup do EF mascara desvínculos — o sincronizador sempre consulta o banco e sempre salva.

## Review Focus

1. Romaneio 12 gerador com liberações em DOIS contratos: cada contrato recebe só a sua parte (Task 1).
2. Liberação cancelada/finalizada com consumo parcial: gerador vale `−ShippedQuantity`, e reabrir volta a `−ReleasedQuantity` (Tasks 1 e 3).
3. Rodar o sync duas vezes seguidas, ou "Recalcular Saldo" do contrato depois dele, não duplica nem altera nada (Tasks 1 e 2).
4. Cancelar a saída (7) de uma liberação de devolução não é barrado pela trava "romaneio tem alocação" e devolve o saldo (Task 4).
5. Contrato encerrado (`Finished`) com liberação de devolução ainda aberta: a reentrega continua funcionando e baixa o contrato (Tasks 1 e 4).

---

### Task 1: Sincronizador das alocações de liberação de devolução

**Files:**
- Create: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsReturnReleaseAllocationSync.cs`
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsReturnReleaseAllocationSyncTests.cs`

**Interfaces:**
- Produces (classe `public static class PurchaseContractsReturnReleaseAllocationSync`):
  - `Task SyncAsync(AppDbContext context, Guid shipmentReleaseKey)` — no-op se a liberação não existe ou `Origin != SalesReturn`.
  - `Task SyncByGeneratorAsync(AppDbContext context, Guid generatorStorageTransactionKey)` — sincroniza todas as liberações `SalesReturn` com esse `GeneratedByStorageTransactionKey`.
  - `Task SyncContractAsync(AppDbContext context, Guid purchaseContractKey)` — sincroniza todas as liberações `SalesReturn` do contrato.
  - `Task<bool> IsDerivedAsync(AppDbContext context, Guid storageTransactionKey)` — verdadeiro se o romaneio é gerador de alguma liberação `SalesReturn` ou é tipo 7/12/13 com `ShipmentReleaseKey` numa liberação `SalesReturn`.
  - Todos salvam (`context.SaveChangesAsync()`) e participam da transação que estiver aberta.

Regra do conjunto esperado, por (contrato da liberação, romaneio):
- gerador vivo → `Volume = −(ShippedCalc + (liberação viva ? ReleasedQuantity − ShippedCalc : 0))`, onde `ShippedCalc` = Σ dos consumos abaixo; se o mesmo gerador tiver mais de uma liberação no mesmo contrato, somar; volume zero não gera linha;
- cada romaneio vivo com `ShipmentReleaseKey == liberação` e tipo 7/13 → `+NetWeight`; tipo 12 → `−NetWeight`.
Aplicação: inserir o que falta (`ApprovedAt = DateTime.Now`, `ApprovedBy = "system"`), corrigir `Volume` divergente, remover alocações do contrato cujo romaneio é o gerador ou um consumidor da liberação e que não estão no esperado. Depois `AllocatedVolume = Σ Volume` do contrato (salvar antes de somar). Sem teto de saldo, sem bloqueio de `Finished`, sem tocar `AvaiableVolumeToAllocate`.

- [ ] **Step 1: Escrever os testes (falhando).** Seed base: contrato `TotalVolume = 10_000`, alocação de compra `+3_000` (romaneio tipo 8), romaneio gerador tipo 12 confirmado de 3 000, liberação `SalesReturn` `Actived` `ReleasedQuantity = 3_000`. Usar `TestDb.CreateUnitOfWork()` como em `StorageEntryTransactionsCreateSalesReturnReleaseTests`. Testes e asserções:
  - `Sync_LiveRelease_GivesTheBalanceBack` → alocação do gerador `−3_000`; `AllocatedVolume == 0`; `AvaiableVolume == 10_000`.
  - `Sync_PartialShipment_ConsumesTheContractAgain` → + romaneio 7 de 1 000 na liberação: alocações `−3_000` e `+1_000`; `AllocatedVolume == 1_000`.
  - `Sync_WarehouseLoss_ConsumesLikeAShipment` → tipo 13 de 200: `+200`.
  - `Sync_ReturnAgainstTheRelease_GivesBack` → tipo 12 COM `ShipmentReleaseKey` de 400 após saída de 1 000: `AllocatedVolume == 600`.
  - `Sync_CancelledShipment_RemovesItsAllocation` → cancelar o 7 e sincronizar: só `−3_000`.
  - `Sync_CancelledGenerator_RemovesTheReturnAllocation` → `AllocatedVolume == 3_000`, nenhuma alocação no gerador.
  - `Sync_CancelledRelease_KeepsOnlyTheConsumedPart` → saída de 1 000, liberação `Cancelled`: gerador `−1_000`, `AllocatedVolume == 3_000`. Mesmo teste com `Completed` (`[Theory]`).
  - `Sync_CancelledReleaseWithoutShipments_LeavesNoGeneratorAllocation` → nenhuma linha de volume zero.
  - `Sync_GeneratorSplitAcrossTwoContracts_EachGetsItsShare` → duas liberações (2 000 e 1 000) do mesmo gerador em contratos A e B: A `−2_000`, B `−1_000`.
  - `Sync_Twice_IsIdempotent` → contagem de alocações e `AllocatedVolume` iguais após a segunda chamada.
  - `Sync_NonSalesReturnOrigin_DoesNothing` (`[Theory]` Standard/Transshipment/WarehouseGain/OwnershipTransfer) → nenhuma alocação nova.
  - `Sync_FinishedContract_StillSyncs` → contrato `Finished`, saída de 1 000: não lança, `AllocatedVolume == 1_000`.
  - `SyncByGenerator_And_SyncContract_CoverAllReleases` e `IsDerived_TrueForGeneratorAndConsumers_FalseForPurchase`.
- [ ] **Step 2:** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractsReturnReleaseAllocationSyncTests"` → falha de compilação (classe inexistente).
- [ ] **Step 3:** Implementar a classe conforme Interfaces e a regra acima. `git add` do arquivo novo.
- [ ] **Step 4:** Mesmo comando → todos passam.
- [ ] **Step 5:** Commit `feat(purchase-contract): sincroniza alocações das liberações de devolução`.

### Task 2: Disparo no recálculo da liberação e no recálculo de saldo do contrato

**Files:**
- Modify: `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesRecalculateShippedService.cs` (`RecalculateAsync`, ~109-120)
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsRecalculateBalanceService.cs` (`RecalculateAsync` privado)
- Test: `SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesRecalculateShippedServiceTests.cs`, `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsRecalculateBalanceServiceTests.cs`

**Interfaces:**
- Consumes: `PurchaseContractsReturnReleaseAllocationSync.SyncAsync` / `SyncContractAsync` (Task 1).
- Produces: `ShipmentReleasesRecalculateShippedService.RecalculateAsync(Guid)` passa a sincronizar o ledger quando `release.Origin == SalesReturn` (depois do `SaveChanges` do `ShippedQuantity`). O construtor NÃO muda (há dezenas de `new ...(ctx)` nos testes).

- [ ] **Step 1: Testes.**
  - `Recalculate_SalesReturnRelease_SyncsTheContractLedger` → liberação de devolução 3 000 + saída 7 de 1 000; após `RecalculateAsync`: `ShippedQuantity == 1_000` e `contract.AllocatedVolume` = compra 3 000 − 3 000 + 1 000 = `1_000`.
  - `Recalculate_StandardRelease_DoesNotTouchTheLedger` → nenhuma alocação criada.
  - Em `PurchaseContractsRecalculateBalanceServiceTests`: `Execute_ContractWithOpenSalesReturnRelease_SyncsBeforeSumming` → ledger só com a compra `+3_000` e liberação de devolução viva de 3 000 sem alocações; após `ExecuteAsync`: `NewAllocatedVolume == 0`. E `ExecuteAll_...` equivalente.
- [ ] **Step 2:** Rodar os dois filtros → falham.
- [ ] **Step 3:** Implementar. No `PurchaseContractsRecalculateBalanceService`, chamar `SyncContractAsync` antes de `CalculateAllocatedAsync` e reler `contract.AllocatedVolume` de modo que `PreviousAllocatedVolume` continue sendo o valor anterior ao recálculo.
- [ ] **Step 4:** Rodar os dois filtros e também `--filter "FullyQualifiedName~ShipmentReleases"` → passam.
- [ ] **Step 5:** Commit `feat(shipment): recálculo da liberação de devolução atualiza o saldo do contrato`.

### Task 3: Nascimento, estorno e mudança de status da liberação de devolução

**Files:**
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadRefusalEffectsService.cs` (`EmitReturnReleasesAsync`, após o `SaveChangesAsync` ~362)
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesReturnService.cs` (após o `SaveChangesAsync` ~322)
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesReverseConfirmService.cs` (~435 e ~480, onde cancela gerador e liberação)
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsCancelRefusalService.cs` e qualquer outro serviço que cancele o romaneio 12 gerador ou sua liberação (procurar por `GeneratedByStorageTransactionKey` e `RefusedFromShipmentLoadKey` em `SiagroB1.Application/Services`)
- Modify: `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesCancelationService.cs` (~70), `ShipmentReleasesCloseService.cs`, `ShipmentReleasesReopenService.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsRefuseServiceTests.cs`, `ShipmentLoadsCancelRefusalTests.cs`, `SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesCancelationServiceTests.cs`, `ShipmentReleasesCloseReopenServiceTests.cs`, e a classe de teste existente do `SalesInvoicesReturnService`/`ReverseConfirm` em `SiagroB1.Application.Tests/SalesInvoices`

**Interfaces:**
- Consumes: `SyncByGeneratorAsync(context, entry.Key)` na criação e no estorno da devolução; `SyncAsync(context, release.Key)` em cancelar/finalizar/reabrir a liberação.

- [ ] **Step 1: Testes** (reaproveitar os seeds de cada classe existente):
  - `Refuse_ToWarehouse_GivesTheContractBalanceBack` → após a recusa, `contract.AllocatedVolume` cai pelo volume devolvido e existe alocação negativa no romaneio 12.
  - `CancelRefusal_TakesTheContractBalanceAgain` → `AllocatedVolume` volta ao valor de antes da recusa; sem alocação no 12.
  - `Return_ByInvoice_GivesTheContractBalanceBack` e `ReverseReturn_TakesItAgain` (retorno de NF).
  - `Cancel_SalesReturnReleaseWithPartialShipment_KeepsOnlyConsumedPart` → ledger = compra (ver exemplo da spec: fecha em 30 para 30 comprados).
  - `Close_SalesReturnReleaseWithOpenBalance_StopsGivingBalanceBack` e `Reopen_SalesReturnRelease_GivesItBackAgain`.
- [ ] **Step 2:** Rodar os filtros das classes tocadas → os novos falham.
- [ ] **Step 3:** Inserir as chamadas. Se algum desses fluxos roda em transação, a chamada fica DENTRO dela, depois do `SaveChanges` que grava a mudança de estado.
- [ ] **Step 4:** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ShipmentLoads|FullyQualifiedName~ShipmentReleases|FullyQualifiedName~SalesInvoices"` → passam. Testes antigos que afirmavam "contrato continua baixado na recusa" devem ser reescritos para a regra nova, não apagados.
- [ ] **Step 5:** Commit `feat(shipment): devolução ao armazém devolve o saldo ao contrato de compra`.

### Task 4: Saídas pela liberação de devolução e travas de cancelamento

**Files:**
- Modify: `SiagroB1.Application/Services/StorageEntryTransactions/StorageEntryTransactionsCreateService.cs` (comentários do `<summary>`; mover o `recalcShipped.RecalculateAsync` para dentro da transação quando `semPernaDeCompra`)
- Modify: `SiagroB1.Application/Services/StorageEntryTransactions/StorageEntryTransactionsCancelService.cs` (~46-68)
- Modify: `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsCancelService.cs` (~121) e `StorageTransactionsReverseService.cs` (~122)
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsAllocationDeleteService.cs` (entradas públicas usadas pela tela/API)
- Modify (só comentários que ficaram falsos): `SiagroB1.Domain/Enums/ReleaseOriginRules.cs`, `ShipmentReleasesFromReturnService.cs` (texto do `Comments` da liberação: trocar "o embarque desta liberação não debita o contrato" por "a devolução devolveu o saldo ao contrato; o embarque desta liberação o baixa de novo"), `ShippingTransactionsCreateService.cs`
- Test: `SiagroB1.Application.Tests/StorageEntryTransactions/StorageEntryTransactionsCreateSalesReturnReleaseTests.cs`, `StorageEntryTransactionsCancelServiceTests.cs`, `SiagroB1.Application.Tests/ShippingTransactions/` (classe que cobre embarque por liberação de devolução), `SiagroB1.Application.Tests/StorageTransactions/` (cancel/reverse), `PurchaseContractsAllocationDeleteServiceTests.cs`

**Interfaces:**
- Consumes: `IsDerivedAsync`, `SyncAsync` (Task 1); o disparo da Task 2.

- [ ] **Step 1: Testes.**
  - Renomear `Execute_SalesReturnRelease_DoesNotAllocateTheContractAgain` para `Execute_SalesReturnRelease_ConsumesTheContractAgain`: seed com alocação do gerador já sincronizada; após a entrada de 1 500: existe alocação `+1_500` no romaneio 7 e `AllocatedVolume` sobe 1 500; `entry.AllocatedVolume` continua `0` (o campo é da perna de compra).
  - `Cancel_SalesReturnEntry_GivesTheContractBalanceBackToo` → após cancelar a entrada, sem alocação no 7 e `AllocatedVolume` volta; **não** lança por contrato `Finished` (`[Fact]` separado: `Cancel_SalesReturnEntry_OnFinishedContract_IsAllowed`).
  - Expedição: `Ship_FromSalesReturnRelease_ConsumesTheContractAgain` e o estorno correspondente devolve.
  - `Cancel_ShipmentOfSalesReturnRelease_IsNotBlockedByDerivedAllocation` (StorageTransactionsCancelService) e o equivalente do Reverse; `Cancel_PurchaseWithAllocation_StillBlocked` continua valendo.
  - `Delete_DerivedAllocation_IsRefused` → mensagem exata: `"Alocação gerada pela devolução ao armazém: ela é mantida pelo sistema e não pode ser removida."`
- [ ] **Step 2:** Rodar os filtros → falham.
- [ ] **Step 3:** Implementar. Nas travas "tem alocação", ignorar quando `IsDerivedAsync(doc.Key)`; o sync disparado pelo recálculo após a mudança de status remove a linha. Em `StorageEntryTransactionsCancelService`, quando a entrada é de liberação sem perna de compra, pular o laço do `allocationDeleteService` e a trava de `Finished`.
- [ ] **Step 4:** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageEntryTransactions|FullyQualifiedName~StorageTransactions|FullyQualifiedName~ShippingTransactions|FullyQualifiedName~PurchaseContracts"` → passam.
- [ ] **Step 5:** Commit `feat(storage): saída pela liberação de devolução baixa o contrato de novo`.

### Task 5: Aba Entregas, extrato e demais leitores do ledger

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsGetAllocationsByContractService.cs`
- Modify: `SiagroB1.Reports/Services/ContractStatementReportService.cs` (`PurchaseMovement`, ~529)
- Conferir (corrigir só se tratar a alocação derivada como compra): `PurchaseFiscalReconciliationQuery.cs`, `PurchaseFiscalReconciliationService.cs`, `PurchaseDischargesListService.cs`, `PurchaseDischargeTransactionsSyncService.cs`, `PurchaseTransactionsChangeSupplierService.cs`, `OwnershipTransfersCancelService.cs`, `WarehouseReconciliationsCancelService.cs`, `ShippingTransactionsReverseService.cs`, `ShippingTransactionsChangeReleaseService.cs`, `PurchaseContractLotStatementReportService.cs`, `ContractPositionData.cs`, `MarketPhysicalExposureService.cs`, `PurchaseContractsTotalsService.cs` e os serviços de liberação de pagamento (GAC-1188, buscar `PaymentRelease` em `Services/PurchaseContracts`)
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsGetAllocationsByContractSqlTests.cs`, teste do `ContractStatementReportService.PurchaseMovement` em `SiagroB1.Application.Tests/Reports`

**Interfaces:**
- Produces (valores que o frontend da Task 7 consome, no DTO `PurchaseContractAllocationsByContractDto`):
  - devolução (romaneio 12 gerador): `StorageTransactionTypeName = "Devolução"`, `Origin = "LoadRefusal"`, `Volume` negativo, `ReasonDescription = "Carga {código} recusada"` (ou `"Devolução ao armazém"` sem carga), `LoadCode`/`DischargeLoadCode` = carga de `RefusedFromShipmentLoadKey`;
  - reentrega (7/13/12 com chave em liberação `SalesReturn`): `StorageTransactionTypeName = "Reentrega"`, `Origin = "ReturnRedelivery"`, `Volume` com o sinal do ledger, `ReasonDescription = "Saída da devolução {código do romaneio gerador}"`, `LoadCode` = carga do próprio romaneio;
  - ambas com `FiscalStatus = null`.

- [ ] **Step 1: Testes.** No teste de SQL: remover `Returns_query_links_refused_loads_through_the_sales_return_release`; adicionar `Main_query_labels_sales_return_rows` (o SQL contém `'Devolução'`, `'Reentrega'`, `'LoadRefusal'`, `'ReturnRedelivery'` e junta `SHIPMENT_RELEASES` com `WITH (NOLOCK)`), e garantir que `Every_table_reference_has_nolock_hint` não lista mais `ReturnsSql`. `PurchaseMovement(SalesShipmentReturn) == "Devolução"`, `PurchaseMovement(SalesShipment) == "Reentrega"`, `PurchaseMovement(WarehouseLoss) == "Reentrega (perda)"`.
- [ ] **Step 2:** Rodar → falham.
- [ ] **Step 3:** Implementar: remover `ReturnsSql` e seu laço; estender o `CASE` de tipo e de origem no SQL principal (a origem depende de a liberação ligada ser `Origin = 2`: gerador via `SHIPMENT_RELEASES.GeneratedByStorageTransactionKey`, consumidor via `ST.ShipmentReleaseKey`); cuidar para o `SELECT DISTINCT` não duplicar linha quando o gerador tem liberações em mais de um contrato (filtrar a liberação pelo contrato consultado). Zerar o status fiscal dessas linhas no mapeamento.
- [ ] **Step 4:** Percorrer a lista "Conferir" e anotar, no corpo do commit, o que cada leitor passa a mostrar; corrigir o que quebrar com teste próprio. Rodar `dotnet build SiagroB1.sln` e `dotnet test SiagroB1.Application.Tests` completo → verde.
- [ ] **Step 5:** Commit `feat(purchase-contract): mostra devolução e reentrega nas entregas a partir do ledger`.

### Task 6: Migration de dados retroativa

**Files:**
- Create: `SiagroB1.Migrations/AppContext/<timestamp>_BackfillSalesReturnReleaseAllocations.cs` (+ `.Designer.cs`) via `dotnet ef migrations add BackfillSalesReturnReleaseAllocations --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`
- Test: `SiagroB1.Application.Tests/Infra/` (seguir o padrão dos testes de migration de backfill existentes, ex. o de `BackfillStorageReceiptWeightRounding`, se houver; senão a verificação é a Task 8)

- [ ] **Step 1:** Gerar a migration vazia (o snapshot não pode mudar; se mudar, há drift de modelo — parar e relatar).
- [ ] **Step 2:** `Up` com `migrationBuilder.Sql` (um lote, `SET QUOTED_IDENTIFIER ON`, idempotente por `NOT EXISTS (contrato + romaneio)`), na ordem: (a) consumidores: romaneios `TransactionStatus <> 2`, tipo 7/13 → `+NetWeight`, tipo 12 → `−NetWeight`, com `ShipmentReleaseKey` em liberação `Origin = 2`; (b) geradores tipo 12 `TransactionStatus <> 2`: por (gerador, contrato), `−Σ(CASE WHEN SR.Status IN (2,3) THEN shipped ELSE SR.ReleasedQuantity END)` onde `shipped` é a soma com sinal dos consumidores vivos da liberação (NÃO confiar em `ShippedQuantity` gravado); não inserir zero; (c) `UPDATE PURCHASE_CONTRACTS SET AllocatedVolume = Σ Volume` para os contratos tocados. Preencher as colunas obrigatórias de `BaseEntity` como os outros backfills fazem (`[Key] = NEWID()`, `ApprovedBy = 'migration'`, datas `GETDATE()`). `Down`: apaga as alocações cujo romaneio é derivado (mesmos critérios) e refaz (c).
- [ ] **Step 3:** `dotnet build SiagroB1.sln` → verde. `git add` dos dois arquivos.
- [ ] **Step 4:** Commit `feat(purchase-contract): retroage as alocações das devoluções ao armazém` com `DB: BackfillSalesReturnReleaseAllocations`.

### Task 7: Frontend — legenda e tooltip

**Files (repo `siagro-b1-frontend`, branch novo `feature/gac-1214-return-restores-contract-balance` a partir de `main`):**
- Modify: `webapp/helpers/AllocationOriginHelpers.ts` (~57-63 e ~109)
- Test: o teste unitário existente desse helper em `webapp/test/unit` (criar caso não exista, no padrão QUnit do repo)

**Interfaces:**
- Consumes: `Origin = "LoadRefusal" | "ReturnRedelivery"`, `ReasonDescription`, `DischargeLoadCode`, `LoadCode` (Task 5).

- [ ] **Step 1: Testes** — `LoadRefusal`: texto `"Devolução"`, tooltip `"Carga {código} recusada: a devolução ao armazém devolveu o saldo ao contrato."`; `ReturnRedelivery`: legenda `"Reentrega de devolução"`, texto `"Reentrega"`, tooltip `"Saída pela liberação de devolução: o contrato foi baixado de novo."`.
- [ ] **Step 2:** Rodar os testes unitários do helper → falham.
- [ ] **Step 3:** Implementar; conferir na view da aba Entregas do contrato de compra que a coluna Tipo usa `StorageTransactionTypeName` e que quantidade negativa já é exibida com sinal (GAC-1212).
- [ ] **Step 4:** `npm run ts-typecheck`, `npm run lint` e os testes do helper → verdes.
- [ ] **Step 5:** Commit (repo frontend) `feat(purchase-contract): legenda de devolução e reentrega nas entregas`, `Refs: GAC-1214`.

### Task 8: Verificação em cópia do banco e pelo caminho do usuário

**Files:** nenhum arquivo de produto.

- [ ] **Step 1:** Criar uma cópia de banco local (nunca o banco compartilhado, nunca produção) com dados que contenham liberações de devolução; aplicar a migration nela com `dotnet ef database update --context AppDbContext` apontando a connection string para a cópia.
- [ ] **Step 2:** Conferir por SQL: para todo contrato, `AllocatedVolume = Σ Volume` do ledger; para toda liberação `Origin = 2` viva com gerador vivo, `−(Σ alocações do gerador+consumidores no contrato) = ReleasedQuantity − shipped`. Rodar a migration duas vezes (Down/Up) → mesmo resultado.
- [ ] **Step 3:** Subir a stack contra a cópia e, pela tela: recusar uma carga para um armazém → saldo do contrato sobe e linha "Devolução" negativa aparece; dar Entrada em Armazenagem pela liberação de devolução → saldo cai e linha "Reentrega" aparece; cancelar a entrada → volta; "Recalcular Saldo" não muda nada.
- [ ] **Step 4:** `dotnet test SiagroB1.Application.Tests` e `dotnet test SiagroB1.Fiscal.Tests` completos + `npm test` do frontend → verdes. Relatar ao usuário os números obtidos e a consulta de conferência para rodar em produção após o deploy (esperado: 00005330 → 147.320; 00005248 → 620; 00005073 e 00005280 inalterados, com as linhas novas).
