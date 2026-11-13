# GAC-1176 — Transferência entre armazéns de terceiros

Chamado da Yokotobi, 12/11/2026. Continuação de
`2026-09-21-gac-1181-load-transshipment-design.md` (transbordo) — leia aquele primeiro.

## O pedido

Uma carga carrega num armazém de terceiro e descarrega em OUTRO armazém de terceiro, terminando
ali. Ela nunca "bate" no cliente e nunca é faturada, porque não atendeu contrato de venda. É
preciso poder concluí-la, controlando as etapas do transporte e as quebras.

Fora do pedido, porque já têm processo: carga que vai ao cliente e volta (retorno a armazém) e
carga que entra em armazém próprio (Entrada em Armazenagem).

## Por que não funcionava

O transbordo já registrava tudo o que a transferência precisa — saída da carga, entrada pesada,
quebra, romaneio de crédito no armazém de destino (tipo 15) e liberação para expedir de lá. Faltava
o fim: o transbordo só fechava com uma Expedição de venda vinculada como saída, então a carga que
termina no armazém ficava em "Em transbordo" para sempre — não concluía (`Concluir Carga` é da
Remoção), não cancelava e não excluía (recusados em carga com transbordo).

## O desenho

**A transferência é um transbordo com outro fim, não um tipo novo de carga.**

- `SHIPMENT_LOAD_TRANSSHIPMENTS.Type` (`TransshipmentType`: `Transshipment = 0`, `Transfer = 1`).
  Só dá o contexto certo na tela — pedido do usuário: manter o processo do transbordo, mas o
  operador não deve ler "transbordo" quando está fazendo uma transferência. Escolhido ao iniciar
  ("Iniciar Transferência", mesma action com o parâmetro `Type`).
- `SHIPMENT_LOAD_TRANSSHIPMENTS.FinalDestinationAt` / `FinalDestinationBy`: o FATO de que a carga
  terminou ali. Nulo = não é destino final. Um fato só, sem bool ao lado.
- `SHIPMENT_LOADS.IsInTransfer`: persistido-derivado, gravado pelo recálculo — a carga está
  `InTransshipment` e a parada em aberto é uma transferência. As telas leem "Em Transferência".
  Não há situação nova no enum da carga.

**Concluir** (`ShipmentLoadsTransferComplete`, `ShipmentLoadsTransshipmentCompleteService`) grava o
fato e deixa o escritor único do status derivar a situação. Não escreve `Completed` à mão: qualquer
recálculo posterior mantém a carga concluída. Regras: sem recusa pendente; carga em transbordo; é o
ÚLTIMO transbordo; entrada registrada; armazém de TERCEIRO (a entrada é o romaneio 15 — estrutura
persistida, não `IsOwn` ao vivo); sem Expedição de saída vinculada. O transbordo comum concluído
assim vira `Transfer` (o plano mudou no meio do caminho).

**Reabrir** (`ShipmentLoadsTransferReopen`) apaga o fato; a carga volta a "em transferência", de
onde pode ser estornada. Recusado se a liberação gerada pela entrada já foi embarcada — a
mercadoria saiu do destino noutra carga, e reabrir deixaria esta em trânsito sem estoque e sem
estorno possível. Não desfaz a conversão para `Transfer`.

**Situação da carga** (`ResolveStatus`, ramo de saldo zerado, nesta ordem): quebra de trajeto →
`TransitLoss`; devolução ao armazém → `Returned`; **último transbordo é destino final e nada
faturado → `Completed`**; senão `Invoiced` (que a Conferência de Entregas ainda refina). Ou seja, a
carga parcialmente faturada cujo resto foi transferido segue o consumo comercial — concluí-la
direto esconderia notas não conferidas.

O critério é "o ÚLTIMO transbordo é final", não "algum é": depois de uma transferência a carga
mista ainda pode abrir outro transbordo (recusa com destino Transbordo), e o anterior vira
histórico.

**O que fica vivo:** o romaneio de entrada (15) e as liberações `Origin = Transshipment`. A
mercadoria está disponível no armazém de destino e sai dali depois pela Expedição de Grãos, em
OUTRA carga. Essa expedição não toca a carga concluída.

**Quebra:** é a `ShrinkageQuantity` do transbordo (saída − entrada pesada), informativa, exibida
por linha e somada no cabeçalho da carga ("Quebra de Transporte").

## Travas nos vizinhos

- Estorno do transbordo final: "reabra a transferência antes".
- Vincular Expedição como saída de um transbordo `Transfer` ou final: recusado.
- Iniciar `Transfer` em armazém próprio: recusado já no início.
- Cancelar/excluir carga com transbordo: continuam recusados.
- `Completed` numa carga Normal passou a ter DOIS significados (Conferência de Entregas ou
  transferência). As dicas de `ShipmentLoadCompletionRules` e as mensagens de cancelar,
  desvincular, vincular, recusar, reabrir e concluir carga distinguem os dois — antes mandavam
  "cancelar os documentos de saída" de uma carga que não tem nenhum. O frontend tinha a mesma
  frase duplicada em `shipmentLoads/Detail.controller` e `storageTransactions/sales/Main.controller`.

## Limitações (decididas, não esquecidas)

- **Ticket de descarga e quebra de trajeto classificada (romaneio 18) continuam exigindo documento
  de saída.** `ShipmentLoadDischargeItem` tem FK obrigatória para o item da nota, e a quebra de
  trajeto nasce da recusa de nota. Na transferência a quebra é só a diferença de peso.
- **Armazém próprio como destino final não é transferência** — é Entrada em Armazenagem.
- **Relatório Cargas por Período** (pedido do usuário, 12/11): a linha lê "Em Transferência" e
  "Transferência Concluída". O FILTRO de situação continua pelo enum — a transferência entra em
  "Em Transbordo" e "Concluída". A concluída é reconhecida pelos números da carga (Normal,
  Concluída, nada faturado, com transbordo), sem consultar os transbordos.
- A lista de Expedições (`storageTransactions/sales`) também lê "Em Transbordo" para a carga em
  transferência: a view não carrega `IsInTransfer`.

## Deploy

Backend + migration `AddShipmentLoadTransshipmentFinalDestination` ANTES do frontend: o detalhe e a
lista de cargas selecionam `IsInTransfer`, `Type` e `FinalDestinationAt`.
