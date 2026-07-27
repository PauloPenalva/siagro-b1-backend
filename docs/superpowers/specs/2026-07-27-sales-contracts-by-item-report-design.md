# Relatório de Contratos de Venda por Produto e Período

Data: 2026-07-27

Espelho do relatório de compra (`2026-07-27-purchase-contracts-by-item-report-design.md`).
**Este documento registra apenas as diferenças** — tudo o que não está aqui é igual ao
relatório de compra: período de emissão obrigatório e inclusivo até o fim do dia,
sobreposição de janela no período de entrega, cancelados fora e rascunhos dentro,
quebra por produto com subtotal, total geral, eco dos filtros no cabeçalho, A4 paisagem,
Consolas 7pt.

## Por que não é um espelho literal

O contrato de venda não tem três coisas que o de compra tem:

| Coluna na compra | Situação na venda |
|---|---|
| Funrural | Não existe (`FunruralType` é só de compra) — coluna removida |
| Comissão | Não existe entidade de corretor para venda — coluna removida |
| Frete (tipo + valor) | Só o tipo: não há `FreightCostStandard` em `SalesContract` |
| Local de entrega (1:1) | Em venda são vários (`DeliveryLocations`, 1:N) |

Decisões do usuário para essas lacunas:

- No lugar do local de entrega entra a **Região Logística** (`LogisticRegion.Name`),
  que é 1:1 no contrato e resolve o problema de imprimir uma coleção numa célula.
- No espaço liberado por Funrural/Comissão entra **Tipo Mercado**
  (`MarketType`: Interno / Exportação).

## Colunas

Largura útil 1084; soma fecha exatamente.

| # | Coluna | Origem | Largura |
|---|---|---|---|
| 1 | Contrato | `Code` | 52 |
| 2 | Filial | `Branch.ShortName` (ou `BranchName`) | 84 |
| 3 | Região Logística | `LogisticRegion.Name` | 180 |
| 4 | Cliente | `CardName` | 280 |
| 5 | Qtde | `TotalVolume` | 84 |
| 6 | UM | `UnitOfMeasureCode` | 24 |
| 7 | Preço | `Price` | 44 |
| 8 | Mercado | `MarketType` | 62 |
| 9 | Prev. Pagto. | `StandardCashFlowDate` | 60 |
| 10 | Frete | `FreightTerms` | 64 |
| 11 | Vendedor | `AgentName` | 150 |

Rótulos seguem a tela de venda: **Cliente** (não "Fornecedor") e **Vendedor**
(não "Comprador"). Filial e Região Logística imprimem só o nome, sem o código.

Textos derivados:

- **Mercado**: `Internal` → "Interno", `External` → "Exportação"; vazio quando nulo.
- **Frete**: "CIF" / "FOB" / "Sem frete" — sem valor, porque o campo não existe.
- **Preço**: `SalesContract.Price` (o equivalente ao `StandardPrice` da compra), sempre
  o do cabeçalho, sem consultar fixações — mesma regra da compra.

## Filtros

Os mesmos sete da compra, com uma troca: **Local de entrega → Região Logística**
(`LogisticRegionCode`), para o filtro casar com a coluna que passou a ser impressa.
"Fornecedor" vira **Cliente** (`CardCode`, value help de clientes).

Fica de fora um filtro por local de entrega (que exigiria casar contra a coleção 1:N) —
se fizer falta, é acréscimo simples depois.

## Arquivos

Espelham os da compra, trocando `PurchaseContracts` por `SalesContracts`:

- `SiagroB1.Reports/Dtos/SalesContractsByItemRequest.cs` e `...RowDto.cs`
- `SiagroB1.Reports/Services/SalesContractsByItemReportService.cs`
- `SiagroB1.Reports/Controllers/SalesContractsByItemController.cs` — `POST /reports/SalesContractsByItem`
- `SiagroB1.Reports/Reports/Templates/SalesContractsByItem.frx`
- Frontend: `view/reports/salesContractsByItem/Main.view.xml`, controllers, rota
  `sales-contracts-by-item/report` (nome `salesContractsByItemReport`), `ServerRoutes.ts`
- Migration de menu no `CommonContext`: Key `salesContractsByItemReport`, título
  "Contratos de Venda por Produto", pai `reports`, ordem 6

`LogisticRegion` e `Branch` são tabelas locais com FK opcional — `Include` gera LEFT JOIN
e é seguro em modo SAPB1. Cliente, produto e vendedor continuam vindo dos campos
desnormalizados do contrato.
