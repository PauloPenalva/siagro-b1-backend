# Envio automático de NF-e por e-mail — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enviar automaticamente DANFE + XML aos e-mails do PN quando uma NF-e própria é autorizada ou cancelada (STANDALONE), com SMTP por empresa, fila com retentativa e reenvio manual.

**Architecture:** Os dois handlers genéricos (`NfeResultHandlerBase`, `NfeCancellationHandlerBase`) gravam uma linha em `NFE_EMAIL_MESSAGES` no mesmo `SaveChanges` da transição de status. Um sweep por empresa (Hangfire, minutely) enfileira o dispatch na fila `email`, que monta anexos (XML do store + DANFE via rota interna do Reports) e envia com o `SmtpEmailSender` usando a `EMAIL_SETTINGS` da empresa. Frontend: lista de e-mails no PN, tela de configuração, botão/histórico nos Detail.

**Tech Stack:** .NET 10, EF Core SQL Server, Hangfire, MailKit, FastReport (Reports), OpenUI5 TS.

**Spec:** `docs/superpowers/specs/2026-10-19-nfe-email-delivery-design.md`

Worktrees: backend `C:\Projetos\SiagroB1\worktrees\nfe-email\backend`, frontend `...\nfe-email\frontend`, branch `feature/nfe-email` nos dois. Commits no padrão do `CLAUDE.md` do backend (`feat(invoice): ...`, `feat(partner): ...`, trailer `DB:` em commit com migration, `Co-Authored-By`). Nunca push. ⚠️ Antes de rodar testes de relatório no worktree, copie `SiagroB1.Reports/wwwroot` do checkout principal (memória worktree-missing-untracked-report-assets).

## Global Constraints

