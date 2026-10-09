# Relatórios de documentos fiscais (compras e vendas) — pacote 1

Data: 2026-10-08 · Branch: `feature/fiscal-documents-reports` (backend e frontend)

## Contexto e objetivo

O usuário pediu relatórios para os módulos de compras e vendas, funcionando em SAPB1 e
STANDALONE. O pedido foi decomposto em três pacotes, cada um com spec → plano → implementação:

1. **Documentos fiscais** (este documento).
2. Logística — cargas e liberações, romaneios de venda.
3. Contratos — saldo/posição de contratos (compra e venda), posição comprado x vendido por
   produto/safra (andamento e futuro), casamento contrato de venda ↔ contratos de compra.

Decisões já tomadas com o usuário:

- `SiagroB1.Reports` **continua sendo um serviço separado** (a ideia de aglutiná-lo ao Web foi
  descartada).
- Saída **só em PDF** (as listas já têm exportação Excel própria).
- O relatório atual "Documentos de Saída" (`GET /reports/SalesInvoices`, sem filtro, SQL embutido
  no `.frx`) é **substituído** por "Notas de Saída por Período".
- Cancelados: **filtro de situação** em múltipla escolha, por padrão sem Cancelado.
- Devoluções de venda: **os dois tipos** — próprias e emitidas pelo cliente.
- Em SAPB1 as colunas fiscais que o Siagro não calcula são **ocultadas**.

## Escopo — 5 relatórios

| # | Nome (tela/menu) | Chave de menu = rota do manifest | Endpoint | Template |
|---|---|---|---|---|
| 1 | Notas de Saída por Período | `salesInvoicesReport` (rota existente, reaproveitada) | `POST /reports/SalesInvoicesByPeriod` | `SalesInvoicesByPeriod.frx` |
| 2 | Notas de Entrada por Período | `purchaseInvoicesReport` | `POST /reports/PurchaseInvoicesByPeriod` | `PurchaseInvoicesByPeriod.frx` |
| 3 | Itens dos Documentos de Saída | `salesInvoiceItemsReport` | `POST /reports/SalesInvoiceItems` | `SalesInvoiceItems.frx` |
| 4 | Itens dos Documentos de Entrada | `purchaseInvoiceItemsReport` | `POST /reports/PurchaseInvoiceItems` | `PurchaseInvoiceItems.frx` |
| 5 | Devoluções de Venda | `salesReturnsReport` | `POST /reports/SalesReturns` | `SalesReturns.frx` |

Fora de escopo: Excel, IPI e armazém (não existem nos documentos), leitura de tributos do SAP.

## Arquitetura

Segue o padrão já usado em `SalesContractsByItem` (controller fino + serviço + `.frx`):

- `Controllers/<Nome>Controller.cs` — `[HttpPost]`, `[FromBody] <Nome>Request`. Devolve 400 com
  mensagem pt-BR quando falta período ou o fim é anterior ao início. Resposta
  `application/pdf`, `Content-Disposition: inline`.
- `Services/<Nome>ReportService.cs` — `BuildRowsAsync(request)` (testável, devolve DTOs) e
  `ExecuteAsync(request)` (gera o PDF via `IFastReportService.GeneratePdfAsync`, com `pFilters` e
  `pStandalone`). Registrado pelo Scrutor (sufixo `Service`).
- `Dtos/<Nome>Request.cs` e `Dtos/<Nome>RowDto.cs`.
- `Reports/Templates/<Nome>.frx` — cabeçalho da empresa aplicado pelo `ReportHeaderService`
  como nos demais. Itens e devoluções em **paisagem**.

### Regra de modo (SAPB1 / STANDALONE)

- A consulta lê **apenas tabelas locais**: `SALES_INVOICES`, `SALES_INVOICES_ITEMS`,
  `PURCHASE_INVOICES`, `PURCHASE_INVOICES_ITEMS`, `BRANCHS` e contratos por LEFT JOIN (FK
  opcional). Nada de `ITEMS` / `BUSINESS_PARTNERS` / `WAREHOUSES` (vazias em SAPB1).
- Parceiro, produto, UM, natureza de operação, CFOP e tributos saem dos **snapshots gravados no
  documento** (`CardCode/CardName`, `ItemCode/ItemName`, `UsageName`, `Cfop`, `*Value`).
- A descrição dos filtros (`pFilters`) é montada a partir das próprias linhas do resultado; sem
  resultado imprime o código, como em `SalesContractsByItemReportService.BuildFiltersDescription`.
