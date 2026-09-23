---
status: fase 1 e fase 2 implementadas; fase 2 redesenhada em 23/09/2026 (liberação nasce na confirmação da saída); verificação no navegador em andamento
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
saídas da origem. Isso **não** ganha serviço próprio: `ShipmentReleasesFromReturnService` passa a
receber a origem como parâmetro obrigatório. O rastreio do contrato (cadeia curta pela
`ShipmentReleaseKey`, cadeia longa por `SHIPPING_TRANSACTIONS`), o rateio por peso e o tratamento
do volume órfão são idênticos, e duplicá-los criaria duas fontes da mesma regra. Volume sem
contrato rastreável **não derruba** o registro: fica sem liberação e o motivo vai para o
`Comments` do 15, como já acontece na recusa.

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
(guards compartilhados) e `ShipmentLoadsRecalculateTransshippedService` (só a fórmula do quarto
termo). As liberações saem de `ShipmentReleasesFromReturnService`, que passa a receber a origem
como parâmetro.

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


## Fase 2 — transbordo em armazém próprio (IMPLEMENTADO; redesenhado em 23/09/2026)

Decisões do usuário em 22/09/2026, depois que a fase 1 foi verificada no navegador e o ramo de
armazém próprio se mostrou incompleto: ele apenas **vinculava** um `Receipt` já lançado, sem
creditar o armazém e sem emitir liberação, deixando a mercadoria **sem porta de saída** — o
operador só a embarcaria consumindo liberação de outro negócio. A fase 1 barrou a entrada nesse
fluxo (`ShipmentLoadTransshipmentRules.EnsureWarehouseAcceptsTransshipmentAsync`); esta fase o
substitui inteiro.

### 1. Por que o desenho é diferente do armazém de terceiro

Em armazém próprio existem **duas dimensões de saldo**, alimentadas por romaneios diferentes:

| Dimensão | Credita | Debita |
|---|---|---|
| **Lote** (`StorageAddress`) | `Receipt (0)` | `Shipment (1)`, `SalesShipment (7)` |
| **Armazém** | `Purchase (8)`, `TransshipmentReceipt (15)` | `SalesShipment (7)` |

É por isso que a Entrada em Armazenagem cria um **par** (8 credita o armazém, 0 credita o lote). O
transbordo em armazém próprio segue o mesmo princípio, com o 15 no lugar do 8 — aqui não há
contrato a debitar, porque ele já foi debitado na saída da origem.

### 2. O fluxo, com os dois papéis que o executam

O desenho reflete a organização do cliente: **o armazém movimenta o físico; o escritório emite os
documentos.** São dois momentos distintos, e o sistema não deve fundi-los.

| # | Onde | Quem | O que acontece | Efeito |
|---|---|---|---|---|
| 1 | Cadastro de lotes | escritório | cria o lote com natureza **Transbordo** | — |
| 2 | Pesagem | armazém | ticket de entrada, gerando `Receipt (0)` no lote de transbordo | **+ lote** |
| 3 | Carga, Registrar Entrada | escritório | vincula esse `Receipt` ao transbordo e cria o `TransshipmentReceipt (15)` | **+ armazém** |
| 4 | Pesagem | armazém | ticket de saída, gerando `Shipment (1)` do mesmo lote com o **peso real carregado** — e é a **confirmação desse romaneio** que vincula a saída ao transbordo e **emite a liberação** | **− lote** |
| 5 | Expedição de Grãos | escritório | cria a Expedição a partir dessa liberação, gerando `SalesShipment (7)` | **− armazém** |
| 6 | Carga, Vincular Romaneios | logística | vincula a Expedição no papel do transbordo | conclui o transbordo; **carga faturável** |

As duas dimensões terminam **iguais**, com a sobra em ambas (no exemplo do usuário: entram 50.000,
carregam 49.000, sobram 1.000 no lote **e** 1.000 no armazém). ⚠️ Versões anteriores deste
documento diziam "fecham em zero" — está errado: o grão da sobra está fisicamente no armazém, e o
total dele acompanha a soma dos lotes, do mesmo modo que a Entrada em Armazenagem comum cria o par
`8`+`0` pelo mesmo peso.

**Por que a liberação nasce na confirmação da saída, e não na entrada:** o peso que sai só é
conhecido no carregamento. Emiti-la na entrada, pelos 50.000, faria a sobra existir duas vezes —
como saldo de liberação e como saldo de lote. Nascendo pelos 49.000 reais, o resíduo tem um lugar
só.

**Por que na BALANÇA e não numa ação do escritório** (decisão do usuário em 23/09/2026, depois de
ver o fluxo anterior funcionando): a liberação é consequência direta de um fato físico — o grão
saiu do lote, pesado. Exigir que alguém do escritório repetisse esse fato num botão só adiava a
liberação e criava um estado em que o caminhão já partiu e o documento ainda não existe. **O botão
"Vincular Saída do Lote" foi removido**, junto com a action OData dele.

**Como a saída sabe a que transbordo pertence:** na confirmação, o sistema procura os transbordos
**abertos** cuja entrada está naquele lote e ainda sem saída vinculada, e desempata pelo
**caminhão** — o `TruckCode` do romaneio contra o da carga. Palavras do usuário: *"o mesmo caminhão
que entrou com a mercadoria vai sair com ela, o que vai variar é a quantidade."* Nenhum candidato,
ou mais de um, **recusa a confirmação** com mensagem acionável: sem isso a liberação nasceria na
carga errada, ou não nasceria e ninguém saberia.

