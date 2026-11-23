# GAC-1232 — Transferência para armazém próprio — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir que a transferência (GAC-1176) termine num armazém PRÓPRIO, com a entrada registrada pelo peso, como já acontece em armazém de terceiro.

**Architecture:** Duas decisões no backend deixam de olhar só `WarehouseComplement.IsOwn`: a trava do início some, e o ramo de armazém próprio da entrada passa a valer só para `Type = Transshipment`. No frontend, o diálogo de entrada usa um helper puro com a mesma regra.

**Tech Stack:** .NET 10 / EF Core / xUnit (InMemory) · OpenUI5 1.141 + TypeScript / QUnit.

**Spec:** `docs/superpowers/specs/2026-11-23-gac-1232-own-warehouse-transfer-design.md`

## Global Constraints

- Mensagens ao usuário em pt-BR; identificadores em inglês.
- Sem migration e sem correção de dado.
- O transbordo (`Type = Transshipment`) em armazém próprio continua exatamente como hoje.
- Backend: branch `feature/gac-1232-transshipment-lot-exit-romaneio`, worktree
  `C:\Projetos\SiagroB1\worktrees\gac-1232\backend`. Frontend: mesmo nome de branch, worktree
  `C:\Projetos\SiagroB1\worktrees\gac-1232\frontend` (com `yarn install` próprio, nunca junction de
  `node_modules`). Commit por task, nunca push.

## Review Focus

- Transbordo comum em armazém próprio depois da mudança: a entrada continua exigindo o `Receipt` do
  lote de transbordo (coberto pelos testes existentes de `RegisterEntry_OwnWarehouse_*` e
  `ShipmentLoadsTransshipmentOwnWarehouseEntryTests`; não podem quebrar).
- Transferência em armazém próprio enviada SEM peso (só com `ReceiptStorageTransactionKey`): recusa
  pela validação de quantidade de terceiro (`EnsureThirdPartyQuantityIsValid`), não vincula o
  romaneio. Teste na Task 1.
- Conclusão da transferência em armazém próprio: passa, porque a entrada é o 15. Teste na Task 1.
- Estorno da transferência em armazém próprio antes de concluir: cancela o 15 e a liberação, sem
  procurar `Receipt`. Teste na Task 1.

---

### Task 1: Backend — transferência aceita armazém próprio

**Files:**
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsTransshipmentStartService.cs:54-65` (remover a trava de `Type = Transfer` + `IsOwn`; o parâmetro `IWarehouseComplementService` fica sem uso: removê-lo do construtor e dos `new` nos testes)
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsTransshipmentRegisterEntryService.cs:81-94,151` (`var usesOwnLot = complement?.IsOwn == true && transshipment.Type == TransshipmentType.Transshipment;` no lugar de `isOwn` nos dois `if`)
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsTransshipmentCompleteService.cs` (só textos: `summary`/`remarks` e a mensagem da linha 78 deixam de dizer "entre armazéns de terceiros"; a lógica não muda)
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadTransshipmentRules.cs` (`remarks` da regra temporal: o ramo próprio da criação depende de `IsOwn` **e** do tipo da linha)
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsTransferGuardsTests.cs` (substituir `A_transfer_to_an_own_warehouse_is_refused_at_the_start`)
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsOwnWarehouseTransferTests.cs` (novo; usa `ShipmentLoadsTransferTestData`)

**Interfaces:**
- Produces: `ShipmentLoadsTransshipmentStartService(IUnitOfWork, IWarehouseService, ShipmentLoadsMovementLogService)` se o complemento ficar sem uso; nenhuma action OData muda de assinatura.

- [ ] **Step 1: Escrever os testes que falham**

Em `ShipmentLoadsTransferGuardsTests`, trocar o teste de recusa por:

```csharp
[Fact]
public async Task A_transfer_to_an_own_warehouse_can_be_started()
// complemento IsOwn = true no DestinationWarehouse; Start com TransshipmentType.Transfer
// Assert: transshipment.Type == Transfer; carga InTransshipment e IsInTransfer
```

Em `ShipmentLoadsOwnWarehouseTransferTests` (carga e transferência semeadas pelos helpers de
`ShipmentLoadsTransferTestData`, origem com liberação de um contrato `PC-001`, complemento
`IsOwn = true` no destino):

