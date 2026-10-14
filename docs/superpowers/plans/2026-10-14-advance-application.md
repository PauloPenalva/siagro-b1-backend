# Compensação de adiantamento contra o provisório (compra) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** O operador compensa, à mão, o crédito de um adiantamento a contrato de compra contra o provisório da fixação do mesmo contrato (até o composto ainda não baixado), com lançamento em par nos dois títulos; devolução e revínculo passam a respeitar o que já foi aplicado (devolve/transfere só o crédito, desmembrando o adiantamento no revínculo) e o encerramento do contrato recusa crédito sobrando.

**Architecture:** O adiantamento ganha a coluna persistida-derivada `AppliedAmount` e o recalculador (escritor único) passa a separar o ledger do adiantamento em dois eixos — **pago** (`SettledAmount`) e **aplicado** (`AppliedAmount`) — classificando cada linha pela origem (e o estorno pela origem da linha estornada), com as linhas pendentes do ChangeTracker. A compensação é um serviço enqueue-only no molde do encontro de contas (`FinancialDocumentsNettingService`): duas linhas `AdvanceApplication`, recálculo e log nos dois, um `SaveChanges`. O estorno genérico aprende os pares `AdvanceApplication`/`AdvanceTransfer` (mesma busca do `Netting`), a trava A7 e o cancelamento do adiantamento nascido do desmembramento.

**Tech Stack:** .NET 10, EF Core (SQL Server; InMemory nos testes), ASP.NET Core OData v4, xUnit; OpenUI5 1.141 + TypeScript, QUnit.

**Spec:** `siagro-b1-backend/docs/superpowers/specs/2026-10-14-advance-application-design.md` (autoridade; regras 1–12, mensagens A1–A11, commit `9e06ef1`). Contexto: `2026-10-13-purchase-firm-payables-design.md` (composição, `AvailableToSettleAmount`, encontro de contas em par, recálculo com pendentes).

## Global Constraints

- Dois repositórios: `C:\Projetos\SiagroB1\siagro-b1-backend` (Tasks 1–8) no branch **`feature/advance-application`** (já existe, a partir de `main`) e `C:\Projetos\SiagroB1\siagro-b1-frontend` (Tasks 9–10) no branch **`feature/advance-application`**, criado a partir de `main` (`03cb330`) na Task 9. `git branch --show-current` antes de cada `git add`. **Nunca push. Nunca merge.**
- Todo arquivo novo: `git add <path>` logo depois de criado, no repo dono. O último passo de cada task é **stage** (`git add`) — o **controlador** commita com a mensagem dada (o `git commit` de subagente é bloqueado nesta sessão).
- Mensagem de commit: `tipo(escopo): descrição pt-BR, imperativo, minúscula, sem ponto`; escopo `financial` (ou `purchase-contract` no encerramento); corpo com o PORQUÊ; trailer `DB: AddFinancialDocumentAppliedAmount` **só** no commit da Task 1 (a única migration); terminar com
  `Co-Authored-By: {MODELO} <noreply@anthropic.com>` e
  `Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT`,
  onde `{MODELO}` é o nome exato do modelo que de fato escreveu o código da task (ex.: `Claude Opus 5.5 (1M context)`).
- Identificadores em inglês; texto ao usuário (mensagens, rótulos) em pt-BR; comentários em pt-BR.
- Enums: valor novo **no fim**, nunca renumerar — `FinancialSettlementOrigin.AdvanceTransfer = 7`. `FinancialSettlementOrigin.AdvanceApplication = 3` e `FinancialDocumentOrigin.FinancialDocument = 5` já existem e passam a ser escritos.
- Modelo: `FINANCIAL_DOCUMENTS.AppliedAmount DECIMAL(18,2) DEFAULT 0` (só no adiantamento; escritor único = recalculador; exposta no EDM por ser persistida, **sem** `AddProperty`). `[NotMapped] AvailableAdvanceAmount = Nature == Advance ? Max(0, Round(SettledAmount − AppliedAmount, 2, ToEven)) : 0`. Sem backfill.
- Critério entrada × saída da `AdvanceTransfer` (só dados do próprio documento): a linha é de **entrada** quando `linha.Origin == AdvanceTransfer && documento.OriginType == FinancialDocumentOrigin.FinancialDocument && documento.OriginKey == linha.RelatedDocumentKey` → conta no **pago**. Toda outra `AdvanceTransfer` é de **saída** → conta no **aplicado**.
- Dinheiro: `decimal.Round(x, 2, MidpointRounding.ToEven)` **antes** de comparar com os tetos. Valores em mensagens: `ToString("N2", CultureInfo.GetCultureInfo("pt-BR"))`. Moeda em mensagem: `FinancialDocumentsContractCancellationGuardService.CurrencySymbol(...)` (R$/US$).
- Migrations: **só** `dotnet ef migrations add` (não toca banco). **Nunca** `dotnet ef database update` sem `ASPNETCORE_ENVIRONMENT=Ceagui-Development` explícito e conferido (CEAGUI dev: `CEAGUI_SIAGRO_DEV` / `CEAGUI_SIAGRO_COMMON_DEV`) — só na Task 11; nunca o profile `db-migration`. Migrations antigas não são reescritas.
- Processos (Web/Gateway/dev server): matar **só por PID** (`Get-NetTCPConnection -LocalPort <p> -State Listen` → `Stop-Process -Id <pid> -Force`). Nunca `taskkill /IM node.exe`/`dotnet.exe`. Conferir as portas de novo no fim (o file watcher ressuscita processo).
- Armadilhas conhecidas (valem para todas as tasks):
  - `--` dentro de comentário XML mata o fragmento inteiro sem erro em gate nenhum — **não** escrever `--` em `<!-- -->`.
  - Formatter em fragmento: `core:require` **no próprio fragmento** (`formatter.x`), ou `.formatter.x` onde o controller expõe `formatter = { ...formatter }` (seção Financeiro do contrato) — nunca misturar os dois no mesmo arquivo.
  - Enum no binding OData V4: `targetType: 'any'`; filtro de enum: `$filter` cru em string.
  - `Edm.Decimal` chega como **string** no modelo V4 e no JSON das functions: sempre `Number(x)` antes de comparar/somar/sugerir (em expression binding a comparação `> 0` coage a string, como o código existente já faz com `SettledAmount`).
  - Parâmetro de dinheiro em action: `Edm.Double` (`Parameter<double>`), nunca `Edm.Decimal`; data como **string** ISO `yyyy-MM-dd` (`Parameter<string>`); data padrão na tela por `localIsoDate(new Date())` (nunca `toISOString`, que vira amanhã depois das 21:00).
  - Recusa de negócio é `BadRequest(string)`; o frontend aceita as três formas (`string`, `.message`, `.error.message`) — chamar as actions novas por `sendJson` de `helpers/FetchHelpers.ts` (usa `readErrorMessage`).
  - `ODataActionParameters` chega **nulo** quando o corpo não casa com o EDM: guardar `parameters is null` antes de tudo; parâmetro presente pode vir `null` (`value?.ToString()`).
  - DTO de function: `[JsonPropertyName("PascalCase")]` em **toda** propriedade (sem isso sai camelCase e a tela lê `undefined`).
  - `DocNumberSequenceService` usa conexão Dapper própria com `UPDLOCK`: buscar o número **antes** do `SaveChanges` e só depois de todas as validações (nos testes: `FakeDocNumberSequenceService`).
  - Recalculador é **enqueue-only** e soma linhas **pendentes** do ChangeTracker (Added soma, Deleted subtrai): compensar e desmembrar gravam linhas + recálculo + log num `SaveChanges` só, sem salvar no meio.
  - InMemory não impõe FK/índice único: os testes conferem o efeito (linhas, saldos, status), não só a ausência de exceção.
- Mensagens EXATAS (copiar literalmente; `{x}` = valor `N2` pt-BR; códigos = `Code` do título):
  - **Do spec:**
    - A1 `Título {código} não é provisório de contrato de compra.`
    - A2 `Adiantamento {código} não pertence ao contrato do título {provisório}.`
    - A3 `O adiantamento {código} está em {moeda} e o provisório {provisório} em {moeda do provisório}: compensação entre moedas diferentes não é suportada.`
    - A4 `Informe um valor maior que zero.`
    - A5 `Valor acima do disponível para baixa do título {provisório} ({x}).`
    - A6 `Valor acima do crédito disponível do adiantamento {código} ({x}).`
    - A7 `O adiantamento {código} tem {x} aplicado: desfaça as compensações antes de estornar o pagamento.`
    - A8 `O adiantamento {código} não tem crédito disponível para devolver.`
    - A9 `O adiantamento {código} tem {x} de crédito não aplicado: compense, devolva ou vincule a outro contrato antes de encerrar.`
    - A10 `O adiantamento {novo} já foi usado: desfaça as compensações dele antes de estornar a transferência.`
    - A11 `Título cancelado não pode ser compensado.`
    - Motivo de cancelamento do adiantamento novo (regra 9): `Transferência de crédito estornada`
  - **Decididas neste plano (o spec não deu texto):**
    - P1 `O título {código} não é um adiantamento.` (AdvanceKey aponta para outra natureza)
    - P2 `O adiantamento {código} não tem crédito disponível para vincular a outro contrato.` (desmembrar sem crédito)
    - P3 `Lançamento par da compensação de adiantamento não encontrado.` · `Lançamento par da transferência de adiantamento não encontrado.`
    - P4 (comentário do adiantamento novo) `Crédito não aplicado do adiantamento {código}.`
    - P5 (controller) `Informe o título provisório.` · `Informe o adiantamento.` · `Informe o valor a compensar.` · `Informe a data da compensação.`
    - P6 (tela) `Nenhum adiantamento com crédito disponível neste contrato.` · `Selecione o adiantamento.` · `Adiantamento compensado.` · `O adiantamento não tem crédito disponível para devolver.` · `O adiantamento não tem crédito disponível para vincular a outro contrato.`
    - Campos de log (códigos): `AdvanceApplication` ("Compensação de adiantamento") e `AdvanceTransfer` ("Transferência de adiantamento"); valor do log `"{código do outro título}: {valor N2 pt-BR}"` (mesmo `FinancialDocumentsComposeService.Describe`); `OldValue` nulo = feito, `NewValue` nulo = desfeito.
- Comandos de teste (raiz do backend): `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Classe>"`. Frontend (raiz, dev server **parado**): `yarn ts-typecheck && yarn lint`; QUnit pelo navegador com `yarn start` em `http://localhost:8080/test/Test.qunit.html?testsuite=test-resources/siagrob1/testsuite.qunit&test=unit/unitTests` (o `yarn test` inteiro falha no gate de cobertura — não é regressão).

## Review Focus

1. **Duas compensações do mesmo crédito em sequência (duplo clique, ou dois provisórios do mesmo contrato)** — a segunda tem de esbarrar no crédito que sobrou (A6), nunca deixar o adiantamento com aplicado > pago. Teste `Second_application_beyond_the_remaining_credit_is_refused` (Task 2); concorrência real pelo `RowVersion` dos dois títulos (ambos são modificados no mesmo `SaveChanges`); a tela trava reentrância por `runDocumentAction`/`_actionRunning` (Task 9).
2. **Valor com mais de duas casas vindo do `Edm.Double`** (ex.: 4000.004, ou 0.004) — espera-se arredondar para centavos antes dos tetos: 4000.004 grava 4.000,00 e 0.004 vira A4. Testes `Amount_is_rounded_to_cents_before_the_ceilings` e `Non_positive_amount_is_refused` (Task 2).
3. **Estornar o pagamento do adiantamento original depois do desmembramento** — a transferência de saída conta como aplicado, então o estorno tem de ser recusado por A7 (senão o novo adiantamento ficaria com crédito sem dinheiro por trás). Teste `Reversing_the_payment_of_a_split_advance_is_refused` (Task 5).
4. **Desfazer o desmembramento depois de compensar e estornar a compensação no adiantamento novo** — as linhas compensação + estorno se anulam; o estorno da transferência tem de passar (o critério é aplicado = 0 e pago = transferência, não "existe outra linha"). Teste `Undone_applications_free_the_transfer_reversal` (Task 5); e o adiantamento novo **devolvido** (cancelado) bloqueia o estorno da transferência pela recusa existente de título cancelado — teste `Refunded_split_advance_blocks_the_transfer_reversal` (Task 5).
5. **Encerrar contrato com adiantamento parcialmente devolvido** (fica `PartiallySettled`, crédito 0) e com adiantamento **não pago** (crédito 0) — nenhum dos dois pode travar o encerramento; só crédito > 0 trava. Testes `Closing_after_refunding_the_credit_passes` e `Unpaid_advance_does_not_block_the_closing` (Task 6).

## Decisões do plano (lacunas do spec)

- **Classificação no recalculador:** o adiantamento lê o próprio ledger em memória (banco sem as linhas Deleted pendentes + as Added pendentes) e classifica cada linha: `AdvanceApplication` e `AdvanceTransfer` de saída → aplicado; `Reversal` → o eixo da linha que ele estorna (a espelho fica no mesmo título); resto (Manual, AdvanceRefund, transferência de entrada) → pago. O provisório segue a soma simples (a `AdvanceApplication` entra no `SettledAmount`).
- **Ordem das pré-condições (regra 2):** provisório (A1 natureza → A11 cancelado → A1 sem contrato de compra) → adiantamento (P1 natureza → A11 cancelado → A2 contrato) → A3 → A4 → A5 → A6. `Amount` é arredondado antes de A4.
- **Desfazer o desmembramento (A10):** recusa quando o novo tem `AppliedAmount ≠ 0` **ou** `SettledAmount ≠ valor da transferência de entrada` — "outras baixas" é lido como efeito líquido, para que compensações já estornadas não travem para sempre.
- **Log:** campos novos `AdvanceApplication` e `AdvanceTransfer` em `FinancialDocumentChangeLogFields`; compensar/desmembrar logam nos dois títulos (`OldValue` nulo) e o estorno do par loga o desfazer nos dois (`NewValue` nulo), como a composição.
- **Devolução:** A8 substitui a mensagem antiga "não tem valor pago: não há o que devolver" (o crédito 0 cobre o não pago); o teste existente passa a conferir A8.
- **Revínculo desmembrado:** valida o destino como hoje antes de desmembrar; o adiantamento novo copia `Direction`, `CardCode`/`CardName`, `BranchCode`, `Currency`, `DueDate`, `PaymentTermsText` do original; `Comments` = P4; `OriginDocNumber` = código do contrato destino. O número só é buscado depois de todas as validações.
- **Candidatos (function nova `FinancialDocumentsGetAdvanceCandidates`):** serviço próprio `FinancialDocumentsGetAdvanceCandidatesService` (não mexe em `GetComposition`); só adiantamentos não cancelados do mesmo contrato **e mesma moeda** com crédito > 0; título que não é provisório de compra ativo → lista vazia (não erro).
- **Encerramento:** A9 roda depois das travas de firme/provisório já existentes (a mensagem mais acionável vem primeiro: quitar o provisório).
- "Estornar baixa" já aparece para `AdvanceApplication`/`AdvanceTransfer` na tela (só `InvoiceOffset`, `Reversal` e `AdvanceRefund` o escondem) — nada a mudar no botão.

---

## File Structure

Backend (`siagro-b1-backend`):

| Arquivo | Responsabilidade |
|---|---|
| Modify `SiagroB1.Domain/Enums/FinancialSettlementOrigin.cs` | `AdvanceTransfer = 7` |
| Modify `SiagroB1.Domain/Entities/FinancialDocument.cs` | `AppliedAmount`, `AvailableAdvanceAmount` novo |
| Modify `SiagroB1.Domain/Entities/FinancialDocumentChangeLogFields.cs` | `AdvanceApplication`, `AdvanceTransfer` |
| Create migration `SiagroB1.Migrations/AppContext/*_AddFinancialDocumentAppliedAmount` | coluna |
| Modify `SiagroB1.Application/Services/Financials/FinancialDocumentsRecalculateBalanceService.cs` | dois eixos do adiantamento |
| Create `SiagroB1.Application/Services/Financials/FinancialDocumentsApplyAdvanceService.cs` | regras 1–4 |
| Modify `…/FinancialDocumentsReverseSettlementService.cs` | regras 5, 6, 9 |
| Modify `…/FinancialAdvancesRefundService.cs` | regra 7 |
| Modify `…/FinancialAdvancesRelinkContractService.cs` | regra 8 |
| Modify `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsCloseService.cs` | regra 10 |
| Create `…/FinancialDocumentsGetAdvanceCandidatesService.cs`, `SiagroB1.Domain/Dtos/FinancialDocumentAdvanceCandidateDto.cs`; Modify `…/FinancialDocumentsGetByContractService.cs`, `SiagroB1.Domain/Dtos/FinancialDocumentByContractDto.cs` | leituras |
| Create `SiagroB1.Web/Actions/Financials/FinancialDocumentsApplyAdvanceController.cs`, `SiagroB1.Web/Functions/Financials/FinancialDocumentsGetAdvanceCandidatesController.cs`; Modify `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` | endpoints e DI |
| Tests `SiagroB1.Application.Tests/Financials/AdvanceApplicationSeed.cs` + `FinancialDocumentsAdvanceRecalculateTests.cs`, `FinancialDocumentsApplyAdvanceTests.cs`, `FinancialDocumentsAdvanceApplicationReverseTests.cs`, `FinancialAdvancesRefundApplicationTests.cs`, `FinancialAdvancesRelinkSplitTests.cs`, `PurchaseContractsCloseAdvanceCreditTests.cs`, `FinancialDocumentsAdvanceReadTests.cs`, `FinancialAdvanceApplicationEdmModelTests.cs`; Modify `FinancialAdvancesRefundServiceTests.cs`, `FinancialAdvancesRelinkContractServiceTests.cs`, `FinancialDocumentsContractCancellationGuardServiceTests.cs` | |

Frontend (`siagro-b1-frontend/webapp`):

| Arquivo | Responsabilidade |
|---|---|
| Modify `model/ServerRoutes.ts`; Create `types/FinancialAdvanceCandidate.ts` | rotas e tipo |
| Create `view/financialDocuments/fragments/ApplyAdvanceDialog.fragment.xml`; Modify `fragments/Settlements.fragment.xml`, `controller/financialDocuments/Detail.controller.ts` | "Compensar adiantamento" no provisório |
| Modify `model/formatter.ts`, `test/unit/model/formatter.qunit.ts` | origem e campos de log |
| Modify `view/financialDocuments/fragments/Form.fragment.xml`, `Detail.view.xml`, `RefundAdvanceDialog.fragment.xml`, `RelinkContractDialog.fragment.xml`, `controller/financialDocuments/Detail.controller.ts` | adiantamento: aplicado, crédito, devolver, revincular |
| Modify `view/financialAdvances/Main.view.xml` | coluna Aplicado |
| Modify `view/purchaseContracts/fragments/PurchaseContractFinancialDocuments.fragment.xml`, `types/FinancialDocumentByContract.ts` | seção Financeiro |

---

### Task 1: Modelo — `AdvanceTransfer`, `AppliedAmount`, crédito disponível, migration e recalculador em dois eixos

**Files:**
- Modify: `SiagroB1.Domain/Enums/FinancialSettlementOrigin.cs`
- Modify: `SiagroB1.Domain/Entities/FinancialDocument.cs` (depois de `ComposedAmount`; e o `AvailableAdvanceAmount` no fim)
- Modify: `SiagroB1.Domain/Entities/FinancialDocumentChangeLogFields.cs`
- Modify: `SiagroB1.Application/Services/Financials/FinancialDocumentsRecalculateBalanceService.cs`
- Create: `SiagroB1.Migrations/AppContext/<timestamp>_AddFinancialDocumentAppliedAmount.cs` (+ `.Designer.cs`, `AppDbContextModelSnapshot.cs` modificado)
- Test: `SiagroB1.Application.Tests/Financials/FinancialDocumentsAdvanceRecalculateTests.cs`

**Interfaces:**
- Consumes: nada de tasks anteriores.
- Produces:
  - `FinancialSettlementOrigin.AdvanceTransfer = 7`.
  - `FinancialDocument.AppliedAmount` (`decimal`, persistida) e `FinancialDocument.AvailableAdvanceAmount` (fórmula nova).
  - `FinancialDocumentChangeLogFields.AdvanceApplication = "AdvanceApplication"`, `FinancialDocumentChangeLogFields.AdvanceTransfer = "AdvanceTransfer"`.
  - `FinancialDocumentsRecalculateBalanceService.RecalculateAsync(AppDbContext, Guid)` escreve `SettledAmount` (pago) e `AppliedAmount` (aplicado) no adiantamento.
  - `internal static bool FinancialDocumentsRecalculateBalanceService.IsIncomingTransfer(FinancialDocument advance, FinancialSettlement line)` (usado pelo estorno, Task 5).

- [ ] **Step 1: Testes que falham**

