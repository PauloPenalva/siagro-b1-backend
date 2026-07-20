# Design: entrada de compra em armazenagem própria (par Purchase/Receipt)

**Data:** 2026-07-20
**Repositórios:** `siagro-b1-backend`, `siagro-b1-frontend`
**Autor:** Paulo Penalva (com Claude Code)
**Status:** Implementado e verificado ponta a ponta. Não commitado.

## Contexto

O módulo de armazenagem e beneficiamento (prestação de serviço para terceiros) controla
saldos por lote (`StorageAddress`), movimentados por romaneios `Receipt`/`Shipment`/
`TechnicalLoss`. O módulo de compras baixa `PurchaseContract` e `ShipmentRelease` através
de um embarque (`ShippingTransactionsCreateService`), que hoje gera só o par
`Purchase`/`SalesShipment` — cobrindo o caso em que a carga comprada é revendida no ato.

Faltava o caso em que a carga comprada **fica com a empresa** (Yokotobi), entrando na
armazenagem própria para uso futuro: precisa baixar contrato e liberação **e** alimentar
o saldo de um lote, em nome da empresa.

### Por que não reaproveitar `ShippingTransaction`

`ShippingTransaction` é só um link table entre dois `StorageTransaction`
(`PurchaseStorageTransactionKey`/`SalesStorageTransactionKey`), sem status próprio e sem
suporte a estorno como unidade. Reusar a tabela renomeando a segunda FK teria exigido
migration em tabela viva e um discriminador de tipo de par, para o mesmo resultado de uma
tabela nova. Optou-se por uma entidade própria — `StorageEntryTransaction` — com estado
(`Confirmed`/`Cancelled`) e auditoria de cancelamento, permitindo o estorno completo.

---

## Desenho

### O par

`Purchase` baixa contrato e liberação **exatamente como o embarque hoje** — mesmo
orquestrador de passos, só que sem gerar `SalesShipment`. O clone vira `Receipt`, mas com
uma diferença deliberada em relação ao `StorageTransactionCopyFactory` genérico: o dono do
produto no lote **não é o fornecedor da compra**. `StorageEntryReceiptFactory.ApplyLot`
troca `CardCode`/`CardName`/`WarehouseCode`/`ProcessingCostCode` pelos do lote selecionado
— é a mesma regra que a quebra técnica automática já segue (`CardCode` do
`StorageAddress`), e reflete que o `ProcessingCost` do `Receipt` vem do lote, não da
compra.

Consequência aceita: como as duas tabelas de custo podem divergir, `Purchase.NetWeight` e
`Receipt.NetWeight` podem ficar diferentes. A entidade grava os dois pesos
(`AllocatedVolume`, `ReceiptNetWeight`), e a tela avisa (sem bloquear) quando divergem.

O encaixe é natural na mecânica existente sem exigir nenhuma mudança no módulo de
armazenagem:

- `Receipt` não está em `PurchaseContractsAllocationCreateService.IsAllocatable` nem em
  `ShipmentReleasesRecalculateShippedService.AffectsShippedQuantity` → não interfere em
  contrato/liberação.
- `StorageAddress.Balance` e `StorageAddressesGetBalanceService` já contam `Receipt` como
  positivo → o lote é alimentado sem código novo.

### Furo pré-existente corrigido no novo fluxo (não no antigo)

Os hooks de `ShippedQuantity` em `StorageTransactionsCreateService`/`ConfirmedService` só
disparam quando `commitMode == CommitMode.Auto`. Como o orquestrador de embarque
(`ShippingTransactionsCreateService`) roda todas as etapas em `CommitMode.Deferred`, **o
embarque hoje não atualiza `ShippedQuantity`** — bug real, fora de escopo desta entrega.
`StorageEntryTransactionsCreateService` chama
`ShipmentReleasesRecalculateShippedService.RecalculateAsync` explicitamente após o commit,
para não repetir o mesmo furo.

### Guard no `PurchaseContractsAllocationDeleteService`

Sem proteção, excluir a alocação pela tela de contratos devolveria o volume ao contrato
mas deixaria o produto no lote, a liberação consumida e a `StorageEntryTransaction` ainda
`Confirmed` — inconsistência silenciosa.

