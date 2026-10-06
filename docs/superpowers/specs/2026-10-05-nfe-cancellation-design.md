# Cancelamento de NF-e — Documento de Saída e Documento de Entrada (NF-e STANDALONE) — design

Data: 05/10/2026. Branch `feature/nfe-cancellation` nos dois repos, criado de `main` (que já contém a cadeia
nfe-standalone-issuance → nfe-purchase-invoice-issuance → nfe-third-party-purchase-return).
Base: spec `2026-10-02-nfe-standalone-issuance-design.md`, cuja D1 reservou o cancelamento para o sub-projeto
**2b** ("cancelamento, CC-e, inutilização, e-mail"). Este spec cobre **só o cancelamento**.

## 1. O pedido

"Criar serviço para cancelamento de NF-e, doc. de entrada e saída." Hoje os dois serviços de cancelamento recusam
documento com NF-e autorizada ("o cancelamento precisa ser feito na SEFAZ, recurso da próxima etapa"). O
cancelamento é o evento **110111** da SEFAZ, com justificativa, enviado sobre uma NF-e autorizada.

O usuário condicionou o desenho à escrituração futura: "como saberemos as NF-e's que foram canceladas? O ideal
seria fazermos uma escrituração fiscal desses documentos, até para geração futura de sped fiscal, envio de reinf".

Sucesso: na filial que emite NF-e, uma saída ou entrada própria autorizada (inclusive devoluções) é cancelada na
SEFAZ pela tela, com justificativa; o documento fica cancelado com os estornos de hoje; o banco guarda tudo o que o
SPED (C100 com `COD_SIT = 02`) e a EFD-Reinf (exclusão da nota) vão precisar; as listas mostram e exportam as
canceladas.

## 2. Decisões (do usuário, em 05/10)

| # | Decisão | Motivo |
|---|---|---|
| D1 | **Cancelar a NF-e cancela o documento.** Com o evento registrado, o documento vai a `Cancelled` com os mesmos estornos do cancelamento comum de hoje (saldo, alocação no contrato, carga, liberação, origem da devolução). Número e chave ficam queimados. | Nota cancelada não tem efeito; não existe "NF-e cancelada com documento ativo". Reemitir é criar outro documento. |
| D2 | **O documento cancelado continua no banco** com status, protocolo, data, justificativa, autor e o XML do evento (`procEventoNFe`). | É o que a escrituração (SPED C100 `COD_SIT 02`, Reinf) e a guarda legal do XML pedem. |
| D3 | **Listas:** filtro "Situação NF-e = Cancelada" e coluna "Cancelada em" (com exportação Excel), na saída e na entrada. | Ver e exportar as canceladas sem relatório novo. |
| D4 | **Fora:** escrituração fiscal (livros, apuração, SPED, Reinf) e relatórios (FastReport) — sub-projetos próprios, depois. CC-e, inutilização e e-mail continuam no 2b, fora deste spec. | Escopo. |
| D5 | **Abordagem A:** ação própria `*CancelNfe` atrás do botão "Cancelar", serviço genérico `NfeCancelServiceBase<T>` no molde da emissão, em **duas fases** (fiscal → efeitos locais) com "Concluir cancelamento" para retomar a fase local. | Nunca deixa nota cancelada na SEFAZ e documento ativo sem aviso; não mistura chamada externa com a transação do estorno. |
| D6 | **Fechar a brecha do "Estornar":** Confirmado → Pendente passa a ser recusado com NF-e em processamento, autorizada ou cancelada, na saída e na entrada. | Hoje dá para desfazer saldo e alocação de uma nota válida na SEFAZ. |

Decisões técnicas propostas e aceitas junto com o desenho:

| # | Decisão |
|---|---|
| T1 | Sem regra local de prazo (24 h): a SEFAZ recusa (501 etc.) e a mensagem dela vai para a tela, como na emissão. |
| T2 | Devoluções próprias (de venda e de compra) cancelam pelo mesmo caminho; a restauração da origem que já existe roda na fase local. |
| T3 | Entrada **de terceiro** fica como hoje: a NF-e é do fornecedor; o cancelamento é só do documento local. |
| T4 | A entrada ganha a trava que a saída já tem: não cancela origem com devolução não cancelada. |
| T5 | Índices de `ChaveNFe`/número **não mudam**: a NF-e própria tira o número da reserva (`NextNumber` só cresce), então número e chave nunca são reaproveitados mesmo com o documento cancelado. O SQL Server também não aceita `OR` em filtro de índice. |
| T6 | Sem trava de perfil nova — segue a emissão; "perfil nas ações" continua pendência antes da VPS. |
| T7 | Sequência do evento sempre 1 (cancelamento é único por NF-e); `idLote` = 1. |

## 3. Escopo

**Dentro:** evento de cancelamento no cliente Fiscal; serviço genérico + subclasses de saída e entrada própria;
gravação fiscal (status, protocolo, data, justificativa, XML do evento); fase local reaproveitando os serviços de
cancelamento existentes; "Concluir cancelamento"; consulta reconhecendo cStat 101; trava do Estornar; `NfeLock` com
`Cancelled`; diálogo de justificativa, botões, painel NF-e, XML do cancelamento, DANFE com tarja; filtro e coluna nas
listas.

**Fora:** D4; cancelamento por substituição (110112, só NFC-e); cancelamento extemporâneo fora do evento (pedido
administrativo na SEFAZ); qualquer efeito financeiro (os cancelamentos de hoje não tocam título — nada muda).

## 4. Isolamento

Só documentos com `NfeStatus = Authorized` entram no fluxo novo — isso só existe na filial que emite NF-e pelo
Siagro (CEAGUI). Yokotobi (SAPB1) e MH Agro não têm documento autorizado: o "Cancelar" deles segue exatamente o
caminho de hoje. A trava nova do Estornar (D6) também só morde documentos com NF-e emitida.

## 5. Modelo de dados

### 5.1 Enums

- `NfeStatus` ganha `Cancelled = 5` (tela: "Cancelada").
- `NfeXmlKind` ganha `CancellationEvent = 4`: o `procEventoNFe` (evento assinado + `retEvento`), gravado nas
  tabelas `SALES_INVOICE_NFE_XMLS` / `PURCHASE_INVOICE_NFE_XMLS`, ao lado do `Authorized`.

### 5.2 Colunas novas (`SALES_INVOICES` e `PURCHASE_INVOICES`, via `INfeDocument`)

| Coluna | Tipo | Conteúdo |
|---|---|---|
| `NfeCancellationProtocol` | VARCHAR(15) NULL | `nProt` do evento |
| `NfeCancelledAt` | DATETIME2 NULL | `dhRegEvento` devolvido pela SEFAZ |
| `NfeCancellationReason` | NVARCHAR(255) NULL | justificativa (`xJust`), 15–255 caracteres após trim |
| `NfeCancellationError` | NVARCHAR(500) NULL | erro da fase local; não nulo = "Concluir cancelamento" disponível |

`CanceledAt`/`CanceledBy` (já no `BaseEntity`) registram quem e quando cancelou o documento; a entrada já os
preenche, a saída passa a preencher.

`RestoreIssuanceFields` (nos dois `NfeLock`) passa a restaurar também as quatro colunas novas: nenhum PATCH as
escreve.

### 5.3 Combinações de estado

| `NfeStatus` | `InvoiceStatus` | `NfeCancellationError` | Significado |
|---|---|---|---|
| `Authorized` | Pending/Confirmed | — | pode cancelar |
| `Cancelled` | Cancelled | NULL | cancelamento completo |
| `Cancelled` | Pending/Confirmed | preenchido | SEFAZ cancelou, estorno local pendente → "Concluir cancelamento"; documento inteiro travado |

## 6. Fiscal (`SiagroB1.Fiscal/Nfe`)

