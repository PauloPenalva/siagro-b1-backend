# Comentários e Log de Alterações na Montagem de Carga

Data: 2026-09-09
Repos: `siagro-b1-backend`, `siagro-b1-frontend`

## Problema

A Carga (`ShipmentLoad`) é hoje o documento-pivô do fluxo de saída, e é editada por várias mãos ao
longo da viagem: a Logística planeja, o motorista troca, a transportadora muda, o frete é
renegociado, a carga é recusada e redestinada. Duas lacunas:

1. **Não há onde anotar.** O único campo livre é `ShipmentLoad.Comments`, um escalar de
   "Observações" do cabeçalho que a última pessoa a editar sobrescreve. Não tem autor, não tem
   data e não guarda a anotação anterior.
2. **Não há rastro de quem alterou o quê.** O `ShipmentLoadsUpdateService` já compara campo a
   campo, mas concatena tudo numa única linha de narrativa em `SHIPMENT_LOAD_MOVEMENTS`
   (`MovementType.Updated`, descrição `Dados da carga alterados: Veículo: ... para ...`).
   Isso é legível, mas não é consultável: não dá para filtrar por campo, não dá para ver a
   história de um campo só, e o texto compete por espaço na mesma grade que o financeiro lê
   para conferir frete.

Contratos de compra e venda, documento de saída e documento de entrada já resolveram exatamente
isto — coleção `CommentEntries` + tabela de log campo a campo. A Carga é a única entidade do fluxo
sem os dois.

## Decisão de escopo

O log da Carga registra **o que o usuário digita no cadastro**:

- edição dos campos do formulário (`ShipmentLoadsUpdateService`);
- inclusão, edição e exclusão de comentário;
- cancelamento da carga (situação e motivo).

O log **não** registra vinculação/desvinculação de romaneio, faturamento, recusa nem devolução.
Esses continuam exclusivamente em `SHIPMENT_LOAD_MOVEMENTS`.

A fronteira é: **log = o que foi digitado; Movimentação = o que aconteceu com o saldo.** Duas
grades contando a mesma história divergiriam com o tempo.

### O que NÃO muda

A linha de Movimentação `Updated` continua sendo gravada como hoje, com a mesma descrição
concatenada. É comportamento já verificado no navegador, e a Movimentação é a linha do tempo que a
Logística lê. O log acrescenta a granularidade que a narrativa não tem; não a substitui. A
redundância é deliberada e conhecida.

## Modelo de dados

Duas tabelas novas, uma migration (`AddShipmentLoadCommentsAndChangeLogs`).

### `SHIPMENT_LOADS_COMMENTS` — `ShipmentLoadComment`

| Coluna | Tipo | Observação |
|---|---|---|
| `Key` | `uniqueidentifier`, identity | PK |
| `ShipmentLoadKey` | `uniqueidentifier` | FK `NoAction`, como todas do projeto |
| `CommentedAt` | `datetime2` | Data/hora da ÚLTIMA escrita — sobrescrita na edição |
| `CommentedBy` | `VARCHAR(100)` | Autor da última escrita |
| `CommentText` | `VARCHAR(500) NOT NULL` | 500 para casar com `OldValue`/`NewValue` do log |

Nav property: **`ShipmentLoad.CommentEntries`**, não `Comments`. O escalar `ShipmentLoad.Comments`
já é a "Observações" do cabeçalho e continua existindo. Mesmo conflito de nome e mesma solução do
contrato e dos documentos de saída/entrada. Renomear o escalar está descartado.

### `SHIPMENT_LOADS_CHANGE_LOGS` — `ShipmentLoadChangeLog`

Estrutura idêntica a `PURCHASE_INVOICES_CHANGE_LOGS` / `SALES_INVOICES_CHANGE_LOGS`:

| Coluna | Tipo |
|---|---|
| `Key` | `uniqueidentifier`, identity |
| `ShipmentLoadKey` | `uniqueidentifier` |
| `ChangedAt` | `datetime2` |
| `ChangedBy` | `VARCHAR(100)` |
| `Field` | `VARCHAR(50) NOT NULL` — código, não rótulo |
| `OldValue` | `VARCHAR(500)` — nulo = inclusão |
| `NewValue` | `VARCHAR(500)` — nulo = remoção |

