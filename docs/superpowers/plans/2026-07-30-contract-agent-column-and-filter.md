# GAC-1125 — Agente comercial (Comprador/Vendedor) nas telas de contrato — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cada agente comercial passa a conseguir isolar "os meus contratos" em todas as telas de lista operacionais de compra e de venda, através de uma coluna e um filtro de Agente (rotulados "Comprador" na compra e "Vendedor" na venda).

**Architecture:** Mudança quase inteiramente de frontend. O agente já está desnormalizado em `PurchaseContract`/`SalesContract` como o par `AgentCode` (`int?`) + `AgentName` (`string?`), e todas as 9 telas alvo bindam um entity set ou uma função `[EnableQuery]` que expõe esse par direto ou por navegação — nenhuma precisa de endpoint novo. Cada tela recebe (a) uma `<t:Column>` nova exibindo `AgentName`, (b) um `<fb:FilterGroupItem>` reusando o value help de agentes que já existe, e (c) um ramo `else if` explícito no `applyFilters()` do controller, porque `AgentCode` é `Edm.Int32` e o ramo genérico geraria `contains(...)`. A única mudança de backend é acrescentar `AgentCode`/`AgentName` ao `PurchaseContractDto`, que alimenta o diálogo "Contratos disponíveis" da Alocação de Romaneios.

**Tech Stack:** OpenUI5/SAPUI5 1.141 + TypeScript (`siagro-b1-frontend`); .NET 10 + EF Core + OData v4 + xUnit (`siagro-b1-backend`).

## Global Constraints

- **Identificadores em inglês, texto ao usuário em pt-BR.** `AgentCode`/`AgentName` nas propriedades e nas chaves do model `filter`; `"Comprador"`/`"Vendedor"` nos `label=` das colunas e dos filtros.
- **Nunca commitar nem dar push.** Os commits são feitos manualmente pelo usuário. A única operação git permitida é `git add` de arquivo novo, no sub-repo a que ele pertence.
- **Todo arquivo novo precisa de `git add`** logo após ser criado.
- **`AgentCode` é `Edm.Int32`**: o `$filter` é `AgentCode eq 5` — sem aspas, sem `contains`. Valor não numérico digitado à mão deve ser ignorado, não enviado.
- **Não usar `descriptionProperty` nos Inputs de filtro.** Gravar `AgentName` no model `filter` faz o loop do `applyFilters()` emitir um filtro extra por `AgentName`, propriedade que não existe na raiz das telas de liberação/alocação → erro do OData.
- **Reusar o value help existente** (`webapp/dialogs/fragments/AgentsSelectDialog.fragment.xml` + `CommonController.openAgentsValueHelp`, que já filtra `Inactive eq 'N'`). Não criar diálogo novo.
- **Sem i18n**: `webapp/i18n/*.properties` ainda é o template do gerador e nenhuma view usa `i18n>`. Labels novos vão hardcoded em pt-BR no XML.
- **Nenhuma migration nesta entrega** — nenhuma coluna nova, nenhuma tela nova, nenhum item de menu novo.
- Gate do frontend, obrigatório ao fim de cada tarefa de frontend: `yarn ts-typecheck`, `yarn lint`, `yarn ui5lint`.
- Gate do backend: `dotnet build SiagroB1.sln` + `dotnet test SiagroB1.Application.Tests` (suíte hoje: 655 verdes).

---

## Achados da exploração (não repetir esse trabalho)

### Como o agente é modelado

- **Não existe navigation property contrato → agente**, de propósito. O par é desnormalizado: `SiagroB1.Domain/Entities/PurchaseContract.cs:27-30` e `SiagroB1.Domain/Entities/SalesContract.cs:27-30` (`public int? AgentCode`, `[Column(TypeName="VARCHAR(100)")] public string? AgentName`).
- A FK para `AGENTS` **existiu e foi removida de propósito** pela migration `SiagroB1.Migrations/AppContext/20260108184649_AlterTablePurchaseContractsAddColumnAgentName.cs` — em modo `Erp=SAPB1` a tabela local fica vazia e o INNER JOIN zeraria a coleção. **Não recriar FK nem nav property.**
- O cadastro é o entity set OData `Agents` (`SiagroB1.Web/ODataConfig/ODataConfigurations.cs:113`), DTO `AgentModel` (`Code:int` / `Name` / `Inactive`); em `SAPB1` lê `OSLP`, em `STANDALONE` lê `AGENTS`.
- `ShipmentRelease`, `SalesShipmentRelease`, `PurchaseContractAllocation`, `SalesContractAllocation` e `StorageTransaction` **não têm campo de agente** — só se chega nele navegando até o contrato.

### Disponibilidade do dado, por tela

| # | Tela | Binding da tabela | Caminho até o agente | Backend muda? |
|---|---|---|---|---|
| 1 | Contratos de Compra | `/PurchaseContracts` | `AgentCode` / `AgentName` | Não |
| 2 | Ctr. Compra — Liberação de entregas | `/PurchaseContractsGetShipmentReleasesAvailable` | `AgentCode` / `AgentName` | Não |
| 3 | Liberações de Entrega (compra) | `/ShipmentReleases` | `PurchaseContract/AgentCode` / `.../AgentName` | Não |
| 4 | Alocação de Romaneios de Compra | `/StorageTransactions` | **não existe** (romaneio não tem agente) | Só o DTO do diálogo |
| 4b | Diálogo "Contratos disponíveis" | `/PurchaseContractsGetAvaiablesList(...)` | `PurchaseContractDto` só tem 4 campos | **Sim** |
| 5 | Entregas de Ctr. de Compra | `/PurchaseContractsAllocations` | `PurchaseContract/AgentCode` / `.../AgentName` | Não |
| 6 | Contratos de Venda | `/SalesContracts` | `AgentCode` / `AgentName` | Não |
| 7 | Ctr. Venda — Liberação de entregas | `/SalesContractsGetShipmentReleasesAvailable` | `AgentCode` / `AgentName` | Não |
| 8 | Liberações de Entrega de Venda | `/SalesShipmentReleases` | `SalesContract/AgentCode` / `.../AgentName` | Não |
| 9 | Entregas de Ctr. de Venda | `/SalesContractsAllocations` | `SalesContract/AgentCode` / `.../AgentName` | Não |

As duas funções das telas 2 e 7 são `[EnableQuery]` sobre `IEnumerable<PurchaseContract>` / `IEnumerable<SalesContract>` (`SiagroB1.Web/Functions/PurchaseContracts/PurchaseContractsGetShipmentReleasesAvailableController.cs:13-17` e o par de venda), sem projeção — devolvem a entidade inteira, então `$filter=AgentCode eq N` funciona server-side e `{AgentName}` está disponível na linha.

### Padrão de UI existente (reutilizar, não inventar)

