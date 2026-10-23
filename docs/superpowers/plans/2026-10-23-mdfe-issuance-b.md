# Emissão de MDF-e — entrega B (cancelamento, inclusão de condutor, não encerrados, encerramento em lote, ZIP) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Completar o ciclo do MDF-e: cancelar, incluir condutor, consultar não encerrados na SEFAZ, encerrar em lote e baixar XML/DAMDFE em ZIP.

**Architecture:** Serviços novos em `Application/Services/Mdfe`, sobre duas peças da entrega A:
- o `IMdfeSefazClient` (A, Tarefa 4), que já tem `CancelAsync`, `IncludeDriverAsync` e `ListNotClosedAsync`;
- o `TransportManifestsCloseService.CloseCoreAsync` (A, Tarefa 10).

O ZIP segue o molde `CteXmlZipService`/`DacteBatchService`.

**Tech Stack:** igual à entrega A.

**Spec:** `docs/superpowers/specs/2026-10-23-mdfe-issuance-design.md`. Pré-requisito: entrega A (`2026-10-23-mdfe-issuance-a.md`) concluída no mesmo branch.

## Global Constraints

Todas as da entrega A valem. Além disso:
- **Cancelamento:**
  - só Authorized e **não encerrado**;
  - justificativa de 15 a 255 caracteres: `"A justificativa do cancelamento tem de 15 a 255 caracteres."`;
  - prazo de **24 horas** da autorização (regra nacional do MDF-e): `"O prazo de cancelamento do MDF-e (24 horas) acabou."`;
  - 135 → Cancelled, com `IsActive=false` nos vínculos (libera os CT-e).
- **Inclusão de condutor:**
  - só Authorized;
  - CPF válido, com 11 dígitos;
  - sequência = última + 1;
  - 135 → grava `TransportManifestDriver` (`IncludedByEvent = true`) e o XML `DriverInclusion`.
- **Não encerrados:** consulta pelo CNPJ da filial e cruza as chaves com `TRANSPORT_MANIFESTS`. Chave desconhecida aparece como "MDF-e fora do sistema".
- **Lote de encerramento:** cada MDF-e é independente (a falha de um não desfaz os outros); o resultado é por item. Síncrono, sem Hangfire (lotes pequenos, frota própria).

## Review Focus

1. **Cancelar um MDF-e já encerrado** (localmente ou só na SEFAZ): recusa local; se a SEFAZ responder que está encerrado, consultar e marcar Closed. Teste na Tarefa 1.
2. **Lote com um MDF-e já encerrado e outro com timeout:** os demais encerram; o resumo é por item. Teste na Tarefa 4.
3. **Não encerrado na SEFAZ que no sistema está Authorized ou Processing:** a lista oferece "Encerrar" e "Consultar". Teste na Tarefa 3.
4. **Incluir o mesmo condutor duas vezes:** recusa `"O condutor já está no MDF-e."`. Teste na Tarefa 2.
5. **ZIP com MDF-e não autorizados selecionados:** ignora-os; sem nenhum autorizado → `"Nenhum MDF-e autorizado entre os selecionados."`. Teste na Tarefa 5.

---

### Task 1: Cancelamento

**Files:**
- Create: `SiagroB1.Application/Services/Mdfe/TransportManifestsCancelService.cs`
- Modify: controller (ação `TransportManifestsCancel(Key, Justification)`)
- Test: `SiagroB1.Application.Tests/Mdfe/TransportManifestsCancelServiceTests.cs`

**Interfaces:** `CancelAsync(Guid key, string justification, string user) → NfeEventResult`.

- [ ] **Step 1: Testes que falham:**
  - `Cancel_short_justification_is_rejected`;
  - `Cancel_after_24h_is_rejected`;
  - `Cancel_closed_is_rejected`;
  - `Cancel_135_marks_cancelled_and_frees_ctes`;
  - `Cancel_sefaz_says_closed_consults_and_marks_closed`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** commit `feat(invoice): cancelamento do MDF-e`.

### Task 2: Inclusão de condutor

**Files:**
- Create: `Services/Mdfe/TransportManifestsIncludeDriverService.cs`
- Modify: `TransportManifestDriver` (+ `bool IncludedByEvent`, `int? EventSequence`) e o controller (ação `TransportManifestsIncludeDriver(Key, Cpf, Name)`)
- Migration (App): `AddMdfeDriverInclusion`
- Test: `Mdfe/TransportManifestsIncludeDriverServiceTests.cs`

- [ ] **Step 1: Testes que falham:**
  - `Include_driver_invalid_cpf_is_rejected`;
  - `Include_driver_duplicate_is_rejected`;
  - `Include_driver_increments_sequence_and_stores_xml`;
  - `Include_driver_only_when_authorized`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** aplicar a migration nos 2 bancos App.
- [ ] **Step 6:** commit `feat(invoice): inclusão de condutor no MDF-e` + `DB: AddMdfeDriverInclusion`.

