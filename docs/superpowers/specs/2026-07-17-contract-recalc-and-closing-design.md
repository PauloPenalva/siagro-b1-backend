# Design: Recálculo manual de saldo + Encerramento de contratos de compra

**Data:** 2026-07-17
**Repositório:** `siagro-b1-backend`
**Autor:** Paulo Penalva (com Claude Code)

## Contexto

`PurchaseContract.AvaiableVolume` passou a derivar de uma coluna persistida `AllocatedVolume` (= Σ das alocações, com sinal), recalculada nos serviços de alocação (ver spec `2026-07-17-contract-available-volume-design.md`). Duas necessidades operacionais surgem:

1. **Recálculo manual** — quando o usuário tem dúvida sobre o saldo de um contrato (ou quer uma reconciliação geral), poder rodar sob demanda um recálculo que redereva `AllocatedVolume` a partir das alocações e mostra o antes/depois.
2. **Encerramento** — marcar um contrato como fechado, impedindo novas movimentações; um contrato encerrado não participa do recálculo em lote.

## Parte A — Serviço de recálculo de saldo

### Serviço

`PurchaseContractsRecalculateBalanceService` (novo, `SiagroB1.Application/Services/PurchaseContracts/`), injeta `AppDbContext` (mesmo padrão de `PurchaseContractsTotalsService`/`PurchaseContractsApprovalService`).

- `Task<PurchaseContractRecalcResultDto> ExecuteAsync(Guid key)` — recalcula um contrato.
- `Task<PurchaseContractRecalcAllResultDto> ExecuteAllAsync()` — recalcula todos, **exceto** os `Finished`.

**Lógica do recálculo (por contrato):**
1. Carregar o contrato (rastreado).
2. `novoAlocado = Σ(a.Volume)` **com sinal** das `PurchaseContractsAllocations` do contrato (query no banco). Mesma fórmula do backfill/runtime.
3. Guardar `previousAllocated = contract.AllocatedVolume` e `previousAvaiable = contract.AvaiableVolume`.
4. `contract.AllocatedVolume = novoAlocado`.
5. `Changed = previousAllocated != novoAlocado`.
6. `SaveChangesAsync` (uma vez no single; uma vez ao final no all).

**Contrato `Finished`:**
- `ExecuteAsync(key)` sobre um contrato `Finished` **lança** `ApplicationException("Contrato encerrado não participa do recálculo de saldo.")` (decisão do usuário: erro com mensagem clara).
- `ExecuteAllAsync()` filtra `Where(c => c.Status != ContractStatus.Finished)`.

**Não encontrado:** `ExecuteAsync(key)` de contrato inexistente lança `NotFoundException`.

### DTOs (`SiagroB1.Domain/Dtos/`)

```csharp
public class PurchaseContractRecalcResultDto
{
    public Guid Key { get; set; }
    public string? Code { get; set; }
    public decimal PreviousAllocatedVolume { get; set; }
    public decimal NewAllocatedVolume { get; set; }
    public decimal PreviousAvaiableVolume { get; set; }
    public decimal NewAvaiableVolume { get; set; }
    public bool Changed { get; set; }
}

public class PurchaseContractRecalcAllResultDto
{
    public int Scanned { get; set; }                                  // contratos avaliados (exclui Finished)
    public int Changed { get; set; }                                  // quantos divergiam
    public ICollection<PurchaseContractRecalcResultDto> Changes { get; set; } = []; // só os que mudaram
}
```

### Endpoints OData

- `PurchaseContractsRecalculateBalance` (Action) — `Parameter<Guid>("Key")`, `Returns<PurchaseContractRecalcResultDto>()`.
- `PurchaseContractsRecalculateAllBalances` (Action) — sem parâmetro, `Returns<PurchaseContractRecalcAllResultDto>()`.

Controllers em `SiagroB1.Web/Actions/PurchaseContracts/`, `ODataController`, `[HttpPost("odata/<Nome>")]`, tratando `NotFoundException` → 404 e `ApplicationException` → 400 (padrão dos controllers existentes).

## Parte B — Encerramento de contrato

### Status

Reusar `ContractStatus.Finished` (valor 2, já existente; hoje só lido em guards de `WithdrawApproval`, nunca atribuído). Sem novo enum, **sem migration**.

### Serviços

Espelham `PurchaseContractsApprovalService` (injeta `AppDbContext`; carrega com guard de status; seta; salva). Auditoria via `UpdatedBy`/`UpdatedAt` (sem campo novo).