- Todas as telas: `sap.f.DynamicPage` + `sap.ui.comp.filterbar.FilterBar` (fragmento `Filterbar.fragment.xml`) + `sap.ui.table.Table`.
- Filtro: two-way binding num `JSONModel` chamado `filter` (`{filter>/Campo}`), criado por `CommonController.createFilterModel()` (`webapp/controller/common/CommonController.ts:29-36`); `clearFilters()` (linha 34) simplesmente recria o model.
- `applyFilters()` de cada controller monta **string OData crua** e chama `oBinding.changeParameters({ $filter })` sobre `table.getBinding("rows")`.
- Value help de agentes: `CommonController.openAgentsValueHelp` (`CommonController.ts:243-246`), hoje usado em `PurchaseContractForm.fragment.xml:125-141` (label "Comprador") e `SalesContractForm.fragment.xml:125-141` (label "Vendedor").
- Export Excel (`createColumnConfig()` + `onExcel()`) existe em **4** das 9 telas — 3, 5, 8 e 9. A lista de colunas é duplicada em TypeScript no `BaseController` de cada uma, então coluna nova na tabela tem que ser replicada lá. As telas 1, 2, 6 e 7 não têm export.

### Armadilhas

1. **`AgentCode` é `Edm.Int32`** — ver Global Constraints. Cada `applyFilters()` precisa do seu `else if`; o `else` genérico produziria `contains(AgentCode,'5')`, que o OData rejeita.
2. **`descriptionProperty` no Input de filtro quebra as telas 3, 5, 8 e 9** — ver Global Constraints.
3. **`openAgentsValueHelp` busca só por `['Name']`** — digitar código dentro do diálogo não encontra nada, porque o `DialogHelper` monta `Contains`, inválido em `Edm.Int32`. Comportamento pré-existente; **não é regressão desta entrega e não está no escopo**.
4. **`suspended: true` nos bindings de `/Branchs` dentro da FilterBar é obrigatório** — a `sap.ui.comp.filterbar.FilterBar` dá `resume()` nos bindings dos seus controles na inicialização; sem `suspended` a rota inteira não renderiza. Não mexer nesses Selects.
5. **A tabela do diálogo "Contratos disponíveis" binda `rows="{viewModel>/}"`** (JSONModel), então **toda** coluna precisa do prefixo `viewModel>`. A coluna "Filial" existente (`PurchaseContractsAvaiables.fragment.xml:62-70`) binda `{Branch/ShortName}` sem prefixo e por isso renderiza vazia — **bug pré-existente, fora do escopo** (corrigir exigiria expor Filial no DTO). Não copiar esse padrão na coluna nova.

### Bugs pré-existentes que esta entrega corrige (Tarefa 6)

- `webapp/controller/purchaseContracts/allocation/Main.controller.ts:24` registra `getRoute("purchaseOrdersAllocations")`, mas a rota da tela é `purchaseContractsAllocations`. Os filtros só reaplicam quando a rota da OUTRA tela dispara — o filtro novo de Comprador pareceria não funcionar ao entrar na tela. **Corrigir é pré-requisito da tarefa.**
- `webapp/controller/purchaseContracts/allocation/BaseController.ts:80-84`: a coluna `StorageTransaction/WarehouseCode` do Excel está rotulada `"Un.Med."` (duplicando a coluna anterior). Uma palavra, na mesma função que a tarefa edita.

---

## File Structure

Backend (`siagro-b1-backend`) — 3 arquivos:

| Arquivo | Responsabilidade |
|---|---|
| `SiagroB1.Domain/Dtos/PurchaseContractDto.cs` (modificar) | Contrato de dados do diálogo "Contratos disponíveis"; ganha `AgentCode`/`AgentName` |
| `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsGetService.cs` (modificar) | `GetAvaiablesPurchaseContracts` passa a projetar os dois campos |
| `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsGetAvaiablesListAgentTests.cs` (criar) | Prova que a projeção carrega o agente |

Frontend (`siagro-b1-frontend`) — 20 arquivos, todos modificações. Por tela: a `Main.view.xml` (coluna), o `Filterbar.fragment.xml` (filtro), a `Main.controller.ts` (ramo do `applyFilters`), e o `BaseController.ts` onde há export Excel.

| Tela | View | Filterbar | Controller | Excel |
|---|---|---|---|---|
| 1 | `view/purchaseContracts/Main.view.xml` | `view/purchaseContracts/fragments/PurchaseContractFilterbar.fragment.xml` | `controller/purchaseContracts/Main.controller.ts` | — |
| 2 | `view/purchaseContracts/shipmentRelease/Main.view.xml` | `view/purchaseContracts/shipmentRelease/fragments/Filterbar.fragment.xml` | `controller/purchaseContracts/shipmentRelease/Main.controller.ts` | — |
| 3 | `view/shipmentReleases/Main.view.xml` | `view/shipmentReleases/fragments/Filterbar.fragment.xml` | `controller/shipmentReleases/Main.controller.ts` | `controller/shipmentReleases/BaseController.ts` |
| 4b | `view/purchaseOrders/allocation/fragments/PurchaseContractsAvaiables.fragment.xml` | — | — | — |
| 5 | `view/purchaseContracts/allocation/Main.view.xml` | `view/purchaseContracts/allocation/fragments/Filterbar.fragment.xml` | `controller/purchaseContracts/allocation/Main.controller.ts` | `controller/purchaseContracts/allocation/BaseController.ts` |
| 6 | `view/salesContracts/Main.view.xml` | `view/salesContracts/fragments/SalesContractFilterbar.fragment.xml` | `controller/salesContracts/Main.controller.ts` | — |
| 7 | `view/salesContracts/shipmentRelease/Main.view.xml` | `view/salesContracts/shipmentRelease/fragments/Filterbar.fragment.xml` | `controller/salesContracts/shipmentRelease/Main.controller.ts` | — |
| 8 | `view/salesShipmentReleases/Main.view.xml` | `view/salesShipmentReleases/fragments/Filterbar.fragment.xml` | `controller/salesShipmentReleases/Main.controller.ts` | `controller/salesShipmentReleases/BaseController.ts` |
| 9 | `view/salesContracts/allocation/Main.view.xml` | `view/salesContracts/allocation/fragments/Filterbar.fragment.xml` | `controller/salesContracts/allocation/Main.controller.ts` | `controller/salesContracts/allocation/BaseController.ts` |

Uma tarefa por tela: cada uma é uma superfície que o usuário abre e testa isoladamente, e um revisor pode rejeitar a tela 3 aprovando a tela 1.

### Convenção de chave no model `filter`

A chave depende de como o `applyFilters()` daquela tela já nomeia as coisas — os dois padrões existentes são mantidos, não normalizados:

| Telas | Chave no model `filter` | `$filter` gerado |
|---|---|---|
| 1, 2, 6, 7 (agente na raiz) | `AgentCode` | `AgentCode eq N` |
| 3 (liberações de compra) | `AgentCode` | `PurchaseContract/AgentCode eq N` |
| 8 (liberações de venda) | `AgentCode` | `SalesContract/AgentCode eq N` |
| 5 (entregas de compra) | `PurchaseContractAgentCode` | `PurchaseContract/AgentCode eq N` |
| 9 (entregas de venda) | `SalesContractAgentCode` | `SalesContract/AgentCode eq N` |

---

## Task 1: Backend — `AgentCode`/`AgentName` no `PurchaseContractDto`

**Files:**
- Modify: `SiagroB1.Domain/Dtos/PurchaseContractDto.cs`
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsGetService.cs:48-75`
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsGetAvaiablesListAgentTests.cs` (criar)

**Interfaces:**
- Consumes: nada (primeira tarefa).
- Produces: `PurchaseContractDto` com `int? AgentCode` (`[JsonPropertyName("AgentCode")]`) e `string? AgentName` (`[JsonPropertyName("AgentName")]`), populados por `PurchaseContractsGetService.GetAvaiablesPurchaseContracts(string cardCode, string itemCode)`. A Tarefa 5 depende desses dois nomes, que chegam ao UI5 dentro do `viewModel` do diálogo.

- [ ] **Step 1: Escrever o teste que falha**

Criar `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsGetAvaiablesListAgentTests.cs`. O fixture segue `PurchaseContractsGetShipmentReleasesAvailableServiceTests.cs`: `TestDb.CreateUnitOfWork()` + `NullLogger`. `AvaiableVolume` é `[NotMapped]` e vale `TotalVolume − AllocatedVolume` (`PurchaseContract.cs:225-226`), então basta semear `TotalVolume` com `AllocatedVolume` em zero para o contrato entrar na lista.

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// O diálogo "Contratos disponíveis" da Alocação de Romaneios mostra a coluna
/// Comprador (GAC-1125). O romaneio não tem agente, então o dado só chega ali
/// pela projeção deste DTO.
/// </summary>
public class PurchaseContractsGetAvaiablesListAgentTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsGetService Service() =>
        new(_db, NullLogger<PurchaseContractsGetService>.Instance);

    private async Task SeedAsync(int? agentCode, string? agentName)
    {
        _db.Context.PurchaseContracts.Add(new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-001", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", DeliveryLocationCode = "01",
            TotalVolume = 1000m, AllocatedVolume = 0m, Status = ContractStatus.Approved,
            AgentCode = agentCode, AgentName = agentName,
        });
        await _db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task ContractWithAgent_DtoCarriesCodeAndName()
    {
        await SeedAsync(5, "JOAO COMPRADOR");

        var dto = Assert.Single(Service().GetAvaiablesPurchaseContracts("F0001", "SOJA"));

        Assert.Equal(5, dto.AgentCode);
        Assert.Equal("JOAO COMPRADOR", dto.AgentName);
    }

    [Fact]
    public async Task ContractWithoutAgent_DtoCarriesNulls()
    {
        await SeedAsync(null, null);

        var dto = Assert.Single(Service().GetAvaiablesPurchaseContracts("F0001", "SOJA"));

        Assert.Null(dto.AgentCode);
        Assert.Null(dto.AgentName);
    }
}
```

- [ ] **Step 2: Rodar o teste e conferir que falha**

```bash
cd siagro-b1-backend
dotnet test SiagroB1.Application.Tests --filter PurchaseContractsGetAvaiablesListAgentTests
```

Esperado: **erro de compilação** — `PurchaseContractDto` não tem `AgentCode`/`AgentName`. É a falha correta nesta etapa; não seguir sem vê-la.

- [ ] **Step 3: Acrescentar os campos ao DTO**

Em `SiagroB1.Domain/Dtos/PurchaseContractDto.cs`, depois de `UnitOfMeasureCode`:

```csharp
    [JsonPropertyName("AgentCode")]
    public int? AgentCode { get; set; }

    [JsonPropertyName("AgentName")]
    public string? AgentName { get; set; }
```

- [ ] **Step 4: Projetar os campos no serviço**

Em `PurchaseContractsGetService.GetAvaiablesPurchaseContracts`, dentro do `new PurchaseContractDto { ... }` (linhas 64-70), acrescentar as duas atribuições:

```csharp
                responseList.Add(new PurchaseContractDto
                {
                    Key = x.Key,
                    Code = x.Code,
                    AvaiableVolume = x.AvaiableVolume,
                    UnitOfMeasureCode = x.UnitOfMeasureCode,
                    AgentCode = x.AgentCode,
                    AgentName = x.AgentName,
                });
```

- [ ] **Step 5: Rodar o teste e conferir que passa**

```bash
dotnet test SiagroB1.Application.Tests --filter PurchaseContractsGetAvaiablesListAgentTests
```

Esperado: **2 passed**.

- [ ] **Step 6: Suíte inteira + build**

```bash
dotnet build SiagroB1.sln
dotnet test SiagroB1.Application.Tests
```

Esperado: build sem erro e **657 passed** (655 de antes + 2). Nenhuma migration é gerada — `PurchaseContractDto` não é entidade.

- [ ] **Step 7: Stage do arquivo novo**

```bash
cd siagro-b1-backend
git add SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsGetAvaiablesListAgentTests.cs
git add docs/superpowers/plans/2026-07-30-contract-agent-column-and-filter.md
```

Não commitar.

---

## Task 2: Tela 1 — Contratos de Compra

**Files:**
- Modify: `webapp/view/purchaseContracts/Main.view.xml` (inserir coluna entre "Fornecedor", que termina na linha 166, e "Cod.Produto", que abre na 167)
- Modify: `webapp/view/purchaseContracts/fragments/PurchaseContractFilterbar.fragment.xml` (inserir grupo depois de `businessPartners`, linhas 67-77)
- Modify: `webapp/controller/purchaseContracts/Main.controller.ts` (`type FilterData` linhas 12-21; `applyFilters` linhas 46-70)

**Interfaces:**
- Consumes: nada.
- Produces: o trio (coluna + `FilterGroupItem name="agent"` + ramo `AgentCode` no `applyFilters`) que as Tarefas 3, 7 e 8 replicam para as outras telas com agente na raiz.

- [ ] **Step 1: Adicionar a coluna "Comprador" na view**

Em `webapp/view/purchaseContracts/Main.view.xml`, entre o `</t:Column>` de "Fornecedor" e o `<t:Column>` de "Cod.Produto":

```xml
          <t:Column
						label="Comprador"
						sortProperty="AgentName"
            filterProperty="AgentName"
            filterOperator="Contains"
            width="12rem"
						>
						<t:template>
            <Text text="{AgentName}" wrapping="false"/>
						</t:template>
					</t:Column>
