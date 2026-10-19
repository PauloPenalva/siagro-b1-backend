# Troca de fornecedor/armazém na alocação de romaneios — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Alocar um romaneio de compra (8) num contrato de outro fornecedor/armazém gera estorno 9 na origem + romaneio 8 novo no destino, alocado, ligados por `PURCHASE_TRANSACTION_CHANGES`.

**Architecture:** Serviço novo `PurchaseTransactionsChangeSupplierService` (irmão de `ShippingTransactionsChangeReleaseService`, mesmo padrão de create/confirm Deferred + `SaveChanges` antes de cada alocação), ação OData nova, invariante do saldo alocável estendido no domínio, travas em cancelar/estornar/alocação comum e ajuste da Conciliação Fiscal. Frontend: o OK do aviso de divergência pede motivo e chama a ação nova.

**Tech Stack:** .NET 10, EF Core (SQL Server; testes com InMemory), OData v4, xUnit; OpenUI5 1.141 TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-19-purchase-transaction-supplier-change-design.md` (leia antes de cada tarefa).

**Onde trabalhar:** worktrees `C:\Projetos\SiagroB1\worktrees\allocation-any-supplier\{backend,frontend}`, branch `feature/allocation-any-supplier` nos dois. Nunca no diretório principal (outra sessão troca o branch dele). Nunca push.

## Global Constraints

- Identificadores em inglês; mensagens ao usuário e comentários em pt-BR.
- Commits: `tipo(escopo): descrição` pt-BR, escopo `purchase-contract`; commit com migration leva trailer `DB: <NomeDaMigration>`; terminar com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` e `Claude-Session: https://claude.ai/code/session_012zwyixmzvGUhaN5Ztdopmm`.
- Todo arquivo novo: `git add` logo após criar.
- Datas do estorno e do novo = `TransactionDate`/`TransactionTime` do original.
- Motivo obrigatório, máx. 500 caracteres.
- Mesmo `ItemCode` obrigatório; só tipo 8 `Confirmed`.
- Testes: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Classe>"`; suíte inteira deve ficar verde (baseline 5419 + novos). Se testes de relatório falharem por "Logo fixture missing", copie `SiagroB1.Reports/wwwroot` do repo principal (não é regressão).
- Servidor local subido por subagente: parar só pelo PID da porta, nunca `taskkill /IM`.

## Review Focus

1. Romaneio original **sem NF e com parte alocada** (troca parcial permitida): saldos exatos — `Net` do estorno e do novo = `S`, gross/descontos proporcionais e a soma fecha sem resíduo de 0,001 → teste `Partial_WithoutInvoice_SplitsWeightsProportionally` (Task 2).
2. Volume informado **menor** que `S`: o novo nasce com `S` e fica com `S − volume` alocável na grade → teste `VolumeBelowBalance_LeavesRemainderOnNewTransaction` (Task 2).
3. **Remover uma alocação** do original depois da troca devolve volume alocável sem perder o trocado → teste `DeleteAllocationAfterChange_FreesOnlyAllocatedVolume` (Task 1).
4. Erro **depois** das escritas (ex.: contrato sem saldo detectado na alocação) não deixa nada gravado → teste `ContractWithoutBalance_RollsBackEverything` (Task 2).
5. Trocar de novo **o romaneio novo** (correção da correção) funciona e encadeia o rastro → teste `NewTransaction_CanBeChangedAgain` (Task 2).

---

### Task 1: Modelo, migration e invariante do saldo alocável

**Files:**
- Create: `SiagroB1.Domain/Entities/PurchaseTransactionChange.cs`
- Modify: `SiagroB1.Domain/Entities/StorageTransaction.cs` (campo novo + `RecalculateAvailableVolume`, linha ~327)
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs` (DbSet perto da linha 140; config perto da 694, padrão de `ShippingReleaseChange`)
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsAllocationCreateService.cs` (linhas ~122 e ~328)
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsAllocationDeleteService.cs` (linha ~96)
- Create: migration `AddPurchaseTransactionChanges` em `SiagroB1.Migrations/AppContext`
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseTransactionChangeAvailableVolumeTests.cs`