O serviço já distinguia dois caminhos de entrada:

- **`ExecuteWithTransactionAsync`** — chamado pela tela (`PurchaseContractsDeleteAllocation`).
- **`ExecuteAsync`** — cascata interna, chamada por `ShipmentBillingDeleteService` e agora
  também por `StorageEntryTransactionsCancelService`.

O guard entrou só em `ExecuteWithTransactionAsync`, ao lado da checagem de contrato
encerrado já existente: bloqueia se a alocação pertencer a uma `StorageEntryTransaction`
`Confirmed`, com a mensagem apontando para o estorno correto. O caminho interno do estorno
segue livre.

> ⚠️ **Limite conhecido.** `ShipmentBillingDeleteService` também chama `ExecuteAsync` e
> passa livre do guard. Hoje é inofensivo porque um `Purchase` de entrada em armazenagem
> não nasce ligado a faturamento de embarque — mas se essa premissa mudar, o mesmo furo
> reaparece por essa porta.

---

## Backend

### Domínio

- `SiagroB1.Domain\Enums\StorageEntryTransactionStatus.cs` — `Confirmed = 0, Cancelled = 1`.
- `SiagroB1.Domain\Entities\StorageEntryTransaction.cs` — tabela `STORAGE_ENTRY_TRANSACTIONS`,
  herda `BaseEntity`. FKs sem cascade (padrão do projeto: todas as relações são
  `DeleteBehavior.NoAction` via `AppDbContext.OnModelCreating`).

  | Campo | Papel |
  |---|---|
  | `PurchaseStorageTransactionKey` / nav | romaneio que baixou contrato e liberação |
  | `ReceiptStorageTransactionKey` / nav | romaneio que alimentou o lote |
  | `PurchaseContractKey` / nav | contrato baixado — necessário para o estorno localizar a alocação |
  | `StorageAddressCode` / nav | lote de destino |
  | `Status` | `Confirmed`/`Cancelled` |
  | `AllocatedVolume` | `Purchase.NetWeight` — o que baixou o contrato |
  | `ReceiptNetWeight` | `Receipt.NetWeight` — o que entrou no lote |
  | `RowVersion` | concorrência otimista, mesmo padrão de `StorageTransaction`/`ShipmentRelease` |

- `SiagroB1.Domain\Interfaces\IStorageAddressBalanceReader.cs` — abstração de leitura de
  saldo do lote, implementada por `StorageAddressesGetBalanceService` (Dapper/T-SQL).
  Existe só para permitir testar o guard de saldo do estorno sem SQL Server.

### Application

- `Services\StorageEntryTransactions\Factories\StorageEntryReceiptFactory.cs` — duas
  funções puras e testadas isoladamente:
  - `EnsureLotAccepts(lot, purchase)` — lança se o lote estiver `Closed` ou o produto
    divergir.
  - `ApplyLot(clone, lot)` — converte o clone da compra em `Receipt`: tipo, status
    `Pending`, dono/armazém/custo do lote, `ShipmentReleaseKey = null` (o `Receipt` não
    consome liberação — deixá-lo preenchido só gera ruído no
    `ShipmentReleaseMovementGuardService`), e zera os três descontos herdados +
    `AvaiableVolumeToAllocate` (`CalculateReceipt` faz `return` silencioso quando não acha
    o `ProcessingCost`; zerar evita herdar os descontos da compra nesse caminho).

- `Services\StorageEntryTransactions\StorageEntryTransactionsCreateService.cs` — orquestrador,
  espelha `ShippingTransactionsCreateService`: uma transação de banco, todas as etapas em
  `CommitMode.Deferred`, `catch` → `RollbackAsync` + `ApplicationException`.

  ```
  ExecuteAsync(purchaseContractKey, storageAddressCode, purchase, userName)
    1. carrega o lote; EnsureLotAccepts
    2. storageCreateService + storageConfirmedService(isShipmentTransaction: true) → Purchase
    3. purchaseAllocationCreateService.ExecuteAsync(overload por entidade, romaneio sem Key ainda)
    4. StorageTransactionCopyFactory.CreateFrom(purchase) + StorageEntryReceiptFactory.ApplyLot
    5. storageCreateService + storageConfirmedService → Receipt
    6. cria StorageEntryTransaction, commit
    7. FORA da transação: RecalculateAsync(purchase.ShipmentReleaseKey) explícito
  ```

