# Retorno parcial com destino "novo destino"

Data: 08/09/2026

## Problema

No diálogo **Retornar** de `/sales-invoices`, escolher o destino "Caminhão segue para novo
destino" trava a coluna `Qtde a Devolver`. O bloqueio é deliberado e existe em duas camadas:

- Frontend — `Return.fragment.xml` amarra `editable` a `${return>/DestinationIndex} === 1`.
- Backend — `SalesInvoicesReturnService.ResolveQuantity` recusa quantidade diferente do
  `NetWeight` quando o destino não é `Warehouse`.

O motivo original: em `Rebilling` o romaneio volta INTEIRO ao pool de faturamento, e devolvê-lo
pela metade des-faturaria o volume que ficou com o cliente. Representar meia carreta livre exigiria
partir o registro em dois — deixado fora de escopo em 02/09/2026.

O caso real que o bloqueio impede: romaneio de 40t faturado, o cliente recebe 15t e recusa 25t, e o
caminhão segue para outro destino com as 25t. Hoje o operador só pode devolver as 40t inteiras.

## Escopo

Liberar o retorno parcial por quantidade no destino `Rebilling` do **retorno de documento de saída
legado**, partindo o romaneio em dois.

A **recusa de carga** (`ShipmentLoadsRefuseService`) já aceita quantidade parcial nos dois destinos
— não há trava de destino no serviço nem no `Refusal.fragment.xml`, e o campo de quantidade da
grade é editável sempre. Nada a desenhar ali; a tela será conferida no navegador durante a
verificação e, se estiver travada na prática, o defeito entra como correção.

## Decisão de desenho

Depois de confirmar o retorno parcial com novo destino, o operador precisa de **um romaneio novo
com as 25t**, livre para faturar a outro cliente, e o romaneio de origem fica com as 15t que o
cliente reteve, ainda faturadas na nota original.

### Abordagens consideradas

| | Abordagem | Veredito |
|---|---|---|
| A | Dividir o romaneio: origem encolhe para 15t, nasce irmão de 25t | **Escolhida** |
| B | Origem intacta em 40t + irmão de 25t excluído das consultas de saldo até ser faturado | Recusada |
| C | Cancelar a origem e criar dois romaneios novos (15t faturado + 25t livre) | Recusada |

**Por que A.** Todas as fórmulas de saldo do sistema — armazém, endereço/lote, liberação — são
somatórios por tipo de transação. Um split 15 + 25 preserva cada uma delas, então nenhum serviço
de saldo muda.

**Por que não B.** O irmão precisaria contar em umas consultas e não em outras enquanto não fosse
faturado, senão o armazém é debitado em 65t. Isso exige acertar de forma consistente os seis
serviços de saldo que o `remarks` de `ShipmentLoadsRefuseService.ReturnToWarehouseAsync` já lista
como checklist — o tipo de mudança que quebra em silêncio meses depois.

**Por que não C.** O romaneio das 15t ganharia código novo, mas a NF-e já emitida aponta o antigo;
e o par `ShippingTransaction` mais a alocação do contrato de compra teriam de ser refeitos.

## Componentes

| Camada | Arquivo | Mudança |
|---|---|---|
| Domínio | `StorageTransaction` | coluna `SplitFromStorageTransactionKey` |
| Aplicação | `StorageTransactionsSplitService` (novo) | parte um romaneio de venda em origem + irmão |
| Aplicação | `SalesInvoicesReturnService` | classifica linha cheia × parcial; chama o split |
| Aplicação | `SalesInvoicesReverseConfirmService` | desfaz a divisão; corrige o discriminador `isNewFlow`; escopa `CancelWarehouseReturnAsync` |
| Aplicação | `ShippingTransactionsReverseService` | guard: não estorna Expedição de romaneio dividido |
| Migrations | uma migration | coluna + FK auto-referente `NoAction` |
| Frontend | `Return.fragment.xml` | quantidade editável nos dois destinos |
| Frontend | `salesInvoices/Main.controller.ts` | remove o reset de quantidade na troca de destino; confirmação própria do parcial |

`StorageTransactionsSplitService` é serviço próprio, e não método privado do
`SalesInvoicesReturnService`: aquele arquivo já tem 542 linhas, e a divisão é uma operação sobre
romaneio, com pré-condições e invariante próprios, testável isoladamente.

## A divisão, campo a campo

