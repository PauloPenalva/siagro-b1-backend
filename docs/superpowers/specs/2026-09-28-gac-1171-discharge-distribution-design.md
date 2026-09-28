# GAC-1171 (rateio) — Ticket de descarga rateado entre os documentos de saída + "Descarregada" automática

**Data:** 2026-09-28
**Chamado:** GAC-1171, terceira rodada de pedidos do usuário. Specs anteriores:
`2026-09-17-gac-1171-discharge-tickets-design.md` (registro de descargas) e
`2026-09-23-gac-1171-discharged-status-and-attachment-viewer-design.md` (status Descarregada/Concluída).
**Branch:** `feature/gac-1171-discharge-distribution`, nos worktrees `siagro-b1-backend-gac-1171` e
`siagro-b1-frontend-gac-1171`, criados a partir de `main` (`dae39fb` / `d958b3f`).

## 1. O pedido

> "A dinâmica de registro de peso de descarga, ao invés de ser selecionando no dropdown o documento de
> saída, não poderia ser um grid com os documentos de saída do Tipo=Normal e Status=Confirmada, para o
> usuário distribuir o peso em cada documento de saída, sendo que por sugestão, o peso informado no
> campo 'Peso Descarregado' já vai dividido proporcionalmente entre os documentos de saída, só para o
> usuário confirmar? Isso porque a maioria dos casos vai ser dessa maneira. Aí quando todos os
> documentos de saída Tipo=Normal e Status=Confirmada tiverem um peso de descarga registrado, a carga
> poderia mudar automaticamente para o status 'Descarregada', ao invés de ser marcada manualmente
> através do comando 'Marcar como Descarregada'."
>
> "Obs.: fizemos um teste retornando parcialmente um documento de saída através do fluxo 'Registrar
> Recusa' da carga, o documento continuou com status 'Confirmada', não sei se seria conveniente criar
> um status 'Retornada parcialmente'."

**Contexto físico.** A carga é UM caminhão (a placa é trava da carga, ver `ShipmentLoad`), mas pode ter
vários documentos de saída. O ticket da balança do destino pesa o caminhão inteiro. Hoje cada registro
de descarga aponta UMA linha de nota, então numa carga com 3 notas o usuário lança o mesmo ticket 3
vezes e faz o rateio de cabeça.

### Decisões do usuário (fechadas no brainstorming)

| # | Decisão |
|---|---|
| D1 | O registro do ticket ganha um **grid de rateio** com as linhas das notas **Normal + Confirmada** da carga. O peso digitado já vem **rateado proporcionalmente à quantidade líquida** (faturado − devolvido) de cada linha. |
| D2 | **A soma do rateio tem de fechar com o peso do ticket** (tela e servidor). Linha com zero é permitida. O resíduo de arredondamento da sugestão vai para a última linha. |
| D3 | Modelo **cabeçalho + linhas**: o ticket é o cabeçalho, o rateio é uma tabela filha. |
| D4 | "Descarregada" passa a ser **automática**: vale quando todas as linhas das notas Normal + Confirmada (com líquido > 0) têm peso de ticket. Os botões **"Marcar como Descarregada" e "Desfazer Descarregada" saem**; desfazer = excluir o ticket. |
| D5 | Devolução parcial: **indicador, sem status novo**. A nota continua "Confirmada"; ganha `ReturnedQuantity` persistido e um selo "Dev. parcial" nas telas. |
| D6 | **Carga mista** (recusa parcial com volta ao armazém, status "Devolvida") **aceita ticket** da parte entregue. O status continua "Devolvida". |

Continuam valendo, das rodadas anteriores:

- **O ticket nunca toca o peso conferido** (`DeliveredQuantity`, `QuantityLoss`, `DeliveryStatus`) nem
  dispara recálculo de contrato/liberação. A Conferência de Entregas segue soberana.
- **Concluída vence Descarregada** (`ResolveClosure`), e "Concluída" continua vindo da Conferência.
- Descarregada/Concluída só refinam o ramo **Faturada**.

### Fora de escopo

