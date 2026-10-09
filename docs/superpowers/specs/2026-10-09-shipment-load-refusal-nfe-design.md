# Recusa de carga com NF-e de entrada própria (STANDALONE) — design

Data: 2026-10-09 · Branch: `feature/load-refusal-nfe` (backend e frontend)

## 1. Problema

Na filial que emite NF-e pelo Siagro (regra de `TaxCalculationGate` ativa — hoje a CEAGUI), registrar a
recusa de uma carga cujos documentos têm NF-e autorizada é recusado por `ShipmentLoadsRefuseService.EnsureNotIssuedBySiagroAsync`:
"Na filial que emite NF-e pelo Siagro, a devolução de documento com romaneio ou carga ainda não é suportada."
A trava foi posta de propósito pela spec `2026-10-04-nfe-sales-return-issuance-design.md` §9.4 — a recusa
atual cria e CONFIRMA a devolução na hora, e na filial com NF-e a devolução só confirma com a NF-e de entrada
autorizada. O botão "Devolver" do documento de saída também não serve: recusa documento com carga. Resultado:
não há caminho para recusar carga faturada nessa filial.

## 2. Decisões do usuário

- **Quem emite:** a própria cerealista emite a **NF-e de entrada de devolução** (finalidade 4), referenciando a
  venda — procedimento padrão da CEAGUI (e de outras cerealistas) para recusa total ou parcial.
- **Fora do escopo:** a NF-e de devolução emitida pelo CLIENTE (que às vezes chega no fechamento do mês/contrato
  para a diferença de uma recusa parcial). Fica para uma fase seguinte.
- **Abordagem A — recusa em dois tempos:** a recusa registra devoluções Pendentes; os efeitos (saldo da carga,
  armazém, transbordo) só acontecem quando a NF-e de TODAS as devoluções for autorizada. Descartadas: "físico
  agora, fiscal depois" (exigiria as fórmulas de saldo contarem devolução pendente) e "emitir dentro da recusa"
  (não se desfaz NF-e autorizada num rollback; timeout da SEFAZ travaria a tela).
- **A carga fica travada** enquanto a recusa aguarda NF-e.

## 3. Escopo

Dentro:
- recusa de carga com NF-e própria de entrada, total ou parcial, nos três destinos (Refaturamento, Armazém,
  Transbordo), com um ou vários documentos;
- situação nova da carga e as travas correspondentes;
- cancelamento da recusa pendente;
- painel da recusa pendente na tela da carga, com emissão das NF-e por ali.

Fora:
- NF-e de devolução do cliente (§2);
- recusa presa por devolução DENEGADA ao lado de outra já autorizada: o caminho é cancelar a NF-e autorizada
  (cancelamento 2b) e então cancelar a recusa; sem tratamento automático;
- filial sem a regra ativa: **nada muda** — a recusa continua síncrona e não grava `SHIPMENT_LOAD_REFUSALS`.

## 4. Modelo

### 4.1 Tabela nova `SHIPMENT_LOAD_REFUSALS` (entidade `ShipmentLoadRefusal`, `AppDbContext`)

| Coluna | Tipo | Observação |
|---|---|---|
| `Key` | `uniqueidentifier` PK | |
| `ShipmentLoadKey` | `uniqueidentifier` NOT NULL | FK `SHIPMENT_LOADS`, índice |
| `Destination` | `int` NOT NULL | `RefusalDestination` |
| `DestinationWarehouseCode` / `DestinationWarehouseName` | `varchar` NULL | só Armazém/Transbordo |
| `Reason` | `varchar(500)` NOT NULL | |
| `Status` | `int` NOT NULL | enum novo `ShipmentLoadRefusalStatus`: `Pending = 0`, `Completed = 1`, `Cancelled = 2` |
| `CreatedAt/By`, `CompletedAt/By`, `CancelledAt/By` | | auditoria |

Invariante: no máximo UMA recusa `Pending` por carga (garantida pela trava de §6, já que a carga em
`RefusalPending` não aceita nova recusa; índice único filtrado `WHERE Status = 0` como segunda linha — ver a
armadilha de `QUOTED_IDENTIFIER` em índice filtrado).

### 4.2 `SALES_INVOICES.ShipmentLoadRefusalKey`

