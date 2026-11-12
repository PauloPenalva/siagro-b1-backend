# GAC-1213 Quebra de trajeto — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Quarto destino da recusa de carga, "Quebra de trajeto": devolve o documento de saída, tira o volume da carga e não toca em armazém nem na perna de compra.

**Architecture:** O destino novo reaproveita `ShipmentLoadsRefuseService` (devoluções) e ganha um ramo em `ShipmentLoadRefusalEffectsService` que grava um romaneio tipo 18 ligado à carga por `RefusedFromShipmentLoadKey`. A soma desses romaneios é o quinto termo do saldo da carga, gravado pelo escritor único `ShipmentLoadsRecalculateInvoicedService`, que também resolve a situação nova.

**Tech Stack:** .NET 10, EF Core (SQL Server; InMemory nos testes), OData, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-11-shipment-load-transit-loss-design.md` — leia antes de qualquer tarefa.

## Global Constraints

- Identificadores em inglês; mensagens ao usuário e comentários em pt-BR.
- Enums gravados como int: valores novos **sempre no fim**, nunca renumerar. Valores fixados: `RefusalDestination.TransitLoss = 3`, `StorageTransactionType.TransitLoss = 18`, `ShipmentLoadStatus.TransitLoss = 10`, `ShipmentLoadMovementType.TransitLoss = 26`, `TransitLossReason { Damage = 0, Accident = 1, Theft = 2, Other = 3 }`.
- Serviços internos chamados de dentro da recusa sempre em `CommitMode.Deferred` (`UnitOfWork.CommitAsync` não é aninhável).
- O romaneio 18 nunca carrega `ShipmentLoadKey`, `ShipmentReleaseKey`, `ReturnInvoiceKey`, contrato ou alocação.
- Somatórios no servidor (`SumAsync`) não veem entidade rastreada: `SaveChangesAsync` antes de recalcular.
- Serviço novo é registrado à mão em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`.
- Todo arquivo novo: `git add` imediato. Commits no branch `feature/gac-1213-transit-loss`, escopo `shipment`, `Refs: GAC-1213`; o commit da migration leva `DB: AddShipmentLoadTransitLoss`. Nunca push.
- Não aplicar migration no banco local compartilhado; verificação em cópia.
- Testes: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Classe>"`.

## Review Focus

1. Segunda recusa com quebra na mesma carga (duas entradas tipo 18): saldo soma as duas e o estorno de uma cancela só a dela. → teste na Task 6.
2. Recusa com quebra parcial seguida de recusa do resto para Armazém: situação final `TransitLoss`, os dois termos corretos, nenhum volume contado duas vezes. → teste na Task 4.
3. Destino `TransitLoss` sem `TransitLossReason`, ou com armazém informado: recusado antes de qualquer escrita, sem devolução criada. → teste na Task 4.
4. Carga `TransitLoss` clicando "Recalcular Saldo" ou recebendo ticket de descarga: situação não muda e romaneios de embarque não voltam a `Confirmed`. → teste na Task 2.
5. Dois tempos: NF-e de uma das devoluções cancelada antes da conclusão → a quebra é gravada só pelo volume confirmado; todas canceladas → recusa cancelada e nenhum romaneio 18. → teste na Task 5.

---

### Task 1: Modelo, enums e migration

**Files:**
- Create: `SiagroB1.Domain/Enums/TransitLossReason.cs`
- Modify: `SiagroB1.Domain/Enums/{RefusalDestination,StorageTransactionType,ShipmentLoadStatus,ShipmentLoadMovementType}.cs`
- Modify: `SiagroB1.Domain/Entities/{ShipmentLoad,StorageTransaction,ShipmentLoadRefusal}.cs`
- Modify (chamadores da fórmula): `ShipmentLoadsBillingGuardService.cs:121`, `ShipmentLoadsTransshipmentStartService.cs:59`
- Create: migration `AddShipmentLoadTransitLoss` (AppDbContext)
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadTransitLossModelTests.cs`; ajustar `ShipmentLoadTransshipmentModelTests.cs`

**Interfaces — Produces:**
- `enum TransitLossReason`
- `ShipmentLoad.TransitLossQuantity` (`decimal`, `DECIMAL(18,3) DEFAULT 0`)
- `ShipmentLoad.CalculateAvailableQuantity(decimal total, decimal invoiced, decimal returnedToWarehouse, decimal transshipped, decimal transitLoss)`
- `StorageTransaction.TransitLossReason` (`TransitLossReason?`), `ShipmentLoadRefusal.TransitLossReason` (`TransitLossReason?`)

- [ ] **Step 1: Testes que falham**

```csharp
[Fact] public void EnumValues_AreAppendOnly()
{
    Assert.Equal(3, (int)RefusalDestination.TransitLoss);
    Assert.Equal(18, (int)StorageTransactionType.TransitLoss);
    Assert.Equal(10, (int)ShipmentLoadStatus.TransitLoss);
    Assert.Equal(26, (int)ShipmentLoadMovementType.TransitLoss);
    Assert.Equal(new[] { 0, 1, 2, 3 }, Enum.GetValues<TransitLossReason>().Cast<int>());
}

