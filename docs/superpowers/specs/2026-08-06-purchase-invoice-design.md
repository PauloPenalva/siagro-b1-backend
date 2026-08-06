# Documento de Entrada — Fase 1: estrutura e absorção da Devolução de Cliente

## Context

A Devolução de Cliente (`CUSTOMER_RETURNS`, 04/08/2026) nasceu como entidade própria e quebrou o
padrão que o Documento de Saída havia firmado dois dias antes: `SalesInvoice` mantém nota normal e
devolução **na mesma tabela**, separadas por `SalesInvoiceType { Normal, Return }`. Duas rotinas para
o mesmo conceito fiscal — documento que ENTRA — é o que se quer desfazer.

Além de destoar, a rotina está inacabada: `CustomerReturnsUpdateService` atualiza só o cabeçalho e
nunca toca `existing.Items`, e o `Detail` é `editable=false` — hoje não existe caminho para amarrar
uma linha depois de gravar.

O roadmap fiscal (`2026-08-04-general-purpose-sales-invoice-design.md`, "Sequência") já previa
**"Documento de entrada — NF de terceiro e emissão própria; reusa `USAGES`, acrescenta os CFOPs de
entrada e a manutenção fiscal do contrato de compra"** como sub-projeto 2. Este spec o inicia,
absorvendo a devolução.

Resultado esperado ao fim das três fases: **duas rotinas fiscais**, Documento de Saída e Documento de
Entrada, cobrindo NF de fornecedor, compra de mercadoria para comercialização, venda futura do
produtor rural com suas remessas, insumo/serviço/imobilizado e devolução de cliente.

## A decisão central: dois tipos, e a variedade mora na natureza de operação

O usuário levantou quatro cenários de entrada além da devolução:

1. NF de entrada genérica (insumo, serviço, imobilizado)
2. Compra de mercadoria para comercialização — amarra contrato de compra, **não gera romaneio**
3. Venda futura: o produtor rural emite NF de faturamento antecipado para receber antes de entregar
4. Remessa: a cada carregamento o produtor emite NF referenciando a NF futura

A tentação é criar um tipo por cenário. **Não.** `PurchaseInvoiceType` fica com `{ Normal, Return }`,
idêntico ao da saída, e os cenários viram configuração de `UsageEffect` — que já tem exatamente os
campos necessários e **não muda de forma**:

| Cenário | `ContractBalanceEffect` | `ContractValueEffect` | `RequiresContract` |
|---|---|---|---|
| Mercadoria para comercialização | `None` | `None` | sim |
| Venda futura (faturamento antecipado) | `None` | `Add` | sim |
| Remessa por carregamento | `None` | `None` | sim |
| Devolução de cliente | `None` | `None` | não |
| Insumo / serviço / imobilizado | `None` | `None` | não |

Consequência prática: **fluxo fiscal novo é cadastro, não migration**. É o mesmo raciocínio que o
documento de saída avulso usou ao rotear por ORIGEM em vez de por campo.

`ContractBalanceEffect` fica `None` em todos os cenários do dia a dia por decisão do usuário: **o
saldo físico continua sendo movido só pelo romaneio**, e a amarração ao contrato serve para
conciliação fiscal. O mecanismo de mover existe (Fase 3), desligado por configuração.

## Escopo desta fase

**Entrega:** a devolução funcionando dentro do Documento de Entrada, mais a entrada tipo `Normal` como
registro com importação de XML no nível atual. Verificável no navegador antes de seguir.

Fases seguintes, cada uma com spec próprio:

- **Fase 2 — camada fiscal.** CFOPs de entrada em `USAGES`, natureza de operação por linha,
  `CfopResolve`, `UsageGuard`, campos fiscais completos + `ItemFiscalDialog`, XML lendo CFOP/NCM/CST/
  impostos e `ide/NFref` (que resolve remessa → NF futura sozinho).
- **Fase 3 — efeito e conciliação.** `PurchaseContractKey` e `StorageTransactionKey` no item, coluna
  de divergência NF × romaneio, `UsageEffect` aplicado sobre `PURCHASE_CONTRACTS_ALLOCATIONS` (que
  ganha `Origin` e `PriceDifference`, hoje só existentes do lado de venda), e numerador `DocNumbers`
  automático para emissão própria.

## Modelo de dados

### `PURCHASE_INVOICES` (nova)

Herda `DocumentEntity` — que traz `DocNumberKey`, `BranchCode`, `Branch` além do `BaseEntity`. É a
mesma base de `SalesInvoice`, e é o que dá filial ao documento desde já.