- `PurchaseContractsCloseService.ExecuteAsync(Guid key, string userName)`:
  - Carrega contrato com `Status == ContractStatus.Approved`; se não achar, `NotFoundException("Contrato não encontrado ou não está aprovado.")`.
  - Sem pré-condição de saldo (decisão do usuário: encerra com qualquer saldo).
  - `Status = Finished; UpdatedAt = now; UpdatedBy = userName;` salva.
- `PurchaseContractsReopenService.ExecuteAsync(Guid key, string userName)`:
  - Carrega contrato com `Status == ContractStatus.Finished`; se não achar, `NotFoundException("Contrato não encontrado ou não está encerrado.")`.
  - `Status = Approved; UpdatedAt = now; UpdatedBy = userName;` salva.

### Guards de movimentação (contrato `Finished` bloqueia)

- **Novas alocações** — `PurchaseContractsAllocationCreateService.ExecuteAsync` (ambos os overloads): após carregar/receber o contrato, se `Status == ContractStatus.Finished` → `ApplicationException("Contrato encerrado: não é possível alocar.")`. Colocar antes das demais validações de volume.
- **Liberações de embarque** — `ShipmentReleasesCreateService.ExecuteAsync`: após carregar `purchaseContract`, se `Status == ContractStatus.Finished` → `ApplicationException("Contrato encerrado: não é possível criar liberação de embarque.")`.
- **Edição do contrato** — **já coberto**: `PurchaseContractsUpdateService` só permite editar se `Status == Draft`. Nenhuma mudança necessária; documentado aqui para rastreabilidade.
- **Aprovação de liberação de embarque** — já coberto: `ShipmentReleasesApprovationService` exige `Status == Approved`.

### Endpoints OData

- `PurchaseContractsClose` (Action) — `Parameter<Guid>("Key")`, `Returns<IActionResult>()`.
- `PurchaseContractsReopen` (Action) — `Parameter<Guid>("Key")`, `Returns<IActionResult>()`.

Controllers em `Actions/PurchaseContracts/`, extraindo `userName` de `User.Identity?.Name`, mesmo tratamento de exceção.

## Registro (comum)

- Todos os serviços novos: `services.AddScoped<...>()` em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (`AddApplicationServices()`).
- Todas as actions: registrar no `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`.

## Testes (TDD, `SiagroB1.Application.Tests`)

Recálculo:
1. `ExecuteAsync` corrige `AllocatedVolume` divergente e retorna antes/depois com `Changed == true`.
2. `ExecuteAsync` sobre saldo já correto retorna `Changed == false`.
3. `ExecuteAsync` sobre contrato `Finished` lança `ApplicationException`.
4. `ExecuteAsync` sobre contrato inexistente lança `NotFoundException`.
5. `ExecuteAllAsync` recalcula os não-encerrados, exclui `Finished`, e `Changes` lista só os divergentes com `Scanned`/`Changed` corretos.

Encerramento:
6. `CloseService` move `Approved → Finished` e grava `UpdatedBy`.
7. `CloseService` sobre contrato não-`Approved` lança `NotFoundException`.
8. `ReopenService` move `Finished → Approved`.
9. `ReopenService` sobre contrato não-`Finished` lança `NotFoundException`.
10. `PurchaseContractsAllocationCreateService` rejeita alocação quando o contrato está `Finished`.
11. `ShipmentReleasesCreateService` rejeita quando o contrato está `Finished`.

## Fora de escopo

- `SalesContract` (padrão análogo; não solicitado).
- Reconciliar o lado do romaneio (`StorageTransaction.AvaiableVolumeToAllocate`) — este recálculo é só do contrato.
- Bloquear fixações de preço em contrato encerrado (usuário não selecionou).
- Migração/backfill (nenhuma mudança de schema).

## Riscos e mitigações

| Risco | Mitigação |
|---|---|
| Recálculo concorrente com criação de alocação no mesmo contrato | `RowVersion` do contrato → `DbUpdateConcurrencyException` (degrada para erro, não corrompe) |
| Guard de `Finished` esquecido em algum caminho de movimentação futuro | Testes cobrem alocação e shipment release; documentar a regra na memória do projeto |
| Reabrir contrato encerrado com saldo "escrito off" reintroduz divergência | Reabertura é explícita e auditada (`UpdatedBy`); ao reabrir, o recálculo volta a considerá-lo |
