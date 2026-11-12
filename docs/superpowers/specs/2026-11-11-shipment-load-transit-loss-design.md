# GAC-1213 — Quebra de mercadoria durante o trajeto

Data: 2026-11-11 · Repositórios: `siagro-b1-backend` e `siagro-b1-frontend` · Branch: `feature/gac-1213-transit-loss`

## 1. Intenção

Mercadoria embarcada e faturada pode se perder no caminho: carga derrubada ou avariada, sinistro,
roubo, ou outro motivo. Hoje não há como registrar isso. As três saídas da Recusa de Carga dão
destino errado ao volume: "segue viagem" o recoloca à venda, "armazém" e "transbordo" o creditam
num armazém.

Pedido do chamado, nas palavras do usuário: essas quebras **baixam o contrato de compra, nunca
entregam para um contrato de venda e não retornam para nenhum armazém**.

## 2. Decisões do usuário (11/11/2026 — não reabrir)

1. A quebra é lançada **só sobre documento de saída** já emitido. Não há quebra sobre saldo ainda
   não faturado da carga.
2. Compra CIF (troca nota) segue a **mesma regra**: a quebra de trajeto baixa o contrato de compra.
3. Carga de **Remoção fica fora** desta rodada.
4. **Fiscal e financeiro ficam fora**: NF-e de baixa de estoque por perda/roubo (CFOP 5.927) e
   cobrança da transportadora ou seguradora são próximos passos, não escopo.

## 3. Como o sistema já se comporta (base do desenho)

- A baixa do contrato de compra nasce no **embarque**: a Expedição grava o par Compra (8) +
  Saída (7) e a Compra fica alocada no contrato. Nenhum destino da recusa desfaz essa perna.
  Logo, "baixar a compra" = **não tocar na perna de compra**.
- O contrato de venda só é consumido pelo documento de saída. A recusa cria e confirma a
  devolução (`SalesInvoiceReturnFactory` + `SalesInvoicesConfirmService`), que devolve o saldo
  à venda e à liberação de venda. É exatamente o "nunca entrega para a venda".
- Saldo da carga hoje: `Disponível = Total − Faturado − DevolvidoAoArmazém − EmTransbordo`
  (`ShipmentLoad.CalculateAvailableQuantity`). A devolução confirmada reduz o Faturado; sem um
  termo novo, o volume perdido voltaria ao Faturamento de Expedição.

## 4. Desenho

### 4.1 Quarto destino da recusa

`RefusalDestination.TransitLoss = 3` (append). A ação `ShipmentLoadsRefuse` passa a aceitar
`Destination = "TransitLoss"` e um parâmetro novo, opcional no EDM e **obrigatório quando o destino
é quebra**: `TransitLossReason`.

Enum novo `TransitLossReason` (gravado como int, valores novos sempre no fim):

| Valor | Código | Rótulo na tela |
|---|---|---|
| 0 | `Damage` | Avaria / mercadoria derrubada |
| 1 | `Accident` | Sinistro |
| 2 | `Theft` | Roubo de carga |
| 3 | `Other` | Outros |

O motivo em texto livre (`Reason`) continua obrigatório, como nos outros destinos. Armazém de
destino não é pedido nem aceito.

Validações da recusa: todas as que já existem valem igual (carga cancelada, planejada,
descarregada, concluída, remoção, recusa pendente, documento confirmado e Normal, quantidade
devolvível por documento). Recusa parcial é permitida: o restante do documento segue entregue.

### 4.2 Efeito: romaneio "Quebra de trajeto" + quinto termo do saldo

Em `ShipmentLoadRefusalEffectsService`, ramo novo `RegisterTransitLossAsync`, **uma entrada por
recusa** (mesmo critério do destino Armazém):

- Romaneio `StorageTransactionType.TransitLoss = 18` (append), criado e confirmado na mesma
  transação, em `CommitMode.Deferred`, com `TransactionCode.ShipmentLoad`.
- Campos: filial, produto, unidade, caminhão e motorista da carga; `GrossWeight = NetWeight =`
  total recusado; `CardCode` do primeiro documento recusado; `WarehouseCode` = armazém de origem
  da carga (a coluna é NOT NULL; é só informativo, ver abaixo); `TransactionDate` = hoje;
  `TransitLossReason` (coluna nova, anulável, em `STORAGE_TRANSACTIONS`); `Comments` no mesmo
  formato da devolução ao armazém, **incluindo a lista `Documento(s): ...`**, que é o que o
  estorno usa para achar a entrada da recusa certa (`RefusalEntryDocuments`).
- Vínculo com a carga: `RefusedFromShipmentLoadKey` (reuso deliberado — é a chave que
  `StorageTransactionsCancelService` e `StorageTransactionsReverseService` já usam para impedir o
  cancelamento pela tela de Romaneios). **Nunca** `ShipmentLoadKey`, `ShipmentReleaseKey`,
  `ReturnInvoiceKey` nem contrato/alocação: as mesmas chaves proibidas do romaneio 12, pelos
  mesmos motivos.