**Por que o passo 5 não é automático** (decisão explícita do usuário): quem carrega é o armazém,
quem documenta é o escritório, e o escritório recebe a informação do embarque depois. A Expedição
de Grãos é o caminho que o escritório já usa para todo embarque, e é ela que alimenta faturamento,
CT-e e o resto.

### 3. O lote ganha uma natureza — campo novo, não valor novo em enum existente

`StorageAddressNature` (`Regular = 0`, `Transshipment = 1`), campo próprio em `StorageAddress`.

- `OwnershipType` responde **de quem é a mercadoria**; `Status`, **o ciclo de vida do lote**.
  Nenhum dos dois responde **para que serve** — e é essa a pergunta que "Transbordo" responde.
- Emprestar um valor de `OwnershipType` misturaria eixos ortogonais: um lote de transbordo tem
  dono de mercadoria como qualquer outro, e todo leitor que hoje decide por propriedade ganharia
  um caso que não é sobre propriedade.
- `Regular = 0` preserva todo lote existente sem backfill.

**Criação (decisão do usuário): manual, na tela de lotes.** A natureza é escolhida no cadastro, só
é oferecida quando o armazém tem `WarehouseComplement.IsOwn == true`, e **não muda depois** — é o
inverso exato da trava da fase 1.

### 4. Travas que o lote de transbordo obriga

- **Só o `Receipt` de um lote de transbordo** pode ser vinculado como entrada (passo 3), e só a um
  transbordo **aberto daquela carga**. O ramo da fase 1, que aceitava qualquer `Receipt` confirmado
  do armazém, é substituído por esta regra.
- **Só o `Shipment (1)` do MESMO lote** vale como saída do transbordo, e o vínculo acontece na
  confirmação do romaneio (passo 4), não por ação do escritório.
- **Saída de lote de transbordo sem transbordo aberto que a reivindique é RECUSADA** na
  confirmação (decisão do usuário): sem isso a mercadoria sairia da balança sem liberação e sem
  ninguém perceber. A recusa também cobre saldo insuficiente no lote, aproveitando a trava que a
  pesagem já aplica.
- **Lote de transbordo é invisível para a Expedição de Grãos comum** e para qualquer consulta que
  ofereça lote livre para expedir (`StorageAddressesListOpenedByItemService` e irmãs). Se
  aparecesse, alguém embarcaria o grão por fora e a carga ficaria esperando uma saída que já
  aconteceu.
- **Fora dos jobs de cobrança de armazenagem e de quebra técnica**
  (`StorageAddressesStorageChargeCalculatorService`, `StorageAddressesTechnicalLossCalculatorService`):
  é mercadoria em trânsito, não armazenagem contratada.
- Os demais leitores de saldo por lote (`StorageAddressesGetBalanceService`,
  `StorageAddressesDailyBalanceBuilderService`, `StorageAddressReportService`) **continuam
  enxergando** o lote — ele tem saldo real; o que muda é só não oferecê-lo como origem livre nem
  cobrá-lo.

### 5. A liberação emitida na confirmação da saída

Origem `ReleaseOrigin.Transshipment`, **sem lote** (o lote já foi debitado pelo `Shipment (1)`;
carregá-la com lote faria a Expedição drenar o lote uma segunda vez), quantidade igual ao
**`NetWeight`** da saída — a mesma grandeza que debitou o lote, e não o bruto, senão a sobra vai
parar na dimensão errada quando houver desconto de secagem —, **não consome contrato** (já debitado
na origem), uma por contrato rastreado a partir das saídas da origem — o mesmo rastreio e rateio de
`ShipmentReleasesFromReturnService`, com `GeneratedByStorageTransactionKey` apontando o
`Shipment (1)` que a originou.

### 6. Situações do transbordo

Ganha um estado intermediário em relação à fase 1: **Aguardando entrada, Aguardando saída,
Aguardando expedição, Concluído**. O `HasOpenTransshipmentAsync` continua correto sem mudança: ele
procura `SalesShipment` vinculado ao transbordo, então a carga permanece `InTransshipment` entre a
confirmação da saída e o vínculo da Expedição, que é o comportamento desejado.

⚠️ A passagem para **Aguardando expedição** acontece sozinha, quando o armazém confirma a pesagem
de saída — não depende de ninguém do escritório clicar.

**O volume da carga não muda de regra:** continua contando só `SalesShipment (7)`. O `Shipment (1)`
do lote é movimento físico de armazém, não volume faturável — o que torna desnecessária a mudança
em `ShipmentLoadsRecalculateTotalService` que a versão anterior deste desenho previa.

### 7. Estorno e cancelamento

- Estornar o transbordo depois da confirmação da saída e antes do vínculo da Expedição cancela a liberação (que ainda não tem
  consumo), desvincula o `Shipment (1)` e cancela o `15`.
- **A sobra do lote fica no lote** (decisão do usuário), que **mantém a natureza Transbordo** e
  segue invisível para a Expedição comum. Ela só sai vinculada a outra carga, num transbordo novo.
- O `Shipment (1)` e o `Receipt (0)` do lote **não são cancelados** pelo estorno: são movimento
  físico pesado na balança, com ciclo próprio.

### Fora de escopo desta fase 2

Transbordo de terceiro num lote (`Nature = Transshipment` fora de armazém próprio) e múltiplos
ciclos de entrada e saída no mesmo lote antes de fechar o transbordo.