`SiagroB1.Application.Tests/Financials/FinancialDocumentsAdvanceRecalculateTests.cs`:

```csharp
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Spec 2026-10-14 (compensação de adiantamento): o adiantamento tem dois eixos — PAGO (SettledAmount) e APLICADO
/// (AppliedAmount) —, e o crédito disponível é pago − aplicado. O provisório segue somando tudo no baixado.
/// </summary>
public class FinancialDocumentsAdvanceRecalculateTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<FinancialDocument> AddDocumentAsync(
        FinancialDocumentNature nature, decimal netAmount,
        FinancialDocumentOrigin originType = FinancialDocumentOrigin.Manual, Guid? originKey = null)
    {
        var document = new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = $"FN-{Guid.NewGuid():N}"[..12], CardCode = "F0001",
            Direction = FinancialDirection.Payable, Nature = nature, Status = FinancialDocumentStatus.Open,
            DueDate = new DateTime(2026, 12, 31), NetAmount = netAmount, Currency = CurrencyType.Brl,
            OriginType = originType, OriginKey = originKey,
        };
        _db.Context.FinancialDocuments.Add(document);
        await _db.Context.SaveChangesAsync();
        return document;
    }

    private static FinancialSettlement Line(
        FinancialDocument document, decimal amount, FinancialSettlementOrigin origin,
        Guid? related = null, Guid? reversed = null) => new()
    {
        Key = Guid.NewGuid(), FinancialDocumentKey = document.Key, SettlementDate = new DateTime(2026, 10, 14),
        Amount = amount, Origin = origin, RelatedDocumentKey = related, ReversedSettlementKey = reversed,
    };

    private async Task SaveLinesAsync(params FinancialSettlement[] lines)
    {
        _db.Context.FinancialSettlements.AddRange(lines);
        await _db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Paid_and_applied_axes_are_split()
    {
        var advance = await AddDocumentAsync(FinancialDocumentNature.Advance, 10_000m);
        await SaveLinesAsync(
            Line(advance, 10_000m, FinancialSettlementOrigin.Manual),
            Line(advance, 3_000m, FinancialSettlementOrigin.AdvanceApplication, related: Guid.NewGuid()),
            Line(advance, 2_000m, FinancialSettlementOrigin.AdvanceTransfer, related: Guid.NewGuid()));

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, advance.Key);

        Assert.Equal(10_000m, advance.SettledAmount);
        Assert.Equal(5_000m, advance.AppliedAmount);
        Assert.Equal(5_000m, advance.AvailableAdvanceAmount);
        Assert.Equal(FinancialDocumentStatus.Settled, advance.Status);
    }

    [Fact]
    public async Task Reversal_follows_the_axis_of_the_reversed_line()
    {
        var advance = await AddDocumentAsync(FinancialDocumentNature.Advance, 10_000m);
        var application = Line(advance, 3_000m, FinancialSettlementOrigin.AdvanceApplication, related: Guid.NewGuid());
        var secondPayment = Line(advance, 2_000m, FinancialSettlementOrigin.Manual);
        await SaveLinesAsync(
            Line(advance, 8_000m, FinancialSettlementOrigin.Manual),
            secondPayment,
            application,
            Line(advance, -3_000m, FinancialSettlementOrigin.Reversal, reversed: application.Key),
            Line(advance, -2_000m, FinancialSettlementOrigin.Reversal, reversed: secondPayment.Key));

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, advance.Key);

        Assert.Equal(8_000m, advance.SettledAmount);
        Assert.Equal(0m, advance.AppliedAmount);
        Assert.Equal(8_000m, advance.AvailableAdvanceAmount);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, advance.Status);
    }

    [Fact]
    public async Task Pending_lines_count_before_save()
    {
        var advance = await AddDocumentAsync(FinancialDocumentNature.Advance, 10_000m);
        await SaveLinesAsync(Line(advance, 10_000m, FinancialSettlementOrigin.Manual));
        _db.Context.FinancialSettlements.Add(
            Line(advance, 4_000m, FinancialSettlementOrigin.AdvanceApplication, related: Guid.NewGuid()));

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, advance.Key);

        Assert.Equal(10_000m, advance.SettledAmount);
        Assert.Equal(4_000m, advance.AppliedAmount);
        Assert.Equal(6_000m, advance.AvailableAdvanceAmount);
    }

    [Fact]
    public async Task Incoming_transfer_counts_as_paid()
    {
        var originalKey = Guid.NewGuid();
        var split = await AddDocumentAsync(FinancialDocumentNature.Advance, 7_000m,
            FinancialDocumentOrigin.FinancialDocument, originalKey);
        await SaveLinesAsync(Line(split, 7_000m, FinancialSettlementOrigin.AdvanceTransfer, related: originalKey));

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, split.Key);

        Assert.Equal(7_000m, split.SettledAmount);
        Assert.Equal(0m, split.AppliedAmount);
        Assert.Equal(7_000m, split.AvailableAdvanceAmount);
        Assert.Equal(FinancialDocumentStatus.Settled, split.Status);
    }

    [Fact]
    public async Task Outgoing_transfer_of_a_split_born_advance_counts_as_applied()
    {
        var originalKey = Guid.NewGuid();
        var split = await AddDocumentAsync(FinancialDocumentNature.Advance, 7_000m,
            FinancialDocumentOrigin.FinancialDocument, originalKey);
        await SaveLinesAsync(
            Line(split, 7_000m, FinancialSettlementOrigin.AdvanceTransfer, related: originalKey),
            Line(split, 2_000m, FinancialSettlementOrigin.AdvanceTransfer, related: Guid.NewGuid()));

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, split.Key);

        Assert.Equal(7_000m, split.SettledAmount);
        Assert.Equal(2_000m, split.AppliedAmount);
        Assert.Equal(5_000m, split.AvailableAdvanceAmount);
    }

    [Fact]
    public async Task Provisional_counts_the_application_as_settlement()
    {
        var provisional = await AddDocumentAsync(FinancialDocumentNature.Provisional, 1_000m);
        await SaveLinesAsync(Line(provisional, 1_000m, FinancialSettlementOrigin.AdvanceApplication, related: Guid.NewGuid()));

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, provisional.Key);

        Assert.Equal(1_000m, provisional.SettledAmount);
        Assert.Equal(0m, provisional.AppliedAmount);
        Assert.Equal(0m, provisional.AvailableAdvanceAmount);
        Assert.Equal(FinancialDocumentStatus.Settled, provisional.Status);
    }

    [Theory]
    [InlineData("10000", "3000", "7000")]
    [InlineData("3000", "5000", "0")]
    [InlineData("100.005", "0", "100.00")]
    public void Available_advance_amount_is_paid_minus_applied_never_negative(string settled, string applied, string expected)
    {
        var advance = new FinancialDocument
        {
            CardCode = "F0001", Nature = FinancialDocumentNature.Advance,
            SettledAmount = decimal.Parse(settled, System.Globalization.CultureInfo.InvariantCulture),
            AppliedAmount = decimal.Parse(applied, System.Globalization.CultureInfo.InvariantCulture),
        };

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), advance.AvailableAdvanceAmount);
    }

    [Fact]
    public void Non_advance_has_no_credit()
    {
        var provisional = new FinancialDocument
        {
            CardCode = "F0001", Nature = FinancialDocumentNature.Provisional, SettledAmount = 500m,
        };

        Assert.Equal(0m, provisional.AvailableAdvanceAmount);
    }
}
```

`git add` o arquivo.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsAdvanceRecalculateTests"`
Expected: erro de compilação (`AppliedAmount` e `FinancialSettlementOrigin.AdvanceTransfer` não existem).

- [ ] **Step 3: Enum**

`SiagroB1.Domain/Enums/FinancialSettlementOrigin.cs`, depois de `Dation = 6` (trocar `Dation = 6` por `Dation = 6,`):

```csharp

    /// <summary>
    /// Transferência do crédito NÃO aplicado de um adiantamento para um adiantamento NOVO em outro contrato (spec
    /// 2026-10-14, regra 8: revínculo de adiantamento parcialmente aplicado). Grava-se em PAR, sempre com Amount positivo:
    /// a de ENTRADA fica no adiantamento novo (OriginType = FinancialDocument e OriginKey = RelatedDocumentKey da linha)
    /// e conta no PAGO; a de SAÍDA fica no original e conta no APLICADO. Só nasce pelo revínculo.
    /// </summary>
    AdvanceTransfer = 7
```

- [ ] **Step 4: Entidade**

`SiagroB1.Domain/Entities/FinancialDocument.cs`, logo depois da propriedade `ComposedAmount`:

```csharp

    /// <summary>
    /// Só no ADIANTAMENTO (spec 2026-10-14): persistido-derivado, Σ das linhas AdvanceApplication e AdvanceTransfer de
    /// SAÍDA (e dos estornos delas). Escritor ÚNICO: FinancialDocumentsRecalculateBalanceService. Nos demais títulos fica 0.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal AppliedAmount { get; set; }
```

E trocar o `AvailableAdvanceAmount` inteiro (comentário incluído) por:

```csharp
    /// <summary>
    /// Crédito de adiantamento disponível (spec 2026-10-14): o que foi pago menos o que já foi aplicado (compensado no
    /// provisório ou transferido no revínculo), nunca negativo. Nos demais títulos, 0.
    /// </summary>
    [NotMapped]
    public decimal AvailableAdvanceAmount => Nature == FinancialDocumentNature.Advance
        ? Math.Max(0m, decimal.Round(SettledAmount - AppliedAmount, 2, MidpointRounding.ToEven))
        : 0m;
```

`SiagroB1.Domain/Entities/FinancialDocumentChangeLogFields.cs`, depois de `Composition`:

```csharp

    /// <summary>
    /// Compensação de adiantamento no provisório (spec 2026-10-14), registrada nos DOIS títulos: "código do outro título:
    /// valor N2 pt-BR". OldValue nulo = compensado; NewValue nulo = estornado.
    /// </summary>
    public const string AdvanceApplication = "AdvanceApplication";

    /// <summary>Transferência do crédito no revínculo desmembrado (spec 2026-10-14, regra 8), nos dois adiantamentos.</summary>
    public const string AdvanceTransfer = "AdvanceTransfer";
```

- [ ] **Step 5: Recalculador em dois eixos**

`SiagroB1.Application/Services/Financials/FinancialDocumentsRecalculateBalanceService.cs` — trocar o bloco

```csharp
        var settled = (await context.FinancialSettlements
                          .Where(x => x.FinancialDocumentKey == documentKey)
                          .SumAsync(x => (decimal?)x.Amount) ?? 0m)
                      + PendingDelta(context.ChangeTracker.Entries<FinancialSettlement>()
                          .Where(e => e.Entity.FinancialDocumentKey == documentKey), x => x.Amount);

        document.SettledAmount = decimal.Round(settled, 2, MidpointRounding.ToEven);
```

por

```csharp
        if (document.Nature == FinancialDocumentNature.Advance)
        {
            // Spec 2026-10-14 (compensação de adiantamento): dois eixos. Aplicado = compensações e transferências de
            // SAÍDA (e os estornos delas); pago = todo o resto, inclusive a transferência de ENTRADA.
            var lines = await LedgerAsync(context, documentKey);
            var byKey = lines.ToDictionary(x => x.Key);
            var applied = lines.Where(x => CountsAsApplied(document, x, byKey)).Sum(x => x.Amount);

            document.AppliedAmount = decimal.Round(applied, 2, MidpointRounding.ToEven);
            document.SettledAmount = decimal.Round(lines.Sum(x => x.Amount) - applied, 2, MidpointRounding.ToEven);
        }
        else
        {
            var settled = (await context.FinancialSettlements
                              .Where(x => x.FinancialDocumentKey == documentKey)
                              .SumAsync(x => (decimal?)x.Amount) ?? 0m)
                          + PendingDelta(context.ChangeTracker.Entries<FinancialSettlement>()
                              .Where(e => e.Entity.FinancialDocumentKey == documentKey), x => x.Amount);

            document.SettledAmount = decimal.Round(settled, 2, MidpointRounding.ToEven);
        }
```

E acrescentar, logo antes de `private static decimal PendingDelta<T>(...)`:

```csharp
    /// <summary>
    /// Linhas do ledger do título como ficarão depois do SaveChanges: as do banco menos as Deleted pendentes, mais as
    /// Added pendentes. Ledger é insert-only, então Modified não entra.
    /// </summary>
    private static async Task<List<FinancialSettlement>> LedgerAsync(AppDbContext context, Guid documentKey)
    {
        var pending = context.ChangeTracker.Entries<FinancialSettlement>()
            .Where(e => e.Entity.FinancialDocumentKey == documentKey)
            .ToList();
        var deleted = pending.Where(e => e.State == EntityState.Deleted).Select(e => e.Entity.Key).ToHashSet();

        var stored = await context.FinancialSettlements.AsNoTracking()
            .Where(x => x.FinancialDocumentKey == documentKey)
            .ToListAsync();

        return stored.Where(x => !deleted.Contains(x.Key))
            .Concat(pending.Where(e => e.State == EntityState.Added).Select(e => e.Entity))
            .ToList();
    }

    /// <summary>
    /// Transferência de ENTRADA: a linha do adiantamento NOVO, nascido do desmembramento, que aponta para o original
    /// (OriginType = FinancialDocument e OriginKey = RelatedDocumentKey). Depende só de dados do próprio documento.
    /// </summary>
    internal static bool IsIncomingTransfer(FinancialDocument advance, FinancialSettlement line) =>
        line.Origin == FinancialSettlementOrigin.AdvanceTransfer &&
        advance.OriginType == FinancialDocumentOrigin.FinancialDocument &&
        advance.OriginKey is not null &&
        line.RelatedDocumentKey == advance.OriginKey;

    private static bool IsAppliedAxis(FinancialDocument advance, FinancialSettlement line) =>
        line.Origin == FinancialSettlementOrigin.AdvanceApplication ||
        (line.Origin == FinancialSettlementOrigin.AdvanceTransfer && !IsIncomingTransfer(advance, line));

    /// <summary>O estorno segue o eixo da linha que ele estorna — a espelho fica sempre no mesmo título.</summary>
    private static bool CountsAsApplied(
        FinancialDocument advance, FinancialSettlement line, IReadOnlyDictionary<Guid, FinancialSettlement> byKey) =>
        line.Origin == FinancialSettlementOrigin.Reversal
            ? line.ReversedSettlementKey is { } reversed &&
              byKey.TryGetValue(reversed, out var original) &&
              IsAppliedAxis(advance, original)
            : IsAppliedAxis(advance, line);
```

(`EntityState` já vem de `Microsoft.EntityFrameworkCore`, importado no arquivo.)

- [ ] **Step 6: Rodar os testes**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsAdvanceRecalculateTests|FullyQualifiedName~FinancialDocumentsRecalculateBalanceServiceTests|FullyQualifiedName~FinancialDocumentsRecalculateComposedTests|FullyQualifiedName~FinancialDocumentsSettleServiceTests|FullyQualifiedName~FinancialAdvancesRefundServiceTests|FullyQualifiedName~FinancialAdvancesRelinkContractServiceTests|FullyQualifiedName~FinancialEdmModelTests"`
Expected: todos PASS (os existentes provam que adiantamento só com Manual/AdvanceRefund continua igual).

- [ ] **Step 7: Migration**

```bash
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add AddFinancialDocumentAppliedAmount --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```
Conferir no `Up`, e **só** isto:

```csharp
            migrationBuilder.AddColumn<decimal>(
                name: "AppliedAmount",
                table: "FINANCIAL_DOCUMENTS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);
```
e no `Down` o `DropColumn` correspondente. Qualquer outra operação = modelo divergente: parar e investigar. `git add` dos 3 arquivos (`.cs`, `.Designer.cs`, snapshot).

- [ ] **Step 8: Gates**

```bash
dotnet build SiagroB1.sln
dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```
Expected: build OK; "No changes have been made to the model since the last migration."

- [ ] **Step 9: Stage**

```bash
git add SiagroB1.Domain/Enums/FinancialSettlementOrigin.cs SiagroB1.Domain/Entities/FinancialDocument.cs SiagroB1.Domain/Entities/FinancialDocumentChangeLogFields.cs SiagroB1.Application/Services/Financials/FinancialDocumentsRecalculateBalanceService.cs SiagroB1.Migrations SiagroB1.Application.Tests/Financials/FinancialDocumentsAdvanceRecalculateTests.cs
```
O controlador commita com:

```
feat(financial): pago e aplicado do adiantamento em eixos separados

O adiantamento passa a ter o valor aplicado (compensado no provisório ou transferido no
revínculo) separado do pago, e o crédito disponível vira pago − aplicado. O recalculador
continua escritor único e classifica o ledger pela origem: estorno segue a linha estornada,
transferência de entrada (adiantamento nascido do desmembramento) conta como pago. Sem
backfill: nenhuma linha de compensação ou transferência existe hoje.

DB: AddFinancialDocumentAppliedAmount
Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 2: Compensar adiantamento no provisório (regras 1–4)

**Files:**
- Create: `SiagroB1.Application/Services/Financials/FinancialDocumentsApplyAdvanceService.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (depois de `services.AddScoped<FinancialDocumentsNettingService>();`)
- Create (test support): `SiagroB1.Application.Tests/Financials/AdvanceApplicationSeed.cs`
- Test: `SiagroB1.Application.Tests/Financials/FinancialDocumentsApplyAdvanceTests.cs`

**Interfaces:**
- Consumes: Task 1 (`AppliedAmount`, `AvailableAdvanceAmount`, `FinancialDocumentChangeLogFields.AdvanceApplication`, recalculador); `FinancialDocumentsComposeService.Describe(string?, decimal)` (internal, já existe); `FinancialDocumentsContractCancellationGuardService.CurrencySymbol(CurrencyType)` (internal, já existe); `FirmPayableSeed`.
- Produces:
  - `FinancialDocumentsApplyAdvanceService(IUnitOfWork db, FinancialDocumentChangeLogService changeLog)`: `Task ExecuteAsync(Guid provisionalKey, Guid advanceKey, decimal amount, DateTime settlementDate, string userName, CommitMode commitMode = CommitMode.Auto)`.
  - `AdvanceApplicationSeed` (testes): `CreateAsync(decimal provisionalAmount = 100_000m, decimal advanceAmount = 10_000m, decimal advancePaid = 10_000m)`; propriedades `Firm`, `Db`, `Context`, `Contract`, `Provisional`, `ComposedFirm`, `Advance`; métodos `ComposeAsync(FinancialDocument provisional, FinancialDocument firm)`, `AddAdvanceAsync(PurchaseContract contract, decimal amount, decimal paid, string code, CurrencyType currency = CurrencyType.Brl)`, `PayAsync(FinancialDocument advance, decimal amount)`, `Apply()`, `ApplyAsync(decimal amount, FinancialDocument? advance = null, FinancialDocument? provisional = null)`, `Reverse()`, `LineAsync(Guid documentKey, FinancialSettlementOrigin origin)`, `ReloadAsync(Guid key)`.

- [ ] **Step 1: Seed dos testes**