```

- [ ] **Step 2: Adicionar o filtro "Comprador" na FilterBar**

Em `PurchaseContractFilterbar.fragment.xml`, depois do `</fb:FilterGroupItem>` de `businessPartners`:

```xml
      <fb:FilterGroupItem name="agent" label="Comprador" groupName="GroupAgent" visibleInFilterBar="true">
        <fb:control>
            <Input
              showClearIcon="true"
              showValueHelp="true"
              valueHelpRequest=".openAgentsValueHelp"
              value="{filter>/AgentCode}"
              />
        </fb:control>
      </fb:FilterGroupItem>
```

Sem `<core:CustomData key="descriptionProperty" .../>` — ver Global Constraints.

- [ ] **Step 3: Declarar a chave no `FilterData` e tratar o `Edm.Int32` no `applyFilters`**

Em `webapp/controller/purchaseContracts/Main.controller.ts`, no `type FilterData` (linhas 12-21) acrescentar `AgentCode?: string,` (o model `filter` guarda o texto do Input, por isso `string`). No `applyFilters`, acrescentar o ramo **antes** do `else` genérico:

```ts
      if (filterKey == "Status" || filterKey == "Type" || filterKey == "MarketType") {
        filters.push(`${filterKey} eq '${value}'`)
      } else if (filterKey == "AgentCode") {
        // Edm.Int32: comparação numérica, sem aspas e sem contains. Texto digitado
        // à mão que não seja número é ignorado em vez de gerar $filter inválido.
        if (!isNaN(Number(value))) filters.push(`AgentCode eq ${Number(value)}`)
      } else {
        filters.push(`contains(${filterKey},'${value}')`)
      }
```

- [ ] **Step 4: Rodar os gates do frontend**

```bash
cd siagro-b1-frontend
yarn ts-typecheck
yarn lint
yarn ui5lint
```

Esperado: os três sem erro novo.

- [ ] **Step 5: Verificar no browser (caminho do usuário)**

Stack: backend com o profile `yktb` (Web + Gateway), `yarn start:dev` no frontend, login `admin/1234`. Menu → **Contratos de Compra**:

1. A coluna "Comprador" aparece entre "Fornecedor" e "Cod.Produto", com nome preenchido nos contratos que têm agente.
2. Clicar no value help do filtro "Comprador", escolher um agente → o Input recebe o **código** (número).
3. "Ir"/Search → a lista reduz só aos contratos daquele agente. Conferir na aba Network que o `$filter` contém `AgentCode eq N` **sem aspas** e que a resposta é 200 (não 400).
4. Digitar `abc` no filtro e buscar → nenhum filtro de agente é enviado e a tela não dá erro.
5. Limpar filtros → a lista volta inteira.

- [ ] **Step 6: Sem commit**

Nenhum arquivo novo nesta tarefa, então não há `git add`. **Não commitar.**

---

## Task 3: Tela 2 — Contratos de Compra a Liberar Entrega

**Files:**
- Modify: `webapp/view/purchaseContracts/shipmentRelease/Main.view.xml` (inserir coluna entre "Fornecedor", que termina na linha 210, e "Cod.Produto", que abre na 211)
- Modify: `webapp/view/purchaseContracts/shipmentRelease/fragments/Filterbar.fragment.xml` (inserir grupo depois de `businessPartners`, linhas 84-94)
- Modify: `webapp/controller/purchaseContracts/shipmentRelease/Main.controller.ts` (`applyFilters` linhas 35-67)

**Interfaces:**
- Consumes: o padrão da Tarefa 2 (mesma coluna, mesmo `FilterGroupItem`, mesmo ramo).
- Produces: nada para tarefas posteriores.

A tabela binda `/PurchaseContractsGetShipmentReleasesAvailable`, função `[EnableQuery]` que devolve `PurchaseContract` inteiro — o agente está na raiz, igual à Tarefa 2.

- [ ] **Step 1: Adicionar a coluna "Comprador" na view**

Entre o `</t:Column>` de "Fornecedor" e o `<t:Column>` de "Cod.Produto":

```xml
          <t:Column
						label="Comprador"
						sortProperty="AgentName"
            filterProperty="AgentName"
            filterOperator="Contains"
            width="12rem"
						>
						<t:template>
            <Text text="{AgentName}" wrapping="false"/>
						</t:template>
					</t:Column>
```

- [ ] **Step 2: Adicionar o filtro "Comprador" na FilterBar**

Em `view/purchaseContracts/shipmentRelease/fragments/Filterbar.fragment.xml`, depois do `</fb:FilterGroupItem>` de `businessPartners`:

```xml
      <fb:FilterGroupItem name="agent" label="Comprador" groupName="GroupAgent" visibleInFilterBar="true">
        <fb:control>
            <Input
              showClearIcon="true"
              showValueHelp="true"
              valueHelpRequest=".openAgentsValueHelp"
              value="{filter>/AgentCode}"
              />
        </fb:control>
      </fb:FilterGroupItem>
```

- [ ] **Step 3: Tratar o `Edm.Int32` no `applyFilters`**

O controller usa `Record<string, string>` (não há `FilterData` para atualizar). Acrescentar o ramo antes do `else` genérico:

```ts
      } else if (filterKey == "StandardCashFlowDateTo") {
        filters.push(`StandardCashFlowDate le ${value}`)
      } else if (filterKey == "AgentCode") {
        // Edm.Int32: comparação numérica, sem aspas e sem contains.
        if (!isNaN(Number(value))) filters.push(`AgentCode eq ${Number(value)}`)
      } else {
        filters.push(`contains(${filterKey},'${value}')`)
      }
```

- [ ] **Step 4: Rodar os gates do frontend**

```bash
cd siagro-b1-frontend
yarn ts-typecheck && yarn lint && yarn ui5lint
```

- [ ] **Step 5: Verificar no browser**

Menu → **Contratos de Compra → Liberação de Entregas**: coluna "Comprador" preenchida; filtro por value help reduz a lista; `$filter` com `AgentCode eq N` sem aspas, resposta 200. Conferir que o botão "Solicitar Liberação" continua navegando com a linha selecionada.

- [ ] **Step 6: Sem commit** — nenhum arquivo novo. **Não commitar.**

---

## Task 4: Tela 3 — Liberações de Entrega de Contrato de Compra

**Files:**
- Modify: `webapp/view/shipmentReleases/Main.view.xml` (inserir coluna depois de "Contrato", que termina na linha 192, antes de "Produto", linha 193)
- Modify: `webapp/view/shipmentReleases/fragments/Filterbar.fragment.xml` (inserir grupo depois de `businessPartners`, linhas 129-139)
- Modify: `webapp/controller/shipmentReleases/Main.controller.ts` (`applyFilters` linhas 69-113)
- Modify: `webapp/controller/shipmentReleases/BaseController.ts` (`createColumnConfig` linhas 9-111)

**Interfaces:**
- Consumes: o padrão da Tarefa 2, adaptado — aqui o agente vem por navegação (`PurchaseContract/AgentCode`), e há export Excel.
- Produces: o padrão "agente por navegação + Excel" que as Tarefas 6, 9 e 10 replicam.

- [ ] **Step 1: Adicionar a coluna "Comprador" na view**

Nesta tela as colunas de contrato são compostas (`({PurchaseContract/Code}) {PurchaseContract/CardName}`). Inserir depois do `</t:Column>` de "Contrato":

```xml
          <t:Column label="Comprador" sortProperty="PurchaseContract/AgentName" width="12rem">
            <t:template>
              <Text text="{PurchaseContract/AgentName}" wrapping="false"/>
            </t:template>
					</t:Column>
