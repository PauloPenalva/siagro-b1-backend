# Transferência entre contas, conta sem filial e adiantamento sem contrato — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transferir dinheiro entre contas financeiras, permitir conta sem filial e incluir/abater adiantamento sem contrato.

**Architecture:** Transferência = entidade `FinancialAccountTransfer` + duas `FinancialAccountEntry` (−/+) ligadas por `TransferKey`, criadas/excluídas juntas numa transação; extrato e conciliação não mudam. Adiantamento sem contrato = `FinancialDocument` Nature=Advance com chaves de contrato nulas; `FinancialAdvancesCreateService` ganha um caminho sem contrato e `ApplyAdvance`/`GetAdvanceCandidates` passam a aceitar qualquer título em aberto do mesmo parceiro/direção/moeda quando o adiantamento não tem contrato.

**Tech Stack:** .NET 10, EF Core (SQL Server; testes em InMemory), OData v4 actions; OpenUI5 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-19-account-transfer-and-advances-without-contract-design.md`

**Worktrees:** backend `C:\Projetos\SiagroB1\be-acct-transfer` (branch `feature/account-transfer-and-free-advances`, já existe). Frontend: criar `C:\Projetos\SiagroB1\fe-acct-transfer` com o mesmo nome de branch a partir de `main` do `siagro-b1-frontend` e rodar `yarn install` próprio (nunca junction de node_modules). `git add` de todo arquivo novo logo ao criar. Commits em pt-BR conforme `CLAUDE.md` de cada repo; trailer `DB:` no commit da migration. Nunca push.

## Global Constraints

- Identificadores em inglês; textos ao usuário (labels, mensagens) em pt-BR.
- Valores numéricos das actions OData como `double` (o OData recusa decimal como string); arredondar com `decimal.Round(x, 2, MidpointRounding.ToEven)`.
- Parâmetros string de action OData são anuláveis — tratar `null`.
- Novo serviço registrado à mão em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (perto da linha 675).
- Transferência: mesma data nas duas pontas; mesma moeda; sem edição; exclusão bloqueada se qualquer ponta estiver conciliada.
- Adiantamento com contrato: comportamento atual intacto.
- Permissão: a de Contas Financeiras; nenhuma migration de menu.
- Migration aplicada em todas as empresas no deploy (anotar na mensagem do commit).

## Review Focus

1. Excluir uma ponta de transferência pela exclusão de lançamento avulso → deve ser recusado ("exclua pela transferência"). Teste na Task 2.
2. Abater adiantamento sem contrato em título de OUTRO parceiro ou direção oposta → recusado. Teste na Task 4.
3. Abater adiantamento sem contrato em outro adiantamento ou título cancelado → recusado. Teste na Task 4.
4. Adiantamento COM contrato tentando abater título sem contrato/de outro contrato → continua recusado (regressão). Teste na Task 4.
5. Salvar conta financeira com filial apagada envia `null`, não `""` (FK para Branch quebraria). Verificação na Task 6.

---

### Task 1: Entidade de transferência + migration

**Files:**
- Create: `SiagroB1.Domain/Entities/FinancialAccountTransfer.cs`
- Modify: `SiagroB1.Domain/Entities/FinancialAccountEntry.cs` (adicionar `TransferKey`)
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs:143` (DbSet)
- Create: migration `AddFinancialAccountTransfers` em `SiagroB1.Migrations` (AppDbContext)

**Interfaces — Produces:**
- `[Table("FINANCIAL_ACCOUNT_TRANSFERS")] class FinancialAccountTransfer : BaseEntity` com `FromAccountCode` / `ToAccountCode` (`VARCHAR(10) NOT NULL`, FK `FinancialAccount`, navegações `FromAccount`/`ToAccount`), `DateTime TransferDate`, `decimal Amount` (`DECIMAL(18,2)`, sempre positivo), `string Description` (`VARCHAR(200) NOT NULL`), `string? Notes` (`VARCHAR(500)`), `virtual ICollection<FinancialAccountEntry> Entries`. Índices em `TransferDate`, `FromAccountCode`, `ToAccountCode`.
- `FinancialAccountEntry.TransferKey : Guid?` (FK para `FinancialAccountTransfer`, `[Index]`), navegação `Transfer`.
- `DbSet<FinancialAccountTransfer> FinancialAccountTransfers`.