- `Services\StorageEntryTransactions\StorageEntryTransactionsCancelService.cs` — estorno,
  guards nesta ordem:

  1. registro existe e `Status == Confirmed`
  2. nenhum dos dois romaneios `Invoiced`
  3. `PurchaseContract.Status != Finished`
  4. `IStorageAddressBalanceReader.GetBalance(lote) >= ReceiptNetWeight` — recusa se o
     produto já saiu do lote, em vez de deixar saldo negativo (mesma filosofia do
     `ValidateBalance` de `StorageTransactionsReverseService`)

  Ações, nesta ordem exata (a alocação sai **primeiro**, porque
  `StorageTransactionsCancelService` recusa romaneio com alocação pendente):

  1. `PurchaseContractsAllocationDeleteService.ExecuteAsync` (devolve saldo ao contrato)
  2. `StorageTransactionsCancelService` no `Receipt` (sai do saldo do lote)
  3. `StorageTransactionsCancelService` no `Purchase` (dispara o recálculo de
     `ShippedQuantity` da liberação, via hook existente)
  4. `Status = Cancelled`, `CanceledAt`/`CanceledBy`

### Web

- `Controllers\StorageEntryTransactionsController.cs` — entity set somente leitura.
  **Duas armadilhas descobertas só na verificação de ponta a ponta:**

  1. O controller nasceu em `Actions\StorageEntryTransactions\` (pasta das actions) em vez
     de `Controllers\` (pasta dos entity sets) e sem a sobrecarga `Get(Guid key)`. Sem ela,
     `/StorageEntryTransactions(key)` respondia **404** e a tela de detalhe abria vazia.
  2. Ao corrigir, devolver a entidade já materializada (`Task<ActionResult<T>>` com
     `FirstOrDefaultAsync`) fazia o `[EnableQuery]` não conseguir compor `$expand`/`$select`
     — as quatro navegações (`PurchaseStorageTransaction`, `ReceiptStorageTransaction`,
     `PurchaseContract`, `StorageAddress`) voltavam nulas, sem erro. A correção foi devolver
     `SingleResult<T>` sobre o `IQueryable` (`SingleResult.Create(query.Where(x => x.Key == key))`),
     que é o padrão exigido pelo Microsoft.AspNetCore.OData para GET por chave com
     `autoExpandSelect`.

- `Actions\StorageEntryTransactions\StorageEntryTransactionsCreateController.cs` —
  `POST /odata/StorageEntryTransactionsCreate`, params `PurchaseContractKey`,
  `StorageAddressCode`, `StorageTransaction` (entity parameter). Retorna
  `{ Key, AllocatedVolume, ReceiptNetWeight }`.
- `Actions\StorageEntryTransactions\StorageEntryTransactionsCancelController.cs` —
  `POST /odata/StorageEntryTransactionsCancel`, param `Key`.
- `ODataConfig\ODataConfigurations.cs` — `EntitySet<StorageEntryTransaction>` + as duas
  actions acima registradas.
- `Extensions\ServiceCollectionExtensions.cs` — `StorageEntryTransactionsCreateService`,
  `StorageEntryTransactionsCancelService` e `IStorageAddressBalanceReader` (mapeado para
  `StorageAddressesGetBalanceService`) registrados manualmente — não há assembly scanning
  neste projeto.

### Migrations

- `AppContext\20260720131015_AddStorageEntryTransactions` — cria `STORAGE_ENTRY_TRANSACTIONS`.
  Um único `CreateTable`, sem cascade, sem drift de outras entidades.
- `CommonContext\20260720131842_AddStorageEntryTransactionMenu` — item de menu
  `storageEntryTransaction` ("Entrada em Armazenagem") no grupo `purchases`, com vínculo em
  `ROLE_MENUS` para `ADMIN`. Sem o `ROLE_MENUS`, o item de menu existe mas não aparece para
  ninguém.

  > ⚠️ Aplicadas com `dotnet ef database update ... --context <AppDbContext|CommonDbContext>`
  > passando `ASPNETCORE_ENVIRONMENT=Yokotobi` **explícito na variável de ambiente**, nunca
  > via `--launch-profile db-migration` (esse profile força `ASPNETCORE_ENVIRONMENT=Migration`
  > e o fallback de connection string pode apontar para outro ambiente — sempre conferir o
  > `appsettings.<Environment>.json` antes de aplicar). `Yokotobi` → `IDX_SIAGRO_DEV`
  > (`localhost`), banco de desenvolvimento local.

### Testes (`SiagroB1.Application.Tests`)

TDD estrito: cada teste escrito e visto falhar (RED) antes do código de produção (GREEN).

- `StorageEntryTransactions\StorageEntryReceiptFactoryTests.cs` — as duas funções puras da
  factory, sem tocar banco.
- `StorageEntryTransactions\StorageEntryTransactionsCancelServiceTests.cs` — o estorno
  completo com EF InMemory; `IStorageAddressBalanceReader` é fakeado (o real usa Dapper/SQL
  cru, que não roda em InMemory) para testar a decisão do guard de saldo isoladamente.

`StorageEntryTransactionsCreateService` não tem teste automatizado próprio: depende de
`DocNumberSequenceService`, que usa T-SQL (`UPDLOCK, HOLDLOCK`) via Dapper e não roda em EF
InMemory. Verificado por execução real contra `IDX_SIAGRO_DEV` (ver seção Verificação).

---

## Frontend

Módulo `storageEntryTransaction`, cinco telas.

### Fluxo e rotas

```
Main (lista, DynamicPage + IconTabBar)
  → Nova Entrada → SelectWarehouse → SelectShipmentRelease → Create → volta para Main
  → Visualizar   → Detail (somente leitura, com o botão Estornar)
