# GAC-1177 v2 — Troca de liberação por romaneios de compensação — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Trocar a liberação (contrato/fornecedor/armazém) de Expedições de uma carga — inclusive faturada — gerando um estorno explícito na origem e uma Expedição nova no destino, datados na data da carga, sem estornar notas.

**Architecture:** Um documento novo (`SHIPPING_RELEASE_CHANGES`) amarra, por troca, a Expedição original (substituída e retirada da chave da carga), o estorno na origem (12 + 9 ou 12 com lote) e a Expedição nova (8 + 7 ou 7 com lote) que vira a vigente da carga. O serviço `ShippingTransactionsChangeReleaseService` (v1, que regravava as pernas) é reescrito sobre o mesmo contrato público; travas de todos os fluxos que leem romaneios da carga ou de estoque passam a respeitar os romaneios da troca.

**Tech Stack:** .NET 10, EF Core (SQL Server; testes xUnit + EF InMemory), ASP.NET Core OData v4, Dapper (saldo de lote); OpenUI5 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-09-16-gac-1177-shipping-release-change-design.md` (v2 — autoridade). O plano v1 (`2026-09-16-gac-1177-shipping-release-change.md`) está SUPERSEDIDO; seu código (Tasks 1-5 v1) já está na árvore e é o ponto de partida.

## Global Constraints

- Identificadores em inglês; texto que o usuário lê em pt-BR.
- **Nunca commitar nem dar push.** Todo arquivo NOVO recebe `git add <path>` imediatamente, no repo certo.
- `ShipmentLoadMovementType.ReleaseChanged = 14` já existe; enums novos só com valor no fim.
- Todos os lançamentos da troca têm `TransactionDate = ShipmentLoad.LoadDate.Date` (e `TransactionTime` da Expedição original).
- Quantidade da Expedição nova: `InvoicedQuantity` recalculado da carga quando a carga tem **uma única Expedição vigente e a chamada tem 1 item**; se ≤ 0 recusa com "Carga sem faturamento: desvincule e estorne a Expedição."; nos demais casos (mais de uma vigente, inversão) = bruto da original.
- Conferência de saldo `Approved`, mesmo `ItemCode`, `WarehouseCode` ∈ {armazém da original, armazém da liberação de destino}, `ReferenceDate >= LoadDate.Date` → recusa citando o código da conferência e pedindo o cancelamento dela.
- Contrato `Finished` bloqueia, exceto quando `!ReleaseOriginRules.ConsumesPurchaseContract(release.Origin)`.
- Destino: mesmo produto, liberação `Actived`, com saldo conferido após aplicar todos os itens; armazém livre (a Expedição nova usa `DeliveryLocationCode`/`DeliveryLocationName` da liberação).
- FKs novas com `DeleteBehavior.NoAction` exatamente (Restrict gera snapshot diferente). `WithMany()` sem coleção inversa.
- Migrations: aplicar só em localhost com `ASPNETCORE_ENVIRONMENT=Yokotobi-Development` explícito; **nunca** o profile `db-migration`.
- Nenhum 12 existente tem `StorageAddressCode` (verificado em IDX_SIAGRO_DEV 16/09): creditar 12 com lote no saldo de lote não altera dado antigo.

Comandos (backend, a partir de `C:\Projetos\SiagroB1\siagro-b1-backend`):
- `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Classe>"`; suíte: `dotnet test SiagroB1.Application.Tests`
- `dotnet build SiagroB1.sln`
Frontend (`C:\Projetos\SiagroB1\siagro-b1-frontend`): `yarn ts-typecheck`, `yarn lint`.

## Vocabulário

