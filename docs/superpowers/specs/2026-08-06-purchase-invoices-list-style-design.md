# Lista de Documentos de Entrada no estilo da lista de Documentos de Saída — Design

**Data:** 06/08/2026
**Escopo:** apenas a tela de listagem `/purchase-invoices` (frontend). Backend não muda.

## Problema

`/purchase-invoices` e `/sales-invoices` são telas irmãs, lidas lado a lado pelo mesmo operador, e
divergem em cinco pontos visíveis:

| | `/sales-invoices` | `/purchase-invoices` (hoje) |
|---|---|---|
| Container | `sap.f.DynamicPage`, header colapsável | `sap.m.Page` |
| Filtros | `sap.ui.comp.filterbar.FilterBar`, 10 campos, Buscar/Limpar | `Select` de Tipo + `SearchField` na toolbar |
| Ações | toolbar no topo da tabela | footer no rodapé |
| Excel | `Exportar Excel` | não tem |
| Filtro no binding | string `$filter` montada do modelo `filter` | `changeParameters` + `Filter` dinâmico |

A divergência nasceu porque a tela de entrada foi criada depois, sem espelhar o layout da irmã —
mesma origem da divergência que a antiga `CustomerReturn` tinha criado no modelo de dados.

## Decisão

Espelho completo: a tela de entrada passa a ter a mesma estrutura da de saída, incluindo a barra de
filtros, as ações no topo, o Exportar Excel e o estilo das colunas.

## Arquivos

```
webapp/view/purchaseInvoices/Main.view.xml                    reescrito
webapp/view/purchaseInvoices/fragments/Filterbar.fragment.xml NOVO
webapp/controller/purchaseInvoices/Main.controller.ts         filtros + Excel
```

## 1. `Filterbar.fragment.xml` (novo)

`fb:FilterBar id="purchaseInvoicesFb" search=".onSearch" showClearOnFB="true" clear=".onClearFilters"`,
espelhando o fragmento de saída. Dez `fb:FilterGroupItem`, todos `visibleInFilterBar="true"`,
ligados ao modelo `filter`:

| `name` | label | controle | chave no modelo `filter` |
|---|---|---|---|
| `invoiceType` | Tipo | `Select` — Todos / Normal / Devolução | `InvoiceType` |
| `issuerType` | Emissão própria | `Select` — Todos / De terceiro / Própria | `IssuerType` |
| `status` | Situação | `Select` — Todos / Pendente / Confirmado / Cancelado | `InvoiceStatus` |
| `businessPartners` | Emitente | `Input` + `.openBusinessPartnersValueHelp` | `CardCode` |
| `invoiceNumber` | Número interno | `Input` | `InvoiceNumber` |
| `taxDocumentNumber` | NF | `Input` | `TaxDocumentNumber` |
| `taxDocumentSeries` | Série | `Input` | `TaxDocumentSeries` |
| `chaveNFe` | Chave NF-e | `Input` | `ChaveNFe` |
| `dateFrom` | Emissão de | `DatePicker` | `DateFrom` |
| `dateTo` | Emissão até | `DatePicker` | `DateTo` |

**Todo `Select` leva `forceSelection="false"`.** No padrão (`true`) ele auto-seleciona um item na
inicialização, o que faria a lista nascer filtrada sem o usuário ter escolhido nada.

**Os três `Select` têm um item de chave vazia (`Todos`)**, uniformemente. É o único ponto em que a
barra melhora sobre a de saída, onde o `Select` de Status não tem esse item: sem ele, desligar um
filtro isolado só é possível pelo `Limpar`, que limpa a barra inteira. Manter só no Tipo — que já o
tem hoje — deixaria os três com comportamentos diferentes.

**`Retornado` NÃO entra na lista de situações.** `InvoiceStatus` é o enum compartilhado com o
documento de saída e tem o valor `Returned`, mas nenhum serviço do documento de entrada o atribui:
o ciclo é `Pending → Confirmed → Pending` (estorno) `→ Cancelled`. O `formatter` mapeia
`Returned → "Devolvido"` por herança do espelho, e oferecer esse filtro seria uma opção que nunca
retorna nada.

Os `DatePicker` usam `displayFormat="dd/MM/yyyy"` e `valueFormat="yyyy-MM-dd"`, como no de saída: o
valor que entra no `$filter` é a data ISO.

**Emitente usa `openBusinessPartnersValueHelp`, não `openCostumersValueHelp`.** Na entrada o emitente
é fornecedor na compra e cliente na devolução — restringir a clientes esconderia metade dos
documentos. É a única divergência deliberada em relação ao fragmento de saída.

## 2. `Main.controller.ts`

`applyFilters()` no molde do de saída: percorre as chaves do modelo `filter`, ignora valor vazio,
monta um array de expressões e aplica a junção com `and` por `changeParameters({ $filter })`.

