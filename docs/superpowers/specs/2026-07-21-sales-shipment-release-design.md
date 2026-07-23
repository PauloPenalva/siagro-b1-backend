# Design: liberação de entrega de contrato de VENDA (SalesShipmentRelease)

**Data:** 2026-07-21
**Repositórios:** `siagro-b1-backend`, `siagro-b1-frontend`
**Autor:** Paulo Penalva (com Claude Code)

## Contexto

O mecanismo de Liberação de Embarque/Entrega (`ShipmentRelease`) existia apenas para
contratos de **compra**: `PurchaseContractKey` é FK `required` e o saldo romaneado
(`ShippedQuantity`) é alimentado só por romaneios `Purchase`/`PurchaseReturn`. Esta
feature replica o mecanismo para contratos de **venda**, como **entidade paralela nova**
(`SalesShipmentRelease`, tabela `SALES_SHIPMENT_RELEASES`) — o fluxo de compra em
produção ficou 100% intocado.

### O fluxo de negócio (decisivo para o design)

1. **Expedição** (`/shipping-transaction`): cria o par romaneio de compra + romaneio de
   venda (`SalesShipment`, cópia retipada). O de compra baixa a liberação de compra e
   aloca no contrato de compra. O `SalesShipment` fica `Confirmed`, aguardando
   faturamento. **Nada mudou aqui.**
2. **Faturamento** (`/shipment-billing`): o usuário seleciona romaneios de venda (mesmo
   produto/veículo) e fatura. No dialog, onde antes apareciam os **contratos de venda**
   disponíveis, agora aparecem as **liberações de venda** disponíveis para o produto
   embarcado. Criar a invoice grava `SalesShipmentReleaseKey` nos romaneios → **consome
   o saldo liberado**; a invoice (nasce `Confirmed` nesse caminho) baixa o saldo físico
   do contrato (derivado de `SalesInvoiceItems`, mecanismo pré-existente).
3. **Devolução de venda**: é uma segunda `SalesInvoice` tipo `Return` que vira o
   `SalesShipment` original para `Returned` — **não existe romaneio
   `SalesShipmentReturn`**. O `Returned` sai da soma → o saldo da liberação **restaura**
   automaticamente. Cancelar a invoice (volta a `Confirmed` + limpa a chave) idem.

## Decisões

### 1. Entidade paralela, não extensão

`SalesShipmentRelease : DocumentEntity` espelha `ShipmentRelease` trocando a FK para
`required SalesContractKey`. Mesmos campos e regras: `ReleasedQuantity`,
`ShippedQuantity` (persistido, `DECIMAL(18,3)`), `[Timestamp] RowVersion`, enum
`ReleaseStatus`, `CancellationReason`, e os `[NotMapped]` `AvailableQuantity` /
`ConsumedQuantity` / `ReturnedToContractQuantity` (invariante
`Consumed + Returned = Released`). Alternativa rejeitada: relaxar `ShipmentRelease`
com FKs anuláveis + discriminador — refatoraria ~15 serviços de compra em produção.

Nova FK nullable **`SalesShipmentReleaseKey`** (+ nav) em `StorageTransaction` **e** em
`SalesInvoiceItem` (transporta a chave do dialog até o vínculo e serve de rastro de
auditoria). `ShipmentReleaseKey` segue exclusivo de compra.

### 2. Consumo no FATURAMENTO, não na expedição

`ShippedQuantity = Σ SalesShipment.NetWeight` sobre transações com a chave e
`TransactionStatus == Invoiced`. Fonte única:
`SalesShipmentReleasesRecalculateShippedService` (`AffectsShippedQuantity =>
SalesShipment` apenas). Pontos de recálculo:

- `SalesInvoicesCreateService` — o `foreach` que já grava `SalesInvoiceKey`/`Invoiced`
  em cada romaneio agora grava também `SalesShipmentReleaseKey`; o orquestrador
  (`ShipmentBillingCreateSalesInvoiceService`) chama o guard
  (`SalesShipmentReleaseMovementGuardService.EnsureCanBillAsync` — bloqueia
  `Completed`/`Cancelled`/`Paused`), valida `Σ NetWeight ≤ AvailableQuantity` e dispara
  o recálculo após o `SaveChanges`;