- **Colunas fiscais em SAPB1:** o Siagro só calcula tributos e emite NF-e em STANDALONE com
  filial "Emite NF-e pelo Siagro" (`TaxCalculationGate`). Em SAPB1 esses campos ficam zerados /
  `NfeStatus.None`. Portanto:
  - o serviço lê `ErpMode.IsStandalone(configuration)` e passa `pStandalone` (bool) ao `.frx`;
  - no `.frx`, as colunas **ICMS, PIS, COFINS, IBS/CBS, Tributos e Situação NF-e** ficam na
    **ponta direita** e todos os objetos delas (cabeçalho, dado, subtotal, total) têm nome
    iniciado por `fiscal`. O `FastReportService`, ao receber `pStandalone = false`, esconde esses
    objetos (`Visible = false`) antes do `Prepare` — sem script no `.frx`. Em SAPB1 sobra espaço
    em branco à direita;
  - no frontend, o filtro "Situação NF-e" fica invisível quando `getSystemInfo().erp === "SAPB1"`
    (inicializado como `false`, ver `usages/Main.controller.ts`), e o serviço ignora o filtro em
    SAPB1;
  - número e série da NF (`TaxDocumentNumber/Series`) aparecem nos dois modos.
- Dados conferidos em 08/10 (somente leitura, bases **dev** por decisão do usuário):
  - `IDX_SIAGRO_DEV` (Yokotobi, SAPB1): 2.385 saídas, 31 devoluções próprias, todas com
    `SalesInvoiceItemOriginKey`; só ~46% com número de NF; 7 entradas. A base está parada na
    migration `20261002032155` — sem colunas de tributo/NF-e; verificar SAPB1 exige atualizá-la
    antes (escrita, pedir autorização).
  - `CEAGUI_SIAGRO_DEV` (STANDALONE): 169 saídas (tributo em só 3 itens), 18 entradas; as 3
    importadas de XML **não trazem tributos** ⇒ a regra de ocultar em SAPB1 vale também para o
    relatório 4.
  - Nenhuma das bases tem devolução emitida pelo cliente: o caminho é coberto por testes, e a
    verificação manual exige lançar uma na CEAGUI dev (pedir autorização).
  - NF/Série em branco é caso comum (saídas sem número) e não pode quebrar o layout.

### Totais

Os totais de documento são `[NotMapped]` (calculados das linhas). A query EF aplica só filtros
traduzíveis, carrega cabeçalho + `Items` com `AsNoTracking`, e a agregação é feita em C#
reaproveitando as propriedades do domínio (`GrandTotal`, `TotalInvoiceItems`, `TotalFreight`,
`TotalDiscount`, `TotalInvoiceTaxes`, `TotalIbsCbs`, e no item `Total`, `GrandTotal`,
`TotalTaxes`, `TotalIbsCbs`). Isso mantém o PDF igual ao "Total geral" das telas e evita LINQ que
passa no InMemory e falha no SQL Server.

### Filtros comuns

| Filtro | Obrigatório | Regra |
|---|---|---|
| Período (de/até) | sim | Saída: `InvoiceDate`; Entrada: `IssueDate`; Devoluções: data do documento de devolução. Fim exclusivo `até + 1 dia`. |
| Filial | não | `BranchCode` |
| Parceiro | não | `CardCode` (cliente/emitente) |
| Produto | não | documento com ao menos um item `ItemCode`; nos relatórios de itens filtra a linha |
| Situação | não | lista de `InvoiceStatus`; vazio = todos menos `Cancelled` (padrão da tela: Pendente, Confirmado, Retornado) |
| Situação NF-e | não | lista de `NfeStatus`; só STANDALONE |

## Relatórios

### 1. Notas de Saída por Período

- Filtros próprios: Tipo (`SalesInvoiceType` Normal/Devolução; vazio = ambos).
- Linha = documento. Ordem: emissão, número interno.
- Colunas: Filial · Nº interno · NF/Série · Emissão · Cliente `(CardCode) CardName` · Tipo ·
  Situação · Sit. NF-e* · Peso líquido · Produtos · Frete · Desconto · Tributos* · IBS/CBS* ·
  Total geral.
- Rodapé: soma de Peso líquido, Produtos, Frete, Desconto, Tributos*, IBS/CBS*, Total geral; e
  contagem de documentos.
- Substitui o relatório atual: remove `SalesInvoicesController` (GET), `SalesInvoices.frx`; a tela
  `reports/salesInvoices` ganha os filtros e passa a postar no novo endpoint.

### 2. Notas de Entrada por Período

- Filtros próprios: Tipo (`PurchaseInvoiceType`), Emissão (`DocumentIssuerType` Terceiro/Própria).
- Linha = documento. Ordem: emissão, número interno.
- Colunas: Filial · Nº interno · NF/Série · Emissão · Entrada (`PostingDate`) · Emitente · Tipo ·
  Emissão própria · Situação · Sit. NF-e* · Valor declarado (`TotalDocumentValue`) · Produtos ·
  Frete · Desconto · Tributos* · Total geral.