```

- [ ] **Step 2: Adicionar o filtro "Comprador" na FilterBar**

Em `view/shipmentReleases/fragments/Filterbar.fragment.xml`, depois do `</fb:FilterGroupItem>` de `businessPartners`:

```xml
      <fb:FilterGroupItem name="agent" label="Comprador" groupName="GroupAgent" visibleInFilterBar="true">
        <fb:control>
            <Input
              showClearIcon="true"
              showValueHelp="true"
              valueHelpRequest=".openAgentsValueHelp"
              value="{filter>/AgentCode}"
              />
        </fb:control>
      </fb:FilterGroupItem>
```

- [ ] **Step 3: Tratar o `Edm.Int32` navegado no `applyFilters`**

Acrescentar o ramo antes do `else` genérico (os vizinhos já navegam, ex.: `contains(PurchaseContract/Code,'...')`):

```ts
      } else if (filterKey == "ItemCode"){
        filters.push(`contains(PurchaseContract/ItemCode,'${value}')`)
      } else if (filterKey == "AgentCode"){
        // Edm.Int32 navegado: comparação numérica, sem aspas e sem contains.
        if (!isNaN(Number(value))) filters.push(`PurchaseContract/AgentCode eq ${Number(value)}`)
      } else {
        filters.push(`contains(${filterKey},'${value}')`)
      }
```

- [ ] **Step 4: Replicar a coluna no export Excel**

Em `webapp/controller/shipmentReleases/BaseController.ts`, depois do `aCols.push` de "Contrato" (linhas 62-66):

```ts
        aCols.push({
          label: "Comprador",
          property: "PurchaseContract/AgentName",
          type: EdmType.String,
        });
```

- [ ] **Step 5: Rodar os gates do frontend**

```bash
cd siagro-b1-frontend
yarn ts-typecheck && yarn lint && yarn ui5lint
```

- [ ] **Step 6: Verificar no browser**

Menu → **Liberações de Entrega** (compra): coluna "Comprador" preenchida; filtro reduz a lista com `$filter` contendo `PurchaseContract/AgentCode eq N` (sem aspas) e resposta 200; **baixar o Excel** e conferir que a coluna "Comprador" existe e vem preenchida. Conferir que os botões de status (Ativar/Pausar/Cancelar/Finalizar) continuam funcionando com a linha selecionada.

- [ ] **Step 7: Sem commit** — nenhum arquivo novo. **Não commitar.**

---

## Task 5: Tela 4b — Diálogo "Contratos disponíveis" da Alocação de Romaneios

**Files:**
- Modify: `webapp/view/purchaseOrders/allocation/fragments/PurchaseContractsAvaiables.fragment.xml` (inserir coluna depois de "Codigo", linhas 71-79)

**Interfaces:**
- Consumes: `AgentName` do `PurchaseContractDto` (Tarefa 1), que chega no `viewModel` via `/PurchaseContractsGetAvaiablesList(...)` (`purchaseOrders/allocation/Main.controller.ts:51-63`).
- Produces: nada.

**Decisão do usuário:** a lista da tela é de romaneios (`/StorageTransactions`), que **não têm agente** — o vínculo com o contrato só nasce na alocação. Portanto **nenhuma coluna e nenhum filtro de agente na lista de romaneios**, nem no export Excel de `purchaseOrders/allocation/BaseController.ts`. O agente aparece só aqui, no diálogo de escolha do contrato.

- [ ] **Step 1: Adicionar a coluna "Comprador" no diálogo**

Entre o `</t:Column>` de "Codigo" e o `<t:Column>` de "Saldo". **O prefixo `viewModel>` é obrigatório** — a tabela binda `rows="{viewModel>/}"` (ver Armadilha 5):

```xml
        <t:Column
          label="Comprador"
          sortProperty="AgentName"
          width="12rem"
          >
          <t:template>
            <Text text="{viewModel>AgentName}" wrapping="false"/>
          </t:template>
        </t:Column>
```

- [ ] **Step 2: Rodar os gates do frontend**

```bash
cd siagro-b1-frontend
yarn ts-typecheck && yarn lint && yarn ui5lint
```

- [ ] **Step 3: Verificar no browser**

Menu → **Alocação de Romaneios de Compra**: selecionar um romaneio pendente → **Alocar** → no diálogo "Selecionar Contrato de Compra", a coluna "Comprador" aparece **preenchida** (se vier vazia, o `viewModel>` foi esquecido ou a Tarefa 1 não está no ar — conferir a resposta de `PurchaseContractsGetAvaiablesList` na aba Network, que precisa trazer `AgentName`). Confirmar uma alocação de teste e conferir que ela continua gravando normalmente.

- [ ] **Step 4: Sem commit** — nenhum arquivo novo. **Não commitar.**

---

## Task 6: Tela 5 — Entregas de Contratos de Compra (+ 2 bugs pré-existentes)

**Files:**
- Modify: `webapp/view/purchaseContracts/allocation/Main.view.xml` (inserir coluna depois de "Fornecedor", que termina na linha 104, antes de "Cod.Produto", linha 105)
- Modify: `webapp/view/purchaseContracts/allocation/fragments/Filterbar.fragment.xml` (inserir grupo depois de `businessPartners`, linhas 30-40)
- Modify: `webapp/controller/purchaseContracts/allocation/Main.controller.ts` (`onInit` linha 24; `applyFilters` linhas 37-83)
- Modify: `webapp/controller/purchaseContracts/allocation/BaseController.ts` (`createColumnConfig` linhas 9-113)

**Interfaces:**
- Consumes: o padrão "agente por navegação + Excel" da Tarefa 4, com a chave `PurchaseContractAgentCode` (esta tela prefixa as chaves do model `filter` com a entidade — `PurchaseContractCode`, `PurchaseContractCardCode`).
- Produces: nada.

- [ ] **Step 1: Corrigir o nome da rota no `onInit`**

Sem isso o filtro novo parece não funcionar: `applyFilters()` só é chamado quando a rota da OUTRA tela dispara. Em `webapp/controller/purchaseContracts/allocation/Main.controller.ts:24`:

```ts
    this.getRouter().getRoute("purchaseContractsAllocations")
       .attachPatternMatched(() => this.applyFilters());