- `SalesInvoicesCancelService` — reverte para `Confirmed`, **limpa a chave** e recalcula;
- `SalesInvoicesConfirmService.ProcessReturnInvoiceAsync` — vira a origem para
  `Returned` e recalcula (restaura o saldo).

Diferença estrutural vs. compra: lá os hooks vivem no ciclo de vida do
`StorageTransaction`; aqui vivem no ciclo de vida da `SalesInvoice`, porque é o
faturamento que vincula romaneio↔liberação.

### 3. Saldo FÍSICO obrigatório (corrige incoerência com o fluxo legado)

O eixo de liberação (`TotalVolume − Σ ConsumedQuantity`) **não basta** para venda: o
fluxo legado fatura direto no contrato (sem passar por liberação), então um contrato
100% faturado ainda mostraria "saldo a liberar" cheio e aceitaria liberação incoerente
(reproduzido no contrato 00000001: `TotalVolume` 600.000, 600.000 faturados, e uma
liberação de 100.000 foi aceita).

Regra: `PhysicalAvailableToRelease = AvaiableVolume − ReservedByOpenReleases`, onde
`ReservedByOpenReleases = Σ AvailableQuantity` das liberações
`Pending`/`Actived`/`Paused` (só o **não faturado** — a parte faturada já está
descontada em `AvaiableVolume` via `SalesInvoiceItems`; contar o total duplicaria).
Ambos `[NotMapped]` em `SalesContract`, registrados no EDM. Enforçado em:

- `SalesShipmentReleasesCreateService` — guard na criação;
- `SalesShipmentReleasesApprovationService` — guard na ativação (sem provisionamento:
  exclui a própria e as `Pending`, conta `Actived`/`Paused`), defesa contra faturamento
  concorrente entre criar e aprovar;
- `SalesContractsGetShipmentReleasesAvailableService` — espelho **SQL** da regra (EF não
  traduz os `[NotMapped]`); a coluna "Saldo a liberar" da tela binda
  `PhysicalAvailableToRelease`. Manter o SQL e os getters em sincronia.

`SalesContract` também ganhou os computed do eixo de liberação
(`TotalShipmentReleases`, `TotalAvailableToRelease`, `...WithoutProvisioning`,
`HasShipmentReleases`), espelhando `PurchaseContract`.

## Estrutura (espelha 1:1 a compra)

**Backend** — `Services/SalesShipmentReleases/`: Create, Delete (só `Pending`), Update
(indisponível), Get, Approvation (`Pending/Paused→Actived`, exige contrato `Approved`),
Pause, Close (`→Completed`), Reopen (`Completed→Actived`), Cancelation (exige motivo,
recusa saldo ≤ 0, congela `ShippedQuantity`, devolve só o não faturado),
RecalculateShipped, RecalculateBalance (single/all, exclui `Completed`), MovementGuard,
GetAvailable (alimenta o dialog de faturamento; projeta `SalesShipmentReleaseAvailableDto`
com dados do contrato: preço, cliente, UoM). Em `Services/SalesContracts/`: Close
(`Approved→Finished`), Reopen, GetShipmentReleasesAvailable. Controllers em
`Web/Controllers/SalesShipmentReleasesController` (CRUD),
`Web/Actions/SalesShipmentReleases/*` e `Web/Actions/SalesContracts/{Close,Reopen}`,
`Web/Functions/{SalesShipmentReleases,SalesContracts}/*`. Registro no bloco
`// sales shipment releases` do `ServiceCollectionExtensions` e no `ODataConfigurations`
(EntitySet + `AddProperty` dos computed + actions/functions).

