# Comentários no documento de saída

Data: 2026-07-27

## Origem

O documento de saída tinha um único campo de texto livre — o escalar `Comments` ("Observações",
`VARCHAR(500)`), editável só pela tela de edição. Depois de confirmado o documento não recebia mais
nenhuma anotação, e não havia onde registrar quem falou o quê, quando.

Pedido do usuário: o mesmo sistema de comentários já feito no contrato de venda
(`2026-07-24-contract-comments-design.md`), agora no documento de saída.

## Decisões

| Ponto | Decisão |
|---|---|
| Permissão | Editar/excluir apenas o **próprio** comentário; usuário **admin** pode qualquer um |
| Status do documento | **Qualquer status** — `Pending`, `Confirmed`, `Cancelled`, `Returned`. Sem guarda |
| Log de alterações | **Criado junto** (`SALES_INVOICES_CHANGE_LOGS`); o documento não tinha nenhum |
| Escopo do log | Só linhas de **comentário**. `SalesInvoicesUpdateService` NÃO passou a diferenciar campos |
| Data/hora ao editar | **Sobrescreve**: `CommentedAt`/`CommentedBy` refletem a última alteração |
| UI | Tabela somente-leitura + diálogo com `TextArea`; gravação por **OData action** |

A diferença estrutural em relação ao contrato é o log: no contrato ele já existia e recebia local de
entrega, anexo, fixação de preço e comentário. Aqui ele nasce com esta feature e, por enquanto, só
tem comentário — o documento de saída não tem nenhuma das outras sub-entidades. A estrutura é a mesma
do log do contrato de propósito, para receber outros campos depois sem migração de dados.

## Conflito de nome: `Comments` já existe

Mesmo caso do contrato. `SalesInvoice.Comments` é o escalar de observação, então:

- Nav properties: **`CommentEntries`** e **`ChangeLogs`**
- Entidades: `SalesInvoiceComment`, `SalesInvoiceChangeLog`
- Tabelas: `SALES_INVOICES_COMMENTS`, `SALES_INVOICES_CHANGE_LOGS`
- Entity sets: `SalesInvoicesComments`, `SalesInvoicesChangeLogs`

O código gravado em `Field` é **`Comment`**, o mesmo constante de `ContractChangeLogFields` usada
pelos contratos. Compartilhar o código (em vez de criar uma classe nova com a mesma string) é o que
faz o formatter do frontend (`formatContractChangeLogField`) funcionar sem alteração. O nome da
classe ficou histórico e está documentado como tal.

## Modelo

```
SALES_INVOICES_COMMENTS
  Key              GUID (identity)
  SalesInvoiceKey  GUID FK
  CommentedAt      DATETIME       -- criação; sobrescrita a cada edição
  CommentedBy      VARCHAR(100)   -- autor; sobrescrito a cada edição
  CommentText      VARCHAR(500) NOT NULL

SALES_INVOICES_CHANGE_LOGS
  Key              GUID (identity)
  SalesInvoiceKey  GUID FK
  ChangedAt        DATETIME
  ChangedBy        VARCHAR(100)
  Field            VARCHAR(50) NOT NULL
  OldValue         VARCHAR(500)
  NewValue         VARCHAR(500)
```

Sem `RowVersion` e sem herança de `BaseEntity`, como os outros filhos de documento/contrato.
`CommentText` é `VARCHAR(500)` para casar com `OldValue`/`NewValue`: nenhuma linha de log sai
truncada.

## Reaproveitamento

`SiagroB1.Application/Services/ContractCommentRules.cs` é usado **sem alteração de comportamento**
(`NormalizeText`, `EnsureCanModify`, `MaxTextLength = 500`) — é literalmente a mesma decisão de
negócio dos contratos. Só o XML doc foi generalizado. `ContractChangeLogFields.Comment` idem.

## Ausência deliberada de guarda de status

Comentário é anotação: não altera valor, peso nem saldo do documento. O
`FinishedContractMutationGuardInterceptor` não alcança a entidade (ele só olha filhos de contrato),
então não foi preciso mexer nele. A única guarda é de autoria, no servidor.

## Log de alterações

| Operação | `OldValue` | `NewValue` |
|---|---|---|
| Inclusão | `null` | texto |
| Edição | texto anterior | texto novo |
| Exclusão | texto | `null` |

`SalesInvoicesChangeLogService.Register` só enfileira no contexto; quem chama salva. Assim a mutação
e a linha de log entram no mesmo `SaveChanges` — nunca sobra log de uma alteração que falhou, nem
alteração sem log.

## Backend

- **Domain**: `SalesInvoiceComment`, `SalesInvoiceChangeLog`, navs `CommentEntries`/`ChangeLogs` em
  `SalesInvoice`.
- **Infra**: 2 `DbSet` em `AppDbContext`. Nada em `OnModelCreating`.
- **Application** (`Services/SalesInvoices/`): `SalesInvoicesChangeLogService`,
  `...ChangeLogsGetService`, `...CommentCreateService`, `...CommentUpdateService`,
  `...CommentDeleteService`, `...CommentsGetService`.
