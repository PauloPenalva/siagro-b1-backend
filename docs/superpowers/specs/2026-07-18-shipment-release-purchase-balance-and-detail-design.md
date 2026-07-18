# Design: saldo da liberação por romaneio de compra + tela de detalhe

**Data:** 2026-07-18
**Repositórios:** `siagro-b1-backend`, `siagro-b1-frontend`
**Autor:** Paulo Penalva (com Claude Code)

## Contexto

O trabalho de cancelamento de liberação de embarque (jul/2026) permitiu cancelar uma
liberação `Actived`/`Paused` mesmo com movimentação, devolvendo ao contrato apenas o
saldo não romaneado via `ShipmentRelease.ConsumedQuantity`. Ao especificar uma tela para
exibir o motivo do cancelamento, o usuário pediu também uma tabela dos romaneios de
compra "para conferência do saldo" — e isso expôs um erro na regra de cálculo.

### O problema

`ShipmentReleasesRecalculateShippedService` calcula `ShippedQuantity` a partir de
`SalesShipment` e `SalesShipmentReturn`. Mas o fluxo real de romanear contra uma
liberação cria romaneio de tipo **`Purchase`**:

- `siagro-b1-frontend/webapp/controller/shippingTransaction/Create.controller.ts:82`
  monta o payload com `TransactionType: "Purchase"` fixo e
  `WarehouseCode: data?.DeliveryLocationCode`;
- o `Select` de tipo em `webapp/view/shippingTransaction/fragments/Form.fragment.xml:71`
  é `editable="false"` — o usuário não escolhe.

Consequências no fluxo real, hoje:

1. `ShippedQuantity` permanece **0**;
2. `AvailableQuantity` (`Released − Shipped`) é sempre igual a `ReleasedQuantity` — a
   coluna "Saldo" da lista nunca desce;
3. `ConsumedQuantity` de uma liberação cancelada é **0**, então o cancelamento devolve
   ao contrato o volume **inteiro**, mesmo com romaneios de compra lançados — exatamente
   o que a feature de cancelamento pretendia impedir.

Isto supersede a convenção adotada em
[`2026-07-17-shipment-release-shipped-quantity-design.md`](2026-07-17-shipment-release-shipped-quantity-design.md),
que fixou o par `SalesShipment`/`SalesShipmentReturn`. Aquela decisão auditou os tipos
já presentes no código sem confrontá-los com o fluxo de criação; os tipos de venda
pertencem ao fluxo de `shipmentBilling`, não ao de liberação.

---

# Parte A — Correção da regra de saldo

**Prioritária.** É um defeito que afeta dados existentes, e a Parte B depende dela para
os números da tela reconciliarem.

## A.1 Fórmula

```
ShippedQuantity = Σ Purchase.NetWeight − Σ PurchaseReturn.NetWeight
```

sobre romaneios com `ShipmentReleaseKey == release.Key` e
`TransactionStatus != Cancelled` (mantém a decisão anterior de contar `Pending`).

`PurchaseQtyComplement` e `PurchasePriceComplement` **não** entram, seguindo o
precedente de `PurchaseContractsAllocationCreateService`, que já mapeia ambos para
volume `0`. Sinal de `Purchase` (+) / `PurchaseReturn` (−) segue a convenção canônica de
estoque em `StorageTransactionsConfirmedService.GetWarehouseBalanceAsync`.

`SalesShipment`/`SalesShipmentReturn` saem da conta.

## A.2 Predicado único

A lista de tipos está hoje **duplicada em cinco lugares** — o serviço de recálculo e os
quatro hooks. Foi essa duplicação que permitiu que serviço e hooks divergissem em
silêncio. Extrair um único predicado e usá-lo nos cinco pontos:

```csharp
public static bool AffectsShippedQuantity(StorageTransactionType type) =>
    type is StorageTransactionType.Purchase or StorageTransactionType.PurchaseReturn;
```

Fica em `ShipmentReleasesRecalculateShippedService` (dono da regra). A consulta continua
precisando da expressão de tipos inline para o EF traduzir — o predicado governa os
hooks, que rodam em memória.

## A.3 Pontos de mudança

| Arquivo | Mudança |
|---|---|
| `ShipmentReleasesRecalculateShippedService.cs` | tipos na consulta de `CalculateShippedAsync` + novo predicado |
| `StorageTransactionsCreateService.cs:77` | filtro do hook → predicado |
| `StorageTransactionsConfirmedService.cs:76` | idem |
| `StorageTransactionsCancelService.cs:48` | idem |
| `StorageTransactionsReverseService.cs:60` | idem |
| nova migration | backfill de `ShippedQuantity` |

`ShipmentReleaseMovementGuardService` **não muda**: já cobre venda e compra desde a
extensão de 18/07. Cobrir também os tipos de venda é inofensivo.

