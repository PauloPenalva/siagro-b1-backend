# Edição pós-aprovação do contrato de VENDA + log de alterações

Data: 2026-07-24

## Origem

Ao restringir o value help de local de entrega da liberação de venda aos locais cadastrados no
contrato, a guarda nova revelou um beco sem saída: contrato **aprovado** não tinha por onde receber
locais de entrega.

- `Detail.view.xml` — botão **Editar** só é visível com `Status === 'Draft'`.
- `SalesContractsUpdateService` — recusa editar contrato fora de Draft.
- `SalesContractsWithdrawApprovalService` — recusa retirar da aprovação contrato que já tenha nota.

Nos dados do ambiente Yokotobi em 24/07/2026: dos **130** contratos aprovados na tela de liberação,
**57 já tinham faturamento** e portanto não conseguiam sequer voltar para Draft. Para esses, a guarda
bloquearia a liberação de entrega permanentemente.

Decisão do usuário: manter a guarda (o fluxo desejado é forçar o cadastro do local antes da
liberação) e **liberar a edição pontual do contrato aprovado** — locais de entrega, anexos e
observação. Todos os demais campos seguem imutáveis. Junto veio o **log de alterações**, exibido no
Detail do contrato — necessidade já levantada antes pelos usuários.

## Escopo

| | Venda |
|---|---|
| Locais de entrega (coleção 1:N `DeliveryLocations`) | ✅ |
| Anexos (incluir/remover) | ✅ |
| Observação (`Comments`) | ❌ revertido a pedido do usuário |

A observação chegou a ser editável depois de aprovada (com action estreita e log próprio), mas o
usuário pediu para voltar ao comportamento anterior: o campo continua em "Dados do Contrato",
editável **apenas em rascunho**, pela tela de edição. Foram removidos a action
`SalesContractsUpdateAfterApproval`, seu service e controller, a seção/fragmento de observação no
Detail e o código `Comments` de `ContractChangeLogFields`. O formatter do frontend ainda traduz esse
código: linhas gravadas antes da reversão continuam no banco e precisam ficar legíveis.

**O espelho para o contrato de COMPRA foi cancelado pelo usuário em 24/07/2026** — chegou a ser
implementado e foi revertido por completo (código, migration e banco). Se for retomado: em compra o
local de entrega é um campo escalar (`DeliveryLocationCode`, um armazém), não uma coleção; o análogo
estrutural da coleção de venda seria `Brokers`, que ficou fora do escopo.

## Log de alterações

Granularidade **campo a campo**: data/hora, usuário, campo, valor anterior, valor novo.

Abrangência:

1. As edições pós-aprovação dos pontos acima (locais de entrega e anexos), **só em venda**.
2. O **ciclo de vida da fixação de preço** — criação, aprovação, rejeição, estorno e exclusão —
   em venda **e em compra**.

Transições de status do próprio contrato (enviar/retirar/aprovar/cancelar) e edições em Draft ficam
fora — já existem os carimbos `CreatedBy/At`, `UpdatedBy/At`, `ApprovedBy/At`, `CanceledBy/At` em
`BaseEntity`.

### Fixação de preço

Descritor compartilhado `ContractChangeLogFields.DescribePriceFixation` →
`"10.000,000 KG @ 2,50 — Em aprovação"`. Volume e preço vão em **todas** as linhas, inclusive nas de
mudança de status: um contrato tem várias fixações, e "Em aprovação → Confirmada" sozinho não diria
qual delas mudou. O texto também sobrevive à exclusão da fixação.

Os services de exclusão (venda e compra) ganharam o parâmetro `deletedBy` — não recebiam o usuário,
e sem ele a linha ficaria sem autor.

No frontend, o refresco do log é disparado dentro de `refreshPriceFixationsList()` nos dois
BaseControllers: toda mutação de fixação já passa por lá, então não depende de lembrar do log em cada
handler. Aprovação e rejeição acontecem na fila da diretoria, fora do Detail — as linhas são gravadas
e aparecem quando o usuário volta ao contrato.