Nav property: `ShipmentLoad.ChangeLogs`.

Índice em `ShipmentLoadKey` nas duas tabelas: as duas são sempre lidas filtrando por carga.

## Backend

### `ShipmentLoadChangeLogFields`

Classe estática nova em `SiagroB1.Domain/Entities/`, com os códigos gravados na coluna `Field`.
Contrato com a tela: o formatter do frontend traduz cada código para pt-BR.

- **`Comment`** — reusa literalmente a mesma string dos contratos e documentos. É o que permite ao
  código compartilhado continuar funcionando sem tocar em `ContractChangeLogFields`.
- Campos do formulário: `LoadDate`, `TruckCode`, `TruckDriver`, `Carrier`, `CardCode`,
  `Warehouse`, `Item`, `UnitOfMeasure`, `Branch`, `HasExcess`, `FreightPrice`, `Comments`
  (a Observação escalar do cabeçalho — plural, distinto do `Comment` singular).
- Cancelamento: `Status`, `CancellationReason`.

Helpers de descrição ficam aqui, e não num formatter do frontend, pelo mesmo motivo dos contratos:
`OldValue`/`NewValue` são texto livre e a tela não teria como saber que aquela linha é uma data, um
booleano ou um decimal. Ficam quatro: `DescribeDate` (`dd/MM/yyyy`), `DescribeBoolean`
(`Sim`/`Não`), `DescribeFreightPrice` (`N2` em pt-BR) e `DescribeStatus` (situação da carga em
pt-BR).

### `ShipmentLoadsChangeLogService`

Porta única de escrita, espelho de `PurchaseContractsChangeLogService`: `Register(loadKey, field,
oldValue, newValue, userName)` apenas enfileira a linha no contexto. Quem chama decide quando
salvar, para que o log e a alteração que ele descreve entrem no mesmo `SaveChanges` — nunca sobra
log de uma alteração que falhou, nem alteração sem log. Trunca em 500 caracteres, porque um texto
longo não pode derrubar a gravação da alteração que o log só acompanha.

### `ShipmentLoadsUpdateService`

`DescribeChanges` deixa de devolver `List<string>` e passa a devolver
`List<(string Field, string Label, string? Old, string? New)>`.

- Do mesmo resultado saem as duas coisas: a descrição concatenada da Movimentação (montada a
  partir de `Label`, `Old` e `New`, preservando o texto atual byte a byte) e **uma linha de log
  por campo alterado**.
- Continua sendo calculado ANTES de a entidade rastreada ser sobrescrita — depois da atribuição
  não haveria mais com o que comparar.
- Campo travado por status fiscal não gera linha, porque `EnsureFiscalFieldsUnchanged` já recusou
  a alteração antes.
- Nenhuma alteração ⇒ nenhuma linha de log e nenhuma linha de movimentação, como hoje.

O `DescribeChanges` atual compara oito campos e trata data, excesso e frete à parte. A lista de
campos logados passa a ser a lista completa do formulário — inclusive `TruckDriverCode`,
`CarrierCardCode` e `UnitOfMeasureCode`, hoje comparados só pelo nome.

### `ShipmentLoadsCancelService`

Duas linhas de log dentro da transação que já existe: `Status` (situação anterior → `Cancelada`) e
`CancellationReason` (`null` → motivo). A linha de Movimentação `Cancelled` continua como está.

### Comentários

Quatro serviços em `SiagroB1.Application/Services/ShipmentLoads/`, espelhos diretos dos de
`PurchaseInvoices`:

- `ShipmentLoadsCommentCreateService(loadKey, text, userName)`
- `ShipmentLoadsCommentUpdateService(commentKey, text, userName, isAdmin)`
- `ShipmentLoadsCommentDeleteService(commentKey, userName, isAdmin)`
- `ShipmentLoadsCommentsGetService.QueryAll(loadKey)` — `IQueryable`, `AsNoTracking`,
  `OrderByDescending(CommentedAt)`