[Theory]
[InlineData(40_000, 25_000, 0, 0, 15_000, 0)]
[InlineData(40_000, 10_000, 5_000, 5_000, 8_000, 12_000)]
public void CalculateAvailableQuantity_SubtractsTheFiveTerms(
    decimal total, decimal invoiced, decimal returned, decimal transshipped, decimal loss, decimal expected) =>
    Assert.Equal(expected, ShipmentLoad.CalculateAvailableQuantity(total, invoiced, returned, transshipped, loss));

[Fact] public void AvailableQuantity_UsesTransitLossQuantity() // instância com TransitLossQuantity = 15_000 de 40_000, 25_000 faturados → 0
```

- [ ] **Step 2:** rodar `--filter "FullyQualifiedName~ShipmentLoadTransitLossModelTests"` → não compila.
- [ ] **Step 3:** implementar enums, colunas e o quinto parâmetro (obrigatório, sem default, para o compilador apontar todo chamador). Os dois chamadores passam o valor recalculado da Task 2; nesta task, `load.TransitLossQuantity`. XML-doc de cada item em pt-BR no estilo dos vizinhos.
- [ ] **Step 4:** `dotnet ef migrations add AddShipmentLoadTransitLoss --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`. Conferir que o `Up` tem só as três colunas da spec §4.8; `TransitLossQuantity` com `nullable: false, defaultValue: 0m`.
- [ ] **Step 5:** testes da classe verdes + `dotnet build SiagroB1.sln` sem erro.
- [ ] **Step 6:** commit `feat(shipment): modela a quebra de trajeto na carga e no romaneio` com `DB: AddShipmentLoadTransitLoss`.

### Task 2: Quinto termo do saldo e situação da carga

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsRecalculateTransitLossService.cs`
- Modify: `ShipmentLoadsRecalculateInvoicedService.cs`, `ShipmentLoadsBillingGuardService.cs`, `ShipmentLoadsTransshipmentStartService.cs`, `ShipmentLoadsCompositionGuardService.cs`, `SiagroB1.Domain/Entities/ShipmentLoadChangeLogFields.cs` (`DescribeStatus`)
- Test: `ShipmentLoadsRecalculateTransitLossServiceTests.cs` (novo), `ShipmentLoadResolveStatusTests.cs`, `ShipmentLoadsBillingGuardServiceTests.cs`, `ShipmentLoadsCancelServiceTests.cs`/`ShipmentLoadsDetachTransactionsServiceTests.cs`

**Interfaces — Produces:**
- `static Task<decimal> ShipmentLoadsRecalculateTransitLossService.CalculateTransitLossAsync(AppDbContext context, Guid shipmentLoadKey)` — Σ `GrossWeight` onde `RefusedFromShipmentLoadKey == carga && TransactionType == TransitLoss && TransactionStatus != Cancelled`, arredondado a 3 casas.
- `ResolveStatus(decimal total, decimal invoiced, decimal returnedToWarehouse, decimal transshipped, decimal transitLoss, bool hasOpenTransshipment, bool hasPendingRefusal = false)`

- [ ] **Step 1: Testes que falham**

```csharp
// ResolveStatus(total, invoiced, returned, transshipped, loss, openTransshipment)
[Theory]
[InlineData(40_000, 0, 0, 0, 40_000, ShipmentLoadStatus.TransitLoss)]        // perda total
[InlineData(40_000, 25_000, 0, 0, 15_000, ShipmentLoadStatus.TransitLoss)]   // mista: entregue + perdida
[InlineData(40_000, 0, 20_000, 0, 20_000, ShipmentLoadStatus.TransitLoss)]   // quebra vence Devolvida
[InlineData(40_000, 0, 0, 0, 10_000, ShipmentLoadStatus.PartiallyInvoiced)]  // saldo aberto: quebra conta como consumo
[InlineData(40_000, 40_000, 0, 0, 0, ShipmentLoadStatus.Invoiced)]           // sem quebra, nada muda
public void ResolveStatus_WithTransitLoss(...)
```