`ConsumedQuantity`, `AvailableQuantity`, `PurchaseContract.TotalShipmentReleases` e
`ShipmentReleasesBalanceService` derivam de `ShippedQuantity` e se corrigem sozinhos.

## A.4 Migration de backfill

Migration sem alteração de esquema, apenas `Sql()`, no molde do backfill de
`20260717195858_AddShipmentReleaseShippedQuantity`, trocando os tipos `7,12` por `8,9`:

```sql
UPDATE SR
SET SR.ShippedQuantity = ISNULL((
    SELECT SUM(CASE
                 WHEN t.TransactionType = 8 THEN t.NetWeight   -- Purchase
                 WHEN t.TransactionType = 9 THEN -t.NetWeight  -- PurchaseReturn
                 ELSE 0 END)
    FROM STORAGE_TRANSACTIONS t
    WHERE t.ShipmentReleaseKey = SR.[Key]
      AND t.TransactionStatus <> 2
      AND t.TransactionType IN (8, 9)
), 0)
FROM SHIPMENT_RELEASES SR;
```

`Down` restaura a fórmula antiga (tipos 7/12), para a migration ser reversível.

A migration **não altera esquema** — só dados. Ainda assim precisa do arquivo
`.Designer.cs` (convenção do repo: 167 migrations, todas com o seu), mas o
`AppDbContextModelSnapshot` fica **inalterado**, já que o modelo não muda. Escrever à
mão, sem scaffolding, para não gerar `AlterColumn` espúrio a partir do snapshot
dessincronizado.

**Impacto em dados:** o backfill reescreve `ShippedQuantity` de todas as liberações.
Onde havia romaneio de venda vinculado, o saldo muda. É a correção pretendida, mas é
alteração de dado em produção e deve ser comunicada antes de aplicar.

## A.5 Testes

Os testes existentes que usam `SalesShipment` para mover o saldo passam a usar
`Purchase`. Casos a cobrir:

| Cenário | Esperado |
|---|---|
| `Purchase` 300 numa liberação de 1.000 | `ShippedQuantity` 300, `AvailableQuantity` 700 |
| `Purchase` 400 + `PurchaseReturn` 150 | `ShippedQuantity` 250 |
| `SalesShipment` vinculado a liberação | `ShippedQuantity` **0** (não conta mais) |
| `PurchaseQtyComplement` / `PurchasePriceComplement` | não somam |
| romaneio `Cancelled` | não conta |
| cancelar liberação com `Purchase` 300 de 1.000 | `ConsumedQuantity` 300, contrato recupera 700 |
| cancelar liberação só com `Purchase` cobrindo tudo | recusa por saldo zero ("Utilize a ação Finalizar") |
| hook de Create/Confirm/Cancel/Reverse com `Purchase` | dispara o recálculo |
| hook com `SalesShipment` | **não** dispara |

---

# Parte B — Página de detalhe da liberação

## B.1 Rota e navegação

Rota `shipmentReleasesDetail`, padrão `shipment-releases/{id}/detail`, target nível 2 com
`clearControlAggregation: true` — igual aos 10 detalhes existentes.

Entrada por botão **"Visualizar"** na toolbar da lista, com o `onDetail()` que já existe
verbatim em `storageInvoices`, `purchaseContracts` e outros:

```ts
onDetail(): void {
  const oTable = this.byId("tableShipmentReleases") as Table;
  const i = oTable.getSelectedIndex();
  if (i < 0) { MessageBox.warning("Selecione um registro."); return; }
  const oContext = oTable.getContextByIndex(i);
  this.navTo("shipmentReleasesDetail", { id: oContext.getProperty("Key") as string });
}
```

## B.2 Estrutura

`ObjectPageLayout` com `busy="{ui>/busy}"`, **somente leitura** (`ui>/editable = false`
no pattern-matched; não há tela de edição de liberação — `ShipmentReleasesUpdateService`
lança `MethodAccessException` de propósito). Sem ações de documento: elas continuam só
na lista.

Cabeçalho: `Code`, `ObjectStatus` com os formatters existentes
`formatShipmentReleaseStatus` / `stateShipmentReleaseStatus`, e o saldo.

**Seção Dados** — contrato, fornecedor, produto, armazém, data de liberação, e o trio
Liberado / Romaneado / Saldo.

**Seção Auditoria** — Criado por·em, Aprovado por·em, Cancelado por·em e **Motivo do
cancelamento** (`TextArea` read-only). O bloco de cancelamento usa
`visible="{= !!${CanceledAt} }"`. Nenhuma tela do app exibe auditoria hoje; o molde é o
bloco comentado em `storageInvoices/fragments/Form.fragment.xml:161`
(`Label "Motivo de Cancelamento"` + `TextArea editable="false" value="{CancellationReason}"`).

**Seção Romaneios** — `sap.ui.table.Table` com Código, Data, Tipo, Armazém, Peso Líquido
e Status.

