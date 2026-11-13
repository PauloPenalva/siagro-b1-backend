# GAC-1214 (rodada 3) — devolução ao armazém devolve saldo ao contrato de compra

Data: 2026-11-12 · Aprovado pelo usuário em chat (12/11/2026)

## Por quê

Quando uma carga é recusada/devolvida para um armazém, o sistema cria um romaneio de devolução
(`SalesShipmentReturn`, tipo 12) e uma liberação de devolução (`ReleaseOrigin.SalesReturn`) no
contrato de compra de origem. Até aqui a regra era "o contrato continua baixado: o grão foi
comprado e só mudou de lugar"; a devolução aparecia na aba Entregas apenas como linha de exibição,
fora do saldo, e a saída posterior por essa liberação (Entrada em Armazenagem ou Expedição de Grãos)
não aparecia.

A Yokotobi (produção) lê a aba Entregas como extrato: Compra − Devolução = 0, logo o saldo do
contrato deveria voltar, e a nova entrega deveria baixar de novo e aparecer. O usuário decidiu
adotar essa leitura (reverte a decisão de 03/11/2026).

## Regra

**Invariante:** para todo contrato de compra, o volume devolvido ao saldo é igual ao saldo em
aberto das suas liberações de devolução (`Origin = SalesReturn`) vivas.

Implementada no ledger `PURCHASE_CONTRACTS_ALLOCATIONS` (fonte única do saldo; `AllocatedVolume`
é Σ Volume com sinal):

1. **Devolução** — o romaneio que gerou a liberação (`ShipmentRelease.GeneratedByStorageTransactionKey`,
   tipo 12) recebe uma alocação de `−ReleasedQuantity` no contrato da liberação. Um romaneio 12 que
   gera liberações em N contratos recebe N alocações (uma por contrato).
2. **Consumo da liberação** — todo romaneio que consome a liberação de devolução recebe alocação
   no contrato dela com o MESMO sinal de `ShipmentReleasesRecalculateShippedService.ShippedSign`:
   `SalesShipment(7)` e `WarehouseLoss(13)` → `+NetWeight`; `SalesShipmentReturn(12)` com
   `ShipmentReleaseKey` desta liberação → `−NetWeight`. Romaneio cancelado não conta
   (mesmo filtro do `CalculateShippedAsync`: `Pending` conta).
3. **Cancelamento/estorno** de qualquer um desses romaneios, ou cancelamento/remoção da liberação
   cujo romaneio gerador foi cancelado, remove as alocações correspondentes.
4. Liberação de devolução **cancelada** não devolve saldo — ver "Liberação cancelada" abaixo.

### Liberação que deixou de ser porta de saída (cancelada ou finalizada)

"Viva" = `Status` diferente de `Cancelled` e de `Completed`. Uma liberação que não é mais porta de
saída não devolve o saldo em aberto — senão o contrato ficaria com saldo que nunca seria
reconsumido. A parte já consumida continua valendo. Fórmula única da alocação do gerador:

```
gerador vivo:       −(ShippedQuantity + saldo em aberto se a liberação é viva)
                    = −ReleasedQuantity (viva)  |  −ShippedQuantity (cancelada/finalizada)
gerador cancelado:  nenhuma alocação
```

Exemplo: compra +30, devolução −30, reembarque +10, liberação cancelada → gerador passa a −10 e o
ledger fecha em 30 (o que foi comprado). Reabrir (`ShipmentReleasesReopenService`) volta a −30.
Alocação de volume zero não é gravada. (Decisão do construtor.)

### Saldo devolvido e alocação manual

O saldo devolvido NÃO fica reservado: é saldo do contrato como qualquer outro. Se alguém alocar
manualmente outra compra nele, a reentrega posterior leva o contrato a saldo negativo, o que o
encerramento já sabe tratar (guard de saldo negativo). Não nasce liberação Standard nova por causa
disso (`TotalAvailableToRelease` ignora a origem SalesReturn). (Decisão do construtor.)

### Fora de escopo

- `Transshipment (3)`, `WarehouseGain (4)` e `OwnershipTransfer (1)`: sem mudança, continuam sem
  mexer no contrato.
- Liberação `Standard`: sem mudança.

## Como

### Serviço de sincronização (peça central)

Novo `PurchaseContractsReturnReleaseAllocationSyncService` em
`SiagroB1.Application/Services/PurchaseContracts/`, registrado em `AddApplicationServices()`.

`SyncAsync(Guid shipmentReleaseKey)` — idempotente, derivado do estado:

- Carrega a liberação. Se não existe ou `Origin != SalesReturn`, não faz nada.
- Calcula o conjunto ESPERADO de alocações `(StorageTransactionKey, Volume)` no contrato da
  liberação, conforme a Regra acima.
