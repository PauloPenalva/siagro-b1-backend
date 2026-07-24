# Comentários no contrato de compra e de venda

Data: 2026-07-24

## Origem

O contrato tem um único campo de texto livre — o escalar `Comments` ("Observações", `VARCHAR(500)`),
editável **apenas em rascunho**, pela tela de edição (decisão registrada em
`2026-07-24-contract-change-log-design.md:30-37`). Não havia onde registrar anotações ao longo da vida
do contrato: quem falou o quê, quando. Contrato aprovado, encerrado ou cancelado simplesmente não
recebia mais nenhum texto.

Pedido do usuário: uma **coleção de comentários** por contrato — data, hora, usuário e texto — na tela
**Detail**, editável a qualquer tempo, com tudo registrado no log de alterações.

## Decisões

| Ponto | Decisão |
|---|---|
| Permissão | Editar/excluir apenas o **próprio** comentário; usuário **admin** pode qualquer um |
| Status do contrato | **Qualquer status**, inclusive `Finished` e `Canceled` — sem guarda de status |
| Data/hora ao editar | **Sobrescreve**: `CommentedAt`/`CommentedBy` refletem a última alteração; o texto anterior fica no log |
| UI | Tabela somente-leitura + diálogo com `TextArea`; gravação por **OData action** |

Tamanho do texto: `VARCHAR(500)`, igual às colunas `OldValue`/`NewValue` do log — assim nenhuma linha
de log sai truncada.

Ordenação da lista: mais recente primeiro, como o log de alterações.

## Conflito de nome: `Comments` já existe

`PurchaseContract.Comments` e `SalesContract.Comments` são o campo escalar de observação. A coleção
**não pode** usar esse nome, e renomear o escalar sairia caro (coluna, bindings do formulário,
relatórios) sem ganho para esta feature. Portanto:

- Nav property nos contratos: **`CommentEntries`**
- Entidades: `PurchaseContractComment` / `SalesContractComment`
- Tabelas: `PURCHASE_CONTRACTS_COMMENTS` / `SALES_CONTRACTS_COMMENTS`
- Entity sets OData: `PurchaseContractsComments` / `SalesContractsComments`

O código gravado no log é **`Comment`** (singular), distinto do legado `Comments` (observação, que
chegou a ser editável depois de aprovada e deixou linhas no banco). Os dois convivem no formatter do
frontend: `Comment` → "Comentário", `Comments` → "Observação".

## Modelo

```
{PURCHASE,SALES}_CONTRACTS_COMMENTS
  Key                  GUID (identity)
  {Purchase,Sales}ContractKey  GUID FK
  CommentedAt          DATETIME       -- criação; sobrescrita a cada edição
  CommentedBy          VARCHAR(100)   -- autor; sobrescrito a cada edição
  CommentText          VARCHAR(500) NOT NULL
```

Sem `RowVersion` e sem herança de `BaseEntity` — mesma forma dos outros filhos de contrato
(`SalesContractDeliveryLocation`, `SalesContractAttachment`).

## Ausência deliberada de guarda de status

Comentário é anotação: não altera valor, volume nem saldo do contrato. Não passa por
`SalesContractsPostApprovalGuard` (Draft/Approved) e **não** entra no `switch` de
`FinishedContractMutationGuardInterceptor` — o interceptor bloqueia mutação de filhos de contrato
`Finished`, e o comentário é a exceção pretendida. Está registrado como comentário no próprio
interceptor para não parecer esquecimento.

A única guarda é de **autoria**: `ContractCommentAuthorGuard.EnsureCanModify(author, userName, isAdmin)`,
compartilhada pelos dois lados. `isAdmin` vem da claim `IsAdmin`, gravada tanto pelo
`BasicAuthenticationHandler` quanto pelo `CookieAuthMiddleware` — chega ao Web pelos dois caminhos de
autenticação.

## Log de alterações

Reaproveita a infraestrutura existente nos dois lados (`{Purchase,Sales}ContractsChangeLogService.Register`,
enfileirado no mesmo `SaveChanges` da mutação):

| Operação | `OldValue` | `NewValue` |
|---|---|---|
| Inclusão | `null` | texto |
| Edição | texto anterior | texto novo |
| Exclusão | texto | `null` |

## Backend

- **Domain**: 2 entidades + nav `CommentEntries` nos dois contratos + `ContractChangeLogFields.Comment`.
- **Infra**: 2 `DbSet` em `AppDbContext`. Nada em `OnModelCreating` — anotações bastam, e o laço de
  `OnModelCreating` já força `DeleteBehavior.NoAction` em toda FK.
- **Application** (`Services/{Purchase,Sales}Contracts/`, 1:1 entre os lados):
  `{X}ContractsCommentCreateService`, `...UpdateService`, `...DeleteService`, `...CommentsGetService`
  (`QueryAll`, `AsNoTracking`, `CommentedAt` desc). Guarda de autoria compartilhada em
  `Services/ContractCommentAuthorGuard.cs`.
