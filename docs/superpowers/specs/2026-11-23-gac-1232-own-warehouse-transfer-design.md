# GAC-1232 — Transferência para armazém próprio (desenho curto)

Data: 23/11/2026 · Chamado: GAC-1232 (Yokotobi, homologação) · Aprovado pelo usuário em 23/11/2026.

## Intenção

Na homologação, a carga CG000354 levou 20.000 kg da Agrohipo (F024689) para a Shangrila (F024538,
armazém próprio) pelo **transbordo**. O usuário lançou a entrada e a saída do lote em Romaneios de
Movimentação e não encontrou o contrato de compra 00005188 na Expedição. O transbordo em armazém
próprio exige a mesma placa na saída e o ticket de pesagem (a Yokotobi não usa a Pesagem), e o
usuário achou esse processo confuso. Além disso, a operação real permite que **outra placa**
carregue o grão depois.

O que o usuário quer: a carga termina no armazém próprio, como uma **transferência**, e o grão fica
disponível ali, ainda ligado ao contrato de compra, para sair depois em outra carga e com qualquer
placa.

## Objetivos

- Permitir **Iniciar Transferência** com destino em armazém próprio.
- **Registrar Entrada** da transferência em armazém próprio pelo **peso**, igual à de terceiro:
  romaneio de crédito do armazém (`TransshipmentReceipt`, tipo 15) e liberação `Origin =
  Transshipment` do contrato de compra no armazém de destino, **sem lote**.
- **Concluir Transferência** encerra a carga sem faturar (já funciona, porque a conclusão decide pelo
  tipo do romaneio de entrada, e ele passa a ser o 15).
- Depois disso, o grão sai pelos caminhos existentes: **Expedição de Grãos** numa carga nova (a
  liberação sem lote é expedida como a do transbordo de terceiro) ou **Entrada em Armazenagem** num
  lote comum (`ReleaseOriginRules.ShipsWithoutPurchaseLeg` já trata `Transshipment`: par
  `SalesShipment`/`Receipt`, sem perna de compra, mesmo precedente da devolução do GAC-1214).

## Fora do escopo

- O **transbordo** continua como está, em armazém próprio e de terceiro (o usuário chegou a pensar em
  removê-lo e decidiu manter).
- Nenhuma migration, nenhuma correção de dado. A CG000354 é refeita pelo usuário (estorno do
  transbordo e nova transferência).

## Abordagens consideradas

1. **Escolhida — mesmo caminho da transferência para terceiro.** Muda duas linhas de decisão no
   backend e uma no diálogo do frontend; estorno, reabertura, conclusão e Expedição já tratam a
   estrutura resultante (entrada 15 + liberação sem lote).
2. Entrada caindo direto num lote comum, pelo romaneio de pesagem, com a liberação carregando o lote.
   Descartada: conflita com as regras das duas dimensões de saldo (lote × armazém) da fase 2 do
   GAC-1181 e exige muito mais mudança.

## Desenho

- `ShipmentLoadsTransshipmentStartService`: sai a trava que recusa `Type = Transfer` em armazém
  próprio.
- `ShipmentLoadsTransshipmentRegisterEntryService`: o ramo de armazém próprio (vincular a Entrada em
  Armazenagem pesada no lote de transbordo) passa a valer só para `Type = Transshipment`. Transferência
  segue sempre o ramo de terceiro, seja qual for o armazém. Isso continua sendo decisão de CRIAÇÃO
  (regra temporal do `remarks` de `ShipmentLoadTransshipmentRules`): o tipo da linha já está
  persistido desde o início, e `IsOwn` ao vivo só importa para o transbordo.
- Frontend (`shipmentLoads/BaseController.ts`): o diálogo de Registrar Entrada pede o romaneio de
  Entrada em Armazenagem só quando o armazém é próprio **e** a linha é transbordo; transferência
  pede o peso. A regra vai para um helper puro em `helpers/TransshipmentHelpers.ts`, com teste QUnit.

## Junto no mesmo branch

A correção anterior deste chamado (commit `1335837b`): confirmar o romaneio de **Saída** num lote de
transbordo pela tela Romaneios de Movimentação também vincula e emite a liberação, como a pesagem.
Continua útil para quem usar o transbordo em armazém próprio com a mesma placa e sem a Pesagem.

## Deixado para quem implementa

- Mensagens e textos da tela de transferência que digam "armazém de terceiro" devem ficar neutros.
- Saldo: depois da Entrada em Armazenagem sobre a liberação, o armazém fica com 15 (+) e 7 (−), e o
  lote com 0 (+). É o mesmo comportamento já aceito na devolução ao armazém (GAC-1214).