- Status novo em `InvoiceStatus` (recusado em D5: o enum é compartilhado com o Documento de Entrada e
  `Confirmed` é testado em ~20 pontos do backend, fora testes).
- Filtro "Dev. parcial" na lista de documentos de saída.
- Backfill de status das cargas já com ticket em todas as notas (ver §7).

## 2. Modelo de dados

### 2.1 Ticket = cabeçalho (`SHIPMENT_LOAD_DISCHARGES`, `ShipmentLoadDischarge`)

Fica: `Key`, `ShipmentLoadKey`, `TicketNumber`, `DischargeDate`, `DischargedQuantity` (**o peso do
papel**, total do ticket), `Comments`, `AttachmentKey`, `CreatedAt/By`, `UpdatedAt/By`.

Sai: `SalesInvoiceKey`, `SalesInvoiceItemKey` e as navegações `SalesInvoice`/`SalesInvoiceItem` (descem
para as linhas).

Entra: navegação `Items` (`ICollection<ShipmentLoadDischargeItem>`).

### 2.2 Rateio (`SHIPMENT_LOAD_DISCHARGE_ITEMS`, `ShipmentLoadDischargeItem`) — nova

| Coluna | Tipo | Observação |
|---|---|---|
| `Key` | `Guid` (identity) | |
| `DischargeKey` | `Guid` NOT NULL | FK → ticket, **`DeleteBehavior.Cascade`** (excluir o ticket leva o rateio) |
| `SalesInvoiceKey` | `Guid` NOT NULL | FK → nota, `NoAction` (sustenta a trava de exclusão da nota) |
| `SalesInvoiceItemKey` | `Guid` NOT NULL | FK → linha da nota, `NoAction` (idem) |
| `Quantity` | `DECIMAL(18,3)` | parcela do peso do ticket; sempre > 0 (zero não é gravado) |

Índices: `DischargeKey`, `SalesInvoiceItemKey`. Único por `(DischargeKey, SalesInvoiceItemKey)`: um
ticket não rateia duas vezes a mesma linha.

A nota fica na linha pelo mesmo motivo que ficava no ticket: o grid chega ao número dela com `$expand`
de um nível. O par nota/linha é **resolvido no servidor a partir da linha** (§3.1), nunca aceito da tela.

EDM: `EntitySet<ShipmentLoadDischargeItem>("ShipmentLoadsDischargesItems")`, só leitura (sem
controller de escrita), para o `$expand=Items(...)` do grid de tickets resolver.

### 2.3 `SHIPMENT_LOADS.IsDischarged` — sai

A "Descarregada" deixa de ter marca: é derivada (§4).

### 2.4 Quantidade devolvida — persistida e derivada

- `SALES_INVOICES_ITEMS.ReturnedQuantity` `DECIMAL(18,3) DEFAULT 0`: Σ `Quantity` das linhas de
  devoluções (`InvoiceType = Return`) **confirmadas** com `SalesInvoiceItemOriginKey` = esta linha.
- `SALES_INVOICES.ReturnedQuantity` `DECIMAL(18,3) DEFAULT 0`: Σ das linhas acima, na nota de origem.
  Existe para a lista de documentos de saída (uma coleção não se binda em célula de linha).
- "Confirmada" é o mesmo critério do saldo da carga (`CalculateInvoicedAsync`): o projeto considera que
  a devolução ocorreu na confirmação.
- **Escritor único:** `SalesInvoicesRecalculateReturnedService` (estático, sem `SaveChanges`, compõe na
  transação do chamador), recebendo a nota de ORIGEM. Chamado:
  - em `SalesInvoicesConfirmService.ProcessReturnInvoiceAsync` (a Recusa de carga passa por aqui);
  - nos três ramos de estorno de devolução de `SalesInvoicesReverseConfirmService`
    (`ReverseNewReturnAsync`, `ReverseLoadReturnAsync`, `ReverseLegacyReturnAsync`).
  - Cancelar/excluir só alcançam devolução NÃO confirmada (`SalesInvoicesCancelService:30` recusa
    retorno confirmado), que não entra na soma — não precisam chamar.
