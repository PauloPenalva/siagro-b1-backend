# GAC-1171 (melhorias) — Carga "Descarregada" / "Concluída" e visualizador de anexos

**Data:** 2026-09-23
**Chamado:** GAC-1171, solicitações de melhoria do usuário depois da entrega do registro de descargas
(spec original: `2026-09-17-gac-1171-discharge-tickets-design.md`).
**Branch:** `feature/gac-1171-discharged-status` (backend e frontend, worktrees a partir de `main`).

## 1. O pedido

1. "Após o registro de uma descarga, o status da carga deveria mudar automaticamente ou terá algum
   gatilho manual? Sugestão de nome de status: Descarregado."
2. "Sobre o anexo, ao clicar, não tem como apenas visualizar em tela sem baixar?"
3. "Ao anexar um documento manualmente pelo grid de anexos, não está abrindo de nenhuma forma o
   anexo, só o que foi anexado através da conferência de descarga pelo link do clip na coluna Anexo."

### Decisões do usuário (fechadas no brainstorming)

| # | Decisão |
|---|---|
| D1 | "Descarregada" é um **valor novo do status da carga**, e não um campo à parte. |
| D2 | O gatilho é **manual** (botão), com uma **ação de reversão** para o status anterior. |
| D3 | A marcação só é permitida com a carga **Faturada** (não Faturada Parcial, Devolvida nem Em Transbordo) e **não exige ticket** registrado. |
| D4 | A carga vira **"Concluída" automaticamente** quando **todos** os itens de **todas** as notas normais dela estiverem `Closed` na Conferência de Entregas (`/sales-invoices/reconciliation`). Passar por Descarregada antes é **opcional**. |
| D5 | O estorno da conferência (`/sales-invoices/open-reconciliation`) desfaz o Concluída automaticamente. |
| D6 | Para visualizar, um **diálogo dentro do sistema** (não uma aba nova do navegador), **genérico**, reaproveitado também na tabela de anexos dos contratos de compra e de venda. |

## 2. Modelo de status da carga

### 2.1 A marca manual e o status derivado

O `ShipmentLoad.Status` é derivado. O escritor único é
`ShipmentLoadsRecalculateInvoicedService.ResolveStatus`, e isso **continua valendo**: o botão
"Marcar como Descarregada" **não grava o status**, grava uma marca. Quem traduz a marca em status é
o recálculo.

- **Entidade** `ShipmentLoad`: nova propriedade `bool IsDischarged` (coluna `IsDischarged`, `BIT NOT
  NULL DEFAULT 0`), com XML-doc explicando que é a marca manual e que ela só tem efeito no ramo
  Faturada.
- **Enum** `ShipmentLoadStatus`: `Discharged = 8` ("Descarregada"). O 7 já é `InTransshipment`
  (GAC-1181). O comentário do `Completed = 6` muda de "não existe para carga Normal" para "Remoção:
  manual; Normal: derivado da Conferência de Entregas".
- **"Concluída" reaproveita `Completed = 6`**. O rótulo "Concluída" já existe nas duas pontas, e as
  travas atuais de `Completed` (vincular/desvincular romaneio, cancelar, trocar liberação, transbordo)
  são exatamente as que uma carga com a entrega encerrada precisa.

### 2.2 Regra de resolução

`ResolveStatus` continua **puro e com a mesma assinatura**. Uma segunda função, também pura, refina só
o resultado `Invoiced`:

```text
ResolveClosure(baseStatus, isDischarged, allDeliveriesClosed):
    se baseStatus != Invoiced          → baseStatus
    se allDeliveriesClosed             → Completed      ("Concluída")
    se isDischarged                    → Discharged     ("Descarregada")
    senão                              → Invoiced       ("Faturada")
```

`RecalculateAsync`, no ramo Normal, passa a fazer:

1. `baseStatus = ResolveStatus(...)`, como hoje;
2. **se `baseStatus != Invoiced` e `IsDischarged` → `IsDischarged = false`**. A carga saiu de
   Faturada (uma nota cancelada, excluída ou devolvida, ou um transbordo aberto) e a marca se referia
   a outra mercadoria: um faturamento novo depois disso não pode herdar em silêncio um "Descarregada".
3. `allDeliveriesClosed` só é consultado quando `baseStatus == Invoiced`, porque só esse ramo o usa;
4. `load.Status = ResolveClosure(baseStatus, load.IsDischarged, allDeliveriesClosed)`.

A carga de Remoção não muda: `ResolveRemoval` segue igual, e o `Completed` dela continua manual e
terminal.

### 2.3 "Todos os itens conferidos" (`allDeliveriesClosed`)

É verdadeiro quando, entre as notas com `ShipmentLoadKey = carga` e `InvoiceType = Normal`:

- **não há** nenhuma com `InvoiceStatus = Pending`, porque ela ainda não entrou na Conferência, cujo
  escopo exige `Confirmed`;
- **há ao menos um** item em nota com `InvoiceStatus = Confirmed`;
- **todos** os itens das notas `Confirmed` estão com `DeliveryStatus = Closed`.

Notas `Cancelled` e `Returned` ficam fora, do mesmo jeito que ficam fora da Conferência. A consulta é
um `AnyAsync`/`AllAsync` no servidor, com o mesmo cuidado de `SumAsync` × `ChangeTracker` já
documentado: quem chama roda o recálculo **depois** do `SaveChangesAsync` que torna a mudança do item
visível.

Método estático novo, no próprio `ShipmentLoadsRecalculateInvoicedService`:
`AreAllDeliveriesClosedAsync(AppDbContext, Guid loadKey, ICollection<Guid>? excludedInvoiceKeys)`. O
`excludedInvoiceKeys` segue a mesma semântica de `CalculateInvoicedAsync`.

### 2.4 Projeção nos romaneios

`shipmentStatus` passa a considerar a carga encerrada também em `Discharged` e `Completed`:

```csharp
load.Status is Invoiced or Returned or Discharged or Completed
    ? StorageTransactionsStatus.Invoiced : StorageTransactionsStatus.Confirmed
```

Sem isso, marcar uma carga como Descarregada devolveria os romaneios para `Confirmed` e eles
reapareceriam na Montagem de Carga.

### 2.5 Transições

| De → Para | Gatilho | Quem grava |
|---|---|---|
| Faturada → Descarregada | botão **Marcar como Descarregada** | `ShipmentLoadsMarkDischargedService`: `IsDischarged = true` + recálculo |
| Descarregada → Faturada | botão **Desfazer Descarregada** | `ShipmentLoadsUndoDischargedService`: `IsDischarged = false` + recálculo |
| Faturada / Descarregada → Concluída | último item vira `Closed` | recálculo disparado pela Conferência (§2.7) |
| Concluída → Descarregada / Faturada | estorno da conferência de qualquer item | recálculo disparado pelo estorno (§2.7); a marca decide para qual dos dois volta |
| Descarregada → Faturada Parcial / Devolvida / Em Transbordo | nota cancelada, excluída ou devolvida, ou transbordo | o recálculo existente; a marca é apagada (§2.2, passo 2) |

### 2.6 As duas ações manuais

Seguem o molde de `ShipmentLoadsCompleteService` / `ShipmentLoadsReopenService`: transação própria,
`changeLog.Register` do campo Status e `movementLog.Register`.

**`ShipmentLoadsMarkDischargedService.ExecuteAsync(Guid key, string userName)`**
- `NotFoundException` se a carga não existir.
- Recusa carga de Remoção: "A carga {Code} é do tipo Remoção e não passa por descarga."
- Recusa quando `Status != Invoiced`, com o motivo por status:
  - Descarregada: "já está marcada como descarregada";
  - Concluída: "já foi concluída";
  - Faturada Parcial: "ainda tem saldo a faturar";
  - Cancelada: "está cancelada";
  - Devolvida: "foi devolvida ao armazém";
  - Em Transbordo: "está em transbordo";
  - qualquer outro: "ainda não foi faturada".