Regra por chave:

| Chave | Expressão gerada |
|---|---|
| `InvoiceType`, `IssuerType`, `InvoiceStatus` | `<campo> eq '<valor>'` |
| `DateFrom` | `IssueDate ge <valor>` |
| `DateTo` | `IssueDate le <valor>` |
| demais | `contains(<campo>,'<valor>')` |

**Os três enums entram como string crua, e isso não é atalho.** `sap.ui.model.Filter` sobre enum faz
o UI5 montar o literal a partir do metadata e estourar `Unsupported type: SIAGROB1.PurchaseInvoiceType`
na cara do usuário. O `$filter` estático passa a string intacta e o backend aceita
`InvoiceType eq 'Return'` — é o que a tela de saída já faz com `InvoiceStatus`.

`onInit` passa a chamar `createFilterModel()` e a aplicar os filtros no `patternMatched` da rota,
como o de saída.

**Sai:** `onFilterTypeChange`, `onSearch` na forma atual, os imports de `Filter`, `FilterOperator` e
`SearchField`, e a propriedade `ui>/filterType`.
**Entra:** `onClearFilters`, `onExcel` e `createColumnConfig`.

Colunas do Excel, espelhando as da tabela:

| label | property | tipo |
|---|---|---|
| Emitente | `CardName` | String |
| Cod.Emitente | `CardCode` | String |
| Tipo | `InvoiceType` | Enumeration — `Normal`/`Return` → `Normal`/`Devolução` |
| Emissão própria | `IssuerType` | Enumeration — `ThirdParty`/`Own` → `De terceiro`/`Própria` |
| Situação | `InvoiceStatus` | Enumeration — `Pending`/`Confirmed`/`Cancelled` |
| Número interno | `InvoiceNumber` | String |
| NF | `TaxDocumentNumber` | String |
| Série | `TaxDocumentSeries` | String |
| Emissão | `IssueDate` | Date |
| Entrada | `PostingDate` | Date |
| Valor declarado | `TotalDocumentValue` | Number, scale 2, delimiter |
| Chave NF-e | `ChaveNFe` | String |

## 3. `Main.view.xml`

`sap.f.DynamicPage` com `DynamicPageTitle` (título "Documentos de Entrada"), `DynamicPageHeader`
contendo o fragmento da filterbar, e a tabela no `f:content`.

Toolbar da tabela, na ordem do de saída:

```
Documentos de Entrada  ──  [Exportar Excel] [+ Incluir] [Visualizar] [Editar]
                            │ [Cancelar Documento] │ [Atualizar]
```

O `<footer>` da `Page` é removido — as ações vivem só na toolbar.

Colunas alinhadas ao estilo da irmã:

- `Situação` — `ObjectStatus` com `inverted="true"` (chip colorido).
- `Emitente` — `Text text="({CardCode}) {CardName}" wrapping="false"`, substituindo o
  `ObjectIdentifier` de duas linhas.
- `sortProperty` em todas as colunas ordenáveis.
- Enums seguem exigindo `targetType: 'any'` no binding; sem isso o UI5 tenta formatar com
  `sap.ui.model.odata.type.Raw` e estoura `FormatException`.

**Limpeza inclusa:** remover `$expand: 'Items'` do binding de `rows`. Nenhuma coluna consome
`Items`, e `TotalInvoiceItems` já vem resolvido porque `PurchaseInvoicesGetService.QueryAll()` faz
o `Include`. Hoje a listagem carrega todas as linhas de todos os documentos sem usar nenhuma.

## Fora de escopo

- **Filtro de Filial.** `BranchCode` existe na entidade (herdado de `DocumentEntity`) mas **nunca é
  gravado**: o `create()` do `Add.controller` não o envia, ao contrário do de venda. Um filtro de
  Filial viria sempre vazio, e incluí-lo exigiria passar a gravar a filial e lidar com os documentos
  já existentes.
- Coluna "Valor dos itens" (`TotalInvoiceItems`) ao lado do valor declarado.
- Qualquer mudança em `Add`, `Edit` ou `Detail`.

## Verificação

Gates: `yarn ts-typecheck` e `yarn lint`. (`yarn test` não passa neste repo — limiar de cobertura de
50% contra ~2,4% reais; não é regressão.) O backend não muda, mas a suíte roda para provar isso.

No navegador, com a stack `yktb` e dados de teste criados na tela:

1. Cada filtro isolado retorna o subconjunto certo — em especial os três enums, que são o caminho
   que estourava antes.
2. Dois filtros combinados aplicam AND.
3. `Limpar` devolve a lista completa.
4. `Exportar Excel` baixa o arquivo com as colunas acima.
5. As ações no topo funcionam com linha selecionada, e avisam quando não há seleção.
6. O header da DynamicPage colapsa ao rolar.