`uniqueidentifier NULL`, FK para `SHIPMENT_LOAD_REFUSALS` (`NoAction`), índice. Gravada só nas devoluções
criadas pela recusa diferida. Entra na lista de campos que o lock de NF-e restaura (não editável pelo PATCH).

### 4.3 Situação da carga

`ShipmentLoadStatus.RefusalPending = 9` (no fim do enum). `ShipmentLoadsRecalculateInvoicedService` continua
o único escritor: `RecalculateAsync` passa a consultar "há recusa `Pending` desta carga" e `ResolveStatus`
ganha esse parâmetro, avaliado **antes** de todos os outros (antes de `InTransshipment`). Não há colisão: a
recusa para Transbordo já exige que não haja transbordo aberto. Texto na tela: "Recusa aguardando NF-e".

### 4.4 Movimento

`ShipmentLoadMovementType.RefusalCancelled = 25` (append). O registro usa o `Refused = 12` existente, com
narrativa "aguardando NF-e de entrada"; a conclusão gera os movimentos que já existem (`Returned` por
documento via `ShipmentLoadsBalanceHookService`, `ReturnedToWarehouse` / `TransshipmentStarted`).

## 5. Fluxo

### 5.1 Registrar (`ShipmentLoadsRefuseService`, filial com a regra ativa)

`EnsureNotIssuedBySiagroAsync` deixa de recusar e passa a **decidir o modo**: se a filial da carga tem a regra
ativa, o fluxo é diferido; senão, o síncrono de hoje. Com a regra ativa, o modo sai dos **documentos recusados**
(depois de `ResolveLinesAsync`): todos `TaxDocumentKind.Nfe` → diferido; todos `TaxDocumentKind.Other`
(papel/talão, que nunca teve NF-e pelo Siagro) → síncrono, como antes desta spec (a devolução síncrona não é
`IsNfeReturn` e passa pela guarda da confirmação); a mistura é recusada: "Recuse separadamente os documentos com
NF-e e os documentos de outro tipo." No modo diferido:

1. Validação de hoje (`Validate`, `ResolveLinesAsync`, transbordo) e mais, por documento recusado:
   - `NfeStatus = Authorized` com chave de 44 posições (NF-e cancelada mantém a mensagem de `NfeLockRules`;
     documento confirmado e não transmitido: "O documento X não tem NF-e autorizada: transmita a NF-e ou
     cancele o documento.");
   - natureza de devolução cadastrada para cada item e numeração de itens da NF-e de venda — as mesmas
     validações do "Devolver".
2. Numa transação: grava a `ShipmentLoadRefusal` `Pending`; para cada documento cria a devolução **Pendente,
   `IsNfeReturn = true`**, com `ShipmentLoadRefusalKey`, pelo **construtor comum** extraído de
   `SalesInvoicesNfeReturnCreateService` (data de hoje em Brasília, sem condição de pagamento, sem local de
   entrega, volume da venda, natureza de devolução por item, `Comments` com "Recusa da carga X" e o motivo).
   A regra "documento com carga não" do `ValidateOriginAsync` continua valendo para o "Devolver"; o
   construtor comum não a aplica. Registra o movimento `Refused`; recalcula a carga (→ `RefusalPending`).
3. NÃO confirma nada, NÃO mexe no saldo, NÃO cria romaneio, liberação nem transbordo.

A devolução pendente já é subtraída do devolvível em `ShipmentLoadsRefusableDocumentsService` (soma
devoluções `!= Cancelled`) — nada a mudar ali.

### 5.2 Emitir

Cada devolução é emitida pelo fluxo existente (`SalesInvoicesIssueNfe`, `SalesInvoicesNfeIssueService`; a
pré-checagem de saldo devolvível e de peso já vale para `IsNfeReturn`). A autorização confirma a devolução
(`SalesInvoiceNfeResultHandler` → `SalesInvoicesConfirmService`, `CommitMode.Auto`), que devolve o saldo do
documento à carga pelo hook de saldo, como hoje.

### 5.3 Concluir (automático, na confirmação)

Serviço novo `ShipmentLoadRefusalCompleteService`, chamado por `SalesInvoicesConfirmService` no ramo de
devolução, **depois** do hook de saldo da carga e dentro da mesma transação, quando a devolução tem
`ShipmentLoadRefusalKey`:

- se ainda houver devolução da recusa não confirmada (e não cancelada), não faz nada;
- senão executa os efeitos do destino, com a quantidade = soma das devoluções da recusa:
  - Armazém: romaneio `SalesShipmentReturn` confirmado + liberações + movimento;
  - Transbordo: rechecagem só de `EnsureIsLastAsync` (a carga está em `RefusalPending`, e
    `EnsureLoadAcceptsTransshipment` a recusaria) e abertura do transbordo;
  - Refaturamento: nada além do saldo;
- marca a recusa `Completed` e recalcula a carga (sai de `RefusalPending`).

Os efeitos de Armazém e Transbordo saem de `ShipmentLoadsRefuseService` (`ReturnToWarehouseAsync`,
`EmitReturnReleasesAsync`, `OpenTransshipmentAsync`) para um serviço compartilhado
`ShipmentLoadRefusalEffectsService`, usado pelo fluxo síncrono e pela conclusão — sem duplicar as três chaves
proibidas no romaneio 12. Todos os serviços internos em `CommitMode.Deferred`.

Falha na conclusão desfaz a confirmação inteira; `NfeResultHandlerBase` mantém a NF-e `Authorized`, grava
`NfeConfirmationError` e o "Concluir confirmação" refaz tudo. Isso é o comportamento existente.

Ordem dentro do recálculo: enquanto a recusa estiver `Pending`, a confirmação da 1ª devolução recalcula a
carga e ela continua `RefusalPending` (o saldo já reflete aquele documento, mas as travas seguem).

**Cancelamento da NF-e de uma devolução da recusa pendente (2b).** A conclusão também é chamável pela chave da
recusa (`ShipmentLoadRefusalCompleteService.TryCompleteAsync`), e `SalesInvoicesCancelService` a chama no
cancelamento pós-SEFAZ (`CancelAfterNfeAsync`), na mesma transação, depois de gravar o documento como cancelado.
Com a recusa `Pending`, considerando só as devoluções não canceladas:
- nenhuma viva → a recusa vira `Cancelled` (`CancelledAt`/`CancelledBy`), a carga é recalculada e registra-se
  `RefusalCancelled`;
- todas as vivas confirmadas → conclui como acima, com a quantidade das confirmadas;
- senão, nada.

**Recusa concluída não se desfaz pelo 2b.** Cancelar a NF-e de uma devolução cuja recusa está `Completed`
deixaria para trás a entrada no armazém, as liberações e o transbordo: é barrado antes da SEFAZ
(`EnsureCanCancelAsync`) e no cancelamento do documento — "Esta devolução concluiu a recusa da carga X: a NF-e
não pode ser cancelada pelo Siagro." (`SalesInvoicesRefusalLink`).

O painel da recusa pendente (`ShipmentLoadsPendingRefusalService`) não lista devoluções canceladas.

### 5.4 Cancelar recusa

Serviço/action novos `ShipmentLoadsCancelRefusal(Key)`:
- recusa `Pending` obrigatória;
- recusado se alguma devolução da recusa estiver `Authorized` ou `Processing`: "A NF-e de entrada do documento
  X já foi autorizada ou está em processamento: cancele a NF-e ou aguarde o retorno antes de cancelar a recusa.";
- cancela as devoluções pendentes pelo `SalesInvoicesCancelService` em modo diferido (acrescentar `CommitMode`
  se ainda não houver — ver a armadilha de `CommitAsync` não aninhável), marca a recusa `Cancelled`, registra
  `RefusalCancelled`, recalcula a carga.

Cancelar ou excluir, pela tela/API do documento, uma devolução ligada a recusa `Pending` é recusado:
"Esta devolução pertence à recusa da carga X: cancele a recusa na Montagem de Carga." (em
`SalesInvoicesCancelService` e `SalesInvoicesDeleteService`, exceto quando chamado pelo cancelamento da recusa).

## 6. Travas com a carga em `RefusalPending`

| Operação | Onde |
|---|---|
| Faturar | `ShipmentLoadsBillingGuardService.EnsureCanBillAsync` |
| Nova recusa | `ShipmentLoadsRefuseService.Validate` |
| Ticket de descarga (criar/alterar/excluir) | `ShipmentLoadDischargeRules.EnsureLoadAcceptsChanges` |
| Transbordo (iniciar, entrada, saída do lote) | `ShipmentLoadTransshipmentRules.EnsureLoadAcceptsTransshipment` |
| Estornar transbordo | `ShipmentLoadsTransshipmentReverseService` (`ShipmentLoadRefusalRules.EnsureNoPendingRefusal`) |
| Alterar campos fiscais | `ShipmentLoadsUpdateService` (lista de situações com campos fiscais travados) |

Mensagem comum: "A carga X tem uma recusa aguardando NF-e de entrada: emita as NF-e ou cancele a recusa."
Expedir, concluir, reabrir, anexar romaneio, cancelar e desvincular já ficam barrados pelas regras atuais
(situação fora da lista aceita, ou carga com faturamento) — conferir cada um nos testes.

## 7. Telas (frontend)

| Onde | Mudança |
|---|---|
| `view/shipmentLoads/fragments/Refusal.fragment.xml` + `Detail.controller.ts` | Diálogo igual; com a regra ativa o texto de confirmação avisa que a recusa conclui após as NF-e de entrada. Após registrar, fecha e recarrega a carga. |
| `view/shipmentLoads/Detail.view.xml` | Painel "Recusa aguardando NF-e" (visível com recusa pendente): destino, armazém, motivo; grade das devoluções — Documento (link para o detalhe), Cliente, Quantidade, Situação da NF-e, Número/Série, erro de confirmação; por linha **Emitir NF-e** / **Consultar situação** / **Concluir confirmação** conforme o estado. Botão **Cancelar recusa** no cabeçalho. Texto da situação `RefusalPending`. |
| `helpers/NfeHelpers.ts` | `runNfeAction` sai do `salesInvoices/Detail.controller.ts` para o helper, usado pelas duas telas. |
| `model/ServerRoutes.ts` | `shipmentLoadsCancelRefusal`; rota da function nova se for chamada por fetch. |

Backend para a tela: function `ShipmentLoadsGetPendingRefusal(Key)` (recusa pendente + devoluções com
`InvoiceNumber`, `CardName`, quantidade, `NfeStatus`, `TaxDocumentNumber`, `TaxDocumentSeries`,
`NfeConfirmationError`, `InvoiceStatus`) e action `ShipmentLoadsCancelRefusal`. `ShipmentLoadsRefuse` mantém o
contrato de entrada e acrescenta `RefusalKey` (nulo no modo síncrono) à resposta.

## 8. Migration

Uma, aditiva, no `AppDbContext`: `AddShipmentLoadRefusals` — tabela, FK + índice em `SALES_INVOICES`, índice
único filtrado. Sem backfill. Aplicar no CEAGUI_SIAGRO_DEV para a verificação; demais bancos no deploy.

## 9. Testes e verificação

TDD com o vermelho visto antes do verde, em `SiagroB1.Application.Tests`:
- registro (regra ativa): cria recusa `Pending` e uma devolução `IsNfeReturn` Pendente por documento, ligadas;
  carga `RefusalPending`; saldo intacto; nenhum romaneio/liberação/transbordo; recusas de validação (origem
  confirmada sem NF-e, NF-e cancelada, sem natureza de devolução, sem numeração de itens);
- `ResolveStatus`: recusa pendente vence as demais situações;
- travas de §6, uma por operação;
- conclusão: 1ª de 2 devoluções confirmada não conclui; a 2ª conclui — um teste por destino (romaneio 12 sem as
  três chaves proibidas + liberações; transbordo aberto com a quantidade recusada; refaturamento com a carga de
  volta ao faturamento); falha nos efeitos desfaz a confirmação;
- cancelamento: com devoluções pendentes/rejeitadas cancela tudo e a carga volta à situação anterior; com uma
  autorizada é recusado; cancelar/excluir a devolução avulsa ligada é recusado;
- regressão: sem a regra ativa, os testes atuais da recusa síncrona seguem verdes.

Frontend: `ts-typecheck` e `lint`.

E2E no CEAGUI_SIAGRO_DEV (homologação): carga com dois documentos autorizados; recusa parcial para Armazém;
emitir a 1ª NF-e (carga continua travada), a 2ª (romaneio 12, liberações, saldo); outra recusa cancelada antes
de emitir.
