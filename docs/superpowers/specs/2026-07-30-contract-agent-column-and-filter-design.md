# Agente comercial (Comprador/Vendedor) nas telas de lista de contrato — GAC-1125

Data: 2026-07-30

## Problema

Cada agente comercial é responsável pela gestão dos contratos que detém, mas o agente aparecia
**apenas** no formulário do contrato (Add/Edit/Detail) e nas telas de aprovação. **Nenhuma tela de
lista mostrava ou filtrava por agente**, então não havia como isolar "os meus contratos" em nenhuma
tela operacional.

O chamado pediu 6 telas; o escopo foi ampliado com os equivalentes de venda, fechando em 9:

| # | Tela | Binding da tabela | Onde mora o agente |
|---|---|---|---|
| 1 | Contratos de Compra | `/PurchaseContracts` | raiz |
| 2 | Ctr. Compra — Liberação de entregas | `/PurchaseContractsGetShipmentReleasesAvailable` | raiz (função `[EnableQuery]` devolve a entidade inteira) |
| 3 | Liberações de Entrega (compra) | `/ShipmentReleases` | `PurchaseContract/…` |
| 4 | Alocação de Romaneios de Compra | `/StorageTransactions` | **não existe** |
| 4b | Diálogo "Contratos disponíveis" | `/PurchaseContractsGetAvaiablesList(...)` | faltava no DTO |
| 5 | Entregas de Ctr. de Compra | `/PurchaseContractsAllocations` | `PurchaseContract/…` |
| 6 | Contratos de Venda | `/SalesContracts` | raiz |
| 7 | Ctr. Venda — Liberação de entregas | `/SalesContractsGetShipmentReleasesAvailable` | raiz |
| 8 | Liberações de Entrega de Venda | `/SalesShipmentReleases` | `SalesContract/…` |
| 9 | Entregas de Ctr. de Venda | `/SalesContractsAllocations` | `SalesContract/…` |

## Decisão

**Reusar o par desnormalizado `AgentCode` (`int?`) + `AgentName` (`string?`)** de `PurchaseContract`
e `SalesContract`, sem recriar FK nem navigation property. A FK para `AGENTS` existiu e foi removida
de propósito (migration `20260108184649_AlterTablePurchaseContractsAddColumnAgentName`): em modo
`Erp=SAPB1` a tabela local fica vazia e o INNER JOIN zeraria a coleção inteira. A verificação
confirmou o valor dessa decisão de forma acidental — com o banco do SAP fora do ar, a coluna de
agente continuou preenchida em todas as telas, porque o nome está gravado no contrato.

**Romaneio não tem agente.** A tela 4 lista `StorageTransactions`, e o vínculo com o contrato só
nasce na alocação. Decisão do usuário: **nada de coluna nem filtro na lista de romaneios**; o agente
aparece no diálogo "Contratos disponíveis" — a única mudança de backend desta entrega.

**Rótulo por lado do negócio, identificador em inglês.** O usuário lê "Comprador" nas 5 telas de
compra e "Vendedor" nas 4 de venda (é como os formulários de contrato já chamam o campo); as
propriedades e as chaves do model `filter` continuam `AgentCode`/`AgentName`.

**A convenção de chave do model `filter` é deliberadamente não uniforme.** `AgentCode` cru nas 6
telas cujo `applyFilters` usa chaves sem prefixo; **prefixado** nas 2 telas de alocação
(`PurchaseContractAgentCode`, `SalesContractAgentCode`), porque é o padrão que elas já usam
(`SalesContractCode`, `SalesContractCardCode`, …). Normalizar seria mais "limpo" e mais arriscado.
⚠️ A chave tem que ser a **mesma string** no `value="{filter>/…}"` do fragmento e no `else if` do
controller: se divergir, o filtro simplesmente nunca dispara — sem erro, sem log, sem sintoma além
de "não funciona".

### Alternativas descartadas

- **Extrair o ramo de filtro para um helper** em vez de replicá-lo nas 8 telas. Cada `applyFilters`
  monta string OData crua com caminho e chave próprios, e o ramo novo tem a mesma forma dos outros
  ~12 ramos de cada controller. Um helper economizaria uma linha por tela ao custo de sair do padrão
  do arquivo. Se um dia valer centralizar, o candidato é a **validação** do código, não a montagem
  do filtro.
