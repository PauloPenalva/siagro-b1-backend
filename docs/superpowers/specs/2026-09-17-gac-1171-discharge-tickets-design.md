---
status: desenho aprovado 17/09/2026 — não implementado
chamado: GAC-1171
---

# Design — Registro de pesos de descarga (tickets) na carga e grid de anexos

## Contexto

Hoje o único lugar onde o peso realmente recebido pelo destino entra no sistema é a **Conferência
de Entregas**, que mora no item do documento de saída (`SalesInvoiceItem.DeliveredQuantity`,
`QuantityLoss`, `DeliveryStatus`). Essa conferência passa a ser responsabilidade do Nauhan, que
confere os relatórios recebidos das tradings.

Falta o passo anterior: quando o caminhão descarrega, chega um **ticket de descarga** com número,
data e peso. Esse documento é da carga, não do relatório da trading, e quem o tem em mãos é a
Logística. Sem lugar para registrá-lo, o peso da balança do destino só entra no sistema dias
depois, pela conferência, e o ticket em si não fica em lugar nenhum.

O chamado pede duas coisas na carga: **registrar as descargas** e **anexar documentos** (tickets
de carga, de descarga e outros relacionados).

## Para que serve o ticket

Duas funções, ditas pelo usuário, e as duas explicam o desenho:

1. **Liberar o pagamento do frete.** É o documento que comprova que a carga chegou e quanto
   chegou.
2. **Confrontar o relatório de descarga do cliente/trading.** Os dois lados podem estar errados por
   motivos diferentes: o **ticket pode ter sido adulterado pelo transportador**, e o **relatório da
   trading pode ter sido alimentado com informação errada**.

É a segunda função que fixa a regra central: o ticket **não** é fonte da conferência. São duas
testemunhas independentes, e o valor do sistema está em mostrar quando elas discordam. Fazer o
ticket preencher o peso conferido transformaria o número possivelmente adulterado no padrão da
conferência — o oposto do que o chamado quer.

## Decisões do usuário (17/09/2026)

1. O ticket alimenta um campo **próprio**: `SalesInvoiceItem.TicketDeliveredQuantity`. **Não**
   escreve em `DeliveredQuantity`, em nenhum estado da entrega. Uma operação não trava a outra.
2. **N tickets por carga**, em tabela filha — cobre o caminhão que descarrega em mais de um
   destino e o ticket corrigido.
3. O ticket **aponta obrigatoriamente para uma nota** da carga. Consequência aceita: não se
   registra descarga antes de a nota existir.
4. Quando a nota tiver mais de uma linha, **o usuário escolhe a linha**. Nenhum rateio implícito.
5. Na Conferência de Entregas, exibir a **diferença entre `TicketDeliveredQuantity` e
   `DeliveredQuantity`**, e **somente quando os dois forem maiores que zero** — sem os dois lados
   presentes não há confronto, e mostrar a diferença contra um zero só produziria alarme falso.
6. A **conferência da entrega é mandatória e soberana**: `DeliveredQuantity` é digitado pelo Nauhan
   a partir do relatório da trading e nenhum ticket o altera. O registro de descarga, por sua vez,
   nunca é barrado por causa do estado da entrega.
7. O grid de anexos tem **tipo de lista fechada**, e o diálogo de descarga permite **anexar o
   ticket na mesma hora**, já tipado e ligado ao registro.
8. **Sem índice único** de ticket por carga: o mesmo número pode ser lançado duas vezes e nada vai
   acusar. Decisão consciente — a numeração das tradings pode repetir entre destinos.

## Fora de escopo, próxima fase

- **Relatório de inconsistências de entrega**, para apuração das divergências acumuladas entre
  ticket e relatório da trading. Decidido como fase seguinte: esta entrega só cria o dado e o
  confronto visível linha a linha.
- **Ato de liberação do pagamento do frete.** Este chamado entrega o insumo — ticket registrado,
  anexo guardado, descarregado × embarcado no cabeçalho da carga — e o financeiro libera olhando
  isso. Nenhuma flag, ação, data de liberação ou valor de frete apurado sobre o peso descarregado
  entra agora; a regra será definida quando essa fase for aberta.