- Somente STANDALONE: `ErpMode.IsStandalone(configuration)` guarda gatilhos, sweep, telas e menu.
- Código em inglês; textos ao usuário, mensagens de erro e corpo do e-mail em pt-BR.
- Senha SMTP cifrada com `CertificatePasswordCipher` (chave `Nfe:CertificateKey`); nunca retornada pela API nem logada.
- `Email:Enabled` (padrão `false`) é trava global: com `false`, o envio vai só para o log e conta como enviado.
- Falha de e-mail nunca altera/atrasa autorização ou cancelamento (só insere linha no mesmo save).
- Envio automático só na **transição** para Authorized/Cancelled; uma linha automática por (DocumentType, DocumentKey, EventType).
- Máximo 10 e-mails por PN; único por PN; validação de formato.
- Retentativa: 3 tentativas Hangfire `[30, 120, 600]` s, depois Failed.
- Assunto: `NF-e nº {nNF} série {serie} – {xNome emitente} – Autorizada|Cancelada`. Anexos: `{chave}-procNFe.xml` / `{chave}-procEventoNFe.xml` + `DANFE-{nNF}.pdf`.
- Índice filtrado → migration com `SET QUOTED_IDENTIFIER ON` se houver SQL cru (memória filtered-index-requires-quoted-identifier).
- Serviços novos registrados à mão em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`.

## Review Focus

1. "Consultar situação" numa nota já Autorizada/Cancelada não pode criar e-mail → teste em Task 4.
2. PN sem nenhum e-mail → linha Skipped com "Parceiro sem e-mail de NF-e", sem exceção → Task 5.
3. Cancelamento fora do Siagro (sem procEvento) → e-mail só com DANFE e aviso → Task 5.
4. Empresa sem `EMAIL_SETTINGS` ou `AutoSendEnabled=false` → nenhuma linha automática; reenvio manual responde erro claro "Configure o envio de e-mail da empresa" → Tasks 4 e 6.
5. Excluir PN com e-mails cadastrados continua funcionando → Task 1.

---

### Task 1: Lista de e-mails de NF-e do PN (backend)

**Files:**
- Create: `SiagroB1.Domain/Entities/BusinessPartnerNfeEmail.cs`
- Modify: `SiagroB1.Domain/Entities/BusinessPartner.cs` (remove `NfeEmail`, add `ICollection<BusinessPartnerNfeEmail> NfeEmails`), `SiagroB1.Domain/Models/BusinessPartnerModel.cs`, `SiagroB1.Application/Services/BusinessPartnerService.cs` (map/validate ~L47,108,205,320,402), `SiagroB1.Application/Services/Nfe/NfeIssueInputAssembler.cs:194`, `SiagroB1.Infra/Context/AppDbContext.cs`, EDM builder of BusinessPartners (contained/expanded collection, same style as partner addresses)
- Create: migration `AddBusinessPartnerNfeEmails` (AppDbContext)
- Test: `SiagroB1.Application.Tests/BusinessPartners/BusinessPartnerNfeEmailsTests.cs`

**Interfaces:**
- Produces: entity `BusinessPartnerNfeEmail { Guid Key; string CardCode; string Email (250); int LineNum; }`, table `BUSINESS_PARTNER_NFE_EMAILS`, unique (CardCode, Email), FK → BusinessPartners cascade. `BusinessPartnerModel.NfeEmails: List<string>`.
- Produces helper used by Task 5: `Task<IReadOnlyList<string>> INfeEmailRecipientResolver.ResolveAsync(string cardCode)` in `SiagroB1.Application/Services/NfeEmail/NfeEmailRecipientResolver.cs` (ordered by LineNum).

- [ ] **Step 1: Failing tests** — `Create_saves_emails_in_order`, `Update_replaces_list`, `Rejects_invalid_email` (msg "E-mail de NF-e inválido: {x}"), `Rejects_duplicate_email`, `Rejects_more_than_10` (msg "Informe no máximo 10 e-mails de NF-e."), `Delete_partner_with_emails_succeeds`, `Assembler_uses_first_email_as_dest_email` (in `NfeIssueInputAssemblerTests`), `Resolver_returns_emails_by_linenum`.
- [ ] **Step 2:** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~BusinessPartnerNfeEmails|FullyQualifiedName~NfeIssueInputAssembler"` → FAIL (compile).
- [ ] **Step 3:** Implement entity, mapping, validation (trim, case-insensitive dup), resolver, assembler change.
- [ ] **Step 4:** Migration: create table; `INSERT ... SELECT NEWID(), CardCode, LTRIM(RTRIM(NfeEmail)), 0 FROM BUSINESS_PARTNERS WHERE NfeEmail IS NOT NULL AND LTRIM(RTRIM(NfeEmail)) <> ''`; then drop column. `Down` restores column from LineNum 0.
- [ ] **Step 5:** Tests PASS; `dotnet build SiagroB1.sln` clean.
- [ ] **Step 6:** Commit `feat(partner): permitir vários e-mails de NF-e no parceiro` + `DB: AddBusinessPartnerNfeEmails`.

### Task 2: Configuração de e-mail da empresa (backend)

**Files:**
- Create: `SiagroB1.Domain/Entities/EmailSettings.cs`, `SiagroB1.Domain/Models/EmailSettingsModel.cs`, `SiagroB1.Application/Services/NfeEmail/EmailSettingsService.cs`, `SiagroB1.Web/Controllers/EmailSettingsController.cs`
- Create: migration `AddEmailSettings` (table + MENU_ITEMS/permission seed for screen `emailSettings`, STANDALONE pattern as in `nfeSettings` menu migration)
- Test: `SiagroB1.Application.Tests/NfeEmail/EmailSettingsServiceTests.cs`

