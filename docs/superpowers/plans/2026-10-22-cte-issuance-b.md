# Emissão de CT-e — entrega B (averbação, título, e-mail, lote) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Completar o CT-e da entrega A com:
- averbação manual na AT&M e cancelamento automático da averbação;
- título a receber contra o tomador;
- e-mail automático com XML e DACTE;
- transmissão e averbação em lote;
- download de XML/DACTE em lote.

**Architecture:**
- **Pontos de entrada:** dois ganchos no ciclo da entrega A.
  - Autorização: `ICteAuthorizationHook`, novo, chamado onde o CT-e vira Authorized.
  - Cancelamento: `ICteCancellationHook`, já existente e hoje no-op.
- **O que pluga nos ganchos:** título, e-mail e cancelamento da averbação.
- **Reaproveitamento:**
  - AT&M: o `AverbaClient` do sub-projeto 2 ganha o envio do XML.
  - Título: modelo de `FinancialDocumentsSalesNoContractService`, que gera parcelas pela condição de pagamento.
  - E-mail: a fila `NfeEmailMessage` ganha o tipo CT-e.
  - Lote: jobs do Hangfire, como o `NfeEmailDispatchJob`.

**Tech Stack:** .NET 10, EF Core/SQL Server, OData v4, Hangfire, FastReport.OpenSource, xUnit; OpenUI5 1.141 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-22-cte-issuance-design.md` (objetivos 3–5, "Ciclo de vida", "Título a receber", decisões 2, 6 e 7).

**Worktrees:** os mesmos da entrega A, na branch `feature/cte-issuance`:
- backend `C:\Projetos\SiagroB1\siagro-b1-backend-cte-issuance`;
- frontend `C:\Projetos\SiagroB1\siagro-b1-frontend-cte-issuance`.

## Global Constraints

- **Modo:** somente STANDALONE. A NF-e não muda; o e-mail da NF-e continua idêntico.
- **Averbação:**
  - **manual** (decisão do usuário: nem todo CT-e é averbado), só de CT-e Authorized, nunca duas vezes;
  - credenciais e ambiente de `BranchTransportSettings` (`AverbaEnabled`, usuário, senha cifrada, código, ambiente).
- **AT&M:**
  - envio do CT-e: `POST {base}/rest/CTE`, com `Authorization: Bearer <token do /rest/Auth>`;
  - `Content-Type` **exatamente** `application/xml`, sem charset (lição da entrega A), e `Accept: application/json`.
- **Resposta da AT&M** (`/rest/CTE`):
  - `Averbado.dhAverbacao` e `Averbado.Protocolo`;
  - `Averbado.DadosSeguro[]`: `NumeroAverbacao`, `CNPJSeguradora`, `NomeSeguradora`, `NumApolice`, `ValorAverbado`, `RamoAverbado`, `TpMov`;
  - `Erros[]`: `Codigo`, `Descricao`, uma lista sem o envelope `Erro`;
  - `Infos[]`.
- **Cancelamento da averbação:**
  - ao cancelar um CT-e averbado, envia o `procEventoCTe` de cancelamento ao mesmo `/rest/CTE`;
  - uma falha **não** desfaz o cancelamento na SEFAZ: a averbação fica com situação "Cancelamento pendente" e botão "Reenviar cancelamento da averbação".
- **Título a receber:**
  - nasce na **autorização**, contra o **tomador**, com valor = `AmountToReceive`;
  - parcelas pela condição de pagamento (`PaymentConditionCode` do documento, que vem do cadastro do tomador e pode ser editada);
  - herda as dimensões do documento;
  - só se a natureza tiver `CreatesFinancialDocument`;
  - o cancelamento do CT-e cancela o título. O cancelamento é **recusado antes da SEFAZ** se o título tiver baixa.
- **Origem nova:** `FinancialDocumentOrigin.TransportDocument = 11`, com `OriginKey` = chave do documento, fora do índice `FirmOrigin`, como `SalesInvoiceNoContract`.
- **E-mail:**
  - automático na autorização e no cancelamento, com XML + DACTE em PDF;
  - destinatários: e-mails NF-e cadastrados do **tomador, remetente e destinatário**, sem repetidos;
  - vale a chave `Email:Enabled` existente.
- **Lote:**
  - transmissão e averbação de vários CT-e por **job do Hangfire**, um por vez;
  - o resultado fica no próprio documento (situação/motivo e situação da averbação);
  - clique em um CT-e só continua síncrono.
- **Textos:** pt-BR; identificadores em inglês. Migrations aditivas em todas as empresas, com trailer `DB:`. Nunca push.

## Review Focus

1. **Cancelar CT-e com título baixado:** recusa **antes** de chamar a SEFAZ, com mensagem, sem nada alterado. Teste na Tarefa 4.
2. **Falha da AT&M no cancelamento da averbação:** o CT-e continua Cancelled e o título cancelado; a averbação fica pendente e pode ser reenviada. Teste na Tarefa 3.
3. **Transmissão em lote com um CT-e que falha:** os outros continuam; nada fica preso em "Processing" sem motivo. Teste na Tarefa 6.
4. **Tomador sem e-mail cadastrado:** o e-mail vai para os outros dois e não lança exceção. Sem nenhum destinatário, a fila registra "sem destinatário" como a NF-e. Teste na Tarefa 5.
5. **Averbar duas vezes em paralelo**, por clique duplo ou lote junto com manual: só uma averbação, com a trava de emissão do documento. Teste na Tarefa 2.

---

### Task 1: AverbaClient — envio do XML do CT-e

**Files:**
- Modify: `SiagroB1.Application/Services/Transport/AverbaClient.cs` (+ `IAverbaClient`)
- Test: `SiagroB1.Application.Tests/Transport/AverbaClientTests.cs`

**Interfaces:**
- Produces:
  - `Task<AverbaCteResult> SendCteAsync(NfeEnvironment env, string bearer, string xml, CancellationToken ct)`;
  - `record AverbaCteResult(bool Success, string? Protocol, string? AverbadoAt, IReadOnlyList<AverbaInsurance> Insurances, string? Error)`;
  - `record AverbaInsurance(string? EndorsementNumber, string? InsurerTaxId, string? InsurerName, string? PolicyNumber, string? Amount, string? Branch, string? MovementType)`.
  - Mapeamento dos campos de `AverbaInsurance`: `NumeroAverbacao`, `CNPJSeguradora`, `NomeSeguradora`, `NumApolice`, `ValorAverbado`, `RamoAverbado`, `TpMov`.
  - Erros: descrições de `Erros[]` unidas por "; ". HTTP de erro com corpo JSON lê os `Erros`.
  - Sucesso = `Averbado.Protocolo` preenchido.
  - O login continua em `LoginAsync`. **Não há cache de token**: cada averbação faz login (o volume é baixo).

- [ ] **Step 1: Testes que falham** (`StubHttpMessageHandler`):
  - `SendCte_posts_xml_with_bearer_and_exact_content_type`: a URL é `{base}/rest/CTE`, `Authorization: Bearer tok`, `Content-Type` = `"application/xml"` exato e `Accept` json.
  - `SendCte_success_reads_protocol_and_all_insurances` (2 seguros na resposta).
  - `SendCte_errors_list_is_joined`.
  - `SendCte_http_400_with_errors_reads_description`.
  - `SendCte_network_failure_is_reported`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** commit `feat(invoice): envio do CT-e para a AT&M (averbação)`.

### Task 2: Averbação manual do CT-e

**Files:**
- Create: `SiagroB1.Domain/Entities/TransportDocumentEndorsement.cs`, `SiagroB1.Domain/Enums/CteEndorsementStatus.cs`
- Modify: `SiagroB1.Domain/Entities/TransportDocument.cs`, `AppDbContext`
- Create: `SiagroB1.Application/Services/Cte/TransportDocumentsEndorseService.cs`
- Modify: `TransportDocumentsController` (ação `TransportDocumentsEndorse(Key)`), `ODataConfigurations`, DI
- Migration (App): `AddCteEndorsements`
- Test: `SiagroB1.Application.Tests/Cte/TransportDocumentsEndorseServiceTests.cs`

**Interfaces:**
- Consumes: Tarefa 1; `BranchTransportSettings` + `CertificatePasswordCipher` (decifrar a senha, como `BranchTransportSettingsService.TestAverbaAsync`); `TransportDocumentXml` Kind Authorized (o `cteProc`); `CteNumberReservationService.AcquireEmissionLockAsync`.
- Produces:
  - `enum CteEndorsementStatus { None = 0, Endorsed = 1, CancellationPending = 2, Cancelled = 3 }`.
  - `TransportDocument` ganha:
    - `CteEndorsementStatus EndorsementStatus` (NOT NULL, default 0);
    - `string? EndorsementProtocol`;
    - `DateTime? EndorsedAt`;
    - `string? EndorsementError` (500);
    - `string? EndorsementCancellationProtocol`.
  - `TransportDocumentEndorsement` (`TRANSPORT_DOCUMENT_ENDORSEMENTS`):
    - `Key`, `TransportDocumentKey` (cascade);
    - `EndorsementNumber`, `InsurerTaxId`, `InsurerName`, `PolicyNumber`, `Amount` (decimal), `Branch`, `MovementType`.
    - Uma linha por seguro devolvido.
  - `TransportDocumentsEndorseService.EndorseAsync(Guid key, string user) → AverbaTestResult` (o mesmo tipo `Success/Message` do sub-projeto 2).
    - Recusas:
      - não Authorized → `"Só CT-e autorizado pode ser averbado."`;
      - já Endorsed → `"Este CT-e já foi averbado."`;
      - filial sem `AverbaEnabled` ou sem credenciais → `"Configure a averbação AT&M na filial (Transportadora)."`.
    - Fluxo: trava de emissão → login → envio do `cteProc` → grava protocolo, data e seguros.
    - Falha grava `EndorsementError` e devolve `Success = false`, sem lançar exceção.

- [ ] **Step 1: Testes que falham** (fake `IAverbaClient`):
  - `Endorse_authorized_stores_protocol_and_insurances`;
  - `Endorse_twice_is_refused`;
  - `Endorse_not_authorized_is_refused`;
  - `Endorse_without_branch_credentials_is_refused`;
  - `Endorse_failure_stores_error_and_keeps_none`;
  - `Concurrent_endorse_is_refused_by_emission_lock` (fake de reserva com a trava ocupada);
  - `Rejected_in_sapb1`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** migration `AddCteEndorsements`, aplicada só em CEAGUI_SIAGRO_DEV e LMCEREAIS_SIAGRO_DEV, com `ASPNETCORE_ENVIRONMENT=Ceagui-Development` e `--connection` explícito.
- [ ] **Step 6:** commit `feat(invoice): averbação do CT-e na AT&M` + `DB: AddCteEndorsements`.

### Task 3: Cancelamento automático da averbação

**Files:**
- Create: `SiagroB1.Application/Services/Cte/CteEndorsementCancellationService.cs`
- Modify: controller (ação `TransportDocumentsResendEndorsementCancellation(Key)`), DI
- Test: `SiagroB1.Application.Tests/Cte/CteEndorsementCancellationServiceTests.cs`

**Interfaces:**
- Consumes: Tarefas 1 e 2; `TransportDocumentXml` Kind Cancellation (o `procEventoCTe`).
- Produces: `CteEndorsementCancellationService.CancelAsync(TransportDocument doc) → Task`.
  - Só age se o documento estiver Endorsed ou CancellationPending.
  - Envia o `procEventoCTe` do cancelamento.
  - Sucesso → `Cancelled` + `EndorsementCancellationProtocol`.
  - Falha → `CancellationPending` + `EndorsementError`.
  - **Nunca lança exceção** para quem chama (o cancelamento na SEFAZ já ocorreu).
  - Também é chamado pela ação de reenvio, que exige Status Cancelled e EndorsementStatus CancellationPending.

- [ ] **Step 1: Testes que falham:**
  - `Cancelled_endorsed_cte_sends_cancellation_event_to_atm`;
  - `Atm_failure_leaves_cancellation_pending_without_throwing`;
  - `Resend_pending_cancellation_succeeds`;
  - `Not_endorsed_does_nothing`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** commit `feat(invoice): cancelamento da averbação ao cancelar o CT-e`.

### Task 4: Título a receber do CT-e + ganchos de autorização e cancelamento

**Files:**
- Modify: `SiagroB1.Domain/Enums/FinancialDocumentOrigin.cs` (`TransportDocument = 11`), `TransportDocument` (`PaymentConditionCode`)
- Create: `SiagroB1.Application/Services/Financials/FinancialDocumentsTransportService.cs`
- Create: `SiagroB1.Application/Services/Cte/ICteAuthorizationHook.cs`, `Services/Cte/CteLifecycleHooks.cs` (implementação real de `ICteAuthorizationHook` e `ICteCancellationHook`, substituindo o no-op)
- Modify: `TransportDocumentsTransmitService`/`TransportDocumentsConsultService` (chamar `ICteAuthorizationHook` no mesmo ponto que grava Authorized, uma vez só), `TransportDocumentsCancelService` (chamar `FinancialDocumentsTransportService.EnsureCanUndoAsync` **antes** da SEFAZ), `TransportDocumentsSaveService` (sugerir `PaymentConditionCode` do tomador), DI
- Migration (App): `AddTransportDocumentPaymentCondition`
- Test: `SiagroB1.Application.Tests/Financials/FinancialDocumentsTransportServiceTests.cs`, `Cte/CteLifecycleHooksTests.cs`

**Interfaces:**
- Consumes: o modelo `FinancialDocumentsSalesNoContractService` (`EnqueueAsync`, `EnsureCanUndoAsync`, `EnqueueUndo`): parcelas, numeração do título e travas de baixa. **Leia antes e copie o padrão.**
- Produces:
  - `FinancialDocumentsTransportService.EnqueueAsync(TransportDocument doc, string user)`: títulos Receivable para o `CardCode` do tomador, com origem `TransportDocument`, as dimensões do documento e o vencimento pela condição.
  - `EnsureCanUndoAsync(Guid docKey)`: recusa com a mensagem padrão dos títulos com baixa.
  - `EnqueueUndo(Guid docKey, string user)`.
  - `ICteAuthorizationHook.OnAuthorizedAsync(TransportDocument doc)` → título (se a natureza gerar financeiro) + e-mail (Tarefa 5).
  - O `ICteCancellationHook` real → desfaz o título + cancela a averbação (Tarefa 3) + e-mail.

- [ ] **Step 1: Testes que falham:**
  - `Authorized_cte_creates_receivable_for_taker_with_installments_and_dimensions`;
  - `Usage_without_financial_creates_nothing`;
  - `Cancel_with_settled_title_is_refused_before_sefaz` (o `FakeCteSefazClient.CancelAsync` não é chamado);
  - `Cancel_cancels_title_and_calls_endorsement_cancellation`;
  - `Authorization_hook_runs_once_on_consult_recovery` (timeout → consulta autoriza → um título só);
  - `Payment_condition_defaults_from_taker`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** migration `AddTransportDocumentPaymentCondition`, aplicada como na Tarefa 2.
- [ ] **Step 6:** commit `feat(financial): título a receber do CT-e contra o tomador` + `DB: AddTransportDocumentPaymentCondition`.

### Task 5: E-mail automático do CT-e (XML + DACTE)

**Files:**
- Modify: `SiagroB1.Domain/Enums/NfeEmailEnums.cs` (`NfeEmailDocumentType.Transport = 3`), `Services/NfeEmail/NfeEmailMessageBuilder.cs`, `NfeEmailRecipientResolver.cs`, `NfeEmailSendService.cs` (ramo do CT-e: anexos `CTe{chave}.xml` + `DACTE{chave}.pdf`; assunto `"CT-e {número}/{série} - {emitente}"`, ou `"Cancelamento do CT-e ..."` no cancelado)
- Create: `SiagroB1.Reports/Controllers/InternalDacteController.cs` (modelo `InternalDanfeController`) se o envio da NF-e busca o DANFE por endpoint interno; senão, o mesmo mecanismo que a NF-e usa
- Test: `SiagroB1.Application.Tests/NfeEmail/CteEmailTests.cs`

**Interfaces:**
- Consumes: `NfeEmailOutboxService.AddAutomaticAsync(type, documentKey, evt, user)`; `BusinessPartnerNfeEmail`; `DacteReportService` (entrega A).
- Produces:
  - Destinatários do CT-e: e-mails NF-e do tomador, remetente e destinatário, deduplicados sem diferenciar maiúsculas.
  - Os ganchos da Tarefa 4 chamam `AddAutomaticAsync(Transport, key, Authorized|Cancelled, user)`.

- [ ] **Step 1: Testes que falham:**
  - `Authorized_cte_enqueues_email_once`;
  - `Recipients_are_taker_sender_recipient_deduplicated`;
  - `Taker_without_email_still_sends_to_others`;
  - `No_recipient_is_recorded_like_nfe`;
  - `Message_has_xml_and_dacte_attachments_and_subject`;
  - `Cancelled_cte_enqueues_cancellation_email`;
  - `Nfe_email_unchanged` (um teste existente da NF-e segue verde, sem alteração).
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** commit `feat(invoice): e-mail automático do CT-e com XML e DACTE`.

### Task 6: Transmissão e averbação em lote (Hangfire) + download em lote

**Files:**
- Create: `SiagroB1.Application/Jobs/CteBatchJob.cs`, `SiagroB1.Application/Services/Cte/CteBatchService.cs`
- Create: `SiagroB1.Reports/Controllers/DacteBatchController.cs` (`POST /reports/DacteBatch` com chaves → ZIP de PDFs) e função `TransportDocumentsXmlBatch(Keys)` → ZIP de XML no Web (ou ação equivalente, seguindo como a NF-e baixa XML)
- Modify: controller (ações `TransportDocumentsTransmitBatch(Keys)` e `TransportDocumentsEndorseBatch(Keys)` → número de enfileirados), DI
- Test: `SiagroB1.Application.Tests/Cte/CteBatchServiceTests.cs`

**Interfaces:**
- Produces:
  - `CteBatchService.EnqueueTransmit(IReadOnlyList<Guid> keys, string user) → int` e `EnqueueEndorse(...)`: um job **por documento** (`IBackgroundJobClient`, `FakeBackgroundJobClient` nos testes).
    - Ignora chave fora de situação: transmitir só Confirmed/Rejected/Processing; averbar só Authorized não averbado.
    - Limite de **200** por chamada → `"Selecione no máximo 200 CT-e por vez."`.
  - `CteBatchJob.TransmitAsync(Guid key, string user)` e `EndorseAsync(...)`: chamam os serviços das entregas A/B.
    - Exceção de negócio vira o motivo no documento.
    - **Nunca relança** (o job não fica em retry infinito).
- [ ] **Step 1: Testes que falham:**
  - `Batch_enqueues_one_job_per_eligible_document`;
  - `Batch_skips_ineligible_and_reports_count`;
  - `Batch_over_200_is_refused`;
  - `Job_failure_on_one_does_not_throw_and_others_run`;
  - `Dacte_batch_zip_contains_one_pdf_per_authorized` (se o projeto de testes já cobre controllers de Reports; senão, teste do serviço que monta o ZIP).
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** commit `feat(invoice): transmissão, averbação e download de CT-e em lote`.

### Task 7: Frontend — averbação, título, lote

**Files** (frontend):
- Modify: `webapp/view/transportDocuments/*` e `webapp/controller/transportDocuments/*`, `webapp/helpers/CteHelpers.ts`, `webapp/model/ServerRoutes.ts`
- Test: `webapp/test/unit/helpers/CteHelpers.qunit.ts`

**O que muda:**
- **Detalhe:**
  - botão **"Averbar"**, só Authorized não averbado e com a filial com AT&M ligada;
  - seção **Averbação**: situação, protocolo, data e a tabela de seguros;
  - botão **"Reenviar cancelamento da averbação"** quando a averbação está pendente;
  - link para o **título** gerado;
  - botão **"Enviar e-mail"**, reenvio manual, como na NF-e.
- **Formulário:** condição de pagamento, sugerida do tomador.
- **Lista:**
  - seleção múltipla;
  - botões "Transmitir selecionados", "Averbar selecionados" e "Baixar XML", "Baixar DACTE" (ZIP);
  - coluna e filtro "Averbado".
- **Helpers testáveis:**
  - `endorsementStatusText`/`endorsementStatusState`;
  - `canEndorse(status, endorsementStatus, averbaEnabled)`;
  - `batchSelectionError(count)` (máx. 200).
- **Lições da entrega A:**
  - `targetType: 'any'` em enum e data;
  - `$select` no `$expand`;
  - `refreshStandaloneFlag`;
  - mensagens limpas do `readErrorMessage`.

- [ ] **Step 1:** testes QUnit dos helpers (RED por typecheck).
- [ ] **Step 2:** implementar.
- [ ] **Step 3:** `yarn ts-typecheck` e `npx eslint webapp` limpos. O controlador roda o QUnit no navegador.
- [ ] **Step 4:** commits `feat(invoice): averbação e título no detalhe do CT-e`, `feat(invoice): ações em lote na lista de CT-e`.

### Task 8: Verificação ponta a ponta

- [ ] **Step 1:** stack `ceagui` dos worktrees. Migrations em dia.
- [ ] **Step 2:** com a filial de teste 02 (COSTA E COSTA, homologação, série 231):
  1. emitir um CT-e → título a receber criado para o tomador, com as parcelas;
  2. e-mail na fila — **sem SMTP real**, conferir a fila e os anexos gerados;
  3. **averbar na AT&M — só com as credenciais de homologação da AT&M fornecidas pelo usuário e com a confirmação dele antes** (ação externa);
  4. cancelar o CT-e → título cancelado, cancelamento da averbação enviado e e-mail de cancelamento na fila;
  5. lote: confirmar três CT-e e transmitir em lote (homologação, confirmação do usuário) → os três autorizados;
  6. baixar o ZIP de XML e de DACTE.
- [ ] **Step 3:** cancelar um CT-e com título baixado → recusa antes da SEFAZ.
- [ ] **Step 4:** derrubar a stack pelos PIDs; superpowers:verification-before-completion; revisão final do branch.
