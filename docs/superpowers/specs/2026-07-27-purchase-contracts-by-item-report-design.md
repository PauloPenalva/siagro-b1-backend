# Relatório de Contratos de Compra por Produto e Período

Data: 2026-07-27

## Objetivo

Conferência diária dos negócios de compra fechados: listar os contratos de compra
emitidos num período, quebrados por produto, com os dados comerciais que a mesa
precisa revisar (quantidade, preço, funrural, previsão de pagamento, comissão de
corretagem, frete e comprador).

O relatório é um PDF gerado pelo FastReport, seguindo exatamente o padrão dos
relatórios já existentes (`StorageDailyBalance`, `StorageAddressesBalance`):
tela UI5 com os filtros → `POST /reports/<Nome>` → PDF aberto em nova aba.

## Filtros

| Rótulo na tela | Campo do contrato | Obrigatório |
|---|---|---|
| Emissão de / até | `CreationDate` | **sim** |
| Produto | `ItemCode` | não |
| Safra | `HarvestSeasonCode` | não |
| Filial | `BranchCode` | não |
| Local de entrega | `DeliveryLocationCode` | não |
| Fornecedor | `CardCode` | não |
| Entrega de / até | `DeliveryStartDate` / `DeliveryEndDate` | não |

Regras:

- **Emissão** é o único par obrigatório. O limite superior é inclusivo até o fim do
  dia (`CreationDate >= De` e `CreationDate < Até + 1 dia`), porque `CreationDate`
  guarda data e hora.
- **Período de entrega usa sobreposição de janela**: entra o contrato cujo intervalo
  de entrega cruza o período informado — `DeliveryStartDate <= Até` **e**
  `DeliveryEndDate >= De`. Cada metade do filtro é aplicada só se preenchida (informar
  apenas "Entrega de" filtra só por `DeliveryEndDate >= De`).
- Filtros vazios não restringem nada.
- Contratos com status **Cancelado** (`ContractStatus.Canceled`) nunca aparecem.
  Todos os demais status entram, inclusive rascunho.

Os value helps de produto, safra, filial, armazém (local de entrega) e fornecedor já
existem em `CommonController` (`openItemValueHelp`, `openHarvestSeasonsValueHelp`,
`openBranchsValueHelp`, `openWarehouseValueHelp`, `openSuppliersValueHelp`).

## Layout do PDF

A4 **paisagem** (`RawPaperSize="9"` + `Landscape="true"`), largura útil ≈ 1047.

```
[logo]  COMERCIO DE CEREAIS YOKOTOBI LTDA                              27/07/2026
                    Contratos de Compra por Produto
        Emissão: 01/07/2026 a 27/07/2026 | Filial: 01 - MATRIZ | Safra: 2026

▼ SOJA EM GRÃOS (10001)
 Contrato Status    Filial     Local entrega  Fornecedor      Qtde    Preço Funrural Prev.Pagto Comissão                Frete       Comprador
 CC-00123 Aprovado  01-MATRIZ  AZ01-SILO 1    AGRO SANTA FE 1.500,000 128,50 Bruto    15/08/2026 João Silva - 2,00 TN;  CIF - 45,00 Carlos Dias
                                                                                                 Maria Souza - 1,50 TN
 CC-00124 Rascunho  01-MATRIZ  AZ02-SILO 2    FAZENDA BOA VI 2.700,000 130,00 Livre    30/08/2026                       FOB - 0,00  Carlos Dias
                                              Subtotal SOJA EM GRÃOS:  2 contratos   4.200,000
▼ MILHO (10002)
 ...
                                              TOTAL GERAL:             7 contratos   9.750,000
```

### Colunas

Larguras em unidades de 1/96 pol; a soma fecha a largura útil da página (1084).

| # | Coluna | Origem | Largura | Formato |
|---|---|---|---|---|
| 1 | Contrato | `Code` | 68 | texto |
| 2 | Filial | `Branch.ShortName` (ou `BranchName`) | 110 | só o nome, ex. `MATRIZ` |
| 3 | Local de entrega | `DeliveryLocationName` | 145 | só o nome, ex. `SILO 1` |
| 4 | Fornecedor | `CardName` | 156 | texto |
| 5 | Quantidade | `TotalVolume` | 92 | número, 3 decimais, pt-BR |
| 6 | UM | `UnitOfMeasureCode` | 32 | texto, logo após a quantidade |
| 7 | Preço | `StandardPrice` | 60 | número, 2 decimais, pt-BR |
| 8 | Funrural | `FunruralType` | 52 | `Livre` / `Bruto` |
| 9 | Prev. Pagto. | `StandardCashFlowDate` | 66 | `dd/MM/yyyy` |
| 10 | Comissão | `Brokers` concatenados | 125 | ver abaixo |
| 11 | Frete | `FreightTerms` + `FreightCostStandard` | 78 | ver abaixo |
| 12 | Comprador | `AgentName` | 92 | texto |