- **Original**: par (8?)+7 da Expedição trocada. Depois da troca: `ShipmentLoadKey = null`, `ReplacedByShippingReleaseChangeKey = <troca>`, status inalterado.
- **Estorno**: 12 (e 9, quando a origem é Standard) com `ShippingReleaseChangeKey = <troca>`, sem `ShipmentLoadKey`/`RefusedFromShipmentLoadKey`/`GeneratedByReturnInvoiceKey`/`ReturnInvoiceKey`/`SalesInvoiceKey`.
- **Nova**: par (8?)+7 com `ShippingReleaseChangeKey = <troca>`; o 7 com `ShipmentLoadKey` da carga e status da 7 original.
- **Vigente**: 7 com `ShipmentLoadKey == carga`, status ∉ {Cancelled, Returned}.

---

## File map

| Arquivo | Responsabilidade |
|---|---|
| Create `SiagroB1.Domain/Entities/ShippingReleaseChange.cs` | Documento da troca |
| Modify `SiagroB1.Domain/Entities/StorageTransaction.cs` | `ReplacedByShippingReleaseChangeKey`, `ShippingReleaseChangeKey` |
| Modify `SiagroB1.Infra/Context/AppDbContext.cs` | DbSet + FKs NoAction |
| Create `SiagroB1.Migrations/AppContext/<ts>_AddShippingReleaseChanges.cs` (+Designer, snapshot) | Migration |
| Modify lot-balance readers (Task 2 list) | 12 com lote credita |
| Modify `PurchaseContracts/PurchaseContractsAllocationCreateService.cs` | `ExecuteReversalAsync` |
| Modify guards (Task 2 list) | Recusar mexer em romaneio da troca |
| Rewrite `ShippingTransactions/ShippingTransactionsChangeReleaseService.cs` | Serviço v2 |
| Create `ShippingTransactions/ShippingReleaseChangeReconciliationGuard.cs` | Trava da conferência |
| Modify `ShipmentLoads/ShipmentLoadsGetService.cs` (`QueryTransactions`), `ShipmentReleases/ShipmentReleasesPurchaseContractsService.cs`, `Web/Actions/ShippingTransactions/ShippingTransactionsChangeReleaseController.cs` | Grid, lista de destinos, controller |
| Front: `view/shipmentLoads/Detail.view.xml`, `controller/shipmentLoads/Detail.controller.ts`, `view/shipmentLoads/fragments/ChangeRelease.fragment.xml`, `model/formatter.ts`, `controller/shipmentLoads/Attach.controller.ts` | UI |

---

### Task 1: Modelo e migration do documento da troca

**Files:**
- Create: `SiagroB1.Domain/Entities/ShippingReleaseChange.cs`
- Modify: `SiagroB1.Domain/Entities/StorageTransaction.cs`, `SiagroB1.Infra/Context/AppDbContext.cs`
- Create: migration `AddShippingReleaseChanges` em `SiagroB1.Migrations/AppContext/`
- Test: `SiagroB1.Application.Tests/ShippingTransactions/ShippingReleaseChangeModelTests.cs`

**Interfaces — Produces:**
```csharp
[Table("SHIPPING_RELEASE_CHANGES")]
[Index(nameof(ShipmentLoadKey))]
[Index(nameof(OperationGroupKey))]
public class ShippingReleaseChange : BaseEntity
{
    public Guid ShipmentLoadKey { get; set; }
    public Guid OperationGroupKey { get; set; }          // itens da mesma chamada (inversão)
    public Guid OriginalSalesStorageTransactionKey { get; set; }
    public Guid? OriginalPurchaseStorageTransactionKey { get; set; }
    public Guid ReturnSalesStorageTransactionKey { get; set; }     // 12
    public Guid? ReturnPurchaseStorageTransactionKey { get; set; } // 9
    public Guid NewSalesStorageTransactionKey { get; set; }        // 7
    public Guid? NewPurchaseStorageTransactionKey { get; set; }    // 8
    public Guid SourceShipmentReleaseKey { get; set; }
    public Guid TargetShipmentReleaseKey { get; set; }
    [Column(TypeName = "DECIMAL(18,3)")] public decimal OriginalQuantity { get; set; }
    [Column(TypeName = "DECIMAL(18,3)")] public decimal NewQuantity { get; set; }
    [Column(TypeName = "VARCHAR(500)")] public required string Reason { get; set; }
}
```
`StorageTransaction`: `public Guid? ReplacedByShippingReleaseChangeKey { get; set; }` e `public Guid? ShippingReleaseChangeKey { get; set; }`, com XML-doc pt-BR explicando (siga o estilo de `RefusedFromShipmentLoadKey`, StorageTransaction.cs:95-113). Sem navigation properties nas duas colunas novas (evita ambiguidade e EDM).