- `INfeSefazClient.CancelAsync(NfeServiceSettings, NfeCancelRequest, CancellationToken)`.
  `NfeCancelRequest(AccessKey, AuthorizationProtocol, Justification, IssuerDocument, EventAt)` —
  `EventAt` é `DateTimeOffset` em Brasília (−03:00), convertido do relógio do servidor como na N3 do 2a.
- Retorno `NfeEventResult(StatusCode, Reason, Protocol, RegisteredAt, ProcEventXml)`. O status é o do
  `retEvento.infEvento` (não o do lote, que vem 128 quando processado); `ProcEventXml` vem de
  `RetornoRecepcaoEvento.ProcEventosNFe` serializado.
- `ZeusNfeSefazClient` chama `ServicosNFe.RecepcaoEventoCancelamento(1, 1, protocolo, chave, justificativa, cnpj,
  dhEvento)` pelo `RunAsync` existente (configuração por chamada, timeout, `NfeCommunicationException`).
- `NfeStatusCodes` ganha: `EventRegistered = 135`, `EventRegisteredOutOfTime = 155`
  (`IsCancellationRegistered`), `DuplicateEvent = 573`, `Cancelled = 101` (já citado no comentário do mapper).
- `ConsultProtocolAsync` passa a devolver também o `procEventoNFe` de cancelamento (`tpEvento 110111`) quando o
  `retConsSitNFe` o trouxer, com `nProt` e `dhRegEvento`, para o §7.4.

## 7. Application

### 7.1 Divisão dos serviços de cancelamento existentes

`SalesInvoicesCancelService` e `PurchaseInvoicesCancelService` são divididos internamente, sem mudar o
comportamento do cancelamento comum:

- `EnsureCanCancelAsync(doc)` — só as checagens de negócio (já cancelado; retorno confirmado; tem devolução
  ativa). Não inclui a trava de NF-e.
- `CancelCoreAsync(doc, userName)` — os efeitos, na transação de sempre, mais `CanceledAt/By`.
- `ExecuteAsync` (ação comum) = `NfeLock.EnsureCancellable` + `EnsureCanCancelAsync` + `CancelCoreAsync`.

Ajustes de regra:
- Saída: a checagem "Documento do tipo retorno já está confirmado" não se aplica a `IsNfeReturn` (devolução com
  NF-e só sai pelo cancelamento da NF-e). A saída passa a gravar `CanceledAt/By`.
- Entrada: nova checagem "Documento de entrada possui devolução." — devolução (`IsNfeReturn`, não cancelada)
  apontando para ele por `PurchaseInvoiceOriginKey` (T4).
- `EnsureCancellable` (comum) com `Authorized`: "NF-e autorizada: cancele pela SEFAZ informando a justificativa.";
  com `Cancelled` e documento ativo: "NF-e já cancelada na SEFAZ: use Concluir cancelamento."

### 7.2 `NfeCancelServiceBase<T>` (+ `SalesInvoicesNfeCancelService`, `PurchaseInvoicesNfeCancelService`)

1. Justificativa: trim, 15–255 caracteres ("A justificativa deve ter entre 15 e 255 caracteres.").
2. Trava `sp_getapplock` `nfe:{key}` (`AcquireEmissionLockAsync`), a mesma da emissão e da consulta.
3. Pré-condições: `NfeStatus = Authorized`, chave de 44 dígitos e protocolo presentes; documento não cancelado;
   entrada com emissão **Própria** (terceiro: "NF-e de terceiro não é cancelada pelo Siagro.").
4. **Ensaio local:** `EnsureCanCancelAsync(doc)` — se recusar, a SEFAZ não é chamada.
5. Abre o certificado com `BranchNfeSettingsService.OpenAsync(branch, doc.NfeEnvironment)` e chama `CancelAsync`.
6. Resultado:
   - **135/155** → fase 1 (§7.3) e fase 2.
   - **573** → consulta (§7.4); se ela confirmar o cancelamento (101 com evento), fase 1 e fase 2.
   - **Outro** → nada muda no documento; a resposta leva cStat e motivo ("Rejeição 501: …").
   - **`NfeCommunicationException`** → nada muda; mensagem "Sem resposta da SEFAZ; consulte a situação antes de
     tentar de novo." (a consulta resolve se o evento entrou).