Entrada: romaneio de origem, quantidade devolvida `q`, chave da nota de retorno, usuário.
Pré-condições, todas recusadas com mensagem que nomeia o romaneio:

- tipo `SalesShipment` e status `Invoiced`;
- `q` maior que zero e menor que `NetWeight` menos a tolerância de 0,001 (igual ao `NetWeight` não
  é split, é retorno cheio);
- `ShipmentLoadKey` nulo — romaneio dentro de carga tem a tela de recusa própria.

### Origem — vira 15t e continua faturada na nota original

- `GrossWeight` e `NetWeight` = `W − q`. Os dois andam juntos porque
  `ExecuteSalesShipmentTransactionAsync` força `NetWeight = GrossWeight` em todo confirm de
  romaneio de venda.
- `DryingDiscount`, `CleaningDiscount`, `OthersDicount` rateados na proporção `(W − q) / W`,
  arredondados a 3 casas, com a sobra ficando na origem.
- `InvoiceQty` = `W − q`. `IsInvoiced` continua `true`, `SalesInvoiceKey` continua apontando a nota
  e o status continua `Invoiced`.
- **Não** recebe `ReturnInvoiceKey`, `ReturnedAt` nem `ReturnedBy`: ela não foi devolvida, parte
  dela foi.
- `Comments` ganha a linha da divisão, nomeando o irmão e a quantidade.

### Irmão — 25t livres

Montado pelo `StorageTransactionCopyFactory`, que já herda armazém, `StorageAddressCode`,
`ShipmentReleaseKey`, placa, motorista, produto e unidade, e já zera `SalesInvoiceKey` e
`ShipmentLoadKey`. Ajustes por cima:

- `Code` novo pela sequência (`TransactionCode.StorageTransaction`).
- `GrossWeight` = `NetWeight` = `q`; descontos com o rateio complementar.
- Nasce `Pending` e é confirmado por `StorageTransactionsConfirmedService` com
  `isShipmentTransaction: true`. O flag pula a validação "quantidade embarcada superior ao saldo
  disponível no armazém", que aqui não se aplica: o grão já saiu do armazém, contado pela origem, e
  a origem acabou de encolher pelo mesmo tanto. Sem o flag o resultado dependeria da ordem exata
  entre encolher a origem e confirmar o irmão — fragilidade desnecessária.
- `SalesShipmentReleaseKey` nulo: o consumo da liberação de venda é gravado no faturamento, e o
  irmão ainda não foi faturado.
- `WeighingTicketKey` **nulo**. `WeighingTicketsCancelService` e `WeighingTicketsReOpenService`
  chegam ao romaneio da pesagem por `FirstOrDefault(x => x.WeighingTicketKey == key)`; dois
  romaneios apontando o mesmo ticket fariam cancelar/reabrir escolher o errado.
- `SplitFromStorageTransactionKey` = chave da origem.
- `GeneratedByReturnInvoiceKey` = chave da nota de **retorno** (não a de origem — a nota pode ser
  retornada em parcelas, e o estorno de uma delas precisa achar exatamente os irmãos dela).
- `Comments` nomeando origem, retorno e motivo.

### Chaves que o irmão NÃO pode carregar

- `SalesInvoiceKey` — é o que `ShipmentBillingTransactionGuardService` lê para recusar
  refaturamento, antes mesmo de olhar o status. Com ela o irmão não reaparece no pool, que é todo o
  objetivo.
- `ShipmentLoadKey` — `ShipmentLoadsRecalculateTotalService` soma `GrossWeight` das transações da
  carga; o irmão inflaria o total de uma carga que ele não integra.
- `ReturnInvoiceKey` — é o discriminador `isNewFlow` de `SalesInvoicesReverseConfirmService`, e um
  estorno carimbaria o irmão como `Invoiced` re-anexando-o à nota de origem.

### O invariante

`15 + 25 = 40` em `StorageTransactionsWarehouseBalanceService.CalculateAsync`, nas consultas de
saldo por endereço e no eixo de venda de
`ShipmentReleasesRecalculateShippedService.CalculateShippedAsync`. É o que dispensa mexer em
qualquer serviço de saldo.

⚠️ O `ShipmentReleaseKey` herdado é condição do invariante, não conveniência: em liberação de
origem `OwnershipTransfer` ou `SalesReturn` o consumo é medido pelo eixo de venda
(`SalesShipment − SalesShipmentReturn`). Sem a chave no irmão, encolher a origem devolveria 25t de
saldo à liberação como se o grão não tivesse saído.