`AppDbContext`: `DbSet<ShippingReleaseChange> ShippingReleaseChanges`; para cada Guid FK da entidade nova e para as 2 colunas novas de `StorageTransaction`, `HasOne<Principal>().WithMany().HasForeignKey(...).OnDelete(DeleteBehavior.NoAction)` (Principal: ShipmentLoad, StorageTransaction, ShipmentRelease, ShippingReleaseChange). Siga o padrão das linhas 217-276.

- [ ] **Step 1: Teste de modelo (RED)** — usando o `AppDbContext` de `TestDb`, afirme via `context.Model.FindEntityType(typeof(ShippingReleaseChange))` o nome da tabela, que todas as FKs existem com `DeleteBehavior.NoAction`, e que `StorageTransaction` tem as 2 propriedades com FK para `SHIPPING_RELEASE_CHANGES`. Referência de estilo: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadModelTests.cs`. Rode — falha de compilação.
- [ ] **Step 2: Entidade, colunas e configuração** (acima). Rode o teste — PASS.
- [ ] **Step 3: Migration** — gere com `dotnet ef migrations add AddShippingReleaseChanges --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext` com `ASPNETCORE_ENVIRONMENT=Yokotobi-Development` no ambiente (confira como outras migrations AppContext foram geradas; se o comando exigir outro formato, siga o que o repo usa). Adicione `<remarks>` pt-BR explicando o porquê (padrão `20260908182214_AddShipmentReleaseReturnOrigin.cs`). Confira que o `Up` só cria a tabela, índices, 2 colunas + FKs, e que nenhuma outra tabela aparece no diff do snapshot.
- [ ] **Step 4: Aplicar em localhost** — `ASPNETCORE_ENVIRONMENT=Yokotobi-Development dotnet ef database update ...` (antes: `dotnet ef migrations list` com o mesmo env e confirme que só a nova está pendente e que o alvo é `localhost/IDX_SIAGRO_DEV` lendo `appsettings.Yokotobi-Development.json`). Se o alvo não for localhost, PARE e reporte BLOCKED.
- [ ] **Step 5:** `dotnet build SiagroB1.sln` + suíte inteira. `git add` dos arquivos novos (entidade, teste, migration, designer).

---

### Task 2: Suporte aos romaneios da troca nos fluxos existentes

**Files (modify) e mudança de cada um:**

1. **Saldo de lote credita 12 com lote** — em cada lista de tipos que credita lote (hoje `0, 6`) acrescente `12`:
   - `StorageAddresses/StorageAddressesGetBalanceService.cs` (SQL Dapper)
   - `StorageAddresses/StorageAddressesGetService.cs:69-81`
   - `SiagroB1.Domain/Entities/StorageAddress.cs:58-76` (`TotalReceipt`)
   - `StorageAddresses/StorageAddressesListOpenedByItemService.cs:29-40`
   - `SiagroB1.Reports/Services/StorageAddressReportService.cs:112-157`
   Comentário curto pt-BR em cada um: "12 com lote = estorno de troca de liberação (GAC-1177); nenhum 12 anterior tem lote". Não mexa em `StorageAddressesDailyBalanceBuilderService` (drift pré-existente; registre no relatório).
2. **Alocação de estorno** — `PurchaseContractsAllocationCreateService.ExecuteReversalAsync(Guid purchaseContractKey, StorageTransaction purchaseReturn, decimal volume, string userName)`: exige `TransactionType == PurchaseReturn`, status ≠ Pending, `volume > 0`, `volume <= AvaiableVolumeToAllocate`; **não** checa contrato Finished nem `AvaiableVolume` (estorno devolve saldo); grava `Volume = -volume` e recalcula romaneio e contrato exatamente como o overload por entidade (`:142-237`, extraia o trecho comum para um método privado em vez de duplicar). XML-doc explicando o porquê.
3. **Guardas contra desfazer romaneio da troca por outro caminho** — recusar com mensagem pt-BR quando `ShippingReleaseChangeKey != null` **ou** `ReplacedByShippingReleaseChangeKey != null`:
   - `StorageTransactions/StorageTransactionsCancelService.cs` (junto das guardas de `RefusedFromShipmentLoadKey`, :35-65)
   - `StorageTransactions/StorageTransactionsReverseService.cs` (mesmo ponto)
   - `ShippingTransactions/ShippingTransactionsReverseService.cs` (antes do guard de carga; mensagem: "O romaneio {Code} faz parte de uma troca de liberação e não pode ser estornado por aqui.") — atenção: a Expedição **nova** (vigente, na carga) continua estornável pelo caminho normal depois de desvinculada; recuse só `ReplacedBy…` e romaneios de estorno (12/9). Para a nova, o `ShippingReleaseChangeKey` está no 7 e no 8: diferencie pelo fato de a troca apontar para ela como `NewSalesStorageTransactionKey` — o mais simples é **recusar apenas `ReplacedByShippingReleaseChangeKey != null`** neste serviço e permitir o resto (12/9 não têm `SHIPPING_TRANSACTIONS`).
   - `ShipmentLoads/ShipmentLoadsAttachTransactionsService.cs:132-154` — recusar `ReplacedBy… != null`.
   - `SalesInvoices/SalesInvoicesReverseConfirmService.cs:483-494` (consulta de órfãos) — acrescentar `ReplacedByShippingReleaseChangeKey == null && ShippingReleaseChangeKey == null`.
   Decisão sobre `StorageTransactionsCancel/Reverse` para a Expedição nova: recusar tudo que tem `ShippingReleaseChangeKey` (esses serviços são o caminho de romaneio avulso; a nova sai por desvincular + `ShippingTransactionsReverseService`).

**Tests** (novos, em `SiagroB1.Application.Tests/ShippingTransactions/ShippingReleaseChangeSupportTests.cs` ou nos arquivos de teste existentes de cada serviço, seguindo o padrão local):
- `ExecuteReversalAsync`: aloca negativo em contrato Finished e sem saldo; recusa tipo ≠ 9; `AllocatedVolume` do contrato cai.
- Cancel/Reverse de StorageTransaction recusam romaneio com cada uma das 2 chaves.
- `ShippingTransactionsReverseService` recusa original substituída.
- Attach recusa original substituída.
- Saldo de lote: se houver teste existente do `StorageAddress.TotalReceipt`/`Balance`, acrescente o caso 12; o SQL Dapper não é testável em InMemory — registre.

- [ ] Steps: RED (testes) → implementação → GREEN focado → suíte inteira → build. `git add` do arquivo de teste novo.

---

### Task 3: Reescrita do `ShippingTransactionsChangeReleaseService` (v2)

**Files:**
- Rewrite: `SiagroB1.Application/Services/ShippingTransactions/ShippingTransactionsChangeReleaseService.cs`
- Create: `SiagroB1.Application/Services/ShippingTransactions/ShippingReleaseChangeReconciliationGuard.cs`
- Modify: `SiagroB1.Commons/Resources/Resource.pt-br.resx` e `Resource.resx` só se o serviço usar resource (preferir literal pt-BR como o v1 faz)
- Modify: DI `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (guard novo + dependências novas do serviço)
- Rewrite: `SiagroB1.Application.Tests/ShippingTransactions/ShippingTransactionsChangeReleaseServiceTests.cs`

