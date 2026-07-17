# Design: Finalização/reabertura + recálculo manual de `ShipmentRelease`

**Data:** 2026-07-17
**Repositório:** `siagro-b1-backend`
**Autor:** Paulo Penalva (com Claude Code)

## Contexto

Espelha a feature de contrato de compra (`2026-07-17-contract-recalc-and-closing-design.md`), agora para liberação de embarque (`ShipmentRelease`). Necessidades:

1. **Finalização/fechamento + reabertura** manual da liberação, bloqueando novos romaneios.
2. **Recálculo manual** de saldo (`ShippedQuantity`) — um release ou todos — para o usuário rodar quando tiver dúvida.
3. **Refatorar `ShipmentRelease.HasStorageTransactions`** (property computada sobre navegação, mesma classe de fragilidade do antigo `AvailableQuantity`).

Fatos:
- `ReleaseStatus` (`SiagroB1.Domain/Enums`): `Pending=0, Actived=1, Completed=2, Cancelled=3, Paused=4`. `Completed` ("Totalmente romaneada") **existe, é tratado como terminal** em Approve/Cancel/Pause, mas **nunca é atribuído** — análogo ao `ContractStatus.Finished`.
- Releases `Actived` são os únicos incluídos nas listas de saldo (`ShipmentReleasesBalanceService`, `ShipmentReleasesPurchaseContractsService`) — um `Completed` já some da UI de seleção.
- `HasStorageTransactions` é usado só em `ShipmentReleasesCancelationService`.

## Parte A — Finalização/reabertura

Reusar `ReleaseStatus.Completed` como estado finalizado (sem enum novo, **sem migration**).

### Serviços (espelham `PurchaseContractsClose/ReopenService`; injetam `AppDbContext`)

- `ShipmentReleasesCloseService.ExecuteAsync(Guid key, string userName)`:
  - Carrega release com `Status ∈ {Actived, Paused}`; senão `NotFoundException("Liberação não encontrada ou não está ativa/pausada.")`.
  - `Status = Completed; UpdatedAt = now; UpdatedBy = userName;` salva.
- `ShipmentReleasesReopenService.ExecuteAsync(Guid key, string userName)`:
  - Carrega release com `Status == Completed`; senão `NotFoundException("Liberação não encontrada ou não está finalizada.")`.
  - `Status = Actived; UpdatedAt = now; UpdatedBy = userName;` salva.

### Guard de movimentação (bloquear novo romaneio)

Uma liberação **não disponível para embarque** — `Completed` (finalizada), `Cancelled` (morta) ou `Paused` (armazém não disponibiliza o produto por período indeterminado) — não aceita novos romaneios. Guard em `StorageTransactionsCreateService` **e** `StorageTransactionsConfirmedService`: se a transação é `SalesShipment`/`SalesShipmentReturn` com `ShipmentReleaseKey` cujo release está em `Completed`/`Cancelled`/`Paused` → `ApplicationException("Liberação de embarque finalizada/cancelada/pausada: não é possível romanear.")`. Colocado antes de criar/confirmar; convive com os hooks de recálculo (o guard roda antes).

`Actived` continua liberando romaneios normalmente.

## Parte B — Recálculo manual de saldo

`ShipmentReleasesRecalculateBalanceService` (novo, user-facing; injeta `AppDbContext` + o `ShipmentReleasesRecalculateShippedService` interno já existente):

- `Task<ShipmentReleaseRecalcResultDto> ExecuteAsync(Guid key)`:
  - Carrega release; se não existe → `NotFoundException`.
  - Se `Status == Completed` → `ApplicationException("Liberação finalizada não participa do recálculo de saldo.")`.
  - Captura `previous`; chama `recalcShipped.RecalculateAsync(key)`; captura `new`; retorna DTO com antes/depois e `Changed`.
- `Task<ShipmentReleaseRecalcAllResultDto> ExecuteAllAsync()`:
  - Todos os releases com `Status != Completed`; recalcula cada um; retorna `Scanned`/`Changed`/lista dos divergentes.

### DTOs (`SiagroB1.Domain/Dtos/`)