| Coluna | Tipo | Observação |
|---|---|---|
| `Key` | `UNIQUEIDENTIFIER` PK | `BaseEntity` |
| `InvoiceType` | `INT` | `Normal` / `Return` |
| `IssuerType` | `INT` | `ThirdParty` / `Own` — ver abaixo |
| `InvoiceStatus` | `INT` | reusa o enum `InvoiceStatus` já existente |
| `InvoiceNumber` | `VARCHAR(9)` | número INTERNO. Nulo em Fase 1 para terceiro; digitado à mão na emissão própria |
| `CardCode` / `CardName` | `VARCHAR(15)` / `VARCHAR(200)` | emitente: fornecedor, produtor ou cliente devolvendo. **Sem FK** — cadastro dual-mode |
| `TaxDocumentNumber` | `VARCHAR(9)` | número da NF do documento fiscal |
| `TaxDocumentSeries` | `VARCHAR(3)` | |
| `ChaveNFe` | `VARCHAR(44)` | chave da NF-e. Nome igual ao de `SalesInvoice` — ver Nomenclatura |
| `IssueDate` | `DATETIME2` | emissão |
| `PostingDate` | `DATETIME2` | entrada/lançamento |
| `TotalDocumentValue` | `DECIMAL(18,2)` | total DECLARADO pelo emitente (`ICMSTot/vNF`) |
| `TaxPayerComments` | `VARCHAR(MAX)` | `infAdic/infCpl` cru |
| `Comments` | `VARCHAR(500)` | observação do cabeçalho |
| `GrossWeight` / `NetWeight` | `DECIMAL(18,3)` | documentos que acompanham carga |
| `TruckCode` | `VARCHAR(10)` | |
| `TruckingCompanyCode` / `Name` | `VARCHAR(15)` / `VARCHAR(200)` | |
| `FreightTerms` | `INT` | |
| `PurchaseInvoiceOriginKey` | `UNIQUEIDENTIFIER NULL` | auto-relação → **remessa aponta a NF de venda futura** |
| `XmlFileName` / `XmlData` | `VARCHAR(200)` / `VARBINARY(MAX)` | prova documental, permite reprocessar |

Coleções: `Items`, `CommentEntries`, `ChangeLogs`.
`[NotMapped]`: `TotalInvoiceItems` (soma das linhas) e `TotalInvoiceTaxes`.

**`TotalDocumentValue` × `TotalInvoiceItems` são propositalmente ambos.** O primeiro é o que o emitente
declarou; o segundo é a soma do que foi digitado/importado. Divergirem é informação de conciliação,
não erro — e é por isso que `CustomerReturn.TotalValue` não pode simplesmente virar derivado.

A auto-relação usa o mecanismo que `SalesInvoiceOriginKey` já usa para nota de retorno → original. A
remessa do produtor apontando a NF de venda futura é o mesmo formato de referência.

Índice único em `ChaveNFe`, **filtrado por `IsNotNull` e `InvoiceStatus <> Cancelled`** — copia o
padrão de `AppDbContext.cs:129-132`. Cancelar libera a chave sem apagar o documento.

### `PURCHASE_INVOICES_ITEMS` (nova)

Espelha `SalesInvoiceItem`. Comerciais em Fase 1; o bloco fiscal chega na Fase 2.

| Coluna | Tipo | Observação |
|---|---|---|
| `Key` | `UNIQUEIDENTIFIER` PK | |
| `PurchaseInvoiceKey` | FK → `PURCHASE_INVOICES` | |
| `ItemCode` / `ItemName` | `VARCHAR(30)` / `VARCHAR(200)` | do XML, código do emitente pode divergir do cadastro |
| `Quantity` | `DECIMAL(18,3)` | |
| `UnitPrice` | `DECIMAL(18,8)` | |
| `UnitOfMeasureCode` | `VARCHAR(4)` | |
| `SalesInvoiceItemKey` | `UNIQUEIDENTIFIER NULL` | FK → `SALES_INVOICES_ITEMS`. **A amarração da devolução** |
| `PurchaseInvoiceItemOriginKey` | `UNIQUEIDENTIFIER NULL` | auto-relação: linha de remessa → linha da NF futura |

`ItemCode` é `VARCHAR(30)` e **nulável**, seguindo `CustomerReturnItem` e não `SalesInvoiceItem`
(`VARCHAR(10) NOT NULL`): o código vem do emitente, é informativo, e pode não existir no cadastro
local. Mesma razão para `UnitOfMeasureCode` ser nulável aqui.

`[NotMapped]`, herdados sem alteração de `CustomerReturnItem`:

```csharp
Total            => Round(Quantity * UnitPrice, 2)
AssessedShortage => SalesInvoiceItem?.AssessedShortage ?? 0m
Difference       => Round(Quantity - AssessedShortage, 3)
```

`PurchaseContractKey` e `StorageTransactionKey` **não entram nesta fase** — são Fase 3, junto com o
value help e a coluna de divergência que os consomem. Criá-los agora seria coluna sem consumidor, o
erro que a Transferência de Titularidade já cometeu neste projeto.

### `PURCHASE_INVOICES_COMMENTS` e `PURCHASE_INVOICES_CHANGE_LOGS` (novas)

Espelhos diretos de `SalesInvoiceComment` / `SalesInvoiceChangeLog`, incluindo o nome `CommentEntries`
para a coleção (porque `Comments` já é o escalar de observação do cabeçalho) e as regras de autoria de
`ContractCommentRules.EnsureCanModify` (autor ou admin).

### `IssuerType` — e por que ele tem consumidor já na Fase 1

`DocumentIssuerType { ThirdParty, Own }`. Todos os casos levantados pelo usuário são de terceiro, mas
a emissão própria foi explicitamente incluída no escopo do sub-projeto.

Em Fase 1 o campo é **plenamente funcional**: selecionável na tela, e em `Own` o operador digita
`InvoiceNumber` à mão. A Fase 3 acrescenta o numerador automático (`TransactionCode.PurchaseInvoice`
+ `DocNumbers` por filial) — que é uma conveniência, não a habilitação do campo. Assim `IssuerType`
nasce com uso real em vez de ficar dormente.

## Ciclo de status

`InvoiceStatus { Pending, Confirmed, Cancelled, Returned }`, reusado. Transições nesta fase:

- **Pending → Confirmed** (`Confirm`) e **Confirmed → Pending** (`ReverseConfirm`)
- **→ Cancelled** (`Cancel`), de qualquer um dos dois
- **Delete** só em `Pending`, como `SalesInvoicesDeleteService`

> **Refinamento consciente do plano aprovado.** O plano listava `Confirm`/`ReverseConfirm` na Fase 3,
> junto com os efeitos. Foram trazidos para a Fase 1 por duas razões: as linhas migradas de
> `CUSTOMER_RETURNS` chegam como `Confirmed` e precisam de caminho de volta, e o gate de edição é
> valor imediato — hoje a devolução não tem nenhum. Em Fase 1 as duas ações **só transicionam
> status**; a Fase 3 pendura os efeitos de contrato nelas sem mexer na máquina de estados.

`Returned` fica sem produtor nesta fase — é o valor que a Fase 3 usará quando a entrada for revertida.

## Migração de `CUSTOMER_RETURNS`

Uma migration no `AppContext` que, **em ordem, dentro do mesmo `Up()`**:

1. cria as 4 tabelas novas;
2. copia `CUSTOMER_RETURNS` → `PURCHASE_INVOICES` com `InvoiceType = Return`,
   `IssuerType = ThirdParty`, `TotalValue → TotalDocumentValue`, `DocumentNumber → TaxDocumentNumber`,
   `DocumentSeries → TaxDocumentSeries`, **`AccessKey → ChaveNFe`**, e status
   `Registered(0) → Confirmed`, `Cancelled(1) → Cancelled`;
3. copia `CUSTOMER_RETURNS_ITEMS` → `PURCHASE_INVOICES_ITEMS` preservando `SalesInvoiceItemKey`
   (a amarração) e as chaves, para que `AssessedShortage`/`Difference` continuem batendo;
4. dropa `CUSTOMER_RETURNS_ITEMS` e `CUSTOMER_RETURNS`.

**Idempotente e tolerante à ausência das tabelas**: a devolução não está commitada e pode não ter sido
aplicada em todos os ambientes — os passos 2-4 ficam sob `IF OBJECT_ID('CUSTOMER_RETURNS') IS NOT NULL`.
`BranchCode` das linhas migradas recebe a filial padrão (`CUSTOMER_RETURNS` não tinha filial).

`Down()` não reconstrói `CUSTOMER_RETURNS` — a entidade deixa de existir no código, então a volta
seria um esquema órfão. Documentar isso no arquivo.

## Regras preservadas da devolução

Continuam valendo, sem alteração, para `InvoiceType = Return`:

- `QuebraApurada = Quantity − (DeliveredQuantity − QuantityLoss)` no item de saída de origem, e 0
  enquanto a entrega está aberta.
- `Diferenca = Quantity − QuebraApurada` por linha amarrada. Diferente de zero a tela **avisa e deixa
  gravar** — arredondamento e devolução parcial são legítimos.