- A soma lê banco + rastreador (mesma técnica de `ShipmentLoadDischargesRecalculateService.SumAsync`),
  porque a devolução que acabou de mudar de status ainda não foi gravada.

### 2.5 Quantidade líquida

`RemainingQuantity = Quantity − ReturnedQuantity` ("faturado que não voltou"), **só em memória**: um
helper estático no backend (`ShipmentLoadDischargeRules`) e o helper do frontend (§5.2). Na tela o
rótulo é "Qtd. Líquida".

⚠️ O nome não pode ser `NetQuantity`: `SalesInvoiceItem.NetQuantity` JÁ EXISTE e é outra coisa
(`DeliveredQuantity − QuantityLoss`, da Conferência).

## 3. Backend — escrita do ticket

### 3.1 Actions (EDM)

| Action | Parâmetros | Mudança |
|---|---|---|
| `ShipmentLoadsDischargeCreate` | `LoadKey`, `TicketNumber`, `DischargeDate`, `Quantity`, `Comments`, `File`/`FileName`/`ContentType` (opcionais), **`SalesInvoiceItemKeys: Collection(Edm.Guid)`**, **`Quantities: Collection(Edm.Double)`** | saem `SalesInvoiceKey` e `SalesInvoiceItemKey` |
| `ShipmentLoadsDischargeUpdate` | `Key`, `TicketNumber`, `DischargeDate`, `Quantity`, `Comments`, **`SalesInvoiceItemKeys`**, **`Quantities`** | passa a receber o rateio e **substitui** as linhas |
| `ShipmentLoadsDischargeDelete` | `Key` | sem mudança de assinatura |
| `ShipmentLoadsMarkDischarged` / `ShipmentLoadsUndoDischarged` | — | **removidas** (controller, serviço, EDM, DI, testes) |

Coleções paralelas `Guid` + `double`: precedente provado em `ShipmentLoadsRefuse` e
`WarehouseReconciliationsDistributeLoss`. Coleção de tipo complexo chegaria como
`EdmComplexObjectCollection`, que os helpers do projeto não leem. `double`, nunca `Decimal`
(`Edm.Decimal` vira string e o 400 não nomeia o campo).

⚠️ `ODataActionParameters` chega NULO quando nada é enviado, e o `TryGetValue` de anulável devolve true
com valor nulo — manter as duas checagens do controller atual.

O anexo continua gravado ANTES do ticket e só na inclusão (anexo órfão aceito, como na rodada 1).

### 3.2 Validação (`ShipmentLoadDischargeRules`)

Na ordem, com mensagens em pt-BR:

1. `TicketNumber` obrigatório (como hoje); `Quantity` (total) > 0 (como hoje).
2. `SalesInvoiceItemKeys` e `Quantities` com o mesmo tamanho; nenhum item repetido; nenhuma parcela
   negativa. Parcelas zero são **descartadas** antes das demais regras.
3. Ao menos uma parcela > 0: "Distribua o peso descarregado entre os documentos de saída."
4. **Fechamento:** `|Σ parcelas − Quantity| <= 0,001`, senão "O rateio (X) não fecha com o peso
   descarregado (Y)." (valores em pt-BR, N3).
5. **Elegibilidade de cada linha**, resolvida no servidor a partir de `SalesInvoiceItemKey`:
   nota com `ShipmentLoadKey` = carga, `InvoiceType = Normal`, `InvoiceStatus = Confirmed`, e
   `RemainingQuantity > 0,001`. Mensagem nomeia o documento.
6. **Sem teto por linha.** A balança do destino pode pesar mais que o faturado.

**Carga:** `EnsureLoadAcceptsChanges` passa a recusar **só `Cancelled`**, nas três operações.
- Criar/alterar: quem barra a carga sem nada entregue (Planejada, Aberta, Devolvida por inteiro) é a
  elegibilidade das linhas. É isso que libera a carga mista "Devolvida" (D6).
