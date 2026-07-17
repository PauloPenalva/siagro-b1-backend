# Design: `PurchaseContract` saldo alocável persistido (`AllocatedVolume`)

**Data:** 2026-07-17
**Repositório:** `siagro-b1-backend`
**Autor:** Paulo Penalva (com Claude Code)

## Contexto e problema

`PurchaseContract.AvaiableVolume` é uma property `[NotMapped]` computada que subtrai de `TotalVolume` a soma das alocações "vivas", filtrando por `Allocations.StorageTransaction.TransactionType`/`TransactionStatus`. Como o filtro depende da navegação de 2 níveis `Allocations → StorageTransaction` e o lazy loading está **desligado** (`SiagroB1.Web/Program.cs` não configura `UseLazyLoadingProxies`), qualquer caminho de leitura que não faça `.ThenInclude(a => a.StorageTransaction)` obtém `StorageTransaction == null`, o filtro descarta todas as alocações e o saldo retorna silenciosamente `TotalVolume` (saldo errado, sem erro).

Isso causou uma regressão real (jul/2026) em `PurchaseContractsTotalsService` — a tela de saldo do contrato mostrava sempre `TotalVolume`. Corrigido pontualmente adicionando `ThenInclude`, mas a fragilidade permanece: **toda** leitura precisa lembrar do include aninhado, e a coleção GET OData (`[EnableQuery]`) pode descartar o `Include` sob `$select`/`$expand` (o EF ignora `Include` quando o OData projeta), reintroduzindo o bug.

## Descoberta que simplifica o problema

`StorageTransactionsCancelService` (linhas 30-37) **bloqueia** o cancelamento de um romaneio que possua alocações ("remove them before canceling"). Combinando as invariantes do sistema:

- Uma alocação **nunca coexiste** com um romaneio `Cancelled` (para cancelar o romaneio é obrigatório apagar a alocação antes, via `PurchaseContractsAllocationDeleteService`).
- Alocações de complemento (`PurchaseQtyComplement`/`PurchasePriceComplement`) são gravadas com `Volume = 0` (switch em `PurchaseContractsAllocationCreateService`), então incluí-las ou excluí-las é indiferente para a soma.
- Um romaneio alocado está sempre `Confirmed` ou `Invoiced` — ambos contados pelo filtro (o create rejeita romaneio `Pending`).

Logo, o filtro sobre `StorageTransaction` é redundante:

```
AvaiableVolume ≡ TotalVolume − Σ(Allocations.Volume)   // com sinal
```

sem qualquer dependência de navegação. Isso também significa que a soma só muda quando uma **alocação** é criada ou removida — nenhum outro evento (confirm/cancel/invoice) altera o resultado.

## Objetivo

Tornar o saldo do contrato robusto a qualquer caminho de leitura (incluindo `$select` do OData), eliminando a dependência de navegação em runtime, de forma simétrica ao que já foi feito no lado do romaneio (`StorageTransaction.AvaiableVolumeToAllocate` — ver `docs`/memória "derive-and-persist").

## Decisão de design

Persistir o total alocado como coluna mapeada e derivar o saldo por aritmética de escalares.

### 1. Modelo de dados

Em `PurchaseContract`:

```csharp
[Column(TypeName = "DECIMAL(18,3)")]
public decimal AllocatedVolume { get; set; }   // Σ(Allocations.Volume), com sinal

[Timestamp]
public byte[]? RowVersion { get; set; }         // concorrência otimista
```

`AvaiableVolume` deixa de tocar navegação:

```csharp
[NotMapped]
public decimal AvaiableVolume =>
    decimal.Round(TotalVolume - AllocatedVolume, 2, MidpointRounding.ToEven);
```

**Por que persistir `AllocatedVolume` e não `AvaiableVolume`:** `TotalVolume` muda quando o contrato é editado. Persistindo o *alocado* (independente de `TotalVolume`), a edição de `TotalVolume` reflete automaticamente no saldo sem recalcular alocação — o recalc fica restrito a onde a alocação muda.

**Sinal:** o contrato usa `Volume` **com sinal** (devolução `PurchaseReturn` é negativa e devolve saldo), diferente do lado do romaneio que usa `decimal.Abs`. Preservar o sinal.

### 2. Recálculo (fonte única de verdade)

Somente em `PurchaseContractsAllocationCreateService` e `PurchaseContractsAllocationDeleteService`, derivando do `SUM` do banco (mesmo padrão já aplicado ao romaneio), nunca por `+=`/`-=`:

- **Create** (`purchaseContractKey` é sempre um contrato persistido): após adicionar a alocação, `contract.AllocatedVolume = Σ(DB das alocações do contrato) + novoVolume` (com sinal). Buscar o contrato rastreado para atualizar.
- **Delete:** carregar o contrato via `alloc.PurchaseContractKey` e `contract.AllocatedVolume = Σ(DB das alocações restantes, excluindo a removida)` (com sinal).

