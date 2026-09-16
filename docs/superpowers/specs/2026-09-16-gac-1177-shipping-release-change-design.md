---
status: aprovado 2026-09-16 (v2 — substitui a v1 de regravação das pernas)
chamado: GAC-1177
---

# Design — Troca de liberação da Expedição dentro da carga, por romaneios de compensação

## Contexto

Depois que a carga é faturada, o contrato de compra baixado numa Expedição de Grãos às vezes precisa mudar:
o armazém informa depois qual contrato baixou, o produtor diz que a baixa foi do lote da Yokotobi (ou o
contrário), ou o financeiro (Mara) pede que outro contrato seja baixado primeiro. Hoje o único caminho é
desvincular e estornar a Expedição, bloqueado quando há documento de saída — exige estornar as notas.

A v1 deste design regravava as pernas no lugar. **Decisão do usuário (16/09/2026): a troca deve ser
explícita** — um romaneio credita o saldo na origem e outro debita no destino, para terceiros e para a
Yokotobi, e tudo aparece na carga.

## Decisões do usuário (16/09/2026)

1. Trocar a **liberação** de uma Expedição por outra, de qualquer contrato, fornecedor **e armazém**.
   Inverter duas Expedições é a mesma operação aplicada às duas, numa transação.
2. Vale para origem produtor (`Standard`) e Yokotobi (`OwnershipTransfer`, com lote), nos dois sentidos,
   e para liberação de devolução (`SalesReturn`).
3. Três lançamentos por troca, **todos com a data da carga (`ShipmentLoad.LoadDate`)**:
   a Expedição original fica intacta (marcada substituída), um **estorno na origem** e uma **Expedição
   nova no destino**.
4. O grid "Romaneios da Carga" mostra os três, com a situação de cada um.
5. **Quantidade da Expedição nova:**
   - carga com **uma única Expedição vigente** (troca de 1): `ShipmentLoad.InvoicedQuantity` (notas
     confirmadas − devoluções confirmadas). Se for ≤ 0, a troca é recusada ("Carga sem faturamento:
     desvincule e estorne a Expedição.");
   - carga com **mais de uma Expedição vigente** e **inversão**: cada Expedição nova repete o bruto da
     original.
6. Motivo obrigatório, registrado na Movimentação da carga com o de/para. Sem papel novo nem prazo.
7. Contrato **Finalizado** na origem ou no destino bloqueia — exceto liberação que não consome contrato
   (`ReleaseOriginRules.ConsumesPurchaseContract == false`, i.e. `SalesReturn`).
8. Destino: **mesmo produto**, liberação **Ativa** e com saldo (conferido depois de aplicar todos os itens
   da operação); armazém livre — a Expedição nova vai para `DeliveryLocationCode` da liberação.
9. **Conferência de saldo de armazém (GAC-1164) Aprovada**, do mesmo produto, no armazém de origem **ou**
   de destino, com `ReferenceDate >= LoadDate`, **bloqueia** a troca; a mensagem cita a conferência e pede
   que ela seja cancelada (estornada) antes.
10. A Expedição de troca é a vigente e **pode ser trocada de novo** (novo trio).

## Modelo

### Documento da troca — `ShippingReleaseChange` / `SHIPPING_RELEASE_CHANGES` (migration nova)

Key, ShipmentLoadKey, OriginalSalesStorageTransactionKey, OriginalPurchaseStorageTransactionKey (anulável),
ReturnSalesStorageTransactionKey (12), ReturnPurchaseStorageTransactionKey (9, anulável),
NewSalesStorageTransactionKey, NewPurchaseStorageTransactionKey (anulável), SourceShipmentReleaseKey,
TargetShipmentReleaseKey, OriginalQuantity, NewQuantity, Reason (500), OperationGroupKey (Guid — itens da
mesma chamada/inversão), BaseEntity (CreatedAt/By…).

`StorageTransaction` ganha `ReplacedByShippingReleaseChangeKey` (anulável) na Expedição original e
`ShippingReleaseChangeKey` (anulável) nos romaneios gerados pela troca (estorno e nova).

### Lançamentos (data = `LoadDate`)

| Lançamento | Origem/destino Standard | OwnershipTransfer / SalesReturn |
|---|---|---|
| Original (7 e 8) | intactos; 7 recebe `ReplacedBy…` | idem (só 7) |
| Estorno na origem | **12** (bruto original, armazém de origem, sem carga) + **9** (mesmos pesos/descontos do 8 original, `ShipmentReleaseKey` de origem, alocação **negativa** no contrato de origem) | **12** com `ShipmentReleaseKey` e `StorageAddressCode` de origem (credita armazém e lote; devolve o romaneado da liberação) |
| Expedição nova no destino | **8** (bruto = quantidade nova, `ProcessingCostCode` do original, confirmado → `NetWeight`, alocação no contrato de destino) + **7** (armazém/lote do destino, `ShipmentLoadKey` da carga, status da 7 original) + `SHIPPING_TRANSACTIONS` novo | **7** com lote do destino |

Saldos resultantes (conferir nos testes):
- Armazém (8/12/14 somam; 7/9/13 subtraem): original −bruto+líq (Standard) é compensado pelo estorno
  +bruto−líq; a nova debita o destino.
- Lote: **passa a creditar o tipo 12 com `StorageAddressCode`** (nenhum 12 existente tem lote).
- Liberação Standard: `Σ8 − Σ9`; Transfer/SalesReturn: `Σ7 − Σ12` — os dois estornos devolvem.
- Contrato: alocação −líq (9) na origem, +líq (8) no destino.
- **Confirmação do tipo 9 não existe hoje** e precisa ser implementada.
- O estorno **não passa** pela trava de movimentação da liberação (liberação de origem costuma estar
  Finalizada).

### Carga

- Total da carga = Σ bruto das 7 **vigentes** (com `ShipmentLoadKey` e sem `ReplacedBy…`).
- Todos os leitores de romaneios da carga (recálculos, cancelamento, desvinculação, recusa, faturamento,
  guards) ignoram a original substituída; o 12 do estorno nunca carrega `ShipmentLoadKey`.
- Grid "Romaneios da Carga": vigentes + substituídas + estornos, com coluna **Situação**: Vigente /
  Substituída / Estorno de troca / Expedição de troca.
- Movimentação `ShipmentLoadMovementType.ReleaseChanged = 14`, quantidade = NewQuantity − OriginalQuantity,
  motivo, de/para (contrato, fornecedor, armazém).

## Fora do escopo
- Desfazer uma troca (corrige-se trocando de novo).
- Tolerância a SAP fora na lista de destinos (follow-up).