`SiagroB1.Application.Tests/Financials/AdvanceApplicationSeed.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.PurchaseFiscalReconciliations;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Cenário da compensação de adiantamento (spec 2026-10-14): o <see cref="FirmPayableSeed"/> (contrato PC-1 do F0001,
/// provisório FN-PROV-1, conta CX01) com o firme ST-0001 de 19.728,33 COMPOSTO no provisório — disponível para baixa =
/// 19.728,33 — e o adiantamento FN-ADT-1 do mesmo contrato, pago pela baixa REAL (linha Manual na CX01).
/// </summary>
internal sealed class AdvanceApplicationSeed
{
    public FirmPayableSeed Firm { get; }
    public UnitOfWork Db => Firm.Db;
    public AppDbContext Context => Firm.Context;
    public PurchaseContract Contract => Firm.Contract;
    public FinancialDocument Provisional => Firm.Provisional;
    public FinancialDocument ComposedFirm { get; private set; } = null!;
    public FinancialDocument Advance { get; private set; } = null!;

    private AdvanceApplicationSeed(FirmPayableSeed firm) => Firm = firm;

    public static async Task<AdvanceApplicationSeed> CreateAsync(
        decimal provisionalAmount = 100_000m, decimal advanceAmount = 10_000m, decimal advancePaid = 10_000m)
    {
        var firm = await FirmPayableSeed.CreateAsync(provisionalAmount);
        var seed = new AdvanceApplicationSeed(firm);
        var conciliation = await firm.ConciliateAsync();
        seed.ComposedFirm = conciliation.Firm!;
        await seed.ComposeAsync(firm.Provisional, conciliation.Firm!);
        seed.Advance = await seed.AddAdvanceAsync(firm.Contract, advanceAmount, advancePaid, "FN-ADT-1");
        return seed;
    }

    public Task ComposeAsync(FinancialDocument provisional, FinancialDocument firm) =>
        new FinancialDocumentsComposeService(Db, new FinancialDocumentChangeLogService(Context))
            .ExecuteAsync(provisional.Key, [firm.Key], "ana");

    public async Task<FinancialDocument> AddAdvanceAsync(
        PurchaseContract contract, decimal amount, decimal paid, string code, CurrencyType currency = CurrencyType.Brl)
    {
        var advance = new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = code, CardCode = FiscalReconciliationSeed.Supplier, CardName = "PRODUTOR TESTE",
            Direction = FinancialDirection.Payable, Nature = FinancialDocumentNature.Advance,
            Status = FinancialDocumentStatus.Open, DueDate = new DateTime(2026, 12, 31), NetAmount = amount,
            OriginType = FinancialDocumentOrigin.Manual, OriginDocNumber = contract.Code,
            PurchaseContractKey = contract.Key, BranchCode = contract.BranchCode, Currency = currency,
            PaymentTermsText = "Depósito no Banco X",
        };
        Context.FinancialDocuments.Add(advance);
        await Context.SaveChangesAsync();

        if (paid > 0m)
            await PayAsync(advance, paid);

        return advance;
    }

    public Task PayAsync(FinancialDocument advance, decimal amount) =>
        new FinancialDocumentsSettleService(Db).ExecuteAsync(
            advance.Key, "CX01", amount, new DateTime(2026, 10, 1), 0m, 0m, 0m, "TED-1", null, "ana");

    public FinancialDocumentsApplyAdvanceService Apply() => new(Db, new FinancialDocumentChangeLogService(Context));

    public Task ApplyAsync(decimal amount, FinancialDocument? advance = null, FinancialDocument? provisional = null) =>
        Apply().ExecuteAsync((provisional ?? Provisional).Key, (advance ?? Advance).Key, amount,
            new DateTime(2026, 10, 14), "ana");

    public FinancialDocumentsReverseSettlementService Reverse() => new(Db);

    public Task<FinancialSettlement> LineAsync(Guid documentKey, FinancialSettlementOrigin origin) =>
        Context.FinancialSettlements.AsNoTracking()
            .SingleAsync(s => s.FinancialDocumentKey == documentKey && s.Origin == origin);

    public Task<FinancialDocument> ReloadAsync(Guid key) => Firm.ReloadAsync(key);
}
```

`git add` o arquivo.

- [ ] **Step 2: Testes que falham**

`SiagroB1.Application.Tests/Financials/FinancialDocumentsApplyAdvanceTests.cs`:

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-14, regras 1–4: compensar o crédito do adiantamento contra o provisório, em par.</summary>
public class FinancialDocumentsApplyAdvanceTests
{
    [Fact]
    public async Task Applying_writes_the_pair_recalculates_both_and_logs_both()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();

        await seed.ApplyAsync(6_000m);

        var onProvisional = await seed.LineAsync(seed.Provisional.Key, FinancialSettlementOrigin.AdvanceApplication);
        Assert.Equal(6_000m, onProvisional.Amount);
        Assert.Null(onProvisional.FinancialAccountCode);
        Assert.Equal(seed.Advance.Key, onProvisional.RelatedDocumentKey);
        Assert.Equal("FN-ADT-1", onProvisional.DocumentReference);
        Assert.Equal(new DateTime(2026, 10, 14), onProvisional.SettlementDate);

        var onAdvance = await seed.LineAsync(seed.Advance.Key, FinancialSettlementOrigin.AdvanceApplication);
        Assert.Equal(6_000m, onAdvance.Amount);
        Assert.Null(onAdvance.FinancialAccountCode);
        Assert.Equal(seed.Provisional.Key, onAdvance.RelatedDocumentKey);
        Assert.Equal("FN-PROV-1", onAdvance.DocumentReference);

        var provisional = await seed.ReloadAsync(seed.Provisional.Key);
        Assert.Equal(6_000m, provisional.SettledAmount);
        Assert.Equal(13_728.33m, provisional.AvailableToSettleAmount);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, provisional.Status);

        var advance = await seed.ReloadAsync(seed.Advance.Key);
        Assert.Equal(10_000m, advance.SettledAmount);
        Assert.Equal(6_000m, advance.AppliedAmount);
        Assert.Equal(4_000m, advance.AvailableAdvanceAmount);
        Assert.Equal(FinancialDocumentStatus.Settled, advance.Status);

        var logs = await seed.Context.FinancialDocumentChangeLogs.AsNoTracking()
            .Where(l => l.Field == FinancialDocumentChangeLogFields.AdvanceApplication)
            .ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.Equal("FN-ADT-1: 6.000,00", logs.Single(l => l.FinancialDocumentKey == seed.Provisional.Key).NewValue);
        Assert.Equal("FN-PROV-1: 6.000,00", logs.Single(l => l.FinancialDocumentKey == seed.Advance.Key).NewValue);
        Assert.All(logs, l => Assert.Null(l.OldValue));
    }

    [Fact]
    public async Task Application_can_settle_the_provisional()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync(
            provisionalAmount: FirmPayableSeed.DefaultFirmAmount, advanceAmount: 20_000m, advancePaid: 20_000m);

        await seed.ApplyAsync(FirmPayableSeed.DefaultFirmAmount);

        var provisional = await seed.ReloadAsync(seed.Provisional.Key);
        Assert.Equal(FinancialDocumentStatus.Settled, provisional.Status);
        Assert.Equal(0m, provisional.AvailableToSettleAmount);

        var advance = await seed.ReloadAsync(seed.Advance.Key);
        Assert.Equal(19_728.33m, advance.AppliedAmount);
        Assert.Equal(271.67m, advance.AvailableAdvanceAmount);
    }

    [Fact]
    public async Task Above_the_provisional_available_is_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync(advanceAmount: 30_000m, advancePaid: 30_000m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => seed.ApplyAsync(20_000m));

        Assert.Equal("Valor acima do disponível para baixa do título FN-PROV-1 (19.728,33).", error.Message);
        Assert.Empty(seed.Context.FinancialSettlements.AsNoTracking()
            .Where(s => s.Origin == FinancialSettlementOrigin.AdvanceApplication));
    }

    [Fact]
    public async Task Above_the_credit_is_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => seed.ApplyAsync(10_000.01m));

        Assert.Equal("Valor acima do crédito disponível do adiantamento FN-ADT-1 (10.000,00).", error.Message);
    }

    [Fact]
    public async Task Unpaid_advance_has_no_credit()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync(advancePaid: 0m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => seed.ApplyAsync(100m));

        Assert.Equal("Valor acima do crédito disponível do adiantamento FN-ADT-1 (0,00).", error.Message);
    }

    [Fact]
    public async Task Second_application_beyond_the_remaining_credit_is_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        await seed.ApplyAsync(6_000m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => seed.ApplyAsync(5_000m));

        Assert.Equal("Valor acima do crédito disponível do adiantamento FN-ADT-1 (4.000,00).", error.Message);
        Assert.Equal(6_000m, (await seed.ReloadAsync(seed.Advance.Key)).AppliedAmount);
    }

    [Fact]
    public async Task Advance_of_another_contract_is_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var other = await seed.Firm.Fiscal.AddContractAsync("PC-2");
        var foreign = await seed.AddAdvanceAsync(other, 5_000m, 5_000m, "FN-ADT-2");

        var error = await Assert.ThrowsAsync<ApplicationException>(() => seed.ApplyAsync(1_000m, advance: foreign));

        Assert.Equal("Adiantamento FN-ADT-2 não pertence ao contrato do título FN-PROV-1.", error.Message);
    }

    [Fact]
    public async Task Different_currency_is_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var dollar = await seed.AddAdvanceAsync(seed.Contract, 5_000m, 0m, "FN-ADT-2", CurrencyType.Usd);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => seed.ApplyAsync(1_000m, advance: dollar));

        Assert.Equal(
            "O adiantamento FN-ADT-2 está em US$ e o provisório FN-PROV-1 em R$: compensação entre moedas diferentes não é suportada.",
            error.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("0.004")]
    public async Task Non_positive_amount_is_refused(string amount)
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            seed.ApplyAsync(decimal.Parse(amount, CultureInfo.InvariantCulture)));

        Assert.Equal("Informe um valor maior que zero.", error.Message);
    }

    [Fact]
    public async Task Amount_is_rounded_to_cents_before_the_ceilings()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();

        await seed.ApplyAsync(4_000.004m);

        Assert.Equal(4_000m, (await seed.LineAsync(seed.Advance.Key, FinancialSettlementOrigin.AdvanceApplication)).Amount);
        Assert.Equal(4_000m, (await seed.ReloadAsync(seed.Advance.Key)).AppliedAmount);
    }

    [Fact]
    public async Task Canceled_provisional_is_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        seed.Provisional.Status = FinancialDocumentStatus.Canceled;
        await seed.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => seed.ApplyAsync(1_000m));

        Assert.Equal("Título cancelado não pode ser compensado.", error.Message);
    }

    [Fact]
    public async Task Canceled_advance_is_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var canceled = await seed.AddAdvanceAsync(seed.Contract, 5_000m, 0m, "FN-ADT-2");
        canceled.Status = FinancialDocumentStatus.Canceled;
        await seed.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => seed.ApplyAsync(1_000m, advance: canceled));

        Assert.Equal("Título cancelado não pode ser compensado.", error.Message);
    }

    [Fact]
    public async Task Only_a_purchase_provisional_receives_the_application()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            seed.ApplyAsync(1_000m, provisional: seed.ComposedFirm));

        Assert.Equal("Título ST-0001 não é provisório de contrato de compra.", error.Message);
    }

    [Fact]
    public async Task Only_an_advance_can_be_applied()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            seed.ApplyAsync(1_000m, advance: seed.ComposedFirm));

        Assert.Equal("O título ST-0001 não é um adiantamento.", error.Message);
    }
}
```

`git add` o arquivo.

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsApplyAdvanceTests"`
Expected: erro de compilação (`FinancialDocumentsApplyAdvanceService` não existe).

- [ ] **Step 4: Serviço**

`SiagroB1.Application/Services/Financials/FinancialDocumentsApplyAdvanceService.cs`:

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Compensa o crédito de um adiantamento contra o PROVISÓRIO do mesmo contrato de compra (spec 2026-10-14, regras 1–4).
/// Manual (D1): o operador escolhe adiantamento e valor. Em PAR (D4), como o encontro de contas: uma linha
/// <see cref="FinancialSettlementOrigin.AdvanceApplication"/> em cada título, sem conta, com RelatedDocumentKey cruzado.
/// No provisório ela entra no baixado como qualquer baixa; no adiantamento, no APLICADO.
///
/// Tetos: o disponível para baixa do provisório (composto − baixado — só se compensa o que já está coberto por
/// documento de entrada) e o crédito do adiantamento (pago − aplicado). Enqueue-only até o SaveChanges final; o
/// RowVersion dos dois títulos protege a dupla compensação simultânea do mesmo crédito.
/// </summary>
public class FinancialDocumentsApplyAdvanceService(IUnitOfWork db, FinancialDocumentChangeLogService changeLog)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task ExecuteAsync(
        Guid provisionalKey, Guid advanceKey, decimal amount, DateTime settlementDate, string userName,
        CommitMode commitMode = CommitMode.Auto)
    {
        var context = db.Context;

        var provisional = await context.FinancialDocuments.FirstOrDefaultAsync(x => x.Key == provisionalKey)
                          ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (provisional.Nature != FinancialDocumentNature.Provisional)
            throw new ApplicationException($"Título {provisional.Code} não é provisório de contrato de compra.");

        if (provisional.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException("Título cancelado não pode ser compensado.");

        // Só compra: o provisório de venda nunca recebe baixa (não tem composição).
        if (provisional.PurchaseContractKey is null)
            throw new ApplicationException($"Título {provisional.Code} não é provisório de contrato de compra.");

        var advance = await context.FinancialDocuments.FirstOrDefaultAsync(x => x.Key == advanceKey)
                      ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (advance.Nature != FinancialDocumentNature.Advance)
            throw new ApplicationException($"O título {advance.Code} não é um adiantamento.");

        if (advance.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException("Título cancelado não pode ser compensado.");

        // Mesmo CONTRATO: adiantamento de outro contrato se revincula antes (fora do escopo compensar entre contratos).
        if (advance.PurchaseContractKey != provisional.PurchaseContractKey)
            throw new ApplicationException(
                $"Adiantamento {advance.Code} não pertence ao contrato do título {provisional.Code}.");

        if (advance.Currency != provisional.Currency)
            throw new ApplicationException(
                $"O adiantamento {advance.Code} está em {Currency(advance.Currency)} e o provisório {provisional.Code} em {Currency(provisional.Currency)}: compensação entre moedas diferentes não é suportada.");

        // O Edm.Double pode trazer mais de duas casas: arredonda ANTES dos tetos.
        var value = decimal.Round(amount, 2, MidpointRounding.ToEven);

        if (value <= 0m)
            throw new ApplicationException("Informe um valor maior que zero.");

        if (value > provisional.AvailableToSettleAmount)
            throw new ApplicationException(
                $"Valor acima do disponível para baixa do título {provisional.Code} ({provisional.AvailableToSettleAmount.ToString("N2", PtBr)}).");

        if (value > advance.AvailableAdvanceAmount)
            throw new ApplicationException(
                $"Valor acima do crédito disponível do adiantamento {advance.Code} ({advance.AvailableAdvanceAmount.ToString("N2", PtBr)}).");

        var now = DateTime.Now;
        context.FinancialSettlements.AddRange(
            Line(provisional, advance, value, settlementDate.Date, now, userName),
            Line(advance, provisional, value, settlementDate.Date, now, userName));

        changeLog.Register(provisional.Key, FinancialDocumentChangeLogFields.AdvanceApplication, null,
            FinancialDocumentsComposeService.Describe(advance.Code, value), userName);
        changeLog.Register(advance.Key, FinancialDocumentChangeLogFields.AdvanceApplication, null,
            FinancialDocumentsComposeService.Describe(provisional.Code, value), userName);

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, provisional.Key);
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, advance.Key);

        if (commitMode == CommitMode.Auto)
            await db.SaveChangesAsync();
    }

    private static string Currency(CurrencyType currency) =>
        FinancialDocumentsContractCancellationGuardService.CurrencySymbol(currency);

    private static FinancialSettlement Line(
        FinancialDocument document, FinancialDocument other, decimal amount, DateTime settlementDate, DateTime now,
        string userName) => new()
    {
        Key = Guid.NewGuid(),
        FinancialDocumentKey = document.Key,
        FinancialAccountCode = null,
        SettlementDate = settlementDate,
        Amount = amount,
        Origin = FinancialSettlementOrigin.AdvanceApplication,
        RelatedDocumentKey = other.Key,
        DocumentReference = other.Code,
        CreatedAt = now, CreatedBy = userName, UpdatedAt = now, UpdatedBy = userName,
    };
}
```

`git add` o arquivo.

- [ ] **Step 5: DI**

`SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, depois de `services.AddScoped<FinancialDocumentsNettingService>();`:

```csharp
        services.AddScoped<FinancialDocumentsApplyAdvanceService>();
```

- [ ] **Step 6: Rodar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsApplyAdvanceTests|FullyQualifiedName~FinancialDocumentsCompositionTests"`
Expected: todos PASS.

- [ ] **Step 7: Stage**

```bash
git add SiagroB1.Application/Services/Financials/FinancialDocumentsApplyAdvanceService.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/Financials/AdvanceApplicationSeed.cs SiagroB1.Application.Tests/Financials/FinancialDocumentsApplyAdvanceTests.cs
```
O controlador commita com:

```
feat(financial): compensar adiantamento contra o provisório da compra

O crédito do adiantamento (pago e ainda não aplicado) abate o provisório do mesmo contrato e
moeda, à mão, até o disponível para baixa — só o que já está coberto por documento de
entrada. Lançamento em par nos dois títulos, sem conta, com recálculo e log nos dois num
SaveChanges só; o valor é arredondado a centavos antes dos tetos.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 3: Estorno do par de compensação e trava do estorno de pagamento (regras 5–6)

**Files:**
- Modify: `SiagroB1.Application/Services/Financials/FinancialDocumentsReverseSettlementService.cs` (arquivo inteiro)
- Test: `SiagroB1.Application.Tests/Financials/FinancialDocumentsAdvanceApplicationReverseTests.cs`

**Interfaces:**
- Consumes: Task 1 (`AppliedAmount`, `IsIncomingTransfer`, `FinancialDocumentChangeLogFields.AdvanceApplication/AdvanceTransfer`), Task 2 (`AdvanceApplicationSeed`, `FinancialDocumentsApplyAdvanceService`).
- Produces:
  - `FinancialDocumentsReverseSettlementService(IUnitOfWork db)` (construtor inalterado): estorna o par de `Netting`, `AdvanceApplication` e `AdvanceTransfer`; A7 para `Manual` positivo em adiantamento.
  - `public const string FinancialDocumentsReverseSettlementService.SplitReversalReason = "Transferência de crédito estornada"` e o tratamento da regra 9 (usado e testado na Task 5).

- [ ] **Step 1: Testes que falham**

`SiagroB1.Application.Tests/Financials/FinancialDocumentsAdvanceApplicationReverseTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-14, regras 5–6: o "Estornar baixa" desfaz a compensação em par e trava o estorno do pagamento.</summary>
public class FinancialDocumentsAdvanceApplicationReverseTests
{
    [Fact]
    public async Task Reversing_the_advance_line_reverses_the_pair_and_restores_both()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        await seed.ApplyAsync(6_000m);
        var onAdvance = await seed.LineAsync(seed.Advance.Key, FinancialSettlementOrigin.AdvanceApplication);
        var onProvisional = await seed.LineAsync(seed.Provisional.Key, FinancialSettlementOrigin.AdvanceApplication);

        await seed.Reverse().ExecuteAsync(onAdvance.Key, "compensação errada", "bia");

        var reversals = await seed.Context.FinancialSettlements.AsNoTracking()
            .Where(s => s.Origin == FinancialSettlementOrigin.Reversal).ToListAsync();
        Assert.Equal(2, reversals.Count);
        Assert.Contains(reversals, r => r.ReversedSettlementKey == onAdvance.Key && r.Amount == -6_000m);
        Assert.Contains(reversals, r => r.ReversedSettlementKey == onProvisional.Key && r.Amount == -6_000m);

        var provisional = await seed.ReloadAsync(seed.Provisional.Key);
        Assert.Equal(0m, provisional.SettledAmount);
        Assert.Equal(19_728.33m, provisional.AvailableToSettleAmount);
        Assert.Equal(FinancialDocumentStatus.Open, provisional.Status);

        var advance = await seed.ReloadAsync(seed.Advance.Key);
        Assert.Equal(10_000m, advance.SettledAmount);
        Assert.Equal(0m, advance.AppliedAmount);
        Assert.Equal(10_000m, advance.AvailableAdvanceAmount);

        var undoLogs = await seed.Context.FinancialDocumentChangeLogs.AsNoTracking()
            .Where(l => l.Field == FinancialDocumentChangeLogFields.AdvanceApplication && l.NewValue == null)
            .ToListAsync();
        Assert.Equal(2, undoLogs.Count);
        Assert.Contains(undoLogs, l => l.FinancialDocumentKey == seed.Provisional.Key && l.OldValue == "FN-ADT-1: 6.000,00");
        Assert.Contains(undoLogs, l => l.FinancialDocumentKey == seed.Advance.Key && l.OldValue == "FN-PROV-1: 6.000,00");
    }

    [Fact]
    public async Task Reversing_from_the_provisional_side_reverses_the_pair_too()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        await seed.ApplyAsync(6_000m);
        var onProvisional = await seed.LineAsync(seed.Provisional.Key, FinancialSettlementOrigin.AdvanceApplication);

        await seed.Reverse().ExecuteAsync(onProvisional.Key, "compensação errada", "bia");

        Assert.Equal(0m, (await seed.ReloadAsync(seed.Advance.Key)).AppliedAmount);
        Assert.Equal(0m, (await seed.ReloadAsync(seed.Provisional.Key)).SettledAmount);
    }

    [Fact]
    public async Task Reversing_the_payment_of_an_applied_advance_is_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        await seed.ApplyAsync(6_000m);
        var payment = await seed.LineAsync(seed.Advance.Key, FinancialSettlementOrigin.Manual);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            seed.Reverse().ExecuteAsync(payment.Key, "pagamento errado", "bia"));

        Assert.Equal(
            "O adiantamento FN-ADT-1 tem 6.000,00 aplicado: desfaça as compensações antes de estornar o pagamento.",
            error.Message);
    }

    [Fact]
    public async Task Reversing_one_of_two_payments_passes_while_the_rest_covers_the_application()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync(advancePaid: 5_000m);
        await seed.PayAsync(seed.Advance, 5_000m);
        await seed.ApplyAsync(4_000m);
        var payment = await seed.Context.FinancialSettlements.AsNoTracking()
            .FirstAsync(s => s.FinancialDocumentKey == seed.Advance.Key && s.Origin == FinancialSettlementOrigin.Manual);

        await seed.Reverse().ExecuteAsync(payment.Key, "pagamento em duplicidade", "bia");

        var advance = await seed.ReloadAsync(seed.Advance.Key);
        Assert.Equal(5_000m, advance.SettledAmount);
        Assert.Equal(4_000m, advance.AppliedAmount);
        Assert.Equal(1_000m, advance.AvailableAdvanceAmount);
    }

    [Fact]
    public async Task Reversing_a_payment_without_application_still_works()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var payment = await seed.LineAsync(seed.Advance.Key, FinancialSettlementOrigin.Manual);

        await seed.Reverse().ExecuteAsync(payment.Key, "pagamento errado", "bia");

        var advance = await seed.ReloadAsync(seed.Advance.Key);
        Assert.Equal(0m, advance.SettledAmount);
        Assert.Equal(FinancialDocumentStatus.Open, advance.Status);
    }
}
```

`git add` o arquivo.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsAdvanceApplicationReverseTests"`
Expected: FAIL — `Reversing_the_advance_line_reverses_the_pair_and_restores_both` (só uma linha estornada), `Reversing_the_payment_of_an_applied_advance_is_refused` (nenhuma exceção).