## Desenho

### Por que o registro do ticket não move saldo nenhum

`SalesContractsRecalculateBalanceService` só considera o item quando
`DeliveryStatus == SalesInvoiceDeliveryStatus.Closed`, e o que ele lê ali é
`DeliveredQuantity - QuantityLoss` (linhas 63–65). `TicketDeliveredQuantity` é coluna nova e não
entra em fator efetivo nenhum — nem no do contrato, nem no da liberação de entrega
(`SalesShipmentReleasesRecalculateShippedService` segue a mesma regra). Logo, **registrar,
corrigir ou excluir ticket é operação sem efeito em saldo, em qualquer estado da entrega**. Quem
move saldo continua sendo um único ato, o encerramento da conferência pelo Nauhan.

A coluna calculada `DeliveryDifference` (`CASE WHEN DeliveredQuantity = 0 AND DeliveryStatus = Open
THEN 0 ELSE DeliveredQuantity - Quantity END`) também não é afetada: ela olha só
`DeliveredQuantity`. A diferença ticket × relatório é calculada **na tela** (decisão 5), não no
banco, porque depende dos dois valores serem positivos e isso é regra de exibição.

### Modelo

**`SHIPMENT_LOAD_DISCHARGES`** (`ShipmentLoadDischarge`), filha da carga no molde de
`ShipmentLoadComment`:

| Coluna | Tipo | Nota |
|---|---|---|
| `Key` | Guid identity | |
| `ShipmentLoadKey` | Guid FK → `SHIPMENT_LOADS` | indexado |
| `SalesInvoiceKey` | Guid FK → `SALES_INVOICES` | `NoAction`; o grid chega ao número da nota por `$expand` de um nível |
| `SalesInvoiceItemKey` | Guid FK → `SALES_INVOICES_ITEMS` | `NoAction`; é o alvo do peso |
| `TicketNumber` | VARCHAR(50) NOT NULL | |
| `DischargeDate` | DATETIME | data informada no ticket |
| `DischargedQuantity` | DECIMAL(18,3) | peso do ticket |
| `Comments` | VARCHAR(500) NULL | |
| `AttachmentKey` | Guid NULL FK → `SHIPMENT_LOAD_ATTACHMENTS` | arquivo do ticket, quando anexado no diálogo |
| `CreatedAt/CreatedBy/UpdatedAt/UpdatedBy` | | carimbos |

Número da nota e nome do produto **não** são denormalizados: o número é atribuído depois, por
`SalesInvoicesSetDocumentNumberService`, e snapshot de campo que muda já custou o "nome fantasia
vazio" do contrato.

**`SHIPMENT_LOAD_ATTACHMENTS`** (`ShipmentLoadAttachment`): cópia de
`WarehouseReconciliationAttachment` (arquivo em `VARBINARY(MAX)`, `Description`, `FileName`,
`ContentType`, `CreatedAt/By`) mais `AttachmentType` (int) sobre o enum novo
`ShipmentLoadAttachmentType`:

| Valor | Tela |
|---|---|
| `LoadingTicket` = 0 | Ticket de Carga |
| `DischargeTicket` = 1 | Ticket de Descarga |
| `TaxDocument` = 2 | Nota Fiscal |
| `FreightDocument` = 3 | Conhecimento de Frete |
| `Other` = 4 | Outro |

⚠️ Valor novo entra **sempre no fim da numeração**, pelo mesmo motivo que
`ShipmentLoadStatus.Planned` é 4: o enum é persistido como int e renumerar reescreve o
significado de toda linha já gravada, sem gerar migration.

**Duas colunas persistido-derivadas:**

- `SALES_INVOICES_ITEMS.TicketDeliveredQuantity` DECIMAL(18,3) DEFAULT 0 — soma dos tickets do
  item. O nome diz a procedência do número, e é isso que o separa do `DeliveredQuantity` ao lado:
  um é o ticket do transportador, o outro é o relatório da trading. Nenhum dos dois manda no outro.