Mais: `CalculateTransitLossAsync` ignora tipo 12 e cancelados; recálculo grava `TransitLossQuantity`, projeta `Invoiced` nos romaneios de embarque de carga `TransitLoss` e mantém a situação ao recalcular de novo (Review Focus 4); `ResolveClosure(TransitLoss, true, true)` devolve `TransitLoss`; guard de composição lança `"A carga {code} teve {qty:N3} perdido(s) em quebra de trajeto e sua composição não pode mais ser alterada."`; guard de faturamento não oferece o volume perdido.

- [ ] **Step 2:** rodar os filtros das classes → falham.
- [ ] **Step 3:** implementar. No `ResolveStatus`, ramo de fechamento: `transitLoss > Tolerance → TransitLoss`, senão as regras atuais; no critério `Open`, somar `transitLoss` ao consumo. `TransitLoss` entra na lista de situações que projetam `Invoiced` nos romaneios. `DescribeStatus` → `"Quebra de trajeto"`. Atualizar todos os chamadores e testes existentes de `ResolveStatus` (parâmetro posicional novo = 0).
- [ ] **Step 4:** `--filter "FullyQualifiedName~ShipmentLoads"` verde.
- [ ] **Step 5:** commit `feat(shipment): quinto termo do saldo e situação Quebra de trajeto`.

### Task 3: Romaneio tipo 18 fora de todo saldo

**Files:**
- Modify: `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsConfirmedService.cs` (case novo), e o que `StorageTransactionsCreateService` exigir para aceitar o tipo sem contrato/endereço
- Test: `SiagroB1.Application.Tests/ShipmentLoads/StorageTransactionTransitLossTests.cs`

**Interfaces — Produces:** confirmar um `StorageTransaction` tipo `TransitLoss` em `CommitMode.Deferred` só o leva a `Confirmed` (sem descontos, sem alocação, sem movimento de saldo).

- [ ] **Step 1: Testes que falham** — criar e confirmar um romaneio 18 de 15.000 no armazém `ARM01` com saldo prévio conhecido e uma liberação de compra com embarcado conhecido, e afirmar:
  - `TransactionStatus == Confirmed`, `NetWeight == GrossWeight`;
  - `StorageTransactionsWarehouseBalanceService` devolve o mesmo saldo de antes;
  - `ShipmentReleasesRecalculateShippedService.AffectsShippedQuantity(TransitLoss) == false`;
  - nenhuma linha em alocações de contrato de compra/venda;
  - `StorageTransactionsCancelService` e `StorageTransactionsReverseService` recusam (vínculo `RefusedFromShipmentLoadKey`), no molde de `StorageTransactionsRefusalGuardTests`.
- [ ] **Step 2:** rodar → falha (cai no `default`, que trata como compra).
- [ ] **Step 3:** case `TransitLoss` em `Processing` chamando `ExecuteTransitLossTransactionAsync(st, userName, commitMode)`, molde enxuto de `ExecuteSalesShipmentReturnTransactionAsync` sem nenhum efeito de saldo. Auditar com `grep -rn "StorageTransactionType\." SiagroB1.Application SiagroB1.Reports SiagroB1.Infra` todo filtro por **exclusão** (`!=`, `is not`) ou `default:` de switch que passaria a incluir o 18; corrigir cada um e listar no corpo do commit os que foram tocados.
- [ ] **Step 4:** classe verde.
- [ ] **Step 5:** commit `feat(storage): romaneio de quebra de trajeto fora dos saldos`.

### Task 4: Recusa síncrona com destino Quebra de trajeto

