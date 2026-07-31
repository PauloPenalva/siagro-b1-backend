# Saldo negativo permitido na venda, bloqueado no encerramento

**Data:** 31/07/2026
**Escopo:** liberação de entrega de venda (`SalesShipmentRelease`) e contrato de venda (`SalesContract`).
Compra e embarque (`ShipmentRelease`) ficam intocados.

## Problema

`ShipmentBillingCreateSalesInvoiceService.EnsureReleaseHasBalanceAsync` recusa qualquer
faturamento cujo `Σ NetWeight` dos romaneios exceda o saldo da liberação de entrega.

Na operação real esse cenário não é uma decisão a ser aprovada: o caminhão já saiu, já pesou
e o peso veio maior que o saldo remanescente da liberação. Recusar o faturamento não desfaz a
entrega física — apenas impede registrá-la. O contorno que o usuário encontra é pior que o
problema: contratos "AJUSTE DE SALDO" com `TotalVolume = 1` absorvendo dezenas de milhões de kg
(quatro deles somam ~85 milhões no Yokotobi, medido em 29/07/2026).

## Decisão

Inverter onde o controle é aplicado:

- **Na entrada, permitir.** O faturamento grava o que aconteceu, mesmo que o saldo fique negativo.
- **Na saída, barrar.** Nenhuma liberação de entrega com saldo negativo pode ser **finalizada**
  nem **cancelada**; o contrato de venda negativo já não pode ser encerrado desde 30/07/2026.

O negativo passa a ser um estado visível e temporário, que obriga a regularização (conciliação
de saldos, cancelamento do documento de saída, aditivo de volume) antes de congelar o registro.

### Limites do escopo (decididos com o usuário)

| Decisão | Escolha |
|---|---|
| Onde o negativo é permitido | Só liberação de entrega de venda e contrato de venda |
| Quais travas caem | **Só a do faturamento**. Criar/aprovar liberação continua exigindo saldo físico positivo |
| Filtros `saldo > 0` das consultas de seleção | **Permanecem** |
| Finalizar / Cancelar no negativo | **Ambos bloqueados** |

O negativo nasce, portanto, de um único caminho: a liberação tinha saldo positivo, foi
selecionada no faturamento, e o `NetWeight` dos romaneios veio maior que esse saldo. Liberar
volume que o contrato não tem continua sendo erro de planejamento — e continua recusado.

## Regras

### 1. Faturamento não olha mais o saldo da liberação

Cai `EnsureReleaseHasBalanceAsync`. Permanecem intactos:

- `ShipmentBillingTransactionGuardService.EnsureCanBillAsync` — romaneio já vinculado a outro
  documento de saída (invariante "1 romaneio = 1 invoice").
- `SalesShipmentReleaseMovementGuardService.EnsureCanBillAsync` — liberação
  Completed/Cancelled/Paused.

Nenhum outro ponto barrava o excesso: `SalesInvoicesCreateService` e
`SalesContractsAllocationCreateService` não têm guard de saldo, e o dialog de
`/shipment-billing` não valida volume no cliente. O contrato de venda fica negativo por
consequência, via ledger `SALES_CONTRACTS_ALLOCATIONS`.

### 2. Finalizar recusa saldo negativo

`SalesShipmentReleasesCloseService` ganha `GuardNegativeBalanceAsync`, espelhando
`SalesContractsCloseService`:

- Decide sobre o saldo **recalculado do ledger** (`CalculateShippedAsync`), nunca sobre o
  `ShippedQuantity` persistido. O agregado é persistido-derivado e drifta; ler o persistido
  barraria por engano uma liberação correta cujo agregado dessincronizou, e o usuário não teria
  como distinguir.
- **Não persiste** o recálculo — finalizar continua sem efeito colateral de saldo. Para
  ressincronizar existe o botão *Recalcular Saldo*, ao lado na tela.
- Saldo **zero e positivo finalizam normalmente**: finalizar é abrir mão do volume liberado e
  não embarcado. Só o negativo (faturado ALÉM do liberado) barra.

### 3. Cancelar separa zero de negativo

A trava atual (`AvailableQuantity <= 0`) já recusava o negativo — o defeito era a mensagem
*"Utilize a ação Finalizar"*, que passa a ser mentira, já que Finalizar também recusa. Os dois
ramos ficam explícitos:

- `saldo == 0` → mantém "sem saldo disponível… Utilize a ação Finalizar".
- `saldo < 0` → mensagem própria de sobre-faturamento, apontando a regularização.

### 4. `ReservedByOpenReleases` precisa de clamp

Consequência direta de permitir liberação negativa, e é um vazamento real de volume.

`PhysicalAvailableToRelease = AvaiableVolume − ReservedByOpenReleases`, e
`ReservedByOpenReleases` soma o `AvailableQuantity` das liberações abertas. Uma liberação
negativa entra na soma com sinal negativo e **aumenta** o saldo físico:

> Contrato de 2.000, faturado 1.300, liberação de 1.000 com 1.300 faturados (saldo −300):
> `AvaiableVolume` = 700, `ReservedByOpenReleases` = −300,
> `PhysicalAvailableToRelease` = **1.000**.

O correto é 700 — o contrato aceitaria liberar 300 kg que não existem. Correção: somar
`Math.Max(0, AvailableQuantity)` por liberação, nos **dois** lugares que precisam ficar em
sincronia — o getter `[NotMapped]` de `SalesContract` e o espelho em SQL de
`SalesContractsGetShipmentReleasesAvailableService.Query()`.

### 5. Contrato de venda — sem código novo

- `SalesContractsCloseService` já barra o saldo negativo recalculado do ledger (30/07/2026).
- `SalesContractsCancelService` já recusa contrato com qualquer nota não cancelada; contrato
  negativo sempre tem nota, então o cancelamento já está fechado.

## Frontend

Na tela de Liberações de Entrega (`salesShipmentReleases/Main`), `canCancel` (`balance > 0`)
já desabilita o Cancelar no negativo. Acrescenta-se `canClose` (`balance >= 0`) ligado ao botão
Finalizar, para o negativo não gastar um round trip só para receber erro. O comentário de
`onRowSelectionChange` deixa de dizer que "sem saldo, a ação correta é Finalizar" — no negativo
nenhuma das duas serve.

Nada muda em `/shipment-billing`: o dialog não valida saldo no cliente e a lista de liberações
continua filtrando `> 0`.

## Riscos aceitos

**Liberação negativa fica presa** — não finaliza nem cancela até ser regularizada. É o
comportamento pedido: o bloqueio é o que força a correção em vez de esconder o erro. Se na
prática sobrarem registros sem caminho de saída, a etapa 2 é estender a tela de conciliação de
saldos às liberações de entrega.

**A liberação negativa some das listas de faturamento** (filtro `> 0` mantido), então não é
possível faturar de novo contra ela — o que é desejável enquanto o saldo não voltar a zero.