- **Coluna/filtro de agente na lista de romaneios** (tela 4), resolvendo o agente pela alocação —
  descartado pelo usuário: um romaneio pode não estar alocado, e alocado a mais de um contrato.

## Implementação

**Backend (única mudança):** `PurchaseContractDto` ganhou `AgentCode`/`AgentName`, projetados em
`PurchaseContractsGetService.GetAvaiablesPurchaseContracts`. Cobertos por
`SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsGetAvaiablesListAgentTests.cs`
(contrato com agente e contrato sem agente). O `ODataConventionModelBuilder` já expõe as duas
propriedades novas no complex type — nenhum registro manual foi preciso. **Sem migration**: as
colunas `AgentCode`/`AgentName` já existiam nas duas tabelas de contrato, e o DTO não é entidade.

**Frontend:** cada tela recebeu o mesmo trio — uma `<t:Column>` exibindo o nome do agente ao lado da
coluna de parceiro; um `<fb:FilterGroupItem>` com `valueHelpRequest=".openAgentsValueHelp"`
(`CommonController.ts`, que já filtra `Inactive eq 'N'`); e um `else if` explícito no `applyFilters`,
antes do `else` genérico. Representativos: `webapp/view/purchaseContracts/Main.view.xml` +
`webapp/controller/purchaseContracts/Main.controller.ts` (raiz) e
`webapp/view/salesContracts/allocation/fragments/Filterbar.fragment.xml` +
`webapp/controller/salesContracts/allocation/Main.controller.ts` (navegado, chave prefixada).

**Excel:** a lista de colunas do export é duplicada em TypeScript no `createColumnConfig()` de cada
`BaseController`, então a coluna nova teve de ser replicada lá nas 4 telas que exportam (3, 5, 8, 9).
Coluna só na view sairia com o cabeçalho ausente na planilha.

**Diálogo "Contratos disponíveis"** (`PurchaseContractsAvaiables.fragment.xml`): a tabela binda
`rows="{viewModel>/}"` (JSONModel), então a coluna nova usa `{viewModel>AgentName}`.

**Duas correções pré-existentes entraram junto**, ambas na tela 5:

- `purchaseContracts/allocation/Main.controller.ts` registrava `getRoute("purchaseOrdersAllocations")`
  — rota que **existe e é de outra tela** (`purchaseOrders/allocation/Main`, a de romaneios). O
  `applyFilters` só rodava quando a outra tela disparava o `patternMatched`; sem corrigir, o filtro
  novo pareceria quebrado ao entrar na tela. Passou a ser `purchaseContractsAllocations`.
- No `createColumnConfig()` do mesmo módulo, a coluna `StorageTransaction/WarehouseCode` estava
  rotulada `"Un.Med."`, duplicando a coluna anterior. Passou a `"Armazém"`.

## Armadilhas encontradas

1. **`!isNaN(Number(value))` não é guard de código numérico.** Era o guard do primeiro desenho e
   não cumpria a própria invariante ("valor não numérico é ignorado"): `Number(" ")` é `0` → o
   `$filter` vira `AgentCode eq 0` e a tela devolve lista vazia sem explicação; `"1e3"` → `1000` e
   `"0x10"` → `16` filtram o **agente errado**; `"Infinity"` gera literal OData inválido → 400.
   Corrigido para `/^\d+$/.test(value.trim())` nos 8 controllers. Agrava porque **digitar o código à
   mão é o caminho primário**: `openAgentsValueHelp` pesquisa só por `Name`, então quem sabe o
   próprio código digita.
2. **`descriptionProperty` no Input de filtro quebra as telas de liberação e alocação.** Gravar
   `AgentName` no model `filter` faz o loop do `applyFilters` emitir um filtro extra por uma
   propriedade que não existe na raiz daquelas entidades, e o OData retorna erro. Só o Input de
   código, como já é feito em "Fornecedor" e "Produto".
3. **`AgentCode` é `Edm.Int32`**: `AgentCode eq 5`, sem aspas e sem `contains`. É por isso que cada
   tela precisa do `else if` — o ramo `else` genérico geraria `contains(AgentCode,'5')`.
4. **`suspended: true` nos bindings de `/Branchs` dentro da FilterBar é obrigatório.** A
   `sap.ui.comp.filterbar.FilterBar` dá `resume()` nos bindings dos seus controles na inicialização;
   sem `suspended` ela lança `Cannot resume a not suspended binding` e a rota inteira não renderiza.
   Não tocar neles.