## Fluxo do retorno

`ResolveQuantity` para de recusar parcial em `Rebilling`. Cada linha passa a ser classificada:

- **cheia** — quantidade omitida, ou dentro da tolerância do `NetWeight`;
- **parcial** — quantidade estritamente menor.

Em `Warehouse` nada muda: as duas classes seguem o caminho atual.

Em `Rebilling`:

1. Só as linhas **cheias** entram no dicionário `shipmentOutcomes`, com `Confirmed` — o caminho de
   hoje, que solta o romaneio da nota.
2. A linha **parcial fica fora do `shipmentOutcomes` de propósito**. O bloco que aplica os outcomes
   em `SalesInvoicesConfirmService` zera `IsInvoiced` e `InvoiceQty` e carimba `ReturnInvoiceKey`
   em todo romaneio listado — exatamente o oposto do que a origem parcial precisa, que é continuar
   faturada por `W − q`.
3. Depois do confirm da nota de retorno, cada linha parcial passa por
   `StorageTransactionsSplitService`.

### Correção obrigatória no discriminador do estorno

`SalesInvoicesReverseConfirmService` decide o ramo por
`any(StorageTransactions.ReturnInvoiceKey == invoice.Key)`. Num retorno **100% parcial** nenhum
romaneio carrega essa chave (nenhum entrou no `shipmentOutcomes`), e o estorno cairia no ramo
LEGADO — aquele cuja consulta de órfãos, casada só por `CardCode`/`ItemCode`, já sequestrou
romaneio alheio uma vez.

`isNewFlow` passa a ser:

```
ReturnInvoiceKey == invoice.Key  OU  GeneratedByReturnInvoiceKey == invoice.Key
```

A segunda condição já é verdadeira para todo retorno com destino armazém (o romaneio tipo 12 nasce
com ela), então a mudança não altera nenhum fluxo existente — só cobre o retorno parcial puro.

## Estorno

`ReverseNewReturnAsync` ganha um passo de desfazer a divisão, executado **antes** de qualquer
outra escrita:

1. Busca os irmãos: `GeneratedByReturnInvoiceKey == returnInvoice.Key` **e**
   `SplitFromStorageTransactionKey != null` **e** `TransactionType == SalesShipment` **e** status
   diferente de `Cancelled`.
2. **Recusa** o estorno inteiro se algum irmão não estiver mais livre, com mensagem que nomeia onde
   ele está:
   - `SalesInvoiceKey != null` → "já foi faturado no documento X";
   - `ShipmentLoadKey != null` → "já está montado na carga Y";
   - status diferente de `Confirmed`, ou o irmão já dividido/devolvido → "já foi movimentado".
3. Se todos livres: devolve `GrossWeight`, `NetWeight`, descontos e `InvoiceQty` à origem e marca o
   irmão `Cancelled`.

O irmão é **cancelado, não apagado**: o `Code` já saiu da sequência e apagar o registro apaga a
auditoria da divisão. `Cancelled` já está fora de toda consulta de saldo, então a origem volta a
valer 40t sozinha.

O laço que já existe em `ReverseNewReturnAsync` — o que re-anexa à nota de origem os romaneios com
`ReturnInvoiceKey == returnInvoice.Key` — **não tem nada a fazer numa linha parcial**, e é isso que
se espera: a origem nunca foi solta da nota nem carimbada com a chave do retorno. Num retorno 100%
parcial esse laço roda vazio, e todo o trabalho do estorno está no passo de desfazer a divisão.

`CancelWarehouseReturnAsync` passa a filtrar `TransactionType == SalesShipmentReturn`. Hoje ela
busca só por `GeneratedByReturnInvoiceKey`, e o irmão passa a usar a mesma coluna — sem o filtro,
o estorno o cancelaria como se fosse uma devolução ao armazém.

## Guard fora do retorno

`ShippingTransactionsReverseService` passa a recusar o estorno da Expedição de um romaneio que foi
dividido — isto é, que tenha irmão vivo apontando para ele por
`SplitFromStorageTransactionKey`.