Formulários em `SimpleForm` com `editable="{ui>/editable}"`, seguindo a convenção das 10
telas existentes. A skill oficial de UI5 desencoraja `SimpleForm` em favor de `Form` +
`ColumnLayout`; a consistência com o codebase foi julgada mais valiosa do que corrigir
essa dívida numa tela isolada. Migrar isso é trabalho à parte, em todas as telas.

Textos em pt-BR hardcoded: não há uso de i18n em nenhuma view do app.

## B.3 Binding da tabela de romaneios

Bindar em `/StorageTransactions` com filtro
`ShipmentReleaseKey eq {id} and (TransactionType eq 'Purchase' or TransactionType eq 'PurchaseReturn')`,
aplicado no handler de rota — **não** na navegação `Transactions`.

Motivo: `ShipmentReleasesGetService.GetByIdAsync` devolve a entidade já materializada, de
modo que `$expand` via `[EnableQuery]` não se aplica de forma confiável. Um list binding
próprio ainda ganha paginação e ordenação de graça, sem tocar no backend.

**Conferência:** o campo *Romaneado* da seção Dados é exatamente a soma da coluna Peso
Líquido (com `PurchaseReturn` subtraindo), e *Saldo = Liberado − Romaneado*.

## B.4 Backend — expor `ConsumedQuantity`

`ConsumedQuantity` é `[NotMapped]`; com `autoExpandSelect: true` no manifest, o modelo
monta um `$select` que só inclui propriedades do EDM. Registrar como já foi feito para
`AvailableQuantity` (`ODataConfigurations.cs:53-55`):

```csharp
modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(ShipmentRelease))
    .AddProperty(typeof(ShipmentRelease).GetProperty(nameof(ShipmentRelease.ConsumedQuantity)));
```

É o campo que torna visível *por que* o contrato devolveu 700 de uma liberação de 1.000.

Os campos de auditoria (`CreatedAt/By`, `ApprovedAt/By`, `CanceledAt/By`) e
`CancellationReason` são colunas mapeadas — já expostos, sem mudança.

## B.5 Arquivos

| Arquivo | Ação |
|---|---|
| `webapp/view/shipmentReleases/Detail.view.xml` | novo |
| `webapp/view/shipmentReleases/fragments/Form.fragment.xml` | novo (seção Dados) |
| `webapp/view/shipmentReleases/fragments/Audit.fragment.xml` | novo (seção Auditoria) |
| `webapp/view/shipmentReleases/fragments/Transactions.fragment.xml` | novo (tabela) |
| `webapp/controller/shipmentReleases/Detail.controller.ts` | novo |
| `webapp/manifest.json` | rota + target |
| `webapp/view/shipmentReleases/Main.view.xml` | botão "Visualizar" |
| `webapp/controller/shipmentReleases/Main.controller.ts` | `onDetail()` |
| `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` | `AddProperty(ConsumedQuantity)` |

---

## Fora de escopo

- Migrar `SimpleForm` → `Form` + `ColumnLayout` nas telas existentes.
- Introduzir i18n.
- Ações de documento na tela de detalhe.
- Tratamento de "não encontrado" — nenhuma tela do app o faz hoje; erros de OData
  aparecem pelo handler global de `Component.ts`.

## Verificação

1. `dotnet test SiagroB1.Application.Tests` — suíte verde, incluindo os casos da A.5.
2. `dotnet ef migrations has-pending-model-changes` — sem alterações pendentes.
3. Aplicar a migration **com `ASPNETCORE_ENVIRONMENT` explícito** e conferir no banco que
   `ShippedQuantity` de uma liberação com romaneio de compra deixou de ser 0.

   > ⚠️ Não usar o launch profile `db-migration`: ele define `ASPNETCORE_ENVIRONMENT=Migration`,
   > não existe `appsettings.Migration.json`, e o fallback para `appsettings.json` aponta para
   > **`IDX_SIAGRO_PRD` (produção)**. Bancos: `Development` → `MHAGRO_SIAGRO_HOM`,
   > `Staging` → `IDX_SIAGRO_HOM`, `Yokotobi` → `IDX_SIAGRO_DEV`.
4. Manual, ponta a ponta:
   - contrato aprovado de 1.000, liberação de 1.000 aprovada no armazém A;
   - romanear 300 (`Purchase` confirmado) → coluna Saldo da lista mostra 700;
   - abrir o detalhe: Liberado 1.000 / Romaneado 300 / Saldo 700, e a tabela lista o
     romaneio de 300 — os números fecham;
   - cancelar com motivo "troca de armazém" → contrato volta a ter 700 disponíveis;
   - reabrir o detalhe: seção Auditoria mostra quem cancelou, quando e o motivo;
   - criar liberação de 700 no armazém B e aprovar;
   - tentar romanear contra a liberação cancelada → bloqueado pelo guard.
5. Frontend: `yarn ts-typecheck` e `npx ui5lint` sem achados novos nos arquivos tocados.