- Origem elegível: mesmo `CardCode`, documento `Confirmed`, `DeliveryStatus == Closed`, quebra > 0.
- **Nenhum efeito em saldo, ledger ou romaneio.** O fator efetivo da Conferência de Entrega já
  devolveu o volume; creditar aqui contaria em dobro.
- A importação de XML **não adivinha a amarração** — o layout põe as referências em `ide/NFref`, que
  é do cabeçalho, e não diz qual linha veio de qual origem.

Duas regras da devolução são **corrigidas**, não preservadas:

- **`Update` persiste as linhas.** O serviço novo aplica alterações de item (incluir, alterar
  amarração, excluir), que é o que falta hoje.
- **`Detail` deixa de ser read-only** para documento `Pending`, ganhando `Edit` como a saída tem.

## Serviços e API

Uma classe por operação em `Services/PurchaseInvoices/`, registradas à mão em
`AddApplicationServices()` (`SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` — não há assembly
scanning, apesar do Scrutor referenciado):

`PurchaseInvoicesGetService`, `CreateService`, `UpdateService`, `DeleteService`, `ConfirmService`,
`ReverseConfirmService`, `CancelService`, `ImportXmlService`, `GetOriginItemsService`,
`Items{Create,Update,Delete,Get}Service`, `ChangeLogService` + `ChangeLogsGetService`,
`Comment{Create,Update,Delete}Service` + `CommentsGetService`.

**Não há `SetDocumentNumberService` nesta fase.** No documento de saída ele existe porque a NF é
emitida depois que o documento nasce; aqui número, série e chave chegam juntos no XML ou na digitação.
A trava de unicidade (número+série por filial, chave global, ambas ignorando cancelados e o próprio
documento) mora dentro de `Create`/`Update`. O serviço separado só ganha função na emissão própria,
na Fase 3.

`GetService` **precisa** de `Include(Items).ThenInclude(SalesInvoiceItem)` — sem isso
`AssessedShortage` volta 0 em silêncio e toda linha parece divergente.

EDM em `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`: EntitySets `PurchaseInvoices`,
`PurchaseInvoicesItems`, `PurchaseInvoicesComments`, `PurchaseInvoicesChangeLogs`, com `AddProperty`
explícito para as `[NotMapped]` (`Total`, `AssessedShortage`, `Difference`, `TotalInvoiceItems`) — a
convenção pula propriedade sem setter público.

Actions OData: `PurchaseInvoicesConfirm`, `ReverseConfirm`, `Cancel`, `ImportXml`.
Function `PurchaseInvoicesOriginItems(CardCode)`.

`ImportXml` é action OData e não upload REST porque o dev server e o Gateway só encaminham `/odata`,
`/security` e `/reports` — a mesma razão documentada no controller da devolução.

> **Armadilha conhecida:** parâmetro string de action OData é anulável. `TryGetValue` devolve `true`
> com `null` e `.ToString()` estoura. `ImportXml` tem `FileName` opcional e cai nesse buraco.

## Frontend

`webapp/view/purchaseInvoices/`: `Main` (lista + Filterbar **com filtro de Tipo**), `Add`, `Edit`,
`Detail` (ObjectPage com seções Form / Items / Comentários / Log de Alterações). Fragmentos
`Filterbar`, `Form`, `Items`, `NotaFiscalDialog`, `Comments`, `ChangeLogs`.

Diálogo de origem: manter o vínculo em `/SalesInvoicesItems` com `$filter` explícito, como a devolução
faz — função que devolve coleção não se liga por `elementPath` e o diálogo abria vazio.

Rotas: `purchase-invoices`, `purchase-invoices/add`, `purchase-invoices/{id}/detail`,
`purchase-invoices/{id}/edit`.

Menu (migration no `CommonContext`): remove `MENU_ITEMS` `customerReturns` e seu `ROLE_MENUS`; cria
`purchaseInvoices` "Documentos de Entrada" em **Compras** (`ParentKey = "purchases"`, `Order = 9` —
1 a 8 estão ocupados, 7 é `storageEntryTransaction` e 8 é a aprovação de fixação). `Key` **tem** de ser
igual ao nome da rota no manifest: `App.controller.ts` faz `navTo(item.getKey())`.

De quebra some a colisão que `customerReturns` tinha: estava em *Vendas* com `Order 8`, igual a
`salesContractsShipmentRelease`, deixando a ordenação entre os dois indefinida.

## Remoções