- O romaneio 18 **não entra em nenhum saldo**: armazém, endereço/lote, embarcado da liberação,
  cobrança de armazenagem, quebra técnica, saldo diário, Kardex/estoque fiscal. Esses filtros são
  listas de inclusão de tipos; o construtor confirma cada um e cobre com teste o saldo de armazém
  e o embarcado da liberação. Não gera liberação nenhuma (nada volta à Expedição).
- `ShipmentLoad.TransitLossQuantity` (coluna nova, `DECIMAL(18,3) DEFAULT 0`): Σ `GrossWeight`
  dos romaneios tipo 18 com `RefusedFromShipmentLoadKey` = carga, exceto cancelados. Fórmula em
  serviço estático próprio (molde de `ShipmentLoadsRecalculateReturnedService`), **gravada por
  `ShipmentLoadsRecalculateInvoicedService`**, que continua o escritor único do status.
- `CalculateAvailableQuantity` ganha o quinto termo. Todo chamador da fórmula e de
  `ResolveStatus` é atualizado (guard de faturamento, guard de composição, início de transbordo).
- Movimento novo `ShipmentLoadMovementType.TransitLoss = 26`, delta negativo, com contexto
  (cliente, local de entrega, motivo, chave do romaneio).

O que **não** muda, e precisa de teste que prove: a Compra (8) da carga continua confirmada e
alocada; `AllocatedVolume`/entregue do contrato de compra não se alteram; o saldo do contrato de
venda e da liberação de venda volta pelo valor devolvido; saldo de armazém nenhum se move.

### 4.3 Situação da carga

`ShipmentLoadStatus.TransitLoss = 10`, rótulo **"Quebra de trajeto"** (decisão minha).

Em `ResolveStatus`, quando o saldo fecha (`available <= tolerância`): havendo quebra, a situação é
`TransitLoss`; senão `Returned` se houve devolução ao armazém; senão `Invoiced`. A quebra vence a
Devolvida por ser o evento mais grave. Carga com saldo ainda aberto segue `Open`/`PartiallyInvoiced`
pelas regras atuais; a quebra conta como consumo no critério "parcial", como a devolução ao armazém.

Consequências, iguais às da Devolvida:
- carga mista (parte entregue, parte perdida) fica "Quebra de trajeto", aceita ticket de descarga
  e não evolui para Descarregada/Concluída;
- os romaneios de embarque ficam `Invoiced` (não voltam à Montagem);
- `ShipmentLoadsCompositionGuardService`: quebra > 0 trava desvincular romaneio e cancelar a carga,
  com mensagem própria.

### 4.4 Filial que emite NF-e pelo Siagro (recusa em dois tempos)

Mesma regra dos outros destinos: a recusa fica Pendente até as NF-e de entrada das devoluções
serem autorizadas. `ShipmentLoadRefusal` ganha `TransitLossReason` (anulável) para guardar o motivo
até a conclusão; `ShipmentLoadRefusalCompleteService` o repassa aos efeitos. Cancelar a recusa
pendente não deixa rastro de quebra.

### 4.5 Estorno

Estornar a confirmação de uma devolução nascida de recusa com quebra **cancela junto o romaneio
18 daquela recusa** e recalcula a carga (o volume volta a constar como faturado), seguindo a
mecânica por recusa já existente em `SalesInvoicesReverseConfirmService`.

O estorno localiza o romaneio pelo vínculo `SalesInvoice.RefusalStorageTransactionKey`, gravado em
toda devolução da recusa nos destinos Armazém e Quebra (fluxos síncrono e em dois tempos). Os
irmãos são as devoluções confirmadas com a mesma chave, estornadas juntas. Motivo: duas recusas
parciais da MESMA nota com destinos diferentes (ex.: parte quebra, parte armazém) não se
distinguem pelo texto da lista de documentos do Comments. O legado sem vínculo segue pela lista do
Comments e só resolve o tipo 12 (o romaneio 18 é novo e sempre tem o vínculo). Sem as partes de
armazém: não há saldo de armazém a conferir nem liberação a cancelar. O histórico da carga recebe
um movimento `TransitLoss` positivo (volume do romaneio cancelado, um por romaneio), registrado
depois do recálculo para que o saldo posterior reflita a carga refeita. Cancelar o romaneio 18
direto pela tela de Romaneios continua recusado; copiá-lo também.

### 4.6 Telas (frontend)

- **Diálogo "Registrar Recusa da Carga"**: quarta opção em Destino Físico, "Mercadoria perdida no
  trajeto (quebra)". Ao escolhê-la aparece o campo obrigatório "Tipo de Quebra" (os quatro
  valores) e some o Armazém de Destino. O botão de confirmação e o texto de confirmação mencionam
  a quebra e que o volume não volta a nenhum armazém nem fica disponível para faturar.