- `IsDischarged = true`, recálculo, carimbo `UpdatedAt`/`UpdatedBy`, log "Faturada → Descarregada"
  com o status **resultante do recálculo** (se a conferência já estiver toda encerrada, o resultado é
  Concluída, e o log diz isso), e movimento `Discharged`.

**`ShipmentLoadsUndoDischargedService.ExecuteAsync(Guid key, string userName)`**
- Recusa quando `Status == Completed` e é carga Normal: "A carga {Code} está concluída. Estorne a
  conferência de entrega antes de desfazer a descarga."
- Recusa quando `Status != Discharged`: "A carga {Code} não está marcada como descarregada."
- `IsDischarged = false`, recálculo, carimbo, log "Descarregada → {status resultante}", movimento
  `DischargeUndone`.

**Enum** `ShipmentLoadMovementType`: `Discharged = 23`, `DischargeUndone = 24`, sempre no fim.

**OData:** actions `ShipmentLoadsMarkDischarged(Key)` e `ShipmentLoadsUndoDischarged(Key)`, em
controllers `Web/Actions/ShipmentLoads/`, com a mesma triagem de exceção dos irmãos (NotFound → 404;
Default/Business/Application → 400; resto → 500).

### 2.7 Gancho da Conferência de Entregas

A Conferência e o estorno da conferência gravam `DeliveryStatus` por PATCH em `SalesInvoicesItems`,
tratado por `SalesInvoicesItemsUpdateService`. Nele, **quando `deliveryChanged`**, depois dos
recálculos de contrato e liberação que já existem:

1. resolve a carga da nota com `SalesInvoiceOriginResolver.ResolveShipmentLoadKeyAsync`, o mesmo do
   `ShipmentLoadsBalanceHookService`;
2. chama `ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(context, loadKey, null)`;
3. se o status mudou, registra no change log da carga "Situação: {de} → {para}", assinado pelo
   usuário da Conferência. A transição é consequência de um ato dele, e sem o log a carga "se
   concluiria sozinha" sem rastro;
4. `SaveChangesAsync`.

Nota sem carga (legada ou avulsa) → no-op, como no hook de saldo.

**Outros escritores de `DeliveryStatus`**:
- `SalesInvoicesConfirmService` (nota de retorno), `SalesInvoicesReverseConfirmService` (3 pontos):
  já chamam o `ShipmentLoadsBalanceHookService`, que recalcula a carga.
- `SalesInvoicesReturnService` e `SalesInvoicesReturnOriginRestoreService` (Closed/Open da nota de
  ORIGEM na devolução): hoje não recalculam a carga.

O plano **confirma cada um desses caminhos com teste**. Onde o recálculo não acontecer, o gancho é
acrescentado no mesmo formato. A regra: quem muda o `DeliveryStatus` de um item de nota de carga
precisa recalcular a carga no mesmo save.

### 2.8 Varredura: quem trata `Invoiced`/`Completed` como "carga encerrada"

Regra geral: **Descarregada se comporta como Faturada em toda trava**, e **Concluída de carga Normal
se comporta como o `Completed` que já existe**.