7. Retorno: `NfeIssueOutcomeDto` (mesmo DTO da emissão; o frontend usa `nfeOutcomeMessage`).

### 7.3 Fase 1 (fiscal) e fase 2 (local)

- **Fase 1:** `AddXml(doc, CancellationEvent, procEventoNFe)`; `NfeStatus = Cancelled`;
  `NfeCancellationProtocol`, `NfeCancelledAt`, `NfeCancellationReason`; `NfeStatusCode/Reason` com o cStat e o motivo
  do evento; `SaveChanges`. Fica gravada mesmo que a fase 2 falhe.
- **Fase 2:** `CancelCoreAsync(doc, userName)`. Sucesso → `NfeCancellationError = NULL`. Exceção → mensagem em
  `NfeCancellationError` (500 caracteres), `SaveChanges`, e a resposta diz "NF-e cancelada na SEFAZ; o estorno
  local falhou: … Use Concluir cancelamento." A fase 2 **não** roda o ensaio de novo para recusar: se uma regra
  local mudou entre as fases, o erro aparece aqui e o usuário resolve e conclui.

### 7.4 Consulta reconhece o cancelamento

`NfeConsultServiceBase`: documento com `NfeStatus = Authorized` (ou `Cancelled` com documento ativo) e consulta com
cStat **101** → fase 1 com o `procEventoNFe` da resposta (sem ele: só status, código e motivo; protocolo e data
ficam nulos e a justificativa recebe "Cancelada fora do Siagro") e fase 2. É o caminho para timeout no envio e
para nota cancelada por fora.

### 7.5 "Concluir cancelamento"

`*NfeCompleteCancellationService`: exige `NfeStatus = Cancelled` e documento não cancelado; roda só a fase 2 sob
a trava `nfe:{key}`.

### 7.6 Travas

- `SalesInvoicesReverseConfirmService` e `PurchaseInvoicesReverseConfirmService` (D6): recusam `Processing`,
  `Authorized` e `Cancelled` — "Documento com NF-e emitida não pode ser estornado; use o cancelamento."
- `SalesInvoiceNfeLock`/`PurchaseInvoiceNfeLock`: `Cancelled` congela como `Authorized` (cabeçalho, linhas, campos
  fiscais) e `EnsureDeletable` recusa.
- Guardas existentes que testam `Authorized` (devolução, recusa de carga, aplicar tributação, itens,
  `NfeCompleteConfirmation`) continuam recusando `Cancelled` porque testam igualdade com `Authorized` — conferir
  cada um no plano.
- Saldo de devolução (`*NfeReturnBalance`) já ignora devoluções `Cancelled` — nada a fazer.

## 8. Web (OData)

Ações no padrão de `Web/Actions/Nfe/` (`DefaultException` → 400, `NotFoundException` → 404, 200 com
`NfeIssueOutcomeDto`):

- `SalesInvoicesCancelNfe(Key, Justification)` / `PurchaseInvoicesCancelNfe(Key, Justification)`.
- `SalesInvoicesCompleteNfeCancellation(Key)` / `PurchaseInvoicesCompleteNfeCancellation(Key)`.
- Download de XML (`*NfeXmlController`): parâmetro de tipo (`Authorized` padrão, `CancellationEvent`); arquivo
  `{chave}-procEventoNFe.xml`.

Reports: `DanfeReportService` passa `documentoCancelado = true` quando `NfeStatus = Cancelled`.

## 9. Telas (frontend)

- **`webapp/dialogs/NfeCancelDialog.ts`** (compartilhado): `TextArea` com contador, 15–255 caracteres; botão
  "Cancelar NF-e" habilitado a partir de 15; aviso "O cancelamento é enviado à SEFAZ e não pode ser desfeito. O
  documento será cancelado e os saldos estornados."
