---
status: desenho aprovado seção a seção pelo usuário; implementação não iniciada
chamado: GAC-1181
data: 2026-09-21
---

# Design — Transbordo na Carga ("mexer na carga")

## Contexto

A mercadoria é carregada num armazém, **descarregada e recarregada** num armazém intermediário
(para padronizar, limpar ou secar) e segue para o cliente. Hoje isso obriga a abrir **duas
cargas**, e a transportadora — que enxerga uma viagem só — fica sem documento único. Pensando no
CT-e, `n` cargas para um CT-e seria pior ainda.

O chamado descreve dois casos:

1. **Armazém de terceiro → armazém próprio (Yokotobi) → cliente.** Paliativo atual: duas cargas,
   cancelando a primeira depois de lançar a Entrada em Armazenagem. O GAC-1175 melhorou isso (a
   primeira vira carga de **Remoção** e termina em Concluída em vez de cancelada), mas continuam
   **dois registros**.
2. **Armazém de terceiro → outro armazém de terceiro → cliente.** Sem cobertura nenhuma.

A esses dois somou-se um terceiro, levantado pelo usuário durante o desenho:

3. **Transbordo depois da recusa.** Carga montada em SP, faturada para o porto no PR, recusada por
   qualidade, segue para um armazém parceiro para transbordo e sai de lá para o **mesmo cliente ou
   outro**, com faturamento novo — tudo na **mesma carga**.

## Decisões do usuário (18 e 21/09/2026)

1. O armazém intermediário **sempre** registra entrada e saída, próprio ou de terceiro, qualquer
   que seja o tempo de permanência.
2. O peso que sai pode ser **menor** que o que entrou: pode **sobrar volume armazenado** lá.
3. A sobra continua **vinculada ao contrato de compra de origem**. Ela sai do armazém por uma
   liberação que **não consome o contrato**, no molde da `SalesReturn`.
4. A sobra **não deixa a viagem pendente**: a carga encerra quando o que saiu para o cliente
   estiver faturado.
5. O transbordo é **parte de uma carga Normal**, não um tipo novo de carga nem um agrupador de
   cargas.
6. A trava de homogeneidade (**mesma placa**, produto, filial e unidade) continua valendo em todas
   as pernas.
7. A **recusa ganha um destino físico novo, "Transbordo"**, e a carga não termina nele.
8. **Sem complemento de frete** nesta versão: fica só o `FreightPrice` da carga.

## Desenho

### 1. O transbordo é um registro filho da carga

`SHIPMENT_LOAD_TRANSSHIPMENTS`, 0..N por carga, no molde de `SHIPMENT_LOAD_DISCHARGES` (GAC-1171):

| Campo | Observação |
|---|---|
| `Key`, `ShipmentLoadKey` | FK `NoAction`, como todas as filhas deste projeto |
| `Sequence` | 1, 2, 3… dentro da carga; é por ele que se acha o **último** |
| `Origin` (`TransshipmentOrigin`) | `Planned = 0` (casos 1 e 2) ou `Refusal = 1` (caso 3) |
| `WarehouseCode` / `WarehouseName` | armazém onde a mercadoria é mexida |
| `TransshipmentDate` | data do evento |
| `OutgoingQuantity` | volume que **sai da carga** para o armazém |
| `EntryQuantity` | peso **pesado na entrada**; zero enquanto a entrada não foi registrada |
| `EntryStorageTransactionKey` | romaneio de entrada (o 15, ou o `Receipt` da Entrada em Armazenagem) |
| `Comments`, auditoria própria | `CreatedAt/By`, `UpdatedAt/By`, sem herdar `BaseEntity` |

`Quebra = OutgoingQuantity − EntryQuantity`. É informativa: ninguém fatura esse volume e ele não
volta ao contrato.

**`OutgoingQuantity` é o saldo disponível INTEIRO da carga no momento**, porque tudo é
descarregado. Não existe descarregar parte e seguir com o resto no caminhão.

### 2. O romaneio de entrada

| Armazém do transbordo | Romaneio de entrada | Liberação emitida |
|---|---|---|
| Terceiro | **`StorageTransactionType.TransshipmentReceipt = 15`**, novo | `ReleaseOrigin.Transshipment = 3`, uma por contrato |
| Próprio (`WarehouseComplement.IsOwn`) | o `Receipt` da Entrada em Armazenagem, lançada pela tela de sempre | nenhuma — o grão entra no **lote** |