**Files:**
- Modify: `ShipmentLoadsRefuseService.cs` (`RefusalRequest`, `Validate`), `ShipmentLoadRefusalEffectsService.cs` (`RefusalEffectsInput`, ramo novo)
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsRefuseTransitLossTests.cs` (reusa a fixture `internal` de `ShipmentLoadsRefuseServiceTests`, como faz `ShipmentLoadsRefuseTransshipmentTests`)

**Interfaces:**
- Produces: `RefusalRequest(Guid ShipmentLoadKey, IReadOnlyList<RefusalLine> Lines, RefusalDestination Destination, string? DestinationWarehouseCode, string Reason, TransitLossReason? TransitLossReason = null)`; `RefusalEffectsInput(..., string Reason, TransitLossReason? TransitLossReason = null)`.
- Consumes: Tasks 1–3.

- [ ] **Step 1: Testes que falham** (carga de 40.000 faturada pelo caminho real, com perna de compra alocada num contrato de compra):
  - `TotalLoss_ClosesLoadAndKeepsPurchaseDelivered`: recusa 40.000 `TransitLoss`/`Theft` → devolução confirmada; um romaneio 18 `Confirmed`, `GrossWeight 40_000`, `TransitLossReason == Theft`, `RefusedFromShipmentLoadKey == load.Key`, `ShipmentLoadKey/ShipmentReleaseKey/ReturnInvoiceKey` nulos, `WarehouseCode == load.WarehouseCode`, `Comments` contendo `"Documento(s): <número>"`; carga `TransitLoss`, `TransitLossQuantity 40_000`, `AvailableQuantity 0`, `ReturnedToWarehouseQuantity 0`; movimento `TransitLoss` com delta `-40_000`; romaneio Compra (8) da carga ainda `Confirmed`/`Invoiced` e a alocação/`AllocatedVolume` do contrato de compra iguais aos de antes; saldo do contrato de venda e da liberação de venda restaurados; nenhuma `ShipmentRelease` nova; saldo de armazém igual.
  - `PartialLoss_KeepsRestDelivered`: recusa 15.000 → carga `TransitLoss`, `InvoicedQuantity 25_000`, `TransitLossQuantity 15_000`.
  - `PartialLossThenWarehouseReturn` (Review Focus 2): 15.000 quebra + 25.000 Armazém → `TransitLoss`, termos 15.000 e 25.000, disponível 0.
  - `MissingReason_IsRejectedBeforeAnyWrite` e `WarehouseInformed_IsRejected` (Review Focus 3): mensagens `"Informe o tipo da quebra de trajeto."` e `"A quebra de trajeto não tem armazém de destino."`; nenhuma devolução criada.
  - `TransitLossReasonOnOtherDestination_IsIgnored`: destino `Rebilling` com motivo informado não grava romaneio 18.
- [ ] **Step 2:** rodar → falham.
- [ ] **Step 3:** implementar `RegisterTransitLossAsync(RefusalEffectsInput input, string userName)` conforme spec §4.2: criar (`TransactionCode.ShipmentLoad`, Deferred) → `SaveChanges` → confirmar (Deferred) → `SaveChanges` → `ShipmentLoadsRecalculateInvoicedService.RecalculateAsync` → movimento. Texto do `Comments`: `"Quebra de trajeto da carga {code} ({rótulo do tipo}). Motivo: {reason}. Documento(s): {números}. Cliente(s): {códigos}."`, respeitando `VARCHAR(500)`. Rótulos pt-BR do tipo num helper estático `TransitLossReasonText.Describe(TransitLossReason)` em `SiagroB1.Domain/Enums/` com os textos da tabela da spec §4.1.
- [ ] **Step 4:** classe verde + `ShipmentLoadsRefuseServiceTests` e `ShipmentLoadsRefuseTransshipmentTests` verdes.
- [ ] **Step 5:** commit `feat(shipment): recusa de carga com destino quebra de trajeto`.

### Task 5: Recusa em dois tempos

**Files:**
- Modify: `ShipmentLoadsRefuseService.cs` (`RegisterDeferredAsync` grava `refusal.TransitLossReason`), `ShipmentLoadRefusalCompleteService.cs` (repassa), `ShipmentLoadsPendingRefusalService.cs` + seu DTO (expõe `TransitLossReason` como string do enum, anulável)
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsDeferredTransitLossTests.cs` (molde de `ShipmentLoadsDeferredRefusalTests` e `ShipmentLoadRefusalCompleteTests`)

- [ ] **Step 1: Testes que falham**: registro deixa a recusa `Pending` com `Destination TransitLoss` e `TransitLossReason`, carga `RefusalPending`, nenhum romaneio 18; confirmar a última devolução conclui e grava o romaneio 18 com o motivo guardado e a carga `TransitLoss`; com uma devolução cancelada, a quebra é só do volume confirmado; todas canceladas → recusa `Cancelled`, zero romaneio 18 (Review Focus 5); "Cancelar recusa" pendente idem; a function de recusa pendente devolve o tipo da quebra.
- [ ] **Step 2–4:** falha → implementar → verde (incluindo `ShipmentLoadPendingRefusalTests`, `ShipmentLoadsCancelRefusalTests`).
- [ ] **Step 5:** commit `feat(shipment): quebra de trajeto na recusa aguardando NF-e`.

