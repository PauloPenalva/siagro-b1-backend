# Documento de Saída: confirmar e depois transmitir a NF-e — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Na filial que emite NF-e pelo Siagro, o documento de saída Normal é confirmado primeiro (no faturamento
da carga ou pelo Confirmar) e a NF-e é transmitida depois por "Transmitir NF-e"; o documento ganha o
"Tipo de documento" (NF-e / Outro).

**Architecture:** A emissão comum (`NfeIssueServiceBase<T>`) deixa de fixar "só Pendente" e pergunta ao documento
qual situação exige; o documento de saída Normal exige Confirmado e tipo NF-e, o resto continua Pendente. O
retorno da SEFAZ (`NfeResultHandlerBase<T>`) só chama a confirmação do documento que ainda está Pendente. A trava
"confirme emitindo" da confirmação fica só para a devolução própria. `SalesInvoice.TaxDocumentKind` reaproveita o
enum do Documento de Entrada.

**Tech Stack:** .NET 10, EF Core (SQL Server; testes com InMemory), OData v4, xUnit; OpenUI5 + TypeScript, QUnit.

**Spec:** `docs/superpowers/specs/2026-10-06-sales-invoice-confirm-then-transmit-design.md` (backend repo, commit
b2235c0).

## Global Constraints

- Branch `feature/sales-invoice-confirm-then-transmit` nos dois repos (`siagro-b1-backend`, `siagro-b1-frontend`).
  Conferir o branch antes de cada commit. **Nunca push.** Commit com pathspec explícito: o índice do backend tem
  `docs/superpowers/{plans,specs}/2026-10-01-nfe-standalone-taxation*` staged que **não** podem entrar em commit.
  No frontend, `.vscode/.advpl/*` está modificado e **não** entra em commit.
- Mensagem de commit: `tipo(escopo): descrição pt-BR` (escopo `invoice`), com o rodapé
  `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>` e
  `Claude-Session: https://claude.ai/code/session_01DBPxMjj5y1ygnQdgHuhgtw`. Commit com migration leva o trailer
  `DB: AddSalesInvoiceTaxDocumentKind`.
- Identificadores em inglês; tudo o que o usuário lê em pt-BR.
- Tudo só tem efeito na filial com a regra ativa (`TaxCalculationGate`: `Erp == STANDALONE` + `Branch.IssuesNfe`).
  Yokotobi (SAPB1) e MH Agro não percebem mudança.
- A devolução própria (`IsNfeReturn`) e o Documento de Entrada **continuam** "emitir é o que confirma".
- NF-e autorizada não estorna (regra já existente em `SalesInvoicesReverseConfirmService`, não mexer).
- Data do documento ≠ hoje: a transmissão recusa (regra atual), e a mensagem do documento Normal manda estornar.
- Gates finais: `dotnet build SiagroB1.sln` 0 erros; `SiagroB1.Application.Tests` e `SiagroB1.Fiscal.Tests`
  verdes; no frontend `yarn ts-typecheck`, `yarn lint` e o QUnit dos helpers. ⚠️ `yarn test` completo nunca passa
  (gate de cobertura irreal): rode só o módulo tocado.
- ⚠️ Os 146 documentos importados do EfisCloud no `CEAGUI_SIAGRO_DEV` são dado REAL da CEAGUI: **nunca** transmitir
  nenhum deles na verificação. Use documentos fictícios criados para o teste.

## Desvios do spec (decididos aqui, menores)

- **Backfill na migration** (o spec §3 dizia só `DEFAULT 0`): documento Normal **não Pendente** e **sem NF-e**
  (`NfeStatus = None`) vira `Other`. Sem isso, todo documento antigo já confirmado da filial que emite ganharia o
  botão "Transmitir NF-e".
- **Tipo travado com número reservado:** depois que a emissão reservou o número (`NfeRandomCode` gravado, ex.: NF-e
  rejeitada), o tipo não muda. Trocar para Outro deixaria um número da série sem nota.
- **"Informar Nota Fiscal" manual** (lista do documento de saída): na filial que emite, o documento Normal do tipo
  **Outro** passa a aceitar número/série digitados (o servidor já aceita: `EnsureManualTaxDocument` só olha a NF-e).
- O teste `Confirmation_failure_keeps_the_nfe_and_records_the_error_without_partial_confirmation` sai do
  `SalesInvoicesNfeIssueServiceTests`: o documento Normal não é mais confirmado na autorização. O caminho de falha
  da confirmação continua coberto por `SalesInvoicesNfeCompleteConfirmationServiceTests.New_failure_replaces_the_error`
  (o mesmo `ConfirmAsync`) e pela devolução própria.

## Review Focus

1. **Documento antigo já confirmado, sem NF-e, na filial que emite.** Esperado: aparece como tipo Outro, sem botão
   "Transmitir NF-e". → backfill na Task 1 e conferência por SQL na Task 6.
2. **Documento Normal autorizado (emissão ou "Consultar situação").** Esperado: a confirmação NÃO roda de novo, o
   ledger do contrato fica com uma linha por item. → testes na Task 3 (emissão, duplicidade e consulta).
3. **Rejeitada e corrigida.** Esperado: Estornar → editar → Confirmar → Transmitir reaproveita o número; o tipo não
   pode virar Outro com o número reservado. → teste de trava na Task 1; roteiro na Task 6.
4. **Documento Pendente Normal com NF-e autorizada de antes da mudança** (confirmação que falhou). Esperado:
   "Concluir confirmação" continua confirmando. → `SalesInvoicesNfeCompleteConfirmationServiceTests` (sem mudança)
   tem de seguir verde na Task 3.
5. **Documento do tipo Outro.** Esperado: Confirmar funciona, "Transmitir NF-e" é recusado no servidor e some da
   tela, e o número/série podem ser digitados. → testes nas Tasks 2, 3 e 4.

---

## File Structure