```csharp
public class ShipmentReleaseRecalcResultDto
{
    public Guid Key { get; set; }
    public decimal PreviousShippedQuantity { get; set; }
    public decimal NewShippedQuantity { get; set; }
    public decimal PreviousAvailableQuantity { get; set; }
    public decimal NewAvailableQuantity { get; set; }
    public bool Changed { get; set; }
}

public class ShipmentReleaseRecalcAllResultDto
{
    public int Scanned { get; set; }
    public int Changed { get; set; }
    public ICollection<ShipmentReleaseRecalcResultDto> Changes { get; set; } = [];
}
```

## Parte C — Refatorar `HasStorageTransactions`

Em `ShipmentReleasesCancelationService`, trocar `if (sr.HasStorageTransactions)` por uma query direta:

```csharp
var hasTransactions = await context.StorageTransactions.AnyAsync(t =>
    t.ShipmentReleaseKey == sr.Key &&
    t.TransactionStatus != StorageTransactionsStatus.Cancelled &&
    (t.TransactionType == StorageTransactionType.SalesShipment ||
     t.TransactionType == StorageTransactionType.SalesShipmentReturn ||
     t.TransactionType == StorageTransactionType.Purchase ||
     t.TransactionType == StorageTransactionType.PurchaseReturn));
```

Remove a dependência da navegação `Transactions` naquele caminho (o `.Include(x => x.Transactions)` pode ser removido). A property `ShipmentRelease.HasStorageTransactions` fica marcada `[Obsolete]` (não removida para não quebrar bindings/serialização inadvertidos; sem uso em código).

## Endpoints OData

- `ShipmentReleasesClose` (Action, `Parameter<Guid>("Key")`, `Returns<IActionResult>()`).
- `ShipmentReleasesReopen` (Action, `Parameter<Guid>("Key")`, `Returns<IActionResult>()`).
- `ShipmentReleasesRecalculateBalance` (Action, `Parameter<Guid>("Key")`, `Returns<ShipmentReleaseRecalcResultDto>()`).
- `ShipmentReleasesRecalculateAllBalances` (Action, `Returns<ShipmentReleaseRecalcAllResultDto>()`).

Controllers em `SiagroB1.Web/Actions/ShipmentReleases/`, `ODataController`, `[HttpPost("odata/<Nome>")]`, mapeando `NotFoundException`/`KeyNotFoundException` → 404 e `ApplicationException` → 400. Serviços registrados no bloco `// shipment releases` de `ServiceCollectionExtensions`; actions no `ODataConfigurations`.

## Testes (TDD, `SiagroB1.Application.Tests`)

1. `Close`: `Actived → Completed` e `Paused → Completed`, grava `UpdatedBy`.
2. `Close` de release não-Actived/Paused (ex.: `Pending`) → `NotFoundException`.
3. `Reopen`: `Completed → Actived`.
4. `Reopen` de release não-Completed → `NotFoundException`.
5. Recalc manual: corrige `ShippedQuantity` divergente + antes/depois; `Changed` correto.
6. Recalc de release `Completed` → `ApplicationException`.
7. Recalc de release inexistente → `NotFoundException`.
8. `ExecuteAllAsync` recalcula não-Completed, exclui `Completed`, lista os divergentes.
9. Guard: criar romaneio (`SalesShipment`) ligado a release `Completed`/`Cancelled`/`Paused` → rejeita (via `StorageTransactionsCreateService`).
10. `ShipmentReleasesCancelationService` detecta transações via query direta (sem depender da navegação carregada).

## Fora de escopo

- Frontend (fase separada, análoga ao contrato: botões Finalizar/Reabrir/Recalcular na tela de liberação).
- Migration (nenhuma mudança de schema; `Completed` já existe).
- `Pending` como estado que bloqueia romaneio (só `Completed`/`Cancelled`/`Paused` bloqueiam; `Actived` libera).

## Riscos e mitigações

| Risco | Mitigação |
|---|---|
| Guard esquecido em algum caminho de criação de romaneio | Cobrir create + confirm; teste no create |
| Recalc concorrente com hook interno no mesmo release | `RowVersion` do release → `DbUpdateConcurrencyException` (degrada para erro) |
| Reabrir release finalizado reintroduz divergência | Reabertura explícita e auditada (`UpdatedBy`); volta a aceitar romaneio e a participar do recálculo |
| `Completed` tinha intenção futura de auto-fechamento ao zerar saldo | Decisão registrada de reusá-lo como fechamento manual; se auto-fechamento surgir, distinguir com novo status |