- **Recusa pendente** (`PendingRefusal.fragment.xml`): mostra o destino "Quebra de trajeto" e o tipo.
- **Detalhe da carga**: quantidade "Quebra de Trajeto" no cabeçalho ao lado de Devolvido/Transbordo
  (visível quando > 0); a aba que lista as devoluções da recusa passa a mostrar também o romaneio
  18, com coluna de tipo e o tipo de quebra; histórico com o movimento novo.
- **Lista e painel de cargas, Faturamento de Expedição**: situação nova com rótulo e cor (erro) nos
  formatters e no filtro de Situação.
- **Romaneios**: rótulo "Quebra de trajeto" para o tipo 18 onde os tipos são listados/filtrados.
- Textos em pt-BR; identificadores em inglês.

### 4.7 Relatórios

`ShipmentLoadsByPeriodReportService` (Cargas por período) e os rótulos de situação usados nos
relatórios de logística passam a conhecer a situação nova e, onde já exibem "devolvido ao
armazém", exibem a quebra. Nenhum relatório novo.

### 4.8 Banco

Uma migration em `AppDbContext`, `AddShipmentLoadTransitLoss`:
- `SHIPMENT_LOADS.TransitLossQuantity DECIMAL(18,3) NOT NULL DEFAULT 0`;
- `STORAGE_TRANSACTIONS.TransitLossReason INT NULL`;
- `SHIPMENT_LOAD_REFUSALS.TransitLossReason INT NULL`.

Segunda migration em `AppDbContext`, `AddSalesInvoiceRefusalEntryLink`: coluna anulável
`SALES_INVOICES.RefusalStorageTransactionKey` + índice + FK para `STORAGE_TRANSACTIONS` com
`NoAction`, sem backfill (ver §4.5).

Sem backfill. Sem migration de menu (nenhuma tela nova).

## 5. Fora do escopo

- Quebra sobre saldo não faturado; carga de Remoção; fluxo legado de retorno de documento de saída
  sem carga (`SalesInvoicesReturnService` recusa o destino novo com mensagem clara).
- NF-e de baixa (CFOP 5.927), título contra transportadora/seguradora, anexos de boletim de
  ocorrência (a carga já tem anexos genéricos).
- Exibir a quebra por contrato nas Entregas/Extrato do contrato de compra. A nota de carga não
  liga romaneio a documento, então atribuir a perda a um contrato seria rateio arbitrário; a
  compra simplesmente continua entregue (decisão minha — revisar se o usuário quiser o rateio).

## 5.1 Limitações conhecidas (11–12/11/2026)

- **Filial que emite NF-e pelo Siagro:** depois de concluída a recusa (NF-e de entrada
  autorizadas), a quebra não tem estorno pela tela: a regra herdada da recusa em dois tempos
  recusa estornar devolução com NF-e. A janela é cancelar a recusa enquanto Pendente. Decisão
  pendente do usuário.
- **PDF Cargas por Período** não ganhou coluna da quebra: o modelo de dados a carrega, mas o
  template `.frx` não foi alterado. Item da §4.7 não entregue.
- **Reconfirmar uma devolução depois do estorno** a deixa como "segue viagem" (o volume volta a
  ficar faturável), igual ao destino Armazém.
- **Recusas ao armazém anteriores à migration** `AddSalesInvoiceRefusalEntryLink` não têm o
  vínculo e mantêm a ambiguidade antiga.
- **Trava de campos fiscais** não cobre a carga Devolvida (lacuna anterior ao branch); a carga com
  quebra (`TransitLoss`) passou a ser coberta.

## 6. O que seria um fracasso mesmo "funcionando"

- o volume perdido reaparecer no Faturamento de Expedição ou na Montagem;
- qualquer saldo de armazém, lote ou liberação se mover;
- o contrato de compra reabrir saldo (como faz a Quebra de descarga 16);
- o contrato de venda continuar consumido;
- um erro de operador sem caminho de volta pela tela.

## 7. Verificação

- Testes xUnit: recusa síncrona total e parcial, dois tempos (pendente → conclusão → cancelamento),
  estorno, situação e saldo (`ResolveStatus` com o quinto termo), guards de composição e
  faturamento, invariantes da §4.2, EDM da ação.
- Frontend: `yarn ts-typecheck`, `yarn lint`, QUnit dos formatters novos.
- No navegador, em cópia do banco local (o banco compartilhado tem migrations de outros
  branches): recusa com quebra parcial e total, conferindo carga, contrato de compra, contrato de
  venda e saldo de armazém; depois o estorno.

## 8. Commits

Escopo `shipment`, `Refs: GAC-1213`, `DB: AddShipmentLoadTransitLoss` no commit da migration.
Sem push; merge em `main` é do usuário.