**Frontend** — ciclo de vida `salesShipmentReleases/{Main,Detail}` (+ fragments
Form/Audit/Transactions/Filterbar; ações via `bindContext("/Ação(...)").invoke()`);
criar-a-partir-do-contrato `salesContracts/shipmentRelease/{Main,Add}` (Main em
`/SalesContractsGetShipmentReleasesAvailable`, Add com create transiente
`bindList("/SalesShipmentReleases").create({SalesContractKey})`); seção "Liberações de
Entrega" + KPIs "Aguardando Liberação"/"Liberado para embarque" no header do
`salesContracts/Detail` (bindados **direto no contexto OData** — diferente da compra,
que usa REST getTotals); dialog do `shipmentBilling` rebindado para as liberações.
Rotas/targets no `manifest.json`, menu no grupo Vendas, `ServerRoutes.ts`.
`formatter.ts` reusado (já genérico em `ReleaseStatus`).

## Migrations

- `AppDbContext` — `20260721175958_CreateTableSalesShipmentReleases`: tabela
  `SALES_SHIPMENT_RELEASES` + coluna/FK `SalesShipmentReleaseKey` em
  `STORAGE_TRANSACTIONS` e `SALES_INVOICES_ITEMS` (+ índices).
- `CommonDbContext` — `20260721183942_AddSalesShipmentReleaseMenus`: itens
  `salesContractsShipmentRelease` (Order 8) e `salesShipmentReleases` (Order 9) no grupo
  `sales` de `MENU_ITEMS` + vínculos `ROLE_MENUS` com a role `ADMIN` (o menu visível vem
  do backend por role, não do `data/menu.json` estático). `Down` completo.

## Armadilhas encontradas (e suas correções)

1. **Nunca bindar `rows` de tabela direto numa function OData custom.** As functions
   deste backend (incluindo as de compra, ex. `ShipmentReleasesGetBalance`) respondem
   **array JSON cru, sem envelope** `{value:[...]}`; o `ODataListBinding` v4 exige o
   envelope e quebra com `Cannot read properties of undefined (reading 'length')`.
   Padrão correto (telas `SelectShipmentRelease`): `bindContext("/Func(...)")` +
   `setParameter` + `invoke()` → `getBoundContext().getObject()` devolve o array →
   `setData` num JSONModel nomeado → a view binda `nome>/`. Aplicado no dialog do
   `shipmentBilling` (model `releases`).
2. **`GetByIdAsync` precisa de `.Include(SalesShipmentReleases)`.** O `$expand` da
   seção do Detail serializa a partir da nav **carregada** (entidade materializada +
   `[EnableQuery]`), e os computed do header serializam 0 **em silêncio** sem o include
   — mesmo padrão do gotcha conhecido de `AvaiableVolume`/includes aninhados.
3. **A função `GetAvailable` filtra `Status == Actived`** — liberação recém-criada
   (`Pending`) não aparece no faturamento até ser ativada. Comportamento intencional,
   mas parece "lista vazia" em teste.

## Correções pós-implementação (23/07/2026)

Após a verificação inicial, três bugs relacionados ao **local de entrega de venda** foram
reportados e corrigidos na mesma sessão. Fio condutor comum: a tela de venda foi copiada
da de compra e herdou resoluções pensadas para **armazém**, mas no escopo de venda
`DeliveryLocationCode`/`DeliveryLocationName` representam um **cliente** (business
partner `CardType='C'`), não um armazém.