- **Detalhe da saída e da entrada:**
  - "Cancelar": NF-e autorizada → diálogo → `*CancelNfe`; sem NF-e → confirmação de hoje.
  - "Concluir cancelamento": visível com `NfeStatus = Cancelled` e documento ativo.
  - "Estornar": desabilitado com NF-e em processamento, autorizada ou cancelada.
  - Painel NF-e: protocolo, data e justificativa do cancelamento; erro da fase 2.
  - "XML do cancelamento": visível com `NfeStatus = Cancelled`.
- **Listas:** "Cancelar Doc." usa o diálogo quando a NF-e está autorizada; filtro "Situação NF-e" ganha
  "Cancelada" (na entrada, coluna e filtro de Situação NF-e são novos); coluna "Cancelada em" (exportada no Excel).
- `formatter`: `Cancelled` → "Cancelada", estado Error. `ServerRoutes`: rotas novas.

## 10. Migration

Uma migration aditiva no App (`AddNfeCancellation`): as quatro colunas em `SALES_INVOICES` e `PURCHASE_INVOICES`.
Enums não mudam esquema. Aplicar no `CEAGUI_SIAGRO_DEV`; entra na fila de migrations do deploy do `main`.

## 11. Testes e verificação

TDD com RED visto. Gates: build 0 erros, `SiagroB1.Application.Tests`, `SiagroB1.Fiscal.Tests`, `tsc`, `lint`,
ui5lint sem regressão.

- **Fiscal:** mapeamento do retorno do evento (135, 155, 573, recusa; status do `retEvento`, não do lote);
  serialização do `procEventoNFe`; consulta 101 com e sem evento.
- **Application** (xUnit + InMemory, `INfeSefazClient` falso), saída e entrada:
  justificativa curta/longa; pré-condições (não autorizada, terceiro, já cancelada); ensaio recusando **sem
  chamar a SEFAZ** (saída com devolução, entrada com devolução); sucesso (fase 1 + 2, XML gravado, estornos de
  hoje acontecendo, `CanceledAt/By`); fase 2 falhando (erro gravado, NfeStatus Cancelled, documento ativo) e
  "Concluir"; recusa da SEFAZ sem mudança; falha de comunicação sem mudança; 573 → consulta; consulta 101;
  devolução própria de venda cancelada restaurando a origem; devolução de compra cancelada; Estornar recusado
  (3 status × 2 documentos); `NfeLock` com `Cancelled`; PATCH não escreve os campos novos; cancelamento comum
  inalterado sem NF-e.
- **E2E em homologação** (`CEAGUI_SIAGRO_DEV`, Playwright, pelo caminho do usuário): emitir saída nova e
  cancelar; cancelar entrada própria; cancelar devolução de venda e ver a origem voltar a Confirmada; conferir no
  banco os dois status, protocolo, XML do evento; DANFE com tarja; filtro e coluna nas listas. Cada envio à SEFAZ
  de homologação pede confirmação do usuário antes. Prazo de 24 h: notas autorizadas antes disso são reemitidas
  para o teste.

## 12. Riscos

- **Timeout no envio** com o evento registrado na SEFAZ: o documento fica `Authorized` localmente até a consulta
  (que faz as duas fases). A mensagem de falha de comunicação manda consultar antes de repetir; repetir também
  funciona (573 → consulta).
- **Regra local que muda entre o ensaio e a fase 2** (ex.: devolução criada no meio): a fase 2 falha, fica
  registrada e o "Concluir" resolve depois de o usuário tratar a causa. A trava `nfe:{key}` reduz a janela.
- **Fora do prazo:** SP recusa cancelamento após 24 h (501). Sem tratamento além da mensagem (T1).
- **Consulta sem o `procEventoNFe`:** escrituração fica sem protocolo do evento para essa nota; o XML pode ser
  obtido depois por distribuição DF-e (fora daqui).

## 13. Próximas etapas (fora daqui)

CC-e e inutilização (resto do 2b); e-mail; escrituração fiscal (livros, SPED Fiscal com `COD_SIT 02`, EFD-Reinf);
relatório de NF-e canceladas; perfil nas ações de NF-e.