| Ponto | Mudança |
|---|---|
| `ShipmentLoadsRecalculateInvoicedService` (projeção nos romaneios) | §2.4 |
| `ShipmentLoadsUpdateService.fiscalFieldsLocked` | incluir `Discharged` e `Completed` |
| `ShipmentLoadsAttachTransactionsService.EnsureLoadAcceptsShipments` | a mensagem de `Completed` diz "reabra-a" e só vale para Remoção. Para carga Normal: "já foi concluída" e nada mais |
| `ShipmentLoadsReopenService` | passa a **recusar carga Normal**: "A carga {Code} é concluída pela conferência de entrega: estorne a conferência para reabri-la." Sem isso, Reabrir numa Concluída normal voltaria na hora para Concluída |
| `ShipmentLoadsCancelService` | `Completed` já barra; `Discharged` já cai na trava de carga com faturamento. O plano confirma por teste |
| `ShipmentLoadsRefuseService` | recusa também `Discharged` e `Completed`: "A carga {Code} já foi descarregada no destino. Desfaça a descarga antes de registrar recusa." |
| `ShipmentLoadTransshipmentRules` / `ShippingTransactionsChangeReleaseService` | incluir `Discharged` ao lado de `Completed` |
| `ShipmentLoadDischargeRules.EnsureLoadAcceptsChanges` | **não muda**: registrar, editar e excluir ticket continua valendo em Descarregada e Concluída, porque o ticket costuma chegar depois |
| `ShipmentLoadChangeLogFields.DescribeStatus` | `Discharged => "Descarregada"` |
| `ShipmentLoadsBillingGuardService` | sem mudança: a carga Descarregada/Concluída não tem saldo a faturar. O plano confirma |

> ⚠️ **Decisão a revisar:** com `Completed` reaproveitado, uma carga Normal **Concluída** fica
> bloqueada para **Trocar Liberação**, como a de Remoção já fica. Se a troca de liberação precisar
> continuar possível depois da conferência encerrada, a trava de `ShippingTransactionsChangeReleaseService`
> passa a olhar `LoadType`.

### 2.9 Migration

Uma migration, `AddShipmentLoadIsDischarged`: coluna `IsDischarged BIT NOT NULL DEFAULT 0` em
`SHIPMENT_LOADS`. **Não há backfill**: toda carga existente continua com o status que tem, porque
nenhuma estava marcada. Cargas Faturadas cuja conferência **já está toda encerrada** só viram Concluída
no próximo recálculo (botão "Recalcular Saldo" ou qualquer mudança). **Não** vamos fazer backfill por
SQL, porque ele duplicaria a regra do §2.3 num segundo lugar. Isso fica registrado para o deploy.

## 3. Frontend da carga

### 3.1 Detalhe (`view/shipmentLoads/Detail.view.xml` + `controller/shipmentLoads/Detail.controller.ts`)

Na barra de ações do cabeçalho, ao lado de Concluir/Reabrir da Remoção:

| Botão | Visível quando | Ação |
|---|---|---|
| **Marcar como Descarregada** | `LoadType !== 'Removal'` e `Status === 'Invoiced'` | `confirmDialog("Marcar a carga como descarregada ?")` → `ShipmentLoadsMarkDischarged(...)` → `MessageToast` → recarrega o contexto, o change log e os movimentos |
| **Desfazer Descarregada** | `Status === 'Discharged'` | mesmo fluxo com `ShipmentLoadsUndoDischarged(...)` |

- Todas as expressões usam `targetType: 'any'`.
- A chave da carga é capturada **antes** do `context.refresh()` (armadilha documentada no
  `refreshDischarges()`).
- **Registrar Recusa** continua só em Faturada Parcial / Faturada.
- **Trocar Liberação** (linha 229) passa a ficar escondido também em `Discharged` e `Completed`,
  acompanhando a trava do backend (§2.8).
- **Iniciar Transbordo** (`ShipmentLoadTransshipments.fragment.xml:61`) passa a ficar escondido
  também em `Discharged`.

### 3.2 Rótulos (`model/formatter.ts`, mapas da carga ~631/658/689)

- `formatShipmentLoadStatus`: `Discharged → "Descarregada"`.
- Estado: `Discharged → "Information"`, para se distinguir de Faturada (`Success`).
- Rótulo longo (~689): `Discharged → "Carga Descarregada"`. O `Completed → "Carga Concluída"` atual
  serve às duas.

### 3.3 Listas e Painel

- `controller/shipmentLoads/Main.controller.ts` (`SHIPMENT_LOAD_STATUSES`): "Descarregada" entre
  Faturada e Devolvida.
