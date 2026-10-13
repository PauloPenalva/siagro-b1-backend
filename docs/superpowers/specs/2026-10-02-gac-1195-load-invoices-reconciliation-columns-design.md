# GAC-1195 — Descarga e conferência nos grids de documentos de saída

**Data:** 2026-10-02
**Chamado:** GAC-1195 (Yokotobi). Continua o GAC-1171 (ticket de descarga, rateio, "Descarregada")
e o GAC-1189 (coluna Local de Entrega no mesmo grid).
**Branch:** `feature/gac-1195-load-invoices-reconciliation-columns`, nos worktrees
`siagro-b1-backend-gac-1195` e `siagro-b1-frontend-gac-1195`, criados a partir de `main`
(`337fde4` / `56c7704`).

## 1. O pedido

> "no grid Documentos de Saída dos detalhes da carga, inserir as informações:
>
> NF - Número da NF informado através dos documentos de saída;
>
> Peso efetivo - Peso efetivo, mais para os casos de retorno parcial, mostrando o peso que foi
> descarregado - Verificar sobre a criação do status 'Retornado Parcialmente' - Pode inserir no grid
> dos documentos de saída também;
>
> Peso descarregado (Qtd. Ticket da Conferência de entregas) - Para facilitar a visualização do peso
> descarregado atribuído ao documento de saída, mostrar no grid o peso descarregado registrado - Pode
> inserir no grid dos documentos de saída também;
>
> Qtd.Entregue - Peso registrado na Conferência de entregas - Pode inserir no grid dos documentos de
> saída também;
>
> Status da descarga - Marcar o documento de saída também com o status 'Descarregada';
>
> Status da conferência de entrega - Similar a como aparece nas entregas dos contratos de venda
> (Encerrado/Aberto);
>
> Talvez tenhamos que pensar em nomenclaturas diferentes para o fluxo de conferência através do ticket
> de descarga e através das conferências de entrega para não confundirem, sobre isso também estou em
> dúvida sobre as diferenças a serem mostradas na carga (peso doc. saída x peso descarregado ticket e
> peso doc. saída x peso da conferência de entrega)."

### O que já existia

| Pedido | Situação em `main` |
|---|---|
| NF | O grid da carga mostra "Número" = `InvoiceNumber`, o número **interno**. A NF é `TaxDocumentNumber`, informada em "Informar Nota Fiscal". A lista de documentos de saída já tem a coluna "Nota Fiscal". |
| Peso efetivo | `NetWeight` e `ReturnedQuantity` estão no cabeçalho. O conceito "faturado que não voltou" já existe no código como `ShipmentLoadDischargeRules.RemainingQuantity` (base do rateio do ticket). |
| "Retornado Parcialmente" | Recusado como status no GAC-1171 (D5 daquele spec): `Confirmed` é testado em ~20 regras. Existe o selo "Dev. parcial" no grid da carga, na lista e no detalhe do documento. |
| Peso descarregado e Qtd. Entregue | Existem **na linha** (`SalesInvoiceItem.TicketDeliveredQuantity`, `SalesInvoiceItem.DeliveredQuantity`). Uma coleção não se binda em célula de `sap.ui.table`. |
| Status da descarga | A **carga** fica "Descarregada" quando toda linha de nota Normal + Confirmada com faturado que não voltou tem peso de ticket (`ShipmentLoadsRecalculateInvoicedService.EvaluateClosureAsync`). O documento não tem indicador próprio. |
| Status da conferência | Por linha (`SalesInvoiceItem.DeliveryStatus`, Aberto/Encerrado). O `SalesInvoice.DeliveryStatus` do cabeçalho **não** acompanha a Conferência: só o retorno legado escreve nele. |

### Decisões do usuário (fechadas no brainstorming)

| # | Decisão |
|---|---|
| D1 | As duas diferenças são **contra o Peso Efetivo**: Dif. Ticket = Qtd. Ticket − Qtd. Efetiva; Dif. Conferência = Qtd. Entregue − Qtd. Efetiva. Vazias enquanto a medição não foi informada. |
| D2 | **Nomenclatura**: os nomes que o usuário já vê na Conferência de entregas (Qtd. Ticket, Qtd. Entregue), cada coluna derivada marcada pelo fluxo — **Ticket/Descarga** de um lado, **Entregue/Conferência** do outro. |
| D3 | O selo **"Dev. parcial" fica como está**. Nenhum status novo, nenhum texto renomeado. |
| D4 | A **lista de Documentos de Saída** recebe o mesmo conjunto de colunas do grid da carga, inclusive no Exportar Excel. |
| D5 | **Derivar no servidor**, sem persistir: propriedades `[NotMapped]` no documento de saída, calculadas das linhas. |
| D6 | A Descarga mostra **"Aguardando"**, e não "Pendente": "Pendente" já é o texto do status do documento, na mesma linha. |
| D7 | A tela de Conferência de entregas **não muda**. A "Diferença" dela continua Entregue − Quantidade faturada; numa nota devolvida em parte ela e a nova "Dif. Conferência" mostram números diferentes, de propósito. |