Filial e local de entrega imprimem **apenas o nome** — sem o código. Sem nome
cadastrado, cai para o código, para a célula nunca sair vazia.

A unidade de medida fica numa **coluna própria**, e não concatenada na quantidade:
`Quantity` precisa continuar numérico para os subtotais somarem.

Não há coluna de status. Cancelados continuam fora do relatório e rascunhos continuam
dentro — só não é mais possível distingui-los na listagem.

O produto (`ItemCode` / `ItemName`) não é coluna: vira cabeçalho de grupo.

### Regras de formatação de texto

- **Preço**: sempre o `StandardPrice` do contrato, sem consultar fixações. Contrato
  PAF (a fixar) sai com preço zerado — é o comportamento pedido.
- **Comissão**: `CardName - Commission ComissionUmCode`, um corretor por linha dentro
  da mesma célula, separados por `; `. Sem corretores, célula vazia. A célula cresce
  em altura (`CanGrow`) e a banda de dados acompanha.
- **Frete**: `CIF - 45,00` ou `FOB - 45,00`; quando `FreightTerms = None`, imprime só
  `Sem frete` (sem valor).
- **Funrural**: rótulo do enum (`Livre`, `Bruto`); vazio quando nulo.

### Agrupamento e totais

- Quebra por produto, ordenada por `ItemName`; dentro do grupo, ordem `CreationDate`,
  depois `Code`.
- Rodapé de grupo: contagem de contratos e soma de `TotalVolume` do produto.
- Resumo do relatório: contagem total e soma total de `TotalVolume`.
- Preço **não** é totalizado (soma de preços unitários não tem significado).

### Eco dos filtros aplicados

O serviço monta **uma string única** com os filtros preenchidos, separados por ` | `,
e passa como parâmetro `pFilters`; o template imprime num `TextObject` de duas linhas
com quebra automática no cabeçalho da página. Filtros vazios são omitidos.

Exemplo: `Emissão: 01/07/2026 a 27/07/2026 | Produto: 10001 - SOJA EM GRÃOS | Filial: 01 - MATRIZ | Entrega: 01/08/2026 a 30/09/2026`

Montar a string no serviço (e não condicionar objetos no `.frx`) mantém o template
simples e deixa a regra testável em C#. Código e descrição de produto, filial, local
de entrega e fornecedor são resolvidos no serviço a partir do próprio filtro; quando a
descrição não for encontrada, imprime só o código.

## Backend — `SiagroB1.Reports`

Nome do recurso: **PurchaseContractsByItem**.

| Arquivo | Conteúdo |
|---|---|
| `Dtos/PurchaseContractsByItemRequest.cs` | `FromDate`, `ToDate` (obrigatórios), `ItemCode`, `HarvestSeasonCode`, `BranchCode`, `DeliveryLocationCode`, `CardCode`, `DeliveryFromDate`, `DeliveryToDate` |
| `Dtos/PurchaseContractsByItemRowDto.cs` | Linha achatada: `ItemCode`, `ItemName`, `ContractCode`, `Status`, `Branch`, `DeliveryLocation`, `Supplier`, `Quantity`, `Price`, `Funrural`, `PaymentForecast`, `Commission`, `Freight`, `Buyer` — já em texto pronto para o template |
| `Services/PurchaseContractsByItemReportService.cs` | Consulta, formatação e chamada ao `IFastReportService` |
| `Controllers/PurchaseContractsByItemController.cs` | `POST /reports/PurchaseContractsByItem`, valida `ModelState`, devolve `File(pdf, "application/pdf")` com `Content-Disposition: inline` |
| `Reports/Templates/PurchaseContractsByItem.frx` | Template escrito à mão (XML) |

Detalhes da consulta:

- `db.Context.PurchaseContracts.AsNoTracking().Include(x => x.Brokers).Include(x => x.Branch)`.
  `Branch` é tabela local (`BRANCHS`) e a FK é opcional, então o `Include` gera LEFT JOIN
  e não zera nada — diferente de navegar para entidade do SAP.