- `controller/storageTransactions/sales/Main.controller.ts:32` (mesma lista): mesma inclusão.
- `controller/storageTransactions/sales/Main.controller.ts:230`: o aviso "já tem faturamento… cancele
  os documentos de saída" passa a cobrir `Discharged` e `Completed`.
- `controller/shipmentLoads/Panel.controller.ts` (`LANES`) + `view/shipmentLoads/Panel.view.xml`: raia
  **Descarregada** (`listDischarged` / `countDischarged`) entre Faturada e Devolvida. A raia Concluída
  já existe e passa a receber cargas Normais.

A Conferência de Entregas **não muda na tela**: o efeito na carga vem do servidor, pelo mesmo
PATCH/batch.

## 4. Visualizador genérico de anexos

### 4.1 Componente

Arquivo novo: `webapp/dialogs/AttachmentViewer.ts`.

```ts
export async function openAttachmentViewer(options: {
  url: string;          // rota de download que devolve o binário
  fileName?: string;    // se ausente: lido do Content-Disposition da resposta
  title?: string;       // default: o nome do arquivo
}): Promise<void>
```

- **Montado em código** (`new Dialog`), sem fragmento: qualquer controller chama sem depender do id
  da view nem de `addDependent`. Um diálogo por abertura, destruído no `afterClose`.
- **Carga**: `fetch(url)`. Se `!ok` → `MessageBox.error(await readErrorMessage(response))` e **não
  abre** o diálogo. Senão → `blob` → `URL.createObjectURL`. Com blob não há `Content-Disposition`
  mandando baixar, então **as rotas de download existentes servem como estão, sem mudança no
  backend**. `setBusy` global durante o fetch.
- **Tipo**: `blob.type`, ou, quando for vazio ou `application/octet-stream`, deduzido da extensão do
  `fileName` (`pdf`, `png`, `jpg`/`jpeg`, `gif`, `webp`, `bmp`, `txt`).
- **Conteúdo pelo tipo**:
  - `application/pdf`, `text/*` → `core:HTML` com `<iframe src="{objectURL}" style="width:100%;height:100%;border:0">`,
    como o `ReportViewer` dos relatórios;
  - `image/*` → `sap.m.Image` com `src = objectURL`, `densityAware=false`, ajustada ao diálogo;
  - outros → `MessageStrip` "Pré-visualização indisponível para este tipo de arquivo. Use Baixar."
- **Diálogo**: título = `title ?? fileName`, `contentWidth="80%"`, `contentHeight="85%"`,
  `resizable`, `draggable`, `stretch` em telefone.
- **Rodapé**: **Baixar** (`<a download>` com o **mesmo** objectURL, sem refazer o fetch) e
  **Fechar**. No `afterClose`: `URL.revokeObjectURL` + `destroy`.
- **Nome do arquivo pelo `Content-Disposition`**: aceita `filename*=UTF-8''…` (decodificado) e
  `filename="…"`. Sem nenhum dos dois, o nome é `"anexo"`.
- **A verificar na implementação**: se o Gateway manda um `Content-Security-Policy` com
  `frame-src`/`img-src`, ele precisa aceitar `blob:`. Se recusar, esse é o primeiro ajuste.

### 4.2 Onde entra

| Tela | Mudança |
|---|---|
| Carga, aba **Anexos** (`ShipmentLoadAttachments.fragment.xml`) | a coluna "Arquivo" vira `Link` → `onViewAttachment` (contexto da própria linha, **sem exigir seleção**). A toolbar ganha **Visualizar** (linha selecionada), entre Anexar e Baixar. O **Baixar** continua |
| Carga, aba **Descargas** (`ShipmentLoadDischarges.fragment.xml`) | o clip passa a abrir o visualizador; tooltip "Visualizar o ticket anexado" |
| Contrato de **compra** (`PurchaseContractAttachments.fragment.xml` + `PurchaseContractsBaseController.ts`) | Link no nome do arquivo + botão **Visualizar**; Baixar mantido |
| Contrato de **venda** (`SalesContractAttachments.fragment.xml` + `SalesContractsBaseController.ts`) | idem |