- Excluir: vale inclusive em "Devolvida", para limpar ticket lançado antes de uma recusa total.

**Alterar** substitui o rateio inteiro: remove as linhas antigas e grava as novas. Linha antiga cuja nota
deixou de ser elegível (cancelada depois, por exemplo) simplesmente não volta — a tela nem a oferece.

### 3.3 Somas (`ShipmentLoadDischargesRecalculateService`, escritor único)

- `SalesInvoiceItem.TicketDeliveredQuantity` = Σ `ShipmentLoadDischargeItem.Quantity` da linha.
- `ShipmentLoad.DischargedQuantity` = Σ `ShipmentLoadDischarge.DischargedQuantity` da carga.
- Mantém a técnica banco + rastreador, com `trackedKeys` incluindo `Deleted` e a lista somada excluindo
  (a assimetria documentada em `<remarks>`), agora para as DUAS entidades.
- Chaves recalculadas = linhas ANTIGAS ∪ linhas NOVAS (alterar pode tirar peso de uma linha que saiu do
  rateio). Na exclusão, as linhas do ticket são lidas ANTES do `Remove` (o cascade do EF não devolve as
  chaves).
- Continua sem `SaveChanges` e sem tocar no conferido.

### 3.4 Log

`ShipmentLoadChangeLogFields.DescribeDischarge` passa a receber o rateio:
`"123 — 35.000,000 (000100: 20.000,000; 000101: 15.000,000)"`. Nota ainda sem número aparece como
"(sem número)". Truncar ao tamanho da coluna do log.

## 4. Backend — status "Descarregada" automático

### 4.1 Regra

Em `ShipmentLoadsRecalculateInvoicedService`, a leitura rastreada de `AreAllDeliveriesClosedAsync` (notas
Normais com `Include(Items)`, filtro de status EM MEMÓRIA) passa a devolver as duas respostas:

```text
allDeliveriesClosed  (como hoje)
allDischarged:
    nenhuma nota Normal viva em Pending
    E existe ao menos uma linha em nota Normal Confirmed com RemainingQuantity > 0,001
    E todas essas linhas têm TicketDeliveredQuantity > 0,001
```

```text
ResolveClosure(baseStatus, allDischarged, allDeliveriesClosed):
    se baseStatus != Invoiced   → baseStatus        (Devolvida continua Devolvida — D6)
    se allDeliveriesClosed      → Completed         ("Concluída")
    se allDischarged            → Discharged        ("Descarregada")
    senão                       → Invoiced          ("Faturada")
```

Sai o `load.IsDischarged = false` do ramo `baseStatus != Invoiced`. A consulta só roda quando
`baseStatus == Invoiced`, como hoje.

A leitura precisa ser RASTREADA: o ticket, as somas e o status entram no mesmo `SaveChanges`, então o
`TicketDeliveredQuantity` que acabou de mudar só existe no rastreador. O EF não sobrescreve os valores de
uma entidade já rastreada numa consulta rastreada.

### 4.2 Quem recalcula

- **Escritas de ticket** (criar/alterar/excluir): depois das somas, chamam o gancho
  `ShipmentLoadsClosureHookService`, com uma sobrecarga nova **por carga** (`ApplyAsync(Guid loadKey,
  string userName)`). Ele recalcula e grava "Situação: Faturada → Descarregada" no log, assinada por quem
  registrou. **Sem movimento**: o saldo não muda. `ShipmentLoadMovementType.Discharged` fica no enum
  (linhas antigas).
- **Mudanças de nota** (confirmar, estornar, cancelar, excluir, refaturar, recusa): já recalculam a carga
  hoje; a regra nova vem junto sem chamada adicional.
- ⚠️ **Ordem na Recusa:** `ReturnedQuantity` é gravado dentro da confirmação da devolução, ANTES do
  recálculo da carga, e a leitura rastreada o enxerga.

### 4.3 Tabela das operações

