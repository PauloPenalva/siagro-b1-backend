# Design: locais de entrega (1:N) no contrato de VENDA (SalesContractDeliveryLocation)

**Data:** 2026-07-23
**Repositórios:** `siagro-b1-backend`, `siagro-b1-frontend`
**Autor:** Paulo Penalva (com Claude Code)

## Contexto

O cliente abriu um chamado de melhoria pedindo que um contrato de venda possa informar
**vários locais de entrega** (relação 1:N), e não apenas um. A motivação de negócio é a
**disponibilidade de cotas do terminal nos portos**: a mercadoria de um mesmo contrato
pode ser entregue em mais de um terminal/porto conforme a cota disponível. Cada local de
entrega é um cliente do **cadastro de clientes** (`BusinessPartner` com `CardType = 'C'`).

Escopo confirmado com o usuário:

- É apenas uma **lista de locais permitidos** — **sem cota/volume por local** (nenhuma
  quantidade é armazenada por linha nesta feature).
- Coleção **opcional** (zero ou mais locais) e **editável somente com o contrato em
  Draft**, consistente com a regra de edição já existente do contrato de venda.
- **Sem campos extras por linha** além de cliente + nome (YAGNI).
- Bloquear **cliente duplicado** no mesmo contrato (validação leve).

**Fora de escopo:** consumir/reconciliar esses locais em faturamento, liberação de
entrega, alocação ou controle de cotas. Esta feature é apenas o **cadastro** dos locais
no contrato.

Uma tentativa anterior (campo único `DeliveryLocationCode`/`DeliveryLocationName` inline
no `SalesContract`) foi **integralmente revertida** — este design a substitui.

## Decisões

### 1. Entidade-filha dedicada, espelhando `PurchaseContractBroker`

O padrão da casa para uma coleção 1:N que referencia um `BusinessPartner` é a stack do
`PurchaseContractBroker` (tabela `PURCHASE_CONTRACTS_BROKERS`): entidade-filha própria +
4 serviços + controller filho com rotas OData aninhadas + `EntitySet` no EDM + tabela
editável no formulário. Vamos replicá-la, removendo os campos de comissão.

Nova entidade `SalesContractDeliveryLocation` → tabela `SALES_CONTRACTS_DELIVERY_LOCATIONS`:

| Coluna | Tipo | Observação |
|---|---|---|
| `Key` | `Guid` identity, `[Key]` | PK |
| `SalesContractKey` | `Guid` (nullable, como no Broker) + `virtual SalesContract? SalesContract` | FK ao pai |
| `CardCode` | `VARCHAR(10)`, `required` | cliente (`CardType='C'`) |
| `CardName` | `VARCHAR(200)`, nullable | desnormalizado na gravação |

**Sem propriedade de navegação para `BusinessPartner`** — mesma regra dos demais campos
de parceiro: em modo SAPB1 o parceiro vem do SAP e a tabela local `BUSINESS_PARTNERS`
está vazia; um INNER JOIN zeraria a coleção. O nome é desnormalizado na gravação.

Em `SalesContract` adiciona-se a coleção de navegação:

```csharp
public ICollection<SalesContractDeliveryLocation> DeliveryLocations { get; set; } = [];
```

Alternativas rejeitadas: (B) tela/diálogo separado fora do formulário — mais navegação,
pior UX, mesmo volume de código; (C) coluna JSON/CSV na `SALES_CONTRACTS` — sem value
help, sem consulta, desnormalizado.

### 2. Persistência: deep insert no Add, controller-filho no Edit

Espelha o comportamento já existente do Broker:

- **Add (criar contrato):** o frontend cria as linhas dentro do binding de criação do
  contrato; o POST em `SalesContracts` chega com `DeliveryLocations` aninhado e é gravado
  por **deep insert**. Em `SalesContractsCreateService`, iterar `entity.DeliveryLocations`
  setando o back-ref `dl.SalesContract = entity` (como já se faz com `PriceFixations`) e
  resolver `dl.CardName` via `IBusinessPartnerService.GetByIdAsync(dl.CardCode)`.
- **Edit (contrato existente, Draft):** cada inclusão/remoção de linha vira um
  **POST/DELETE** direto no controller-filho `SalesContractsDeliveryLocations`, no mesmo
  update group do OData v4. O `CreateService`/`UpdateService` do filho resolvem `CardName`
  no servidor (espelho de `PurchaseContractsBrokersCreateService`).

### 3. Backend — arquivos a criar

- `SiagroB1.Domain/Entities/SalesContractDeliveryLocation.cs` — entidade acima.
- `SalesContract.cs` — adicionar a coleção `DeliveryLocations`.
- `Services/SalesContracts/`: `SalesContractsDeliveryLocationsCreateService`,
  `...UpdateService`, `...DeleteService`, `...GetService` (espelho 1:1 dos de Broker;
  Create/Update resolvem `CardName`).