Regras reusadas de `ContractCommentRules` **sem alteração de comportamento**: texto obrigatório,
máximo 500 caracteres (recusa em vez de truncar), e só o autor altera ou exclui o próprio
comentário — administrador pode qualquer um. O nome da classe é histórico; ela já serve quatro
entidades.

**Sem guarda de status.** Comentário é anotação: não altera valor, peso nem saldo. Vale em carga
`Cancelled`, onde registrar o motivo é o uso mais comum. O
`FinishedContractMutationGuardInterceptor` não alcança a entidade — ele só olha filhos de contrato.

Editar reescreve `CommentedAt` e `CommentedBy`: a linha mostra a última alteração, e a versão
anterior sobrevive no log.

Mais um serviço de leitura: `ShipmentLoadsChangeLogsGetService.QueryAll(loadKey)`, mesma forma,
ordenado por `ChangedAt` decrescente.

### Web

**Actions** (escrita por action, não por POST/PATCH na coleção aninhada: garante mutação + log no
mesmo `SaveChanges` e desvia do update group diferido do Detail):

| Action | Parâmetros |
|---|---|
| `ShipmentLoadsCommentCreate` | `LoadKey: Guid`, `Text: string` |
| `ShipmentLoadsCommentUpdate` | `Key: Guid`, `Text: string` |
| `ShipmentLoadsCommentDelete` | `Key: Guid` |

Cada uma com seu controller em `SiagroB1.Web/Actions/ShipmentLoads/`. Dois cuidados que já
morderam neste projeto: `ODataActionParameters` chega **nulo** quando nenhum parâmetro do EDM é
enviado (NRE = 500 de corpo vazio), e parâmetro `string` do OData é anulável mesmo quando
`TryGetValue` devolve `true` — o `as string` do padrão existente cobre os dois.

**Leitura**: dois controllers em `SiagroB1.Web/Controllers/`, com as rotas declaradas à mão nas
duas formas (parêntese e barra), porque navegação não declarada devolve 404 aqui:

```
odata/ShipmentLoads({key:guid})/CommentEntries
odata/ShipmentLoads/{key:guid}/CommentEntries
odata/ShipmentLoads({key:guid})/ChangeLogs
odata/ShipmentLoads/{key:guid}/ChangeLogs
```

**EDM**: `modelBuilder.EntitySet<ShipmentLoadComment>("ShipmentLoadsComments")` e
`EntitySet<ShipmentLoadChangeLog>("ShipmentLoadsChangeLogs")`, junto das actions.

**DI**: os seis serviços novos em `ServiceCollectionExtensions`.

## Frontend

### Fragmentos

Três novos em `webapp/view/shipmentLoads/fragments/`:

- **`ShipmentLoadComments.fragment.xml`** — `sap.ui.table.Table` somente leitura, seleção `Single`,
  colunas Data/Hora, Usuário e Comentário, com toolbar Incluir / Editar / Remover. Binding:

  ```
  rows="{ path: 'CommentEntries',
          parameters: { '$$ownRequest': true },
          sorter: { path: 'CommentedAt', descending: true } }"
  ```

  `$$ownRequest` é obrigatório: sem ele o binding vira `$expand` no GET da carga, e o
  `ShipmentLoadsGetService` não inclui a coleção — a tabela viria vazia. O `sorter` também é
  obrigatório, por menos óbvio: o `OrderByDescending` do serviço só vale na consulta sem paginação;
  a `sap.ui.table` pede `$skip`/`$top` e o `[EnableQuery]` reordena por chave para estabilizar a
  paginação. `targetType: 'any'` na data, porque o `datetime2` chega com 7 casas de fração de
  segundo que o `DateTimeOffset` do UI5 recusa. `wrapping="false"` no texto, senão um comentário
  longo estoura a altura da linha; o texto inteiro fica no tooltip.