**Interfaces:**
- Consumes: Task 1 (entidade/colunas), Task 2 (`ExecuteReversalAsync`, guards), `ShipmentReleaseLotRules.ResolveAsync`, `StorageTransactionsCreateService`, `StorageTransactionsConfirmedService`, `PurchaseContractsAllocationCreateService.ExecuteAsync(Guid, StorageTransaction, decimal, string, CommitMode)`, `ShipmentReleasesRecalculateShippedService.RecalculateAsync`, `ShipmentLoadsRecalculateTotalService.RecalculateAsync(db.Context, loadKey)`, `ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(db.Context, loadKey, excludedInvoiceKeys: null)` e seu `CalculateInvoicedAsync`, `ShipmentLoadsMovementLogService.Register`, `StorageTransactionCopyFactory.CreateFrom`.
- Produces (contrato público inalterado): `record ShippingReleaseChangeItem(Guid SalesStorageTransactionKey, Guid TargetShipmentReleaseKey)`; `Task ExecuteAsync(IReadOnlyList<ShippingReleaseChangeItem> items, string? reason, string userName)`.
- Produces: `ShippingReleaseChangeReconciliationGuard.EnsureNoApprovedReconciliationAsync(string itemCode, IEnumerable<string> warehouseCodes, DateTime loadDate)` — lança `ApplicationException("A conferência de saldo {Code} do armazém {WarehouseCode} ({ReferenceDate:dd/MM/yyyy}) é posterior à data da carga. Cancele a conferência antes de trocar a liberação.")`.