**Interfaces:**
- Produces:
  - `public class PurchaseTransactionChange : BaseEntity` com `Key` (Guid), `OriginalStorageTransactionKey`, `ReturnStorageTransactionKey`, `NewStorageTransactionKey` (Guid, FK `NoAction` para `StorageTransaction`, navegações `OriginalStorageTransaction`/`ReturnStorageTransaction`/`NewStorageTransaction`), `PurchaseContractKey` (Guid, FK `NoAction`), `OriginalCardCode`, `OriginalWarehouseCode`, `NewCardCode`, `NewWarehouseCode` (string, required), `Quantity` (decimal 18,3), `AllocatedVolume` (decimal 18,3), `Reason` (required, `[MaxLength(500)]`). Tabela `PURCHASE_TRANSACTION_CHANGES`, índice em `OriginalStorageTransactionKey`.
  - `StorageTransaction.PurchaseTransactionChangeKey` (`Guid?`, FK `NoAction`) — preenchido no estorno e no novo.
  - `DbSet<PurchaseTransactionChange> PurchaseTransactionChanges`.
  - `StorageTransaction.RecalculateAvailableVolume(decimal totalAllocated, decimal changedQuantity = 0m)`: `0` se não alocável **ou** se `TransactionType == PurchaseReturn && PurchaseTransactionChangeKey != null`; senão `NetWeight − totalAllocated − changedQuantity`.
  - `public static Task<decimal> PurchaseTransactionChangeQuery.ChangedQuantityAsync(AppDbContext ctx, Guid storageTransactionKey)` em `SiagroB1.Infra/Queries/PurchaseTransactionChangeQuery.cs` = Σ `Quantity` das trocas com `OriginalStorageTransactionKey == key`.

- [ ] **Step 1: Testes que falham** — `PurchaseTransactionChangeAvailableVolumeTests`:
  - `Recalculate_SubtractsChangedQuantity`: 8 com Net 30000, `RecalculateAvailableVolume(10000m, 20000m)` → `0m`; `(5000m, 20000m)` → `5000m`.
  - `Recalculate_ChangeReturnIsNeverAllocatable`: 9 com `PurchaseTransactionChangeKey = Guid.NewGuid()`, Net 20000 → `0m`; 9 sem a marca → `20000m − alocado` (comportamento atual).
  - `DeleteAllocationAfterChange_FreesOnlyAllocatedVolume`: semear 8 Net 30000 com alocação de 10000 no contrato A e uma `PurchaseTransactionChange` com `Quantity = 20000` apontando como original; remover a alocação via `PurchaseContractsAllocationDeleteService` → `AvaiableVolumeToAllocate == 10000m`.
- [ ] **Step 2:** rodar `--filter "FullyQualifiedName~PurchaseTransactionChangeAvailableVolumeTests"` → falha de compilação.
- [ ] **Step 3:** implementar entidade, campo, config, query e o novo parâmetro; nos três chamadores passar `await PurchaseTransactionChangeQuery.ChangedQuantityAsync(ctx, st.Key)` (no create, quando `st.Key == Guid.Empty`, usar `0m` sem consulta — ver memória do fluxo deferred).
- [ ] **Step 4:** testes passam; suíte inteira verde.
- [ ] **Step 5:** gerar migration: `dotnet ef migrations add AddPurchaseTransactionChanges --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web` (com `ASPNETCORE_ENVIRONMENT=Ceagui-Development` explícito). Conferir que o `Up` só cria a tabela, a coluna e os índices/FKs — nada além (snapshot pode ter drift; se houver, ver memória `migrations-hand-edited-baseline-sync`). **Não** aplicar em banco nenhum nesta tarefa.
- [ ] **Step 6:** commit `feat(purchase-contract): registro da troca de fornecedor/armazém do romaneio` com trailer `DB: AddPurchaseTransactionChanges`.

### Task 2: Serviço da troca