| Operação | Efeito na "Descarregada" |
|---|---|
| Registrar ticket que completa as linhas | Faturada → Descarregada (log com o usuário) |
| Alterar ticket tirando uma linha / excluir ticket | Descarregada → Faturada |
| Excluir ticket de carga Concluída | continua Concluída (Concluída vence) |
| Faturar nota nova na carga (refaturamento "segue viagem") | a nota nova sem ticket devolve a carga a Faturada |
| Estornar confirmação de nota Normal | nota Pending → a carga sai de Descarregada |
| Recusa parcial "segue viagem" | a carga sai de Faturada (Faturada Parcial) |
| Recusa com volta ao armazém | Devolvida (rótulo da Recusa prevalece); ticket continua aceito (D6) |
| Encerrar toda a Conferência | Concluída |

### 4.4 Travas que citavam o botão removido

| Local | Mensagem nova |
|---|---|
| `ShipmentLoadsRefuseService.Validate` (`:422`) | "A carga X já foi descarregada no destino. Exclua o ticket de descarga antes de registrar recusa." |
| `ShipmentLoadTransshipmentRules.EnsureLoadAcceptsTransshipment` (`:64`) | "… Exclua o ticket de descarga antes de iniciar o transbordo." |
| `ShippingTransactionsChangeReleaseService` (`:207`) | "… Exclua o ticket de descarga antes de trocar a liberação." |

Comentários e XML-docs que citam "Desfazer Descarregada", `IsDischarged` ou "marca manual"
(`ShipmentLoad`, `ShipmentLoadStatus`, `ShipmentLoadCompletionRules`, `ShipmentLoadsRecalculateInvoicedService`)
são atualizados. `ShipmentLoadsUpdateService` (trava de campos fiscais) não muda.

## 5. Frontend

### 5.1 Diálogo "Registrar / Editar Descarga" (`ShipmentLoadDischargeDialog.fragment.xml`)

Continua escrevendo num buffer JSON (`viewModel>/dischargeDialog`), nunca no contexto OData. ~60rem.

- **Formulário:** Nº do Ticket, Data da Descarga, **Peso Descarregado** (total), Observação, Ticket
  digitalizado (só na inclusão, com `.clear()` do FileUploader a cada abertura, como hoje).
- **Grid de rateio** (`sap.m.Table` sobre `viewModel>/dischargeDialog/lines`): Documento, Contrato,
  Produto, Qtd. Faturada, Qtd. Devolvida, Qtd. Líquida, Já descarregado (outros tickets), **Peso
  Rateado** (`Input`, `sap.ui.model.type.Float`, 3 casas, mínimo 0).
- **Faixa de fechamento:** `MessageStrip` "Rateado X de Y" — Success quando fecha, Warning com "Falta
  distribuir Z" (ou "Excede em Z") quando não. Mesmo padrão de `LossDistribution.fragment.xml`.
- **Comportamento:**
  - `change` do Peso Descarregado → rateia de novo, proporcional à Qtd. Líquida (sobrescreve o grid).
  - `change` de uma linha → só atualiza a faixa.
  - Botão "Ratear proporcionalmente" na barra do grid → refaz a sugestão.
  - Uma linha só → recebe o total inteiro (cai naturalmente do algoritmo).
  - Gravar recusa (alerta) enquanto não fecha; o servidor confere de novo.
  - Arredonda cada parcela para 3 casas e descarta zeros ANTES de enviar (como `saveDistribution`).
- **Carga das linhas:** o `bindList` de `/ShipmentLoads(...)/Invoices` que já existe, agora com
  `$select=Key,InvoiceNumber,InvoiceType,InvoiceStatus` e
  `$expand=Items($select=Key,ItemCode,ItemName,Quantity,ReturnedQuantity,TicketDeliveredQuantity;$expand=SalesContract($select=Key,Code))`.
  Filtra em memória: Normal + Confirmed + líquido > 0,001. `Edm.Decimal` chega como STRING → `Number()`.
- **Edição:** as linhas vêm preenchidas com o rateio gravado (lido do `$expand=Items` do ticket); "Já
  descarregado" desconta a parcela do próprio ticket. O total e as parcelas continuam editáveis.