**Reaproveitar do v1:** validação de entrada, `BuildPlanAsync` (ajustes abaixo), `LoadReleaseAsync`, `ResolveLotsAsync` (ajustar), esqueleto transacional, recálculo multi-liberação, conferência final de saldo das liberações de destino, bloco de movimentação (ajustar), `CreatePurchaseLegAsync` (como base para 8 e 9).

**Regras (validação antes de qualquer escrita):**
1. Motivo; ≥ 1 item; sem duplicados.
2. `BuildPlanAsync`: mantenha as checagens do v1 **exceto**: remova a de armazém; Finished só quando `ConsumesPurchaseContract(origin)` (origem e destino). Acrescente: a 7 é **vigente** (`ReplacedBy… == null`, `ShipmentLoadKey != null`, tipo 7).
3. Carga única por chamada? Não exigir; mas cada item carrega sua carga.
4. Quantidade nova por item (Global Constraints): conte as vigentes da carga (`StorageTransactions` tipo 7, `ShipmentLoadKey == load`, status ∉ {Cancelled, Returned}); se `items.Count == 1 && vigentes == 1` → `CalculateInvoicedAsync` (recalcule, não leia a coluna); ≤ 0 → recusa; senão `GrossWeight` da original.
5. Conferência: `guard.EnsureNoApprovedReconciliationAsync(sales.ItemCode, [sales.WarehouseCode, target.DeliveryLocationCode], load.LoadDate)`.
6. Lote de destino: `ShipmentReleaseLotRules.ResolveAsync(ctx, reader, target, sales.ItemCode, target.DeliveryLocationCode, required)` com `required = Σ NewQuantity dos itens que vão para o lote − Σ GrossWeight dos itens cuja original está no lote` (o estorno devolve).

**Execução (uma transação; tudo Deferred salvo onde indicado):**
Para cada item, crie a linha `ShippingReleaseChange` (Key nova, `OperationGroupKey` comum, quantidades, motivo) e:

a. **Estorno 12**: `CreateFrom(originalSales)`; `TransactionType = SalesShipmentReturn`; `TransactionDate = load.LoadDate.Date`; `TransactionTime = originalSales.TransactionTime`; `GrossWeight = NetWeight = originalSales.GrossWeight`; descontos zerados; armazém/lote da original; limpe `ShipmentLoadKey`, `SalesInvoiceKey`, `RefusedFromShipmentLoadKey`, `GeneratedByReturnInvoiceKey`, `ReturnInvoiceKey`; `ShippingReleaseChangeKey = change.Key`; `ShipmentReleaseKey = null` **durante** create/confirm (desvia a trava de movimentação) e, depois do confirm, `ShipmentReleaseKey = source.Key` **somente se** `ShipsWithoutPurchaseLeg(source.Origin)` (na Standard o eixo é 8/9). `StorageAddressCode` = lote da original (null na Standard). create + confirm Deferred.
b. **Estorno 9** (só se a original tem perna 8): `CreateFrom(originalPurchase)` com `QualityInspections` carregadas; `TransactionType = PurchaseReturn`; data/hora como acima; `ShipmentReleaseKey = null` no create/confirm e `= source.Key` depois; `ShippingReleaseChangeKey`; sem lote/carga/nota. create + confirm Deferred (o confirm cai no ramo de compra e recalcula `NetWeight` com a mesma tabela/inspeções — **afirme em teste que é igual ao da original**; se divergir, force `NetWeight`/descontos iguais aos da original e documente). `SaveChanges`; `ExecuteReversalAsync(sourceContract.Key, purchaseReturn, purchaseReturn.NetWeight, user)`.
c. **Original**: `originalSales.ShipmentLoadKey = null`; `ReplacedByShippingReleaseChangeKey = change.Key` (também na 8 original, se houver); `UpdatedAt/By`.
d. **Nova 7**: `CreateFrom(originalSales)`; tipo 7, status Pending (confirm exige); `GrossWeight = NetWeight = NewQuantity`; descontos zerados; `WarehouseCode/Name = target.DeliveryLocationCode/Name`; `StorageAddressCode = targetLot?.Code`; `CardCode/CardName = targetContract`; `ShipmentReleaseKey = target.Key`; data/hora; `ShippingReleaseChangeKey`. create + confirm Deferred com `isShipmentTransaction: true`. Depois: `TransactionStatus = originalSales.TransactionStatus` (Invoiced/Confirmed), `SalesInvoiceKey = originalSales.SalesInvoiceKey`, `ShipmentLoadKey = load.Key`.
e. **Nova 8** (só se `!ShipsWithoutPurchaseLeg(target.Origin)`): `CreateFrom(novaSales)` com inspeções da **8 original** se houver (copie `QualityInspections` e `ProcessingCostCode` da 8 original; na ausência, as da 7), tipo 8, Pending, sem lote/carga/nota, `GrossWeight = NewQuantity`, armazém/cartão/liberação do destino, data/hora; create + confirm Deferred (`isShipmentTransaction: true`); `SaveChanges`; pré-checagem pt-BR `if (novaPurchase.NetWeight > targetContract.AvaiableVolume)` (sem tolerância) → "O contrato {Code} não tem saldo para {x:N3} (disponível {y:N3})."; `allocationCreate.ExecuteAsync(targetContract.Key, novaPurchase, novaPurchase.NetWeight, user)`.
f. `SHIPPING_TRANSACTIONS` novo `{ PurchaseStorageTransaction = nova8?, SalesStorageTransaction = nova7 }`. Preencha as chaves da `ShippingReleaseChange`.

Depois de todos os itens: `SaveChanges`; recálculo de `ShippedQuantity` de todas as liberações de origem e destino; conferência de saldo das liberações de destino (`ReleasedQuantity − ShippedQuantity < −0,001` → "Saldo insuficiente na liberação do contrato {Code}: faltam {x:N3}."); para cada carga tocada: `ShipmentLoadsRecalculateTotalService.RecalculateAsync` e `ShipmentLoadsRecalculateInvoicedService.RecalculateAsync`; movimentação `ReleaseChanged` por item com `quantity = New − Original`, `balanceAfter = load.AvailableQuantity`, descrição "Romaneio {orig} substituído por {nova}: contrato {C1} ({F1}), armazém {W1} → contrato {C2} ({F2}), armazém {W2}; estorno {12code}." e contexto (Card/Warehouse novos, Reason, `StorageTransactionKey = nova7.Key`); `SaveChanges`; `Commit`. Erro → `Rollback` e relançar.