**Interfaces:**
- Produces: `EmailSettings { int Key (single row =1); string Host; int Port=587; bool UseStartTls=true; string? UserName; byte[]? PasswordEncrypted; string FromAddress; string? FromName; bool AutoSendEnabled; DateTime UpdatedAt; string UpdatedBy; }` table `EMAIL_SETTINGS`.
- Produces: `IEmailSettingsService.GetAsync(): Task<EmailSettingsModel?>` (no password, `HasPassword` bool), `SaveAsync(EmailSettingsModel model, string userName)` (null/empty `Password` keeps current), `GetSmtpAsync(): Task<SmtpSettings?>` (decrypted, internal), `SendTestAsync(string to): Task<EmailSendResult>`.
- OData: singleton-style `GET/PUT odata/EmailSettings` + action `EmailSettingsSendTest(To)`; ADMIN only (`HasRoleAsync`, memória admin-role-vs-isadmin-flag).

- [ ] **Step 1: Failing tests** — `Save_encrypts_password_and_get_never_returns_it`, `Save_without_password_keeps_previous`, `Save_requires_host_and_from_address` (msg "Informe o servidor SMTP e o e-mail do remetente."), `GetSmtp_decrypts`, `SendTest_returns_smtp_error_message` (FakeEmailSender failing).
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement (cipher via `CertificatePasswordCipher.FromBase64(config[ConfigurationKey])`).
- [ ] **Step 4:** Tests PASS; build clean.
- [ ] **Step 5:** Commit `feat(invoice): configurar remetente de e-mail por empresa` + `DB: AddEmailSettings`.

### Task 3: Remetente SMTP com anexos e vários destinatários

**Files:**
- Modify: `SiagroB1.Domain/Interfaces/IEmailSender.cs`, `SiagroB1.Infra/Email/SmtpEmailSender.cs`, `SiagroB1.Application.Tests/Support/FakeEmailSender.cs`, `SiagroB1.Web/Program.cs` (register `IEmailSender` → `SmtpEmailSender`)
- Create: `SiagroB1.Domain/Dtos/Email/EmailMessage.cs`
- Test: `SiagroB1.Application.Tests/NfeEmail/SmtpEmailSenderTests.cs`

**Interfaces:**
- Produces: `record SmtpSettings(string Host, int Port, bool UseStartTls, string? UserName, string? Password, string FromAddress, string? FromName)`; `record EmailAttachment(string FileName, byte[] Content, string ContentType)`; `record EmailMessage(IReadOnlyList<string> To, string Subject, string HtmlBody, IReadOnlyList<EmailAttachment> Attachments)`; `record EmailSendResult(bool Success, string? Error)`.
- `IEmailSender.SendAsync(EmailMessage message, SmtpSettings smtp, CancellationToken ct = default): Task<EmailSendResult>` (never throws). Existing overload unchanged.
- `FakeEmailSender` records `Sent` list and can be set `FailWith(string error)`.

- [ ] **Step 1: Failing tests** — `Disabled_globally_logs_and_returns_success` (`Email:Enabled=false`, no SMTP call), `Builds_mime_with_all_recipients_and_attachments` (expose internal `BuildMime` and assert To count, attachment names).
- [ ] **Step 2:** FAIL. **Step 3:** implement. **Step 4:** PASS.
- [ ] **Step 5:** Commit `feat(platform): enviar e-mail com anexos e vários destinatários`.

### Task 4: Fila de e-mail e gatilhos nos handlers

**Files:**
- Create: `SiagroB1.Domain/Entities/NfeEmailMessage.cs`, enums `NfeEmailDocumentType {Sales=1, Purchase=2}`, `NfeEmailEventType {Authorized=1, Cancelled=2}`, `NfeEmailTrigger {Automatic=1, Manual=2}`, `NfeEmailStatus {Pending=1, Sent=2, Failed=3, Skipped=4}`; `SiagroB1.Application/Services/NfeEmail/NfeEmailOutboxService.cs`
- Modify: `NfeResultHandlerBase.cs` (`ApplyAsync`, before the `SaveChangesAsync` of the authorized branch), `NfeCancellationHandlerBase.cs` (`ApplyRegisteredAsync`, before its save), both subclasses (`SalesInvoiceNfeResultHandler`, `PurchaseInvoiceNfeResultHandler`, cancellation handlers) to pass the `NfeEmailDocumentType`; `AppDbContext`
- Create: migration `AddNfeEmailMessages`
- Test: `SiagroB1.Application.Tests/NfeEmail/NfeEmailTriggerTests.cs`