5. **No diálogo "Contratos disponíveis" o binding precisa do prefixo `viewModel>`.** A coluna
   "Filial" vizinha binda `{Branch/ShortName}` sem prefixo e por isso já nascia vazia — bug
   pré-existente, deixado como está (corrigir exigiria expor Filial no DTO). Não copiar o padrão.
6. **Índice de célula ≠ índice de coluna** (armadilha de verificação, não de produto):
   `row.getCells()` só devolve as colunas **visíveis**, e a coluna `Key` (`visible="false"`) desloca
   tudo em relação a `getColumns()`. Ler dado por índice de coluna leva a conclusões erradas.

## Verificação (30/07/2026, Yokotobi)

- Backend: `dotnet build SiagroB1.sln` sem erros; **657 testes verdes** (2 novos). Frontend:
  `ts-typecheck` e `lint` limpos; `ui5lint` em 641 problems / 633 erros, **idêntico ao baseline**
  pré-entrega (débito técnico do app inteiro), sem nenhum erro novo nos 20 arquivos tocados.
- **6 das 9 telas conferidas com dado real** (coluna, binding, chave, `$filter` gerado e agente nas
  linhas retornadas): Contratos de Compra 790 → **399** registros do agente 87, todos "Dennis
  Muzzana"; Entregas de Ctr. de Compra 791; Entregas de Ctr. de Venda 597; Contratos de Venda 141;
  Ctr. Venda a Liberar 46; Ctr. Compra a Liberar com contextos retornados.
- **Armadilha 1 confirmada na UI**: com `" "` no campo de agente, **nenhum `$filter` é enviado**
  (antes viraria `AgentCode eq 0`); e `AgentCode eq 0` de fato devolve 0 registros, que era o
  resultado confuso que o guard antigo produzia.
- **Backend do diálogo confirmado pela função real**:
  `/PurchaseContractsGetAvaiablesList(CardCode='F004654',ItemCode='P000802')` devolveu 5 contratos,
  cada um com `AgentCode: 87` e `AgentName: "Dennis Muzzana"` no payload.

### O que não foi verificado visualmente, e por quê

Nada aqui é limitação do código — os três casos são do ambiente:

- **Telas 3 e 8 (Liberações de Entrega)**: os entity sets `/ShipmentReleases` e
  `/SalesShipmentReleases` estouram o `CommandTimeout` de 30s no `INNER JOIN STORAGE_TRANSACTIONS`
  eager, e a tela carrega 0 linhas. **A query que estoura não tem `WHERE` nem parâmetro de agente** —
  é a carga inicial, sem filtro. Estrutura conferida (coluna, binding navegado, chave, `$filter`
  montado) e filtro provado por requisição mais leve (2 registros em
  `/SalesShipmentReleases?$filter=SalesContract/AgentCode eq 87`) e pelo SQL gerado do lado da
  compra: `INNER JOIN PURCHASE_CONTRACTS … WHERE [p].[AgentCode] = @p`. A rota da tela 8 renderiza,
  o que confirma que o `suspended: true` da armadilha 4 está intacto.
- **Diálogo "Contratos disponíveis"**: não há romaneio pendente no ambiente, então ele não abre.
- **Os 4 exports Excel**: não foram baixados.
- O **value help de agentes** retorna 500 porque o `SapErpDbContext` não alcança
  `SBO_YOKOTOBI_PRD` (o entity set `Agents` lê `OSLP` do SAP). Não afeta o filtro por código digitado.

## Pendências (decisão do usuário)

- `webapp/view/salesContracts/approval/Main.view.xml` rotula `{AgentName}` como **"Comprador"** numa
  tela de **venda**. Pré-existente e fora das 9 telas do chamado, mas é a única exceção visível à
  convenção que esta entrega formaliza.
- `webapp/view/shipmentBilling/Main.view.xml` referencia o `SalesContractFilterbar` alterado, com a
  referência **comentada**. Sem impacto hoje; se alguém descomentar, a tela de Faturamento de
  Expedição passa a exibir um filtro "Vendedor" inerte (ela binda `/StorageTransactions` e monta o
  `$filter` na mão, sem ler o model `filter`).
- **Sem teste automatizado nas 9 telas.** `webapp/test/` contém apenas o exemplo do gerador e
  nenhum dos ~90 módulos de feature tem teste; criar a primeira infraestrutura de teste de tela do
  projeto não cabia no chamado.
