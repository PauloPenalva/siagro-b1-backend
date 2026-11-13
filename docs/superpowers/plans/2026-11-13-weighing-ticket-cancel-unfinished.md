# Cancelar ticket de pesagem não concluído (ADMIN) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir que o papel ADMIN cancele um ticket de pesagem ainda não concluído.

**Architecture:** O serviço e a ação OData de cancelamento que já existem passam a decidir pelo status do ticket: em andamento exige o papel ADMIN e só marca o ticket como cancelado; concluído segue o caminho atual do romaneio pendente. A tela espelha a regra escondendo o botão.

**Tech Stack:** .NET 10 / EF Core / xUnit (EF InMemory) no backend; OpenUI5 + TypeScript no frontend.

**Spec:** `docs/superpowers/specs/2026-11-13-weighing-ticket-cancel-unfinished-design.md`

## Global Constraints

- Papel checado com `IUserPermissions.HasRoleAsync(user, Domain.Constants.Roles.Admin)` (qualificado: o namespace `Application.Services.Roles` esconde a classe), nunca `HasAsync`.
- Mensagens ao usuário em pt-BR; identificadores em inglês.
- Sem migration. Ticket cancelado não é apagado.
- Ticket `Complete` com romaneio pendente: comportamento inalterado, sem exigir ADMIN.
- Branch `feature/weighing-ticket-cancel-unfinished` nos dois repos; commits no padrão `tipo(weighing): ...`; nunca push.

## Review Focus

- Ticket já `Cancelled` chamado de novo → recusa com mensagem clara (teste na Task 1).
- Não ADMIN chamando a API direto num ticket em andamento → recusa e ticket intacto (teste na Task 1).
- Usuário só com o flag legado `IsAdmin`, sem o papel → barrado (coberto por `HasRoleAsync`, já testado em `UserPermissionsServiceTests`).
- Ticket `Complete` sem romaneio/ com romaneio não pendente → mesmas mensagens de hoje (teste de regressão na Task 1).
- Tela aberta por não ADMIN → botão "Cancelar Ticket" ausente nas telas de andamento (verificação no navegador na Task 2).

---

### Task 1: Backend — cancelamento de ticket em andamento restrito a ADMIN

**Files:**
- Modify: `SiagroB1.Application/Services/WeighingTickets/WeighingTicketsCancelService.cs`
- Test (create): `SiagroB1.Application.Tests/WeighingTickets/WeighingTicketCancelUnfinishedTests.cs`

**Interfaces:**
- Produces: `WeighingTicketsCancelService(IUnitOfWork db, WeighingTicketsGetService getService, StorageTransactionsCancelService stCancel, IUserPermissions permissions, IStringLocalizer<Resource> resource)`; `ExecuteAsync(Guid key, string userName)` inalterado. O DI resolve o parâmetro novo sozinho (`IUserPermissions` já registrado).

- [ ] **Step 1: Testes que falham** (fixture no padrão de `WeighingTicketFreightPriceTests`: `TestDb.CreateUnitOfWork()`, fake `RolePermissions(params string[] roles)`):
  - `Admin_cancels_ticket_waiting_first_weighing` — ticket `Waiting`/`ReadyForFirstWeighing`; depois: `Status == Cancelled`, `Stage == Completed`, `UpdatedBy == "ana"`.
  - `Admin_cancels_ticket_in_processing` — ticket `Processing`/`ReadyForCompleting`; mesmo resultado.
  - `Non_admin_cannot_cancel_unfinished_ticket` — `BusinessException` com "Somente usuário com o papel ADMIN pode cancelar um ticket de pesagem não concluído."; ticket continua `Waiting`.
  - `Already_cancelled_ticket_is_refused` — `BusinessException` "Ticket de pesagem já está cancelado."
  - `Complete_ticket_without_storage_transaction_keeps_current_error` — ticket `Complete`, não ADMIN → `NotFoundException` (caminho atual).
- [ ] **Step 2:** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WeighingTicketCancelUnfinishedTests"` → falha (não compila / STORAGE_TRANSACTION_NOT_FOUND).
- [ ] **Step 3: Implementar** no início de `ExecuteAsync`, depois de carregar o ticket: `Cancelled` → exceção; `Waiting`/`Processing` → checa papel, seta `Status`, `Stage`, `UpdatedBy`, `UpdatedAt`, `SaveChangesAsync`, retorna; senão o código atual. As duas mensagens novas ficam literais em pt-BR no serviço (como em `WeighingCaptureValidator`).
- [ ] **Step 4:** mesmo comando → 5 passam; rodar também `--filter "FullyQualifiedName~WeighingTickets"` e `dotnet build SiagroB1.sln` → sem erro.
- [ ] **Step 5: Commit** `feat(weighing): permite ao ADMIN cancelar ticket de pesagem não concluído` (inclui spec e plano).

### Task 2: Frontend — botão só para ADMIN e mensagem corrigida

**Files (repo `siagro-b1-frontend`):**
- Modify: `webapp/controller/weighingTicket/GenericController.ts` (`onCancel` + flag)
- Modify: `webapp/controller/weighingTicket/completed/BaseController.ts` (texto do toast)
- Modify: `webapp/view/weighingTicket/{FirstWeighing,SecondWeighing,Confirm,Detail}.view.xml` (visible do botão que chama `.onCancel`)
- Modify: `package.json` (versão patch +1)

- [ ] **Step 1:** Expor `canCancelTicket = SessionService.hasRole("ADMIN")` de um jeito que valha nas quatro views (formatter no controller: `formatter: '.canCancelUnfinishedTicket'` sobre `Status`, ou propriedade no modelo `ui` se todas o tiverem — conferir antes) e ligar ao `visible` dos botões. Sem getter no controller (quebra no UI5).
- [ ] **Step 2:** Toast dos dois `onCancel` → "Ticket cancelado com sucesso."
- [ ] **Step 3:** `yarn ts-typecheck` e `yarn lint` → sem erro.
- [ ] **Step 4:** Verificar no navegador com a stack local: ADMIN vê o botão e cancela um ticket aguardando 1ª pesagem (some da lista); não ADMIN não vê o botão e a chamada direta da ação devolve 400 com a mensagem.
- [ ] **Step 5: Commit** `feat(weighing): mostra "Cancelar Ticket" em andamento só para ADMIN`.