| Arquivo | Responsabilidade |
|---|---|
| `SiagroB1.Domain/Entities/SalesInvoice.cs` | propriedade `TaxDocumentKind` |
| `SiagroB1.Migrations/AppContext/<ts>_AddSalesInvoiceTaxDocumentKind.cs` | coluna + backfill |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeLock.cs` | tipo congelado com NF-e e com número reservado |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCreateService.cs` | devolução nasce `Nfe` |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesConfirmService.cs` | trava "confirme emitindo" só na devolução própria |
| `SiagroB1.Application/Services/Nfe/NfeIssueServiceBase.cs` | situação exigida e dica da data por documento |
| `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeIssueService.cs` | Normal exige Confirmado + tipo NF-e |
| `SiagroB1.Application/Services/Nfe/NfeResultHandlerBase.cs` | confirma só o documento Pendente |
| `SiagroB1.Application.Tests/Support/NfeTestSeed.cs` | situação do documento semeado como parâmetro |
| frontend `helpers/NfeHelpers.ts` (+ QUnit) | "Informar Nota Fiscal" aceita tipo Outro |
| frontend `view/salesInvoices/*`, `controller/salesInvoices/*` | campo, botões, textos |
| frontend `view/shipmentBilling/fragments/Billing.fragment.xml`, `controller/shipmentBilling/Main.controller.ts` | tipo no faturamento |

---

### Task 1: Modelo — `TaxDocumentKind` no documento de saída, migration e travas

**Files:**
- Modify: `SiagroB1.Domain/Entities/SalesInvoice.cs` (depois de `IsNfeReturn`, ~linha 160)
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeLock.cs` (`HeaderFiscalFields` e `EnsureHeaderEditable`)
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCreateService.cs:41`
- Create: `SiagroB1.Migrations/AppContext/<timestamp>_AddSalesInvoiceTaxDocumentKind.cs` (+ Designer, snapshot)
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoiceNfeLockTests.cs`
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesCreateTaxationTests.cs`

**Interfaces:**
- Produces: `SalesInvoice.TaxDocumentKind` (`SiagroB1.Domain.Enums.TaxDocumentKind`, padrão `Nfe`), serializado no
  OData como `"Nfe"`/`"Other"`. As Tasks 3, 4 e 5 leem esse nome.

- [ ] **Step 1: Write the failing tests**

Em `SalesInvoiceNfeLockTests.cs`, no fim da classe:

```csharp
    [Fact]
    public async Task Authorized_document_cannot_change_the_document_kind()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized);
        invoice.TaxDocumentKind = TaxDocumentKind.Other;

        var ex = await Assert.ThrowsAsync<DefaultException>(() => HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester"));

        Assert.Contains("autorizada", ex.Message);
    }

    /// <summary>Rejeitada com número reservado: virar "Outro" deixaria o número da série sem nota.</summary>
    [Fact]
    public async Task Document_with_a_reserved_number_cannot_change_the_document_kind()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Rejected);
        invoice.NfeRandomCode = "12345678";
        await db.SaveChangesAsync();
        invoice.TaxDocumentKind = TaxDocumentKind.Other;

        var ex = await Assert.ThrowsAsync<DefaultException>(() => HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester"));

        Assert.Equal("Este documento já tem número de NF-e reservado: o tipo de documento não pode mudar.", ex.Message);
    }

    [Fact]
    public async Task Document_never_issued_can_change_the_document_kind()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.None);
        invoice.TaxDocumentKind = TaxDocumentKind.Other;

        await HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester");

        Assert.Equal(TaxDocumentKind.Other,
            (await db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == invoice.Key)).TaxDocumentKind);
    }
```

Em `SalesInvoicesCreateTaxationTests.cs`, no fim da classe (o arquivo já importa `Microsoft.EntityFrameworkCore`?
se não, acrescente `using Microsoft.EntityFrameworkCore;`):

```csharp
    [Fact]
    public async Task Normal_document_keeps_the_document_kind_from_the_body()
    {
        var db = await Seed();
        var invoice = Invoice();
        invoice.TaxDocumentKind = TaxDocumentKind.Other;

        await Create(db).ExecuteAsync(invoice, "tester");

        Assert.Equal(TaxDocumentKind.Other,
            (await db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == invoice.Key)).TaxDocumentKind);
    }

    /// <summary>O tipo só vale para o Normal: a devolução própria é NF-e e a do cliente não usa o campo.</summary>
    [Fact]
    public async Task Return_document_is_always_nfe()
    {
        var db = await Seed();
        var invoice = Invoice(SalesInvoiceType.Return);
        invoice.TaxDocumentKind = TaxDocumentKind.Other;

        await Create(db).ExecuteAsync(invoice, "tester");

        Assert.Equal(TaxDocumentKind.Nfe, invoice.TaxDocumentKind);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoiceNfeLockTests|FullyQualifiedName~SalesInvoicesCreateTaxationTests"`
Expected: FAIL na compilação — `'SalesInvoice' does not contain a definition for 'TaxDocumentKind'`.

- [ ] **Step 3: Implement**

`SalesInvoice.cs`, logo depois de `public bool IsNfeReturn { get; set; }`:

```csharp

    /// <summary>
    /// Tipo do documento fiscal (spec 2026-10-06 D3). Só tem efeito no documento Normal da filial que emite NF-e pelo
    /// Siagro: <see cref="TaxDocumentKind.Nfe"/> sai pelo "Transmitir NF-e" depois de confirmado;
    /// <see cref="TaxDocumentKind.Other"/> (papel/talão) termina no Confirmar, com número e série digitados.
    /// Devolução é sempre <see cref="TaxDocumentKind.Nfe"/>.
    /// </summary>
    public TaxDocumentKind TaxDocumentKind { get; set; } = TaxDocumentKind.Nfe;
```

`SalesInvoiceNfeLock.cs` — em `HeaderFiscalFields`, acrescente `nameof(SalesInvoice.TaxDocumentKind)` depois de
`nameof(SalesInvoice.VolumeNumbering)`:

```csharp
        nameof(SalesInvoice.VolumeBrand), nameof(SalesInvoice.VolumeNumbering), nameof(SalesInvoice.TaxDocumentKind),
```

E em `EnsureHeaderEditable`, depois do `if (NfeLockRules.IsFrozen(status) && ...)` existente:

```csharp

        // Número já reservado pela emissão (ex.: NF-e rejeitada): o documento é NF-e. Virar "Outro" deixaria o número
        // da série sem nota.
        if (entry.OriginalValues[nameof(SalesInvoice.NfeRandomCode)] is not null
            && NfeLockRules.AnyChanged(entry, [nameof(SalesInvoice.TaxDocumentKind)]))
            throw new DefaultException("Este documento já tem número de NF-e reservado: o tipo de documento não pode mudar.");
```

`SalesInvoicesCreateService.cs`, logo depois da linha `salesInvoice.IsNfeReturn = nfeReturn && ...;`:

```csharp

        // O tipo só vale para o documento Normal: a devolução própria é NF-e, a do cliente não usa o campo.
        if (salesInvoice.InvoiceType != SalesInvoiceType.Normal)
            salesInvoice.TaxDocumentKind = TaxDocumentKind.Nfe;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoiceNfeLockTests|FullyQualifiedName~SalesInvoicesCreateTaxationTests"`
Expected: PASS.

- [ ] **Step 5: Generate the migration**

```bash
dotnet build SiagroB1.sln
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add AddSalesInvoiceTaxDocumentKind --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --no-build
```

Confira que o `Up()` gerado só adiciona a coluna `TaxDocumentKind` em `SALES_INVOICES` (`int`, `nullable: false`,
`defaultValue: 0`). Se aparecer qualquer outra operação, pare e investigue (drift do snapshot). Acrescente o
backfill no fim do `Up()`:

```csharp

            // Documento Normal já confirmado (ou cancelado/devolvido) que nunca teve NF-e pelo Siagro não é NF-e: sem
            // isto, o "Transmitir NF-e" apareceria para todo documento antigo da filial que emite. Enums: InvoiceType
            // Normal = 0, InvoiceStatus Pending = 0, NfeStatus None = 0, TaxDocumentKind Other = 1.
            migrationBuilder.Sql(
                "UPDATE SALES_INVOICES SET TaxDocumentKind = 1 WHERE InvoiceType = 0 AND InvoiceStatus <> 0 AND NfeStatus = 0");
```

**Não** aplique a migration em banco nenhum nesta task (a Task 6 aplica no `CEAGUI_SIAGRO_DEV`).

- [ ] **Step 6: Full suite**

Run: `dotnet build SiagroB1.sln` e `dotnet test SiagroB1.Application.Tests`
Expected: 0 erros; tudo verde.

- [ ] **Step 7: Commit**

```bash
git branch --show-current   # feature/sales-invoice-confirm-then-transmit
git add SiagroB1.Domain/Entities/SalesInvoice.cs SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeLock.cs SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCreateService.cs SiagroB1.Migrations/AppContext SiagroB1.Application.Tests/SalesInvoices/SalesInvoiceNfeLockTests.cs SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesCreateTaxationTests.cs
git commit -m "feat(invoice): tipo de documento (NF-e/Outro) no documento de saída" -m "Nem todo documento de saída da filial que emite é NF-e. O tipo trava com a NF-e autorizada e com o número já reservado; documento antigo confirmado sem NF-e vira Outro no backfill." -m "DB: AddSalesInvoiceTaxDocumentKind" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DBPxMjj5y1ygnQdgHuhgtw" -- SiagroB1.Domain/Entities/SalesInvoice.cs SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeLock.cs SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCreateService.cs SiagroB1.Migrations/AppContext SiagroB1.Application.Tests/SalesInvoices/SalesInvoiceNfeLockTests.cs SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesCreateTaxationTests.cs
```

---

### Task 2: Confirmação — o documento Normal confirma pelo Confirmar na filial que emite

**Files:**
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesConfirmService.cs:79-88`
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesConfirmNfeGuardTests.cs`

**Interfaces:**
- Consumes: `SalesInvoice.TaxDocumentKind` (Task 1).
- Produces: nada novo; muda a regra.

- [ ] **Step 1: Rewrite the tests**

Em `SalesInvoicesConfirmNfeGuardTests.cs`:

1. Troque o `<summary>` da classe por:

```csharp
/// <summary>
/// Com a regra ativa, o documento Normal confirma pelo Confirmar e transmite a NF-e depois (spec 2026-10-06 D1); só a
/// devolução própria confirma com a NF-e autorizada. SAPB1 e STANDALONE sem a chave confirmam como sempre.
/// </summary>
```

2. Substitua o teste `Rule_active_refuses_direct_confirmation` inteiro por:

```csharp
    [Fact]
    public async Task Rule_active_confirms_a_normal_document_directly()
    {
        var (db, invoice) = await SeedAsync();

        await Confirm(db, "STANDALONE").ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, await StatusAsync(db, invoice.Key));
    }

    [Fact]
    public async Task Rule_active_confirms_a_document_of_kind_other()
    {
        var (db, invoice) = await SeedAsync();
        invoice.TaxDocumentKind = TaxDocumentKind.Other;
        await db.SaveChangesAsync();

        await Confirm(db, "STANDALONE").ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, await StatusAsync(db, invoice.Key));
    }
```

Os testes `Rule_active_refuses_direct_confirmation_of_an_own_return` e `Own_return_with_authorized_nfe_is_confirmed`
ficam como estão.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoicesConfirmNfeGuardTests"`
Expected: FAIL — os dois testes novos com `DefaultException: Na filial que emite NF-e pelo Siagro, confirme emitindo a NF-e.`

- [ ] **Step 3: Implement**

Em `SalesInvoicesConfirmService.cs`, troque o bloco do comentário `// NF-e STANDALONE: na filial com a regra ativa...`
até o `throw` por:

```csharp
        // NF-e STANDALONE: na filial com a regra ativa, a devolução própria só confirma com a NF-e autorizada — é a
        // emissão que chama esta confirmação. O documento Normal confirma aqui e transmite a NF-e depois (spec
        // 2026-10-06 D1); as demais devoluções seguem como sempre. Sem o gate (os testes antigos constroem o serviço
        // sem ele) a regra fica inativa.
        if (gate is not null &&
            invoice.IsNfeReturn &&
            invoice.NfeStatus != NfeStatus.Authorized &&
            await gate.IsActiveAsync(invoice.BranchCode))
        {
            throw new DefaultException("Na filial que emite NF-e pelo Siagro, confirme emitindo a NF-e.");
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoicesConfirmNfeGuardTests|FullyQualifiedName~SalesInvoicesNfeReturn"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git branch --show-current
git commit -m "feat(invoice): documento de saída Normal confirma antes da NF-e" -m "O caminhão já saiu e já pesou: a baixa do contrato acontece na confirmação, e a NF-e é transmitida depois. A devolução própria continua confirmando pela autorização." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DBPxMjj5y1ygnQdgHuhgtw" -- SiagroB1.Application/Services/SalesInvoices/SalesInvoicesConfirmService.cs SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesConfirmNfeGuardTests.cs
```

---

### Task 3: Transmissão — Normal exige Confirmado e tipo NF-e; o retorno não reconfirma

**Files:**
- Modify: `SiagroB1.Application/Services/Nfe/NfeIssueServiceBase.cs` (`EnsurePreconditionsAsync` + 2 membros novos)
- Modify: `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeIssueService.cs`
- Modify: `SiagroB1.Application/Services/Nfe/NfeResultHandlerBase.cs` (ramo autorizado de `ApplyAsync`)
- Modify: `SiagroB1.Application.Tests/Support/NfeTestSeed.cs`
- Test: `SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeIssueServiceTests.cs`
- Test: `SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeConsultServiceTests.cs`
- Test: `SiagroB1.Application.Tests/Nfe/PurchaseInvoicesNfeIssueServiceTests.cs` (`Entry_and_sale_share_the_branch_number_sequence`)

**Interfaces:**
- Consumes: `SalesInvoice.TaxDocumentKind` (Task 1).
- Produces:
  - `protected virtual InvoiceStatus RequiredStatus(TDocument document)` em `NfeIssueServiceBase<TDocument>` (padrão `Pending`).
  - `protected virtual string DateFixHint(TDocument document)` em `NfeIssueServiceBase<TDocument>`.
  - `NfeTestSeed.SeedAsync(InvoiceStatus status = InvoiceStatus.Pending)`.
  - Mensagens: `"Só documento Confirmado pode ter a NF-e transmitida: confirme o documento antes."` e
    `"Documento do tipo Outro não é transmitido como NF-e."`.

- [ ] **Step 1: Seed with the status as a parameter**

Em `NfeTestSeed.cs`, troque a assinatura `public static async Task<NfeScenario> SeedAsync()` por:

```csharp
    /// <param name="status">
    /// Situação do documento de saída. O documento Normal é transmitido já Confirmado (spec 2026-10-06 D1); o padrão
    /// Pendente fica para quem testa o que vem antes da confirmação (cancelamento, conclusão de confirmação, travas).
    /// </param>
    public static async Task<NfeScenario> SeedAsync(InvoiceStatus status = InvoiceStatus.Pending)
```

e, no `new SalesInvoice { ... }`, troque `InvoiceStatus = InvoiceStatus.Pending` por `InvoiceStatus = status`.

- [ ] **Step 2: Move the issue and consult tests to the new flow**

Em `SalesInvoicesNfeIssueServiceTests.cs` e `SalesInvoicesNfeConsultServiceTests.cs`, troque **todas** as
ocorrências de `NfeTestSeed.SeedAsync()` por `NfeTestSeed.SeedAsync(InvoiceStatus.Confirmed)`:

```bash
sed -i 's/NfeTestSeed.SeedAsync()/NfeTestSeed.SeedAsync(InvoiceStatus.Confirmed)/g' SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeIssueServiceTests.cs SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeConsultServiceTests.cs
```

Depois, em `SalesInvoicesNfeIssueServiceTests.cs`:

1. Substitua o teste `Authorized_nfe_is_saved_and_the_document_confirmed` pelo mesmo corpo com nome e duas
   asserções diferentes:

```csharp
    [Fact]
    public async Task Authorized_nfe_is_saved_and_the_confirmed_document_is_not_confirmed_again()
    {
        var scenario = await NfeTestSeed.SeedAsync(InvoiceStatus.Confirmed);
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        var confirm = new RecordingConfirmService(scenario.Db);

        var outcome = await Issue(scenario, sefaz, confirm).ExecuteAsync(scenario.InvoiceKey, "tester");

        var invoice = await ReloadAsync(scenario);
        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        Assert.Equal(NfeStatus.Authorized, invoice.NfeStatus);
        Assert.Equal(InvoiceStatus.Confirmed, invoice.InvoiceStatus);
        Assert.Equal("000000001", invoice.TaxDocumentNumber);
        Assert.Equal("1", invoice.TaxDocumentSeries);
        Assert.Equal(44, invoice.ChaveNFe!.Length);
        Assert.Equal("135260000000001", invoice.NfeProtocol);
        Assert.Equal(NfeEnvironment.Homologation, invoice.NfeEnvironment);
        // Já confirmado antes de transmitir: confirmar de novo duplicaria a alocação no contrato (spec T3).
        Assert.Equal(0, confirm.Calls);

        var xmls = await scenario.Db.Context.SalesInvoiceNfeXmls.AsNoTracking().ToListAsync();
        Assert.Contains(xmls, x => x.Kind == NfeXmlKind.Signed && x.Xml.Contains("NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL"));
        Assert.Contains(xmls, x => x.Kind == NfeXmlKind.Authorized && x.Xml.Contains("<nfeProc") && x.Xml.Contains("<protNFe"));
    }
```

2. No teste `Rejection_keeps_the_document_pending_with_the_number`, renomeie para
   `Rejection_keeps_the_document_confirmed_with_the_number` e troque
   `Assert.Equal(InvoiceStatus.Pending, invoice.InvoiceStatus);` por
   `Assert.Equal(InvoiceStatus.Confirmed, invoice.InvoiceStatus);`.

3. No teste `Duplicate_is_consulted_and_followed`, troque `Assert.Equal(1, confirm.Calls);` por
   `Assert.Equal(0, confirm.Calls);`.

4. **Apague** o teste `Confirmation_failure_keeps_the_nfe_and_records_the_error_without_partial_confirmation`
   (ver "Desvios do spec") e, no lugar dele, acrescente:

```csharp
    [Fact]
    public async Task Pending_normal_document_is_refused()
    {
        var scenario = await NfeTestSeed.SeedAsync(InvoiceStatus.Pending);
        var sefaz = new FakeNfeSefazClient();
        var reservation = new FakeNfeNumberReservationService();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db), reservation: reservation)
                .ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal("Só documento Confirmado pode ter a NF-e transmitida: confirme o documento antes.", ex.Message);
        Assert.Equal(0, reservation.Calls);
        Assert.Empty(sefaz.Sent);
    }

    [Fact]
    public async Task Document_of_kind_other_is_refused()
    {
        var scenario = await NfeTestSeed.SeedAsync(InvoiceStatus.Confirmed);
        (await scenario.Db.Context.SalesInvoices.SingleAsync()).TaxDocumentKind = TaxDocumentKind.Other;
        await scenario.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db)).ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal("Documento do tipo Outro não é transmitido como NF-e.", ex.Message);
        Assert.Empty(sefaz.Sent);
    }
```

5. No teste `Document_dated_another_day_is_refused_before_reserving_a_number`, a mensagem esperada passa a ser:

```csharp
        Assert.Equal(
            "A data do documento (02/10/2026) precisa ser a de hoje para emitir a NF-e: estorne a confirmação, altere a data e salve (os impostos são recalculados).",
            ex.Message);
```

Em `SalesInvoicesNfeConsultServiceTests.cs`, no teste `Consult_after_no_response_authorizes_from_the_saved_signed_xml`,
troque `Assert.Equal(1, confirm.Calls);` por `Assert.Equal(0, confirm.Calls);` e acrescente logo abaixo:

```csharp
        Assert.Equal(InvoiceStatus.Confirmed,
            (await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == scenario.InvoiceKey)).InvoiceStatus);
```

Em `PurchaseInvoicesNfeIssueServiceTests.cs`, no teste `Entry_and_sale_share_the_branch_number_sequence`, logo
antes de `await saleIssue.ExecuteAsync(scenario.SaleKey, "tester");`:

```csharp
        // O documento de saída Normal é transmitido já Confirmado (spec 2026-10-06 D1).
        (await scenario.Db.Context.SalesInvoices.SingleAsync(i => i.Key == scenario.SaleKey)).InvoiceStatus = InvoiceStatus.Confirmed;
        await scenario.Db.SaveChangesAsync();
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SiagroB1.Application.Tests.Nfe"`
Expected: FAIL — os testes de emissão e consulta com `DefaultException: Só documento Pendente pode ser emitido.`;
`Pending_normal_document_is_refused` falha na mensagem; `Document_of_kind_other_is_refused` falha (nada é recusado).

- [ ] **Step 4: Implement the base**

Em `NfeIssueServiceBase.cs`, junto dos outros membros `protected virtual` (depois de `BeforeSigning`):

```csharp

    /// <summary>
    /// Situação que o documento precisa ter para emitir. Padrão: Pendente — "emitir é o que confirma". O documento de
    /// saída Normal é transmitido já Confirmado (spec 2026-10-06 D1).
    /// </summary>
    protected virtual InvoiceStatus RequiredStatus(TDocument document) => InvoiceStatus.Pending;

    /// <summary>O que o operador faz para corrigir a data do documento; vai na recusa da data.</summary>
    protected virtual string DateFixHint(TDocument document) => "altere a data e salve (os impostos são recalculados).";
```

Em `EnsurePreconditionsAsync`, troque:

```csharp
        if (invoice.InvoiceStatus != InvoiceStatus.Pending)
            throw new DefaultException("Só documento Pendente pode ser emitido.");
```

por:

```csharp
        var requiredStatus = RequiredStatus(invoice);
        if (invoice.InvoiceStatus != requiredStatus)
            throw new DefaultException(requiredStatus == InvoiceStatus.Confirmed
                ? "Só documento Confirmado pode ter a NF-e transmitida: confirme o documento antes."
                : "Só documento Pendente pode ser emitido.");
```

e a recusa da data:

```csharp
        if (invoiceDay != today)
            throw new DefaultException(
                $"A data do documento ({invoiceDay:dd/MM/yyyy}) precisa ser a de hoje para emitir a NF-e: " +
                DateFixHint(invoice));
```

- [ ] **Step 5: Implement the sales document rules**

Em `SalesInvoicesNfeIssueService.cs`, troque o `<summary>` da classe por:

```csharp
/// <summary>
/// "Transmitir NF-e" do documento de saída (spec 2026-10-02 §9.2; spec 2026-10-06). O documento Normal é transmitido
/// já Confirmado e com tipo NF-e; a devolução própria continua Pendente — nela, emitir é o que confirma.
/// Ordem que não pode mudar: reservar o número e SALVAR; assinar e validar; gravar "Em processamento" + XML assinado e
/// SALVAR; só então enviar.
/// </summary>
```

Substitua `EnsureIssuableType` por:

```csharp
    protected override void EnsureIssuableType(SalesInvoice invoice)
    {
        if (invoice.InvoiceType != SalesInvoiceType.Normal && !SalesInvoicesTaxApplyService.IsOwnNfeReturn(invoice))
            throw new DefaultException(
                "Só o documento Normal e a devolução criada pelo Devolver são emitidos como NF-e por aqui.");

        if (!invoice.IsNfeReturn && invoice.TaxDocumentKind != TaxDocumentKind.Nfe)
            throw new DefaultException("Documento do tipo Outro não é transmitido como NF-e.");
    }

    protected override InvoiceStatus RequiredStatus(SalesInvoice invoice) =>
        invoice.IsNfeReturn ? InvoiceStatus.Pending : InvoiceStatus.Confirmed;

    protected override string DateFixHint(SalesInvoice invoice) => invoice.IsNfeReturn
        ? "altere a data e salve (os impostos são recalculados)."
        : "estorne a confirmação, altere a data e salve (os impostos são recalculados).";
```

- [ ] **Step 6: Implement the result handler**

Em `NfeResultHandlerBase.cs`, no ramo autorizado de `ApplyAsync`, troque:

```csharp
            // A NF-e autorizada vai para o banco ANTES da confirmação: se ela falhar, a nota
            // continua registrada como autorizada.
            await db.SaveChangesAsync();

            return await ConfirmAsync(invoice.Key, userName);
```

por:

```csharp
            // A NF-e autorizada vai para o banco ANTES da confirmação: se ela falhar, a nota
            // continua registrada como autorizada.
            await db.SaveChangesAsync();

            // Só confirma o documento que ainda está Pendente (spec 2026-10-06 T3): o documento de saída Normal foi
            // confirmado antes de transmitir, e confirmar de novo duplicaria a alocação no contrato.
            if (invoice.InvoiceStatus != InvoiceStatus.Pending)
                return NfeIssueOutcomeDto.From(invoice);

            return await ConfirmAsync(invoice.Key, userName);
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SiagroB1.Application.Tests.Nfe"`
Expected: PASS — inclusive `SalesInvoicesNfeReturnIssueTests` (devolução própria Pendente continua emitindo e
confirmando), `SalesInvoicesNfeCompleteConfirmationServiceTests` e `PurchaseInvoicesNfeIssueServiceTests`.

- [ ] **Step 8: Full suite**

Run: `dotnet test SiagroB1.Application.Tests` e `dotnet test SiagroB1.Fiscal.Tests`
Expected: tudo verde. Se algum teste fora de `Nfe/` quebrar por emitir documento Normal Pendente, a correção é semear
`InvoiceStatus.Confirmed` nele — nunca mudar a asserção de comportamento sem entender por quê.

- [ ] **Step 9: Commit**

```bash
git branch --show-current
git commit -m "feat(invoice): transmitir a NF-e do documento de saída já confirmado" -m "A emissão pergunta ao documento qual situação exige: o de saída Normal, Confirmado e do tipo NF-e; a devolução própria e o Documento de Entrada seguem Pendentes. O retorno da SEFAZ só confirma o que ainda está Pendente." -m "Atenção: confirmar de novo um documento já confirmado duplicaria a alocação no ledger do contrato de venda." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DBPxMjj5y1ygnQdgHuhgtw" -- SiagroB1.Application/Services/Nfe/NfeIssueServiceBase.cs SiagroB1.Application/Services/Nfe/SalesInvoicesNfeIssueService.cs SiagroB1.Application/Services/Nfe/NfeResultHandlerBase.cs SiagroB1.Application.Tests/Support/NfeTestSeed.cs SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeIssueServiceTests.cs SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeConsultServiceTests.cs SiagroB1.Application.Tests/Nfe/PurchaseInvoicesNfeIssueServiceTests.cs
```

(Se o Step 8 obrigou a mexer em outro teste, acrescente o arquivo ao pathspec.)

---

### Task 4: Frontend — "Informar Nota Fiscal" aceita o tipo Outro

Repo: `siagro-b1-frontend` (os caminhos abaixo são relativos a `webapp/`).

**Files:**
- Modify: `helpers/NfeHelpers.ts:148-157` (`isManualTaxDocumentBlocked`)
- Modify: `controller/salesInvoices/Main.controller.ts` (chamada em ~linha 274)
- Modify: `view/salesInvoices/Main.view.xml:49` (`$select`)
- Test: `test/unit/helpers/NfeHelpers.qunit.ts`

**Interfaces:**
- Consumes: propriedade OData `TaxDocumentKind` (`"Nfe"`/`"Other"`) da Task 1.
- Produces: `isManualTaxDocumentBlocked(nfeStatus, taxLocked, invoiceType, isNfeReturn = false, taxDocumentKind = "Nfe")`.

- [ ] **Step 1: Write the failing test**

Em `NfeHelpers.qunit.ts`, depois do teste `"regra de bloqueio por situação, trava e tipo"`:

```ts
QUnit.test("documento do tipo Outro aceita número digitado na filial que emite", function (assert) {
	assert.strictEqual(isManualTaxDocumentBlocked("None", true, "Normal", false, "Other"), false, "CEAGUI Normal tipo Outro");
	assert.strictEqual(isManualTaxDocumentBlocked("None", true, "Normal", false, "Nfe"), true, "CEAGUI Normal tipo NF-e");
	assert.strictEqual(isManualTaxDocumentBlocked("Rejected", true, "Normal", false, "Other"), true, "já foi à SEFAZ");
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `yarn ts-typecheck`
Expected: FAIL — `Expected 2-4 arguments, but got 5`.

- [ ] **Step 3: Implement**

Em `NfeHelpers.ts`, substitua o comentário e a função:

```ts
/**
 * "Informar Nota Fiscal" manual não vale quando a NF-e já foi emitida, no documento Normal do tipo NF-e de filial
 * que emite pelo Siagro, nem na devolução própria (criada pelo Devolver, sai com NF-e do Siagro). O documento Normal
 * do tipo Outro (papel/talão) e as demais devoluções seguem manuais.
 */