- **Web**: `EntitySet` × 2; 6 actions (`{X}ContractsCommentCreate/Update/Delete`); um controller de
  leitura por lado (`GET odata/{X}Contracts({key})/CommentEntries`); 6 action controllers em
  `Actions/{Purchase,Sales}Contracts/`; `ClaimsPrincipalExtensions.IsAdmin()`.
- **Migrations** (`AppContext`): `AddPurchaseContractComments`, `AddSalesContractComments`.

Escrita por action, e não por POST/PATCH na coleção aninhada, por dois motivos: garante que a mutação e
a linha de log entrem no mesmo `SaveChanges` de um service único, e evita o update group diferido do
frontend — no Detail não existe o "Salvar" da tela.

## Frontend

- `SessionService` passa a expor `sessionModel>/userName` e `/isAdmin` (o endpoint
  `/security/auth/status` já devolvia os dois; o serviço descartava). Preenchido no boot e no login,
  limpo no logout. É o que permite esconder/recusar a edição de comentário de outro autor antes de
  bater no servidor.
- Seção **"Comentários"** no `Detail` dos dois contratos: `sap.ui.table.Table` somente-leitura
  (`CommentEntries` com `$$ownRequest`) + Incluir/Editar/Remover, e um diálogo com `TextArea`.
- O diálogo escreve num **buffer JSON** (`viewModel>/commentDialog/...`), nunca no contexto OData:
  two-way binding num Detail deixa PATCH pendente no update group diferido e derruba o batch inteiro
  (armadilha #1 do spec do log de alterações).
- Toda mutação chama `refreshCommentsList()`, que refresca a tabela e o log de alterações.

### Armadilha: `OrderBy` do service não sobrevive à paginação

O `OrderByDescending` de `{X}ContractsCommentsGetService` só vale na consulta **sem paginação**. A
`sap.ui.table` pede `$skip`/`$top`, e aí o `[EnableQuery]` reordena por chave para estabilizar a
paginação — a lista sai na ordem de inserção. Medido no ambiente Yokotobi:

| Requisição | Ordem devolvida |
|---|---|
| `.../CommentEntries` | `CommentedAt` desc (a do service) |
| `.../CommentEntries?$skip=0&$top=10` | ordem de inserção |
| `.../CommentEntries?$orderby=CommentedAt desc&$skip=0&$top=10` | desc |

Por isso o binding declara `sorter: { path: 'CommentedAt', descending: true }`: manda `$orderby`
explícito, que sobrevive à paginação.

As tabelas do **log de alterações** (compra e venda) tinham o mesmo defeito — apareciam em ordem
crescente na tela apesar do `OrderByDescending` no service — e receberam o mesmo `sorter`, em
`ChangedAt`. Verificado no browser: as duas passaram a listar do mais recente para o mais antigo.

## Verificação realizada

`dotnet build SiagroB1.sln` limpo; **433 testes** passando (baseline era 384 — 49 novos, dos quais 3
de modelo relacional). `yarn ts-typecheck`, `yarn lint` e `yarn ui5lint` sem nenhum apontamento nos
arquivos novos. Migration `20260724181229_AddContractComments` aplicada em
`localhost / IDX_SIAGRO_DEV` (`ASPNETCORE_ENVIRONMENT=Yokotobi`).

No browser (profile `yktb`, `yarn start:dev`, login `admin`):

1. **Compra 00004437 (Aprovado)** — incluir, editar e remover comentário, com as 4 linhas
   correspondentes no Log de Alterações: `∅ → texto` (2×), `texto antigo → texto novo`,
   `texto → ∅`. Data/hora e usuário reescritos na edição (15:27 → 15:31).
2. **Venda 00000562 (Aprovado)** — inclusão e linha `Comment` no log.
3. **Compra 00000014 (`Finished`)** — comentário **aceito**, confirmando que o
   `FinishedContractMutationGuardInterceptor` não alcança a entidade nova.
4. **Identidade da sessão** — `sessionModel` traz `userName: "admin"` e `isAdmin: true` vindos de
   `/security/auth/status`.
5. **Regra de autoria (cliente)** — com a sessão forçada para um usuário não-admin diferente do
   autor, Editar recusa com "Somente o autor do comentário pode alterá-lo.".
6. Console do browser sem nenhum erro novo (só o ruído pré-existente do dev server:
   `Component-preload.js`, cachebuster, LREP, `i18n_pt_BR`).

**Não verificado pelo caminho real:** a recusa do **servidor** a um não-autor — exigiria um segundo
usuário de verdade. Coberta por 4 testes unitários (compra e venda, update e delete), que também
conferem que nenhuma linha de log é gravada na recusa.

Dados de teste deixados no ambiente de desenvolvimento: um comentário em cada um dos contratos
00004437 (compra), 00000014 (compra, encerrado) e 00000562 (venda).