- [ ] **Step 1:** Criar entidade e propriedade; FKs para conta com `DeleteBehavior.NoAction`/Restrict seguindo o padrão das outras FKs de `FinancialAccount` no `AppDbContext` (conferir a memória "Restrict e NoAction não são intercambiáveis").
- [ ] **Step 2:** `dotnet ef migrations add AddFinancialAccountTransfers --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web` — conferir que só cria a tabela, a coluna `TransferKey` e índices/FKs.
- [ ] **Step 3:** `dotnet build SiagroB1.sln` → 0 erros.
- [ ] **Step 4:** Commit `feat(financial): adicionar tabela de transferência entre contas` com trailer `DB: AddFinancialAccountTransfers`.

### Task 2: Serviços de criar e excluir transferência

**Files:**
- Create: `SiagroB1.Application/Services/Financials/FinancialAccountTransfersCreateService.cs`
- Create: `SiagroB1.Application/Services/Financials/FinancialAccountTransfersDeleteService.cs`
- Modify: `SiagroB1.Application/Services/Financials/FinancialAccountEntriesDeleteService.cs`
- Test: `SiagroB1.Application.Tests/Financials/FinancialAccountTransfersServiceTests.cs` (padrão de `FinancialAccountEntriesServiceTests`: `TestDb.CreateUnitOfWork()`)

**Interfaces — Produces:**
- `FinancialAccountTransfersCreateService(IUnitOfWork db).ExecuteAsync(FinancialAccountTransfer transfer, string userName, CommitMode commitMode = CommitMode.Auto) : Task<FinancialAccountTransfer>`
- `FinancialAccountTransfersDeleteService(IUnitOfWork db).ExecuteAsync(Guid key) : Task`

- [ ] **Step 1: Testes que falham**
  - `Creates_transfer_with_two_entries_on_same_date`: BCO1→CX1, 1.000,00, data 2026-10-05 14:30 → 1 transfer; entries `BCO1 = -1000.00` e `CX1 = +1000.00`, ambas `EntryDate = 2026-10-05`, mesmo `TransferKey`; descrições `"Transferência para CX1"` e `"Transferência de BCO1"` (+ `" - " + Description` quando informada).
  - `Rejects_same_account` → `ApplicationException` "A conta de origem e a de destino precisam ser diferentes."
  - `Rejects_different_currency` (CX1 em Usd) → mensagem contém "mesma moeda".
  - `Rejects_inactive_account` (origem ou destino) → "inativa".
  - `Rejects_zero_or_negative_amount` (0 e −5) → "maior que zero".
  - `Allows_accounts_of_different_branches`.
  - `Delete_removes_transfer_and_both_entries`.
  - `Delete_blocked_when_any_entry_reconciled` (ponta destino com `ReconciliationKey`) → "conciliad".
  - `Entry_delete_rejects_transfer_leg` (Review Focus 1): `FinancialAccountEntriesDeleteService` numa ponta → "Lançamento de transferência: exclua pela transferência."
- [ ] **Step 2:** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialAccountTransfersServiceTests"` → FAIL (tipos inexistentes).
- [ ] **Step 3:** Implementar. Códigos de conta `Trim().ToUpperInvariant()`; data `.Date`; transfer e entries gravados com `BeginTransactionAsync/CommitAsync` quando `CommitMode.Auto` (memória: Rollback sem Begin é no-op). Delete: remove entries e transfer juntos. Em `FinancialAccountEntriesDeleteService` acrescentar a trava de `TransferKey`.
- [ ] **Step 4:** Rodar os testes → PASS; rodar também `FinancialAccountEntriesServiceTests` → PASS.
- [ ] **Step 5:** Registrar os dois serviços no DI; commit `feat(financial): transferir entre contas financeiras`.

### Task 3: API OData da transferência

