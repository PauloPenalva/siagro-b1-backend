# Contrato de compra na Transferência de Titularidade

**Data:** 2026-08-01
**Status:** aprovado, em implementação

## Problema

A empresa é ao mesmo tempo **comércio** (compra grão) e **prestador de serviço** (armazenagem e
beneficiamento). Um mesmo produtor pode ser cliente do armazém *e* fornecedor: o grão dele está
depositado em lote de terceiro, ele vende para a empresa, e a mercadoria muda de dono sem nenhum
caminhão andar.

Hoje `OwnershipTransfer` move grão entre dois lotes e **não tem efeito comercial nenhum**. O
contrato de compra continua com o saldo cheio depois da mercadoria já ser da empresa.

## Princípio

Dois fatos acontecem no mesmo instante e precisam de dois registros:

1. **Custódia** — o grão sai do lote do produtor e entra no lote próprio. É o que a transferência
   já faz hoje (par `Shipment(1)` / `Receipt(0)`).
2. **Comercial** — a empresa comprou. É uma liberação de embarque contra o contrato, e mais tarde
   um romaneio de compra.

A transferência **emite** o documento comercial; ela não *é* o documento comercial. Mesmo princípio
já aplicado em `StorageEntryTransaction` (entrada em armazenagem própria), que é a referência
estrutural desta feature.

## Decisões

| # | Decisão |
|---|---|
| 1 | "Armazém próprio" = `StorageAddress.OwnershipType == OwnedInOurCustody` no lote de **destino**. |
| 2 | O lote de **origem** não pode ser `OwnedInOurCustody` quando há contrato — senão a empresa "compraria" grão que já é dela. |
| 3 | Backfill: **todos** os lotes existentes viram `ThirdParty`; a classificação dos próprios é manual pela tela. Falha fechada. |
| 4 | Sem amarração por `CardCode`. Só `ItemCode` e `UomCode` precisam bater. |
| 5 | Um contrato por transferência, consumindo **sempre** a `Quantity` inteira. |
| 6 | No confirm nasce uma `ShipmentRelease` **`Actived`, com saldo** (não `Completed`): a mercadoria ainda precisa ser embarcada para faturamento. |
| 7 | A liberação carrega `Origin = OwnershipTransfer`, `OwnershipTransferKey` e `StorageAddressCode` (o lote próprio a drenar). |
| 8 | O confirm **debita o saldo físico** do contrato: cria um `Purchase(8)` sem vínculo com a liberação e o aloca. A liberação continua com saldo a carregar. A Expedição de Grãos **não** realoca essa liberação. |

## Por que a Expedição de Grãos precisa mudar

Os dois eixos de saldo usam conjuntos de tipos **disjuntos**:

- Saldo do **lote**: entra `(0 Receipt, 6 ShipmentReleased)`, sai `(1 Shipment, 7 SalesShipment, 4 TechnicalLoss)`.
- Saldo do **armazém**: entra `(8 Purchase, 12 SalesShipmentReturn)`, sai `(7, 9)`.
- Só `Purchase(8)` é alocável a contrato (`StorageTransaction.IsAllocatable`) — e o `8` **não** entra
  no saldo do lote. Um romaneio só não consegue fazer as duas coisas; daí o par.

E a Expedição é cega a lote: `shippingTransaction/Create.controller.ts` monta o payload com
`WarehouseCode` e nunca com `StorageAddressCode`.

Consequência: se a transferência criar o `Receipt(0)` no lote próprio e deixar a liberação com saldo,
o mesmo grão fica contado em dois lugares, e ao embarcar só a liberação baixa — **o lote próprio nunca
esvazia**. Por isso `ShipmentRelease` passa a carregar o lote e a Expedição passa a propagá-lo para o
`SalesShipment(7)`.

`StorageTransactionType.Transfer = 3` existe mas está morto e não entra em nenhuma fórmula de saldo —
não usar.