**Files:**
- Create: `SiagroB1.Application/Services/PurchaseContracts/PurchaseTransactionsChangeSupplierService.cs`
- Modify: `SiagroB1.Application/Services/ShippingTransactions/ShippingReleaseChangeReconciliationGuard.cs` (parâmetro de mensagem)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (registro, perto da linha 222)
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseTransactionsChangeSupplierServiceTests.cs` (montar dependências como `ShippingTransactionsChangeReleaseServiceTests`, linhas 26–71: fakes de parceiro F0001/F0002, item SOJA, armazéns 01/02)

**Interfaces:**
- Consumes: Task 1 (entidade, marca, `RecalculateAvailableVolume`, `ChangedQuantityAsync`).
- Produces:
  - `public sealed record PurchaseTransactionChangeResult(string ReturnCode, string NewCode);`
  - `Task<PurchaseTransactionChangeResult> PurchaseTransactionsChangeSupplierService.ExecuteAsync(Guid storageTransactionKey, Guid purchaseContractKey, decimal volume, string? reason, string userName)` — abre e fecha a própria transação (rollback total em erro).
  - `ShippingReleaseChangeReconciliationGuard.EnsureNoApprovedReconciliationAsync(string itemCode, IEnumerable<string> warehouseCodes, DateTime referenceDate, string dateLabel = "data da carga", string action = "trocar a liberação")` — mensagens atuais intactas no default; a troca passa `"data do romaneio"` e `"trocar o fornecedor/armazém"`.
- Dependências do construtor: `IUnitOfWork`, `StorageTransactionsCreateService`, `StorageTransactionsConfirmedService`, `PurchaseContractsAllocationCreateService`, `ShippingReleaseChangeReconciliationGuard`.

Comportamento (spec §"Os três lançamentos" e §"Travas"); mensagens fixas:
- motivo vazio → `"Informe o motivo da troca de fornecedor/armazém."`
- não-8 / não confirmado / `S <= 0` → `"Só romaneio de compra confirmado com saldo a alocar pode ter o fornecedor/armazém trocado."`
- produto diferente → `"O contrato é de outro produto."`
- mesmo fornecedor **e** mesmo armazém → `"Fornecedor e armazém são os mesmos do contrato: use a alocação normal."`
- contrato não Aprovado → `"Contrato encerrado ou não aprovado: não é possível alocar."`
- parcial com NF/conciliação → `"Romaneio com nota fiscal e parte alocada: remova as alocações antes de trocar o fornecedor/armazém."`
- royalties ITS, remessa para depósito: reaproveitar `RoyaltyRules.EnsureNoActiveWriteOffAsync` e `StorageDepositRules.EnsureTransactionsNotReconciledAsync`.
- perna de Expedição de Grãos (`ShippingTransactions` com `PurchaseStorageTransactionKey`) ou de Entrada em armazenagem própria (`StorageEntryTransactions.PurchaseStorageTransactionKey`) → `"O romaneio {Code} faz parte de outro fluxo (expedição ou entrada em armazenagem) e não pode ter o fornecedor/armazém trocado por aqui."`
- volume `<= 0`, `> S`, `> saldo do contrato`: mensagens atuais do create de alocação, em pt-BR.

Ordem de escrita (copiar o padrão de `ReversePurchaseLegAsync`/`CreatePurchaseLegAsync`, sem carga): validar tudo → `BeginTransaction` → estorno 9 (create Pending + confirm Deferred, depois sobrescrever Gross/descontos/Net com valores × `r`, `ZeroServicePrices`, `PurchaseTransactionChangeKey`) → novo 8 (cópia do original com `CardCode/CardName` do contrato, `WarehouseCode/Name` = `DeliveryLocationCode/Name`, mesmos pesos do estorno, inspeções/royalty/`ProcessingCostCode` copiados, preços de serviço zerados, NF movida na troca total) → `SaveChanges` → alocar `volume` no contrato → gravar `PurchaseTransactionChange` → reapontar `PurchaseFiscalReconciliations` do original para o novo (troca total) e limpar NF do original → recalcular `AvaiableVolumeToAllocate` do original → `SaveChanges` → `Commit`. Rateio: gross/descontos arredondados a 3 casas; `Net = S` exato (resíduo absorvido no `OthersDicount` — decisão do construtor documentada em comentário).

- [ ] **Step 1: Testes que falham** (todos com romaneio original F0001/armazém 01, Net 30000, data 2026-09-05 10:30; contrato destino F0002/armazém 02 Aprovado, saldo 100000):
  - `FullChange_CreatesReturnAndNewTransaction`: sem alocação; volume 30000 → existe 9 F0001/01 Net 30000 e 8 F0002/02 Net 30000 confirmado; datas = 2026-09-05; contrato destino `AllocatedVolume == 30000`; original `AvaiableVolumeToAllocate == 0`; 9 `== 0`; novo `== 0`; uma linha em `PurchaseTransactionChanges` com `Quantity 30000`, `AllocatedVolume 30000`, motivo.
  - `FullChange_MovesInvoiceAndReconciliationLinks`: original com `InvoiceNumber "123"`, `ChaveNFe` e um `PurchaseFiscalReconciliation` → novo tem os dois campos, original não, vínculo aponta para o novo.
  - `OnlyWarehouseDiffers_IsAChange`: contrato F0001/02 → troca acontece.
  - `SameSupplierAndWarehouse_IsRefused`.
  - `OtherItem_IsRefused`.
  - `Partial_WithoutInvoice_SplitsWeightsProportionally`: original Gross 31000, Drying 600, Cleaning 400, Net 30000, 10000 alocados em contrato F0001/01 → estorno e novo com Net 20000, Gross 20666,667, Drying 400, Cleaning 266,667; original `AvaiableVolumeToAllocate == 0`; alocação de 10000 intocada.
  - `Partial_WithInvoice_IsRefused` (e variante com só vínculo de conciliação).
  - `VolumeBelowBalance_LeavesRemainderOnNewTransaction`: volume 12000 → novo `AvaiableVolumeToAllocate == 18000`.
  - `ApprovedReconciliationAfterDate_IsRefused`: `WarehouseReconciliation` Approved SOJA armazém 02 `ReferenceDate` 2026-09-30 → mensagem cita "data do romaneio".
  - `ContractWithoutBalance_RollsBackEverything`: contrato com saldo 5000, volume 30000 → exceção; contagem de `StorageTransactions`, alocações e trocas igual à de antes.
  - `NewTransaction_CanBeChangedAgain`: trocar o novo para contrato F0001/01 → segunda troca com `OriginalStorageTransactionKey` = romaneio novo da primeira.
  - `EmptyReason_IsRefused`.
- [ ] **Step 2:** rodar o filtro da classe → falha de compilação.
- [ ] **Step 3:** implementar serviço, generalização do guard e registro no DI.
- [ ] **Step 4:** testes passam; `ShippingTransactionsChangeReleaseServiceTests` continuam verdes; suíte inteira verde.
- [ ] **Step 5:** commit `feat(purchase-contract): trocar fornecedor/armazém do romaneio por estorno e romaneio novo`.

### Task 3: Proteções (cancelar, voltar a Pendente, alocação comum)

**Files:**
- Modify: `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsCancelService.cs` (~linha 70)
- Modify: `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsReverseService.cs` (~linha 75)
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsAllocationCreateService.cs` (`ExecuteWithTransactionAsync`, linha ~33)
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseTransactionChangeGuardsTests.cs`

**Interfaces:**
- Consumes: Task 1 (`PurchaseTransactionChangeKey`, `PurchaseTransactionChanges`).
- Produces: nada novo para outras tarefas.

Regras:
- Cancelar/estornar recusa quando `PurchaseTransactionChangeKey != null` **ou** existe troca com `OriginalStorageTransactionKey == doc.Key`: `"O romaneio {Code} faz parte de uma troca de fornecedor/armazém e não pode ser cancelado por aqui."` (estornar: `"... não pode voltar a pendente por aqui."`).
- A trava de divergência fica **só** em `ExecuteWithTransactionAsync` (a entrada da tela/API). `ExecuteAsync`/`ExecuteReversalAsync` (fluxos internos: Expedição de Grãos, GAC-1177, transferência de titularidade, entrada em armazenagem) **não** mudam. Mensagem: `"O contrato tem outro fornecedor ou armazém: use a troca de fornecedor/armazém."` Comparação de `CardCode` e de `WarehouseCode` × `DeliveryLocationCode` sem diferenciar maiúsculas.

- [ ] **Step 1: Testes que falham:** `Cancel_RefusesChangeReturn`, `Cancel_RefusesReplacedOriginal`, `Reverse_RefusesNewTransaction`, `PublicAllocation_RefusesOtherSupplier`, `PublicAllocation_RefusesOtherWarehouse`, `PublicAllocation_SameSupplierAndWarehouse_StillWorks`.
- [ ] **Step 2:** rodar → falham.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** testes passam; suíte inteira verde (atenção a testes existentes que usam `ExecuteWithTransactionAsync` com contrato de outro fornecedor/armazém: se aparecerem, corrigir o **seed** para bater, não a regra).
- [ ] **Step 5:** commit `feat(purchase-contract): proteger romaneios da troca de fornecedor/armazém`.

### Task 4: Conciliação Fiscal de Compras

**Files:**
- Modify: `SiagroB1.Infra/Queries/PurchaseFiscalReconciliationQuery.cs` (`TransactionsBase` ~linha 23; quantidade física ~linhas 151–170)
- Modify: `SiagroB1.Application/Services/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationPlanner.cs` / quem monta os `PlannerTransaction` (usar o mesmo `OpenBalance` corrigido)
- Modify: `SiagroB1.Application/Services/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationService.cs` (~linha 265, recusar vincular estorno de troca)
- Test: `SiagroB1.Application.Tests/PurchaseFiscalReconciliations/PurchaseTransactionChangeReconciliationTests.cs` (seed via `FiscalReconciliationSeed`)

**Interfaces:**
- Consumes: Task 1.
- Regras: estorno de troca (`PurchaseReturn` com `PurchaseTransactionChangeKey != null`) fora de `TransactionsBase` e recusado no vínculo manual (`"Estorno de troca de fornecedor/armazém não recebe nota fiscal."`); `PhysicalQuantity` do original = `NetWeight − Σ Quantity das trocas` em que ele é original (zero → aparece como sem pendência física; o construtor decide se o some da lista quando 0 e sem vínculo — preferir sumir).

- [ ] **Step 1: Testes que falham:** `ChangeReturn_NotListed`, `ChangeReturn_CannotBeLinked`, `FullyChangedOriginal_NotListed`, `PartiallyChangedOriginal_PhysicalIsRemaining` (Net 30000, troca 20000 → 10000), `Planner_DoesNotSuggestChangeReturn`.
- [ ] **Step 2:** rodar → falham.
- [ ] **Step 3:** implementar sem quebrar o tempo da aba Entregas (nada de `STRING_AGG`; subconsulta agregada por chave).
- [ ] **Step 4:** testes passam; suíte inteira verde.
- [ ] **Step 5:** commit `feat(purchase-contract): conciliação fiscal ignora o estorno da troca de fornecedor`.

### Task 5: Ação OData

**Files:**
- Create: `SiagroB1.Web/Actions/PurchaseContracts/PurchaseTransactionsChangeSupplierController.cs` (padrão de `ShippingTransactionsChangeReleaseController`)
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (perto da linha 796)

**Interfaces:**
- Consumes: Task 2 (`ExecuteAsync`, `PurchaseTransactionChangeResult`).
- Produces: `POST odata/PurchaseTransactionsChangeSupplier` com `StorageTransactionKey` (Guid), `PurchaseContractKey` (Guid), `Volume` (**double** — decimal editável chega como string e é recusado; converter para decimal no controller), `Reason` (string). Resposta 200 `{ "ReturnCode": "...", "NewCode": "..." }` com `[JsonPropertyName]` (a API serializa camelCase sem isso). `parameters == null` → 400 `"Parâmetros obrigatórios não informados."`; `DbUpdateConcurrencyException` (direta ou interna) → 400 `"O romaneio foi alterado por outro usuário. Recarregue a tela e tente novamente."`; demais → 400 com a mensagem.

- [ ] **Step 1:** implementar controller e registro no EDM.
- [ ] **Step 2:** `dotnet build SiagroB1.sln` sem erros novos.
- [ ] **Step 3:** commit `feat(purchase-contract): ação para trocar fornecedor/armazém do romaneio`.

### Task 6: Frontend

**Files (worktree frontend):**
- Modify: `webapp/model/ServerRoutes.ts` (perto da linha 163): `purchaseTransactionsChangeSupplier: '/PurchaseTransactionsChangeSupplier(...)'`
- Modify: `webapp/controller/purchaseOrders/allocation/Main.controller.ts` (`onConfirmDialog` e `confirmAllocation`)

**Interfaces:**
- Consumes: Task 5.
- Comportamento:
  - Sem divergência: igual hoje (`purchaseContractsAllocationsCreate`).
  - Com divergência: o aviso passa a dizer
    `"O contrato selecionado é diferente do romaneio:\n\n• …\n\nSerá gerado um estorno no fornecedor/armazém do romaneio e um romaneio novo no fornecedor/armazém do contrato, com a data do romaneio original. Continuar?"`;
    OK → `DialogHelper.promptDialog("Troca de fornecedor/armazém", "Motivo", "")` (vazio = desistiu, nada acontece) → chama a ação com `Volume` numérico e `Reason` → toast `Romaneio ${Code} trocado: estorno ${ReturnCode}, romaneio novo ${NewCode} alocado no contrato ${contrato}.` → `refreshData()`.
  - Erro da ação: o handler global de OData já mostra a mensagem; garantir `setBusy(false)` no `finally`.

- [ ] **Step 1:** implementar.
- [ ] **Step 2:** `npm run ts-typecheck` e `npx eslint webapp/controller/purchaseOrders/allocation/Main.controller.ts` limpos (worktree sem `node_modules`: criar junction para o do repo principal e removê-la com `cmd /c rmdir` ao fim — **nunca** `Remove-Item -Recurse` na junction).
- [ ] **Step 3:** commit `feat(purchase-contract): trocar fornecedor/armazém pelo diálogo de alocação`.

### Task 7: Verificação pelo caminho do usuário

- [ ] **Step 1:** aplicar `AddPurchaseTransactionChanges` **só** no banco local `CEAGUI_SIAGRO_DEV` (perfil `ceagui`, `ASPNETCORE_ENVIRONMENT=Ceagui-Development` explícito; nunca o perfil `db-migration`, nunca banco remoto).
- [ ] **Step 2:** subir Gateway → esperar `Loading proxy data from config` → Web, ambos `--launch-profile ceagui` do worktree; frontend `yarn start:dev`.
- [ ] **Step 3:** criar no banco local um romaneio 8 confirmado com saldo (FTESTE01, produto 1, armazém FTESTEPJ) pela tela de romaneios, abrir `/purchase-orders/allocations` pelo menu, alocar no CC000001 (F00004, armazém 0000000001) com motivo. Conferir no banco, por **chave**: 9 em FTESTE01/FTESTEPJ, 8 novo em F00004/0000000001 com a data do original, alocação no CC000001, linha em `PURCHASE_TRANSACTION_CHANGES`, original fora da grade. Tentar cancelar o estorno pela tela de romaneios → recusa.
- [ ] **Step 4:** derrubar a stack pelo PID das portas 50000/5246/8080 e conferir de novo no fim.
- [ ] **Step 5:** registrar na memória do projeto o estado final (commits, migration aplicada só no CEAGUI local).