`CustomerReturn`, `CustomerReturnItem`, `CustomerReturnStatus`, `CustomerReturnOriginItemDto`,
`CustomerReturnDraftDto`, `Services/CustomerReturns/`, `CustomerReturnsController.cs`,
`Actions/CustomerReturns/`, os registros DI, `webapp/view/customerReturns/`,
`webapp/controller/customerReturns/`, `CustomerReturnOriginItemsSelectDialog.fragment.xml`, os
formatters `formatCustomerReturnStatus`/`stateCustomerReturnStatus`/`stateReturnDifference` e as 3
rotas/targets do `manifest.json`.

Os testes de `SiagroB1.Application.Tests/CustomerReturns/` **migram para
`PurchaseInvoices/`**, não somem.

## Nomenclatura da chave da NF-e

O campo chama-se **`ChaveNFe`**, igual ao de `SalesInvoice`. É uma exceção consciente à regra de
identificadores em inglês, decidida pelo usuário: as duas entidades são irmãs diretas — documento de
entrada e documento de saída — e serão lidas lado a lado o tempo todo. Nome divergente entre elas
custa mais do que a exceção.

`CUSTOMER_RETURNS.AccessKey` é renomeada na migração de dados. A regra de inglês continua valendo
para todo o resto do modelo.

## Testes (`SiagroB1.Application.Tests/PurchaseInvoices/`)

Migrados da devolução, agora sobre a entidade nova:

- `QuebraApurada` = faturado − líquido; 0 em item com entrega aberta.
- Diferença apontada quando a devolvida não bate; gravação **não** é bloqueada.
- Origens elegíveis excluem: outro cliente, documento cancelado, entrega aberta, quebra zero.
- Importação de XML: cabeçalho e linhas preenchidos, `infCpl` preservado, XML guardado.
- Chave duplicada recusada; cancelada libera a chave; o próprio documento não colide consigo.
- Nenhuma linha de allocation criada e nenhum saldo de contrato alterado em criar/alterar/cancelar.

Novos desta fase:

- `Update` **persiste alteração, inclusão e exclusão de linhas** — a regressão que a devolução tem.
- `Delete` recusado em documento `Confirmed`.
- `Confirm`/`ReverseConfirm` transicionam e recusam transição inválida.
- `TotalDocumentValue` divergente de `TotalInvoiceItems` é aceito e preservado.

## Verificação

Gates:

```
dotnet build SiagroB1.sln
dotnet test SiagroB1.Application.Tests
cd siagro-b1-frontend && yarn ts-typecheck && yarn lint
```

`yarn test` não passa neste repo — o gate de cobertura é 50% contra ~2,4% reais. Não é regressão desta
mudança. Parar o dev server antes de rodar (porta 8080).

Migration aplicada com `ASPNETCORE_ENVIRONMENT` **explícito** — o perfil `db-migration` aponta para
produção no fallback, e o alvo muda; ler a connection string antes de escrever.

**Pelo caminho do usuário, no navegador** (Web + Gateway no profile `yktb`, `yarn start:dev`, login
`admin/1234`) — é onde os bugs deste projeto aparecem:

1. Compras → **Documentos de Entrada** aparece no menu e abre a lista
2. Incluir → **Importar XML** de uma NF-e real → cabeçalho e itens preenchem
3. Gravar → reabrir → **Editar e regravar as linhas** (o que a devolução não faz hoje)
4. Filtrar por Tipo = Devolução → as linhas migradas de `CUSTOMER_RETURNS` aparecem com a amarração à
   NF de saída e a Quebra Apurada preservadas
5. Confirmar, estornar, cancelar → cancelar libera a chave e a mesma NF pode ser reimportada
6. Conferir que o saldo do contrato **não mudou** em nenhum dos passos

Armadilhas a conferir explicitamente no navegador, todas com histórico neste projeto: enum em binding
precisa de `targetType:'any'`; propriedade ausente do `create()` inicial quebra com *"Must not change
a property before it has been read"*; `$expand` faltando faz `AssessedShortage` voltar 0 em silêncio;
`Include` de FK obrigatória vira INNER JOIN e zera a coleção inteira em modo SAPB1.

## O que NÃO entra nesta fase

- Campos fiscais da linha, CFOP, natureza de operação, `UsageGuard` — Fase 2.
- CFOPs de entrada em `USAGES` — Fase 2.
- Amarração a contrato de compra e a romaneio, coluna de divergência — Fase 3.
- Qualquer efeito em saldo, valor de contrato ou ledger — Fase 3.
- Numerador automático `DocNumbers` da emissão própria e `SetDocumentNumberService` — Fase 3.
- Emissão de NF-e — sub-projeto 3 do roadmap.
- Motor de cálculo de tributação — fora da fila; impostos são informados pelo usuário.
