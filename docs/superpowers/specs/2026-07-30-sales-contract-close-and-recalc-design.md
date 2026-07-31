# Encerramento e recálculo de saldo no contrato de venda — design

Data: 2026-07-30

## Problema

A tela de Detalhe do **contrato de compra** oferece três ações no header: *Recalcular Saldo*,
*Encerrar* e *Reabrir*. A tela de Detalhe do **contrato de venda** não oferece nenhuma delas,
embora o backend de venda já tenha os três serviços prontos e registrados:

| Serviço | Action OData | Controller |
|---|---|---|
| `SalesContractsCloseService` (`Approved → Finished`, com guard de fixação PAF) | `SalesContractsClose` | `Actions/SalesContracts/SalesContractsCloseController` |
| `SalesContractsReopenService` (`Finished → Approved`) | `SalesContractsReopen` | `Actions/SalesContracts/SalesContractsReopenController` |
| `SalesContractsRecalculateBalanceService` (unitário e em lote) | `SalesContractsRecalculateBalance` / `...AllBalances` | `Actions/SalesContracts/SalesContractsRecalculateBalance(All)Controller` |

Ou seja: a capacidade existe, mas é inalcançável pelo usuário — só a tela ADMIN de conciliação
(`/sales-contracts/reconciliation`) chama o recálculo, e em lote.

Falta também uma regra de negócio: **encerrar um contrato com saldo negativo**. Saldo negativo
(`TotalVolume − AllocatedVolume < 0`) significa contrato faturado ALÉM do volume contratado —
um erro de distribuição que precisa ser conciliado, não congelado. Encerrar nesse estado
esconde o problema: o contrato sai das listas de alocação e do recálculo em lote, e o volume
excedente fica órfão.

## Escopo

1. Botões *Recalcular Saldo*, *Encerrar* e *Reabrir* na tela de Detalhe do contrato de venda.
2. Trava de saldo negativo no encerramento — **nos dois lados**, venda e compra.

Fora de escopo: saldo positivo (contrato entregue só em parte) continua podendo ser encerrado —
encerrar é justamente como se abre mão do resto. Nenhuma alteração no recálculo em lote, na tela
de conciliação, nem no guard de fixação PAF que já existe.

## Decisão de design: a trava lê o saldo recalculado, não o persistido

`AllocatedVolume` é **persistido-derivado** e pode ficar defasado — é o que o próprio
`SalesContractsGetNegativeBalancesService` documenta: parte dos saldos negativos é drift do
agregado, não distribuição errada.

- **Ler o valor persistido** (`contract.AvaiableVolume`) é uma linha e casa com o número da
  tela, mas **barra por engano** um contrato correto cujo agregado drifou — e o usuário não tem
  como distinguir um caso do outro.
- **Recalcular do ledger na hora** custa uma query `SUM` (a mesma fonte que o botão *Recalcular
  Saldo* usa) e decide sobre o valor verdadeiro. Nunca barra por drift.

Adotado: **recalcular na hora**. O encerramento **não persiste** o valor recalculado — continua
sem efeito colateral além da mudança de status. Se o número da mensagem divergir do que está na
tela, o botão *Recalcular Saldo*, que fica ao lado, reconcilia.

## Backend

### Venda — `SalesContractsCloseService`

Novo `GuardNegativeBalanceAsync`, aplicado a **todos os tipos** de contrato, antes de mudar o
status. O guard de fixação PAF existente continua valendo só para `ContractType.ToBeDetermined`
e roda antes.

```csharp
var allocated = await SalesContractsRecalculateBalanceService
    .CalculateAllocatedAsync(context, contract.Key);
var balance = decimal.Round(contract.TotalVolume - allocated, 3, MidpointRounding.ToEven);

if (balance < 0)
    throw new ApplicationException(
        $"Contrato faturado além do volume contratado. Contratado: {contract.TotalVolume:N3}, " +
        $"alocado: {allocated:N3}, saldo: {balance:N3}. " +
        "Ajuste as alocações na tela de conciliação de saldos antes de encerrar.");
```

`ApplicationException` já é mapeada para 400 pelo `SalesContractsCloseController`.

### Compra — `PurchaseContractsCloseService`

Mesmo guard, mesma mensagem. Para reusar a fórmula sem duplicá-la, extrair de
`PurchaseContractsRecalculateBalanceService.RecalculateAsync` (hoje privado) um
`public static Task<decimal> CalculateAllocatedAsync(AppDbContext, Guid)` com o `Σ a.Volume`
assinado — espelhando o que o serviço de venda já expõe — e passar a usá-lo nos dois lugares.
`PurchaseContract.AvaiableVolume` usa a mesma fórmula `TotalVolume − AllocatedVolume`, com
arredondamento em 2 casas (venda usa 3); cada lado mantém a sua precisão.