- `SHIPMENT_LOADS.DischargedQuantity` DECIMAL(18,3) DEFAULT 0 — soma dos tickets da carga, para o
  cabeçalho mostrar descarregado × embarcado. Aqui não há número concorrente, então o nome não
  precisa do prefixo.

Três migrations (tabela de descargas, tabela de anexos, as duas colunas), aplicadas com
`dotnet ef database update` e `ASPNETCORE_ENVIRONMENT` **explícito**. Sem índice filtrado, logo
sem a armadilha do `QUOTED_IDENTIFIER`.

### Escritor único

`ShipmentLoadDischargesRecalculateService` é o **único** lugar que escreve as duas quantidades
derivadas. Recebe as chaves de item afetadas e, para cada uma:

```
item.TicketDeliveredQuantity = Σ DischargedQuantity dos tickets daquele item
```

Depois recalcula `load.DischargedQuantity`. É tudo. **Sem condicional, sem estado, sem leitura de
`DeliveryStatus`** — e sem chamar `SalesContractsRecalculateBalanceService` ou
`SalesShipmentReleasesRecalculateShippedService`, porque não há fator efetivo envolvido.

O serviço **nunca escreve em `DeliveredQuantity`, `QuantityLoss` ou `DeliveryStatus`**. Essa é a
linha divisória entre as duas operações, e é o que faz uma não travar a outra: a Logística lança
ticket a qualquer momento, inclusive depois de a entrega estar encerrada, e o número do Nauhan não
se mexe. O ganho colateral é que desaparecem três problemas que as versões anteriores deste desenho
tinham: sobrescrita de valor digitado, ordem de operação a memorizar e guard de entrega encerrada.

### Guards (mensagens de negócio em pt-BR)

- Carga `Cancelled` ou `Returned` recusa criar, alterar **e excluir** ticket: a carga está
  congelada e as três operações mexem em quantidade. Diferente do comentário da carga, que é
  editável até em carga cancelada justamente porque não move número nenhum.
- Nota cancelada não aceita registro novo.
- O item informado tem de pertencer à **nota informada** e essa nota, à **esta carga** — as duas
  validações no servidor, porque a tabela guarda as duas chaves e nada impede a tela de mandar um
  par inconsistente.
- `DischargedQuantity` > 0.
- Excluir um anexo ainda referenciado por um ticket (`AttachmentKey`) é **barrado** com mensagem
  pedindo que se exclua o registro de descarga primeiro — senão a FK estoura com erro 547 e o
  usuário vê um 500 sem explicação.

**Não** existe guard de entrega encerrada, e isso é intencional (decisão 6). O que protege o número
do Nauhan não é uma recusa: é o fato de o escritor único não conhecer o campo dele. Barrar seria
pior — deixaria a Logística sem onde lançar o ticket que tem em mãos, que é justamente o documento
de que o financeiro precisa para liberar o frete.

### Integrações obrigatórias

- `ShipmentLoadsDeleteService` remove as filhas à mão (linhas 67–89: movements, comments,
  changeLogs). As duas tabelas novas **entram ali** — senão o delete do pai quebra com erro 547 e
  o teste InMemory passa verde. **Ordem importa**: descargas primeiro, anexos depois, porque a
  descarga referencia o anexo.
- Cancelar nota que já tem ticket continua permitido e **não** mexe nos tickets: eles são
  evidência do físico. O `DeliveredQuantity` também já não é tocado pelo cancelamento hoje, e
  este chamado não muda isso.

### OData

- Leitura: `EntitySet` `ShipmentLoadsDischarges` (precedente exato de `ShipmentLoadsComments`).
- Escrita: actions `ShipmentLoadsDischargeCreate`, `ShipmentLoadsDischargeUpdate`,
  `ShipmentLoadsDischargeDelete`.
- Anexos, no molde da Conferência de Armazém: `Action ShipmentLoadsAttachmentUpload` (base64),
  `Function ShipmentLoadsAttachmentsList`, `Function ShipmentLoadsAttachmentsDownload`. Bytes
  nunca viajam na leitura normal.
