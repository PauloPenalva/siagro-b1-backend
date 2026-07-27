# Ocultar armazém e contrato sem saldo na expedição de grãos

Data: 2026-07-27

## Contexto

Na tela `/shipping-transaction` (Expedição de Grãos), a seção "Seleção de Armazém" lista todos os
locais de entrega que têm liberação de entrega ativa para o produto filtrado — inclusive os que já
tiveram todo o volume embarcado, com saldo zerado. O mesmo acontece na tela seguinte, "Seleção de
Contrato": contratos cuja liberação já foi totalmente consumida continuam listados.

Isso obriga o usuário a garimpar a linha útil no meio de linhas que não servem para nada: selecionar
um armazém/contrato sem saldo leva a um embarque que o backend recusaria. A mudança é ocultar da
listagem tudo que não tem saldo a embarcar.

As duas telas são alimentadas por duas funções OData:

- `ShipmentReleasesGetBalance(ItemCode=…)` → `ShipmentReleasesBalanceService` (lista de armazéns)
- `ShipmentReleasesGetPurchaseContracts(ItemCode=…,WarehouseCode=…)` →
  `ShipmentReleasesPurchaseContractsService` (lista de contratos)

Ambas calculam `AvailableQuantity = ReleasedQuantity − ShippedQuantity`.

As mesmas duas funções também alimentam a tela de **Entrada em Armazenagem Própria**
(`storage-entry-transaction`: `SelectWarehouse.controller.ts:97` e
`SelectShipmentRelease.controller.ts:80`). Decisão do usuário: a regra vale para as duas telas — não
faz sentido oferecer liberação sem saldo em nenhuma delas. Por isso o filtro fica no backend, sem
parâmetro novo na função.

## Regra

Uma liberação de entrega só aparece nas listagens se `ReleasedQuantity − ShippedQuantity > 0`.

Saldo **zero e negativo** são igualmente ocultados (decisão do usuário). Negativo só ocorre por
inconsistência de dados e, em ambos os casos, não há nada a embarcar.

O filtro é aplicado **por liberação, antes do agrupamento** — não sobre o total agrupado do armazém:

- Filtrar só o total (`HAVING soma > 0`) esconderia um armazém com liberação A = +100 e B = −100, que
  tem saldo real em A.
- E o "Saldo" exibido na linha do armazém somaria liberações que a tela seguinte não vai listar — os
  dois números divergiriam.

Filtrando por liberação, o armazém desaparece naturalmente quando nenhuma liberação sobrevive, e o
saldo do armazém passa a ser exatamente a soma dos contratos listados na tela de seleção. Traduz para
um `WHERE` simples em SQL, sem `HAVING`.

## Mudanças

### Backend — dois predicados

`SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesBalanceService.cs`, em
`LoadBalancesAsync` (o `Where` que hoje filtra `PurchaseContract.ItemCode` e
`Status == ReleaseStatus.Actived`): acrescentar

```csharp
sr.ReleasedQuantity - sr.ShippedQuantity > 0
```

`SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesPurchaseContractsService.cs`, em
`LoadShipmentReleasesAsync`: o mesmo predicado, no mesmo lugar.

Nenhuma outra alteração no backend: DTOs, controllers (`SiagroB1.Web/Functions/ShipmentReleases/…`),
registro em `ODataConfigurations.cs` e DI ficam como estão.

### Frontend — nenhuma

`Main.controller.ts` / `SelectShipmentRelease.controller.ts` de `shippingTransaction` (e os
equivalentes de `storageEntryTransaction`) apenas jogam o retorno da função em um `JSONModel`. Sem
saldo zerado no retorno, a linha não é renderizada.

Consequência visual: com filtro de produto cujas liberações estão todas consumidas, a tabela fica
vazia — mesmo comportamento já existente quando o produto não tem liberação nenhuma. Sem tratamento
adicional.

## Testes

Projeto `SiagroB1.Application.Tests` (xUnit + EF Core InMemory), reaproveitando `Support/TestDb.cs`,
`Support/FakeBusinessPartnerService.cs`, o stub `EmptyWarehouseService` de
`ShipmentReleasesBalanceServiceShippedTests.cs` e `NullLogger<T>.Instance`.

`ShipmentReleasesBalanceServiceZeroBalanceTests.cs`:

1. Liberação com `ReleasedQuantity == ShippedQuantity` → armazém não aparece.
2. Liberação com `ShippedQuantity > ReleasedQuantity` (negativo) → armazém não aparece.
3. Armazém com duas liberações, uma com saldo 100 e outra zerada → uma linha com
   `AvailableQuantity == 100` (a zerada não polui a soma).
4. Dois armazéns, um com saldo e outro totalmente consumido → só o com saldo é retornado.

`ShipmentReleasesPurchaseContractsServiceZeroBalanceTests.cs`:

1. Liberação zerada → não listada.
2. Liberação negativa → não listada.
3. Duas liberações no mesmo armazém, uma zerada e uma com saldo → só a com saldo é listada, com
   `AvailableQuantity` correto.

## Verificação

1. `dotnet build SiagroB1.sln`
2. `dotnet test SiagroB1.Application.Tests` — testes novos passam e os existentes de
   `ShipmentReleases`/`StorageEntryTransactions` continuam verdes (atenção a
   `ShipmentReleasesBalanceServiceShippedTests` e `ShipmentReleasesBalanceServiceFNameTests`, que
   dependem do mesmo `Where`).
3. Pelo caminho do usuário, no browser: `/shipping-transaction` → produto com uma liberação já
   totalmente embarcada e outra com saldo → só o armazém com saldo aparece, e a coluna "Saldo" bate
   com a soma dos contratos da tela seguinte; "Selecionar Contrato" sem nenhum contrato zerado;
   regressão em `/storage-entry-transaction` com o mesmo comportamento e o fluxo de criação intacto.