```

| Rota | Padrão | Tela |
|---|---|---|
| `storage-entry-transaction:?query:` | nível 1 | `Main` |
| `storage-entry-transaction/select-warehouse:?query:` | nível 2 | `SelectWarehouse` |
| `storage-entry-transaction/select-shipment-release:?query:` | nível 2 | `SelectShipmentRelease` |
| `storage-entry-transaction/create:?query:` | nível 2 | `Create` |
| `storage-entry-transaction/{id}/detail` | nível 2, `{id}` no path | `Detail` |

`Detail` segue o único padrão de rota usado pelas ~10 telas Detail do projeto — `{id}` no
path, nunca query string — diferente das demais telas deste módulo, que usam query string
por serem um fluxo de passos.

### Main — lista com filtros e abas

- `DynamicPage` + `IconTabBar` (não `ObjectPageLayout`): é o único idiom do projeto para
  lista com abas de status + `$count` (mesmo molde de `weighingTicket/Main`). Um
  `ObjectPageLayout` teria colocado abas dentro de abas, já que ele tem sua própria barra.
- Abas **Confirmadas**/**Canceladas**, contagem via `$count` com `$filter=Status eq '...'`
  — literal simples, não qualificado com o namespace CLR do enum
  (`SiagroB1.Domain.Enums.StorageEntryTransactionStatus'Confirmed'` retorna **400**; o
  namespace correto do schema OData é `SIAGROB1`, mas o literal simples funciona sem
  amarrar a tela a nenhum dos dois).
- Filtros de Lote/Contrato/Romaneio via `FilterBar` + `contains()`, inclusive sobre
  navegação para-um (`contains(PurchaseContract/Code,'...')`,
  `contains(PurchaseStorageTransaction/Code,'...')`) — confirmado que o OData V4 aceita.
  Montagem do `$filter` em `Main.controller.ts.applyFilters()`, combinando aba + campos com
  `and`, aplicado via `binding.changeParameters(...)` (não `binding.filter()` — os dois não
  convivem no mesmo binding; é o idiom de `weighingTicket/Main.controller.ts`).
- Botão **Visualizar** (não mais **Estornar** — ver Detail) lê `getSelectedItem()` da
  `sap.m.Table` e navega para `storageEntryTransactionDetail`.

### SelectWarehouse — seleção de produto/armazém

Réplica do que era a primeira seção do `Main` original, extraída para tela própria quando
o `IconTabBar` entrou. Usa `ShipmentReleasesGetBalance`, igual ao `shippingTransaction`.

### SelectShipmentRelease e Create

Réplicas quase literais de `shippingTransaction/SelectShipmentRelease` e
`shippingTransaction/Create`, com as diferenças:

- `Create` invoca `/StorageEntryTransactionsCreate(...)` com o parâmetro extra
  `StorageAddressCode` (escolhido via o diálogo de lotes **abertos por item** —
  `StorageAddressesListOpenedByItem`/`BaseController.openLotsDialog`, **não** o
  `StorageAddressesSelectDialog` genérico, que não filtra por item nem por lote aberto).
- No retorno da action, se `AllocatedVolume != ReceiptNetWeight`,
  `MessageBox.warning` mostrando os dois valores — a divergência de peso descrita acima.

### Detail — visualização com estorno

Somente leitura, `ui>/editable = false`. Segue o molde de `storageTransactions/Detail`
(o único Detail do projeto com ação de estorno):

- Ações em `uxap:actions` (header title), **não** em `uxap:footer` — nenhum Detail do
  projeto usa footer, mesmo declarando `showFooter="true"`.
- `Estornar` com `enabled` por expression binding sobre `Status`, substituindo a checagem
  imperativa que antes vivia no `Main.controller.ts` (`onCancelEntry` foi removido; o
  estorno mora só aqui agora).
- Seção "Dados da Entrada": contrato, lote, auditoria.
- Seção "Romaneios do Par": dois `Panel` com `binding="{PurchaseStorageTransaction}"` /
  `binding="{ReceiptStorageTransaction}"`, cada um incluindo o mesmo fragmento
  (`TransactionFields.fragment.xml`) com caminhos **relativos** — os 11 campos existem uma
  vez só e servem aos dois romaneios.
- Após estornar com sucesso: re-binda (`setData(key)`) em vez de navegar de volta à lista —
  o usuário vê o `Status` virar `Cancelled` e o botão se desabilitar sozinho.

### Manifest e menu

Rotas/targets em `manifest.json` (padrão do projeto: `id`, `level`, `name`,
`clearControlAggregation: true`). Item de menu em `webapp\data\menu.json` (cópia local,
não afeta runtime) e no seed real do backend (ver Migrations acima) — os dois precisam
estar sincronizados, mas só o segundo importa em produção.

---

## Armadilhas descobertas (relevantes além deste módulo)

Nenhuma delas estava documentada nos docs existentes do frontend antes desta entrega.

1. **Enum OData V4 aplica o tipo do `$metadata` sozinho, mesmo sem `type:` declarado.**
   Bindings de enum (`Status`, `TransactionType`, `TransactionStatus`) e de data
   (`CreatedAt`, `CanceledAt`) precisam de `targetType: 'any'` em **toda** leitura,
   inclusive dentro de expression binding (`{= ${path: '...', targetType: 'any'} === ... }`).
   Sem isso:
   - enum: `Type 'sap.ui.model.odata.type.Raw' does not support formatting` — erro visível.
   - data: `Illegal sap.ui.model.odata.type.DateTimeOffset value: ...` porque o `datetime2`
     do SQL Server serializa 7 casas de fração de segundo, que o tipo rejeita — o campo
     **fica em branco silenciosamente**, sem erro no console em alguns caminhos. O
     `formatter.formatDateTime` do projeto já existia e já tinha o comentário explicando a
     causa (precisão do `datetime2`), mas só funciona com `targetType: 'any'` no binding —
     o comentário não deixava isso explícito.

2. **`SingleResult<T>` é obrigatório para GET por chave com `[EnableQuery]` quando o model
   tem `autoExpandSelect: true`.** Devolver a entidade já materializada
   (`Task<ActionResult<T>>`) faz o OData não conseguir compor `$expand` sobre as
   navegações — elas voltam `null`, sem erro. `ODataBaseController<T,ID>` (a base genérica
   do projeto) já usa esse padrão implicitamente através de `IBaseService`; um controller
   escrito à mão precisa repeti-lo.

3. **`--launch-profile dev` ignora `ASPNETCORE_ENVIRONMENT` setado no ambiente do shell** —
   o `launchSettings.json` define `"ASPNETCORE_ENVIRONMENT": "Development"` dentro do
   próprio profile `dev`, sobrescrevendo qualquer variável externa. Para testar contra
   Yokotobi localmente, usar `--launch-profile yktb`.

4. **`SiagroB1.Web/wwwroot` precisa existir** (mesmo vazio) para o projeto subir —
   `StaticWebAssetsLoader` lança `DirectoryNotFoundException` sem ele. Não está no
   `.gitignore` observável nem documentado; provavelmente seed de build normalmente
   presente após um `dotnet publish`/`yarn build:opt` que popula o SPA.

5. **UI5 cacheia falha de `$metadata`.** Se a tela abrir com o Gateway ainda frio (ou logo
   após reiniciá-lo), o primeiro `GET /odata/$metadata` pode responder `504 Gateway
   Timeout`; o UI5 guarda esse erro e todos os bindings da sessão ficam vazios até um F5.
   Não é defeito de tela — mas é fácil confundir com um durante depuração.

---

## Fora de escopo

- Alterar o fluxo existente `Purchase`/`SalesShipment` (`ShippingTransaction`), inclusive o
  furo de `ShippedQuantity` em `CommitMode.Deferred` que ele ainda tem.
- Dar vida ao enum `StorageOwnershipType` (hoje morto no domínio — só lido em relatórios).
- Fechar o guard de `PurchaseContractsAllocationDeleteService` para o caminho de
  `ShipmentBillingDeleteService` (ver "Limite conhecido" acima).
- Bugs pré-existentes encontrados na exploração original e não tocados: arredondamento de
  `StorageAddress.Balance` para inteiro (falta `3` no `decimal.Round`), `IsWarehouseOwner()`
  hard-coded `false`, validações de pesagem comentadas, `CalculateReceipt` falhando em
  silêncio sem `ProcessingCost`.
- i18n — o projeto não usa; textos seguem hardcoded em pt-BR.

## Verificação

Executada de ponta a ponta contra `IDX_SIAGRO_DEV` (`ASPNETCORE_ENVIRONMENT=Yokotobi`,
`--launch-profile yktb` em Web e Gateway), com o contrato `00001028`/liberação
`6E9C8D72-...` e o lote `00000002` (MILHO FLAMBOIA).

1. `dotnet build SiagroB1.sln` — 0 erros.
2. `dotnet test SiagroB1.Application.Tests` — 175 aprovados (15 novos desta feature), 3
   falhas pré-existentes em `PurchaseContractsAllocationCreateServiceTests` (baseline
   confirmada via `git stash`, não é regressão desta entrega).
3. `POST StorageEntryTransactionsCreate` real: contrato alocado 0→1000, liberação
   romaneada 0→1000, saldo do lote 18080→19080; os dois romaneios do par conferidos no
   banco (`Purchase` com fornecedor/liberação, `Receipt` com dono/armazém/custo do lote,
   `ShipmentReleaseKey` nulo).
4. `POST PurchaseContractsDeleteAllocation` na alocação da entrada → **400**, mensagem do
   guard, alocação preservada. `POST StorageEntryTransactionsCancel` na mesma entrada →
   **200**, alocação removida, saldos restaurados a 0/0/18080.
5. Frontend, via Playwright contra o app real: lista com abas e contagens corretas;
   filtro de romaneio reduzindo linhas e combinando com a aba (`AND`); navegação Main →
   SelectWarehouse com breadcrumb correto; Detail exibindo os dois romaneios com
   dono/armazém corretos; estorno pela tela de detalhe confirmado (`Status` vira
   `Cancelled`, botão se desabilita, auditoria preenchida) e saldos restaurados no banco.
6. `yarn ts-typecheck`, `yarn lint` — limpos. `yarn ui5lint` — sem achados novos além dos
   padrões já pré-existentes no restante do projeto (`no-globals` em tipos OData,
   `valueHelpOnly` deprecated).