- [ ] **Step 3: Estorno com pares, A7 e regra 9**

Substituir **todo** o conteúdo de `SiagroB1.Application/Services/Financials/FinancialDocumentsReverseSettlementService.cs` por:

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Estorna uma baixa gravando a linha NEGATIVA espelho. O ledger é insert-only — é dinheiro,
/// nunca se apaga. O índice único filtrado em ReversedSettlementKey impede estornar a mesma
/// baixa duas vezes; a consulta abaixo é a camada amigável do mesmo impedimento.
///
/// Lançamentos em PAR — encontro de contas (spec 2026-10-13, regra 21), compensação de adiantamento e transferência de
/// crédito (spec 2026-10-14, regras 5 e 9): estornar uma ponta estorna o PAR no mesmo ato.
/// Abatimento de composição (regra 14): recusado — só o desfazer da composição o estorna.
/// Pagamento do adiantamento (regra 6): recusado se o pago ficaria abaixo do aplicado.
/// Transferência (regra 9): estornar o par CANCELA o adiantamento nascido do desmembramento.
/// </summary>
public class FinancialDocumentsReverseSettlementService(IUnitOfWork db)
{
    /// <summary>Motivo de cancelamento do adiantamento novo quando a transferência é estornada (spec 2026-10-14, regra 9).</summary>
    public const string SplitReversalReason = "Transferência de crédito estornada";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task ExecuteAsync(
        Guid settlementKey, string reason, string userName, CommitMode commitMode = CommitMode.Auto)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ApplicationException("Informe o motivo do estorno.");

        var settlement = await db.Context.FinancialSettlements
                             .FirstOrDefaultAsync(x => x.Key == settlementKey)
                         ?? throw new NotFoundException("Baixa não encontrada.");

        if (settlement.Origin == FinancialSettlementOrigin.Reversal)
            throw new ApplicationException("Não é possível estornar um estorno.");

        // Regra 14 (spec 2026-10-13, títulos firmes): o abatimento nasce da composição e só ela o desfaz (remove a
        // linha de composição junto); estornar por aqui deixaria a composição órfã.
        if (settlement.Origin == FinancialSettlementOrigin.InvoiceOffset)
            throw new ApplicationException("Abatimento de composição se desfaz pela composição.");

        // A devolução é o registro de que o dinheiro voltou por fora — não é uma baixa errada,
        // então não há o que "desfazer" nela.
        if (settlement.Origin == FinancialSettlementOrigin.AdvanceRefund)
            throw new ApplicationException(
                "Não é possível estornar uma devolução: ela é o registro de que o dinheiro voltou.");

        var settlements = new List<FinancialSettlement> { settlement };

        if (settlement.Origin is FinancialSettlementOrigin.Netting
            or FinancialSettlementOrigin.AdvanceApplication
            or FinancialSettlementOrigin.AdvanceTransfer)
            settlements.Add(await CounterpartAsync(settlement));

        var documents = new Dictionary<Guid, FinancialDocument>();
        foreach (var line in settlements)
        {
            var document = await db.Context.FinancialDocuments
                               .FirstOrDefaultAsync(x => x.Key == line.FinancialDocumentKey)
                           ?? throw new NotFoundException("Documento financeiro não encontrado.");

            // A devolução de adiantamento é o primeiro caminho do sistema que produz um documento
            // Canceled com um ledger de baixas ainda vivo (a baixa original + a linha negativa da
            // devolução). Sem este guard, estornar a baixa original reabriria o saldo enquanto o
            // Status continua Canceled — e nem o recálculo (que não mexe em documento cancelado) nem
            // o guard de cancelamento de contrato (que já filtra Status != Canceled) percebem. É o
            // buraco que esta feature existe para fechar reabrindo sozinho, então NÃO apagar por
            // parecer redundante com o guard de Origin acima.
            if (document.Status == FinancialDocumentStatus.Canceled)
                throw new ApplicationException("Não é possível estornar a baixa de um título cancelado.");

            if (await db.Context.FinancialSettlements.AnyAsync(x => x.ReversedSettlementKey == line.Key))
                throw new ApplicationException("Esta baixa já foi estornada.");

            documents[line.FinancialDocumentKey] = document;
        }

        EnsurePaymentStillCoversTheApplied(settlement, documents[settlement.FinancialDocumentKey]);

        var splitAdvance = settlement.Origin == FinancialSettlementOrigin.AdvanceTransfer
            ? EnsureSplitAdvanceUnused(settlements, documents)
            : null;

        var reversals = settlements.Select(line => Mirror(line, reason, userName)).ToList();
        LogUndo(settlements, documents, userName);

        try
        {
            if (commitMode == CommitMode.Auto) await db.BeginTransactionAsync();

            db.Context.FinancialSettlements.AddRange(reversals);
            await db.Context.SaveChangesAsync();

            foreach (var line in settlements)
                await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(db.Context, line.FinancialDocumentKey);

            // Regra 9: o recálculo deixa o novo sem pago (Open); o cancelamento vem DEPOIS para prevalecer.
            if (splitAdvance is not null)
                FinancialDocumentsCancelService.Cancel(splitAdvance, SplitReversalReason, userName);

            await db.Context.SaveChangesAsync();

            if (commitMode == CommitMode.Auto) await db.CommitAsync();
        }
        catch
        {
            if (commitMode == CommitMode.Auto) await db.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// A outra ponta do par: lançamento entre os mesmos dois títulos (chaves cruzadas), mesma origem e valor, ainda não
    /// estornado. Qualquer uma serve — financeiramente são idênticas.
    /// </summary>
    private async Task<FinancialSettlement> CounterpartAsync(FinancialSettlement settlement)
    {
        var counterpart = await db.Context.FinancialSettlements
            .Where(x => x.FinancialDocumentKey == settlement.RelatedDocumentKey &&
                        x.RelatedDocumentKey == settlement.FinancialDocumentKey &&
                        x.Origin == settlement.Origin &&
                        x.Amount == settlement.Amount &&
                        !db.Context.FinancialSettlements.Any(r => r.ReversedSettlementKey == x.Key))
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync();

        return counterpart ?? throw new ApplicationException(settlement.Origin switch
        {
            FinancialSettlementOrigin.Netting => "Lançamento par do encontro de contas não encontrado.",
            FinancialSettlementOrigin.AdvanceApplication => "Lançamento par da compensação de adiantamento não encontrado.",
            _ => "Lançamento par da transferência de adiantamento não encontrado.",
        });
    }

    /// <summary>
    /// Regra 6 (spec 2026-10-14): estornar um pagamento do adiantamento não pode deixar o pago abaixo do aplicado — o
    /// crédito já usado ficaria sem dinheiro por trás. A transferência de saída do desmembramento conta como aplicado.
    /// </summary>
    private static void EnsurePaymentStillCoversTheApplied(FinancialSettlement settlement, FinancialDocument document)
    {
        if (settlement.Origin != FinancialSettlementOrigin.Manual ||
            document.Nature != FinancialDocumentNature.Advance ||
            settlement.Amount <= 0m)
            return;

        var paidAfter = decimal.Round(document.SettledAmount - settlement.Amount, 2, MidpointRounding.ToEven);
        if (paidAfter < document.AppliedAmount)
            throw new ApplicationException(
                $"O adiantamento {document.Code} tem {document.AppliedAmount.ToString("N2", PtBr)} aplicado: desfaça as compensações antes de estornar o pagamento.");
    }

    /// <summary>
    /// Regra 9: o adiantamento NOVO é o da transferência de ENTRADA. Só se desfaz o desmembramento se ele continua
    /// intocado — nada aplicado e o pago igual à transferência (compensação estornada se anula e não trava).
    /// </summary>
    private static FinancialDocument EnsureSplitAdvanceUnused(
        IReadOnlyList<FinancialSettlement> settlements, IReadOnlyDictionary<Guid, FinancialDocument> documents)
    {
        var incoming = settlements.FirstOrDefault(line =>
                           FinancialDocumentsRecalculateBalanceService.IsIncomingTransfer(
                               documents[line.FinancialDocumentKey], line))
                       ?? throw new ApplicationException("Lançamento par da transferência de adiantamento não encontrado.");

        var split = documents[incoming.FinancialDocumentKey];
        if (split.AppliedAmount != 0m || split.SettledAmount != incoming.Amount)
            throw new ApplicationException(
                $"O adiantamento {split.Code} já foi usado: desfaça as compensações dele antes de estornar a transferência.");

        return split;
    }

    /// <summary>Compensação e transferência logam o desfazer nos dois títulos (NewValue nulo), como a composição.</summary>
    private void LogUndo(
        IReadOnlyList<FinancialSettlement> settlements, IReadOnlyDictionary<Guid, FinancialDocument> documents,
        string userName)
    {
        var field = settlements[0].Origin switch
        {
            FinancialSettlementOrigin.AdvanceApplication => FinancialDocumentChangeLogFields.AdvanceApplication,
            FinancialSettlementOrigin.AdvanceTransfer => FinancialDocumentChangeLogFields.AdvanceTransfer,
            _ => null,
        };
        if (field is null)
            return;

        var changeLog = new FinancialDocumentChangeLogService(db.Context);
        foreach (var line in settlements)
        {
            var other = documents[line.RelatedDocumentKey!.Value];
            changeLog.Register(line.FinancialDocumentKey, field,
                FinancialDocumentsComposeService.Describe(other.Code, line.Amount), null, userName);
        }
    }

    private static FinancialSettlement Mirror(FinancialSettlement settlement, string reason, string userName) => new()
    {
        Key = Guid.NewGuid(),
        FinancialDocumentKey = settlement.FinancialDocumentKey,
        FinancialAccountCode = settlement.FinancialAccountCode,
        SettlementDate = DateTime.Today,
        Amount = -settlement.Amount,
        InterestAmount = -settlement.InterestAmount,
        FineAmount = -settlement.FineAmount,
        DiscountAmount = -settlement.DiscountAmount,
        Origin = FinancialSettlementOrigin.Reversal,
        ReversedSettlementKey = settlement.Key,
        RelatedDocumentKey = settlement.RelatedDocumentKey,
        CompositionKey = settlement.CompositionKey,
        DocumentReference = settlement.DocumentReference,
        Notes = reason,
        CreatedAt = DateTime.Now,
        CreatedBy = userName,
        UpdatedAt = DateTime.Now,
        UpdatedBy = userName
    };
}
```

- [ ] **Step 4: Rodar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsAdvanceApplicationReverseTests|FullyQualifiedName~FinancialDocumentsNettingTests|FullyQualifiedName~FinancialDocumentsCompositionTests|FullyQualifiedName~FinancialDocumentsSettleServiceTests|FullyQualifiedName~FinancialAdvancesRefundServiceTests|FullyQualifiedName~FinancialDocumentsDationTests"`
Expected: todos PASS (os de encontro de contas provam que o par do `Netting` não mudou).

- [ ] **Step 5: Stage**

```bash
git add SiagroB1.Application/Services/Financials/FinancialDocumentsReverseSettlementService.cs SiagroB1.Application.Tests/Financials/FinancialDocumentsAdvanceApplicationReverseTests.cs
```
O controlador commita com:

```
feat(financial): estorno da compensação em par e trava do pagamento aplicado

"Estornar baixa" sobre qualquer ponta da compensação de adiantamento estorna o par, como o
encontro de contas, e loga o desfazer nos dois títulos. Estornar o pagamento do adiantamento é
recusado se o pago ficaria abaixo do já aplicado. O estorno da transferência de crédito (par
do revínculo desmembrado) cancela o adiantamento novo se ele continua intocado.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 4: Devolver só o crédito disponível (regra 7)

**Files:**
- Modify: `SiagroB1.Application/Services/Financials/FinancialAdvancesRefundService.cs` (arquivo inteiro)
- Modify: `SiagroB1.Application.Tests/Financials/FinancialAdvancesRefundServiceTests.cs` (teste `Refuses_an_advance_with_nothing_settled`)
- Test: `SiagroB1.Application.Tests/Financials/FinancialAdvancesRefundApplicationTests.cs`

**Interfaces:**
- Consumes: Task 1 (`AvailableAdvanceAmount`, `AppliedAmount`), Task 2 (`AdvanceApplicationSeed`).
- Produces: `FinancialAdvancesRefundService.ExecuteAsync(...)` (assinatura inalterada) devolve `−AvailableAdvanceAmount`; cancela só quando `AppliedAmount == 0`.

- [ ] **Step 1: Testes que falham**

`SiagroB1.Application.Tests/Financials/FinancialAdvancesRefundApplicationTests.cs`:

```csharp
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-14, regra 7: a devolução leva só o crédito disponível (pago − aplicado).</summary>
public class FinancialAdvancesRefundApplicationTests
{
    private static Task RefundAsync(AdvanceApplicationSeed s) =>
        new FinancialAdvancesRefundService(s.Db).ExecuteAsync(
            s.Advance.Key, "CX01", new DateTime(2026, 10, 20), "TED-2", "sobra devolvida", "ana");

    [Fact]
    public async Task Refund_with_application_returns_only_the_credit_and_keeps_the_document()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        await seed.ApplyAsync(6_000m);

        await RefundAsync(seed);

        var refund = await seed.LineAsync(seed.Advance.Key, FinancialSettlementOrigin.AdvanceRefund);
        Assert.Equal(-4_000m, refund.Amount);
        Assert.Equal("CX01", refund.FinancialAccountCode);

        var advance = await seed.ReloadAsync(seed.Advance.Key);
        Assert.Equal(6_000m, advance.SettledAmount);
        Assert.Equal(6_000m, advance.AppliedAmount);
        Assert.Equal(0m, advance.AvailableAdvanceAmount);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, advance.Status);
        Assert.Null(advance.CancellationReason);
    }

    [Fact]
    public async Task Refund_without_application_returns_everything_and_cancels()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();

        await RefundAsync(seed);

        Assert.Equal(-10_000m, (await seed.LineAsync(seed.Advance.Key, FinancialSettlementOrigin.AdvanceRefund)).Amount);
        var advance = await seed.ReloadAsync(seed.Advance.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, advance.Status);
        Assert.Equal(0m, advance.AvailableAdvanceAmount);
    }

    [Fact]
    public async Task Fully_applied_advance_has_nothing_to_refund()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        await seed.ApplyAsync(10_000m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => RefundAsync(seed));

        Assert.Equal("O adiantamento FN-ADT-1 não tem crédito disponível para devolver.", error.Message);
    }
}
```

`git add` o arquivo.

Em `FinancialAdvancesRefundServiceTests.cs`, no teste `Refuses_an_advance_with_nothing_settled`, trocar

```csharp
        Assert.Contains("não há", error.Message, StringComparison.OrdinalIgnoreCase);
```

por

```csharp
        Assert.Equal("O adiantamento FN000217 não tem crédito disponível para devolver.", error.Message);
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialAdvancesRefundApplicationTests|FullyQualifiedName~FinancialAdvancesRefundServiceTests"`
Expected: FAIL — `Refund_with_application_returns_only_the_credit_and_keeps_the_document` (devolve −10.000 e cancela), `Fully_applied_advance_has_nothing_to_refund` (devolve), `Refuses_an_advance_with_nothing_settled` (mensagem antiga).

- [ ] **Step 3: Serviço**

Substituir **todo** o conteúdo de `SiagroB1.Application/Services/Financials/FinancialAdvancesRefundService.cs` por:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Devolução do valor adiantado: o dinheiro saiu e voltou.
///
/// Uma das três saídas para um adiantamento pago cujo contrato alguém quer cancelar (as outras
/// são estornar a baixa e vincular o adiantamento a outro contrato).
///
/// Spec 2026-10-14 (regra 7): devolve só o CRÉDITO DISPONÍVEL (pago − aplicado). Sem nada aplicado é o comportamento
/// de sempre — devolve tudo e o título fica cancelado. Com compensação, a devolução abate só o pago e o título NÃO é
/// cancelado: fica quitado/parcial pelo que sobrou pago, que é igual ao aplicado.
/// </summary>
public class FinancialAdvancesRefundService(IUnitOfWork db)
{
    public async Task ExecuteAsync(
        Guid documentKey,
        string financialAccountCode,
        DateTime refundDate,
        string? documentReference,
        string reason,
        string userName,
        CommitMode commitMode = CommitMode.Auto)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ApplicationException("Informe o motivo da devolução.");

        var document = await db.Context.FinancialDocuments
                           .FirstOrDefaultAsync(x => x.Key == documentKey)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (document.Nature != FinancialDocumentNature.Advance)
            throw new ApplicationException(
                $"O documento {document.Code} não é um adiantamento. Só adiantamento se devolve.");

        if (document.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException($"O documento {document.Code} já está cancelado.");

        // Crédito 0 cobre o não pago e o totalmente aplicado.
        var credit = document.AvailableAdvanceAmount;
        if (credit <= 0m)
            throw new ApplicationException($"O adiantamento {document.Code} não tem crédito disponível para devolver.");

        // Com algo aplicado o título continua vivo: o aplicado é dinheiro que pagou o provisório.
        var keepsDocument = document.AppliedAmount > 0m;

        // Obrigatória: o dinheiro volta para algum lugar concreto. FinancialAccountCode ser
        // anulável no modelo existe para as origens não-caixa, não para esta.
        var account = await db.Context.FinancialAccounts
            .FirstOrDefaultAsync(x => x.Code == financialAccountCode);

        if (account is null)
            throw new ApplicationException("Informe a conta financeira que recebeu a devolução.");

        if (account.Inactive)
            throw new ApplicationException($"A conta financeira {account.Code} está inativa.");

        if (account.Currency != document.Currency)
            throw new ApplicationException(
                $"A conta {account.Code} é em {account.Currency} e o adiantamento em " +
                $"{document.Currency}: a moeda precisa ser a mesma.");

        var refund = new FinancialSettlement
        {
            Key = Guid.NewGuid(),
            FinancialDocumentKey = documentKey,
            FinancialAccountCode = financialAccountCode,
            SettlementDate = refundDate,
            Amount = -credit,
            InterestAmount = 0m,
            FineAmount = 0m,
            DiscountAmount = 0m,
            Origin = FinancialSettlementOrigin.AdvanceRefund,
            ReversedSettlementKey = null,
            DocumentReference = documentReference,
            Notes = reason,
            CreatedAt = DateTime.Now,
            CreatedBy = userName,
            UpdatedAt = DateTime.Now,
            UpdatedBy = userName
        };

        try
        {
            if (commitMode == CommitMode.Auto) await db.BeginTransactionAsync();

            db.Context.FinancialSettlements.Add(refund);
            await db.Context.SaveChangesAsync();

            // O recálculo só ENFILEIRA a mudança no `document` rastreado; o save abaixo persiste
            // recálculo e (se houver) cancelamento de uma vez.
            await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(db.Context, documentKey);

            // O recálculo escreve Status; o cancelamento vem DEPOIS para prevalecer sobre ele.
            if (!keepsDocument)
            {
                document.Status = FinancialDocumentStatus.Canceled;
                document.CancellationReason = $"Adiantamento devolvido: {reason}";
                document.CanceledAt = DateTime.Now;
                document.CanceledBy = userName;
            }

            document.UpdatedAt = DateTime.Now;
            document.UpdatedBy = userName;
            await db.Context.SaveChangesAsync();

            if (commitMode == CommitMode.Auto) await db.CommitAsync();
        }
        catch
        {
            if (commitMode == CommitMode.Auto) await db.RollbackAsync();
            throw;
        }
    }
}
```

- [ ] **Step 4: Rodar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialAdvancesRefundApplicationTests|FullyQualifiedName~FinancialAdvancesRefundServiceTests|FullyQualifiedName~FinancialDocumentsContractCancellationGuardServiceTests"`
Expected: todos PASS.