### Task 3: Consulta de MDF-e não encerrados

**Files:**
- Create: `Services/Mdfe/MdfeNotClosedQueryService.cs`
- Modify: controller (função `MdfeNotClosed(BranchCode)`, que retorna `{AccessKey, Protocol, ManifestKey?, Number?, Series?, Status?, TractionPlate?}`)
- Test: `Mdfe/MdfeNotClosedQueryServiceTests.cs`

- [ ] **Step 1: Testes que falham:**
  - `NotClosed_matches_known_manifests`;
  - `NotClosed_unknown_key_is_reported_outside_system`;
  - `NotClosed_none_returns_empty`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** commit `feat(invoice): consulta de MDF-e não encerrados na SEFAZ`.

### Task 4: Encerramento em lote

**Files:**
- Create: `Services/Mdfe/TransportManifestsCloseBatchService.cs`
- Modify: controller (ação `TransportManifestsCloseBatch(Keys, ClosedOn?)`, que retorna a lista `{Key, Number, Success, Message}`)
- Test: `Mdfe/TransportManifestsCloseBatchServiceTests.cs`

**Interfaces:** cada item chama `CloseCoreAsync` com os padrões do próprio MDF-e.

- [ ] **Step 1: Testes que falham:**
  - `Batch_closes_each_independently`;
  - `Batch_reports_already_closed_and_timeout_per_item`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** commit `feat(invoice): encerramento de MDF-e em lote`.

### Task 5: ZIP de XML e DAMDFE

**Files:**
- Create: `Services/Mdfe/MdfeXmlZipService.cs`, no molde do `CteXmlZipService`. Se a montagem do ZIP for idêntica, extrair `Services/Dfe/DfeZipBuilder` para os dois, com os testes do CT-e verdes.
- Create: `SiagroB1.Reports/Services/DamdfeBatchService.cs`, `Reports/Controllers/DamdfeBatchController.cs`
- Modify: controller (ação `TransportManifestsXmlZip(Keys)`)
- Test: `Mdfe/MdfeXmlZipServiceTests.cs`, `Reports/DamdfeBatchTests.cs`

- [ ] **Step 1: Testes que falham:**
  - `Zip_contains_authorized_and_event_xmls_named_by_key`;
  - `Zip_skips_unauthorized`;
  - `Zip_nothing_authorized_is_refused`;
  - `Damdfe_batch_returns_one_pdf_per_key`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** PASS, e `dotnet test` completo verde (CT-e incluso).
- [ ] **Step 5:** commits `feat(invoice): ZIP de XML do MDF-e` e `feat(reports): DAMDFE em lote`.

### Task 6: Frontend — ações da entrega B

**Files** (`siagro-b1-frontend-mdfe`):
- Modify: `webapp/view/transportManifests/{Main,Detail}.view.xml` + controllers
- Create: fragments `CancelDialog`, `IncludeDriverDialog`, `NotClosedDialog`, `CloseBatchResultDialog`
- Modify: `webapp/helpers/MdfeHelpers.ts` (+ `cancellationJustificationError`, `cpfError`)
- Test: `MdfeHelpers.qunit.ts`

**O que muda:**
- **Lista:**
  - "Encerrar selecionados": diálogo com a data, depois o resumo por item;
  - "Baixar XML" e "Baixar DAMDFE" (ZIP);
  - "Consultar não encerrados": diálogo com Encerrar e Consultar por linha.
- **Detalhe:** Cancelar e Incluir condutor.

- [ ] **Step 1:** QUnit (RED):
  - justificativa com 14, 15, 255 e 256 caracteres;
  - CPF.
- [ ] **Step 2:** implementar.
- [ ] **Step 3:** `yarn ts-typecheck`, `yarn lint` e QUnit verdes.
- [ ] **Step 4:** commits `feat(invoice): cancelamento e inclusão de condutor na tela do MDF-e` e `feat(invoice): encerramento em lote, não encerrados e ZIP do MDF-e`.

### Task 7: Verificação ponta a ponta (homologação SEFAZ)

> Só pela tela, filial 02 COSTA, série 231. O usuário já autorizou os testes de homologação; anunciar cada transmissão.

- [ ] **Step 1:** stack `ceagui` dos worktrees `-mdfe`; migrations em dia.
- [ ] **Step 2:** novo MDF-e com CT-e liberados (ex.: 1 e 4) → autorizar → incluir condutor → cancelar (dentro de 24 h) → conferir que os CT-e foram liberados.
- [ ] **Step 3:** com dois MDF-e autorizados:
  - "Consultar não encerrados" lista os dois;
  - "Encerrar selecionados" encerra ambos;
  - a consulta seguinte vem vazia.
- [ ] **Step 4:** baixar o ZIP de XML e o de DAMDFE, e abrir os dois.
- [ ] **Step 5:** derrubar a stack pelos PIDs; superpowers:verification-before-completion; revisão final do branch.