- **Sem linha elegível:** o diálogo não abre; a mensagem distingue "sem documento de saída", "todos
  cancelados", "nenhum confirmado" e "todos devolvidos".
- Trava de duplo clique (`_dischargeInFlight`) e "fecha só depois do resolve" continuam.

### 5.2 Helper puro (`webapp/helpers/DischargeDistributionHelpers.ts`) + QUnit

- `distributeProportionally(total, bases): number[]` — parcelas em 3 casas, proporcionais às bases;
  o resíduo vai para a **última linha com base > 0**; base total zero → tudo zero.
- `summarizeDistribution(total, quantities)` → `{ distributed, remaining, closed }`, tolerância 0,001.
- Testes: 2 linhas iguais; bases desiguais com dízima (resíduo na última); uma linha; base zero no meio;
  total zero; fechamento dentro e fora da tolerância.

### 5.3 Aba Descargas (`ShipmentLoadDischarges.fragment.xml`)

Uma linha por **ticket**: Ticket, Data, Peso Descarregado, **Documentos**, Anexo, Observação,
Registrado por.

- `rows`: `$select=Key,TicketNumber,DischargeDate,DischargedQuantity,AttachmentKey,Comments,CreatedBy`,
  `$expand=Items($select=Key,Quantity,SalesInvoiceItemKey;$expand=SalesInvoice($select=Key,InvoiceNumber))`
  (profundidade 2, cabe no `MaxExpansionDepth` padrão). `$$ownRequest` e `sorter` continuam obrigatórios.
- Coluna Documentos: um `HBox` com `items="{ path: 'Items', templateShareable: false }"` e um `Text`
  por parcela (`formatter.formatDischargeShare`), mostrando `000100 (20.000,000)  000101 (15.000,000)`.
  É uma lista RELATIVA à linha, que lê o `$expand=Items` já em cache, sem requisição própria.
  Troca feita na escrita do plano: a alternativa (`{ path: 'Items', mode: 'OneTime', targetType:
  'any' }`) exige `OneTime` para coleção de entidade, e `OneTime` desliga o binding depois da primeira
  leitura — não acompanharia a reciclagem de linhas da `sap.ui.table`.
- ⚠️ Provar no navegador que a coluna acompanha a rolagem e o `refresh()`. **Plano B:**
  mestre-detalhe — segundo grid com o rateio do ticket selecionado.
- Registrar/Editar/Excluir visíveis em todo status **exceto Cancelada** (Devolvida entra).
- Some a coluna Contrato (o contrato fica no diálogo; na aba exigiria `$expand` de 3 níveis).

### 5.4 Cabeçalho da carga (`shipmentLoads/Detail`)

Saem "Marcar como Descarregada" e "Desfazer Descarregada" (`Detail.view.xml:52-64`) e os handlers
`onMarkDischarged`/`onUndoDischarged` (`Detail.controller.ts:159-170`).

### 5.5 Indicador "Dev. parcial" (D5)

Aparece com `InvoiceStatus = Confirmed` e `ReturnedQuantity > 0` (via formatter com `Number()`, nunca
comparação crua de string):

- **Lista de documentos de saída** (`salesInvoices/Main.view.xml:133`): segundo `ObjectStatus`
  "Dev. parcial" (Warning) ao lado do status; a coluna alarga.
- **Detalhe do documento** (`salesInvoices/Detail.view.xml:41`): o mesmo selo no cabeçalho, com a
  quantidade devolvida; coluna "Qtd. Devolvida" nos itens.
- **Aba Documentos de Saída da carga** (`shipmentLoads/Detail.view.xml:358`): selo no status e coluna
  "Qtd. Devolvida" ao lado de "Qtde".

## 6. Testes

### Backend (xUnit + EF InMemory, TDD)

- **Regras** (`ShipmentLoadDischargeRules`): fechamento dentro/fora da tolerância; parcela negativa;
  item repetido; tamanhos diferentes; tudo zero; linha de outra carga; nota Pending/Cancelled/Returned;
  nota de devolução; linha sem líquido; carga Cancelada nas três operações; carga mista Devolvida aceita
  criar/excluir.