- [ ] **Step 5: Stage**

```bash
git add SiagroB1.Application/Services/Financials/FinancialAdvancesRefundService.cs SiagroB1.Application.Tests/Financials/FinancialAdvancesRefundServiceTests.cs SiagroB1.Application.Tests/Financials/FinancialAdvancesRefundApplicationTests.cs
```
O controlador commita com:

```
feat(financial): devolução do adiantamento leva só o crédito disponível

Com parte do adiantamento já compensada no provisório, devolver tudo desfaria em silêncio um
pagamento que quitou título. A devolução passa a ser do crédito disponível (pago − aplicado):
sem aplicação, continua devolvendo tudo e cancelando; com aplicação, o título fica ativo pelo
valor aplicado. Sem crédito, recusa.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 5: Revínculo desmembrado e desfazer a transferência (regras 8, 9 e 12)

**Files:**
- Modify: `SiagroB1.Application/Services/Financials/FinancialAdvancesRelinkContractService.cs` (arquivo inteiro)
- Modify: `SiagroB1.Application.Tests/Financials/FinancialAdvancesRelinkContractServiceTests.cs` (fábrica `Service()`)
- Modify: `SiagroB1.Application.Tests/Financials/FinancialDocumentsContractCancellationGuardServiceTests.cs` (~linha 323, construção do serviço)
- Test: `SiagroB1.Application.Tests/Financials/FinancialAdvancesRelinkSplitTests.cs`

**Interfaces:**
- Consumes: Task 1 (`AdvanceTransfer`, `AvailableAdvanceAmount`, `FinancialDocumentChangeLogFields.AdvanceTransfer`), Task 2 (`AdvanceApplicationSeed`), Task 3 (`FinancialDocumentsReverseSettlementService.SplitReversalReason`, regra 9 no estorno), `DocNumberSequenceService` (`GetKeyByTransactionCode`, `GetDocNumber`).
- Produces: `FinancialAdvancesRelinkContractService(IUnitOfWork db, FinancialDocumentChangeLogService changeLog, DocNumberSequenceService docNumberSequence)` — `ExecuteAsync(Guid documentKey, string targetContractType, Guid targetContractKey, string userName)` (assinatura inalterada). O controller existente não muda (DI resolve o construtor novo; `DocNumberSequenceService` já é registrado).

- [ ] **Step 1: Testes que falham**

`SiagroB1.Application.Tests/Financials/FinancialAdvancesRelinkSplitTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Spec 2026-10-14, regras 8–9 (D3): revincular adiantamento parcialmente aplicado DESMEMBRA — o crédito vai para um
/// adiantamento novo no outro contrato; estornar a transferência cancela o novo, se intocado.
/// </summary>
public class FinancialAdvancesRelinkSplitTests
{
    private static FinancialAdvancesRelinkContractService Relink(AdvanceApplicationSeed s) =>
        new(s.Db, new FinancialDocumentChangeLogService(s.Context), s.Firm.Numbers);

    /// <summary>Compensa 3.000 do FN-ADT-1 (10.000 pago) e revincula a sobra ao PC-2: nasce o ST-0002 com 7.000.</summary>
    private static async Task<(PurchaseContract target, FinancialDocument split)> SplitAsync(AdvanceApplicationSeed seed)
    {
        await seed.ApplyAsync(3_000m);
        var target = await seed.Firm.Fiscal.AddContractAsync("PC-2");

        await Relink(seed).ExecuteAsync(seed.Advance.Key, "Purchase", target.Key, "ana");

        var split = await seed.Context.FinancialDocuments.AsNoTracking().SingleAsync(d =>
            d.OriginType == FinancialDocumentOrigin.FinancialDocument && d.OriginKey == seed.Advance.Key);
        return (target, split);
    }

    private static Task<FinancialSettlement> OutgoingTransferAsync(AdvanceApplicationSeed seed) =>
        seed.LineAsync(seed.Advance.Key, FinancialSettlementOrigin.AdvanceTransfer);

    [Fact]
    public async Task Relinking_an_applied_advance_splits_the_credit_into_a_new_paid_advance()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();

        var (target, split) = await SplitAsync(seed);

        Assert.Equal("ST-0002", split.Code);
        Assert.Equal(FinancialDocumentNature.Advance, split.Nature);
        Assert.Equal(FinancialDirection.Payable, split.Direction);
        Assert.Equal(7_000m, split.NetAmount);
        Assert.Equal(7_000m, split.SettledAmount);
        Assert.Equal(0m, split.AppliedAmount);
        Assert.Equal(7_000m, split.AvailableAdvanceAmount);
        Assert.Equal(FinancialDocumentStatus.Settled, split.Status);
        Assert.Equal(target.Key, split.PurchaseContractKey);
        Assert.Equal("PC-2", split.OriginDocNumber);
        Assert.Equal(seed.Advance.CardCode, split.CardCode);
        Assert.Equal(seed.Advance.Currency, split.Currency);
        Assert.Equal(seed.Advance.DueDate, split.DueDate);
        Assert.Equal("Depósito no Banco X", split.PaymentTermsText);
        Assert.Equal("Crédito não aplicado do adiantamento FN-ADT-1.", split.Comments);

        var original = await seed.ReloadAsync(seed.Advance.Key);
        Assert.Equal(seed.Contract.Key, original.PurchaseContractKey);
        Assert.Equal("PC-1", original.OriginDocNumber);
        Assert.Equal(10_000m, original.SettledAmount);
        Assert.Equal(10_000m, original.AppliedAmount);
        Assert.Equal(0m, original.AvailableAdvanceAmount);

        var outgoing = await OutgoingTransferAsync(seed);
        Assert.Equal(7_000m, outgoing.Amount);
        Assert.Null(outgoing.FinancialAccountCode);
        Assert.Equal(split.Key, outgoing.RelatedDocumentKey);
        Assert.Equal("ST-0002", outgoing.DocumentReference);

        var incoming = await seed.LineAsync(split.Key, FinancialSettlementOrigin.AdvanceTransfer);
        Assert.Equal(7_000m, incoming.Amount);
        Assert.Equal(seed.Advance.Key, incoming.RelatedDocumentKey);
        Assert.Equal("FN-ADT-1", incoming.DocumentReference);

        var logs = await seed.Context.FinancialDocumentChangeLogs.AsNoTracking()
            .Where(l => l.Field == FinancialDocumentChangeLogFields.AdvanceTransfer).ToListAsync();
        Assert.Equal("ST-0002: 7.000,00", logs.Single(l => l.FinancialDocumentKey == seed.Advance.Key).NewValue);
        Assert.Equal("FN-ADT-1: 7.000,00", logs.Single(l => l.FinancialDocumentKey == split.Key).NewValue);
    }

    [Fact]
    public async Task Relinking_without_application_still_moves_the_whole_advance()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var target = await seed.Firm.Fiscal.AddContractAsync("PC-2");

        await Relink(seed).ExecuteAsync(seed.Advance.Key, "Purchase", target.Key, "ana");

        Assert.Equal(target.Key, (await seed.ReloadAsync(seed.Advance.Key)).PurchaseContractKey);
        Assert.Empty(seed.Context.FinancialDocuments.AsNoTracking()
            .Where(d => d.OriginType == FinancialDocumentOrigin.FinancialDocument));
    }

    [Fact]
    public async Task Splitting_without_credit_is_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        await seed.ApplyAsync(10_000m);
        var target = await seed.Firm.Fiscal.AddContractAsync("PC-2");

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Relink(seed).ExecuteAsync(seed.Advance.Key, "Purchase", target.Key, "ana"));

        Assert.Equal("O adiantamento FN-ADT-1 não tem crédito disponível para vincular a outro contrato.", error.Message);
    }

    [Fact]
    public async Task Reversing_the_transfer_cancels_the_split_advance_and_returns_the_credit()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var (_, split) = await SplitAsync(seed);

        await seed.Reverse().ExecuteAsync((await OutgoingTransferAsync(seed)).Key, "revínculo errado", "bia");

        var canceled = await seed.ReloadAsync(split.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, canceled.Status);
        Assert.Equal("Transferência de crédito estornada", canceled.CancellationReason);
        Assert.Equal(0m, canceled.SettledAmount);

        var original = await seed.ReloadAsync(seed.Advance.Key);
        Assert.Equal(3_000m, original.AppliedAmount);
        Assert.Equal(7_000m, original.AvailableAdvanceAmount);
    }

    [Fact]
    public async Task Reversing_the_transfer_of_a_used_split_advance_is_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var (target, split) = await SplitAsync(seed);
        var c2 = await seed.Firm.ConciliateAsync(contract: target, taxNumber: "1002");
        var provisional2 = await seed.Firm.AddProvisionalAsync(target, 50_000m, "FN-PROV-2");
        await seed.ComposeAsync(provisional2, c2.Firm!);
        await seed.ApplyAsync(2_000m, advance: split, provisional: provisional2);

        var error = await Assert.ThrowsAsync<ApplicationException>(async () =>
            await seed.Reverse().ExecuteAsync((await OutgoingTransferAsync(seed)).Key, "revínculo errado", "bia"));

        Assert.Equal(
            "O adiantamento ST-0002 já foi usado: desfaça as compensações dele antes de estornar a transferência.",
            error.Message);
    }

    [Fact]
    public async Task Undone_applications_free_the_transfer_reversal()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var (target, split) = await SplitAsync(seed);
        var c2 = await seed.Firm.ConciliateAsync(contract: target, taxNumber: "1002");
        var provisional2 = await seed.Firm.AddProvisionalAsync(target, 50_000m, "FN-PROV-2");
        await seed.ComposeAsync(provisional2, c2.Firm!);
        await seed.ApplyAsync(2_000m, advance: split, provisional: provisional2);
        var application = await seed.LineAsync(split.Key, FinancialSettlementOrigin.AdvanceApplication);
        await seed.Reverse().ExecuteAsync(application.Key, "compensação errada", "bia");

        await seed.Reverse().ExecuteAsync((await OutgoingTransferAsync(seed)).Key, "revínculo errado", "bia");

        Assert.Equal(FinancialDocumentStatus.Canceled, (await seed.ReloadAsync(split.Key)).Status);
        Assert.Equal(7_000m, (await seed.ReloadAsync(seed.Advance.Key)).AvailableAdvanceAmount);
    }

    [Fact]
    public async Task Refunded_split_advance_blocks_the_transfer_reversal()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var (_, split) = await SplitAsync(seed);
        await new FinancialAdvancesRefundService(seed.Db).ExecuteAsync(
            split.Key, "CX01", new DateTime(2026, 10, 20), null, "produtor pediu de volta", "ana");

        var error = await Assert.ThrowsAsync<ApplicationException>(async () =>
            await seed.Reverse().ExecuteAsync((await OutgoingTransferAsync(seed)).Key, "revínculo errado", "bia"));

        Assert.Equal("Não é possível estornar a baixa de um título cancelado.", error.Message);
    }

    [Fact]
    public async Task Reversing_the_payment_of_a_split_advance_is_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        await SplitAsync(seed);
        var payment = await seed.LineAsync(seed.Advance.Key, FinancialSettlementOrigin.Manual);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            seed.Reverse().ExecuteAsync(payment.Key, "pagamento errado", "bia"));

        Assert.Equal(
            "O adiantamento FN-ADT-1 tem 10.000,00 aplicado: desfaça as compensações antes de estornar o pagamento.",
            error.Message);
    }
}
```

`git add` o arquivo.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialAdvancesRelinkSplitTests"`
Expected: erro de compilação (o construtor de três argumentos não existe).

- [ ] **Step 3: Serviço**