- **`ShipmentLoadCommentDialog.fragment.xml`** — `TextArea` de 6 linhas, `maxLength="500"`, ligado a
  um **buffer JSON** (`viewModel>/commentDialog/text`), nunca ao contexto OData: two-way binding num
  Detail deixa PATCH pendente no update group diferido e derruba o batch inteiro.
- **`ShipmentLoadChangeLogs.fragment.xml`** — tabela somente leitura: Data/Hora, Usuário, Campo
  (via formatter), De, Para. Mesmo `$$ownRequest` + `sorter` por `ChangedAt` decrescente.

### `Detail.view.xml`

Duas `ObjectPageSection` novas depois de "Movimentação": **"Comentários"** e
**"Log de Alterações"**, cada uma com o fragmento correspondente. Nenhuma alteração nas seções
existentes. Nada no Panel: ele é um painel diário somente leitura, e clicar num cartão já abre o
Detail.

Nenhum `--` dentro de comentário XML nos fragmentos novos: `Fragment.load` devolve nulls em
silêncio e nenhum gate acusa.

### `Detail.controller.ts`

Handlers espelhando `purchaseInvoices/Detail.controller.ts`: `onAddComment`, `onEditComment`,
`onRemoveComment`, `onConfirmComment`, `onCloseCommentDialog`, mais os privados
`selectedCommentContext`, `canModifyComment`, `prepareCommentDialog`, `openCommentDialog`,
`refreshCommentsList`.

`canModifyComment` compara `CommentedBy` com `sessionModel>/userName` e libera se
`sessionModel>/isAdmin` — os dois já são publicados pelo `SessionService`. É verificação de
conveniência: a recusa de verdade é do servidor.

Depois de cada action, refresh isolado das duas tabelas (comentários e log), nunca do binding do
elemento inteiro.

### Outros

- `ServerRoutes.ts`: três rotas novas.
- `formatter.ts`: `formatShipmentLoadChangeLogField`, mapeando os códigos de
  `ShipmentLoadChangeLogFields` para os rótulos em pt-BR — os mesmos rótulos que o formulário da
  carga já usa (Veículo, Motorista, Transportadora, Cliente, Armazém, Produto, Filial, Excesso,
  Valor do Frete, Observações, Comentário, Situação, Motivo do Cancelamento).

## Testes

xUnit + EF InMemory em `SiagroB1.Application.Tests/ShipmentLoads/`:

**`ShipmentLoadsCommentTests`**

- inclui comentário com autor e data carimbados;
- recusa texto vazio ou só espaços;
- recusa texto acima de 500 caracteres (recusa, não trunca);
- editar reescreve texto, data e autor, e registra `Comment` no log com o texto anterior em
  `OldValue`;
- excluir registra `Comment` com o texto em `OldValue` e `NewValue` nulo;
- não-autor não edita nem exclui; admin edita e exclui o comentário de outro;
- comentário em carga `Cancelled` é aceito.

**`ShipmentLoadsChangeLogTests`**

- update com três campos alterados grava exatamente três linhas, uma por campo, com `De`/`Para`
  corretos;
- update sem alteração nenhuma não grava linha (nem de log, nem de movimentação);
- a descrição da linha de Movimentação `Updated` continua idêntica à atual (teste de regressão
  sobre o texto concatenado);
- cancelamento grava `Status` e `CancellationReason`.

**`ShipmentLoadModelTests`** ganha a verificação das duas tabelas e das duas nav properties.

## Riscos e cuidados

- **Migration**: aplicar com `ASPNETCORE_ENVIRONMENT` explícito. O profile `db-migration` aponta
  para produção por padrão.
- **Staging**: `git add` de cada arquivo novo, no sub-repo certo. Backend e frontend são dois
  repos — a mudança é dois conjuntos de staging, nunca um. **Nenhum commit**: os commits são
  manuais.
- **Verificação**: pelo caminho do usuário — abrir a Carga a partir do menu, não testar o endpoint
  por curl. No ambiente Yokotobi o value help do SAP devolve 500 e a tela de Liberações estoura
  30s; nenhum dos dois é regressão desta feature.