1. **Nome do local de entrega em branco (tela Add + lista + detalhe).**
   `SalesShipmentReleasesCreateService` resolvia o nome via
   `IWarehouseService.GetByIdAsync(DeliveryLocationCode).Name` — sempre `null`, pois o
   código é um `CardCode` de cliente. Corrigido para `IBusinessPartnerService
   .GetByIdAsync(code).CardName` (serviço registrado nos dois modos ERP — SAP e
   standalone — logo funciona no Yokotobi, onde a tabela local de parceiros é vazia).
   `Update` é imutável (`throw`), então `Create` é o único ponto de escrita.
   No frontend, a tela `salesContracts/shipmentRelease/Add.view.xml` já usava
   `.openCostumersValueHelp` corretamente, mas o `descriptionProperty` apontava
   `DeliveryLocationName:Name` (propriedade de armazém) — corrigido para `:CardName`.
   Mesma correção aplicada no filtro da lista (`salesShipmentReleases/fragments
   /Filterbar.fragment.xml`): `valueHelpRequest` trocado de `.openWarehouseValueHelp`
   para `.openCostumersValueHelp`.
   Rótulos "Armazém" renomeados para "Local de entrega" na coluna da lista
   (`Main.view.xml`), no detalhe (`Form.fragment.xml`) e no filtro — **exceto** o
   "Armazém" da aba Romaneios (`Transactions.fragment.xml`, que é `WarehouseCode`, um
   armazém de verdade, e não deve mudar).
   **Backfill:** como o nome só resolve em runtime (modo SAP não tem os clientes na
   tabela local, então uma migration SQL não serviria), foi criada a action de
   manutenção `SalesShipmentReleasesBackfillDeliveryLocationNameService`, exposta como
   unbound action `POST odata/SalesShipmentReleasesBackfillDeliveryLocationName`
   (espelha o padrão `RecalculateAllBalances`). Roda uma vez via HTTP, sem botão no
   frontend; devolve `{Scanned, Updated}`.

2. **Campo "Filial" em branco no detalhe (venda E compra).** O detalhe binda
   `{Branch/ShortName}`. O endpoint by-key (`[EnableQuery]` sobre `GetByIdAsync`)
   **materializa** a entidade — e o `$expand` do OData não expande uma navigation
   property em objeto único já materializado, só o que o serviço trouxe via `.Include`.
   `SalesContract`/`PurchaseContract` estavam incluídos (por isso Cliente/Produto
   carregavam), mas `Branch` não. A **lista** funciona porque usa `QueryAll`
   (`IQueryable`), onde o `$expand` do cliente é aplicado normalmente pelo pipeline
   OData. Corrigido com `.Include(x => x.Branch)` em
   `SalesShipmentReleasesGetService.GetByIdAsync` **e**, por ter o mesmo defeito,
   em `ShipmentReleasesGetService.GetByIdAsync` (compra) — mesmo padrão de armadilha do
   item 2 da seção anterior, mas para a nav `Branch` em vez de `SalesShipmentReleases`.

Verificação: `SiagroB1.Application.Tests` subiu de 267 para **370 testes verdes**
(+resolução de nome no Create, +backfill com 2 cenários, +Include de Branch em ambos os
GetService). Confirmado também na camada OData real (`?$expand=Branch` por chave passou
a devolver o objeto `Branch`) e no browser (menu Vendas, ambiente Yokotobi/yktb): F4
"Clientes" no campo Local de entrega, nome preenchido na lista/detalhe/backfill, e
"Filial" exibindo corretamente no detalhe de venda e de compra.

⚠️ Armadilha de teste: no EF Core **InMemory**, um GET-by-key com `.Include` falha
silenciosamente (retorna `null`) se alguma FK apontar para uma entidade não semeada —
diferente do SQL Server real. Os testes de `GetByIdAsync` precisam semear **todas** as
entidades relacionadas (contrato **e** `Branch`), não só a que está sendo testada.

## Verificação executada (21/07/2026, dev Yokotobi)

- `dotnet build` 0 erros; **267 testes** verdes em `SiagroB1.Application.Tests`
  (`SalesShipmentReleases/*`: recálculo/consumo por status, cancelamento, close/reopen,
  guard físico na criação, GetAvailable).
- Migrations aplicadas nos dois bancos (connection strings conferidas antes).
- Browser (caminho do usuário, login dev): criar liberação a partir do contrato →
  ativar → faturar 50.000 contra liberação de 100.000 no `/shipment-billing` →
  `ShippedQuantity` 50.000, romaneio `Invoiced` com a chave, invoice `Confirmed` com o
  preço do contrato; contrato esgotado (00000001) sumiu da lista de disponíveis; Detail
  do contrato exibe a seção e os KPIs coerentes.