export function isManualTaxDocumentBlocked(
  nfeStatus: string, taxLocked: boolean, invoiceType: string, isNfeReturn = false, taxDocumentKind = "Nfe"
): boolean {
  return isEmittedNfeStatus(nfeStatus) || isNfeReturn === true
    || (taxLocked === true && invoiceType === "Normal" && taxDocumentKind !== "Other");
}
```

Em `controller/salesInvoices/Main.controller.ts`, na chamada:

```ts
    if (isManualTaxDocumentBlocked(nfeStatus, taxLocked, invoiceType, ctx.getProperty("IsNfeReturn") === true,
      ctx.getProperty("TaxDocumentKind") as string)) {
```

e ajuste a frase da segunda mensagem logo abaixo para:
`"Na filial que emite NF-e pelo Siagro, número, série e chave do documento do tipo NF-e vêm da emissão."`

Em `view/salesInvoices/Main.view.xml`, acrescente `TaxDocumentKind` ao fim do `$select` da tabela (nenhuma coluna o
exibe, e coluna invisível não entra no `$select`):

```xml
          parameters: { $select: 'ShipmentLoadKey,WithoutTaxDocument,NfeStatus,NfeCancelledAt,InvoiceStatus,BranchCode,InvoiceType,IsNfeReturn,TotalInvoiceItems,TaxDocumentKind' },
```

- [ ] **Step 4: Run to verify it passes**

Run: `yarn ts-typecheck` e `yarn lint`; depois, com `yarn start` rodando em segundo plano,
`npx ui5-test-runner --url http://localhost:8080/test/testsuite.qunit.html` (ou abra
`http://localhost:8080/test/unit/unitTests.qunit.html?module=NfeHelpers` no navegador).
Expected: typecheck e lint limpos; os testes do módulo `NfeHelpers` verdes.