Ambos respeitam o `CommitMode` existente (deferred participa da transação do chamador).

Nenhum hook em confirm/cancel/invoice de romaneio — justificado pela invariante do cancel-guard acima.

### 3. Concorrência

`RowVersion` (`[Timestamp]`, coluna SQL `rowversion`) em `PurchaseContract`, simétrico ao romaneio. Protege `AllocatedVolume` contra duas alocações concorrentes ao mesmo contrato (dois usuários alocando romaneios diferentes ao mesmo contrato ⇒ lost update sem o token).

**Blast radius (aceito):** todo `UPDATE` em `PURCHASE_CONTRACTS` passa a carregar `WHERE RowVersion = @orig` e pode lançar `DbUpdateConcurrencyException`. Os serviços que hoje fazem `catch(Exception) => ApplicationException` degradam para mensagem de erro em vez de corromper dados — comportamento desejado.

### 4. Migration + backfill

Migration real (coluna nova, ausente no banco):

- `AddColumn AllocatedVolume DECIMAL(18,3) NOT NULL DEFAULT 0` em `PURCHASE_CONTRACTS`.
- `AddColumn RowVersion rowversion NULL`.
- **Backfill** via SQL cru no `Up()` (após o AddColumn):

  ```sql
  UPDATE PC
  SET PC.AllocatedVolume = ISNULL((
      SELECT SUM(a.Volume)
      FROM PURCHASE_CONTRACTS_ALLOCATIONS a
      WHERE a.PurchaseContractKey = PC.[Key]
  ), 0)
  FROM PURCHASE_CONTRACTS PC;
  ```

Aplicada pelo fluxo `Environment == "Migration"` de `SiagroB1.Web`, não por `dotnet ef database update` (ver memória "migrations-hand-edited-baseline-sync").

### 5. Impacto nos caminhos de leitura

`AvaiableVolume` passa a funcionar em **qualquer** materialização de `PurchaseContract` (é aritmética de dois escalares mapeados), inclusive sob `$select` do OData. Os `ThenInclude(StorageTransaction)` adicionados no fix anterior tornam-se desnecessários para `AvaiableVolume`, mas permanecem inofensivos; a limpeza fica fora de escopo (nota abaixo).

## Testes (TDD)

1. `PurchaseContract.AvaiableVolume` correto com **nenhuma** alocação carregada na entidade (prova que a dependência de navegação sumiu) — teste de robustez central.
2. `PurchaseContractsTotalsService.GetTotals` retorna saldo correto **sem** `Include(Allocations)` (simula o `$select`/projeção do OData) — regressão do risco residual.
3. `PurchaseContractsAllocationCreateService`: após criar, `AllocatedVolume`/`AvaiableVolume` refletem `Σ` (incluindo segunda alocação e auto-cura de valor corrompido).
4. `PurchaseContractsAllocationDeleteService`: após remover, recalcula a partir do `Σ` restante, com devolução (`Volume` negativo) somando com sinal.
5. Guarda de mapeamento: `PurchaseContract.RowVersion` é token de concorrência (`IsConcurrencyToken`, `ValueGenerated.OnAddOrUpdate`).

Observação sobre o provider InMemory: transações e `rowversion` não são enforçados, então a corrida real não é exercitável nos testes — o guarda #5 valida apenas o mapeamento (mesmo tratamento do lado do romaneio).

## Fora de escopo

- `SalesContract` (padrão análogo; não solicitado).
- Reverter/simplificar os `ThenInclude(StorageTransaction)` adicionados no fix anterior (`PurchaseContractsTotalsService`, `PurchaseContractsCancelService`, `SiagroB1.Reports/Controllers/PurchaseContractsController`). O guard de `PurchaseContractsCancelService` ainda lê `StorageTransaction?.TransactionStatus`, então ali o include permanece necessário; nos demais é limpeza opcional futura.

## Riscos e mitigações

| Risco | Mitigação |
|---|---|
| Alocação mutada fora dos 2 serviços deixaria `AllocatedVolume` stale | Grep confirma que só os 2 serviços mutam `PurchaseContractsAllocations`; correção é centralizada |
| Backfill incorreto para contratos existentes | SQL de backfill derivado da mesma fórmula; teste manual de spot-check pós-deploy |
| `DbUpdateConcurrencyException` em fluxos legítimos de update de contrato | Degrada para erro (não corrompe); monitorar após deploy |
| Invariante do cancel-guard quebrar no futuro (permitir cancelar romaneio com alocação) | Comentário no `RecalculateAvailableVolume`/serviços apontando a dependência; se a regra mudar, adicionar hook de recalc no cancel |