- As duas propriedades derivadas são colunas mapeadas, não `[NotMapped]`, então entram no EDM sem
  `AddProperty`.
- ⚠️ `ODataActionParameters` chega **nulo** quando falta parâmetro no EDM — checar antes do
  primeiro `TryGetValue`, senão o NRE vira 500 de corpo vazio. Parâmetro string do EDM é
  anulável: `TryGetValue` devolve `true` com `null` e `.ToString()` estoura.

### Log

Inclusão, alteração e exclusão de ticket geram linha em `ShipmentLoadChangeLog` (código novo em
`ShipmentLoadChangeLogFields`) — é dado digitado. A **Movimentação** da carga não recebe nada: o
saldo da carga não muda.

### Frontend

Nenhuma rota nova, logo **nenhuma migration de `MENU_ITEMS`/`ROLE_MENUS`**.

**Aba "Descargas"** em `view/shipmentLoads/Detail.view.xml`, nova `ObjectPageSection` depois de
"Documentos de Saída". `sap.ui.table.Table` com `$$ownRequest` — sem ele a coleção vira `$expand`
do pai e o `refresh()` depois de gravar não funciona (a aba Romaneios já documenta isso no
próprio XML). Colunas: Ticket, Data da Descarga, Nota, Produto, Contrato, Peso Descarregado,
Anexo (ícone que baixa), Observação, Registrado por. Toolbar Registrar / Editar / Excluir,
escondida em carga `Cancelled` ou `Returned`.

**`ShipmentLoadDischargeDialog.fragment.xml`**: Nº do Ticket, Data, Peso, Nota, Item, Observação,
`FileUploader`.

- ⚠️ As listas de Nota e de Item vão para um **`JSONModel` estático carregado ANTES de abrir o
  diálogo**. `Select` com `selectedKey` sobre coleção OData renderiza vazio com o estado interno
  correto — foi o que aconteceu no diálogo de NF.
- ⚠️ O peso usa `sap.ui.model.odata.type.Double`, nunca `Decimal`: `Decimal` serializa como
  string e o leitor OData devolve 400 sem nomear o campo.
- Escolher a nota filtra os itens **em memória**. Com um item só, já vem selecionado.
- O anexo informado é gravado como `ShipmentLoadAttachment` tipo `DischargeTicket` e a chave volta
  no `AttachmentKey` do ticket, numa única ida ao servidor, dentro da transação do serviço de
  criação.

**Aba "Anexos"**: grid com Tipo, Descrição, Arquivo, Data, Usuário; Anexar / Baixar / Excluir.
Reaproveita `warehouseReconciliations/fragments/AttachmentUploadDialog.fragment.xml` e o
`BaseController` (linhas 381–455): base64 por `runAction`, download por `fetch` no endpoint da
Function. As três rotas novas entram em `model/ServerRoutes.ts`. O diálogo ganha o campo Tipo,
que o original não tem.

**Duas colunas na Conferência de Entregas** (`view/salesInvoices/reconciliation/Main.view.xml`):

- **"Qtd. Ticket"**, somente leitura, lendo `TicketDeliveredQuantity`, ao lado de "Qtd.Entregue".
- **"Diferença Ticket"**, calculada na tela: `TicketDeliveredQuantity − DeliveredQuantity`,
  exibida **somente quando os dois forem > 0** (decisão 5). Com um dos lados ausente a célula fica
  **vazia**, não zero: vazio diz "ainda não há confronto", e zero diria "confere", que é afirmação
  diferente. `ObjectStatus` com `state` por formatter para destacar a divergência.

`DeliveredQuantity` **segue editável e é o único que o Nauhan digita**. `TicketDeliveredQuantity`
entra no `$select`, e as partes do formatter levam `targetType: 'any'` — as duas são
`Edm.Decimal`, e sem isso o formatter recebe string e a comparação `> 0` passa a mentir.

**Cabeçalho da carga** (`fragments/LoadCard.fragment.xml`): "Descarregado" e a diferença contra o
peso embarcado.

## Arquivos

