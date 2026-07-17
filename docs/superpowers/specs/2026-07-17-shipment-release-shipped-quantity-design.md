# Design: `ShipmentRelease` saldo disponível persistido (`ShippedQuantity`)

**Data:** 2026-07-17
**Repositório:** `siagro-b1-backend`
**Autor:** Paulo Penalva (com Claude Code)

## Contexto e problemas

`ShipmentRelease.AvailableQuantity` (saldo disponível para romanear/embarcar) hoje é uma property `[NotMapped]` computada:

```
AvailableQuantity = (Status ≠ Cancelled)
    ? ReleasedQuantity − Σ(GrossWeight de SalesShipment/SalesShipmentReturn não canceladas)
    : 0
```

Auditoria (jul/2026) encontrou:

- **A — Sinal da devolução (bug de correção):** os cálculos subtraem `SalesShipmentReturn` com o mesmo sinal de `SalesShipment`. Pela convenção canônica de estoque do próprio codebase (`StorageTransactionsConfirmedService.GetWarehouseBalanceAsync`, linhas 237-240), `SalesShipmentReturn` é **entrada** (`+`, como `Purchase`) e `SalesShipment` é **saída** (`−`). Logo, uma devolução deveria **restaurar** o disponível do release, não consumi-lo. Devoluções ligam-se a releases (`StorageTransactionCopyFactory` copia `ShipmentReleaseKey`), então é real.
- **B — `GrossWeight` vs `NetWeight`:** a entidade usa `GrossWeight`; `ShipmentReleasesBalanceService` e `ShipmentReleasesPurchaseContractsService` usam `NetWeight` para o mesmo "usado". Coincidem só porque o confirm faz `NetWeight = GrossWeight` para vendas (linhas 96/121). Frágil.
- **C — Dependência de navegação + OData:** `AvailableQuantity` é `[NotMapped]` sobre a navegação `Transactions`; se não carregada → retorna `ReleasedQuantity` cheio, silenciosamente. Exposta no EDM (`ODataConfigurations:55`), lida na lista de releases; sob `$select`/projeção do OData o `Include` pode ser descartado. `GetByIdAsync` não inclui `Transactions`.
- **D — Triplicação:** o cálculo de "usado" existe em 3 lugares com diferenças (Gross vs Net) e todos com o problema de sinal do A.

## Decisão

Persistir a quantidade usada (`ShippedQuantity`) como coluna, derivar `AvailableQuantity` por aritmética escalar, e centralizar o recálculo — simétrico ao `PurchaseContract.AllocatedVolume` (ver `2026-07-17-contract-available-volume-design.md`). Padronizar em **NetWeight** e corrigir o sinal.

### 1. Modelo de dados (`ShipmentRelease`)

```csharp
[Column(TypeName = "DECIMAL(18,3)")]
public decimal ShippedQuantity { get; set; }   // usado = Σ(SalesShipment.Net) − Σ(SalesShipmentReturn.Net)

[Timestamp]
public byte[]? RowVersion { get; set; }         // concorrência otimista
```

`AvailableQuantity` deixa de tocar navegação:

```csharp
[NotMapped]
public decimal AvailableQuantity =>
    Status != ReleaseStatus.Cancelled
        ? decimal.Round(ReleasedQuantity - ShippedQuantity, 3, MidpointRounding.ToEven)
        : decimal.Zero;
```

**Convenção (fix A + B):** `ShippedQuantity = Σ(SalesShipment.NetWeight) − Σ(SalesShipmentReturn.NetWeight)`, sobre transações com `TransactionStatus != Cancelled` (preserva contagem de `Pending`, decisão do usuário) e `ShipmentReleaseKey == release.Key`. `AvailableQuantity = Released − ShippedQuantity` restaura o disponível quando há devolução.

`HasStorageTransactions` permanece como está (usa navegação, mas só em `ShipmentReleasesCancelationService`, que dá `Include(Transactions)`) — **fora de escopo**, apenas anotado.

### 2. Recálculo (fonte única de verdade)

Serviço `ShipmentReleasesRecalculateShippedService`:

```csharp
public async Task RecalculateAsync(Guid shipmentReleaseKey)
```

Deriva do `SUM` no banco (nunca `+=`/`-=`):

```
shipped = Σ sobre STORAGE_TRANSACTIONS onde ShipmentReleaseKey == key
          e TransactionStatus != Cancelled
          e TransactionType ∈ {SalesShipment, SalesShipmentReturn}
          de (SalesShipment ? +NetWeight : -NetWeight)
release.ShippedQuantity = shipped
```

Carrega o release (rastreado), seta `ShippedQuantity`, salva. Idempotente.

### 3. Pontos de disparo (hooks)