- **Web**: 2 `EntitySet`; 3 actions (`SalesInvoicesComment{Create,Update,Delete}`); 2 controllers de
  leitura; 3 action controllers em `Actions/SalesInvoices/`.
- **Migration** (`AppContext`): `20260727160404_AddSalesInvoiceComments` — as duas tabelas juntas.

Escrita por action, e não por POST/PATCH na coleção aninhada, pelos mesmos dois motivos do contrato:
garante mutação + log no mesmo `SaveChanges` de um service único, e desvia do update group diferido
do frontend (no Detail não existe o "Salvar" da tela).

O parâmetro de criação chama `InvoiceKey` (no contrato é `ContractKey`); no Update/Delete o `Key` é o
do **comentário**.

## Frontend

- 3 entradas novas em `ServerRoutes.ts`.
- Handlers em `controller/salesInvoices/Detail.controller.ts` — e não no `BaseController` da pasta,
  que carrega só a exportação Excel compartilhada com Main/Edit. O Detail é o único consumidor.
- 3 fragmentos em `view/salesInvoices/fragments/`: `SalesInvoiceComments`,
  `SalesInvoiceCommentDialog`, `SalesInvoiceChangeLogs`.
- 2 `ObjectPageSection` novas no `Detail.view.xml`. Os botões Incluir/Editar/Remover ficam **sempre
  visíveis**: não há guarda de status, e o Detail do documento de saída não tem o flag
  `ui>/readonly` que o contrato usa.
- O diálogo escreve num **buffer JSON** (`viewModel>/commentDialog/*`), nunca no contexto OData:
  two-way binding num Detail deixa PATCH pendente no update group diferido e derruba o batch inteiro.
- `refreshCommentsList()` refresca as duas tabelas — toda mutação de comentário grava linha no log.

### As duas armadilhas obrigatórias

`$$ownRequest` e `sorter` nos dois bindings de tabela, pelos mesmos motivos do contrato: sem o
primeiro o binding vira `$expand` no GET do documento (que não inclui as coleções) e a tabela vem
vazia; sem o segundo o `OrderByDescending` do service não sobrevive ao `$skip`/`$top` da
`sap.ui.table`. Verificado no batch real:

```
GET SalesInvoices(...)/ChangeLogs?$orderby=ChangedAt desc&...&$skip=0&$top=110
GET SalesInvoices(...)/CommentEntries?$orderby=CommentedAt desc&...&$skip=0&$top=110
```

Duas requisições separadas, cada uma com `$orderby` explícito.

## Verificação realizada

`dotnet build SiagroB1.sln` limpo; **496 testes** passando (baseline era 474 — 22 novos, dos quais 4
de modelo relacional). `yarn ts-typecheck` e `yarn lint` sem apontamento. `yarn ui5lint` sem nenhum
apontamento nos 3 fragmentos novos; o único apontamento novo é
`Detail.controller.ts` chamando `getSelectedIndex` (deprecado) — é o padrão da casa, com 104
ocorrências no repositório, inclusive no controller de comentários do contrato.

Migration aplicada em `localhost / IDX_SIAGRO_DEV` (`ASPNETCORE_ENVIRONMENT=Yokotobi`); as duas
tabelas conferidas no `sys.tables`.

No browser (profile `yktb`, `yarn start:dev`, login `admin`), chegando pelo menu
Vendas → Documentos de Saída → Visualizar:

1. **Documento 000002358 (`Confirmed`)** — incluir, editar e remover comentário, com as 3 linhas
   correspondentes no Log de Alterações (`∅ → texto`, `antigo → novo`, `texto → ∅`), listadas do mais
   recente para o mais antigo. Data/hora reescrita na edição (13:14 → 13:15).
2. **Documento 000002305 (`Cancelled`)** — comentário **aceito**, confirmando a ausência de guarda de
   status.
3. **Ordenação** — conferida no corpo do `$batch`: as duas tabelas mandam `$orderby ... desc` junto
   com `$skip`/`$top`.
4. Console do browser sem nenhum erro novo (só o ruído pré-existente do dev server:
   `Component-preload.js`, cachebuster, LREP, `i18n_pt_BR`, `HEAD /odata/` 405).

**Não verificado pelo caminho real:** a recusa do **servidor** a um não-autor — exigiria um segundo
usuário de verdade. Coberta por 2 testes unitários (update e delete), que também conferem que nenhuma
linha de log é gravada na recusa. A regra do lado do cliente (`canModifyComment`) também não foi
exercida com um segundo usuário.

**Não verificado:** documento em status `Pending` — o ambiente de desenvolvimento não tem nenhum
(2080 `Confirmed`, 275 `Cancelled`, 3 `Returned`). Os quatro status estão cobertos por teste unitário.

Dados de teste deixados no ambiente de desenvolvimento: um comentário no documento 000002305, e 3
linhas de log no documento 000002358 (cujo comentário foi excluído no teste).

## Achado fora de escopo

`view/salesInvoices/fragments/Items.fragment.xml:61` tem `<Text value="{ItemName}" editable="false"/>`
— `sap.m.Text` não tem nem `value` nem `editable`, e o console emite dois `[FUTURE FATAL]` por isso.
É anterior a esta feature e não foi tocado.
