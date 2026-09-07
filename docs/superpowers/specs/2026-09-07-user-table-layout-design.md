# GAC-1163 — Largura e ordem das colunas gravadas no perfil do usuário

Data: 2026-09-07

## Problema

As tabelas do frontend já são redimensionáveis e reordenáveis por arrasto (`sap.ui.table.Table`
traz `enableColumnReordering = true` por padrão), mas nada é persistido: os `targets` do manifest
usam `clearControlAggregation: true`, então a view é destruída a cada navegação e recriada com as
larguras e a ordem declaradas no XML. O operador refaz o mesmo ajuste várias vezes por dia.

O chamado pede que essas definições passem a viver no **perfil do usuário**, não no navegador.

## Escopo

- **105 `sap.ui.table.Table`** (101 arquivos, 736 colunas). Cobertura automática, sem alterar views.
- Persistir **largura** e **ordem** das colunas. Nada de visibilidade, ordenação ou filtro.
- Gravação **automática** ao redimensionar/mover, com debounce. Sem botão "Salvar".
- Restauração do padrão **apenas** em "Meu Perfil", limpando todos os layouts de uma vez.

### Fora de escopo

- **As 13 `sap.m.Table`** (inclusive as 29 `TableSelectDialog` de value help): não têm
  redimensionamento nem reordenação por arrasto, nem os eventos `columnResize`/`columnMove` — não há
  o que capturar. Dar-lhes resize exigiria o plugin `sap.m.plugins.ColumnResizer`, que é
  funcionalidade nova, não persistência.
- **Exportação para Excel.** `CommonController.createExportBinding` e os `Spreadsheet` dos ~15
  controllers usam arrays de coluna hard-coded, não `table.getColumns()`. O Excel continuará saindo
  na ordem declarada. Ticket próprio.

## Modelo de dados

Tabela `USER_TABLE_LAYOUTS` no banco COMMON (`CommonDbContext`, junto de `USERS`), uma linha por
`(Username, TableKey)`:

| Coluna | Tipo | Nota |
|---|---|---|
| `Id` | `uniqueidentifier` | PK |
| `Username` | `VARCHAR(50)` | sem FK para USERS, padrão de `USER_TRUCK_SCALES` |
| `TableKey` | `VARCHAR(200)` | maior chave medida ≈ 120 |
| `LayoutJson` | `nvarchar(max)` | documento versionado |
| `UpdatedAt` | `datetime2` | |

Índice único `(Username, TableKey)` — 250 bytes, bem abaixo do limite de 1700 do SQL Server.

Uma coluna JSON única em `USERS` foi **descartada**: (a) com abas concorrentes, um documento único
faz a aba B sobrescrever o ajuste da aba A; (b) `UserProfileService.GetProfileAsync` faz `SELECT *`
e `USERS` também é lido no login e no `/status`, então um `nvarchar(max)` entraria em toda leitura.

`LayoutJson`:

```json
{"Version":1,"Columns":[{"Key":"Key"},{"Key":"Branch/ShortName","Width":"180px"},{"Key":"Type"}]}
```

O array **é** a ordem — não existe campo `Position`, que só convidaria a inconsistência. `Width`
ausente significa "largura padrão do XML": gravar apenas o que diverge do default evita congelar
para sempre as larguras de uma release antiga.

## API

Todos no Gateway, sob `security/users`, `[Authorize]`, resolvendo o alvo só por
`User.Identity.Name` — nunca por identificador vindo da requisição. `/security/**` não passa pelo
YARP.

| Verbo | Rota | Corpo |
|---|---|---|
| GET | `me/table-layouts` | → `{ "Layouts": [ { TableKey, Version, Columns, UpdatedAt } ] }` |
| PUT | `me/table-layouts` | `{ TableKey, Columns: [{ Key, Width? }] }` → upsert |
| DELETE | `me/table-layouts` | limpa tudo do usuário |

Array e não mapa na resposta: as chaves contêm `/`, `::` e `#`. Chave no corpo do PUT e não na URL,
para não escapar esses mesmos caracteres.

### Validação (servidor)

`TableKey` obrigatório, `<= 200`, casando `^[A-Za-z0-9_.:#/\-]+$`. `Columns` de 1 a 60. `Key`
obrigatório, `<= 120`, sem duplicata na lista. `Width` opcional casando
`^\d{1,5}(\.\d{1,3})?(px|rem|em|%)$` — **é a única superfície de injeção do recurso, porque o valor
vai direto para o atributo `style` do `<th>`**. `LayoutJson` `<= 8000` chars. Máximo de 300 linhas
por usuário (barra o insert de chave nova; update de chave existente sempre passa).

## Identidade

### Tabela

`<viewName>::[<escopo>--]<localId>`, ex. `siagrob1.view.purchaseContracts.Main::tablePurchaseContracts`.