```

(era `"purchaseOrdersAllocations"`; a rota correta está em `manifest.json`, padrão `purchase-contracts/allocations`.)

- [ ] **Step 2: Adicionar a coluna "Comprador" na view**

Entre o `</t:Column>` de "Fornecedor" e o `<t:Column>` de "Cod.Produto":

```xml
          <t:Column
						label="Comprador"
						sortProperty="PurchaseContract/AgentName"
            filterProperty="PurchaseContract/AgentName"
            filterOperator="Contains"
            width="12rem"
						>
						<t:template>
            <Text text="{PurchaseContract/AgentName}" wrapping="false"/>
						</t:template>
					</t:Column>
```

- [ ] **Step 3: Adicionar o filtro "Comprador" na FilterBar**

Em `view/purchaseContracts/allocation/fragments/Filterbar.fragment.xml`, depois do `</fb:FilterGroupItem>` de `businessPartners`. **Note a chave prefixada**, coerente com as vizinhas desta tela:

```xml
      <fb:FilterGroupItem name="agent" label="Comprador" groupName="GroupAgent" visibleInFilterBar="true">
        <fb:control>
            <Input
              showClearIcon="true"
              showValueHelp="true"
              valueHelpRequest=".openAgentsValueHelp"
              value="{filter>/PurchaseContractAgentCode}"
              />
        </fb:control>
      </fb:FilterGroupItem>
```

- [ ] **Step 4: Tratar o `Edm.Int32` navegado no `applyFilters`**

Acrescentar o ramo junto dos outros `PurchaseContract...`, antes do `else` genérico:

```ts
      } else if (filterKey == "PurchaseContractItemCode") {
        filters.push(`contains(PurchaseContract/ItemCode, '${value}')`)
      } else if (filterKey == "PurchaseContractAgentCode") {
        // Edm.Int32 navegado: comparação numérica, sem aspas e sem contains.
        if (!isNaN(Number(value))) filters.push(`PurchaseContract/AgentCode eq ${Number(value)}`)
      } else if (filterKey == "StorageTransactionBranchCode") {
```

- [ ] **Step 5: Replicar a coluna no export Excel e corrigir o label duplicado**

Em `webapp/controller/purchaseContracts/allocation/BaseController.ts`, depois do `aCols.push` de "Fornecedor" (linhas 24-28):

```ts
			aCols.push({
				label: "Comprador",
				property: "PurchaseContract/AgentName",
				type: EdmType.String,
			});
```

E no `aCols.push` das linhas 80-84, trocar o label errado `"Un.Med."` por `"Armazém"` (a propriedade é `StorageTransaction/WarehouseCode`; a coluna anterior, `StorageTransaction/UnitOfMeasureCode`, é que é "Un.Med.").

- [ ] **Step 6: Rodar os gates do frontend**

```bash
cd siagro-b1-frontend
yarn ts-typecheck && yarn lint && yarn ui5lint
```

- [ ] **Step 7: Verificar no browser**

Menu → **Entregas de Contratos de Compra**:

1. **Entrar na tela pela primeira vez e buscar** — com a rota corrigida, o `applyFilters()` roda no `patternMatched` e o filtro vale desde a primeira entrada (antes só valia depois de passar por outra tela).
2. Coluna "Comprador" preenchida entre "Fornecedor" e "Cod.Produto".
3. Filtro por value help → `$filter` com `PurchaseContract/AgentCode eq N` sem aspas, resposta 200.
4. Excel baixado tem "Comprador" preenchido e a coluna de armazém rotulada "Armazém" (não mais duas "Un.Med.").
5. O botão de estorno continua funcionando com a linha selecionada.

- [ ] **Step 8: Sem commit** — nenhum arquivo novo. **Não commitar.**

---

## Task 7: Tela 6 — Contratos de Venda

**Files:**
- Modify: `webapp/view/salesContracts/Main.view.xml` (inserir coluna entre "Cliente", que termina na linha 166, e "Cod.Produto", linha 167)
- Modify: `webapp/view/salesContracts/fragments/SalesContractFilterbar.fragment.xml` (inserir grupo depois de `businessPartners`, linhas 51-61)
- Modify: `webapp/controller/salesContracts/Main.controller.ts` (`type FilterData` linhas 12-21; `applyFilters` linhas 46-70)

**Interfaces:**
- Consumes: o padrão da Tarefa 2, com o rótulo de venda.
- Produces: nada.

**Rótulo:** na venda o agente é o **"Vendedor"** (é assim que `SalesContractForm.fragment.xml:125-141` já chama). O identificador continua `AgentCode`/`AgentName`.

- [ ] **Step 1: Adicionar a coluna "Vendedor" na view**

Entre o `</t:Column>` de "Cliente" e o `<t:Column>` de "Cod.Produto":

```xml
          <t:Column
						label="Vendedor"
						sortProperty="AgentName"
            filterProperty="AgentName"
            filterOperator="Contains"
            width="12rem"
						>
						<t:template>
            <Text text="{AgentName}" wrapping="false"/>
						</t:template>
					</t:Column>
```

- [ ] **Step 2: Adicionar o filtro "Vendedor" na FilterBar**

Em `SalesContractFilterbar.fragment.xml`, depois do `</fb:FilterGroupItem>` de `businessPartners`:

```xml
      <fb:FilterGroupItem name="agent" label="Vendedor" groupName="GroupAgent" visibleInFilterBar="true">
        <fb:control>
            <Input
              showClearIcon="true"
              showValueHelp="true"
              valueHelpRequest=".openAgentsValueHelp"
              value="{filter>/AgentCode}"
              />
        </fb:control>
      </fb:FilterGroupItem>
```

- [ ] **Step 3: Declarar a chave no `FilterData` e tratar o `Edm.Int32` no `applyFilters`**

Acrescentar `AgentCode?: string,` ao `type FilterData` e o ramo antes do `else` genérico:

```ts
      if (filterKey == "Status" || filterKey == "Type" || filterKey == "MarketType") {
        filters.push(`${filterKey} eq '${value}'`)
      } else if (filterKey == "AgentCode") {
        // Edm.Int32: comparação numérica, sem aspas e sem contains.
        if (!isNaN(Number(value))) filters.push(`AgentCode eq ${Number(value)}`)
      } else {
        filters.push(`contains(${filterKey},'${value}')`)
      }
```

- [ ] **Step 4: Rodar os gates do frontend**

```bash
cd siagro-b1-frontend
yarn ts-typecheck && yarn lint && yarn ui5lint
```

- [ ] **Step 5: Verificar no browser**

Menu → **Contratos de Venda**: coluna "Vendedor" preenchida; value help preenche o código; busca reduz a lista com `AgentCode eq N` sem aspas e resposta 200; valor não numérico é ignorado sem erro; limpar filtros restaura a lista.

- [ ] **Step 6: Sem commit** — nenhum arquivo novo. **Não commitar.**

---

## Task 8: Tela 7 — Contratos de Venda a Liberar Entrega

**Files:**
- Modify: `webapp/view/salesContracts/shipmentRelease/Main.view.xml` (inserir coluna depois de "Cliente", que termina na linha 110, antes de "Cod.Produto", linha 111)
- Modify: `webapp/view/salesContracts/shipmentRelease/fragments/Filterbar.fragment.xml` (inserir grupo depois de `itemName`, linhas 18-22)
- Modify: `webapp/controller/salesContracts/shipmentRelease/Main.controller.ts` (`applyFilters` linhas 31-63)

**Interfaces:**
- Consumes: o padrão da Tarefa 7.
- Produces: nada.

A tabela binda `/SalesContractsGetShipmentReleasesAvailable`, função `[EnableQuery]` que devolve `SalesContract` inteiro — agente na raiz. ⚠️ Esta FilterBar filtra por **nome** (`CardName`, `ItemName`), diferente da de compra; o filtro de agente continua por **código**, como manda a Global Constraint (`AgentName` no model `filter` geraria filtro extra).

- [ ] **Step 1: Adicionar a coluna "Vendedor" na view**

Entre o `</t:Column>` de "Cliente" e o `<t:Column>` de "Cod.Produto":

```xml
          <t:Column label="Vendedor" sortProperty="AgentName" filterProperty="AgentName" filterOperator="Contains" width="12rem">
						<t:template>
            <Text text="{AgentName}" wrapping="false"/>
						</t:template>
					</t:Column>
```

- [ ] **Step 2: Adicionar o filtro "Vendedor" na FilterBar**

Em `view/salesContracts/shipmentRelease/fragments/Filterbar.fragment.xml`, depois do `</fb:FilterGroupItem>` de `itemName`:

```xml
      <fb:FilterGroupItem name="agent" label="Vendedor" groupName="GroupAgent" visibleInFilterBar="true">
        <fb:control>
            <Input
              showClearIcon="true"
              showValueHelp="true"
              valueHelpRequest=".openAgentsValueHelp"
              value="{filter>/AgentCode}"
              />
        </fb:control>
      </fb:FilterGroupItem>
```

- [ ] **Step 3: Tratar o `Edm.Int32` no `applyFilters`**

```ts
      } else if (filterKey == "StandardCashFlowDateTo") {
        filters.push(`StandardCashFlowDate le ${value}`);
      } else if (filterKey == "AgentCode") {
        // Edm.Int32: comparação numérica, sem aspas e sem contains.
        if (!isNaN(Number(value))) filters.push(`AgentCode eq ${Number(value)}`);
      } else {
        filters.push(`contains(${filterKey},'${value}')`);
      }