A Conferência de Armazém fica **fora do escopo**. Plugar o visualizador lá depois é trocar uma
chamada.

### 4.3 Item 3 — "anexo manual não abre"

Pelo código, o anexo manual e o do ticket passam pelo **mesmo** `loadAttachmentBase64`, gravam
`ContentType` do mesmo jeito e saem pela **mesma** rota `ShipmentLoadsAttachmentsDownload(Key=…)`. A
causa, portanto, **não está identificada**. Hipóteses:

- **(a)** falta de affordance: o único caminho é o botão "Baixar" da toolbar, que exige linha
  selecionada, e nada na linha é clicável. A §4.2 resolve;
- **(b)** falha real no download desses arquivos. Ela quebraria o visualizador novo também.

**Primeira tarefa da implementação**: reproduzir no navegador, anexando pela aba Anexos e tentando
abrir, com `superpowers:systematic-debugging` e lendo a rede. Se for (b), a correção da causa raiz
entra nesta mesma branch, **com teste que falha antes**, e o spec ganha uma nota com a causa.

## 5. Testes e verificação

### Backend (`SiagroB1.Application.Tests`, xUnit + EF InMemory)

- **`ResolveClosure`** (puro, tabela):
  - `Invoiced` × {marca, conferência toda encerrada} → Faturada / Descarregada / Concluída /
    Concluída;
  - qualquer outro `baseStatus` passa intacto.
- **`AreAllDeliveriesClosedAsync`**:
  - todos `Closed` → true;
  - um `Open` → false;
  - uma nota `Pending` → false;
  - nota `Cancelled`/`Returned` com item `Open` é ignorada;
  - carga sem nota Confirmed → false.
- **Recálculo**:
  - a marca é apagada quando a carga sai de Faturada;
  - a projeção nos romaneios fica `Invoiced` em Descarregada e Concluída.
- **Marcar / Desfazer**:
  - cada recusa do §2.6;
  - o status resultante;
  - change log e movimento gravados;
  - Marcar com a conferência já encerrada → Concluída.
- **Gancho da Conferência** (`SalesInvoicesItemsUpdateService`):
  - fechar o último item → Concluída + change log;
  - estornar um item → volta a Descarregada (com a marca) ou a Faturada (sem a marca);
  - nota sem carga → no-op.
- **Demais escritores de `DeliveryStatus`** (§2.7): um teste por caminho provando que a carga é
  recalculada.
- **Varredura** (§2.8): Reabrir recusa Normal; Recusa barra Descarregada/Concluída; campos fiscais
  travados; trava de transbordo e de troca de liberação em Descarregada.
- A suíte inteira verde antes de cada commit. Baseline no worktree: copiar `SiagroB1.Reports/wwwroot`
  (armadilha conhecida de worktree novo).

### Frontend

- `npm run ts-typecheck` e `npm run lint` limpos. O `yarn test` não serve de gate, porque a trava de
  cobertura é irreal.

### No navegador, pelo caminho do usuário (stack local, profile `yktb`)

1. Carga Faturada → **Marcar como Descarregada** → status, Painel e filtro da lista; **Desfazer** →
   Faturada.
2. Conferência de Entregas: encerrar todos os itens das notas da carga → carga **Concluída** na lista
   e no Painel; estornar um item → volta a Descarregada (ou a Faturada).
3. Anexos: PDF e imagem pela aba Anexos (link e botão), pelo clip das Descargas e pelos anexos do
   contrato de compra e do de venda; um `.docx` mostra o aviso e baixa pelo botão.
4. O defeito do item 3 reproduzido **antes** e ausente **depois**.

## 6. Fora de escopo

- Anexos da Conferência de Armazém.
- Backfill por SQL das cargas já conferidas (§2.9).
- Exigir ticket, ou a soma dos tickets, para marcar Descarregada (D3).
- Alterar o registro de ticket para mudar o status sozinho (D2: o gatilho é manual).