O 15 vale para as duas origens (`Planned` e `Refusal`). A primeira versão do desenho usava o 12
(`SalesShipmentReturn`) no transbordo vindo de recusa; foi descartada porque as duas liberações
teriam regras idênticas e porque manter o 12 fora daqui deixa o ramo `Warehouse` da recusa
**intacto**.

**Chaves que o 15 NÃO pode carregar:**

- **`ShipmentReleaseKey`** — o romaneio que ORIGINA uma liberação nunca aponta para ela. É a
  armadilha provada em `Recalc_SalesReturnRelease_WouldGoNegative_IfTheReturnEntryCarriedTheKey`:
  com a chave, 30.000 kg de entrada dariam `Shipped = −30.000` e `Available = 60.000`. O vínculo
  certo é o inverso — `ShipmentRelease.GeneratedByStorageTransactionKey` aponta o 15.
- **`ShipmentLoadKey`** — a carga o alcança pelo `ShipmentLoadTransshipmentKey`. Com a chave da
  carga, o cancelamento (`ShipmentLoadsCancelService`) a zeraria e o romaneio ficaria órfão, e
  qualquer somatório futuro por FK passaria a incluí-lo.

O 15 usa `TransactionCode.ShipmentLoad` como `TransactionOrigin`, como faz a recusa. **Não precisa
de seed em `DOC_NUMBERS`**: `StorageTransactionsCreateService:53` numera sempre por
`TransactionCode.StorageTransaction`, e o parâmetro só vira carimbo de origem.

### 3. O papel de cada romaneio é explícito

`STORAGE_TRANSACTIONS.ShipmentLoadTransshipmentKey` (anulável, FK `NoAction`):

| Romaneio | `ShipmentLoadKey` | `ShipmentLoadTransshipmentKey` |
|---|---|---|
| Saída da origem (7) | a carga | nulo |
| Entrada no transbordo (15 ou `Receipt`) | **nulo** | o transbordo |
| Saída do transbordo para o cliente (7) | a carga | o transbordo |

A primeira versão deduzia o papel pelo **armazém**. Caiu quando entrou o caso 3: depois da recusa,
a saída do armazém parceiro é tão "saída para o cliente" quanto a de SP foi, e um mesmo armazém
pode ser origem de uma carga e transbordo de outra.

### 4. A liberação de transbordo

`ReleaseOrigin.Transshipment = 3`. As três perguntas de `ReleaseOriginRules`:

| Pergunta | Resposta | Consequência |
|---|---|---|
| `ShipsWithoutPurchaseLeg` | **sim** (já vale: `origin != Standard`) | a Expedição de saída cria só o 7, sem Compra(8) e sem alocar contrato |
| `ConsumesPurchaseContract` | **não** (muda: `origin is not (SalesReturn or Transshipment)`) | o volume já foi debitado do contrato na saída da origem |
| `RequiresStorageAddress` | **não** (já vale: só `OwnershipTransfer`) | entrada em nível de ARMAZÉM, sem lote |

`ShippedSign` e `CalculateShippedAsync` não mudam: no ramo sem perna de compra, o 7 consome e o 12
devolve, que é exatamente o comportamento desejado para a saída do transbordo.

**Uma liberação por CONTRATO**, com o peso de entrada rateado por peso entre os contratos das
saídas da origem — reaproveitando `ShipmentReleasesFromReturnService.DistributeByWeight` e o
rastreio de contrato (cadeia curta pela `ShipmentReleaseKey`, cadeia longa por
`SHIPPING_TRANSACTIONS`). Volume sem contrato rastreável **não derruba** o registro: fica sem
liberação e o motivo vai para o `Comments` do 15, como já acontece na recusa.

### 5. Saldo e ciclo de vida

```
TotalQuantity     = Σ GrossWeight das Expedições (7) com ShipmentLoadKey da carga
                    — origem E saídas de transbordo
AvailableQuantity = Total − Faturado − DevolvidoAoArmazém − Σ OutgoingQuantity dos transbordos
```

O quarto termo, `TransshippedQuantity`, é persistido-derivado na carga, escrito pelo mesmo
`ShipmentLoadsRecalculateInvoicedService` que já grava `InvoicedQuantity`,
`ReturnedToWarehouseQuantity` e `Status` — escritor único, pelo mesmo motivo do terceiro termo.