- `SiagroB1.Web/Controllers/SalesContractsDeliveryLocationsController.cs` — rotas OData
  aninhadas `odata/SalesContracts({key})/DeliveryLocations(...)` (POST/PUT/DELETE/GET/
  PATCH), espelho de `PurchaseContractsBrokersController`.
- `ODataConfig/ODataConfigurations.cs` — `modelBuilder.EntitySet<SalesContractDeliveryLocation>("SalesContractsDeliveryLocations")`.
- `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` — registrar os 4 serviços em
  `AddApplicationServices()` (não há assembly scanning).

### 4. Validação de duplicidade

Bloquear o mesmo `CardCode` repetido no mesmo contrato. Aplicar no `CreateService` do
filho (Edit) e no `SalesContractsCreateService` (Add, ao iterar a coleção), lançando
`ApplicationException`/`DefaultException` com mensagem de negócio.

### 5. Frontend — arquivos a criar/alterar

- Novo fragmento `webapp/view/salesContracts/fragments/SalesContractDeliveryLocations.fragment.xml`
  — `sap.ui.table.Table` com `rows="{DeliveryLocations}"`, espelho de
  `PurchaseContractBrokers.fragment.xml` sem as colunas de comissão:
  - Coluna **Código**: `Input` com `showValueHelp`/`valueHelpOnly`,
    `valueHelpRequest=".openCostumersValueHelp"` e
    `<core:CustomData key="descriptionProperty" value="CardName" />`.
  - Coluna **Nome**: `Text`/`Input` read-only ligado a `{CardName}`.
  - Toolbar com título "Locais de Entrega do Contrato" + botões **Incluir**/**Remover**
    visíveis só em `{ui>/editable}`.
- `SalesContractsBaseController.ts` — handlers `onAddDeliveryLocation` /
  `onRemoveDeliveryLocation` (espelho de `onAddBroker`/`onRemoveBroker`:
  `oBinding.create({}, false, true, false)` e `oContext.delete(oModel.getUpdateGroupId())`).
- Embutir o fragmento em `Add.view.xml`, `Edit.view.xml` e `Detail.view.xml` do contrato
  de venda, após o formulário principal (como as demais seções filhas — Fixações,
  Liberações, Alocações, Anexos).

O value help de clientes (`openCostumersValueHelp`, filtro `CardType eq 'C'`, dialog
`CostumersSelectDialog`) já existe e é reaproveitado — sem novo handler ou dialog.

### 6. Migration

Nova migration em `SiagroB1.Migrations/AppContext/` criando a tabela
`SALES_CONTRACTS_DELIVERY_LOCATIONS` (PK `Key`, FK `SalesContractKey` →
`SALES_CONTRACTS.Key` com índice, `CardCode`, `CardName`). Gerar via
`dotnet ef migrations add` (com `ASPNETCORE_ENVIRONMENT` explícito; **não** aplica ao
banco) e conferir o `Up`/`Down` + snapshot antes de aplicar.

## Verificação

Ambiente autorizado pelo usuário: profile **`yktb`** (environment **Yokotobi**),
credenciais dev **admin / 1234**, **aplicação de migrations autorizada**.

1. **Build backend:** `dotnet build SiagroB1.sln` — 0 erros.
2. **Aplicar migration:** `dotnet ef database update` com `ASPNETCORE_ENVIRONMENT=Yokotobi`
   (conferir a connection string antes, conforme prática de migrations). Confirmar a
   criação da tabela `SALES_CONTRACTS_DELIVERY_LOCATIONS`.
3. **Gates frontend:** `yarn ts-typecheck`, `yarn lint`, `yarn ui5lint` (sem regressão
   nova além do baseline pré-existente).
4. **Subir a stack:** backend profile `yktb` (Web + Gateway) + frontend `yarn start:dev`;
   login `admin/1234`.
5. **Caminho do usuário (obrigatório, no browser):** Contratos de Venda → Novo. Confirmar:
   - a seção "Locais de Entrega" aparece com Incluir/Remover;
   - incluir 2 linhas, cada value help listando só clientes (`CardType='C'`) e preenchendo
     código + nome; salvar cria o contrato com os 2 locais (deep insert) — conferir no
     GET/OData ou no banco;
   - tentar incluir o mesmo cliente duas vezes é bloqueado;
   - abrir em Edit (Draft): incluir/remover linha persiste (POST/DELETE no controller
     filho); abrir em Detail mostra a lista read-only;
   - criar contrato **sem** nenhum local grava normalmente (coleção opcional).