**Interfaces:**
- Produces: `NfeEmailMessage { Guid Key; NfeEmailDocumentType DocumentType; Guid DocumentKey; NfeEmailEventType EventType; NfeEmailTrigger Trigger; string? Recipients; NfeEmailStatus Status; int AttemptCount; DateTime? LastAttemptAt; string? LastError (1000); DateTime? SentAt; DateTime CreatedAt; string CreatedBy; }`, table `NFE_EMAIL_MESSAGES`, index (Status, CreatedAt), unique filtered (DocumentType, DocumentKey, EventType) `WHERE [Trigger] = 1`.
- `INfeEmailOutboxService.AddAutomaticAsync(NfeEmailDocumentType type, Guid documentKey, NfeEmailEventType evt, string userName): Task` — adds (no save) only if STANDALONE, settings exist with `AutoSendEnabled`, and no automatic row exists. Handler calls it **only when previous `NfeStatus` != target status**.
- `AddManualAsync(type, documentKey, evt, IReadOnlyList<string> recipients, string userName): Task<NfeEmailMessage>` — saves.

- [ ] **Step 1: Failing tests** (use `SalesInvoicesNfeIssueServiceTests`/`ConsultServiceTests` seeds): `Authorization_enqueues_one_automatic_email`, `Consult_on_already_authorized_does_not_enqueue`, `Cancellation_enqueues_cancelled_email`, `Consult_cancellation_outside_siagro_enqueues`, `Disabled_settings_do_not_enqueue`, `Sapb1_mode_does_not_enqueue`, `Purchase_own_authorization_enqueues_purchase_type`, `Second_apply_does_not_duplicate`.
- [ ] **Step 2:** FAIL. **Step 3:** implement; migration (filtered index). **Step 4:** PASS + full `SiagroB1.Application.Tests/Nfe` suite still green.
- [ ] **Step 5:** Commit `feat(invoice): enfileirar e-mail da NF-e autorizada ou cancelada` + `DB: AddNfeEmailMessages`.

### Task 5: DANFE interno no Reports + dispatch + sweep

**Files:**
- Create: `SiagroB1.Reports/Controllers/InternalDanfeController.cs` (route `internal/danfe/{type}/{key}`, `type` = `sales|purchase`; header `X-Internal-Key` must equal `Reports:InternalApiKey` (empty → 404); header `X-Company` → `ICompanyCatalog` lookup + `ICompanyContext.Set`; returns PDF from `DanfeReportService`)
- Create: `SiagroB1.Application/Services/NfeEmail/IDanfeProvider.cs` + `SiagroB1.Infra/Reports/HttpDanfeProvider.cs` (`Reports:InternalBaseUrl`, `Reports:InternalApiKey`); `SiagroB1.Application/Services/NfeEmail/NfeEmailMessageBuilder.cs`; `SiagroB1.Application/Jobs/NfeEmailDispatchJob.cs`, `SiagroB1.Application/Jobs/NfeEmailSweepJob.cs`
- Modify: `SiagroB1.Web/Program.cs` (Hangfire server queue `email`, WorkerCount 1; recurring `nfe-email-sweep` minutely via `CompanyScopedJobRunner` like `contract-notification-sweep`, only STANDALONE), `CompanyScopedJobRunner` key constant, appsettings (Web+Reports) keys with empty values
- Test: `SiagroB1.Application.Tests/NfeEmail/NfeEmailDispatchJobTests.cs`, `NfeEmailMessageBuilderTests.cs`, `NfeEmailSweepJobTests.cs`, `SiagroB1.Application.Tests/Reports/InternalDanfeControllerTests.cs`

