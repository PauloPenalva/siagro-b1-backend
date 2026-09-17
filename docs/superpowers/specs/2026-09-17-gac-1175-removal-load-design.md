---
status: implementado e verificado no navegador 17/09/2026
chamado: GAC-1175
---

# Design — Tipo de carga (Normal × Remoção) e vínculo do romaneio de Recebimento

## Contexto

Com a Montagem de Carga em produção, o frete pago a terceiros para **remover** mercadoria de um
armazém (de terceiro ou próprio) e trazê-la para a armazenagem própria continua sem documento: a
carga só aceita romaneio de embarque (`SalesShipment`) e todo o ciclo dela termina em
faturamento.

A carga ganha uma **natureza**: `Normal` (expedição + faturamento, comportamento anterior
intacto) e `Removal` (vincula romaneios de **Recebimento**, nunca fatura, termina em
`Completed`).

## Decisões do usuário (17/09/2026)

1. A carga de Remoção vincula **`StorageTransactionType.Receipt`** — o documento de quem traz
   mercadoria para dentro de um armazém.
   *(A primeira versão deste design vinculava `StorageEntryTransaction`, a "Entrada em
   Armazenagem". O usuário corrigiu durante a verificação: o documento certo é o Recebimento.)*
2. O Recebimento que é **perna de uma Entrada em Armazenagem entra normalmente** — num filtro só
   ficam cobertos o depositante terceiro e a compra que virou entrada própria; nos dois casos
   houve frete de remoção.
3. **Concluir é ação manual** (`Concluir Carga`), com `Reabrir` para desfazer engano.
4. O vínculo é feito **pela carga**, na mesma página de "Vincular Romaneios".
5. A homogeneidade continua exigindo **mesma placa, produto, filial e unidade**; romaneio sem
   placa simplesmente não é candidato.

## Desenho

**O vínculo reusa `StorageTransaction.ShipmentLoadKey`** — a FK que a expedição já usa. O que
muda por natureza de carga é só o **tipo aceito**, resolvido num lugar único:

```csharp
// ShipmentLoadsAttachTransactionsService
public static StorageTransactionType ExpectedTransactionType(ShipmentLoadType loadType) =>
    loadType == ShipmentLoadType.Removal
        ? StorageTransactionType.Receipt
        : StorageTransactionType.SalesShipment;
```

Consequência que enxugou a feature: **nenhum serviço, action, controller ou página paralela**.
Vincular, desvincular, cancelar, excluir e o grid do detalhe são os mesmos dos dois tipos.

- `LoadType` é escolhido na **criação** e imutável (`ShipmentLoadsUpdate` não o recebe).
- Ciclo da Remoção: `Planned` → (vincula) `Open` → (Concluir) `Completed` → (Reabrir) volta ao
  status **recalculado**, que é `Planned` se a carga ficou vazia.
- `Completed` é terminal e **manual**, como `Cancelled`: o recálculo não o reescreve, senão
  qualquer chamada reabriria a carga em silêncio.
- A carga de Remoção **não projeta `TransactionStatus`** no romaneio — o Recebimento pertence ao
  fluxo de armazenagem, e carimbá-lo como `Invoiced` bloquearia o estorno da entrada. Por isso
  desvincular e cancelar também **não** reescrevem o status dele.
- `TotalQuantity` soma o `GrossWeight` dos romaneios do tipo esperado — mesma grandeza nos dois.
- Recusada em: faturamento (`ShipmentLoadsBillingGuardService`), recusa/devolução
  (`ShipmentLoadsRefuseService`) e troca de liberação.
- Estornar uma entrada cujo Recebimento está em carga é barrado pelo guard que já existia em
  `StorageTransactionsCancelService` (`ShipmentLoadKey != null`) — sem código novo.

## Arquivos

**Backend** — `ShipmentLoadType` (novo enum), `ShipmentLoadStatus.Completed = 6`, 4 valores novos
em `ShipmentLoadMovementType`, `ShipmentLoad.LoadType`; migration `AddShipmentLoadType`
(coluna `int NOT NULL DEFAULT 0` — cargas existentes viram Normal); serviços novos
`ShipmentLoadsCompleteService` / `ShipmentLoadsReopenService` e actions
`ShipmentLoadsComplete` / `ShipmentLoadsReopen`; `LoadType` como `Parameter<string>` em
`ShipmentLoadsCreate`; ramos por tipo em `AttachTransactions`, `DetachTransactions`,
`RecalculateTotal`, `RecalculateInvoiced`, `Cancel`, `GetService.QueryTransactions`,
`BillingGuard`, `Refuse` e `ChangeRelease`.

**Frontend** — `formatShipmentLoadType` e `Completed` nos formatters; `Select` "Tipo de Carga"
travado por `form>/typeEditable`; coluna e filtro "Tipo" na lista; raia "Concluída" no Painel;
`LoadType eq 'Normal'` no escopo do Faturamento de Expedição; título da página de vínculo e do
grid por natureza; botões `Concluir`/`Reabrir` e ocultação de Recalcular Saldo, Qtd. Faturada,
Documentos de Saída, Devoluções, Recusa e Trocar Liberação na carga de Remoção.

## Verificação executada (17/09/2026, ambiente Yokotobi-Development)

`dotnet build` limpo, 2025 testes verdes, `ts-typecheck` e `eslint` limpos. No navegador, da
home: criação da carga de Remoção (tipo travado na edição), vínculo de 2 Recebimentos
(100.000 KG, incluindo a perna de uma Entrada em Armazenagem), ausência no Faturamento de
Expedição, bloqueio do estorno da entrada, Concluir → travas de desvincular e cancelar →
Reabrir → desvincular → volta a Planejada; e a regressão do tipo Normal (criar, vincular
embarque, aparecer no faturamento, desvincular).

**Dois achados corrigidos durante a verificação**, ambos armadilhas já conhecidas do projeto:

1. Rota de navigation property só responde declarada à mão — o grid de uma coleção nova volta
   404 e a seção aparece vazia. (Resolvido ao voltar para `Transactions`, que já tinha rota.)
2. `Include` de FK obrigatória vira INNER JOIN e some com a linha inteira, sem erro.

**Corrigido a pedido do usuário, na sequência:** a tela de Entrada em Armazenagem mostrava
situação e tipo em inglês ("Confirmed", "Purchase", "Receipt") porque os `ObjectStatus`/`Text`
bindavam o enum cru, sem formatter — defeito preexistente, não introduzido aqui. Entraram
`formatStorageEntryTransactionStatus`/`stateStorageEntryTransactionStatus` (Confirmada /
Estornada — o vocabulário da tela é ESTORNO: a ação chama "Estornar" e a auditoria grava
"Estornado por/em", então a aba "Canceladas" virou "Estornadas"), e os romaneios do par passaram
a usar os formatters que já existiam (`formatStorageTransactionType`,
`formatStorageTransactionStatus`, `stateStorageTransactionStatus`).