**Tests (TDD; fixture: reaproveitar a do v1 — `SeedReleaseAsync`, `ShipIntoInvoicedLoadAsync` —, com a carga ganhando `LoadDate` passado explícito e `InvoicedQuantity` coerente; para a regra da quantidade, crie nota da carga no InMemory seguindo `ShipmentLoadsRecalculateInvoicedServiceTests`):**
1. Standard→Standard (carga com 2 vigentes): original substituída fora da carga; 12 e 9 com data da carga, `ShippingReleaseChangeKey`; 9 com `NetWeight` igual à 8 original; alocação −líq no contrato de origem e +líq no destino; `ShippedQuantity` origem 0 / destino líq; nova 7 com `ShipmentLoadKey`, status Invoiced, armazém/fornecedor do destino, data da carga; `TotalQuantity` da carga inalterado; linha `SHIPPING_RELEASE_CHANGES` completa; movimentação 14.
2. Carga com **1 vigente**: nova quantidade = faturado líquido (≠ bruto original) e `TotalQuantity` passa a ser esse valor; faturado 0 → recusa com a mensagem.
3. Standard→OwnershipTransfer (lote em outro armazém): sem 9 nova/8 nova; 12 da origem sem liberação; nova 7 no armazém/lote do destino; `ShippedQuantity` do destino = nova quantidade.
4. OwnershipTransfer→Standard: 12 com liberação e lote de origem; sem 9; nova 8+7 com alocação; `ShippedQuantity` da origem 0.
5. OwnershipTransfer→OwnershipTransfer no **mesmo lote** com saldo do reader = 0 → aceita (required líquido ≤ 0).
6. **Inversão entre origens** (1 Standard + 1 Transfer, mesma carga) → cada nova com o bruto da original; alocações/ShippedQuantity/lotes corretos; `OperationGroupKey` comum.
7. Troca da **Expedição nova** de novo (vigente) → segunda troca gera outro trio; a primeira nova fica substituída.
8. Conferência Approved com `ReferenceDate == LoadDate` no armazém de destino → recusa com código; `Draft`/`Cancelled` ou data anterior → passa.
9. Contrato Finished: recusa em Standard; **aceita** liberação `SalesReturn` com contrato Finished.
10. Recusas do v1 que continuam (motivo, destino inativo, produto, mesmo destino, carga Cancelled/Returned, duplicado com "mais de uma vez", saldo da liberação, lote sem saldo).
11. Regressão: depois da troca, cancelar a carga (`ShipmentLoadsCancelService`, se a fixture permitir sem notas — use carga sem nota e 2 vigentes) devolve só a nova à Montagem; a substituída continua fora.

- [ ] Steps: reescrever testes (RED) → guard → serviço → GREEN focado → suíte → build → DI. `git add` do guard novo.

---

### Task 4: Grid da carga, lista de destinos e controller

**Files:**
- Modify: `ShipmentLoads/ShipmentLoadsGetService.cs` (`QueryTransactions`, :40-45)
- Modify: `ShipmentReleases/ShipmentReleasesPurchaseContractsService.cs` (+ controller da function se ler `WarehouseCode` com `.ToString()` inseguro)
- Modify: `SiagroB1.Web/Actions/ShippingTransactions/ShippingTransactionsChangeReleaseController.cs`
- Tests: onde houver classe de teste do serviço; senão novas em `SiagroB1.Application.Tests/ShipmentLoads/` e `.../ShipmentReleases/`