**Backend, novos:** `SiagroB1.Domain/Entities/ShipmentLoadDischarge.cs`,
`ShipmentLoadAttachment.cs`, `SiagroB1.Domain/Enums/ShipmentLoadAttachmentType.cs`;
`SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadDischarges{Create,Update,Delete,Get,Recalculate}Service.cs`
e `ShipmentLoadAttachments{Create,Get,Delete}Service.cs`;
`SiagroB1.Web/Actions/ShipmentLoads/` (3 actions de descarga + upload),
`SiagroB1.Web/Functions/ShipmentLoads/` (list + download),
`SiagroB1.Web/Controllers/ShipmentLoadsDischargesController.cs`; 3 migrations.

**Backend, alterados:** `SiagroB1.Domain/Entities/ShipmentLoad.cs` (coleções +
`DischargedQuantity`), `SalesInvoiceItem.cs` (`TicketDeliveredQuantity`),
`ShipmentLoadChangeLogFields.cs`, `SiagroB1.Infra/Context/AppDbContext.cs`,
`ShipmentLoadsDeleteService.cs`, `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`.

**Frontend, novos:** `view/shipmentLoads/fragments/ShipmentLoadDischarges.fragment.xml`,
`ShipmentLoadDischargeDialog.fragment.xml`, `ShipmentLoadAttachments.fragment.xml`,
`ShipmentLoadAttachmentUploadDialog.fragment.xml`.

**Frontend, alterados:** `view/shipmentLoads/Detail.view.xml`,
`controller/shipmentLoads/Detail.controller.ts` (ou `BaseController.ts`),
`view/shipmentLoads/fragments/LoadCard.fragment.xml`,
`view/salesInvoices/reconciliation/Main.view.xml`,
`controller/salesInvoices/reconciliation/Main.controller.ts`, `model/ServerRoutes.ts`,
`model/formatter.ts` (formatter da divergência, ao lado de `formatDeliveryDifference`, linha 215).

## Testes

TDD, em `SiagroB1.Application.Tests/ShipmentLoads` (xUnit + EF InMemory):

- Recálculo: soma de N tickets em `item.TicketDeliveredQuantity`; soma da carga em
  `load.DischargedQuantity`.
- **O teste que guarda a regra central**, repetido para entrega Aberta **e** Encerrada:
  registrar/alterar/excluir ticket deixa `DeliveredQuantity`, `QuantityLoss` e `DeliveryStatus`
  **inalterados**, e nem saldo de contrato nem de liberação se movem.
- Guards: carga cancelada/devolvida barrando as três operações, nota cancelada, par
  nota/item inconsistente, nota de outra carga, peso zero/negativo, exclusão de anexo vinculado a
  ticket.
- Excluir ticket devolve as somas ao valor anterior; excluir o último zera
  `TicketDeliveredQuantity` e **não** toca no conferido.
- `ShipmentLoadsDeleteService` apaga descargas e anexos. ⚠️ O InMemory **não** prova o erro 547 —
  o delete também será exercitado contra o banco real na verificação.
- Anexos: criar com tipo, listar, baixar, excluir; ticket com anexo vinculado.
- EDM (precedente `ShipmentLoadEdmModelTests`): actions, functions e as duas propriedades novas
  presentes no modelo.

## Verificação planejada

Pelo caminho do usuário, não por `curl`: Web + Gateway no profile `yktb`, `yarn start:dev`, login
`admin`, e o percurso: carga → registrar descarga com anexo → ver "Qtd. Ticket" preenchida na
Conferência de Entregas com a coluna "Diferença Ticket" **vazia** (só um lado) → digitar o peso do
relatório da trading e ver a diferença **aparecer** → encerrar a entrega e confirmar que **só
então** o saldo do contrato se move → registrar um **segundo ticket com a entrega já encerrada** e
confirmar que o conferido não muda e que a diferença se atualiza.

`yarn test` do frontend não passa neste repo (gate de cobertura de 50% contra ~2,4% reais), então
a checagem de frontend é `yarn ts-typecheck` + `yarn lint` + a navegação no navegador.