- [ ] **Step 5: Commit**

```bash
git branch --show-current   # feature/sales-invoice-confirm-then-transmit
git commit -m "feat(invoice): número digitado no documento de saída do tipo Outro" -m "Na filial que emite NF-e, o documento Normal do tipo Outro (papel/talão) não passa pela emissão; o número e a série são digitados, como o servidor já aceita." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DBPxMjj5y1ygnQdgHuhgtw" -- webapp/helpers/NfeHelpers.ts webapp/test/unit/helpers/NfeHelpers.qunit.ts webapp/controller/salesInvoices/Main.controller.ts webapp/view/salesInvoices/Main.view.xml
```

---

### Task 5: Frontend — campo "Tipo de documento", Confirmar/Transmitir e faturamento da carga

Repo: `siagro-b1-frontend` (caminhos relativos a `webapp/`).

**Files:**
- Modify: `view/salesInvoices/fragments/Form.fragment.xml` (depois do `Select` de `InvoiceType`, ~linha 78)
- Modify: `controller/salesInvoices/Add.controller.ts:70` (dados iniciais do `create`)
- Modify: `view/salesInvoices/Detail.view.xml:31-37` (botões)
- Modify: `controller/salesInvoices/Detail.controller.ts` (`onIssueNfe`)
- Modify: `view/shipmentBilling/fragments/Billing.fragment.xml` (depois do `Select` "Tipo Frete")
- Modify: `controller/shipmentBilling/Main.controller.ts` (`BillingForm`, `openBillingDialog`, payload)