### Task 6: Estorno da devolução desfaz a quebra

**Files:**
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesReverseConfirmService.cs` (ramo `ReverseLoadReturnAsync` / vizinhança de `ReclaimGoodsFromWarehouseAsync`)
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesReverseConfirmTransitLossTests.cs` (molde de `SalesInvoicesReverseConfirmLoadReturnTests`)

- [ ] **Step 1: Testes que falham**: estornar a confirmação da devolução de uma recusa com quebra marca o romaneio 18 daquela recusa `Cancelled` (não apaga), zera `TransitLossQuantity`, a carga volta a `Invoiced` e o contrato de venda volta a ficar consumido; recusa de dois documentos: estornar um estorna o irmão junto (mesma regra da devolução ao armazém); duas recusas com quebra na carga (Review Focus 1): estornar a segunda cancela só a entrada que lista os documentos dela e `TransitLossQuantity` fica com o volume da primeira; estorno de devolução "segue viagem" continua sem tocar em romaneio 18.
- [ ] **Step 2:** rodar → falham.
- [ ] **Step 3:** implementar `ReclaimTransitLossAsync(Guid shipmentLoadKey, SalesInvoice returnInvoice, string userName)` devolvendo os retornos irmãos confirmados, com a mesma seleção por `RefusalEntryDocuments.Parse(entry.Comments)` usada em `ReclaimGoodsFromWarehouseAsync`, sem conferência de saldo de armazém e sem cancelar liberações. Extrair o que for comum em vez de copiar.
- [ ] **Step 4:** classe verde + `SalesInvoicesReverse*` verdes.
- [ ] **Step 5:** commit `feat(invoice): estorno da devolução cancela a quebra de trajeto`.

### Task 7: Camada Web, fluxo legado e relatórios

**Files:**
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (ação `ShipmentLoadsRefuse`: parâmetro `TransitLossReason`, `Edm.String`, opcional/nullable), `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsRefuseController.cs`, `ShipmentLoadActionParameters.cs` se a ação estiver lá, `SiagroB1.Web/Actions/SalesInvoices/SalesInvoicesReturnController.cs` + `SalesInvoicesReturnService.cs`, `SiagroB1.Reports/Services/ShipmentLoadsByPeriodReportService.cs` e demais mapas de rótulo de situação/tipo em `SiagroB1.Reports` (`grep -rn "Devolvida\|ReturnedToWarehouse" SiagroB1.Reports`)
- Test: `ShipmentLoadEdmModelTests.cs`, `ShipmentLoadActionParametersTests.cs`, `SalesInvoicesReturnServiceTests.cs`, `Reports/ShipmentLoadsByPeriodReportServiceTests.cs`

- [ ] **Step 1: Testes que falham**: EDM tem `TransitLossReason` na ação e `TransitLossQuantity` em `ShipmentLoad`, `TransitLossReason` em `StorageTransaction`; `ParseDestination("TransitLoss")` e parsing `"Damage"|"Accident"|"Theft"|"Other"` (valor desconhecido → 400 `"Tipo de quebra de trajeto inválido."`); ação chamada **sem** o parâmetro novo continua funcionando para os outros destinos (o `ODataActionParameters` não pode chegar nulo — ver comentário do controller); retorno legado com destino `TransitLoss` → `"A quebra de trajeto só pode ser registrada pela recusa de carga."`; relatório de cargas por período rotula a situação 10 como `"Quebra de trajeto"` e soma a quebra onde soma o devolvido ao armazém.
- [ ] **Step 2–3:** falha → implementar. A resposta da ação passa a incluir `load.TransitLossQuantity`.
- [ ] **Step 4:** classes verdes.
- [ ] **Step 5:** commit `feat(shipment): expõe a quebra de trajeto na API e nos relatórios`.

### Task 8: Fechamento

- [ ] **Step 1:** `dotnet build SiagroB1.sln` sem erro nem warning novo.
- [ ] **Step 2:** `dotnet test SiagroB1.Application.Tests` e `dotnet test SiagroB1.Fiscal.Tests` — tudo verde; registrar a contagem.
- [ ] **Step 3:** `dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web` → "No changes".
- [ ] **Step 4:** `git status` limpo (nada untracked), `git log --oneline main..HEAD` conferido contra as tarefas.
