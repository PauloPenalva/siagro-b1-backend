# Conciliação de saldos negativos em contratos de venda

Data: 2026-07-29

## Problema

Contratos de venda legados ficaram com **saldo negativo** (`AvaiableVolume = TotalVolume − AllocatedVolume < 0`): o fluxo antigo faturava direto no contrato, sem passar por liberação de entrega, e o backfill do ledger `SALES_CONTRACTS_ALLOCATIONS` fotografou essa distribuição errada. Corrigir exige mover entregas para o contrato certo — e isso era **impossível**.

### O deadlock

| Guard | Efeito sobre contrato negativo |
|---|---|
| `SalesShipmentReleasesCreateService` (saldo físico) | nunca consegue criar liberação |
| `SalesContractsGetShipmentReleasesAvailableService` (`> 0`) | nem aparece no value help de criar liberação |
| `SalesShipmentReleasesApprovationService` | mesmo bloqueio na aprovação |
| `SalesContractsReallocationCreateService` | destino exige liberação **Actived** com saldo **e** `volume ≤ target.AvaiableVolume` |

Um contrato negativo **pode ser origem** de realocação (a liberação de origem é derivada do ledger e pode ser nula), mas **nunca pode ser destino**. Logo travam:

- **Troca cruzada** — a nota de A pertence ao B e a de B pertence ao A, ambos zerados: cada movimento exigiria que o outro já tivesse acontecido. Ciclo.
- **Destino esgotado** — o contrato correto também está em zero pelo mesmo fluxo legado.

Confirmado no Yokotobi (29/07/2026): para o cliente CAVALCA/SOJA, `SalesShipmentReleasesGetAvailable` retornava `[]` e `SalesContractsGetShipmentReleasesAvailable` retornava `[]` — os 7 contratos candidatos estavam **todos** com saldo exatamente 0. O cliente vinha contornando com contratos de `TotalVolume = 1` chamados "AJUSTE DE SALDO" absorvendo dezenas de milhões de kg.

## Decisão

Modo **CONCILIAÇÃO** na própria action de realocação, selecionado pela **ausência** da liberação de destino (`TargetSalesShipmentReleaseKey = null`):

- dispensa os guards de liberação (`EnsureCanBillAsync`, `Status == Actived`, saldo da liberação);
- **não** aplica `volume > target.AvaiableVolume` — o destino **pode ficar negativo**, que é o que quebra o ciclo;
- em troca, exige **motivo** (`Reason`), gravado nas duas pontas do par;
- nasce com `Origin = Reconciliation` (4), distinguível de `Reallocation` em relatório e filtro.

Guards **compartilhados** que continuam valendo nos dois modos: nota `Confirmed` + `Normal`, contratos diferentes, nenhum `Finished`, mesmo cliente/produto/UoM, `volume > 0`, `volume ≤ saldo alocado da nota na origem`.

### Alternativas descartadas

- **Liberação de ajuste (flag `IsAdjustment`)** — exigiria separar `SalesShipmentReleasesGetAvailable` em duas (senão a liberação-fantasma aparece no dialog de `/shipment-billing` e pode ser faturada) **e ainda assim** precisaria relaxar `volume > target.AvaiableVolume`. É a solução escolhida + máquina extra.
- **Tela de conciliação em lote** (redistribuição validada só no estado final) — melhor UX e resolve ordem/ciclo de uma vez, mas build muito maior. Fica como evolução.

### Controle de acesso

Tela e rota próprias (`salesContractsReconciliation`), liberadas **só para ADMIN** via `ROLE_MENUS`, separadas da realocação operacional do dia a dia (`salesContractsAllocations`).

## Implementação

**Domínio:** `SalesContractAllocationOrigin.Reconciliation = 4`; `SalesContractAllocation.ReconciliationReason VARCHAR(500)` nullable.

**Serviços:** `SalesContractsReallocationCreateService` ganhou `Guid? targetSalesShipmentReleaseKey` + `string? reconciliationReason` e a ramificação de modo. `SalesContractsReallocationDeleteService` passou a aceitar estornar `Reconciliation` (o resto já lidava com liberação nula: `releaseDeltas` filtra `!= null`). Dois serviços novos: `SalesContractsGetReconciliationTargetsService` (candidatos a destino **sem** filtro de saldo e **sem** exigir liberação, derivando cliente/produto/UoM da nota no servidor) e `SalesContractsGetNegativeBalancesService` (diagnóstico).

**Recalcs não mudaram:** `affectedReleaseKeys` já filtrava `SalesShipmentReleaseKey != null`, então a linha de destino sem liberação não entra no recalc de `ShippedQuantity`; `AllocatedVolume` segue derivado-da-soma.

**Migrations:** `AddSalesAllocationReconciliationReason` (AppContext) e `AddSalesContractsReconciliationMenu` (CommonContext, ADMIN).

**Frontend:** tela `salesContracts/reconciliation` (lista de negativos + botão "Recalcular Saldos" + entregas do contrato + dialog com destinos/volume/motivo e aviso explícito do saldo negativo resultante). Ajustes na tela de alocações: origem "Conciliação" no formatter, no Excel e no filtro; estorno passa a aceitar `Reconciliation`; coluna "Motivo".

## Armadilhas encontradas

1. **`CASE A.Origin ... ELSE 'Migração'`** no Dapper de `SalesContractsGetAllocationsByContractService` engoliria o valor 4 novo, rotulando conciliação como "Migração". Corrigido para enumerar 3 e 4 explicitamente.
2. **Parâmetro OData string é anulável**: `TryGetValue` devolve `true` com valor `null`; `Guid.Parse(o.ToString()!)` e `reason.ToString()` estouram. Ler com `is not null` / `?.ToString()`.
3. **`bindContext().invoke()` exige binding deferido** mesmo em função SEM parâmetros: `/SalesContractsGetNegativeBalances()` dá "The binding must be deferred" — tem que ser `(...)`. Só quebra no browser.
4. **A URL que o `invoke()` monta inclui os parênteses** (`SalesContractsGetNegativeBalances()`), que a rota `[HttpGet("odata/SalesContractsGetNegativeBalances")]` não casa → 404 dentro do `$batch`. O controller declara as duas formas.

## Verificação (29/07/2026, Yokotobi/IDX_SIAGRO_DEV)

- Suíte: **637 testes verdes** (13 novos), incluindo o teste de **troca cruzada** A↔B entre contratos zerados — o caso hoje impossível.
- `ts-typecheck` e `eslint` limpos.
- Pelo caminho do usuário (`admin/1234`, profile `yktb`): menu Vendas → Conciliação de Saldos; lista de negativos reais em vermelho; dialog lista os 7 destinos com saldo 0 (o dialog antigo listava vazio); guards de destino/motivo disparam; aviso "ficará com saldo NEGATIVO de -1.000,000"; conciliação executada movendo 1.000 kg de `00000293` → `00000294`; par −/+ visível como "Conciliação" com motivo e contrato relacionado; estorno pela tela de alocações restaurou `AllocatedVolume` aos valores originais (2.499.380 / 180.000) e zerou as linhas.
- Regressão: `/shipment-billing` continua sem liberações-fantasma (conciliação não cria liberação); realocação operacional com liberação inalterada.