- `CardName`, `DeliveryLocationName`, `ItemName` e `AgentName` são lidos dos campos
  desnormalizados do próprio contrato: em modo SAPB1 as tabelas locais correspondentes
  estão vazias e qualquer navegação zeraria o resultado.
- A concatenação de corretores, o mapeamento dos enums e a ordenação acontecem em
  memória, depois do `ToListAsync()`.
- O DI de `SiagroB1.Reports` é auto-scan por sufixo `Service` — não há registro manual
  a fazer.

O `.frx` precisa conter `picLogo` e o parâmetro `pCompanyName`, senão os testes de guarda
já existentes (`ReportTemplateHeaderTests`) quebram; e precisa preparar/exportar sem dados
registrados para passar em `ReportTemplateRenderSmokeTests`.

## Frontend — `siagro-b1-frontend`

| Arquivo | Conteúdo |
|---|---|
| `webapp/view/reports/purchaseContractsByItem/Main.view.xml` | `Page` + `f:SimpleForm` com os filtros + footer com botão "Imprimir" |
| `webapp/controller/reports/purchaseContractsByItem/BaseController.ts` | Estende `CommonController` (mesmo padrão dos outros relatórios) |
| `webapp/controller/reports/purchaseContractsByItem/Main.controller.ts` | Modelo `params`, limpeza no `patternMatched`, `onPrintReport` com `fetch` + `window.open(blobUrl)` |
| `webapp/manifest.json` | Rota `purchase-contracts-by-item/report`, nome `purchaseContractsByItemReport`, target level 1 com `clearControlAggregation` |
| `webapp/model/ServerRoutes.ts` | Entrada para `/reports/PurchaseContractsByItem` |

Só os dois campos de emissão são `required`; `onPrintReport` valida o formulário antes
de chamar o backend (`validateForm`), como nos relatórios existentes.

Armadilhas de UI5/OData v4 que valem aqui: `DatePicker` ligado a modelo JSON usa
`sap.ui.model.odata.type.DateTimeOffset` com `constraints: { precision: 7 }`, e as
descrições dos value helps vêm por `CustomData key="descriptionProperty"` (nunca por
formatter assíncrono).

## Migration de menu — `SiagroB1.Migrations/CommonContext`

`AddPurchaseContractsByItemReportMenu`:

- `MENU_ITEMS`: Key `purchaseContractsByItemReport` (**igual ao nome da rota** — o
  `App.controller.ts` navega com `navTo(item.getKey())`), Title
  "Contratos de Compra por Produto", ícone `sap-icon://folder-blank`, `Enabled = true`,
  `Expanded = false`, `Order = 5`, `ParentKey = "reports"`.
- `ROLE_MENUS`: vínculo com a role `ADMIN` (GUID fixo no arquivo, para o `Down` apagar).

Sem esses dois inserts a tela existe mas não é alcançável pelo menu.

## Testes — `SiagroB1.Application.Tests`

O projeto já referencia `SiagroB1.Reports` e tem a pasta `Reports/`. Novos testes em
`Reports/PurchaseContractsByItemReportServiceTests.cs`, com EF InMemory
(`Support/TestDb.cs`), cobrindo a montagem das linhas (o método que devolve
`IReadOnlyList<PurchaseContractsByItemRowDto>`, separado da geração do PDF):

1. Filtra pelo período de emissão, incluindo contrato criado no último dia às 23h.
2. Exclui contrato Cancelado; mantém Rascunho.
3. Cada filtro opcional restringe o resultado (produto, safra, filial, local de
   entrega, fornecedor).
4. Período de entrega por sobreposição: contrato que começa antes e termina dentro do
   período entra; contrato inteiramente fora não entra.
5. Corretores concatenados numa única linha, na ordem em que estão no contrato;
   contrato sem corretor devolve célula vazia.
6. Frete com `None` imprime "Sem frete" sem valor; CIF/FOB imprimem tipo e valor.
7. Ordenação por produto e, dentro do produto, por data de criação.
8. String de filtros aplicados omite os filtros vazios.

O template novo entra automaticamente em `ReportTemplateHeaderTests` e
`ReportTemplateRenderSmokeTests`.

## Fora de escopo

- Exportação para Excel/CSV.
- Preço efetivo das fixações (PAF) — decidido usar sempre o preço standard.
- Relatório espelho para contratos de venda.