Substituir **todo** o conteúdo de `SiagroB1.Application/Services/Financials/FinancialAdvancesRelinkContractService.cs` por:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Vincula um adiantamento a OUTRO contrato.
///
/// Sem nada aplicado, move o adiantamento INTEIRO (comportamento original): o contrato antigo fica
/// sem adiantamento e o cancelamento passa; o contrato novo herda o adiantamento e passa a ser
/// protegido pelo mesmo guard.
///
/// Spec 2026-10-14 (regra 8, D3): com algo aplicado, DESMEMBRA — o original fica no contrato com o aplicado, e só o
/// crédito disponível vai para um adiantamento NOVO no destino (OriginType = FinancialDocument, OriginKey = original),
/// pago pela transferência em par (AdvanceTransfer de saída no original, de entrada no novo). É o único lugar onde
/// AdvanceTransfer nasce (regra 12). Desfaz-se pelo "Estornar baixa" da transferência (regra 9).
///
/// O índice único filtrado IX_FINANCIAL_DOCUMENTS_ProvisionalOrigin cobre apenas
/// Nature = Provisional, então adiantamento migrando ou nascendo noutro contrato nunca colide com ele.
/// </summary>
public class FinancialAdvancesRelinkContractService(
    IUnitOfWork db,
    FinancialDocumentChangeLogService changeLog,
    DocNumberSequenceService docNumberSequence)
{
    public async Task ExecuteAsync(
        Guid documentKey, string targetContractType, Guid targetContractKey, string userName)
    {
        var document = await db.Context.FinancialDocuments
                           .FirstOrDefaultAsync(x => x.Key == documentKey)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (document.Nature != FinancialDocumentNature.Advance)
            throw new ApplicationException(
                $"O documento {document.Code} não é um adiantamento. Só adiantamento troca de contrato.");

        if (document.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException($"O documento {document.Code} está cancelado.");

        var isPurchase = string.Equals(targetContractType, "Purchase", StringComparison.OrdinalIgnoreCase);
        var isSales = string.Equals(targetContractType, "Sales", StringComparison.OrdinalIgnoreCase);

        if (!isPurchase && !isSales)
            throw new ApplicationException("Tipo de contrato inválido. Informe Purchase ou Sales.");

        // Direção cruzada é erro de NEGÓCIO: título a pagar é do produtor, a receber é do cliente.
        if (isPurchase && document.Direction != FinancialDirection.Payable)
            throw new ApplicationException(
                "Um título a receber só pode ser vinculado a um contrato de venda.");

        if (isSales && document.Direction != FinancialDirection.Receivable)
            throw new ApplicationException(
                "Um título a pagar só pode ser vinculado a um contrato de compra.");

        string targetCode, targetCardCode, targetBranchCode;
        CurrencyType targetCurrency;
        ContractStatus? targetStatus; // Status é nullable no modelo (PurchaseContract/SalesContract).

        if (isPurchase)
        {
            var contract = await db.Context.PurchaseContracts.AsNoTracking()
                               .FirstOrDefaultAsync(x => x.Key == targetContractKey)
                           ?? throw new NotFoundException("Contrato de compra não encontrado.");

            targetCode = contract.Code ?? string.Empty;
            targetCardCode = contract.CardCode;
            targetBranchCode = contract.BranchCode ?? string.Empty;
            targetCurrency = contract.StandardCurrency ?? CurrencyType.Brl;
            targetStatus = contract.Status;
        }
        else
        {
            var contract = await db.Context.SalesContracts.AsNoTracking()
                               .FirstOrDefaultAsync(x => x.Key == targetContractKey)
                           ?? throw new NotFoundException("Contrato de venda não encontrado.");

            targetCode = contract.Code ?? string.Empty;
            targetCardCode = contract.CardCode;
            targetBranchCode = contract.BranchCode ?? string.Empty;
            targetCurrency = contract.StandardCurrency ?? CurrencyType.Brl;
            targetStatus = contract.Status;
        }

        var currentKey = isPurchase ? document.PurchaseContractKey : document.SalesContractKey;

        if (currentKey == targetContractKey)
            throw new ApplicationException(
                $"O adiantamento {document.Code} já está vinculado ao contrato {targetCode}.");

        if (targetStatus != ContractStatus.Approved)
            throw new ApplicationException(
                $"O contrato {targetCode} precisa estar aprovado para receber o adiantamento.");

        // O dinheiro é DAQUELE parceiro: é este guard que impede mover crédito de um produtor
        // para o contrato de outro.
        if (!string.Equals(targetCardCode, document.CardCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(
                $"O contrato {targetCode} é de outro parceiro ({targetCardCode}). O adiantamento " +
                $"é de {document.CardCode} e só pode migrar entre contratos do mesmo parceiro.");

        if (!string.Equals(targetBranchCode, document.BranchCode ?? string.Empty,
                StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(
                $"O contrato {targetCode} é da filial {targetBranchCode} e o adiantamento da " +
                $"filial {document.BranchCode}: a filial precisa ser a mesma.");

        if (targetCurrency != document.Currency)
            throw new ApplicationException(
                $"O contrato {targetCode} é em {targetCurrency} e o adiantamento em " +
                $"{document.Currency}: a moeda precisa ser a mesma.");

        if (document.AppliedAmount > 0m)
        {
            await SplitAsync(document, isPurchase, targetContractKey, targetCode, userName);
            return;
        }

        var oldCode = document.OriginDocNumber;

        document.PurchaseContractKey = isPurchase ? targetContractKey : null;
        document.SalesContractKey = isSales ? targetContractKey : null;
        document.OriginDocNumber = targetCode;
        document.UpdatedAt = DateTime.Now;
        document.UpdatedBy = userName;

        // PaymentTermsText NÃO muda: é a cópia de "onde pagar" do contrato de origem, e para um
        // adiantamento já pago foi por ali que o dinheiro saiu.

        changeLog.Register(documentKey, FinancialDocumentChangeLogFields.Contract,
            oldCode, targetCode, userName);

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Regra 8: adiantamento NOVO no destino com o crédito disponível, pago pela transferência em par; o original fica
    /// com crédito 0. Tudo num SaveChanges (o recálculo enxerga o título e as linhas ainda pendentes).
    /// </summary>
    private async Task SplitAsync(
        FinancialDocument original, bool isPurchase, Guid targetContractKey, string targetCode, string userName)
    {
        var credit = original.AvailableAdvanceAmount;
        if (credit <= 0m)
            throw new ApplicationException(
                $"O adiantamento {original.Code} não tem crédito disponível para vincular a outro contrato.");

        // Número buscado ANTES do SaveChanges e só depois de todas as validações: Dapper em conexão própria com UPDLOCK.
        var docNumberKey = await docNumberSequence.GetKeyByTransactionCode(TransactionCode.FinancialDocument);
        var code = await docNumberSequence.GetDocNumber(docNumberKey);
        var now = DateTime.Now;

        var split = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = code,
            DocNumberKey = docNumberKey,
            BranchCode = original.BranchCode,
            Direction = original.Direction,
            Nature = FinancialDocumentNature.Advance,
            Status = FinancialDocumentStatus.Open,
            CardCode = original.CardCode,
            CardName = original.CardName,
            DocumentDate = now,
            DueDate = original.DueDate,
            Currency = original.Currency,
            NetAmount = credit,
            OriginType = FinancialDocumentOrigin.FinancialDocument,
            OriginKey = original.Key,
            OriginDocNumber = targetCode,
            PurchaseContractKey = isPurchase ? targetContractKey : null,
            SalesContractKey = isPurchase ? null : targetContractKey,
            PaymentTermsText = original.PaymentTermsText,
            Comments = $"Crédito não aplicado do adiantamento {original.Code}.",
            CreatedAt = now, CreatedBy = userName, UpdatedAt = now, UpdatedBy = userName,
        };
        db.Context.FinancialDocuments.Add(split);

        db.Context.FinancialSettlements.AddRange(
            Transfer(original, split, credit, now, userName),
            Transfer(split, original, credit, now, userName));

        changeLog.Register(original.Key, FinancialDocumentChangeLogFields.AdvanceTransfer, null,
            FinancialDocumentsComposeService.Describe(split.Code, credit), userName);
        changeLog.Register(split.Key, FinancialDocumentChangeLogFields.AdvanceTransfer, null,
            FinancialDocumentsComposeService.Describe(original.Code, credit), userName);

        original.UpdatedAt = now;
        original.UpdatedBy = userName;

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(db.Context, original.Key);
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(db.Context, split.Key);

        await db.SaveChangesAsync();
    }

    /// <summary>Uma ponta da transferência: Amount positivo, sem conta, RelatedDocumentKey = a outra ponta.</summary>
    private static FinancialSettlement Transfer(
        FinancialDocument document, FinancialDocument other, decimal amount, DateTime now, string userName) => new()
    {
        Key = Guid.NewGuid(),
        FinancialDocumentKey = document.Key,
        FinancialAccountCode = null,
        SettlementDate = now.Date,
        Amount = amount,
        Origin = FinancialSettlementOrigin.AdvanceTransfer,
        RelatedDocumentKey = other.Key,
        DocumentReference = other.Code,
        CreatedAt = now, CreatedBy = userName, UpdatedAt = now, UpdatedBy = userName,
    };
}
```

(`TransactionCode` mora em `SiagroB1.Domain.Enums`, já importado.)

- [ ] **Step 4: Construções existentes do serviço**

Em `FinancialAdvancesRelinkContractServiceTests.cs`, trocar a fábrica

```csharp
    private FinancialAdvancesRelinkContractService Service() =>
        new(_db, new FinancialDocumentChangeLogService(_db.Context));
```

por

```csharp
    private FinancialAdvancesRelinkContractService Service() =>
        new(_db, new FinancialDocumentChangeLogService(_db.Context), new FakeDocNumberSequenceService());
```

Em `FinancialDocumentsContractCancellationGuardServiceTests.cs` (~linha 323), trocar

```csharp
            await new FinancialAdvancesRelinkContractService(
                    new UnitOfWork(relink), new FinancialDocumentChangeLogService(relink))
```

por

```csharp
            await new FinancialAdvancesRelinkContractService(
                    new UnitOfWork(relink), new FinancialDocumentChangeLogService(relink), new FakeDocNumberSequenceService())
```

(Acrescentar `using SiagroB1.Application.Tests.Support;` no arquivo se ainda não houver.)

- [ ] **Step 5: Rodar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialAdvancesRelinkSplitTests|FullyQualifiedName~FinancialAdvancesRelinkContractServiceTests|FullyQualifiedName~FinancialDocumentsContractCancellationGuardServiceTests|FullyQualifiedName~FinancialDocumentsAdvanceApplicationReverseTests"`
Expected: todos PASS.

- [ ] **Step 6: Stage**

```bash
git add SiagroB1.Application/Services/Financials/FinancialAdvancesRelinkContractService.cs SiagroB1.Application.Tests/Financials/FinancialAdvancesRelinkSplitTests.cs SiagroB1.Application.Tests/Financials/FinancialAdvancesRelinkContractServiceTests.cs SiagroB1.Application.Tests/Financials/FinancialDocumentsContractCancellationGuardServiceTests.cs
```
O controlador commita com:

```
feat(financial): revínculo desmembra adiantamento parcialmente aplicado

Mover o adiantamento inteiro levaria junto o que já pagou o provisório deste contrato. Com
algo aplicado, só o crédito disponível vai para um adiantamento novo no contrato destino, pago
por uma transferência em par; o original fica com o aplicado e crédito zero. Estornar a
transferência cancela o adiantamento novo, recusado se ele já foi usado.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 6: Encerrar contrato de compra recusa crédito de adiantamento (regra 10)

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsCloseService.cs`
- Test: `SiagroB1.Application.Tests/Financials/PurchaseContractsCloseAdvanceCreditTests.cs`

**Interfaces:**
- Consumes: Task 1 (`AppliedAmount`), Tasks 2, 4, 5 (aplicar, devolver, revincular nos testes).
- Produces: `PurchaseContractsCloseService.ExecuteAsync` recusa com A9 (construtor inalterado).

- [ ] **Step 1: Testes que falham**

`SiagroB1.Application.Tests/Financials/PurchaseContractsCloseAdvanceCreditTests.cs`:

```csharp
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-14, regra 10 (D2): encerrar o contrato de compra exige crédito de adiantamento zero.</summary>
public class PurchaseContractsCloseAdvanceCreditTests
{
    private static PurchaseContractsCloseService Close(AdvanceApplicationSeed s) =>
        new(s.Context, new PurchaseContractsFixedVolumeService(s.Context), TestNotificationOutbox.For(s.Context),
            TestConfiguration.Empty(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PurchaseContractsCloseService>.Instance);

    /// <summary>Fixação confirmada do provisório do seed (mesma do FinancialDocumentFirmLocksTests).</summary>
    private static async Task AddFixationAsync(AdvanceApplicationSeed s)
    {
        s.Context.PurchaseContractsPriceFixations.Add(new PurchaseContractPriceFixation
        {
            Key = s.Provisional.OriginKey!.Value, PurchaseContractKey = s.Contract.Key, FixationVolume = 9_864.165m,
            FixationPrice = 2m, Status = PriceFixationStatus.Confirmed, FinancialDueDate = new DateTime(2026, 12, 31),
        });
        await s.Context.SaveChangesAsync();
    }

    /// <summary>Provisório de 19.728,33 quitado por compensação de um adiantamento pago de <paramref name="advance"/>.</summary>
    private static async Task<AdvanceApplicationSeed> SettledByAdvanceAsync(decimal advance)
    {
        var seed = await AdvanceApplicationSeed.CreateAsync(
            provisionalAmount: FirmPayableSeed.DefaultFirmAmount, advanceAmount: advance, advancePaid: advance);
        await AddFixationAsync(seed);
        await seed.ApplyAsync(FirmPayableSeed.DefaultFirmAmount);
        return seed;
    }

    [Fact]
    public async Task Closing_with_credit_left_is_refused()
    {
        var seed = await SettledByAdvanceAsync(20_000m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Close(seed).ExecuteAsync(seed.Contract.Key, "ana"));

        Assert.Equal(
            "O adiantamento FN-ADT-1 tem 271,67 de crédito não aplicado: compense, devolva ou vincule a outro contrato antes de encerrar.",
            error.Message);
        Assert.Equal(ContractStatus.Approved, seed.Contract.Status);
    }

    [Fact]
    public async Task Closing_after_applying_all_the_credit_passes()
    {
        var seed = await SettledByAdvanceAsync(FirmPayableSeed.DefaultFirmAmount);

        await Close(seed).ExecuteAsync(seed.Contract.Key, "ana");

        Assert.Equal(ContractStatus.Finished, seed.Contract.Status);
    }

    [Fact]
    public async Task Closing_after_refunding_the_credit_passes()
    {
        var seed = await SettledByAdvanceAsync(20_000m);
        await new FinancialAdvancesRefundService(seed.Db).ExecuteAsync(
            seed.Advance.Key, "CX01", new DateTime(2026, 10, 20), null, "sobra devolvida", "ana");

        await Close(seed).ExecuteAsync(seed.Contract.Key, "ana");

        Assert.Equal(ContractStatus.Finished, seed.Contract.Status);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, (await seed.ReloadAsync(seed.Advance.Key)).Status);
    }

    [Fact]
    public async Task Closing_after_relinking_the_credit_passes()
    {
        var seed = await SettledByAdvanceAsync(20_000m);
        var target = await seed.Firm.Fiscal.AddContractAsync("PC-2");
        await new FinancialAdvancesRelinkContractService(
                seed.Db, new FinancialDocumentChangeLogService(seed.Context), seed.Firm.Numbers)
            .ExecuteAsync(seed.Advance.Key, "Purchase", target.Key, "ana");

        await Close(seed).ExecuteAsync(seed.Contract.Key, "ana");

        Assert.Equal(ContractStatus.Finished, seed.Contract.Status);
    }

    [Fact]
    public async Task Unpaid_advance_does_not_block_the_closing()
    {
        var seed = await SettledByAdvanceAsync(FirmPayableSeed.DefaultFirmAmount);
        await seed.AddAdvanceAsync(seed.Contract, 5_000m, 0m, "FN-ADT-2");

        await Close(seed).ExecuteAsync(seed.Contract.Key, "ana");

        Assert.Equal(ContractStatus.Finished, seed.Contract.Status);
    }
}
```

`git add` o arquivo.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractsCloseAdvanceCreditTests"`
Expected: FAIL — `Closing_with_credit_left_is_refused` (encerra sem exceção); os demais PASS.

- [ ] **Step 3: Trava**

`PurchaseContractsCloseService.ExecuteAsync`, logo depois de `await GuardFinancialDocumentsAsync(contract, userName);`:

```csharp

        await GuardAdvanceCreditAsync(contract);
```

E acrescentar, logo antes de `private async Task CancelLegacyUntouchedProvisionalsAsync(`:

```csharp
    /// <summary>
    /// Spec 2026-10-14 (regra 10, D2): nenhum adiantamento não cancelado do contrato com crédito não aplicado — dinheiro
    /// pago ao produtor que não abateu nada. Saídas: compensar no provisório, devolver ou revincular a outro contrato.
    /// Crédito = pago − aplicado (o EF traduz a subtração; a propriedade [NotMapped] não).
    /// </summary>
    private async Task GuardAdvanceCreditAsync(PurchaseContract contract)
    {
        var advance = await context.FinancialDocuments.AsNoTracking()
            .Where(x => x.PurchaseContractKey == contract.Key &&
                        x.Nature == FinancialDocumentNature.Advance &&
                        x.Status != FinancialDocumentStatus.Canceled &&
                        x.SettledAmount - x.AppliedAmount > 0m)
            .OrderBy(x => x.Code)
            .Select(x => new { x.Code, x.SettledAmount, x.AppliedAmount })
            .FirstOrDefaultAsync();

        if (advance is null)
            return;

        var credit = decimal.Round(advance.SettledAmount - advance.AppliedAmount, 2, MidpointRounding.ToEven);
        throw new ApplicationException(
            $"O adiantamento {advance.Code} tem {credit.ToString("N2", CultureInfo.GetCultureInfo("pt-BR"))} de crédito não aplicado: compense, devolva ou vincule a outro contrato antes de encerrar.");
    }
```

(`System.Globalization` já é importado no arquivo.)

- [ ] **Step 4: Rodar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractsCloseAdvanceCreditTests|FullyQualifiedName~FinancialDocumentFirmLocksTests|FullyQualifiedName~FinancialDocumentUndoHooksTests"`
Expected: todos PASS.

- [ ] **Step 5: Stage**

```bash
git add SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsCloseService.cs SiagroB1.Application.Tests/Financials/PurchaseContractsCloseAdvanceCreditTests.cs
```
O controlador commita com:

```
feat(purchase-contract): encerrar contrato recusa crédito de adiantamento não aplicado

Contrato encerrado com adiantamento pago e não aplicado deixaria dinheiro do produtor parado
sem abater nada e fora de qualquer tela de ação. O encerramento passa a exigir crédito zero
em todos os adiantamentos ativos do contrato: compense no provisório, devolva ou revincule.
Adiantamento não pago ou já devolvido não trava.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 7: Leituras — candidatos de compensação e financeiro do contrato

**Files:**
- Create: `SiagroB1.Domain/Dtos/FinancialDocumentAdvanceCandidateDto.cs`
- Create: `SiagroB1.Application/Services/Financials/FinancialDocumentsGetAdvanceCandidatesService.cs`
- Modify: `SiagroB1.Domain/Dtos/FinancialDocumentByContractDto.cs`, `SiagroB1.Application/Services/Financials/FinancialDocumentsGetByContractService.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (depois de `services.AddScoped<FinancialDocumentsApplyAdvanceService>();`)
- Test: `SiagroB1.Application.Tests/Financials/FinancialDocumentsAdvanceReadTests.cs`

**Interfaces:**
- Consumes: Tasks 1–2.
- Produces:
  - `FinancialDocumentAdvanceCandidateDto { DocumentKey: Guid, Code: string?, DocumentDate: DateTime, SettledAmount, AppliedAmount, AvailableAdvanceAmount, SuggestedAmount: decimal }` (PascalCase por `[JsonPropertyName]`).
  - `FinancialDocumentsGetAdvanceCandidatesService(IUnitOfWork db)`: `Task<List<FinancialDocumentAdvanceCandidateDto>> ExecuteAsync(Guid key)`.
  - `FinancialDocumentByContractDto.AppliedAmount`, `.AvailableAdvanceAmount`.

- [ ] **Step 1: Testes que falham**

`SiagroB1.Application.Tests/Financials/FinancialDocumentsAdvanceReadTests.cs`:

```csharp
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-14: candidatos do diálogo "Compensar adiantamento" e aplicado/crédito no financeiro do contrato.</summary>
public class FinancialDocumentsAdvanceReadTests
{
    private static FinancialDocumentsGetAdvanceCandidatesService Candidates(AdvanceApplicationSeed s) => new(s.Db);

    [Fact]
    public async Task Candidates_are_the_contract_advances_with_credit_in_the_same_currency()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        await seed.AddAdvanceAsync(seed.Contract, 30_000m, 30_000m, "FN-ADT-2");
        await seed.AddAdvanceAsync(seed.Contract, 5_000m, 0m, "FN-ADT-3");
        await seed.AddAdvanceAsync(seed.Contract, 5_000m, 0m, "FN-ADT-4", CurrencyType.Usd);
        var other = await seed.Firm.Fiscal.AddContractAsync("PC-2");
        await seed.AddAdvanceAsync(other, 5_000m, 5_000m, "FN-ADT-5");

        var list = await Candidates(seed).ExecuteAsync(seed.Provisional.Key);

        Assert.Equal(new[] { "FN-ADT-1", "FN-ADT-2" }, list.Select(c => c.Code!).Order().ToArray());
        Assert.Equal(10_000m, list.Single(c => c.Code == "FN-ADT-1").SuggestedAmount);
        Assert.Equal(19_728.33m, list.Single(c => c.Code == "FN-ADT-2").SuggestedAmount);
    }

    [Fact]
    public async Task Candidate_shows_paid_applied_and_credit()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        await seed.ApplyAsync(6_000m);

        var candidate = (await Candidates(seed).ExecuteAsync(seed.Provisional.Key)).Single();

        Assert.Equal(seed.Advance.Key, candidate.DocumentKey);
        Assert.Equal(10_000m, candidate.SettledAmount);
        Assert.Equal(6_000m, candidate.AppliedAmount);
        Assert.Equal(4_000m, candidate.AvailableAdvanceAmount);
        Assert.Equal(4_000m, candidate.SuggestedAmount);
    }

    [Fact]
    public async Task A_key_that_is_not_a_purchase_provisional_has_no_candidates()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();

        Assert.Empty(await Candidates(seed).ExecuteAsync(seed.Advance.Key));
    }

    [Fact]
    public async Task By_contract_rows_carry_applied_and_credit()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        await seed.ApplyAsync(6_000m);

        var rows = (await new FinancialDocumentsGetByContractService(seed.Db)
            .ExecuteAsync("Purchase", seed.Contract.Key)).ToList();

        var advance = rows.Single(r => r.Code == "FN-ADT-1");
        Assert.Equal(6_000m, advance.AppliedAmount);
        Assert.Equal(4_000m, advance.AvailableAdvanceAmount);
        Assert.Equal(0m, rows.Single(r => r.Code == "FN-PROV-1").AvailableAdvanceAmount);
    }
}
```

`git add` o arquivo.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsAdvanceReadTests"`
Expected: erro de compilação (serviço e propriedades novas não existem).

- [ ] **Step 3: DTOs**

`SiagroB1.Domain/Dtos/FinancialDocumentAdvanceCandidateDto.cs`:

```csharp
using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Function FinancialDocumentsGetAdvanceCandidates (spec 2026-10-14): adiantamento que pode ser compensado no provisório.
/// PascalCase explícito: sem os atributos o ASP.NET serializa camelCase e a tela lê undefined.
/// </summary>
public class FinancialDocumentAdvanceCandidateDto
{
    [JsonPropertyName("DocumentKey")] public Guid DocumentKey { get; set; }
    [JsonPropertyName("Code")] public string? Code { get; set; }
    [JsonPropertyName("DocumentDate")] public DateTime DocumentDate { get; set; }
    [JsonPropertyName("SettledAmount")] public decimal SettledAmount { get; set; }
    [JsonPropertyName("AppliedAmount")] public decimal AppliedAmount { get; set; }
    [JsonPropertyName("AvailableAdvanceAmount")] public decimal AvailableAdvanceAmount { get; set; }

    /// <summary>min(disponível para baixa do provisório, crédito do adiantamento) — regra 4.</summary>
    [JsonPropertyName("SuggestedAmount")] public decimal SuggestedAmount { get; set; }
}
```

`SiagroB1.Domain/Dtos/FinancialDocumentByContractDto.cs`, depois de `ComposedAmount`:

```csharp
    [JsonPropertyName("AppliedAmount")] public decimal AppliedAmount { get; set; }
    [JsonPropertyName("AvailableAdvanceAmount")] public decimal AvailableAdvanceAmount { get; set; }
```

`FinancialDocumentsGetByContractService.cs`, na projeção, depois de `ComposedAmount = x.ComposedAmount,`:

```csharp
            AppliedAmount = x.AppliedAmount,
            AvailableAdvanceAmount = x.AvailableAdvanceAmount,
```

- [ ] **Step 4: Serviço de candidatos**

`SiagroB1.Application/Services/Financials/FinancialDocumentsGetAdvanceCandidatesService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Candidatos do "Compensar adiantamento" (spec 2026-10-14, regras 2 e 4): adiantamentos não cancelados do MESMO
/// contrato de compra e MESMA moeda do provisório, com crédito > 0, com o valor sugerido. Título que não é provisório
/// de compra ativo devolve lista vazia — a tela só oferece o botão no provisório. Projeção em memória porque
/// AvailableAdvanceAmount/AvailableToSettleAmount são [NotMapped].
/// </summary>
public class FinancialDocumentsGetAdvanceCandidatesService(IUnitOfWork db)
{
    public async Task<List<FinancialDocumentAdvanceCandidateDto>> ExecuteAsync(Guid key)
    {
        var provisional = await db.Context.FinancialDocuments.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key)
                          ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (provisional.Nature != FinancialDocumentNature.Provisional ||
            provisional.Status == FinancialDocumentStatus.Canceled ||
            provisional.PurchaseContractKey is null)
            return [];

        var available = provisional.AvailableToSettleAmount;

        var advances = await db.Context.FinancialDocuments.AsNoTracking()
            .Where(d => d.PurchaseContractKey == provisional.PurchaseContractKey &&
                        d.Nature == FinancialDocumentNature.Advance &&
                        d.Status != FinancialDocumentStatus.Canceled &&
                        d.Currency == provisional.Currency &&
                        d.SettledAmount - d.AppliedAmount > 0m)
            .OrderBy(d => d.DocumentDate).ThenBy(d => d.Code)
            .ToListAsync();

        return advances.Select(d => new FinancialDocumentAdvanceCandidateDto
        {
            DocumentKey = d.Key,
            Code = d.Code,
            DocumentDate = d.DocumentDate,
            SettledAmount = d.SettledAmount,
            AppliedAmount = d.AppliedAmount,
            AvailableAdvanceAmount = d.AvailableAdvanceAmount,
            SuggestedAmount = Math.Min(available, d.AvailableAdvanceAmount),
        }).ToList();
    }
}
```

`git add` os dois arquivos novos.

- [ ] **Step 5: DI**

`ServiceCollectionExtensions.cs`, depois de `services.AddScoped<FinancialDocumentsApplyAdvanceService>();`:

```csharp
        services.AddScoped<FinancialDocumentsGetAdvanceCandidatesService>();
```

- [ ] **Step 6: Rodar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsAdvanceReadTests|FullyQualifiedName~FinancialDocumentsReadModelTests"`
Expected: todos PASS.

- [ ] **Step 7: Stage**

```bash
git add SiagroB1.Domain/Dtos/FinancialDocumentAdvanceCandidateDto.cs SiagroB1.Domain/Dtos/FinancialDocumentByContractDto.cs SiagroB1.Application/Services/Financials/FinancialDocumentsGetAdvanceCandidatesService.cs SiagroB1.Application/Services/Financials/FinancialDocumentsGetByContractService.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/Financials/FinancialDocumentsAdvanceReadTests.cs
```
O controlador commita com:

```
feat(financial): candidatos de compensação e crédito no financeiro do contrato

O diálogo de compensar precisa dos adiantamentos do contrato e moeda do provisório com
crédito, já com o valor sugerido (menor entre o disponível do provisório e o crédito). O
financeiro do contrato passa a trazer aplicado e crédito disponível de cada adiantamento.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 8: Endpoints OData (action e function) e testes de EDM

**Files:**
- Create: `SiagroB1.Web/Actions/Financials/FinancialDocumentsApplyAdvanceController.cs`
- Create: `SiagroB1.Web/Functions/Financials/FinancialDocumentsGetAdvanceCandidatesController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (logo depois de `financialComposition.Returns<FinancialDocumentCompositionDto>();`)
- Test: `SiagroB1.Application.Tests/Financials/FinancialAdvanceApplicationEdmModelTests.cs`

**Interfaces:**
- Consumes: Task 2 (`FinancialDocumentsApplyAdvanceService`), Task 7 (`FinancialDocumentsGetAdvanceCandidatesService`, DTO).
- Produces (o frontend chama por `sendJson`):
  - `POST odata/FinancialDocumentsApplyAdvance` `{ Key: Guid, AdvanceKey: Guid, Amount: number, SettlementDate: "yyyy-MM-dd" }` → `200`; recusa de negócio `400` com o texto; não encontrado `404`.
  - `GET odata/FinancialDocumentsGetAdvanceCandidates(Key={guid})` → `{ value: FinancialDocumentAdvanceCandidateDto[] }`.

- [ ] **Step 1: Teste de EDM que falha**

`SiagroB1.Application.Tests/Financials/FinancialAdvanceApplicationEdmModelTests.cs`:

```csharp
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>EDM da compensação de adiantamento (spec 2026-10-14): chaves Guid, dinheiro Edm.Double, data string.</summary>
public class FinancialAdvanceApplicationEdmModelTests
{
    private static IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("Key", "Edm.Guid")]
    [InlineData("AdvanceKey", "Edm.Guid")]
    [InlineData("Amount", "Edm.Double")]
    [InlineData("SettlementDate", "Edm.String")]
    public void The_apply_action_parameters_have_the_expected_types(string parameter, string type)
    {
        var action = BuildModel().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == "FinancialDocumentsApplyAdvance");

        Assert.Equal(type, action.Parameters.Single(p => p.Name == parameter).Type.FullName());
    }

    [Fact]
    public void The_candidates_function_takes_the_key_and_returns_a_collection()
    {
        var function = BuildModel().SchemaElements.OfType<IEdmFunction>()
            .Single(f => f.Name == "FinancialDocumentsGetAdvanceCandidates");

        Assert.Equal("Edm.Guid", function.Parameters.Single(p => p.Name == "Key").Type.FullName());
        Assert.True(function.ReturnType.IsCollection());
    }

    [Theory]
    [InlineData(nameof(FinancialDocument.AppliedAmount))]
    [InlineData(nameof(FinancialDocument.AvailableAdvanceAmount))]
    public void Applied_and_credit_are_in_the_edm(string property)
    {
        var type = BuildModel().EntityContainer.FindEntitySet("FinancialDocuments").EntityType;

        Assert.NotNull(type.FindProperty(property));
    }
}
```

`git add` o arquivo.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialAdvanceApplicationEdmModelTests"`
Expected: FAIL — `Sequence contains no matching element` nos testes de action/function; os de propriedade PASS (Task 1).

- [ ] **Step 3: EDM**

`ODataConfigurations.cs`, logo depois de `financialComposition.Returns<FinancialDocumentCompositionDto>();`:

```csharp

        // Compensação de adiantamento no provisório (spec 2026-10-14). ⚠️ Dinheiro em Edm.Double e data em string,
        // como na baixa. Nenhum parâmetro opcional: uma rota só. A transferência de crédito NÃO tem action (regra 12):
        // nasce só pelo revínculo desmembrado.
        var financialApplyAdvance = modelBuilder.Action("FinancialDocumentsApplyAdvance");
        financialApplyAdvance.Parameter<Guid>("Key");
        financialApplyAdvance.Parameter<Guid>("AdvanceKey");
        financialApplyAdvance.Parameter<double>("Amount");
        financialApplyAdvance.Parameter<string>("SettlementDate");
        financialApplyAdvance.Returns<IActionResult>();

        var financialAdvanceCandidates = modelBuilder.Function("FinancialDocumentsGetAdvanceCandidates");
        financialAdvanceCandidates.Parameter<Guid>("Key");
        financialAdvanceCandidates.ReturnsCollection<FinancialDocumentAdvanceCandidateDto>();
```

(`AppliedAmount` é persistida: entra no EDM sozinha — **não** acrescentar ao laço de `AddProperty`.)

- [ ] **Step 4: Controllers**

`SiagroB1.Web/Actions/Financials/FinancialDocumentsApplyAdvanceController.cs`:

```csharp
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

/// <summary>
/// "Compensar adiantamento" no provisório (spec 2026-10-14, regras 1–4). Key = provisório; AdvanceKey = adiantamento;
/// Amount é Edm.Double; SettlementDate, string ISO yyyy-MM-dd. Recusas de negócio voltam como 400 com o texto.
/// </summary>
public class FinancialDocumentsApplyAdvanceController(FinancialDocumentsApplyAdvanceService service) : ODataController
{
    [HttpPost("odata/FinancialDocumentsApplyAdvance")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        // ODataActionParameters chega NULL quando o corpo não casa com nenhum parâmetro do EDM.
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Informe o título provisório.");

            if (!parameters.TryGetValue("AdvanceKey", out var advanceObj) || advanceObj is null)
                return BadRequest("Informe o adiantamento.");

            if (!parameters.TryGetValue("Amount", out var amountObj) || amountObj is null)
                return BadRequest("Informe o valor a compensar.");

            if (!parameters.TryGetValue("SettlementDate", out var dateObj) || string.IsNullOrWhiteSpace(dateObj?.ToString()))
                return BadRequest("Informe a data da compensação.");

            await service.ExecuteAsync(
                Guid.Parse(keyObj.ToString()!),
                Guid.Parse(advanceObj.ToString()!),
                Convert.ToDecimal(amountObj, CultureInfo.InvariantCulture),
                DateTime.Parse(dateObj!.ToString()!, CultureInfo.InvariantCulture),
                User.Identity?.Name ?? "Unknown");

            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
```

`SiagroB1.Web/Functions/Financials/FinancialDocumentsGetAdvanceCandidatesController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.Financials;

/// <summary>Adiantamentos que podem ser compensados no provisório (spec 2026-10-14), DTO PascalCase.</summary>
public class FinancialDocumentsGetAdvanceCandidatesController(
    FinancialDocumentsGetAdvanceCandidatesService service) : ODataController
{
    [EnableQuery]
    [HttpGet("odata/FinancialDocumentsGetAdvanceCandidates(Key={key})")]
    public async Task<ActionResult<IEnumerable<FinancialDocumentAdvanceCandidateDto>>> Get([FromRoute] Guid key)
    {
        try
        {
            return Ok(await service.ExecuteAsync(key));
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
```

`git add` os dois arquivos.

- [ ] **Step 5: Rodar e gates**

```bash
dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialAdvanceApplicationEdmModelTests|FullyQualifiedName~FinancialEdmModelTests|FullyQualifiedName~FinancialFirmPayablesEdmModelTests"
dotnet build SiagroB1.sln
dotnet test SiagroB1.Application.Tests
dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```
Expected: EDM PASS; build OK; suíte inteira verde (anotar a contagem); "No changes have been made to the model since the last migration."

- [ ] **Step 6: Stage**

```bash
git add SiagroB1.Web/Actions/Financials/FinancialDocumentsApplyAdvanceController.cs SiagroB1.Web/Functions/Financials/FinancialDocumentsGetAdvanceCandidatesController.cs SiagroB1.Web/ODataConfig/ODataConfigurations.cs SiagroB1.Application.Tests/Financials/FinancialAdvanceApplicationEdmModelTests.cs
```
O controlador commita com:

```
feat(financial): endpoints da compensação de adiantamento

Action para compensar o adiantamento no provisório e function que lista os adiantamentos
candidatos com o valor sugerido. Chaves Guid, valor em Edm.Double, data em string e recusas
de negócio como 400 com a mensagem.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 9: Frontend — "Compensar adiantamento" no Detail do provisório

**Repo:** `C:\Projetos\SiagroB1\siagro-b1-frontend` (todos os caminhos abaixo relativos a ele).

**Files:**
- Modify: `webapp/model/ServerRoutes.ts` (bloco "Títulos firmes de compra")
- Create: `webapp/types/FinancialAdvanceCandidate.ts`
- Create: `webapp/view/financialDocuments/fragments/ApplyAdvanceDialog.fragment.xml`
- Modify: `webapp/view/financialDocuments/fragments/Settlements.fragment.xml` (botão na barra)
- Modify: `webapp/controller/financialDocuments/Detail.controller.ts`

**Interfaces:**
- Consumes: Task 8 (`POST /odata/FinancialDocumentsApplyAdvance`, `GET /odata/FinancialDocumentsGetAdvanceCandidates(Key=…)`); no controller: `runDocumentAction(path, payload, success)`, `_compositionKey`, `documentContext()`, `sendJson`, `localIsoDate`, `DialogHelper.createDialog`.
- Produces: `viewModel>/applyAdvanceDialog = { candidates: FinancialAdvanceCandidate[], advanceKey: string, amount: number, settlementDate: string, availableToSettle: number }`; rotas `financialDocumentsApplyAdvance`, `financialDocumentsGetAdvanceCandidates`.

- [ ] **Step 1: Branch**

```bash
git -C /c/Projetos/SiagroB1/siagro-b1-frontend status --short
git -C /c/Projetos/SiagroB1/siagro-b1-frontend log --oneline -1 main
git -C /c/Projetos/SiagroB1/siagro-b1-frontend checkout -b feature/advance-application main
git -C /c/Projetos/SiagroB1/siagro-b1-frontend branch --show-current
```
Expected: working tree limpo; `main` em `03cb330`; branch atual `feature/advance-application`. Árvore suja: parar e perguntar.

- [ ] **Step 2: Rotas e tipo**

`webapp/model/ServerRoutes.ts`, depois de `financialDocumentsGetByContract: '/odata/FinancialDocumentsGetByContract',`:

```ts
  // Compensação de adiantamento no provisório (spec 2026-10-14): chamados por sendJson (recusa nas três formas).
  financialDocumentsApplyAdvance: '/odata/FinancialDocumentsApplyAdvance',
  financialDocumentsGetAdvanceCandidates: '/odata/FinancialDocumentsGetAdvanceCandidates',
```

`webapp/types/FinancialAdvanceCandidate.ts`:

```ts
/**
 * Linha da function FinancialDocumentsGetAdvanceCandidates (spec 2026-10-14). PascalCase; valores Edm.Decimal podem
 * chegar como string — sempre Number() antes de comparar ou sugerir.
 */
export type FinancialAdvanceCandidate = {
  DocumentKey: string;
  Code: string;
  DocumentDate: string;
  SettledAmount: number | string;
  AppliedAmount: number | string;
  AvailableAdvanceAmount: number | string;
  SuggestedAmount: number | string;
};
```

`git add webapp/types/FinancialAdvanceCandidate.ts`.

- [ ] **Step 3: Diálogo**

`webapp/view/financialDocuments/fragments/ApplyAdvanceDialog.fragment.xml`:

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:f="sap.ui.layout.form"
    xmlns:core="sap.ui.core"
    core:require="{
      formatter: 'siagrob1/model/formatter'
    }"
>
  <!--
    Compensar adiantamento no provisório (spec 2026-10-14, regras 1 a 4): adiantamentos do mesmo contrato e moeda com
    crédito, pela function FinancialDocumentsGetAdvanceCandidates. Itens e chave chegam no mesmo setProperty, antes de
    abrir: o Select precisa dos itens antes do selectedKey. Trocar o adiantamento troca o valor sugerido.
  -->
  <Dialog title="Compensar adiantamento">
    <content>
      <VBox class="sapUiSmallMargin" width="520px">
        <f:Form editable="true">
          <f:layout>
            <f:ColumnLayout columnsM="1" columnsL="1" columnsXL="1"/>
          </f:layout>
          <f:formContainers>
            <f:FormContainer>
              <f:formElements>
                <f:FormElement label="Disponível para baixa">
                  <f:fields>
                    <Text text="{path: 'viewModel>/applyAdvanceDialog/availableToSettle', formatter: 'formatter.formatDecimal2'}"/>
                  </f:fields>
                </f:FormElement>
                <f:FormElement label="Adiantamento">
                  <f:fields>
                    <Select
                      id="financialApplyAdvanceSelect"
                      width="100%"
                      forceSelection="false"
                      selectedKey="{viewModel>/applyAdvanceDialog/advanceKey}"
                      items="{viewModel>/applyAdvanceDialog/candidates}"
                      change=".onApplyAdvanceCandidateChange">
                      <core:Item
                        key="{viewModel>DocumentKey}"
                        text="{viewModel>Code} · crédito {path: 'viewModel>AvailableAdvanceAmount', formatter: 'formatter.formatDecimal2'}"/>
                    </Select>
                  </f:fields>
                </f:FormElement>
                <f:FormElement label="Valor">
                  <f:fields>
                    <Input value="{path: 'viewModel>/applyAdvanceDialog/amount', type: 'sap.ui.model.odata.type.Double'}" required="true"/>
                  </f:fields>
                </f:FormElement>
                <f:FormElement label="Data">
                  <f:fields>
                    <DatePicker
                      value="{viewModel>/applyAdvanceDialog/settlementDate}"
                      required="true"
                      displayFormat="dd/MM/yyyy"
                      valueFormat="yyyy-MM-dd"/>
                  </f:fields>
                </f:FormElement>
              </f:formElements>
            </f:FormContainer>
          </f:formContainers>
        </f:Form>
      </VBox>
    </content>
    <footer>
      <OverflowToolbar>
        <ToolbarSpacer />
        <Button text="Compensar" type="Emphasized" press=".onConfirmApplyAdvance"/>
        <Button text="Fechar" press=".onCloseApplyAdvanceDialog"/>
      </OverflowToolbar>
    </footer>
  </Dialog>
</core:FragmentDefinition>
```

`git add` o arquivo.

- [ ] **Step 4: Botão na barra de Baixas**

`webapp/view/financialDocuments/fragments/Settlements.fragment.xml`, logo depois do botão "Compensar com firme" (antes do comentário do "Estornar baixa"):

```xml
        <!-- Provisório com disponível para baixa recebe o crédito do adiantamento do contrato (spec 2026-10-14) -->
        <Button type="Transparent" text="Compensar adiantamento" press=".onOpenApplyAdvanceDialog"
                visible="{= ${path: 'Nature', targetType: 'any'} === 'Provisional'
                          &amp;&amp; ${path: 'Status', targetType: 'any'} !== 'Canceled'
                          &amp;&amp; ${path: 'AvailableToSettleAmount', targetType: 'any'} > 0 }"/>
```

("Estornar baixa" já aparece para `AdvanceApplication` e `AdvanceTransfer`: só `InvoiceOffset`, `Reversal` e `AdvanceRefund` o escondem — não mexer.)

- [ ] **Step 5: Controller**

`webapp/controller/financialDocuments/Detail.controller.ts`:

Trocar o import

```ts
import { odataValue, sendJson } from "siagrob1/helpers/FetchHelpers";
```

por

```ts
import { odataCollection, odataValue, sendJson } from "siagrob1/helpers/FetchHelpers";
import { FinancialAdvanceCandidate } from "siagrob1/types/FinancialAdvanceCandidate";
import { Select$ChangeEvent } from "sap/m/Select";
```

E acrescentar, logo depois do método `onConfirmNetting()`:

```ts
  private _applyAdvanceDialog: Dialog;

  /**
   * "Compensar adiantamento" (spec 2026-10-14): carrega os candidatos ANTES de abrir (o Select precisa dos itens antes
   * do selectedKey) e sugere min(disponível do provisório, crédito do adiantamento), calculado no backend.
   */
  async onOpenApplyAdvanceDialog(): Promise<void> {
    const ctx = this.documentContext();
    if (!ctx) return;

    const key = this._compositionKey;
    const result = await sendJson("GET", `${this.api.financialDocumentsGetAdvanceCandidates}(Key=${key})`);
    // Resposta atrasada de outro título (troca rápida de rota) não abre diálogo na tela errada.
    if (key !== this._compositionKey) return;

    if (!result.ok) {
      MessageBox.error(result.message);
      return;
    }

    const candidates = odataCollection<FinancialAdvanceCandidate>(result.data);
    if (!candidates.length) {
      MessageBox.information("Nenhum adiantamento com crédito disponível neste contrato.");
      return;
    }

    (this.getModel("viewModel") as JSONModel).setProperty("/applyAdvanceDialog", {
      candidates,
      advanceKey: candidates[0].DocumentKey,
      amount: Number(candidates[0].SuggestedAmount),
      settlementDate: localIsoDate(new Date()),
      availableToSettle: Number(ctx.getProperty("AvailableToSettleAmount") ?? 0),
    });

    this._applyAdvanceDialog ??= await DialogHelper.createDialog(
      this, "siagrob1.view.financialDocuments.fragments.ApplyAdvanceDialog");
    this._applyAdvanceDialog.open();
  }

  /** Trocar o adiantamento troca o valor sugerido (lido do item escolhido, não do binding ainda por atualizar). */
  onApplyAdvanceCandidateChange(ev: Select$ChangeEvent): void {
    const advanceKey = ev.getParameter("selectedItem")?.getKey() ?? "";
    const viewModel = this.getModel("viewModel") as JSONModel;
    const candidates = (viewModel.getProperty("/applyAdvanceDialog/candidates") as FinancialAdvanceCandidate[]) ?? [];
    const chosen = candidates.find((c) => c.DocumentKey === advanceKey);

    viewModel.setProperty("/applyAdvanceDialog/advanceKey", advanceKey);
    viewModel.setProperty("/applyAdvanceDialog/amount", chosen ? Number(chosen.SuggestedAmount) : 0);
  }

  onCloseApplyAdvanceDialog(): void {
    this._applyAdvanceDialog?.close();
  }

  async onConfirmApplyAdvance(): Promise<void> {
    const form = (this.getModel("viewModel") as JSONModel).getProperty("/applyAdvanceDialog") as {
      advanceKey: string; amount: number; settlementDate: string;
    };

    if (!form.advanceKey) {
      MessageBox.alert("Selecione o adiantamento.");
      return;
    }

    if (!(Number(form.amount) > 0)) {
      MessageBox.alert("Informe um valor maior que zero.");
      return;
    }

    if (!form.settlementDate) {
      MessageBox.alert("Informe a data da compensação.");
      return;
    }

    this.onCloseApplyAdvanceDialog();
    // Tetos (disponível e crédito) quem decide é o backend: a recusa A5/A6 aparece pelo MessageBox do runDocumentAction.
    await this.runDocumentAction(this.api.financialDocumentsApplyAdvance, {
      Key: this._compositionKey,
      AdvanceKey: form.advanceKey,
      Amount: Number(form.amount),
      SettlementDate: form.settlementDate,
    }, "Adiantamento compensado.");
  }
```

- [ ] **Step 6: Gates**

Dev server parado. Na raiz do frontend: `yarn ts-typecheck && yarn lint && yarn ui5lint`
Expected: sem erro (ui5lint sem regra nova).

- [ ] **Step 7: Stage**

```bash
git add webapp/model/ServerRoutes.ts webapp/types/FinancialAdvanceCandidate.ts webapp/view/financialDocuments/fragments/ApplyAdvanceDialog.fragment.xml webapp/view/financialDocuments/fragments/Settlements.fragment.xml webapp/controller/financialDocuments/Detail.controller.ts
```
O controlador commita com:

```
feat(financial): compensar adiantamento no detalhe do provisório

O provisório com disponível para baixa ganha "Compensar adiantamento" na barra de Baixas: o
diálogo lista os adiantamentos do contrato com crédito, sugere o menor entre o disponível e
o crédito e grava a compensação; a recusa do backend aparece na tela.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 10: Frontend — adiantamento (aplicado, crédito, devolver, revincular), origem e log, listas e Financeiro do contrato

**Repo:** `C:\Projetos\SiagroB1\siagro-b1-frontend`, branch `feature/advance-application` (`git branch --show-current` antes de começar).

**Files:**
- Modify: `webapp/model/formatter.ts` (`formatFinancialChangeLogField`, `formatFinancialSettlementOrigin`)
- Modify: `webapp/test/unit/model/formatter.qunit.ts`
- Modify: `webapp/view/financialDocuments/fragments/Form.fragment.xml`
- Modify: `webapp/view/financialDocuments/Detail.view.xml` (botão "Devolver adiantamento")
- Modify: `webapp/view/financialDocuments/fragments/RefundAdvanceDialog.fragment.xml`, `RelinkContractDialog.fragment.xml`
- Modify: `webapp/controller/financialDocuments/Detail.controller.ts` (`onRefundAdvance`, `onConfirmRefund`, `onRelinkContract`, `onConfirmRelink`)
- Modify: `webapp/view/financialAdvances/Main.view.xml`
- Modify: `webapp/view/purchaseContracts/fragments/PurchaseContractFinancialDocuments.fragment.xml`, `webapp/types/FinancialDocumentByContract.ts`

**Interfaces:**
- Consumes: Tasks 1, 4, 5, 7 (campos `AppliedAmount`, `AvailableAdvanceAmount` na entidade e no DTO por contrato; origem `AdvanceTransfer`; campos de log `AdvanceApplication`/`AdvanceTransfer`).
- Produces: `viewModel>/refundDialog.partial: boolean`; `viewModel>/relinkDialog.split: boolean`, `viewModel>/relinkDialog.splitText: string`.

- [ ] **Step 1: Teste do formatter que falha**

`webapp/test/unit/model/formatter.qunit.ts`, no fim do arquivo:

```ts
QUnit.module("formatter - compensação de adiantamento (spec 2026-10-14)");

QUnit.test("origem Transferência de adiantamento e campos do log", function (assert) {
	assert.strictEqual(formatter.formatFinancialSettlementOrigin("AdvanceTransfer"), "Transferência de adiantamento");
	assert.strictEqual(formatter.formatFinancialSettlementOrigin("AdvanceApplication"), "Amortização de adiantamento");
	assert.strictEqual(formatter.formatFinancialChangeLogField("AdvanceApplication"), "Compensação de adiantamento");
	assert.strictEqual(formatter.formatFinancialChangeLogField("AdvanceTransfer"), "Transferência de adiantamento");
});
```

Rodar a QUnit (URL das Global Constraints, `yarn start`): o teste novo FALHA (devolve o próprio código). Parar o dev server **por PID** depois (porta 8080).

- [ ] **Step 2: Formatter**

`webapp/model/formatter.ts`, em `formatFinancialChangeLogField`, depois de `m.set("Composition", "Composição");`:

```ts
    m.set("AdvanceApplication", "Compensação de adiantamento");
    m.set("AdvanceTransfer", "Transferência de adiantamento");
```

Em `formatFinancialSettlementOrigin`, depois de `m.set("Dation", "Dação");`:

```ts
    m.set("AdvanceTransfer", "Transferência de adiantamento");
```

- [ ] **Step 3: Formulário do título — Aplicado e Crédito disponível**

`webapp/view/financialDocuments/fragments/Form.fragment.xml`, no container "Valores", logo depois do `FormElement` "Em aberto":

```xml
          <f:FormElement label="Aplicado" visible="{= ${path: 'Nature', targetType: 'any'} === 'Advance' }">
            <f:fields>
              <Text text="{
                path: 'AppliedAmount',
                type: 'sap.ui.model.odata.type.Decimal',
                constraints: { precision: 18, scale: 2 },
                formatOptions: { decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' }
              }"/>
            </f:fields>
          </f:FormElement>
          <f:FormElement label="Crédito disponível" visible="{= ${path: 'Nature', targetType: 'any'} === 'Advance' }">
            <f:fields>
              <ObjectNumber
                number="{
                  path: 'AvailableAdvanceAmount',
                  type: 'sap.ui.model.odata.type.Decimal',
                  constraints: { precision: 18, scale: 2 },
                  formatOptions: { decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' }
                }"
                state="Success"/>
            </f:fields>
          </f:FormElement>
```

- [ ] **Step 4: Botão "Devolver adiantamento" pelo crédito**

`webapp/view/financialDocuments/Detail.view.xml`, no botão "Devolver adiantamento", trocar

```xml
                    &amp;&amp; ${path: 'SettledAmount', targetType: 'any'} > 0 }"
```

por

```xml
                    &amp;&amp; ${path: 'AvailableAdvanceAmount', targetType: 'any'} > 0 }"
```

- [ ] **Step 5: Textos dos diálogos de devolver e revincular**

`RefundAdvanceDialog.fragment.xml`, trocar o `MessageStrip` inteiro por:

```xml
      <MessageStrip
        text="Nada deste adiantamento foi compensado: a devolução é pelo crédito inteiro e o título fica cancelado."
        type="Warning"
        showIcon="true"
        visible="{= !${viewModel>/refundDialog/partial} }"
        class="sapUiSmallMarginBottom"/>
      <MessageStrip
        text="A devolução é só do crédito disponível. O que já foi compensado fica no título, que continua ativo pelo valor aplicado."
        type="Information"
        showIcon="true"
        visible="{= !!${viewModel>/refundDialog/partial} }"
        class="sapUiSmallMarginBottom"/>
```

`RelinkContractDialog.fragment.xml`, trocar o `MessageStrip` inteiro por:

```xml
      <MessageStrip
        text="O adiantamento migra INTEIRO. O contrato de destino precisa ser do mesmo parceiro, da mesma filial, na mesma moeda e estar aprovado."
        type="Information"
        showIcon="true"
        visible="{= !${viewModel>/relinkDialog/split} }"
        class="sapUiSmallMarginBottom"/>
      <MessageStrip
        text="{viewModel>/relinkDialog/splitText}"
        type="Warning"
        showIcon="true"
        visible="{= !!${viewModel>/relinkDialog/split} }"
        class="sapUiSmallMarginBottom"/>
```

- [ ] **Step 6: Controller — devolver e revincular**

`webapp/controller/financialDocuments/Detail.controller.ts`, em `onRefundAdvance()`, trocar o trecho de

```ts
    const settled = Number(ctx.getProperty("SettledAmount") ?? 0);

    if (!(settled > 0)) {
      MessageBox.warning("O adiantamento não tem valor pago: não há o que devolver.");
      return;
    }
```

até `amountText: formatter.formatDecimal(settled, 2),` (inclusive) por:

```ts
    // Spec 2026-10-14 (regra 7): devolve só o crédito disponível (pago − aplicado).
    const credit = Number(ctx.getProperty("AvailableAdvanceAmount") ?? 0);

    if (!(credit > 0)) {
      MessageBox.warning("O adiantamento não tem crédito disponível para devolver.");
      return;
    }

    (this.getModel("viewModel") as JSONModel).setProperty("/refundDialog", {
      financialAccountCode: await this.lastSettlementAccount(),
      refundDate: localIsoDate(new Date()),
      amountText: formatter.formatDecimal(credit, 2),
      partial: Number(ctx.getProperty("AppliedAmount") ?? 0) > 0,
```

(as linhas seguintes — `documentReference: "",`, `reason: "",` e o fechamento `});` — continuam as mesmas.)

Em `onConfirmRefund()`, trocar

```ts
      MessageToast.show("Devolução registrada. O título foi cancelado.");
```

por

```ts
      const partial = (this.getModel("viewModel") as JSONModel).getProperty("/refundDialog/partial") as boolean;
      MessageToast.show(partial
        ? "Devolução registrada. O título continua ativo pelo valor aplicado."
        : "Devolução registrada. O título foi cancelado.");
```

Em `onRelinkContract()`, trocar

```ts
    (this.getModel("viewModel") as JSONModel).setProperty("/relinkDialog", {
      currentContractCode: (ctx.getProperty("OriginDocNumber") as string) || "(sem contrato)",
      contractCode: "",
      contractKey: null,
    });
```

por

```ts
    // Spec 2026-10-14 (regra 8): com algo aplicado o revínculo DESMEMBRA — só o crédito vai para o destino.
    const applied = Number(ctx.getProperty("AppliedAmount") ?? 0);
    const credit = Number(ctx.getProperty("AvailableAdvanceAmount") ?? 0);
    const split = applied > 0;

    if (split && !(credit > 0)) {
      MessageBox.warning("O adiantamento não tem crédito disponível para vincular a outro contrato.");
      return;
    }

    (this.getModel("viewModel") as JSONModel).setProperty("/relinkDialog", {
      currentContractCode: (ctx.getProperty("OriginDocNumber") as string) || "(sem contrato)",
      contractCode: "",
      contractKey: null,
      split,
      creditText: formatter.formatDecimal(credit, 2),
      splitText: split
        ? `Este adiantamento tem ${formatter.formatDecimal(applied, 2)} aplicado: só o crédito disponível ` +
          `(${formatter.formatDecimal(credit, 2)}) vai para o contrato de destino, num adiantamento novo. ` +
          "O original fica neste contrato com o valor aplicado."
        : "",
    });
```

Em `onConfirmRelink()`, trocar

```ts
    const form = (this.getModel("viewModel") as JSONModel)
      .getProperty("/relinkDialog") as { contractCode: string; contractKey: string };
```

por

```ts
    const form = (this.getModel("viewModel") as JSONModel)
      .getProperty("/relinkDialog") as { contractCode: string; contractKey: string; split: boolean; creditText: string };
```

e

```ts
      MessageToast.show(`Adiantamento vinculado ao contrato ${form.contractCode}.`);
```

por

```ts
      MessageToast.show(form.split
        ? `Crédito de ${form.creditText} transferido para um adiantamento novo no contrato ${form.contractCode}.`
        : `Adiantamento vinculado ao contrato ${form.contractCode}.`);
```

- [ ] **Step 7: Lista de adiantamentos — coluna Aplicado**

`webapp/view/financialAdvances/Main.view.xml`, logo antes da coluna "Crédito disponível":

```xml
					<t:Column label="Aplicado" width="11rem" hAlign="End">
						<t:template>
							<Text text="{
								path: 'AppliedAmount',
								type: 'sap.ui.model.odata.type.Decimal',
								constraints: { precision: 18, scale: 2 },
								formatOptions: { decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' }
							}" wrapping="false"/>
						</t:template>
					</t:Column>
```

- [ ] **Step 8: Seção Financeiro do contrato**

`webapp/types/FinancialDocumentByContract.ts`, depois de `ComposedAmount: number | string;`:

```ts
  AppliedAmount: number | string;
  AvailableAdvanceAmount: number | string;
```

`webapp/view/purchaseContracts/fragments/PurchaseContractFinancialDocuments.fragment.xml`: depois de `<Column hAlign="End"><Text text="Em aberto" /></Column>`:

```xml
      <Column hAlign="End"><Text text="Aplicado" /></Column>
      <Column hAlign="End"><Text text="Crédito disponível" /></Column>
```

e depois da célula `<Text text="{path: 'contractFinancial>OpenAmount', formatter: '.formatter.formatDecimal2'}" />`:

```xml
          <Text text="{path: 'contractFinancial>AppliedAmount', formatter: '.formatter.formatDecimal2'}"
                visible="{= ${contractFinancial>Nature} === 'Advance' }" />
          <Text text="{path: 'contractFinancial>AvailableAdvanceAmount', formatter: '.formatter.formatDecimal2'}"
                visible="{= ${contractFinancial>Nature} === 'Advance' }" />
```

(Este fragmento usa `.formatter` do controller, sem `core:require` — não acrescentar `core:require` aqui.) No comentário do topo do fragmento, acrescentar a frase: `Aplicado e crédito disponível só aparecem no adiantamento (spec 2026-10-14).` — sem `--`.

- [ ] **Step 9: Gates e QUnit**

Dev server parado: `yarn ts-typecheck && yarn lint && yarn ui5lint` → sem erro. Depois `yarn start` e a QUnit unit (URL das Global Constraints): módulo "formatter - compensação de adiantamento (spec 2026-10-14)" verde e nenhum outro módulo regredido. Parar o dev server **por PID** (porta 8080).

- [ ] **Step 10: Stage**

```bash
git add webapp/model/formatter.ts webapp/test/unit/model/formatter.qunit.ts webapp/view/financialDocuments webapp/controller/financialDocuments/Detail.controller.ts webapp/view/financialAdvances/Main.view.xml webapp/view/purchaseContracts/fragments/PurchaseContractFinancialDocuments.fragment.xml webapp/types/FinancialDocumentByContract.ts
```
O controlador commita com:

```
feat(financial): aplicado e crédito do adiantamento nas telas

O adiantamento mostra o aplicado e o crédito disponível no detalhe, na lista e no financeiro
do contrato. Devolver passa a mostrar o crédito como valor e avisa quando o título continua
ativo; vincular a outro contrato avisa que vai desmembrar quando há aplicação. A baixa de
transferência e os campos novos do log ganham rótulo.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 11: Verificação ponta a ponta (CEAGUI)

**Files:** nenhum código novo — achado vira correção na task dona, com commit próprio (`fix(financial): …`).

- [ ] **Step 1: Gates completos**

Backend (raiz): `dotnet build SiagroB1.sln`, `dotnet test SiagroB1.Application.Tests` (anotar a contagem), `dotnet ef migrations has-pending-model-changes` dos dois contextos ("No changes…"). Frontend (raiz, dev server parado): `yarn ts-typecheck && yarn lint` e `yarn ui5lint` sem regra nova; QUnit unit com o módulo novo verde. `git status --short` limpo nos dois repos depois dos commits do controlador; `git log --oneline main..` nos dois mostra só os commits deste plano.

- [ ] **Step 2: Aplicar a migration só no CEAGUI dev**

Ler antes as memórias `run-stack-locally` e `db-migration-profile-targets-production`. No PowerShell, na raiz do backend:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Ceagui-Development"
(Get-Content SiagroB1.Web\appsettings.Ceagui-Development.json -Raw | ConvertFrom-Json).ConnectionStrings.SiagroDB -split ';' | Where-Object { $_ -match '^(Server|Database)=' }
```
Expected: `Database=CEAGUI_SIAGRO_DEV` em servidor local. Qualquer outro banco: **parar**.

Conferir que não há linha a migrar (sem backfill):

```powershell
sqlcmd -S localhost -E -I -d CEAGUI_SIAGRO_DEV -Q "SELECT COUNT(*) FROM FINANCIAL_SETTLEMENTS WHERE Origin IN (3, 7)"
```
Expected: `0` (senão parar e levar ao usuário). Então:

```powershell
dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```
Conferir na saída o banco-alvo e a migration `AddFinancialDocumentAppliedAmount`. Nunca produção, nunca o profile `db-migration`.

- [ ] **Step 3: Subir a stack (profile `ceagui`)**

Gateway primeiro (`dotnet run --project SiagroB1.Gateway --launch-profile ceagui`, esperar "Loading proxy data from config"), depois Web (`dotnet run --project SiagroB1.Web --launch-profile ceagui`); frontend `yarn start:dev`. Login `admin`/`1234`. Navegar pelo **menu** (não por `goto` de hash).

- [ ] **Step 4: Roteiro (spec, "Verificação no navegador")**

1. Contrato de compra **aprovado** com provisório (fixação confirmada) e um adiantamento: Adiantamentos → Incluir (contrato de compra, valor maior que o firme que será composto) → abrir o adiantamento → Baixar (conta, valor inteiro). Detail do adiantamento: "Aplicado" 0,00 e "Crédito disponível" = valor pago.
2. Compor um firme no provisório (Conciliação Fiscal de Compras → vincular NF × romaneio do contrato; provisório → Composição → Compor). "Disponível para baixa" = valor do firme.
3. Provisório → Baixas → "Compensar adiantamento": o diálogo lista o adiantamento com o crédito, "Disponível para baixa" correto, valor sugerido = menor dos dois, data = hoje (local). Trocar o valor para **acima** do disponível → recusa A5 **visível na tela**. Compensar uma parte → tabela de Baixas com "Amortização de adiantamento"; provisório: Baixado e Disponível atualizados. Adiantamento: Pago inalterado, "Aplicado" = parte, "Crédito disponível" = pago − parte; Baixas do adiantamento com a linha par; Log de Alterações com "Compensação de adiantamento" nos dois títulos.
4. No adiantamento, selecionar a baixa Manual (pagamento) → "Estornar baixa" → recusa A7 na tela. Selecionar a "Amortização de adiantamento" → "Estornar baixa" → os dois títulos voltam (aplicado 0, provisório sem baixa); log com o desfazer.
5. Compensar de novo até quitar o provisório (Status "Quitado"). Seção Financeiro do contrato: provisório quitado; adiantamento com "Aplicado" e "Crédito disponível".
6. Contrato → Encerrar com crédito sobrando → recusa A9 na tela, com o código do adiantamento e o valor.
7. Adiantamento → "Vincular a outro contrato": o diálogo avisa que vai desmembrar (texto com aplicado e crédito); escolher outro contrato aprovado do mesmo produtor → toast "Crédito de … transferido…". Adiantamentos: o novo aparece no outro contrato (Valor = crédito, Situação Quitado, Origem "Documento financeiro", Comentários "Crédito não aplicado do adiantamento …"); o original com "Crédito disponível" 0,00 e a baixa "Transferência de adiantamento". (Opcional: "Estornar baixa" na transferência → o novo fica Cancelado com "Transferência de crédito estornada"; revincular de novo para seguir.)
8. Encerrar o contrato → passa.
9. "Devolver adiantamento" num adiantamento com aplicação e crédito (outro cenário curto, ou o novo do passo 7 depois de compensar parte): o diálogo mostra o crédito e o aviso de que o título continua ativo; depois de devolver, o título não fica Cancelado.
10. Derrubar a stack **por PID** nas portas 50000, 5246 e 8080 (`Get-NetTCPConnection -LocalPort <p> -State Listen` → `Stop-Process -Id <pid> -Force`) e conferir as portas de novo no fim (o file watcher ressuscita processo enquanto houver edição).

- [ ] **Step 5: Fechamento**

Anotar contagem de testes, achados e correções. Atualizar a memória do projeto (entrada nova para o sub-projeto 3: migration `AddFinancialDocumentAppliedAmount` aplicada só no CEAGUI dev; o desmembramento cria título novo pelo `DocNumberSequenceService`). Merge em `main` é decisão do usuário; **nunca push**.

---

## Cobertura do spec

| Item do spec | Onde |
|---|---|
| Enum `AdvanceTransfer = 7` (no fim; `AdvanceApplication = 3` já existia) | Task 1 |
| Coluna `AppliedAmount DECIMAL(18,2) DEFAULT 0`, só no adiantamento, escritor único = recalculador, no EDM sem `AddProperty` | Tasks 1 e 8 (teste de EDM) |
| `AvailableAdvanceAmount` = `Max(0, Round(Settled − Applied))` só no adiantamento | Task 1 |
| Sem backfill; migration `AddFinancialDocumentAppliedAmount` (só a coluna) | Task 1 (aplicada só no CEAGUI dev na Task 11) |
| Recalculador: dois eixos do adiantamento, estornos pelo eixo da linha estornada, linhas pendentes; status por `SettledAmount` × `NetAmount` | Task 1 |
| Recalculador: `AdvanceApplication` no provisório entra no baixado; `ComposedAmount` inalterado | Task 1 |
| Critério entrada × saída da `AdvanceTransfer` (OriginType `FinancialDocument` + OriginKey = RelatedDocumentKey) | Task 1 (`IsIncomingTransfer`, usado na Task 3) |
| Regra 1 (action no provisório) | Tasks 2 e 8 |
| Regra 2 (pré-condições na ordem; mesmo contrato e moeda; tetos do provisório e do crédito) | Task 2 |
| Regra 3 (par sem conta, `RelatedDocumentKey`/`DocumentReference` cruzados, recálculo e log nos dois, `RowVersion`) | Task 2 |
| Regra 4 (valor sugerido = min) | Task 7 (backend) e Task 9 (tela) |
| Regra 5 (estorno do par de `AdvanceApplication`; recusa com título cancelado) | Task 3 |
| Regra 6 (A7: estorno de pagamento não deixa pago < aplicado) | Task 3 (+ desmembrado na Task 5) |
| Regra 7 (devolução só do crédito; sem aplicação cancela; com aplicação não cancela; A8) | Task 4 |
| Regra 8 (revínculo: simples move; aplicado desmembra com adiantamento novo, transferência em par, número antes do SaveChanges, validações de destino) | Task 5 |
| Regra 9 (estorno da transferência estorna o par e cancela o novo; A10) | Task 3 (código) e Task 5 (testes) |
| Regra 10 (encerrar recusa crédito > 0; A9) | Task 6 |
| Regra 11 (cancelar contrato / retirar aprovação inalterado) | sem mudança — `FinancialDocumentsContractCancellationGuardServiceTests` roda nas Tasks 4–5 |
| Regra 12 (`AdvanceTransfer` só pelo revínculo, sem action) | Task 5 (único escritor) e Task 8 (nenhuma action de transferência no EDM) |
| Mensagens A1–A11 (texto exato) | A1–A6, A11: Task 2 · A7: Tasks 3 e 5 · A8: Task 4 · A9: Task 6 · A10: Task 5 |
| Valores `N2` pt-BR e moeda por `CurrencySymbol` | Global Constraints; Tasks 2, 3, 6 |
| Serviços e API: `FinancialDocumentsApplyAdvanceService` (`CommitMode`), action `FinancialDocumentsApplyAdvance(Key, AdvanceKey, Amount: Edm.Double, SettlementDate: string)`, function `FinancialDocumentsGetAdvanceCandidates(Key)` PascalCase com os 7 campos, recusas `BadRequest(string)` | Tasks 2, 7, 8 |
| `FinancialDocumentsGetByContractService` com `AppliedAmount` e `AvailableAdvanceAmount` | Task 7 |
| Frontend — botão "Compensar adiantamento" (provisório, não cancelado, disponível > 0), diálogo com candidatos, valor sugerido, data local, recusa exibida | Task 9 |
| Frontend — Detail do adiantamento: "Aplicado" e "Crédito disponível"; devolver mostra o crédito; revincular avisa o desmembramento | Task 10 |
| Frontend — Baixas: origem `AdvanceTransfer` → "Transferência de adiantamento"; "Estornar baixa" nas duas | Task 10 (formatter); botão já habilitado (Task 9, nota) |
| Frontend — lista de adiantamentos: coluna "Aplicado" | Task 10 |
| Frontend — seção Financeiro do contrato: aplicado e crédito | Tasks 7 e 10 |
| Frontend — armadilhas (`--` em XML, `core:require`/`.formatter`, `targetType:'any'`, `Number()`, data local) | Global Constraints; Tasks 9–10 |
| Testes listados no spec (compensar, recalculador, estorno do par, A7, devolução, revínculo, estorno da transferência e A10, encerramento A9 e liberado) | Tasks 1–8 |
| Verificação no navegador (CEAGUI dev), stack derrubada por PID | Task 11 |
| Fora do escopo (venda, automático, entre contratos) | não implementado — candidatos e A2 restringem ao mesmo contrato de compra |