Sem migration, sem entidade nova, sem action nova.

## Frontend

### `view/salesContracts/Detail.view.xml`

Três botões no `<uxap:actions>`, no formato idêntico ao da compra
(`view/purchaseContracts/Detail.view.xml:34-36`):

```xml
<Button visible="{= ${path: 'Status', targetType: 'any'}==='Approved' &amp;&amp; !${ui>/readonly} }" text="Recalcular Saldo" type="Transparent" press=".onRecalculateBalance"/>
<Button visible="{= ${path: 'Status', targetType: 'any'}==='Approved' &amp;&amp; !${ui>/readonly} }" text="Encerrar" type="Transparent" press=".onCloseContract"/>
<Button visible="{= ${path: 'Status', targetType: 'any'}==='Finished' &amp;&amp; !${ui>/readonly} }" text="Reabrir" type="Transparent" press=".onReopenContract"/>
```

O `targetType: 'any'` no `Status` é obrigatório (enum do modelo v4 chega formatado sem ele) e o
`!${ui>/readonly}` preserva o modo somente-leitura da fila de aprovação de fixações.

### `controller/salesContracts/Detail.controller.ts`

Os três handlers ficam no **Detail**, não no `SalesContractsBaseController` — só esta tela usa.
Duas diferenças em relação à compra, ambas simplificações:

- **Recalcular** usa `bindContext` + `setParameter("Key", key)` + `invoke()` e lê o DTO por
  `getBoundContext().getObject()` — o padrão OData v4 que `salesContracts/reconciliation/Main.controller.ts`
  já usa para a variante em lote. Reaproveita a rota `salesContractsRecalculateBalance`
  (`'/SalesContractsRecalculateBalance(...)'`) que já existe no `ServerRoutes`, evitando uma
  segunda entrada apontando para o mesmo endpoint. O `(...)` é obrigatório: sem ele o
  `invoke()` falha com "The binding must be deferred", e só quebra no browser.
- **Sem viewModel para atualizar**: a tela de venda liga o Saldo direto em `AvaiableVolume` da
  entidade (`Detail.view.xml:100`), então `oContext.refresh()` já repinta o número. A de compra
  precisa mexer em `viewModel>/AvaiableVolume` porque lê os totais de um endpoint separado.

Feedback do recálculo espelha a compra: `MessageBox.information` com antes → depois quando
`Changed`, `MessageToast` "Saldo já estava correto." caso contrário.

`Encerrar`/`Reabrir` seguem o `jQuery.ajax` POST `{Key}` de
`PurchaseContractsBaseController.onCloseContract/onReopenContract`, com `confirmDialog` antes e
`MessageBox.error(err.responseJSON.error.message)` na falha — é por aí que a mensagem da trava
chega ao usuário.

Novo type `types/SalesContractRecalcResult.ts` espelhando `PurchaseContractRecalcResult`.

### `model/ServerRoutes.ts`

Duas rotas novas, no formato REST usado pelas demais ações de venda:

```ts
salesContractsClose: '/odata/SalesContractsClose',
salesContractsReopen: '/odata/SalesContractsReopen',
```

## Testes

`SiagroB1.Application.Tests`:

- **Venda** (`SalesContracts/SalesContractsCloseNegativeBalanceGuardTests.cs`, novo):
  saldo negativo barra com `ApplicationException`; saldo positivo encerra; saldo zerado encerra;
  e o caso que prova a decisão de design — `AllocatedVolume` **persistido** negativo mas ledger
  dizendo o contrário **encerra normalmente** (nada de falso bloqueio por drift).
- **Compra** (`PurchaseContracts/PurchaseContractsCloseReopenServiceTests.cs`, casos novos):
  os mesmos quatro casos.
- Regressão: `SalesContractsCloseFixationGuardTests` continua verde — os contratos de teste dele
  precisam de saldo não-negativo para chegarem ao guard de fixação.

## Verificação

1. `dotnet test SiagroB1.Application.Tests` e `dotnet build SiagroB1.sln`.
2. `yarn ts-typecheck`, `yarn lint`, `yarn ui5lint` (sem achados novos nos arquivos tocados).
3. No browser (perfil `yktb`, `yarn start:dev`, `admin`/`1234`), em `/sales-contracts/{id}/detail`:
   contrato `Approved` mostra *Recalcular Saldo* e *Encerrar*; recalcular exibe antes → depois e
   repinta o Saldo do header; encerrar um contrato com saldo negativo mostra a mensagem da trava
   e **não** muda o status; contrato `Finished` mostra *Reabrir*. Contratos negativos podem ser
   localizados pela tela `/sales-contracts/reconciliation`.