- Compara com as alocações EXISTENTES do contrato cujo romaneio é o gerador da liberação ou tem
  `ShipmentReleaseKey == liberação` (tipos 7/12/13). Insere as que faltam, corrige volume
  divergente, remove as que sobram.
  - ⚠️ O gerador pode ter gerado liberações em mais de um contrato: a alocação do gerador é
    identificada por (romaneio gerador, contrato da liberação). Duas liberações vivas do MESMO
    gerador no MESMO contrato não ocorrem (`ShipmentReleasesFromReturnService` agrupa por contrato);
    se ocorrerem, somar `ReleasedQuantity`.
- Recalcula `PurchaseContract.AllocatedVolume = Σ Volume` do contrato (mesma fórmula de
  `PurchaseContractsRecalculateBalanceService.CalculateAllocatedAsync`), considerando as mudanças
  pendentes no contexto.
- Não aplica teto de saldo do contrato nem bloqueio de contrato `Finished`: o grão precisa de porta
  de saída (mesmo espírito de `ExecuteReversalAsync`).
- Não mexe em `StorageTransaction.AvaiableVolumeToAllocate` dos romaneios 7/12/13 (o campo é da
  alocação manual de romaneios de compra).
- Aceita `CommitMode` (Auto salva; Deferred deixa para o chamador).

Uma sobrecarga `SyncByGeneratorAsync(Guid storageTransactionKey)` sincroniza todas as liberações
`SalesReturn` geradas por um romaneio (para criação e estorno da devolução).

### Pontos de chamada

- `ShipmentReleasesRecalculateShippedService.RecalculateAsync` — é o gargalo por onde passa todo
  evento que muda o consumo de uma liberação (embarque, entrada em armazenagem, cancelamento,
  estorno, troca de liberação, conferência). Depois de atualizar `ShippedQuantity`, chama o sync
  quando `Origin == SalesReturn`. O planejador deve conferir os ~18 chamadores e qualquer caminho que
  altere `ShippedQuantity` de liberação de devolução SEM passar por aqui, e cobrir o que faltar.
- Criação das liberações de devolução: `ShipmentLoadRefusalEffectsService.EmitReturnReleasesAsync`
  e `SalesInvoicesReturnService` (após o `SaveChanges` que grava as liberações).
- Mudam estado/consumo SEM passar por `RecalculateAsync` e precisam chamar o sync:
  `ShipmentReleasesCancelationService` (linha ~70), `ShipmentReleasesCloseService`,
  `ShipmentReleasesReopenService`, `SalesInvoicesReverseConfirmService` (~435 e ~480, cancela
  gerador e liberação à mão) e o cancelamento/estorno da recusa de carga.
- Liberação de devolução nunca é apagada fisicamente (nasce `Actived`; `ShipmentReleasesDeleteService`
  exige `Pending`) e `ReleasedQuantity` é imutável.
- `StorageTransactionsCancelService` e `StorageTransactionsReverseService` (~121) recusam romaneio
  "com alocação" ANTES de mudar o status: a trava tem de ignorar a alocação derivada (romaneio
  7/12/13 de liberação de devolução); o sync a remove depois.
- `StorageEntryTransactionsCancelService` (~46-68) apaga alocações via
  `PurchaseContractsAllocationDeleteService` (desfaz títulos firmes, recalcula
  `AvaiableVolumeToAllocate`, bloqueia contrato `Finished`): desviar esse trecho quando a entrada é
  sobre liberação sem perna de compra e deixar o sync remover a alocação derivada.
- Atomicidade: onde o `RecalculateAsync` roda depois do `CommitAsync` (ex.:
  `StorageEntryTransactionsCreateService` ~111-117) uma falha de concorrência no `RowVersion` do
  contrato deixaria a saída gravada sem baixar o contrato. Nos fluxos de liberação de devolução,
  trazer a chamada para dentro da transação quando for simples; senão, o "Recalcular Saldo" do
  contrato NÃO corrige (ele só soma o ledger) — então o sync precisa também ser acionável pelo
  recálculo de saldo do contrato (`PurchaseContractsRecalculateBalanceService` sincroniza as
  liberações de devolução do contrato antes de somar).

### Aba Entregas (`PurchaseContractsGetAllocationsByContractService`)

- As linhas passam a vir da consulta principal (ledger). `ReturnsSql` e o laço correspondente saem.
- `StorageTransactionTypeName`: tipo 12 → `Devolução`; tipo 7 e 13 em alocação → `Reentrega`.
- `Origin`: `LoadRefusal` para a devolução (gerador, tipo 12) e origem NOVA `ReturnRedelivery` para
  o consumo (7/13/12 com chave). `ReasonDescription`/`LoadCode`/`DischargeLoadCode` como hoje para a
  devolução (carga recusada via `RefusedFromShipmentLoadKey`); para a reentrega, `LoadCode` é a
  carga do próprio romaneio (`ShipmentLoadKey`) quando houver e `ReasonDescription` cita o romaneio
  de devolução que gerou a liberação.