- **Criar/alterar/excluir:** linhas gravadas; somas por linha e por carga; alterar tirando uma linha zera
  o `TicketDeliveredQuantity` dela; excluir leva as linhas (cascade) e zera as somas; log com o rateio.
- **Status:** registrar o último ticket → Descarregada + log "Situação"; excluir → Faturada; Concluída
  vence; nota Pending impede; nota nova sem ticket devolve a Faturada; Devolvida continua Devolvida.
- **`ReturnedQuantity`:** recusa parcial grava na linha e na nota; recusa total; estorno volta a zero
  (nos três ramos); devolução pendente não conta.
- **Travas:** as três mensagens novas.
- **EDM:** as coleções das actions; `ShipmentLoadsDischargesItems`; `ReturnedQuantity` no EDM; as
  actions Mark/Undo ausentes.
- **Guards de exclusão de nota/linha** (`SalesInvoicesDeleteService`, `SalesInvoicesItemsDeleteService`)
  consultando a tabela de rateio.
- Remoção dos testes do Mark/Undo e ajuste dos que semeiam `IsDischarged`.

### Frontend

QUnit do helper (§5.2), `yarn ts-typecheck`, `yarn lint`, `yarn ui5lint`. (`yarn test` inteiro não é
critério: o gate de cobertura nunca passa neste repo.)

### Navegador (stack local Yokotobi, `yktb` + `yarn start:dev`)

1. Carga com 2 notas Normais confirmadas: diálogo sugere o rateio proporcional; ajustar uma linha mostra
   "Falta distribuir"; gravar fechado → carga **Descarregada** sozinha, log "Situação".
2. Excluir o ticket → volta a Faturada.
3. Aba Descargas mostra a coluna Documentos, inclusive depois de gravar/excluir (prova da lista
   aninhada, ou aciona o plano B).
4. Carga mista Devolvida aceita ticket; status continua Devolvida.
5. Selo "Dev. parcial" nas três telas depois de uma recusa parcial.
6. Anexo junto com o ticket continua funcionando (visualizador).

## 7. Migration e implantação

Uma migration (`AddShipmentLoadDischargeItemsAndReturnedQuantity`), commit com `DB:`. `Up()`, nesta ordem:

1. Cria `SHIPMENT_LOAD_DISCHARGE_ITEMS`.
2. `INSERT` de uma linha por ticket existente (`DischargeKey`, `SalesInvoiceKey`, `SalesInvoiceItemKey`,
   `Quantity = DischargedQuantity`) — nenhum dado se perde.
3. Remove FKs, índices e as colunas `SalesInvoiceKey`/`SalesInvoiceItemKey` de `SHIPMENT_LOAD_DISCHARGES`.
4. `UPDATE SHIPMENT_LOADS SET Status = 2 WHERE Status = 8` (Descarregada manual → Faturada) e remove
   `IsDischarged`.
5. Cria `ReturnedQuantity` nas duas tabelas de nota e preenche com `UPDATE` a partir das devoluções
   confirmadas (`InvoiceType = 1`, `InvoiceStatus = 1` — enums gravados como `int`).

`Down()`: recria as colunas do ticket copiando a PRIMEIRA linha do rateio (**com perda** quando o ticket
tem mais de uma linha — documentado no método), recria `IsDischarged` (0), remove a tabela e as colunas.

**Sem backfill de status em C#** (mesma decisão de 23/09): carga que já tenha ticket em todas as notas
só vira Descarregada no próximo recálculo — "Recalcular Saldo" ou a próxima escrita de ticket. Anotar no
deploy.

## 8. Riscos

- Lista aninhada na célula da `sap.ui.table` (§5.3) — plano B definido.
- Tolerância 0,001: um teste que "passa" com `total + 0,001` passa pela guarda de propósito; para provar
  a recusa, usar valor claramente fora.
- Recusa parcial com volta ao armazém deixa a carga Devolvida: ela nunca vira Descarregada/Concluída
  (D6, comportamento consciente).