**Interfaces:**
- `IDanfeProvider.GetPdfAsync(NfeEmailDocumentType type, Guid key, string companyCode, CancellationToken ct): Task<byte[]>`
- `NfeEmailMessageBuilder.Build(NfeEmailEventType evt, string procXml /*procNFe*/, string? eventXml, byte[] danfe): (string Subject, string HtmlBody, IReadOnlyList<EmailAttachment> Attachments)` — reads nNF, serie, chave, emit xNome/CNPJ, dest xNome, dhEmi, vNF, nProt from procNFe.
- `NfeEmailDispatchJob.ExecuteAsync(string companyCode, Guid messageKey, CancellationToken ct)`, `[Queue("email")]`, `[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 120, 600])]`; constant `QueueName = "email"`.
- `NfeEmailSweepJob.ExecuteAsync(CancellationToken ct)` — Pending, `CreatedAt <= now - 1 min`, Trigger Automatic, top 200 → enqueue.

Dispatch rules: return if not Pending; recipients = row snapshot or (automatic) resolver → empty ⇒ Skipped `"Parceiro sem e-mail de NF-e"`; no settings ⇒ Failed `"Configure o envio de e-mail da empresa"`; XML via `INfeDocumentStore.LatestAuthorizedXmlAsync` / `LatestXmlAsync(CancellationEvent)`; send; success ⇒ Sent + SentAt + Recipients; failure ⇒ AttemptCount++, LastError, and if AttemptCount < 3 throw (Hangfire retry) else Failed.

- [ ] **Step 1: Failing tests** — builder: `Authorized_subject_and_attachments` (assert exact subject `NF-e nº 293 série 1 – CEAGUI ... – Autorizada`, files `{chave}-procNFe.xml`, `DANFE-293.pdf`), `Cancelled_without_event_xml_has_only_danfe_and_notice` (body contains "cancelada fora do Siagro"); dispatch: `Sends_to_all_partner_emails`, `No_emails_marks_skipped`, `Smtp_failure_records_error_and_throws_until_third_attempt`, `Already_sent_is_noop`; sweep: `Enqueues_only_pending_older_than_grace`; controller: `Wrong_key_returns_404`, `Valid_key_sets_company_and_returns_pdf`.
- [ ] **Step 2:** FAIL. **Step 3:** implement. **Step 4:** PASS; build clean.
- [ ] **Step 5:** Commit `feat(invoice): enviar por e-mail o DANFE e o XML da NF-e`.

### Task 6: Reenvio manual e histórico (backend)

**Files:**
- Create: `SiagroB1.Application/Services/NfeEmail/NfeEmailSendService.cs`, `NfeEmailMessagesGetService.cs`; OData actions in `SiagroB1.Web/Controllers/SalesInvoicesController.cs` and `PurchaseInvoicesController.cs` (or dedicated `Functions/NfeEmail/...`): `SalesInvoicesSendNfeEmail(Key, Recipients[])`, `PurchaseInvoicesSendNfeEmail(...)`; entity set `NfeEmailMessages` filterable by DocumentType/DocumentKey
- Test: `SiagroB1.Application.Tests/NfeEmail/NfeEmailSendServiceTests.cs`

**Interfaces:**
- `NfeEmailSendService.SendAsync(NfeEmailDocumentType type, Guid key, IReadOnlyList<string> recipients, string userName): Task<NfeEmailMessage>` — event from current NfeStatus (Authorized/Cancelled, else error "Só é possível enviar NF-e autorizada ou cancelada."); third-party purchase ⇒ error "Esta nota não foi emitida pela empresa."; validates emails (same rules as Task 1); requires settings ("Configure o envio de e-mail da empresa"); `AddManualAsync` then `backgroundJobClient.Enqueue` dispatch.
- Recipients default for the dialog: `NfeEmailRecipientResolver` exposed via function `NfeEmailDefaultRecipients(DocumentType, Key)`.