**Interfaces:**
- Consumes: `TaxDocumentKind` (Task 1); `ui>/taxLocked` (já existe nas telas do documento de saída);
  `isTaxCalculationActive(branchCode)` (já existe em `CommonController`, herdado pelo `BaseController` do faturamento).
- Produces: nada consumido por outras tasks.

- [ ] **Step 1: Form field**

Em `view/salesInvoices/fragments/Form.fragment.xml`, logo depois do `</Select>` que fecha o `Select` de `InvoiceType`
(o que tem os itens "Normal"/"Retorno"):

```xml
            <!-- Na filial que emite NF-e pelo Siagro (ui>/taxLocked), o documento Normal é NF-e (transmitida depois de
                 confirmado) ou Outro (papel/talão, termina no Confirmar). Fora dela o campo some. -->
            <Label text="Tipo de documento"
              visible="{= ${ui>/taxLocked} === true &amp;&amp; ${path: 'InvoiceType', targetType: 'any'} === 'Normal' }" />
            <Select
              visible="{= ${ui>/taxLocked} === true &amp;&amp; ${path: 'InvoiceType', targetType: 'any'} === 'Normal' }"
              editable="{= ${ui>/editable} === true }"
              forceSelection="false"
              selectedKey="{ path: 'TaxDocumentKind', targetType: 'any' }">
              <core:ListItem key="Nfe" text="NF-e (eletrônica)" />
              <core:ListItem key="Other" text="Outro (papel/talão…)" />
            </Select>
```