Uma transação `SalesShipment`/`SalesShipmentReturn` ligada a um release muda o `ShippedQuantity` ao ser criada, confirmada, cancelada ou estornada. Em cada serviço de ciclo de vida, **após** a mudança da transação ser persistida (mesma unidade de trabalho), se `type ∈ {SalesShipment, SalesShipmentReturn}` e `ShipmentReleaseKey` tem valor, chamar `RecalculateAsync(ShipmentReleaseKey)`:

- `StorageTransactionsConfirmedService` (confirmar venda/devolução)
- `StorageTransactionsCancelService` (cancelar)
- `StorageTransactionsReverseService` (estornar)
- `StorageTransactionsCreateService` (criar)

**Timing:** o recálculo lê o `SUM` do banco, então roda depois do `SaveChanges` da transação, dentro da mesma transação de banco (as leituras enxergam as escritas já flushadas na mesma conexão) — atômico. Para fluxos `CommitMode.Deferred` (ex.: `ShippingTransactionsCreateService`), as vendas criadas não são ligadas a release (fluxo de faturamento), então não disparam recálculo; documentado nos riscos.

### 4. Unificação (D)

`ShipmentReleasesBalanceService` e `ShipmentReleasesPurchaseContractsService` passam a ler a coluna em vez de re-agregar transações:

```
UsedQuantity = g.Sum(sr => sr.ShippedQuantity)
AvailableQuantity = Σ ReleasedQuantity − Σ ShippedQuantity
```

Uma fonte só; some a divergência Gross/Net e o problema de sinal dos dois services.

### 5. Migration + backfill + concorrência

Migration real (colunas novas):

- `AddColumn ShippedQuantity DECIMAL(18,3) NOT NULL DEFAULT 0` em `SHIPMENT_RELEASES`.
- `AddColumn RowVersion rowversion NULL`.
- **Backfill** via SQL cru no `Up()` (valores de enum como int: `SalesShipment = 7`, `SalesShipmentReturn = 12`, `Cancelled = 2`):

  ```sql
  UPDATE SR
  SET SR.ShippedQuantity = ISNULL((
      SELECT SUM(CASE
                   WHEN t.TransactionType = 7  THEN t.NetWeight
                   WHEN t.TransactionType = 12 THEN -t.NetWeight
                   ELSE 0 END)
      FROM STORAGE_TRANSACTIONS t
      WHERE t.ShipmentReleaseKey = SR.[Key]
        AND t.TransactionStatus <> 2
        AND t.TransactionType IN (7, 12)
  ), 0)
  FROM SHIPMENT_RELEASES SR;
  ```

Aplicada pelo fluxo `Environment == "Migration"` de `SiagroB1.Web`.

`RowVersion` dá concorrência otimista (blast radius: todo `UPDATE` em `SHIPMENT_RELEASES` passa a carregar `WHERE RowVersion=@orig`; degrada para `DbUpdateConcurrencyException` → erro, não corrompe).

## Testes (TDD, `SiagroB1.Application.Tests`)

1. `RecalculateAsync` calcula `ShippedQuantity = Σ shipment − Σ return` (NetWeight), ignora Cancelled, considera Pending.
2. Devolução restaura o disponível: release Released=100, shipment 80, return 30 → ShippedQuantity 50, AvailableQuantity 50.
3. `AvailableQuantity` derivado da coluna **sem navegação carregada** (robustez OData); Cancelled → 0.
4. Cada hook (confirm/cancel/reverse/create) dispara o recálculo do release ligado.
5. `ShipmentReleasesBalanceService`/`PurchaseContractsService` calculam via `Sum(ShippedQuantity)` sem agregar transações.
6. Guarda de mapeamento do `RowVersion` (token de concorrência).

Provider InMemory não enforça `rowversion`; guarda #6 valida só o mapeamento (mesmo tratamento do contrato/romaneio).

## Fora de escopo

- `HasStorageTransactions` (mantém navegação; caminho único já dá `Include`).
- Mudar o filtro de status (`Pending` continua contando).
- Lado de vendas/faturamento além do vínculo com release.

## Riscos e mitigações

| Risco | Mitigação |
|---|---|
| Hook esquecido em algum caminho que mude transação venda/devolução ligada a release | Cobrir os 4 serviços de ciclo de vida + teste por hook; expor recálculo manual como rede de segurança (futuro, análogo ao do contrato) |
| Fluxo `Deferred` (faturamento) não dispara recálculo | Vendas do faturamento não são ligadas a release; backfill cobre estado inicial |
| Recálculo lê `SUM` antes do flush da transação | Recalc roda após o `SaveChanges` da transação, dentro da mesma transação de banco |
| Backfill herda inconsistência atual dos dados | Fórmula idêntica ao runtime; spot-check pós-deploy comparando com a tela antiga |
| Concorrência de recálculo com outra edição do release | `RowVersion` → `DbUpdateConcurrencyException` (degrada para erro) |