- [ ] **Step 1: Failing tests** — `Manual_send_creates_row_and_enqueues`, `Rejects_pending_invoice`, `Rejects_third_party`, `Rejects_without_settings`, `Rejects_invalid_recipient`.
- [ ] **Step 2–4:** FAIL → implement → PASS; EDM test that the actions exist (pattern `NfeCancellationEdmModelTests`).
- [ ] **Step 5:** Commit `feat(invoice): reenviar por e-mail a NF-e pela nota`.

### Task 7: Frontend — e-mails no PN e tela de configuração

**Files (frontend worktree):**
- Modify: `webapp/view/parceirosNegocio/fragments/Form.fragment.xml:121` (replace Input by editable `sap.m.Table` bound to `NfeEmails`, add/remove buttons, `visible="{ui>/standalone}"`), `webapp/controller/parceirosNegocio/Add.controller.ts:45` (`NfeEmails: []`) and the edit controller; i18n
- Create: `webapp/controller/emailSettings/Main.controller.ts`, `webapp/view/emailSettings/Main.view.xml` (Form + ColumnLayout: Servidor, Porta, STARTTLS, Usuário, Senha (type Password, placeholder "•••• (mantida)" when `HasPassword`), E-mail do remetente, Nome do remetente, Envio automático switch; buttons Salvar, "Enviar e-mail de teste"); `manifest.json` route/target `emailSettings` (copy `nfeSettings` pattern)

- [ ] **Step 1:** Implement following `nfeSettings` screen; `npm run ts-typecheck` and `npm run lint` clean.
- [ ] **Step 2:** Run stack (memória run-stack-locally), verify via Playwright from the home: create PN with 2 e-mails, edit, remove one; open "Configuração de e-mail", save, test-send (with `Email:Enabled=false` result is success and log shows message).
- [ ] **Step 3:** `git add` + commit `feat(partner): cadastrar vários e-mails de NF-e e configurar o remetente`.

### Task 8: Frontend — botão "Enviar e-mail" e histórico nas notas

**Files:**
- Modify: `webapp/controller/salesInvoices/Detail.controller.ts` (near L174-258) + view; `webapp/controller/purchaseInvoices/Detail.controller.ts` (near L560-663) + view; i18n
- Create: `webapp/view/common/fragments/NfeEmailDialog.fragment.xml` + helper `webapp/controller/common/NfeEmailHelper.ts` (shared by both Details: open dialog prefilled with `NfeEmailDefaultRecipients`, editable list, call send action, refresh history)

- [ ] **Step 1:** Button "Enviar e-mail" visible when STANDALONE and own NF-e with status Authorized/Cancelled; section "E-mails enviados" table (Data, Evento, Automático/Manual, Destinatários, Situação, Erro, Usuário) bound to `NfeEmailMessages` filtered by DocumentType/DocumentKey.
- [ ] **Step 2:** typecheck + lint clean.
- [ ] **Step 3:** Playwright from home with `Email:Enabled=false`: authorize (or use an already-authorized note) → manual send → history shows Sent; check Web log contains subject. Confirm a third-party purchase invoice does not show the button.
- [ ] **Step 4:** Commit `feat(invoice): enviar e-mail da NF-e e ver o histórico na nota`.

### Task 9: Verificação final

- [ ] Full backend `dotnet test SiagroB1.Application.Tests` (compare failures with `main` baseline; only pre-existing ones allowed) and `dotnet build SiagroB1.sln`.
- [ ] End-to-end local with real SMTP **only to an internal address** (configure with the CEAGUI Hostinger data entered on the screen, never in files) — PN with only the user's own e-mail; authorize in homologação if available, else manual send; confirm receipt with both attachments.
- [ ] Deploy notes appended to spec: migrations in all companies; `Email:Enabled=true`, `Reports:InternalBaseUrl`, `Reports:InternalApiKey` (same in Web/Reports) only in CEAGUI production.
- [ ] Use superpowers:verification-before-completion and superpowers:requesting-code-review; leave merge to the user.