**Situação nova `ShipmentLoadStatus.InTransshipment = 7`** ("Em transbordo"), enquanto o último
transbordo não tem saída vinculada. O ramo entra **antes** dos demais em `ResolveStatus`, que passa
a receber também `hasOpenTransshipment`. Sem ele, o saldo zero depois do transbordo faria a carga
ler "Faturada" e sumir das telas de pendência — a mesma armadilha do `ResolveStatus(0, 0)` que já
jogou a carga planejada no Faturamento.

**O cenário 3 em números** (SP → porto PR → recusa → parceiro → cliente 2):

| Passo | Total | Faturado | Transbordado | Disponível | Situação |
|---|---|---|---|---|---|
| Expedição SP 30 t vinculada | 30 | 0 | 0 | 30 | Carregada |
| Nota para o porto | 30 | 30 | 0 | 0 | Faturada |
| Recusa, destino Transbordo | 30 | 0 | 30 | 0 | **Em transbordo** |
| Entrada em B: 29,8 t (quebra 0,2) | 30 | 0 | 30 | 0 | Em transbordo |
| Expedição de B 29,5 t vinculada | 59,5 | 0 | 30 | 29,5 | Carregada |
| Nota para o cliente 2 | 59,5 | 29,5 | 30 | 0 | **Faturada** |

Os 0,3 t restantes ficam como saldo da liberação de transbordo em B e saem da viagem.

**Caso 2 (planejado), sem recusa:** origem 30 t → transbordo com `Outgoing = 30`, `Entry = 29,8` →
disponível 0, "Em transbordo" → saída de B 29,5 t vinculada → total 59,5, disponível 29,5 →
faturamento normal.

**Caso 1 (armazém próprio):** não há saída da origem na carga (a Entrada em Armazenagem cobre o
trecho), então `OutgoingQuantity = 0` e o transbordo nasce já com a entrada: o `Receipt` da
Entrada em Armazenagem é vinculado no papel de entrada. A saída do lote segue o fluxo padrão.

### 6. Ciclo do transbordo e travas

1. **Aberto** — "Iniciar Transbordo" (armazém + data), ou a recusa com destino Transbordo.
   `OutgoingQuantity` = saldo disponível da carga no momento.
2. **Com entrada** — "Registrar Entrada" com os pesos gera o 15 e as liberações (armazém de
   terceiro) ou vincula o `Receipt` (armazém próprio).
3. **Com saída** — as Expedições de saída são vinculadas pela página de Vincular Romaneios, no
   papel do transbordo.

Na tela, 1 e 2 podem ser feitos de uma vez. Existem separados porque, na recusa, o caminhão ainda
está no cliente quando a recusa é lançada.

**Travas:**

- Desvincular a saída da **origem** é recusado quando existe transbordo: as liberações de B
  derivam dela.
- **Estornar Transbordo** só o **último**, e só enquanto nenhuma liberação de transbordo tiver
  consumo (`ShippedQuantity = 0`). Cancela o 15 e as liberações, devolve o saldo à carga.
- Estornar um transbordo de origem `Refusal` **não desfaz** as devoluções das notas — o saldo volta
  para a carga, como num "segue viagem".
- **Cancelar** ou **excluir** a carga com transbordo é recusado, pedindo o estorno antes.
- O 15 **não se cancela nem se estorna pela tela de romaneios**: só pelo Estornar Transbordo.
- Faturar carga `InTransshipment` é recusado **por status**, com mensagem própria (o saldo é zero e
  a mensagem de quantidade mandaria o usuário procurar um problema que não existe).
- Carga de **Remoção** não tem transbordo.

## Arquivos

### Backend

**Domínio:** `ShipmentLoadTransshipment` (entidade nova), `TransshipmentOrigin` (enum novo),
`StorageTransactionType.TransshipmentReceipt = 15`, `ReleaseOrigin.Transshipment = 3`,
`ShipmentLoadStatus.InTransshipment = 7`, `RefusalDestination.Transshipment = 2`,
`ShipmentLoadMovementType` 19-21 (`TransshipmentStarted`, `TransshipmentEntered`,
`TransshipmentReversed`), `ShipmentLoad.TransshippedQuantity` + coleção `Transshipments`,
`StorageTransaction.ShipmentLoadTransshipmentKey`, `ReleaseOriginRules.ConsumesPurchaseContract`.