Em `controller/salesInvoices/Add.controller.ts`, nos dados do `oBinding.create({ ... })` que têm
`InvoiceType: "Normal",`, acrescente a linha seguinte:

```ts
        TaxDocumentKind: "Nfe",
```

- [ ] **Step 2: Detail buttons**

Em `view/salesInvoices/Detail.view.xml`, substitua o comentário e os botões "Confirmar" e "Emitir NF-e" por:

```xml
          <!-- Na filial com a regra ativa (ui>/taxLocked), o documento Normal confirma e depois transmite a NF-e (spec
               2026-10-06 D1); a devolução própria segue "emitir é o que confirma". -->
          <Button text="Confirmar" type="Success" press=".onConfirm"
            visible="{= ${ui>/taxLocked} !== true || ${IsNfeReturn} !== true }"
            enabled="{= ${path: 'InvoiceStatus', targetType: 'any'}==='Pending' ? true : false }" />
          <Button text="Transmitir NF-e" type="Success" icon="sap-icon://paper-plane" press=".onIssueNfe"
            visible="{= ${ui>/taxLocked} === true &amp;&amp; ${path: 'InvoiceType', targetType: 'any'} === 'Normal' &amp;&amp; ${IsNfeReturn} !== true &amp;&amp; ${path: 'TaxDocumentKind', targetType: 'any'} === 'Nfe' }"
            enabled="{= ${path: 'InvoiceStatus', targetType: 'any'} === 'Confirmed' &amp;&amp; (${path: 'NfeStatus', targetType: 'any'} === 'None' || ${path: 'NfeStatus', targetType: 'any'} === 'Rejected') }" />
          <Button text="Emitir NF-e" type="Success" icon="sap-icon://paper-plane" press=".onIssueNfe"
            visible="{= ${ui>/taxLocked} === true &amp;&amp; ${IsNfeReturn} === true }"
            enabled="{= ${path: 'InvoiceStatus', targetType: 'any'} === 'Pending' &amp;&amp; (${path: 'NfeStatus', targetType: 'any'} === 'None' || ${path: 'NfeStatus', targetType: 'any'} === 'Rejected') }" />
```