Depois do split o par compra/venda deixa de bater 1:1: o `ShippingTransaction` cobre 40t e a perna
de venda vale 15t. Estornar por ali devolveria contrato de compra e saldo de armazém pelo valor
errado, em silêncio. É a mesma família do guard que já existe para romaneio `Returned`.

## Migration

Uma migration, aplicada com `dotnet ef database update` e `ASPNETCORE_ENVIRONMENT` explícito:

- `SPLIT_FROM_STORAGE_TRANSACTION_KEY` em `STORAGE_TRANSACTIONS`, `uniqueidentifier NULL`;
- FK auto-referente para `STORAGE_TRANSACTIONS`, `OnDelete` **`NoAction`** — `Restrict` e
  `NoAction` geram o mesmo DDL mas snapshots diferentes; usar exatamente o que o resto da tabela
  usa evita migration inerte depois;
- índice não-único na coluna (o estorno busca por ela).

## Tela

`Return.fragment.xml`:

- `Qtde a Devolver` editável nos dois destinos;
- o texto do toolbar deixa de alternar por destino;
- o teto por linha continua o `NetWeight` do romaneio.

`salesInvoices/Main.controller.ts`:

- some o reset de quantidade ao trocar de destino;
- as quantidades passam a ser enviadas também em `Rebilling` (o array `Quantities` já existe no
  EDM como `Collection(Edm.Double)` opcional e paralelo às chaves — **nunca** `Edm.Decimal`, que o
  UI5 serializa como string e o backend recusa com 400 sem nomear o campo);
- confirmação própria quando houver linha parcial com novo destino:
  *"O romaneio 00001571 será dividido: 15.000 seguem faturados neste documento e 25.000 voltam a
  ficar disponíveis num romaneio novo."* A divisão é irreversível depois que o irmão for usado, e o
  operador precisa ler isso antes.

## Testes

TDD em `SiagroB1.Application.Tests`, cada um vermelho antes de verde:

1. parcial + `Rebilling` divide: origem `W − q` `Invoiced` na nota, irmão `q` `Confirmed` solto;
2. saldo de armazém não muda depois do split;
3. saldo da liberação não muda depois do split, em liberação de origem `OwnershipTransfer`;
4. linha cheia + `Rebilling` continua no caminho antigo (`Confirmed`, solta), sem irmão;
5. misto na mesma nota: um romaneio cheio e um parcial;
6. retorno 100% parcial cai no ramo novo do estorno, não no legado;
7. estorno com irmão livre funde de volta e a origem volta ao peso original;
8. estorno com irmão já faturado é recusado nomeando a nota;
9. estorno com irmão anexado a carga é recusado nomeando a carga;
10. `CancelWarehouseReturnAsync` não cancela o irmão;
11. `ShippingTransactionsReverseService` recusa romaneio dividido;
12. split recusa romaneio com `ShipmentLoadKey`;
13. regressão: parcial + `Warehouse` continua idêntico ao comportamento atual.

⚠️ A guarda de excesso usa tolerância de 0,001. Um teste que devolve `NetWeight + 0.001` **passa**
pela guarda — para provar a recusa, usar valor claramente acima.

## Verificação no navegador

Stack local no perfil `yktb` (Web + Gateway no mesmo ambiente) e `yarn start:dev`. Roteiro:

1. Retorno parcial com novo destino num documento de saída legado: conferir origem encolhida e
   faturada, irmão novo com o restante, e o irmão aparecendo no Faturamento de Expedição e na
   Montagem de Carga.
2. Estorno do retorno com o irmão ainda livre: origem volta ao peso cheio, irmão `Cancelled`.
3. Estorno com o irmão já faturado: recusado com a mensagem que nomeia a nota.
4. Recusa de carga com novo destino e quantidade parcial: confirmar que o campo aceita digitação
   (esperado pelo código; se estiver travado, é bug e entra como correção).

## Fora de escopo

- **Acumulador de devolução por romaneio no destino `Warehouse`.** O teto por linha continua sendo
  o `NetWeight` cheio, sem descontar retornos anteriores daquele romaneio; quem impede o excesso é o
  teto por ITEM. O split resolve isso naturalmente em `Rebilling` (a origem encolhe), mas mudar o
  destino armazém é outra feature.
- Endereçamento do irmão em nível de endereço/lote além do que o clone já herda.
- Extrato de Armazenagem, que segue sem enxergar `SalesShipmentReturn`.
- Recusa de carga: nenhuma mudança de desenho.