## Fluxo

```
Lote terceiro (produtor)                     Lote próprio
        │                                          │
        │  OwnershipTransfer.Confirm               │
        ├── Shipment(1) ──────────────────────────>┤ Receipt(0)   (custódia)
        │                                          │
        │   + Purchase(8) SEM ShipmentReleaseKey   │  (comercial)
        │       -> aloca o contrato
        │       -> saldo físico (AvaiableVolume) CAI
        │                                          │
        │   + ShipmentRelease (Actived, COM saldo) │
        │       StorageAddressCode = lote próprio  │
        │       Origin = OwnershipTransfer         │
        │       ShippedQuantity = 0  -> tudo a carregar
        │                                          │
        │  Expedição de Grãos                      │
        │       NÃO realoca (já debitado no confirm)
        │       SalesShipment(7) ──────────────────┤ drena o lote próprio
```

O `Purchase(8)` fica fora da liberação de propósito: `CalculateShippedAsync` só soma romaneios com
`ShipmentReleaseKey` preenchido, então `ShippedQuantity` continua zerado e a liberação mantém o saldo
a carregar — o grão foi entregue, mas ainda precisa ser embarcado para faturamento.

## Validações no confirm

1. Sem contrato informado → comportamento atual, inalterado.
2. Lote de destino != `OwnedInOurCustody` → rejeita.
3. Lote de origem == `OwnedInOurCustody` → rejeita.
4. Contrato != `Approved` → rejeita (replica a guarda de `ShipmentReleasesApprovationService`, que
   é contornada por a liberação nascer `Actived`).
5. `ItemCode` / `UomCode` divergentes → rejeita. Sem checagem de `CardCode`.
6. `Quantity > TotalAvailableToRelease` → rejeita (eixo de liberação).
7. `Quantity > AvaiableVolume` → rejeita (eixo de alocação — garante que a alocação lá na Expedição
   não vá falhar depois).

Comparações com tolerância de 0,001: os saldos do contrato arredondam para 2 casas, enquanto
`Quantity` e `ReleasedQuantity` são `DECIMAL(18,3)`.

## Invariante "uma transferência ⇄ uma liberação"

Protegida em três camadas:

1. Guarda de status no `Confirm` (uma transferência `Closed` não pode ser confirmada de novo).
2. Índice único filtrado em `SHIPMENT_RELEASES.OwnershipTransferKey`.
3. Guarda inversa: os serviços de liberação (`Cancelation`, `Delete`, `Pause`, `Close`, `Reopen`,
   `Approvation`) rejeitam `Origin == OwnershipTransfer` e mandam o usuário cancelar a transferência.

O cancelamento da transferência bloqueia quando a liberação já tem embarque (`ShippedQuantity > 0`,
calculado, não lido da coluna persistida); caso contrário delega a `ShipmentReleasesCancelationService`.

## Defeitos pré-existentes corrigidos junto

Deixaram de ser aceitáveis a partir do momento em que o confirm passa a consumir contrato:

- `Confirm` sem guarda de status — uma transferência `Closed` podia ser confirmada de novo,
  duplicando movimentação. Com contrato, duplicaria também a liberação.
- `Cancel` sem guarda contra já-`Cancelled`.
- `Update` sem guarda de status e usando `CurrentValues.SetValues`, o que permite a um PATCH
  reescrever `TransferStatus`, `TransferCode` e as colunas de auditoria.
- Nenhuma validação de origem != destino, `Quantity > 0`, ou `ItemCode`/`UoM` entre transferência e lotes.

## Fora de escopo

- Conversão de unidade de medida (não existe tabela de conversão no projeto): a validação é
  igualdade estrita.
- Adaptar a tela de Expedição para liberações sem caminhão (hoje ela coleta motorista, placa e
  pesagens, que não existem numa transferência).
- Bloquear a reclassificação de `OwnershipType` em lotes que já têm movimento.