```

- [ ] **Step 4: Rodar os gates do frontend**

```bash
cd siagro-b1-frontend
yarn ts-typecheck && yarn lint && yarn ui5lint
```

- [ ] **Step 5: Verificar no browser**

Menu → **Contratos de Venda → Liberação de Entregas**: coluna "Vendedor" preenchida; filtro reduz a lista com `AgentCode eq N` sem aspas e resposta 200; "Solicitar Liberação" continua navegando com a linha selecionada.

- [ ] **Step 6: Sem commit** — nenhum arquivo novo. **Não commitar.**

---

## Task 9: Tela 8 — Liberações de Entrega de Contrato de Venda

**Files:**
- Modify: `webapp/view/salesShipmentReleases/Main.view.xml` (inserir coluna depois de "Contrato", que termina na linha 142, antes de "Produto", linha 143)
- Modify: `webapp/view/salesShipmentReleases/fragments/Filterbar.fragment.xml` (inserir grupo depois de `cardCode`, linhas 72-76)
- Modify: `webapp/controller/salesShipmentReleases/Main.controller.ts` (`applyFilters` linhas 68-112)
- Modify: `webapp/controller/salesShipmentReleases/BaseController.ts` (`createColumnConfig` linhas 9-101)

**Interfaces:**
- Consumes: o padrão "agente por navegação + Excel" da Tarefa 4, com `SalesContract/` em vez de `PurchaseContract/`.
- Produces: nada.

- [ ] **Step 1: Adicionar a coluna "Vendedor" na view**

Depois do `</t:Column>` de "Contrato" (que exibe `({SalesContract/Code}) {SalesContract/CardName}`):

```xml
          <t:Column label="Vendedor" sortProperty="SalesContract/AgentName" width="12rem">
            <t:template>
              <Text text="{SalesContract/AgentName}" wrapping="false"/>
            </t:template>
					</t:Column>
```

- [ ] **Step 2: Adicionar o filtro "Vendedor" na FilterBar**

Em `view/salesShipmentReleases/fragments/Filterbar.fragment.xml`, depois do `</fb:FilterGroupItem>` de `cardCode`:

```xml
      <fb:FilterGroupItem name="agent" label="Vendedor" groupName="GroupAgent" visibleInFilterBar="true">
        <fb:control>
            <Input
              showClearIcon="true"
              showValueHelp="true"
              valueHelpRequest=".openAgentsValueHelp"
              value="{filter>/AgentCode}"
              />
        </fb:control>
      </fb:FilterGroupItem>
```

⚠️ Não tocar no Select de `/Branchs` (linhas 8-21) — o `suspended: true` dele é obrigatório (Armadilha 4).

- [ ] **Step 3: Tratar o `Edm.Int32` navegado no `applyFilters`**

```ts
      } else if (filterKey == "ItemCode") {
        filters.push(`contains(SalesContract/ItemCode,'${value}')`);
      } else if (filterKey == "AgentCode") {
        // Edm.Int32 navegado: comparação numérica, sem aspas e sem contains.
        if (!isNaN(Number(value))) filters.push(`SalesContract/AgentCode eq ${Number(value)}`);
      } else {
        filters.push(`contains(${filterKey},'${value}')`);
      }
```

- [ ] **Step 4: Replicar a coluna no export Excel**

Em `webapp/controller/salesShipmentReleases/BaseController.ts`, depois do `aCols.push` de "Contrato" (linhas 62-66):

```ts
    aCols.push({
      label: "Vendedor",
      property: "SalesContract/AgentName",
      type: EdmType.String,
    });