**Compra:** a infraestrutura de log (`PURCHASE_CONTRACTS_CHANGE_LOGS`, services, rota aninhada e
seção no Detail) existe **apenas** para as fixações. A edição pós-aprovação de compra foi cancelada
pelo usuário e não deve ser reintroduzida sem pedido explícito.

### Modelo

```
SALES_CONTRACTS_CHANGE_LOGS
  Key               GUID (identity)
  SalesContractKey  GUID FK
  ChangedAt         DATETIME
  ChangedBy         VARCHAR(100)
  Field             VARCHAR(50)
  OldValue          VARCHAR(500)
  NewValue          VARCHAR(500)
```

`Field` guarda um **código** (`ContractChangeLogFields`: `Comments`, `DeliveryLocation`,
`Attachment`), não o rótulo traduzido — o rótulo em pt-BR sai por
`formatter.formatContractChangeLogField`, para não travar o i18n.

Inclusão de filho grava `OldValue = null`; remoção grava `NewValue = null`. Assim a mesma linha
serve para "de → para" e para "incluído/removido".

## Backend

1. **Entidade** `SalesContractChangeLog` + nav `ChangeLogs` em `SalesContract` + `DbSet`.
   Migration `20260724144218_AddSalesContractChangeLogs`.
2. **`SalesContractsChangeLogService.Register`** — porta única de escrita. Apenas enfileira no
   contexto: o log e a alteração que ele descreve entram no mesmo `SaveChanges`.
3. **`SalesContractsChangeLogsGetService`** — leitura por contrato, `ChangedAt` desc.
4. **`SalesContractsPostApprovalGuard.EnsureEditable`** — `Draft` ou `Approved`, `DefaultException`
   (400) caso contrário. Usada nos 4 pontos de mutação: locais (create/delete) e anexos
   (create/delete). Esses quatro **não tinham guarda de status nenhuma** antes.

`SalesContractsUpdateService` **não** foi relaxado: continua Draft-only protegendo o contrato
inteiro.

### Exposição OData

- `EntitySet<SalesContractChangeLog>("SalesContractsChangeLogs")`
- `GET /odata/SalesContracts({key})/ChangeLogs`

## Frontend

- Flags `ui>/postApprovalEditable` e `ui>/postApprovalSaveVisible` — setadas nas **três** telas
  (Add/Edit/Detail), porque os fragmentos são compartilhados.
- Seções "Locais de Entrega" e "Anexos" no Detail: Incluir/Remover pelo flag novo; a de locais ganhou
  um **Salvar** próprio, porque o update group é diferido e no Detail não há o Salvar da tela.
- Nova seção **"Log de Alterações"**: Data/Hora, Usuário, Campo, De, Para.

### Armadilhas que só apareceram no browser

1. **Two-way binding num Detail envenena o update group.** Enquanto a observação foi editável, o
   `TextArea` ligado a `{Comments}` deixava um PATCH do contrato pendente no `UpdateGroup` diferido;
   o `submitBatch` dos locais o arrastava junto e o servidor recusava — **e o batch inteiro falhava,
   o local nem era criado**. `Context#resetChanges()` não serve: reseta também os bindings
   dependentes (as linhas de local pendentes). Vale para qualquer campo que venha a ser editado num
   Detail: use um buffer JSON, não o contexto OData.
2. **`Text` sem `wrapping="false"` nem largura em `sap.ui.table`** estoura a altura da linha quando o
   valor é longo.

## Verificação realizada

Contrato **00000141** (aprovado e já faturado, um dos 57 travados): local de entrega incluído, e em
seguida a liberação de entrega solicitada com esse local aparecendo no value help — ciclo fechado.
`dotnet build` limpo, 384 testes, `ts-typecheck`/`lint` limpos.

A alteração de observação também foi verificada no browser antes de ser revertida; o caminho que
restou (locais + anexos) é o mesmo que já estava exercitado.

Não verificado pelo caminho real: a recusa em contrato `Finished` — não há nenhum nesse status na
base. Coberto por 3 testes unitários.