## 2. As regras

Tolerância: `0,001` (a casa decimal das quantidades, a mesma de `ShipmentLoadDischargeRules.Tolerance`).
Todas as somas e diferenças são arredondadas em 3 casas (`MidpointRounding.ToEven`).

### 2.1 Linha

`SalesInvoiceItem.RemainingQuantity` (`[NotMapped]`) = `Quantity − ReturnedQuantity`, em 3 casas.
`ShipmentLoadDischargeRules.RemainingQuantity(item)` passa a devolver `item.RemainingQuantity`, para
que a Qtd. Efetiva da tela e a base do rateio do ticket nunca divirjam.

⚠️ Não confundir com `SalesInvoiceItem.NetQuantity`, que é o conferido menos a quebra.

### 2.2 Documento

| Propriedade (`SalesInvoice`) | Tela | Tipo | Valor | Nula quando |
|---|---|---|---|---|
| `RemainingQuantity` | Qtd. Efetiva | `decimal?` | Σ `Items.RemainingQuantity` | `InvoiceType = Return` ou `InvoiceStatus = Cancelled` |
| `TicketDeliveredQuantity` | Qtd. Ticket | `decimal` | Σ `Items.TicketDeliveredQuantity` | nunca (zero sem ticket) |
| `TicketDifference` | Dif. Ticket | `decimal?` | `TicketDeliveredQuantity − RemainingQuantity` | `RemainingQuantity` nula, ou `TicketDeliveredQuantity ≤ 0,001` |
| `IsDischarged` | Descarga | `bool?` | `true` se toda linha com `RemainingQuantity > 0,001` tem `TicketDeliveredQuantity > 0,001` | nota que não é `Normal`, sem `ShipmentLoadKey`, não `Confirmed`, ou sem linha com `RemainingQuantity > 0,001` (voltou inteira) |
| `DeliveredQuantity` | Qtd. Entregue | `decimal` | Σ `Items.DeliveredQuantity` | nunca (zero sem conferência) |
| `ReconciliationDifference` | Dif. Conferência | `decimal?` | `DeliveredQuantity − RemainingQuantity` | `RemainingQuantity` nula, ou `DeliveredQuantity ≤ 0,001` |
| `IsDeliveryClosed` | Conferência | `bool?` | `true` se todas as linhas estão `Closed` | nota que não é `Normal`, não `Confirmed`, ou sem linhas — o mesmo escopo da tela de Conferência de entregas |

Notas sobre as regras:

- **`IsDischarged` é a regra da carga aplicada a um documento.** A carga só é "Descarregada" se todos
  os seus documentos Normais Confirmados o forem (e nenhum estiver Pendente). Os dois lugares citam um
  ao outro em comentário; a regra da carga **não** é reescrita neste chamado.
- **`ReconciliationDifference` tem esse nome de propósito**: a linha já tem `DeliveryDifference`
  (Entregue − Quantidade, computed column persistida), com outra fórmula.
- **Status `Returned` (devolução total)** continua com `RemainingQuantity` calculada: a origem vira
  `Returned` já na criação do retorno, e o `ReturnedQuantity` só sobe quando o retorno é confirmado.
  Descarga e Conferência ficam vazias, porque a nota não é `Confirmed`.
- **Documento avulso** (sem carga) tem Descarga vazia e Qtd. Ticket zero. A Conferência vale para ele,
  como vale na tela de Conferência de entregas.

## 3. Backend

- **`SiagroB1.Domain/Entities/SalesInvoiceItem.cs`**: `RemainingQuantity` `[NotMapped]`.
- **`SiagroB1.Domain/Entities/SalesInvoice.cs`**: as sete propriedades `[NotMapped]` da §2.2, puras,
  dependentes só de `Items` carregado — o mesmo contrato de `SalesContractCode` e `TotalInvoiceItems`.
- **`SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadDischargeRules.cs`**: `RemainingQuantity`
  delega para a linha.
- **`SiagroB1.Web/ODataConfig/ODataConfigurations.cs`**: `AddProperty` das sete no tipo `SalesInvoice`.
  Sem isso o `$select` devolve 400. `SalesInvoiceItem.RemainingQuantity` **não** entra no EDM: nenhuma
  tela a binda.