```

- [ ] **Step 5: Rodar os gates do frontend**

```bash
cd siagro-b1-frontend
yarn ts-typecheck && yarn lint && yarn ui5lint
```

- [ ] **Step 6: Verificar no browser**

Menu → **Liberações de Entrega de Venda**: a rota **renderiza** (se a tela ficar em branco, o Select de `/Branchs` perdeu o `suspended` — ver console: `Cannot resume a not suspended binding`); coluna "Vendedor" preenchida; filtro com `SalesContract/AgentCode eq N` sem aspas e resposta 200; Excel com "Vendedor" preenchido; botões de status continuam operando.

- [ ] **Step 7: Sem commit** — nenhum arquivo novo. **Não commitar.**

---

## Task 10: Tela 9 — Entregas de Contratos de Venda

**Files:**
- Modify: `webapp/view/salesContracts/allocation/Main.view.xml` (inserir coluna depois de "Cliente", que termina na linha 81, antes de "Produto", linha 82)
- Modify: `webapp/view/salesContracts/allocation/fragments/Filterbar.fragment.xml` (inserir grupo depois de `customers`, linhas 27-36)
- Modify: `webapp/controller/salesContracts/allocation/Main.controller.ts` (`applyFilters` linhas 220-252)
- Modify: `webapp/controller/salesContracts/allocation/BaseController.ts` (`createColumnConfig` linhas 9-38)

**Interfaces:**
- Consumes: o padrão da Tarefa 6 (chave prefixada com a entidade), com `SalesContract/`.
- Produces: nada.

⚠️ Esta tela e a `salesContracts/reconciliation` foram entregues em 29/07/2026 e ainda **não estão commitadas**; conferir com `git status` no `siagro-b1-frontend` antes de editar, para não confundir mudança pendente com a sua.

- [ ] **Step 1: Adicionar a coluna "Vendedor" na view**

Entre o `</t:Column>` de "Cliente" e o `<t:Column>` de "Produto":

```xml
          <t:Column label="Vendedor" sortProperty="SalesContract/AgentName" filterProperty="SalesContract/AgentName" filterOperator="Contains" width="12rem">
						<t:template>
            <Text text="{SalesContract/AgentName}" wrapping="false"/>
						</t:template>
					</t:Column>
```

- [ ] **Step 2: Adicionar o filtro "Vendedor" na FilterBar**

Em `view/salesContracts/allocation/fragments/Filterbar.fragment.xml`, depois do `</fb:FilterGroupItem>` de `customers`. **Chave prefixada**, coerente com `SalesContractCode`/`SalesContractCardCode`:

```xml
      <fb:FilterGroupItem name="agent" label="Vendedor" groupName="GroupAgent" visibleInFilterBar="true">
        <fb:control>
            <Input
              showClearIcon="true"
              showValueHelp="true"
              valueHelpRequest=".openAgentsValueHelp"
              value="{filter>/SalesContractAgentCode}"
              />
        </fb:control>
      </fb:FilterGroupItem>
```

- [ ] **Step 3: Tratar o `Edm.Int32` navegado no `applyFilters`**

```ts
      } else if (key === "SalesContractItemCode") {
        filters.push(`contains(SalesContract/ItemCode, '${value}')`);
      } else if (key === "SalesContractAgentCode") {
        // Edm.Int32 navegado: comparação numérica, sem aspas e sem contains.
        if (!isNaN(Number(value))) filters.push(`SalesContract/AgentCode eq ${Number(value)}`);
      } else if (key === "InvoiceNumber") {
```

- [ ] **Step 4: Replicar a coluna no export Excel**

Em `webapp/controller/salesContracts/allocation/BaseController.ts`, depois do `aCols.push` de "Cliente" (linha 14):

```ts
    aCols.push({ label: "Vendedor", property: "SalesContract/AgentName", type: EdmType.String });
```

- [ ] **Step 5: Rodar os gates do frontend**

```bash
cd siagro-b1-frontend
yarn ts-typecheck && yarn lint && yarn ui5lint
```

- [ ] **Step 6: Verificar no browser**

Menu → **Entregas de Contratos de Venda**: coluna "Vendedor" preenchida; filtro com `SalesContract/AgentCode eq N` sem aspas e resposta 200; Excel com "Vendedor"; **Realocar** e **Estornar** continuam funcionando (regressão da entrega de 29/07).

- [ ] **Step 7: Sem commit** — nenhum arquivo novo. **Não commitar.**

---

## Verificação final (caminho do usuário — [[verify-via-user-path-not-just-my-layer]])

- [ ] **1. Gates completos**

```bash
cd siagro-b1-backend && dotnet build SiagroB1.sln && dotnet test SiagroB1.Application.Tests
cd ../siagro-b1-frontend && yarn ts-typecheck && yarn lint && yarn ui5lint
```

Esperado: build sem erro, **657 passed**, três gates de frontend sem erro novo. `yarn test` (QUnit/OPA5) continua sendo só o template do gerador — nenhuma das telas tocadas tem teste automatizado, então **a verificação visual abaixo é a única prova real** e precisa ser feita, não presumida.

- [ ] **2. Subir a stack**

Backend: profile `yktb` (ambiente Yokotobi) para `SiagroB1.Web` + `SiagroB1.Gateway`. Frontend: `yarn start:dev`. Login `admin/1234`. **Sem migration nesta entrega** — não rodar `dotnet ef database update`.

- [ ] **3. Percorrer as 9 telas pelo menu**

Para cada uma: menu → tela → conferir a coluna ("Comprador" nas 5 de compra, "Vendedor" nas 4 de venda) → filtrar por um agente pelo value help → conferir que a lista reduz e que o `$filter` na aba Network tem `AgentCode eq N` **sem aspas** com resposta **200**. Na Alocação de Romaneios, o agente é conferido no diálogo "Contratos disponíveis" (a lista de romaneios continua sem coluna de agente, por decisão do usuário).

- [ ] **4. Exports Excel**

Baixar o Excel das 4 telas que têm export (3, 5, 8, 9) e conferir a coluna de agente preenchida.

- [ ] **5. Um agente sem contrato e um contrato sem agente**

Filtrar por um agente que não tem contrato → lista vazia, sem erro. Conferir que contratos legados com `AgentCode` nulo aparecem com a coluna em branco e **não desaparecem** quando não há filtro de agente (o filtro é opcional, não deve virar INNER JOIN).

- [ ] **6. Regressão dos filtros vizinhos**

Em cada tela, combinar o filtro de agente com um filtro que já existia (Fornecedor/Cliente, Produto, datas) e conferir que o `and` sai correto e a resposta é 200.

- [ ] **7. Derrubar a stack** (portas 50000 / 5246 / 8080).

- [ ] **8. Deixar tudo staged, sem commit**

```bash
cd siagro-b1-backend && git status --short
cd ../siagro-b1-frontend && git status --short
```

Todo arquivo novo aparece como `A`; nenhum commit foi feito. Os commits são do usuário.