`View#getViewName()` é markup, portanto estável; o id de runtime não é. Isso resolve o id duplicado
`tableReleaseTransactions`, presente em dois fragments diferentes.

`View#getLocalId` **não basta**: ele só casa com o prefixo `"<viewId>--"`, e `DialogHelper` monta o
id como `view.getId() + "_" + name`. O sufixo é derivado cortando o prefixo do `viewId` à mão.

### Coluna

Nenhuma das 736 colunas tem `id`. A chave é derivada por candidatas, com **fall-through na colisão**
(a primeira ainda livre naquela tabela vence):

```
sortProperty → filterProperty → path do binding de conteúdo do template → label:<texto> → col<i>
```

O fall-through é o que torna correta a coluna "Tipo de Contrato" de três telas, que tem
`sortProperty="Code"` copiado da coluna "Codigo" (bug pré-existente): ela cai no path do template e
vira `Type`. Corrigir esse `sortProperty` depois **não muda a chave**, então o bug pode ser tratado
em separado sem resetar o layout de ninguém.

Só `view/veiculo/Main.view.xml` precisa do desempate `#n` — tem duas colunas "Modelo" idênticas.

## Aplicação e auto-cura

- **Largura**: aplicada por chave, sempre.
- **Ordem**: merge ancorado. Colunas conhecidas seguem a ordem salva; colunas novas ficam ao lado da
  vizinha conhecida à esquerda — ou seja, na posição em que foram declaradas. Se menos da metade das
  colunas atuais for reconhecida, a ordem salva é descartada (tabela reescrita).
- `Version` no documento: se a derivação de chave mudar um dia, basta bumpar e descartar o antigo.

## Engate no frontend

`onBeforeRendering()` no `BaseController`, varrendo a view por `sap.ui.table.Table` com um `WeakSet`
de raízes já visitadas. `onBeforeRendering` e não `onAfterRendering` porque: zero controllers o
definem hoje (contra 2); em `onAfterRendering` a tabela já pintou no padrão e o salto é visível; e a
varredura roda antes de as linhas existirem.

Não existe hook global suportado no 1.141: `Controller.registerExtensionProvider` está deprecated
desde 1.136 e `connectToView` não está no `.d.ts`.

Diálogos renderizam sozinhos e não disparam o `onBeforeRendering` da view — quatro pontos de código
(`DialogHelper.createDialog` e três controllers) cobrem os 7 fragments com tabela.

## Captura

`columnResize` e `columnMove` são `allowPreventDefault` e disparam **antes** de aplicar a mudança.
O handler nunca reconstrói o estado a partir dos parâmetros do evento e nunca chama
`preventDefault()`: agenda `setTimeout(0)`, tira um snapshot completo, atualiza cache e espelho
local imediatamente, e só o PUT entra em debounce de 800 ms por chave. Assim, redimensionar e
navegar em 300 ms não perde o ajuste.

O layout é aplicado **antes** de anexar os handlers, o que elimina cascata por construção; um flag
`applying` fica como seguro contra refactor futuro.

## Carga e limpeza

A carga entra no `Promise.all` de `SessionService.hydrate()` **com `.catch()` próprio, nunca
rejeitando** — uma rejeição de `hydrate()` deixaria `startIdleWatch()` sem rodar. Como
`bootstrapSession()` só chama `router.initialize()` depois de `hydrate()`, nenhuma view existe antes
de os layouts chegarem: não há flash a evitar, e o espelho em `localStorage` serve como resiliência.

O espelho é namespaceado por usuário (`{ username, layouts }`), senão o usuário B veria por um
instante o layout do usuário A na mesma máquina. Diferente do tema, o layout **não** sobrevive ao
logout.

Falhas do PUT são mudas (`console.warn`). Um 401 aqui não passa pelo message model do OData e
portanto não expulsa ninguém para o login — o que é o desejado.

## Riscos aceitos

1. Excel sai na ordem antiga (fora de escopo, acima).
2. As 138 colunas sem `width` não são persistidas e voltam a flexionar após F5 — correto, mas parece
   bug.
3. Duas abas: última escrita vence, por tabela.
4. Um controller futuro que sobrescreva `onBeforeRendering` sem `super` mata a persistência daquela
   tela; a rede do `onAfterRendering` salva, com pisca.
5. `removeColumn` limpa `_aSortedColumns` — hoje inofensivo (nenhuma coluna declara `sorted=`) porque
   a ordem é aplicada uma única vez, antes de qualquer interação.
6. Chaves órfãs de telas removidas ficam para sempre; o teto de 300 linhas limita o payload e o botão
   de restaurar é a válvula de escape.
7. Fragmento reusado em `Add` e `Edit` gera duas chaves — correto (são telas distintas), mas ajustar
   numa não reflete na outra.