- `Volume` = `PA.Volume` com sinal (devolução negativa), como o GAC-1212 já faz.
- Status de conciliação fiscal dessas linhas: nulo (não são compra do fornecedor).
- Manter `WITH (NOLOCK)` em todo FROM/JOIN (há teste que confere).

### Frontend (`siagro-b1-frontend`, commit separado)

`webapp/helpers/AllocationOriginHelpers.ts`: o tooltip de `LoadRefusal` hoje diz que "o contrato
continua baixado" — trocar por texto que diga que a devolução devolveu o saldo ao contrato; incluir
a origem `ReturnRedelivery` (legenda "Reentrega de devolução", texto "Reentrega", tooltip dizendo
que a saída pela liberação de devolução baixou o contrato de novo). Conferir a coluna de quantidade
com sinal negativo e os testes do helper.

### Outros leitores do ledger (conferir no plano, corrigir se quebrar)

- `ContractStatementReportService` (extrato): uma linha por alocação — deve rotular 12 como
  devolução e 7/13 como reentrega, sem valor financeiro novo.
- `PurchaseContractLotStatementReportService`, `ContractPositionData`, `MarketPhysicalExposureService`:
  passam a refletir o saldo devolvido; só garantir que não quebram com tipos 7/12/13.
- `PurchaseFiscalReconciliationQuery`/`Service`, `PurchaseDischargesListService`,
  `PurchaseTransactionsChangeSupplierService`, `OwnershipTransfersCancelService`,
  `WarehouseReconciliationsCancelService`, `ShippingTransactionsReverseService`,
  `ShippingTransactionsChangeReleaseService`, `StorageEntryTransactionsCancelService`: conferir que
  não tratam as novas alocações como compra.
- Resumo financeiro do contrato e liberação de pagamento (GAC-1188): conferir de onde tiram o
  volume entregue. Se usam o ledger, o "entregue" cai durante o intervalo em que o grão está no
  armazém — é o comportamento pedido; apenas relatar ao usuário.
- `PurchaseContractsAllocationsController`/alocação manual: as alocações derivadas não podem ser
  apagadas à mão pela tela (bloquear a exclusão com mensagem pt-BR).

### Dados existentes (migration `BackfillSalesReturnReleaseAllocations`)

Migration só de dados no `AppDbContext`, idempotente (não insere se já existir alocação para o par
contrato+romaneio), `SET QUOTED_IDENTIFIER ON`, compatível com SQL Server 2016 (sem `STRING_AGG`):

1. Insere a alocação do gerador pela fórmula única acima (`−ReleasedQuantity` se a liberação é
   viva, `−ShippedQuantity` se cancelada/finalizada; zero não insere), somada por gerador+contrato,
   para liberações `Origin = 2` com gerador tipo 12 não cancelado.
2. Insere `±NetWeight` para romaneios 7/13/12 não cancelados com `ShipmentReleaseKey` em liberação
   `Origin = 2`.
3. Recalcula `PURCHASE_CONTRACTS.AllocatedVolume = Σ Volume` para os contratos tocados (inclusive
   encerrados — o ledger e a coluna têm de concordar).

`Down`: remove as alocações cujo romaneio é 7/12/13 ligado a liberação `Origin = 2` e recalcula.

Resultado esperado em produção Yokotobi (dados de 12/11/2026): 00005330 saldo 115.140 → 147.320;
00005248 0 → 620; 00005073 e 00005280 inalterados (61.440 e 36.520), ganhando as linhas na lista.

## Testes

xUnit em `SiagroB1.Application.Tests` (EF InMemory; lembrar que InMemory esconde LINQ não
traduzível e que o fixup do EF mascara desvínculos):

- sync: devolução cria alocação negativa e o saldo volta; saída 7 baixa de novo; saída parcial;
  perda 13; cancelamento da saída; cancelamento do gerador; liberação cancelada; gerador com
  liberações em dois contratos; idempotência (rodar duas vezes não duplica); origem ≠ SalesReturn
  não faz nada; contrato encerrado não bloqueia.
- `RecalculateAsync` dispara o sync só para `SalesReturn`.
- Entrada em Armazenagem e Expedição sobre liberação de devolução baixam o contrato.
- Recusa de carga e retorno de NF devolvem saldo; seus estornos desfazem.
- SQL da aba Entregas: sem `ReturnsSql`, rótulos novos, `NOLOCK` em tudo.
- Atualizar os testes existentes que afirmam "não aloca / contrato continua baixado".

Verificação final em CÓPIA do banco (o banco local é compartilhado com migrations de outros
branches): aplicar a migration e conferir os quatro contratos acima.

## Deploy

Backend + migration primeiro, depois o frontend (dois repositórios, dois commits). Sem push; merge em `main` é
decisão do usuário.