⚠️ Valor de enum persistido entra **sempre no fim**. Mudança de enum não gera migration e nada
avisa.

**Serviços novos:** `ShipmentLoadsTransshipmentStartService`, `...RegisterEntryService`,
`...ReverseService`, `ShipmentLoadsTransshipmentsGetService`, `ShipmentLoadTransshipmentRules`
(guards compartilhados) e `ShipmentReleasesFromTransshipmentService`.

**Serviços alterados:**

| Arquivo | O que muda |
|---|---|
| `StorageTransactionsWarehouseBalanceService:51-73` | o **15 vira crédito**, ao lado de 8, 12 e 14 |
| `StorageTransactionsConfirmedService` | ramo de confirmação do 15 (entrada em nível de armazém, `NetWeight = GrossWeight`) |
| `ShipmentLoadsRecalculateTotalService` | soma as Expedições de todas as etapas |
| `ShipmentLoadsRecalculateInvoicedService` | quarto termo, `ResolveStatus` com `hasOpenTransshipment`, projeção de status |
| `ShipmentLoadsRecalculateTransshippedService` | fórmula do quarto termo (só a fórmula, como a de `Returned`) |
| `ShipmentLoadsBillingGuardService` | saldo novo + recusa por `InTransshipment` |
| `ShipmentLoadsCompositionGuardService` | transbordo congela a composição |
| `ShipmentLoadsCancelService`, `ShipmentLoadsDeleteService` | recusam com transbordo; `DeleteService` remove os filhos |
| `ShipmentLoadsAttachTransactionsService` | `ExpectedTransactionType` passa a ser função do **papel**; valida o armazém da saída do transbordo |
| `ShipmentLoadsDetachTransactionsService` | limpa `ShipmentLoadTransshipmentKey`; recusa desvincular a origem |
| `ShipmentLoadsRefuseService` | destino `Transshipment`: devoluções + abertura do transbordo |
| `ShipmentLoadsGetService.QueryTransactions` | inclui o romaneio de entrada pelo `ShipmentLoadTransshipmentKey` |
| `ShippingTransactionsChangeReleaseService` | recusa troca de liberação em saída de transbordo |
| `StorageTransactionsCancelService` / `ReverseService` | barram o romaneio com `ShipmentLoadTransshipmentKey` |
| `ShipmentReleaseMovementGuardService` | lista de tipos inalterada (o 15 não consome liberação) |

**Fora, de propósito:** leitores de saldo por LOTE (`StorageAddressesGetBalanceService` e os cinco
irmãos), `StorageAddressesDailyBalanceBuilderService` e o Extrato de Armazenagem — o 15 é entrada
em nível de armazém, sem lote, exatamente como o 12.

**Web:** `EntitySet<ShipmentLoadTransshipment>`, actions `ShipmentLoadsTransshipmentStart`,
`...RegisterEntry`, `...Reverse`, controller de leitura com as **duas rotas declaradas à mão**
(`odata/ShipmentLoads({key})/Transshipments` e `.../{key}/Transshipments`), `Destination` da recusa
aceitando `"Transshipment"`. Nos parâmetros: quantidade é `Edm.Double`, data é `string
yyyy-MM-dd` lida com `TryParseExact`, enum viaja como `string`, e `.Optional()` em tudo que puder
faltar — sem isso o `ODataParameterReader` recusa o payload.

### Frontend

- `fragments/ShipmentLoadTransshipments.fragment.xml` + `ShipmentLoadTransshipmentDialog.fragment.xml`,
  no molde das Descargas: `$$ownRequest: true`, `$select` explícito incluindo os campos lidos só
  pelo controller, `sorter` no cliente, buffer JSON `viewModel>/transshipmentDialog/...` (nunca
  two-way no contexto OData), `Fragment.load({ id: view.getId(), controller: this })` com
  `addDependent` dentro do `if`, trava `_inFlight` antes do primeiro `await`.
- Seção "Transbordos" no `Detail.view.xml` e o id da tabela em `refreshAll()`.
- Handlers em `shipmentLoads/BaseController.ts` (é lá que vivem os das Descargas, não no `Detail`).
- Coluna "Etapa" no grid de romaneios.
- Terceiro destino no `Refusal.fragment.xml`, com value help de armazém escrito **à mão** no model
  `refusal` (o `applyValueHelp` grava no contexto OData da view e o OData recusa).