1. `QueryTransactions(loadKey)`: `ShipmentLoadKey == key` **ou** (`ReplacedByShippingReleaseChangeKey`/`ShippingReleaseChangeKey` ∈ trocas com `ShipmentLoadKey == key`) — restrito a tipos 7 e 12 (8 e 9 não aparecem no grid). Mantenha `IQueryable` de raiz DbSet (o `$expand` depende disso).
2. `ShipmentReleasesPurchaseContractsService.ExecuteAsync(itemCode, warehouseCode)`: `warehouseCode` nulo/vazio → sem filtro de armazém. Callers com armazém inalterados.
3. Controller: `NotFound(e.Message)`; `DbUpdateConcurrencyException` (direto ou como `InnerException`) → `BadRequest("Os romaneios foram alterados por outro usuário. Recarregue a carga e tente novamente.")`.
- Tests: grid devolve vigente + substituída + 12 da troca e não devolve 9/8 nem romaneio de outra carga; lista sem armazém traz liberações de 2 armazéns e com armazém filtra.
- [ ] Steps: RED → implementação → GREEN → suíte → build.

---

### Task 5: Frontend

**Files:** `webapp/view/shipmentLoads/Detail.view.xml`, `webapp/controller/shipmentLoads/Detail.controller.ts`, `webapp/view/shipmentLoads/fragments/ChangeRelease.fragment.xml`, `webapp/model/formatter.ts`, `webapp/controller/shipmentLoads/Attach.controller.ts`

1. Grid "Romaneios da Carga": `$select` passa a incluir `TransactionType,ReplacedByShippingReleaseChangeKey,ShippingReleaseChangeKey,WarehouseName`; nova coluna **Situação** (`ObjectStatus`) via formatter `formatShipmentLoadTransactionSituation(type, replacedBy, changeKey)` → "Substituída" (Warning) quando `replacedBy`; "Estorno de troca" (Information) quando tipo `SalesShipmentReturn`; "Expedição de troca" (Success) quando `changeKey` e tipo `SalesShipment`; "Vigente" (None) nos demais. Use `targetType: 'any'` no enum.
2. `onDetachShipments` e `onChangeRelease`: só aceitam linhas vigentes (tipo `SalesShipment` e `ReplacedByShippingReleaseChangeKey` vazio); outra seleção → `MessageBox.warning("Selecione apenas romaneios vigentes da carga.")`.
3. Diálogo: 1 linha → function com `WarehouseCode = ""` (todos os armazéns), coluna **Armazém** `({DeliveryLocationCode}) {DeliveryLocationName}` nos destinos; tabela de romaneios com **Armazém Atual** e, na inversão, **Novo Armazém** (espelho de NewContractCode). Texto informativo no diálogo: "A troca gera um estorno na origem e uma nova Expedição no destino, com a data da carga."
4. `Attach.controller.ts`: filtro de disponíveis acrescenta `ReplacedByShippingReleaseChangeKey eq null and ShippingReleaseChangeKey eq null`.
5. Movimentação: rótulo "Troca de Liberação" já existe.
- Gates: `yarn ts-typecheck`, `yarn lint`. Sem `--` em comentário XML.

---

### Task 6: Verificação no navegador (SAP acessível)

Seguir `superpowers:verification-before-completion` e as memórias `run-stack-locally` / `yokotobi-verification-env-timeouts-and-sap-unreachable`.
1. Subir `yktb` (Web+Gateway) + `yarn start:dev`; usuário faz login (senha não é digitada pelo agente); filial 3.
2. Criar pela tela 2 liberações do contrato 00004422 no armazém F024409 (10.000 kg e 50.000 kg) e ativá-las.
3. Registrar checksums (alocações, liberações, romaneios, movimentações) antes.
4. CG000002: trocar 00001606 → liberação 50.000 kg; conferir grid (substituída, estorno, expedição de troca), Movimentação, saldos da liberação e alocações nos dois contratos, notas intactas, datas = data da carga, quantidade conforme a regra.
5. Recusa com rollback: trocar a nova vigente para a de 10.000 kg quando ela não comportar → mensagem pt-BR e checksums iguais aos anteriores a essa tentativa.
6. Se houver conferência aprovada posterior no dado local, conferir a trava; senão registrar como coberto só por teste.
7. Derrubar o stack e conferir portas.