Em `controller/salesInvoices/Detail.controller.ts`, substitua `onIssueNfe`:

```ts
  async onIssueNfe() {
    const ctx = this.getView().getBindingContext() as Context;
    // Documento Normal: já confirmado, a NF-e é transmitida. Devolução própria: emitir é o que confirma.
    const verb = ctx?.getProperty("IsNfeReturn") === true ? "Emitir" : "Transmitir";
    if (!ctx || !(await confirmDialog(`${verb} a NF-e deste documento ?`, `${verb} NF-e ?`))) {
      return;
    }

    await this.runNfeAction(ServerRoutes.salesInvoicesIssueNfe, ctx);
  }
```

- [ ] **Step 3: Billing dialog**

Em `controller/shipmentBilling/Main.controller.ts`, no tipo `BillingForm`, acrescente:

```ts
  /** "Nfe" ou "Other" — só aparece (e só vale) na filial que emite NF-e pelo Siagro. */
  TaxDocumentKind?: string,
  /** Só exibição — a filial da carga emite NF-e pelo Siagro (TaxCalculationGate). */
  TaxLocked?: boolean,
```

Em `openBillingDialog`, no `viewModel.setData({ ... })`, acrescente as duas propriedades:

```ts
      TaxDocumentKind: "Nfe",
      TaxLocked: false,
```