**Files:**
- Create: `SiagroB1.Web/Actions/Financials/FinancialAccountTransfersCreateController.cs` (espelhar `FinancialAccountEntriesCreateController`)
- Create: `SiagroB1.Web/Controllers/FinancialAccountTransfersController.cs` (espelhar `FinancialAccountEntriesController`: `Get` com `[EnableQuery]` e `Delete`)
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs:~1677/1711`

**Interfaces — Produces:**
- Entity set `FinancialAccountTransfers` (expand `FromAccount`, `ToAccount`, `Entries`).
- Action `FinancialAccountTransfersCreate(FromAccountCode: string, ToAccountCode: string, TransferDate: string, Amount: double, Description: string?, Notes: string?) → Guid`.

- [ ] **Step 1:** Teste de EDM no padrão de `FinancialAdvanceApplicationEdmModelTests`: o modelo contém o entity set e a action com esses parâmetros (`Description`/`Notes` opcionais). Rodar → FAIL.
- [ ] **Step 2:** Implementar controllers e config. Faltando data/valor → 400 "Informe a data da transferência." / "Informe o valor da transferência."
- [ ] **Step 3:** Teste → PASS; `dotnet build` → 0 erros.
- [ ] **Step 4:** Commit `feat(financial): expor transferência entre contas na API`.

### Task 4: Adiantamento sem contrato no backend

**Files:**
- Modify: `SiagroB1.Application/Services/Financials/FinancialAdvancesCreateService.cs`
- Modify: `SiagroB1.Application/Services/Financials/FinancialDocumentsApplyAdvanceService.cs`
- Modify: `SiagroB1.Application/Services/Financials/FinancialDocumentsGetAdvanceCandidatesService.cs`
- Modify: `SiagroB1.Web/Actions/Financials/FinancialAdvancesCreateController.cs` + `ODataConfigurations.cs` (parâmetros novos opcionais)
- Test: `FinancialAdvancesCreateServiceTests.cs`, novo `FinancialAdvancesWithoutContractTests.cs` (usar `AdvanceApplicationSeed` onde servir)

**Interfaces:**
- Novo método `FinancialAdvancesCreateService.ExecuteWithoutContractAsync(FinancialDirection direction, string cardCode, string branchCode, decimal amount, DateTime dueDate, string financialAccountCode, DateTime paymentDate, string? comments, string userName, CommitMode commitMode = CommitMode.Auto) : Task<FinancialDocument>`. O `ExecuteAsync` atual fica igual; extrair a parte comum (validação de conta, numeração, criação + baixa) num método privado.
- Action `FinancialAdvancesCreate`: `ContractType`/`ContractKey` passam a opcionais; novos opcionais `Direction` (`"Payable"|"Receivable"`), `CardCode`, `BranchCode`. Controller: com `ContractKey` → `ExecuteAsync`; sem → `ExecuteWithoutContractAsync`.

- [ ] **Step 1: Testes que falham**
  - `Creates_advance_without_contract_paid_on_account`: Payable, PN "F0001", filial "01", 500,00, conta BCO1 (Brl) → documento Advance, chaves de contrato nulas, `Currency = Brl` (da conta), `BranchCode = "01"`, `OriginDocNumber` vazio/nulo, uma baixa de 500 na BCO1.
  - `Without_contract_requires_partner` / `requires_branch` → "Informe o parceiro" / "Informe a filial".
  - `Without_contract_rejects_unknown_partner` → `NotFoundException` (usar `IBusinessPartnerService.GetByIdAsync` retornando null).
  - Apply (`FinancialAdvancesWithoutContractTests`):
    - `Applies_free_advance_to_firm_title_of_same_partner` (título Nature Firm, sem contrato, mesmo CardCode/direção/moeda) → baixa `AdvanceApplication` nos dois, saldo do título cai.
    - também a título Manual e a Provisional com contrato → PASS.
    - Rejeições: outro parceiro ("outro parceiro"), direção oposta ("direção"), alvo Advance ("adiantamento"), alvo cancelado, moeda diferente.
    - Regressão (Review Focus 4): adiantamento COM contrato contra título Firm sem contrato → "não pertence ao contrato".
  - Candidates: para um título Firm do PN, lista os adiantamentos SEM contrato do mesmo PN/direção/moeda com crédito > 0; para um Provisional de contrato, lista os do contrato **e** os sem contrato do PN.
  - Relink (Review Focus — decisão 2): `FinancialAdvancesRelinkContractService` em adiantamento sem contrato parcialmente aplicado move só o disponível; sem disponível → recusa. Se já funciona, o teste só trava o comportamento.
- [ ] **Step 2:** Rodar `--filter "FullyQualifiedName~FinancialAdvances|FullyQualifiedName~ApplyAdvance|FullyQualifiedName~AdvanceCandidates"` → novos FAIL.
- [ ] **Step 3:** Implementar.
  - ApplyAdvance: carregar o adiantamento antes do alvo-check. Se o adiantamento tem contrato → regras atuais (alvo Provisional com contrato, `HasSameContractAs`). Se não tem → alvo: não cancelado, `Nature != Advance`, mesmo `CardCode` (case-insensitive), mesma `Direction`, mesma moeda; teto `AvailableToSettleAmount` igual.
  - Candidates: alvo cancelado ou Advance → `[]`. Sempre incluir adiantamentos sem contrato do mesmo CardCode/Direction/moeda com crédito; se alvo é Provisional com contrato, unir os do contrato.
  - Conferir guardas que usam contrato (`FinancialDocumentsContractCancellationGuardService`, close/cancel de contratos) — não devem quebrar com chaves nulas; adicionar teste só se houver mudança.
- [ ] **Step 4:** Rodar a suíte `SiagroB1.Application.Tests` inteira → PASS.
- [ ] **Step 5:** Commit `feat(financial): permitir adiantamento sem contrato`.

### Task 5: Frontend — transferência

**Files (repo frontend, worktree `fe-acct-transfer`):**
- Modify: `webapp/view/financialAccounts/Main.view.xml` + `webapp/controller/financialAccounts/Main.controller.ts` (botão "Transferir" e "Transferências")
- Create: `webapp/view/financialAccounts/fragments/TransferDialog.fragment.xml`
- Create: `webapp/view/financialAccounts/fragments/TransfersDialog.fragment.xml` (lista com excluir)
- Modify: `webapp/model/ServerRoutes.ts` (`financialAccountTransfersCreate`, `financialAccountTransfers`)

- [ ] **Step 1:** Diálogo "Transferência entre contas": Conta de origem*, Conta de destino* (value help de contas ativas), Data* (padrão hoje em data local — memória toISOString é UTC), Valor*, Descrição, Observação. Envia a action com `Amount` como Number. Mensagem de sucesso "Transferência registrada."
- [ ] **Step 2:** Diálogo "Transferências": tabela (Data, Origem, Destino, Valor, Descrição) via `FinancialAccountTransfers?$orderby=TransferDate desc`, filtro por período; botão Excluir com confirmação "Excluir a transferência e os dois lançamentos?"; mostra o erro do backend se conciliada.
- [ ] **Step 3:** `yarn ts-typecheck` e `yarn lint` → sem erros.
- [ ] **Step 4:** Commit `feat(financial): tela de transferência entre contas`.

### Task 6: Frontend — filial opcional na conta

**Files:** `webapp/view/financialAccounts/fragments/Form.fragment.xml:54-61`, `webapp/controller/financialAccounts/BaseController.ts`/`Edit.controller.ts`

- [ ] **Step 1:** Trocar `valueHelpOnly="true"` por campo que permita limpar (manter value help; aceitar texto vazio) e, ao salvar, converter `""` em `null` (Review Focus 5).
- [ ] **Step 2:** `yarn ts-typecheck` + `yarn lint` → OK.
- [ ] **Step 3:** Commit `fix(financial): permitir conta financeira sem filial`.

### Task 7: Frontend — adiantamento sem contrato

**Files:** `webapp/view/financialAdvances/fragments/CreateAdvanceDialog.fragment.xml`, `webapp/controller/financialAdvances/Main.controller.ts:78-150`, `webapp/view/financialAdvances/Main.view.xml`, `webapp/view/financialDocuments/fragments/Settlements.fragment.xml:43-46`

- [ ] **Step 1:** No diálogo, Select "Vínculo": "Com contrato" (padrão, campos atuais) / "Sem contrato" (Direção A pagar/A receber, Parceiro* com value help de PN, Filial* — sugerida pela filial da conta escolhida, Observação). Contrato deixa de ser `required` quando "Sem contrato". Envia `Direction`, `CardCode`, `BranchCode` e omite `ContractType`/`ContractKey`.
- [ ] **Step 2:** Lista de adiantamentos: coluna Contrato vazia quando nula; filtro "Contrato": Todos / Com contrato / Sem contrato (`PurchaseContractKey eq null and SalesContractKey eq null`).
- [ ] **Step 3:** Botão "Compensar adiantamento" no Detail do título: visível para qualquer título não cancelado, `Nature !== 'Advance'` e `AvailableToSettleAmount > 0` (o backend devolve candidatos vazios quando não há; manter a mensagem atual de "nenhum adiantamento").
- [ ] **Step 4:** `yarn ts-typecheck` + `yarn lint` → OK.
- [ ] **Step 5:** Commit `feat(financial): incluir e compensar adiantamento sem contrato`.

### Task 8: Verificação ponta a ponta

- [ ] Subir a stack local (memória "Subir a stack local"; aplicar a migration numa **cópia** do banco local — memória sobre banco com migration de outro branch), e pelo caminho do usuário a partir da home: criar transferência, ver as duas pontas no extrato de cada conta, excluir; salvar conta sem filial; incluir adiantamento sem contrato a pagar e abatê-lo num título manual do mesmo PN. Derrubar os processos pelo PID ao terminar.
- [ ] `dotnet test` completo no backend → PASS.