- Seletor "Vincular como" no `Attach.controller.ts`; o `$filter` continua **string crua** (enum no
  `sap.ui.model.Filter` do V4 estoura).
- Formatters: `InTransshipment` em `formatShipmentLoadStatus`/`stateShipmentLoadStatus`,
  `TransshipmentReceipt` em `formatStorageTransactionType`, os três movimentos novos, e
  `formatTransshipmentOrigin`.
- Situação nova na `LoadsFilterbar` (multi-seleção) e raia no Painel (`LANES`).
- Texto em pt-BR literal, como todo o módulo (não usa i18n).

### Banco — uma migration

1. `CREATE TABLE SHIPMENT_LOAD_TRANSSHIPMENTS` com FKs `NoAction` e índices em `ShipmentLoadKey` e
   `EntryStorageTransactionKey`.
2. `ALTER TABLE STORAGE_TRANSACTIONS ADD ShipmentLoadTransshipmentKey UNIQUEIDENTIFIER NULL` + FK
   `NoAction` + índice.
3. `ALTER TABLE SHIPMENT_LOADS ADD TransshippedQuantity DECIMAL(18,3) NOT NULL DEFAULT 0`.

Sem seed de `DOC_NUMBERS` (ver §2). Sem backfill: cargas existentes nascem com zero transbordos e
`TransshippedQuantity = 0`, o que preserva o saldo atual de todas elas.

## Testes

xUnit + EF InMemory, TDD, no padrão de `ShipmentLoadsRefuseServiceTests` (carga montada e faturada
pelo caminho REAL, não por fixture artificial).

1. **Saldo, os três cenários numéricos** do §5, passo a passo, incluindo a situação em cada passo.
2. Sobra permanecendo na liberação de transbordo, sem deixar a viagem pendente.
3. `ResolveStatus`: `InTransshipment` vencendo os demais ramos; regressão dos casos existentes.
4. Saldo do armazém de transbordo creditado pelo 15; saldo do armazém de origem **não** creditado.
5. O 15 nascendo sem `ShipmentReleaseKey` e sem `ShipmentLoadKey` (teste de regressão direto da
   armadilha do `SalesReturn`).
6. Liberação de transbordo: não consome contrato, embarca sem perna de compra, não exige lote; uma
   por contrato; rateio por peso; volume órfão sem derrubar o registro.
7. Travas: estorno fora de ordem, estorno com consumo, cancelar/excluir carga, desvincular a
   origem, faturar `InTransshipment`, cancelar o 15 pela tela de romaneios, troca de liberação.
8. Recusa com destino `Transshipment`: devoluções criadas, carga não vai para `Returned`, ramo
   `Warehouse` **intacto** (regressão).
9. Modelo relacional (`DeleteBehavior.NoAction` em todas as FKs novas) e EDM (entity set, actions,
   tipos dos parâmetros, propriedade derivada).
10. Regressão das cargas Normal sem transbordo e das de Remoção.

## Verificação

Build, `dotnet test` inteiro, `ts-typecheck` e `eslint` limpos; depois, **no navegador, a partir da
home**, no ambiente Yokotobi-Development:

- caso 2 ponta a ponta (origem → transbordo em armazém de terceiro → saída → faturamento);
- caso 3 (o cenário do usuário: faturar, recusar com destino Transbordo, registrar entrada, sair
  para outro cliente, faturar de novo);
- caso 1 com armazém próprio;
- estorno do transbordo e as travas;
- regressão de uma carga Normal comum e de uma recusa com destino Armazém.

## Fora de escopo

Complemento de frete, CT-e, troca de liberação em saída de transbordo, estorno de transbordo que
não seja o último, transbordo em carga de Remoção, e controle por lote quando o transbordo cai em
armazém próprio vindo de recusa.

## Riscos conhecidos

1. **O tipo 15 precisa entrar em todas as fórmulas de saldo de ARMAZÉM e em nenhuma de LOTE.** A
   lista está em Arquivos; errar para menos esconde grão, errar para mais o duplica.
2. **`ResolveStatus`** é onde uma situação nova costuma se perder: a primeira cláusula do
   resolvedor antigo já casa com o estado novo.
3. **`StorageAddressesDailyBalanceBuilderService`** já diverge das demais fórmulas de lote hoje
   (só 0 − 1 − 4). Não é deste chamado, mas foi anotado no levantamento.
4. **Concorrência** continua protegida só pelo `[Timestamp] RowVersion` da carga.