e, logo depois do `setData`, antes de `contractsTable.clearSelection();`:

```ts
    viewModel.setProperty("/TaxLocked", await this.isTaxCalculationActive(load.BranchCode));
```

No objeto `salesInvoice` montado no faturamento (o que tem `ShipmentLoadKey: billing?.ShipmentLoadKey,`),
acrescente:

```ts
            TaxDocumentKind: billing?.TaxDocumentKind ?? "Nfe",
```

Em `view/shipmentBilling/fragments/Billing.fragment.xml`, logo depois do `</Select>` do "Tipo Frete":

```xml
				<Label text="Tipo de documento" visible="{= ${viewModel>/TaxLocked} === true }" />
				<Select
					visible="{= ${viewModel>/TaxLocked} === true }"
					forceSelection="false"
					selectedKey="{viewModel>/TaxDocumentKind}"
				>
					<core:Item key="Nfe" text="NF-e (eletrônica)" />
					<core:Item key="Other" text="Outro (papel/talão…)" />
				</Select>
```

- [ ] **Step 4: Typecheck and lint**

Run: `yarn ts-typecheck` e `yarn lint`
Expected: limpos. (A verificação de comportamento é a Task 6, no navegador.)

- [ ] **Step 5: Commit**

```bash
git branch --show-current
git commit -m "feat(invoice): confirmar e depois transmitir a NF-e na tela do documento de saída" -m "Na filial que emite, o documento Normal mostra o Confirmar e, já confirmado, o Transmitir NF-e; o tipo de documento aparece no formulário e no faturamento da carga. A devolução própria continua com o Emitir NF-e." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DBPxMjj5y1ygnQdgHuhgtw" -- webapp/view/salesInvoices/fragments/Form.fragment.xml webapp/controller/salesInvoices/Add.controller.ts webapp/view/salesInvoices/Detail.view.xml webapp/controller/salesInvoices/Detail.controller.ts webapp/view/shipmentBilling/fragments/Billing.fragment.xml webapp/controller/shipmentBilling/Main.controller.ts
```

---

### Task 6: Migration no banco de desenvolvimento da CEAGUI e verificação ponta a ponta

**Files:** nenhum código. Banco `CEAGUI_SIAGRO_DEV` (local).

- [ ] **Step 1: Contar o que o backfill vai mudar e aplicar a migration**

Antes de aplicar, guarde a contagem (rode no SQL Server local, banco `CEAGUI_SIAGRO_DEV`, com `sqlcmd -I`):

```sql
SELECT InvoiceStatus, NfeStatus, COUNT(*) FROM SALES_INVOICES WHERE InvoiceType = 0 GROUP BY InvoiceStatus, NfeStatus;
```

Aplique só neste banco, sempre com o ambiente explícito:

```bash
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

Confira o backfill:

```sql
SELECT InvoiceStatus, NfeStatus, TaxDocumentKind, COUNT(*) FROM SALES_INVOICES WHERE InvoiceType = 0 GROUP BY InvoiceStatus, NfeStatus, TaxDocumentKind;
```

Expected: os Pendentes (incluindo os 146 importados) ficam `0` (NF-e); os confirmados com NF-e autorizada ficam `0`;
os confirmados/cancelados sem NF-e (`NfeStatus = 0`) ficam `1` (Outro).

- [ ] **Step 2: Subir a stack**

Backend com o perfil `ceagui` (Web, Gateway, Reports) e frontend com `yarn start:dev`; login `admin`/`1234`, filial
CEAGUI. Confirme antes que os dois repos estão no branch `feature/sales-invoice-confirm-then-transmit`.

- [ ] **Step 3: Roteiro no navegador (homologação)**

Use **só documentos fictícios** criados agora (Global Constraints). Data de hoje.

1. **Avulso NF-e:** criar documento de saída Normal (cliente/produto fictícios, natureza com CFOP), tipo NF-e, pesos
   preenchidos → Confirmar → o botão "Transmitir NF-e" aparece habilitado → Transmitir → autorizada (DANFE abre).
   "Estornar confirmação" é recusado. No ledger do contrato (`SALES_CONTRACTS_ALLOCATIONS`), uma linha por item do
   documento — conferir por SQL se o documento tiver contrato.
2. **Avulso Outro:** criar documento Normal tipo Outro → Confirmar → nenhum botão de NF-e; na lista, "Informar Nota
   Fiscal" aceita número e série digitados.
3. **Data de ontem:** documento confirmado com data de ontem → Transmitir recusa com "estorne a confirmação, altere a
   data e salve…"; Estornar → trocar a data → Confirmar → Transmitir funciona.
4. **Carga:** se o banco tiver carga com saldo a faturar, faturar pelo `/shipment-billing` com tipo NF-e → o documento
   nasce Confirmado com o tipo gravado → Transmitir pelo detalhe. Se não houver carga faturável no
   `CEAGUI_SIAGRO_DEV`, registre o passo como **não exercido** no relatório final (não monte carga com dado real).
5. **Devolução própria:** num documento autorizado do passo 1, "Devolver" → a devolução mostra "Emitir NF-e" (não o
   Confirmar) e, autorizada, fica Confirmada.

- [ ] **Step 4: Relatório**

Liste o que foi visto em cada passo, os números de NF-e de homologação consumidos e o que ficou sem exercer. Não
faça merge: é decisão do usuário.