- Rodapé: somas e contagem.

### 3. Itens dos Documentos de Saída

- Filtros próprios: Tipo, Contrato (código do contrato, texto), CFOP (texto). Sem filtro de
  natureza: não há value help de natureza reaproveitável e o CFOP cobre o recorte fiscal.
- Linha = item. **Agrupado por produto** (`ItemName (ItemCode)`), ordem dentro do grupo: emissão,
  NF.
- Colunas: Emissão · NF/Série · Cliente · CFOP · Natureza · Qtd · UM · Preço · Total · ICMS* ·
  PIS* · COFINS* · IBS/CBS* · Total geral · Contrato.
- Subtotal por produto (Qtd, Total, tributos*, Total geral) e total geral. Qtd somada por grupo
  (mesmo produto ⇒ mesma UM; se houver UMs distintas no grupo, o subtotal de Qtd é impresso por UM).

### 4. Itens dos Documentos de Entrada

- Igual ao 3, com Emitente, filtro Emissão (Terceiro/Própria), Contrato = `PurchaseContractKey`.
- Itens com `ItemCode` nulo (entrada importada sem vínculo de produto) agrupam em
  "Sem produto vinculado", mostrando `ItemName`.

### 5. Devoluções de Venda

Une duas fontes, por item devolvido:

| Origem | Documento | Nota original |
|---|---|---|
| Própria | `SalesInvoice` com `InvoiceType = Return` (inclui `IsNfeReturn`) | `SalesInvoiceItem.SalesInvoiceItemOriginKey` → item e cabeçalho originais |
| Cliente | `PurchaseInvoice` com `InvoiceType = Return` e `IssuerType = ThirdParty` | `PurchaseInvoiceItem.SalesInvoiceItemKey` → item e cabeçalho de saída |

- Filtros próprios: Emissão (Própria / Cliente / ambas).
- Período sobre a data do documento de devolução (`InvoiceDate` / `IssueDate`).
- Colunas: Data · Emissão (Própria/Cliente) · NF/Série da devolução · Nota original
  (NF/Série/data) · Cliente · Produto · Qtd devolvida · UM · Valor (`GrandTotal` da linha) ·
  Situação.
- Agrupado por cliente com subtotal de Qtd e Valor; total geral.
- Linha sem vínculo com a original mostra "—" na coluna Nota original.
- Não move saldo; é só leitura.

## Frontend

- Telas `webapp/controller/reports/<nome>/Main.controller.ts` + `webapp/view/reports/<nome>/Main.view.xml`,
  no padrão de `reports/salesContractsByItem` (JSONModel `params`, Form, value helps de Filial,
  Parceiro, Produto; Contrato e CFOP como texto; `validateForm`; `fetch` POST → blob → nova aba).
  Situação e Situação NF-e com `MultiComboBox` + defaults.
- Rotas/targets no `manifest.json` (a rota `salesInvoicesReport` é reaproveitada), entradas em
  `model/ServerRoutes.ts`, textos pt-BR no i18n.
- Seguir o skill `ui5:ui5-best-practices` (Form com ColumnLayout para telas novas).

## Menu

Migration nova no `CommonContext` inserindo, sob o pai `reports`, os 5 itens (`Key` = nome da
rota, `StandaloneOnly = false`) com `ROLE_MENUS` ADMIN; `Down` remove. A rota antiga
`salesInvoicesReport` não tinha item de menu — passa a ter.

## Tratamento de erros

- 400 com mensagem pt-BR: período ausente ou invertido.
- Resultado vazio: PDF com cabeçalho, filtros e "Nenhum registro encontrado." (não é erro).
- `BusinessException` segue o tratamento já existente do Reports.

## Testes

- `SiagroB1.Application.Tests/Reports/` — por serviço, com EF InMemory:
  filtros (período, filial, parceiro, produto, tipo, emissão, situação padrão sem Cancelado),
  totais iguais às propriedades do domínio, agrupamento, e nas devoluções a ligação com a nota
  original nas duas fontes e o caso sem vínculo.
- Teste de `pStandalone` (SAPB1 ⇒ false) e de que o filtro de Situação NF-e é ignorado em SAPB1.
- Smoke de render de cada `.frx` em `ReportTemplateRenderSmokeTests`, nos dois valores de
  `pStandalone`.
- Verificação final pelo caminho do usuário: home → Relatórios → cada tela → PDF, contra SQL
  Server real, em STANDALONE (`ceagui`) e SAPB1 (`yktb`).