- **Consultas:** nenhuma muda. `SalesInvoicesGetService.QueryAll` e `ShipmentLoadsGetService.QueryInvoices`
  já fazem `Include(Items)`.
- **Sem migration, sem escritor novo, sem mudança de saldo.** Não é possível `$filter`/`$orderby` por
  estas colunas — mesma limitação de `TotalInvoiceItems`.

## 4. Frontend

### 4.1 Grid "Documentos de Saída" do detalhe da carga (`shipmentLoads/Detail.view.xml`, `loadInvoicesTable`)

Ordem final das colunas (novas em **negrito**):

Número | **Nota Fiscal** | Tipo | Status | Emissão | Cliente | Local de Entrega | Qtde | Qtd. Devolvida |
**Qtd. Efetiva** | **Qtd. Ticket** | **Dif. Ticket** | **Descarga** | **Qtd. Entregue** | **Dif. Conferência** |
**Conferência** | Tipo Frete | Valor do Frete | Contrato de Venda | Complemento | Frete Std. Contrato

- **Nota Fiscal**: a mesma expressão da lista — "Sem NF" quando `WithoutTaxDocument`, senão
  `TaxDocumentNumber`. `targetType: 'any'` no booleano (sem ele o tipo OData entrega "Não", que é
  truthy, e toda linha vira "Sem NF").

### 4.2 Lista de Documentos de Saída (`salesInvoices/Main.view.xml`, `tableSalesInvoices`)

As sete colunas novas entram depois de "Serie", na mesma ordem do §4.1. Entram também:

- no `$select` do binding (`ShipmentLoadKey,WithoutTaxDocument` + as sete): a exportação lê do binding,
  e coluna escondida pelo usuário não gera `$select` sozinha;
- no `createColumnConfig` do `salesInvoices/BaseController.ts`: números com 3 casas; Descarga e
  Conferência como booleano com `trueValue`/`falseValue` em pt-BR.
  ⚠️ **Conferir no arquivo exportado como o booleano nulo sai.** A documentação do `sap.ui.export` não
  diz. Se sair como `falseValue` ("Aguardando" numa nota avulsa), trocar as duas colunas da planilha
  por `EdmType.Enumeration` com `valueMap: { "true": ..., "false": ... }` e conferir de novo — o nulo
  não tem chave no mapa.

### 4.3 Formatação (`model/formatter.ts`, com QUnit em `test/unit/model/formatter.qunit.ts`)

| Coluna | Texto | Estado |
|---|---|---|
| Qtd. Efetiva | 3 casas; vazio quando nulo | — |
| Qtd. Ticket, Qtd. Entregue | 3 casas; **vazio quando zero** (zero pareceria "chegou nada") | — |
| Dif. Ticket, Dif. Conferência | 3 casas com sinal (`+50,000`, `-100,000`); vazio quando nulo | `Success` quando zero, `Error` quando diferente, `None` quando nulo |
| Descarga | Descarregada / Aguardando / vazio | `Success` / `Warning` / `None` |
| Conferência | Encerrado / Aberto / vazio | `Success` / `None` / `None` |

Todos os bindings usam `targetType: 'any'`: `Edm.Decimal` chega como string e `Edm.Boolean` chega
como texto localizado sem ele.

## 5. Testes e verificação

- **xUnit** (`SiagroB1.Application.Tests`): cada linha da tabela §2.2, inclusive todos os casos "nula
  quando"; `SalesInvoiceItem.RemainingQuantity`; e `ShipmentLoadEdmModelTests` exigindo as sete no EDM.
  Suíte inteira verde.
- **QUnit**: os formatters do §4.3, inclusive nulo, zero e string vinda do `Edm.Decimal`.
- **Gates do frontend**: `yarn ts-typecheck` e `yarn lint` (o `yarn test` não passa por causa do gate
  de cobertura, independentemente da mudança).
- **Navegador** (perfil `yktb`, backend rodando **deste worktree**, navegando pelo menu a partir da
  home): na carga com ticket rateado e na lista, conferir uma nota descarregada, uma conferida, uma com
  devolução parcial e uma avulsa; conferir o Excel exportado da lista.

## 6. Fora do escopo

- Status novo "Retornada parcialmente" (D3) e renomear o selo "Dev. parcial".
- Qualquer mudança na tela de Conferência de entregas (D7), inclusive a "Diferença" dela.
- Filtro ou ordenação da lista pelas colunas novas.
- Sincronizar o `SalesInvoice.DeliveryStatus` do cabeçalho com as linhas.
- O detalhe do documento de saída.