```csharp
[Fact] public async Task RegisterEntry_TransferToOwnWarehouse_UsesTheWeighedQuantity()
// RegisterEntry(key, 29_800m, hoje, receiptKey: null)
// Assert: um StorageTransaction TransshipmentReceipt Confirmed, WarehouseCode = destino,
//         NetWeight 29_800, StorageAddressCode null; transshipment.EntryQuantity 29_800

[Fact] public async Task RegisterEntry_TransferToOwnWarehouse_EmitsTheReleaseAtTheDestinationWithoutLot()
// Assert: uma ShipmentRelease Origin = Transshipment, PurchaseContractKey = PC-001,
//         DeliveryLocationCode = destino, ReleasedQuantity 29_800, StorageAddressCode null

[Fact] public async Task RegisterEntry_TransferToOwnWarehouse_WithoutWeight_IsRefused()
// RegisterEntry(key, 0m, hoje, receiptKey: <Receipt qualquer>) → ApplicationException;
// Assert: nenhum TransshipmentReceipt criado, o Receipt continua sem ShipmentLoadTransshipmentKey

[Fact] public async Task Complete_TransferToOwnWarehouse_ClosesTheLoadWithoutBilling()
// RegisterEntry + CompleteService → transshipment.FinalDestinationAt != null; carga Completed

[Fact] public async Task Reverse_TransferToOwnWarehouse_CancelsTheCreditAndTheRelease()
// RegisterEntry + ReverseService(key, null, ...) antes de concluir
// Assert: o 15 fica Cancelled e a liberação Origin = Transshipment fica cancelada
//         (mesmos campos que os testes de estorno de terceiro já conferem)
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ShipmentLoadsOwnWarehouseTransferTests|FullyQualifiedName~ShipmentLoadsTransferGuardsTests"`
Expected: FAIL. O início recusa com "armazém próprio" e a entrada pede "Informe o romaneio de Entrada em Armazenagem".

- [ ] **Step 3: Implementar** as duas mudanças de lógica e os textos listados em Files.

- [ ] **Step 4: Rodar os testes da task e a suíte inteira**

Run: `dotnet test SiagroB1.Application.Tests`
Expected: tudo verde (antes copiar `SiagroB1.Reports/wwwroot` do checkout principal se os testes de relatório reclamarem de `logo.png`).

- [ ] **Step 5: Commit** com `feat(shipment): permite transferência para armazém próprio` e `Refs: GAC-1232`.

### Task 2: Frontend — diálogo de entrada pede peso na transferência

**Files:**
- Modify: `webapp/helpers/TransshipmentHelpers.ts` (nova função exportada)
- Modify: `webapp/controller/shipmentLoads/BaseController.ts:918-948` (`onRegisterTransshipmentEntry`: carregar recibos e marcar o form só quando o helper disser que precisa)
- Test: `webapp/test/unit/helpers/TransshipmentHelpers.qunit.ts`

**Interfaces:**
- Produces: `export function entryNeedsReceipt(isOwnWarehouse: boolean, type: string): boolean` — `true` só para armazém próprio com tipo diferente de `"Transfer"` (tipo ausente = transbordo).

- [ ] **Step 1: Escrever o teste QUnit que falha**

```ts
QUnit.test("entryNeedsReceipt: só o transbordo em armazém próprio pede o romaneio", assert => {
	assert.strictEqual(entryNeedsReceipt(true, "Transshipment"), true);
	assert.strictEqual(entryNeedsReceipt(true, undefined), true, "linha anterior ao GAC-1176");
	assert.strictEqual(entryNeedsReceipt(true, "Transfer"), false, "GAC-1232");
	assert.strictEqual(entryNeedsReceipt(false, "Transshipment"), false);
	assert.strictEqual(entryNeedsReceipt(false, "Transfer"), false);
});
```

- [ ] **Step 2: Rodar e ver falhar** — `yarn ts-typecheck` acusa `entryNeedsReceipt` inexistente.

- [ ] **Step 3: Implementar** o helper e usar no controller: `const isOwn = entryNeedsReceipt(await this.isOwnWarehouseAsync(warehouseCode), type);`. O resto do fluxo (`form.isOwn` decide entre peso e romaneio) não muda.

- [ ] **Step 4: Verificar** — `yarn ts-typecheck`, `yarn lint` e `yarn test` verdes (as falhas antigas conhecidas do `yarn test` devem ser só as mesmas de antes da mudança).

- [ ] **Step 5: Commit** com o mesmo assunto da Task 1 e `Refs: GAC-1232`.

### Task 3: Verificação pelo caminho do usuário

- [ ] No `yktb` (Web + Gateway do worktree do backend, frontend do worktree com `yarn start:dev`), numa carga de teste: Iniciar Transferência num armazém próprio → Registrar Entrada pelo peso → conferir a liberação na Expedição de Grãos do armazém de destino → Concluir Transferência → carga "Concluída".
- [ ] Derrubar Web, Gateway e o dev server pelo PID ao final.
