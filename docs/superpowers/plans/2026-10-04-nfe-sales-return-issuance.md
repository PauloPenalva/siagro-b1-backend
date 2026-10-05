# NF-e STANDALONE — NF-e de devolução de venda — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Numa filial que emite NF-e pelo Siagro, o usuário clica em "Devolver" numa venda autorizada, informa quanto voltou e o motivo, e o Siagro cria a devolução Pendente que, ao "Emitir NF-e", sai como NF-e de **entrada** (finalidade 4) referenciando a venda no cabeçalho (`NFref`) e em cada item (`DFeReferenciado`), e só confirma com a autorização.

**Architecture:** O que muda é pequeno e encaixado no 2a: (1) a natureza de saída aponta a natureza de entrada da devolução (`USAGES.ReturnUsageCode`); (2) um serviço novo cria a devolução própria (`SALES_INVOICES.IsNfeReturn = true`) reaproveitando `SalesInvoiceReturnFactory` e `SalesInvoicesCreateService`; (3) `SalesInvoicesTaxApplyService` passa a calcular a devolução própria com a natureza de entrada, IBS/CBS pela data da venda e uma conferência contra a linha vendida; (4) o montador/builder da NF-e ganham finalidade, nota referenciada e item referenciado; (5) a numeração dos itens (`NfeItemNumber`) passa a ser gravada na emissão; (6) travas na confirmação, no "Informar Nota Fiscal", na edição e no "Retornar"/"Recusa de carga". Frontend: botão e diálogo "Devolver", campo "Natureza de devolução" e as travas visuais.

**Tech Stack:** .NET 10, EF Core 10 (SQL Server), Microsoft.AspNetCore.OData 9.4.1, xUnit + EF InMemory, Zeus.Net.NFe.NFCe 2026.9.24.1416; OpenUI5 1.141 + TypeScript (OData v4), QUnit.

**Spec:** `siagro-b1-backend/docs/superpowers/specs/2026-10-04-nfe-sales-return-issuance-design.md` (commit 3689f47)

## Global Constraints

- Código (classes, tabelas, colunas) em inglês; texto que o usuário lê (labels, mensagens de negócio) em **pt-BR**; comentários em pt-BR.
- **Regra ativa** = `Erp == "STANDALONE"` **e** `Branch.IssuesNfe`, sempre perguntada ao `TaxCalculationGate`. Sem ela (Yokotobi/SAPB1, MH Agro): comportamento idêntico ao de hoje.
- **Devolução própria** = `InvoiceType == Return` **e** `IsNfeReturn == true`. Só `SalesInvoicesNfeReturnCreateService` grava `IsNfeReturn = true`; o corpo da API nunca.
- Regras da SEFAZ que o XML cumpre: `finNFe 4` exige `tPag 90` com `vPag 0` (rejeição 871) e documento referenciado (rejeição 321); **VC02-14** exige `det/DFeReferenciado` (chave + `nItem`) em cada item (homologação desde 01/07/2026, produção 03/11/2026).
- Mensagens de guarda/negócio: `DefaultException` em pt-BR (o controller devolve 400).
- Quantidades em action OData: `Collection(Edm.Double)`, nunca `Decimal` (o UI5 serializa `Edm.Decimal` como string e o 400 não nomeia o campo).
- Migration única `AddSalesReturnNfe` (aditiva). Gerar/aplicar a partir de `siagro-b1-backend/` com ambiente explícito: `ASPNETCORE_ENVIRONMENT=Ceagui-Development` (banco `CEAGUI_SIAGRO_DEV`). **Ler a migration gerada antes de seguir.** Não aplicar em nenhum outro banco.
- Novo arquivo ⇒ `git add <arquivo>` imediato no repo dele.
- Branch nos dois repos: `feature/nfe-sales-return-issuance` (conferir antes de cada commit). Commits por tarefa, padrão `tipo(escopo): descrição pt-BR`, escopos `master-data` (natureza) e `invoice` (o resto); trailer `DB: AddSalesReturnNfe` no commit da migration. Mensagem termina com:
  ```
  Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01BgJE3fiJjTbTPaTvZhCpnx
  ```
  **Sempre com pathspec explícito** (`git commit -F <msg> -- <arquivos>`): no backend os documentos do sub-projeto 1 (`docs/superpowers/{specs,plans}/2026-10-01-nfe-standalone-taxation*`) estão staged e **não** entram; no frontend, `.vscode/.advpl/*.tlpp` estão modificados e **não** entram. Nunca push.
- Commit negado pelo classificador do auto mode: grave a mensagem num arquivo, deixe o comando `git commit -F <arquivo> -- <pathspecs>` para o usuário e siga. Nunca contornar.
- Testes backend (a partir de `siagro-b1-backend/`): `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Classe>"` e `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~<Classe>"`. Pare qualquer `SiagroB1.Web`/`Gateway`/`Reports` em execução antes (travam as DLLs) — **menos** o `SiagroB1.Reports` aberto pelo Rider do usuário: se ele travar o build, avise o usuário em vez de matá-lo.
- Frontend (a partir de `siagro-b1-frontend/`): `yarn ts-typecheck`, `yarn lint`, `npx ui5lint` (base **920** problemas — não pode subir), QUnit: `npx ui5 serve --port 8080` em segundo plano e `npx ui5-test-runner --url http://localhost:8080/test/testsuite.qunit.html` (o `yarn test` nunca passa por causa do gate de cobertura — não use).
- Frontend: enum em expressão usa `${path: 'X', targetType: 'any'}`; filtro de enum vai como `$filter` estático; tipo de dado em XML via `core:require` (string global aumenta o ui5lint); `--` dentro de comentário XML mata o fragmento; `setProperty` em contexto com update group diferido **não** leva `await` (trava).

## Review Focus

1. **Devolução de só parte dos itens de uma venda com vários itens** — o `DFeReferenciado.nItem` tem de ser o número do item **na venda**, não a posição na devolução (devolver só o 2º item ⇒ `det nItem=1` referencia `nItem 2`). Teste na Task 6.
2. **Venda de origem que deixou de estar confirmada** (estornada depois de a devolução nascer) — a emissão recusa antes de reservar número, senão a confirmação pós-autorização falharia. Teste na Task 6.
3. **Saldo estourado na hora de emitir** (duas devoluções pendentes da mesma venda, ou a quantidade editada depois) — recusa antes de reservar número. Teste na Task 6.
4. **Venda autorizada antes desta mudança, de um item só** (o DS000146 de homologação) — `nItem` vale 1 sem número gravado. Teste na Task 5.
5. **Quantidade apagada no diálogo** (campo vazio vira `null`) — o item fica de fora, sem erro de número. Teste na Task 9.

---

## File Structure

**Backend (`siagro-b1-backend/`)**

| Arquivo | Responsabilidade |
|---|---|
| `SiagroB1.Domain/Entities/Usage.cs` (mod) | `ReturnUsageCode` + navegação `ReturnUsage` |
| `SiagroB1.Domain/Entities/SalesInvoice.cs` (mod) | `IsNfeReturn` |
| `SiagroB1.Domain/Entities/SalesInvoiceItem.cs` (mod) | `NfeItemNumber` |
| `SiagroB1.Domain/Models/UsageModel.cs` (mod) | `ReturnUsageCode`, `ReturnUsageName` |
| `SiagroB1.Domain/Dtos/SalesInvoiceNfeReturnableItemDto.cs` (novo) | linha do diálogo "Devolver" |
| `SiagroB1.Application/Services/Taxes/UsageTaxationMapper.cs` (mod) | copia `ReturnUsageCode` |
| `SiagroB1.Application/Services/UsageService.cs` (mod) | projeção + validações do vínculo + trava de exclusão |
| `SiagroB1.Fiscal/Nfe/NfeIssueInput.cs` (mod) | `NfePurpose`, `NfeItemReference`, `Purpose`, `ReferencedKeys`, `Reference` |
| `SiagroB1.Fiscal/Nfe/NfeXmlBuilder.cs` (mod) | `tpNF`/`finNFe`/`NFref`/`DFeReferenciado` |
| `SiagroB1.Application/Services/Nfe/SalesInvoiceNfeItemNumbering.cs` (novo) | `det/@nItem` fixo e gravado |
| `SiagroB1.Application/Services/Nfe/SalesInvoiceNfeReturnBalance.cs` (novo) | saldo devolvível por item da venda |
| `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeReturnCreateService.cs` (novo) | "Devolver": cria a devolução própria |
| `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeReturnableItemsService.cs` (novo) | itens e saldos para o diálogo |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeReturnConference.cs` (novo) | conferência devolução × venda |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeReturnLock.cs` (novo) | travas de cabeçalho/linha da devolução própria |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesTaxApplyService.cs` (mod) | cálculo da devolução própria |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCreateService.cs` (mod) | parâmetro `nfeReturn` |
| `SiagroB1.Application/Services/Nfe/NfeIssueContext.cs` (mod) | condição anulável + `NfeReturnOrigin` |
| `SiagroB1.Application/Services/Nfe/NfeReadinessValidator.cs` (mod) | CFOP 1/2, sem condição, origem |
| `SiagroB1.Application/Services/Nfe/NfeIssueInputAssembler.cs` (mod) | ordem/numeração, finalidade, referências, pag 90, infCpl |
| `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeIssueService.cs` (mod) | aceita a devolução própria, saldo, renumeração |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesConfirmService.cs` (mod) | guarda vale para a devolução própria |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeLock.cs` (mod) | "Informar Nota Fiscal" e `IsNfeReturn` imutável |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesUpdateService.cs`, `SalesInvoicesItemsUpdateService.cs`, `SalesInvoicesItemsCreateService.cs` (mod) | travas |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesReturnService.cs`, `ShipmentLoads/ShipmentLoadsRefuseService.cs` (mod) | recusa com origem autorizada na regra ativa |
| `SiagroB1.Web/Actions/Nfe/SalesInvoicesCreateNfeReturnController.cs` (novo), `SiagroB1.Web/Functions/Nfe/SalesInvoicesNfeReturnableItemsController.cs` (novo) | endpoints |
| `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (mod) | EDM e DI |
| `SiagroB1.Migrations/AppContext/<ts>_AddSalesReturnNfe.cs` (+ Designer, snapshot) (gerado) | 3 colunas + FK |

**Frontend (`siagro-b1-frontend/webapp/`)**

| Arquivo | Responsabilidade |
|---|---|
| `helpers/NfeReturnHelpers.ts` (novo) | pré-preenchimento e validação do diálogo |
| `helpers/NfeHelpers.ts` (mod) | `isManualTaxDocumentBlocked` com a devolução própria |
| `controller/usages/BaseController.ts` (novo) | value help da natureza de devolução |
| `controller/usages/Add.controller.ts`, `Edit.controller.ts` (mod) | herdam a base; payload inicial |
| `view/usages/fragments/Form.fragment.xml` (mod) | campo "Natureza de devolução" |
| `view/salesInvoices/fragments/NfeReturnDialog.fragment.xml` (novo) | diálogo "Devolver" |
| `controller/salesInvoices/Detail.controller.ts`, `BaseController.ts`, `Edit.controller.ts`, `Main.controller.ts` (mod) | Devolver, flag `ui>/nfeReturn`, "Informar Nota Fiscal" |
| `view/salesInvoices/Detail.view.xml`, `Main.view.xml`, `fragments/Form.fragment.xml`, `fragments/Items.fragment.xml` (mod) | botões e travas |
| `model/ServerRoutes.ts` (mod) | rotas novas |
| `test/unit/helpers/NfeReturnHelpers.qunit.ts` (novo), `test/unit/helpers/NfeHelpers.qunit.ts`, `test/unit/unitTests.qunit.ts` (mod) | testes |

---

### Task 1: Esquema e natureza de devolução

**Files:**
- Modify: `SiagroB1.Domain/Entities/Usage.cs`, `SiagroB1.Domain/Entities/SalesInvoice.cs`, `SiagroB1.Domain/Entities/SalesInvoiceItem.cs`, `SiagroB1.Domain/Models/UsageModel.cs`
- Modify: `SiagroB1.Application/Services/Taxes/UsageTaxationMapper.cs`, `SiagroB1.Application/Services/UsageService.cs`
- Create (gerado): `SiagroB1.Migrations/AppContext/<timestamp>_AddSalesReturnNfe.cs` + `.Designer.cs`; Modify (gerado): `AppDbContextModelSnapshot.cs`
- Test: `SiagroB1.Application.Tests/Usages/UsageReturnUsageTests.cs` (novo)

**Interfaces:**
- Produces: `Usage.ReturnUsageCode (int?)`, `Usage.ReturnUsage (Usage?)`, `SalesInvoice.IsNfeReturn (bool)`, `SalesInvoiceItem.NfeItemNumber (int?)`, `UsageModel.ReturnUsageCode (int?)`, `UsageModel.ReturnUsageName (string?)`.

- [ ] **Step 1: Escrever os testes que falham**

Criar `SiagroB1.Application.Tests/Usages/UsageReturnUsageTests.cs` e `git add` dele:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Usages;

/// <summary>
/// Natureza de devolução (spec §5): a natureza de SAÍDA aponta a natureza de ENTRADA que a NF-e de
/// devolução de uma venda feita com ela vai usar.
/// </summary>
public class UsageReturnUsageTests
{
    private static UsageService Service(UnitOfWork db) => new(db, NullLogger<UsageService>.Instance);

    private static UsageModel Sale(string name = "Venda", int? returnUsage = null) => new()
    {
        Name = name, Direction = UsageDirection.Outgoing, CfopOutgoingInState = "5102", CfopOutgoingOutState = "6102",
        RequiresQuantity = true, ReturnUsageCode = returnUsage,
    };

    private static UsageModel Return(bool inactive = false) => new()
    {
        Name = "Entrada devolução", Direction = UsageDirection.Incoming,
        CfopIncomingInState = "1202", CfopIncomingOutState = "2202",
        PisCst = "72", CofinsCst = "72", RequiresQuantity = true, Inactive = inactive,
    };

    private static async Task<string> Rejects(Func<Task> act)
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(act);
        return ex.Message;
    }

    [Fact]
    public async Task Sale_points_to_an_incoming_return_usage_and_reads_back_its_name()
    {
        var db = TestDb.CreateUnitOfWork();
        var ret = await Service(db).CreateAsync(Return());

        var sale = await Service(db).CreateAsync(Sale(returnUsage: ret.Code));
        var read = await Service(db).GetByIdAsync(sale.Code);

        Assert.Equal(ret.Code, read!.ReturnUsageCode);
        Assert.Equal("Entrada devolução", read.ReturnUsageName);
    }

    [Fact]
    public async Task Incoming_usage_cannot_have_a_return_usage()
    {
        var db = TestDb.CreateUnitOfWork();
        var ret = await Service(db).CreateAsync(Return());
        var model = Return();
        model.Name = "Outra entrada";
        model.ReturnUsageCode = ret.Code;

        Assert.Equal("Só natureza de saída tem natureza de devolução.", await Rejects(() => Service(db).CreateAsync(model)));
    }

    [Fact]
    public async Task Return_usage_must_be_incoming()
    {
        var db = TestDb.CreateUnitOfWork();
        var other = await Service(db).CreateAsync(Sale("Venda 2"));

        Assert.Equal("A natureza de devolução Venda 2 precisa ser de entrada.",
            await Rejects(() => Service(db).CreateAsync(Sale(returnUsage: other.Code))));
    }

    [Fact]
    public async Task Return_usage_must_exist()
    {
        var db = TestDb.CreateUnitOfWork();

        Assert.Equal("Natureza de devolução 999 não encontrada.",
            await Rejects(() => Service(db).CreateAsync(Sale(returnUsage: 999))));
    }

    [Fact]
    public async Task Inactive_return_usage_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var ret = await Service(db).CreateAsync(Return(inactive: true));

        Assert.Equal("A natureza de devolução Entrada devolução está inativa.",
            await Rejects(() => Service(db).CreateAsync(Sale(returnUsage: ret.Code))));
    }

    [Fact]
    public async Task Usage_cannot_be_its_own_return_usage()
    {
        var db = TestDb.CreateUnitOfWork();
        var sale = await Service(db).CreateAsync(Sale());
        sale.ReturnUsageCode = sale.Code;

        Assert.Equal("A natureza de devolução não pode ser a própria natureza.",
            await Rejects(() => Service(db).UpdateAsync(sale.Code, sale)));
    }

    [Fact]
    public async Task Incoming_usage_used_as_return_cannot_become_outgoing()
    {
        var db = TestDb.CreateUnitOfWork();
        var ret = await Service(db).CreateAsync(Return());
        await Service(db).CreateAsync(Sale(returnUsage: ret.Code));
        var changed = Return();
        changed.Direction = UsageDirection.Outgoing;
        changed.CfopOutgoingInState = "5102";
        changed.PisCst = null;
        changed.CofinsCst = null;

        Assert.Equal(
            "A natureza Entrada devolução é a natureza de devolução de outra natureza de saída: o tipo não pode virar Saída.",
            await Rejects(() => Service(db).UpdateAsync(ret.Code, changed)));
    }

    [Fact]
    public async Task Return_usage_in_use_cannot_be_deleted()
    {
        var db = TestDb.CreateUnitOfWork();
        var ret = await Service(db).CreateAsync(Return());
        await Service(db).CreateAsync(Sale(returnUsage: ret.Code));

        Assert.Equal(
            "Natureza de operação Entrada devolução é a natureza de devolução de outra natureza. Inative-a em vez de excluir.",
            await Rejects(() => Service(db).DeleteAsync(ret.Code)));
    }

    [Fact]
    public async Task Incoming_usage_never_keeps_a_return_usage_in_the_table()
    {
        var db = TestDb.CreateUnitOfWork();
        var ret = await Service(db).CreateAsync(Return());
        var sale = await Service(db).CreateAsync(Sale(returnUsage: ret.Code));
        sale.ReturnUsageCode = null;

        await Service(db).UpdateAsync(sale.Code, sale);

        Assert.Null((await db.Context.Usages.AsNoTracking().SingleAsync(u => u.Code == sale.Code)).ReturnUsageCode);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~UsageReturnUsageTests"`
Expected: FAIL de compilação — `UsageModel` não contém `ReturnUsageCode`/`ReturnUsageName`.

- [ ] **Step 3: Entidades e modelo**

Em `SiagroB1.Domain/Entities/Usage.cs`, depois de `CbsRateReduction` (fim da classe):

```csharp

    /// <summary>
    /// Natureza de ENTRADA da NF-e de devolução de uma venda feita com esta natureza (só natureza de
    /// Saída, só STANDALONE). A devolução criada pelo "Devolver" herda esta natureza em cada linha.
    /// </summary>
    public int? ReturnUsageCode { get; set; }

    [ForeignKey(nameof(ReturnUsageCode))]
    public Usage? ReturnUsage { get; set; }
```

Em `SiagroB1.Domain/Entities/SalesInvoice.cs`, logo depois de `NfeConfirmationError`:

```csharp

    /// <summary>
    /// Devolução criada pelo "Devolver" de uma venda autorizada: sai com NF-e PRÓPRIA de entrada
    /// (finalidade 4). Só <c>SalesInvoicesNfeReturnCreateService</c> grava <c>true</c>; create e PATCH
    /// da API nunca.
    /// </summary>
    public bool IsNfeReturn { get; set; }
```

Em `SiagroB1.Domain/Entities/SalesInvoiceItem.cs`, logo depois de `CreatesFinancialDocument`:

```csharp

    /// <summary>
    /// <c>det/@nItem</c> com que a linha saiu na NF-e. Gravado na emissão: a NF-e de devolução
    /// referencia o item da venda por este número (<c>DFeReferenciado</c>, regra VC02-14).
    /// </summary>
    public int? NfeItemNumber { get; set; }
```

Em `SiagroB1.Domain/Models/UsageModel.cs`, depois de `CbsRateReduction`:

```csharp

    /// <summary>Natureza de entrada da NF-e de devolução (só natureza de Saída, só STANDALONE).</summary>
    public int? ReturnUsageCode { get; set; }

    /// <summary>Nome da natureza de devolução — só leitura (projeção); o PATCH o ignora.</summary>
    public string? ReturnUsageName { get; set; }
```

- [ ] **Step 4: Mapper, projeção e validações**

Em `UsageTaxationMapper.CopyToEntity`, ao final do método:

```csharp

        // Natureza de entrada não tem natureza de devolução: a coluna fica nula, venha o que vier.
        usage.ReturnUsageCode = incoming ? null : model.ReturnUsageCode;
```

Em `UsageService.QueryAll`, dentro do `new UsageModel()`, logo depois de `CbsRateReduction = x.usage.CbsRateReduction,`:

```csharp
                    ReturnUsageCode = x.usage.ReturnUsageCode,
                    ReturnUsageName = x.usage.ReturnUsage != null ? x.usage.ReturnUsage.Name : null,
```

Em `UsageService.CreateAsync`, depois de `UsageTaxationValidator.Validate(entity);`:

```csharp
        await ValidateReturnUsageAsync(entity, key: null);
```

Em `UsageService.UpdateAsync`, depois de `UsageTaxationValidator.Validate(entity);`:

```csharp
        await ValidateReturnUsageAsync(entity, key);
```

E, ainda em `UpdateAsync`, logo depois do bloco que recusa a troca de tipo de natureza já usada (o `if (newDirection != usage.Direction && ...)`):

```csharp

        // Natureza de entrada que é a devolução de outra natureza viraria uma "devolução" de saída:
        // a devolução criada pelo Devolver passaria a nascer com CFOP de saída.
        if (newDirection == UsageDirection.Outgoing && usage.Direction == UsageDirection.Incoming &&
            await db.Context.Usages.AnyAsync(x => x.ReturnUsageCode == key))
        {
            throw new DefaultException(
                $"A natureza {usage.Name} é a natureza de devolução de outra natureza de saída: " +
                "o tipo não pode virar Saída.");
        }
```

Em `UsageService.DeleteAsync`, depois do `if` que recusa a natureza usada em documento:

```csharp

        // Com a FK de USAGES.ReturnUsageCode o banco recusaria com um 547 sem mensagem de negócio.
        if (await db.Context.Usages.AnyAsync(x => x.ReturnUsageCode == key))
        {
            throw new DefaultException(
                $"Natureza de operação {usage.Name} é a natureza de devolução de outra natureza. " +
                "Inative-a em vez de excluir.");
        }
```

E o método novo, antes de `EntityExists`:

```csharp
    /// <summary>Natureza de devolução (spec §5): só em natureza de Saída e apontando uma de Entrada ativa.</summary>
    private async Task ValidateReturnUsageAsync(UsageModel model, int? key)
    {
        if (model.ReturnUsageCode is not { } returnCode)
            return;

        if (model.Direction == UsageDirection.Incoming)
            throw new DefaultException("Só natureza de saída tem natureza de devolução.");

        if (key == returnCode)
            throw new DefaultException("A natureza de devolução não pode ser a própria natureza.");

        var target = await db.Context.Usages.AsNoTracking().FirstOrDefaultAsync(x => x.Code == returnCode)
                     ?? throw new DefaultException($"Natureza de devolução {returnCode} não encontrada.");

        if (target.Direction != UsageDirection.Incoming)
            throw new DefaultException($"A natureza de devolução {target.Name} precisa ser de entrada.");

        if (target.Inactive)
            throw new DefaultException($"A natureza de devolução {target.Name} está inativa.");
    }
```

- [ ] **Step 5: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Usage"`
Expected: PASS (os novos e os testes de natureza que já existiam).

- [ ] **Step 6: Gerar a migration e ler**

```bash
dotnet build SiagroB1.sln
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add AddSalesReturnNfe --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

Ler o `Up()` gerado. Esperado **só**: `AddColumn<bool>("IsNfeReturn", "SALES_INVOICES", nullable: false, defaultValue: false)`, `AddColumn<int>("NfeItemNumber", "SALES_INVOICES_ITEMS", nullable: true)`, `AddColumn<int>("ReturnUsageCode", "USAGES", nullable: true)`, `CreateIndex("IX_USAGES_ReturnUsageCode")` e `AddForeignKey("FK_USAGES_USAGES_ReturnUsageCode")` sem cascade. Qualquer outra operação = drift do snapshot: pare e investigue antes de seguir. `git add` dos três arquivos gerados.

Run: `ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`
Expected: "No changes have been made to the model since the last migration."

- [ ] **Step 7: Aplicar no banco de desenvolvimento da CEAGUI**

Run: `ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --no-build`
Expected: "Applying migration '..._AddSalesReturnNfe'" e "Done." Confirme com `sqlcmd -S localhost -E -d CEAGUI_SIAGRO_DEV -I -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE COLUMN_NAME IN ('IsNfeReturn','NfeItemNumber','ReturnUsageCode')"` → `3`.

- [ ] **Step 8: Commit**

```bash
git add SiagroB1.Application.Tests/Usages/UsageReturnUsageTests.cs
git commit -F <msg> -- SiagroB1.Domain/Entities/Usage.cs SiagroB1.Domain/Entities/SalesInvoice.cs SiagroB1.Domain/Entities/SalesInvoiceItem.cs SiagroB1.Domain/Models/UsageModel.cs SiagroB1.Application/Services/Taxes/UsageTaxationMapper.cs SiagroB1.Application/Services/UsageService.cs SiagroB1.Application.Tests/Usages/UsageReturnUsageTests.cs SiagroB1.Migrations/AppContext
```

Mensagem: `feat(master-data): natureza de devolução vinculada à natureza de saída` + corpo explicando o vínculo e as três colunas novas + `DB: AddSalesReturnNfe` + trailers.

---

### Task 2: XML da NF-e de devolução no Fiscal

**Files:**
- Modify: `SiagroB1.Fiscal/Nfe/NfeIssueInput.cs`, `SiagroB1.Fiscal/Nfe/NfeXmlBuilder.cs`
- Modify: `SiagroB1.Fiscal.Tests/Support/NfeTestData.cs`
- Test: `SiagroB1.Fiscal.Tests/Nfe/NfeXmlBuilderTests.cs`, `SiagroB1.Fiscal.Tests/Nfe/NfeSignerTests.cs`

**Interfaces:**
- Produces: `enum NfePurpose { Sale, Return }`; `record NfeItemReference(string AccessKey, int ItemNumber)`; `NfeIssueInput.Purpose` (default `Sale`), `NfeIssueInput.ReferencedKeys` (default `[]`), `NfeItem.Reference` (`NfeItemReference?`).

- [ ] **Step 1: Dado de teste da devolução**

Em `SiagroB1.Fiscal.Tests/Support/NfeTestData.cs`, dentro da classe (precisa de `using SiagroB1.Fiscal.Payments;`, que já existe):

```csharp
    /// <summary>Chave da venda que a devolução de teste referencia.</summary>
    public const string SaleAccessKey = "35261012345678000195550010000000981481516230";

    /// <summary>
    /// Devolução da venda de teste pelo cliente de SP (operação interna): entrada, CFOP 1202, PIS/COFINS
    /// de entrada, sem pagamento, com a venda referenciada no cabeçalho e no item.
    /// </summary>
    public static NfeIssueInput ReturnInput() => Input(recipientAddress: SaoPaulo()) with
    {
        OperationNature = "DEVOLUCAO DE VENDA",
        Purpose = NfePurpose.Return,
        ReferencedKeys = [SaleAccessKey],
        Items = [Item() with { Cfop = "1202", PisCst = "72", CofinsCst = "72", Reference = new NfeItemReference(SaleAccessKey, 1) }],
        Payment = new PaymentPlan(PaymentMeansCodes.NoPayment, null, 0m, []),
    };
```

- [ ] **Step 2: Testes que falham**

Em `NfeXmlBuilderTests.cs` (acrescentar `using NFe.Classes.Informacoes.Identificacao;` no topo):

```csharp
    [Fact]
    public void Return_note_is_an_incoming_return_with_the_sale_referenced()
    {
        var nfe = Build(NfeTestData.ReturnInput());

        Assert.Equal(TipoNFe.tnEntrada, nfe.infNFe.ide.tpNF);
        Assert.Equal(FinalidadeNFe.fnDevolucao, nfe.infNFe.ide.finNFe);
        Assert.Equal(NfeTestData.SaleAccessKey, Assert.Single(nfe.infNFe.ide.NFref).refNFe);
    }

    [Fact]
    public void Each_returned_item_references_the_sale_item()
    {
        var det = Assert.Single(Build(NfeTestData.ReturnInput()).infNFe.det);

        Assert.Equal(NfeTestData.SaleAccessKey, det.DFeReferenciado.chaveAcesso);
        Assert.Equal(1, det.DFeReferenciado.nItem);
        Assert.Equal(1202, det.prod.CFOP);
    }

    [Fact]
    public void Return_note_pays_nothing_and_has_no_billing()
    {
        var nfe = Build(NfeTestData.ReturnInput());

        var payment = Assert.Single(Assert.Single(nfe.infNFe.pag).detPag);
        Assert.Equal(90, (int)payment.tPag!);
        Assert.Equal(0m, payment.vPag);
        Assert.Null(nfe.infNFe.cobr);
    }

    [Fact]
    public void Sale_stays_an_outgoing_normal_note_without_references()
    {
        var nfe = Build(NfeTestData.Input());

        Assert.Equal(TipoNFe.tnSaida, nfe.infNFe.ide.tpNF);
        Assert.Equal(FinalidadeNFe.fnNormal, nfe.infNFe.ide.finNFe);
        Assert.Null(nfe.infNFe.ide.NFref);
        Assert.Null(Assert.Single(nfe.infNFe.det).DFeReferenciado);
    }
```

Em `NfeSignerTests.cs`:

```csharp
    [Fact]
    public void Return_note_validates_against_the_official_schema()
    {
        using var certificate = Certificate();

        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.ReturnInput(), Settings(certificate));

        Assert.Contains("<tpNF>0</tpNF>", signed.Xml);
        Assert.Contains("<finNFe>4</finNFe>", signed.Xml);
        Assert.Contains($"<NFref><refNFe>{NfeTestData.SaleAccessKey}</refNFe></NFref>", signed.Xml);
        Assert.Contains($"<DFeReferenciado><chaveAcesso>{NfeTestData.SaleAccessKey}</chaveAcesso><nItem>1</nItem></DFeReferenciado>", signed.Xml);
        Assert.Contains("<tPag>90</tPag>", signed.Xml);
        Assert.DoesNotContain("<cobr>", signed.Xml);
    }
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~NfeXmlBuilderTests|FullyQualifiedName~NfeSignerTests"`
Expected: FAIL de compilação — `NfePurpose`, `NfeItemReference`, `Purpose`, `ReferencedKeys`, `Reference` não existem.

- [ ] **Step 4: Entrada do montador**

Em `SiagroB1.Fiscal/Nfe/NfeIssueInput.cs`, antes de `public sealed record NfeItem`:

```csharp
/// <summary>Finalidade da NF-e: venda (saída, <c>finNFe</c> 1) ou devolução de venda (entrada, <c>finNFe</c> 4).</summary>
public enum NfePurpose
{
    Sale,
    Return,
}

/// <summary>Item da nota original que este item devolve (<c>det/DFeReferenciado</c>, regra VC02-14).</summary>
public sealed record NfeItemReference(string AccessKey, int ItemNumber);
```

Em `NfeItem`, depois de `Cest`:

```csharp

    /// <summary>Na devolução, o item da venda devolvido; nulo na venda.</summary>
    public NfeItemReference? Reference { get; init; }
```

Em `NfeIssueInput`, depois de `RandomCode`:

```csharp

    public NfePurpose Purpose { get; init; } = NfePurpose.Sale;

    /// <summary><c>ide/NFref/refNFe</c> — na devolução, a chave da venda (rejeição 321 sem ela).</summary>
    public IReadOnlyList<string> ReferencedKeys { get; init; } = [];
```

- [ ] **Step 5: Builder**

Em `NfeXmlBuilder.BuildIde`, trocar `tpNF = TipoNFe.tnSaida,` por:

```csharp
        tpNF = input.Purpose == NfePurpose.Return ? TipoNFe.tnEntrada : TipoNFe.tnSaida,
```

trocar `finNFe = FinalidadeNFe.fnNormal,` por:

```csharp
        finNFe = input.Purpose == NfePurpose.Return ? FinalidadeNFe.fnDevolucao : FinalidadeNFe.fnNormal,
```

e, depois de `verProc = ...,`:

```csharp
        NFref = input.ReferencedKeys.Count == 0
            ? null
            : input.ReferencedKeys.Select(key => new NFref { refNFe = key }).ToList(),
```

Em `BuildItem`, depois do bloco `imposto = new imposto { ... },`:

```csharp
        // VC02-14: cada item da devolução aponta o item da venda (chave + nItem da nota original).
        DFeReferenciado = item.Reference is { } reference
            ? new DFeReferenciado { chaveAcesso = reference.AccessKey, nItem = reference.ItemNumber }
            : null,
```

(`NFref` está em `NFe.Classes.Informacoes.Identificacao` e `DFeReferenciado` em `NFe.Classes.Informacoes.Detalhe`, os dois já importados. **Não** importar `NFe.Classes.Servicos.Evento.Informacoes.ItemConsumo`, que tem outra classe `DFeReferenciado`.) O pagamento 90 e a ausência de `cobr` já saem do `PaymentPlan` sem parcelas — nada a mudar ali.

- [ ] **Step 6: Rodar e ver passar**

Run: `dotnet test SiagroB1.Fiscal.Tests`
Expected: PASS em todos (171 anteriores + 5 novos).

- [ ] **Step 7: Commit**

```bash
git commit -F <msg> -- SiagroB1.Fiscal/Nfe/NfeIssueInput.cs SiagroB1.Fiscal/Nfe/NfeXmlBuilder.cs SiagroB1.Fiscal.Tests/Support/NfeTestData.cs SiagroB1.Fiscal.Tests/Nfe/NfeXmlBuilderTests.cs SiagroB1.Fiscal.Tests/Nfe/NfeSignerTests.cs
```

Mensagem: `feat(invoice): XML da NF-e de devolução com nota e item referenciados` (corpo: tpNF 0, finNFe 4, NFref, DFeReferenciado da VC02-14).

---

### Task 3: Número do item da NF-e gravado na emissão

**Files:**
- Create: `SiagroB1.Application/Services/Nfe/SalesInvoiceNfeItemNumbering.cs`
- Modify: `SiagroB1.Application/Services/Nfe/NfeIssueInputAssembler.cs`, `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeIssueService.cs`
- Test: `SiagroB1.Application.Tests/Nfe/SalesInvoiceNfeItemNumberingTests.cs` (novo), `SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeIssueServiceTests.cs`

**Interfaces:**
- Consumes: `SalesInvoiceItem.NfeItemNumber` (Task 1).
- Produces: `SalesInvoiceNfeItemNumbering.Ordered(IEnumerable<SalesInvoiceItem>) : IReadOnlyList<SalesInvoiceItem>`, `.Renumber(IEnumerable<SalesInvoiceItem>)`, `.OriginNumber(SalesInvoiceItem originItem, int originItemCount) : int?`.

- [ ] **Step 1: Testes que falham**

Criar `SiagroB1.Application.Tests/Nfe/SalesInvoiceNfeItemNumberingTests.cs` (`git add`):

```csharp
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary><c>det/@nItem</c> fixo (spec §8.2): gravado na emissão, 1..n sem buracos, ordem estável.</summary>
public class SalesInvoiceNfeItemNumberingTests
{
    private static SalesInvoiceItem Line(string key, int? number = null) => new()
    {
        Key = Guid.Parse(key), ItemCode = "SOJA", UnitOfMeasureCode = "KG", NfeItemNumber = number,
    };

    [Fact]
    public void New_lines_are_numbered_in_key_order()
    {
        var b = Line("00000000-0000-0000-0000-000000000002");
        var a = Line("00000000-0000-0000-0000-000000000001");

        SalesInvoiceNfeItemNumbering.Renumber([b, a]);

        Assert.Equal(1, a.NfeItemNumber);
        Assert.Equal(2, b.NfeItemNumber);
    }

    [Fact]
    public void Renumbering_keeps_the_previous_order_and_closes_gaps()
    {
        var third = Line("00000000-0000-0000-0000-000000000001", 3);
        var first = Line("00000000-0000-0000-0000-000000000009", 1);
        var added = Line("00000000-0000-0000-0000-000000000005");

        SalesInvoiceNfeItemNumbering.Renumber([third, first, added]);

        Assert.Equal(1, first.NfeItemNumber);
        Assert.Equal(2, third.NfeItemNumber);
        Assert.Equal(3, added.NfeItemNumber);
    }

    [Fact]
    public void Legacy_sale_with_a_single_line_counts_as_item_1()
    {
        Assert.Equal(1, SalesInvoiceNfeItemNumbering.OriginNumber(Line("00000000-0000-0000-0000-000000000001"), 1));
    }

    [Fact]
    public void Legacy_sale_with_several_lines_has_no_known_number()
    {
        Assert.Null(SalesInvoiceNfeItemNumbering.OriginNumber(Line("00000000-0000-0000-0000-000000000001"), 2));
    }

    [Fact]
    public void Numbered_sale_line_returns_its_number()
    {
        Assert.Equal(2, SalesInvoiceNfeItemNumbering.OriginNumber(Line("00000000-0000-0000-0000-000000000001", 2), 3));
    }
}
```

Em `SalesInvoicesNfeIssueServiceTests.cs`, acrescentar:

```csharp
    [Fact]
    public async Task Item_numbers_are_saved_with_the_signed_xml()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        await Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db)).ExecuteAsync(scenario.InvoiceKey, "tester");

        var item = await TestDb.CreateUnitOfWork(scenario.DatabaseName).Context.SalesInvoicesItems.AsNoTracking()
            .SingleAsync(i => i.SalesInvoiceKey == scenario.InvoiceKey);
        Assert.Equal(1, item.NfeItemNumber);
    }
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoiceNfeItemNumberingTests|FullyQualifiedName~SalesInvoicesNfeIssueServiceTests"`
Expected: FAIL de compilação (`SalesInvoiceNfeItemNumbering` não existe).

- [ ] **Step 3: Implementar**

Criar `SiagroB1.Application/Services/Nfe/SalesInvoiceNfeItemNumbering.cs` (`git add`):

```csharp
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// O <c>det/@nItem</c> de cada linha (spec §8.2). Numerado na emissão e GRAVADO na linha: a NF-e de
/// devolução referencia o item da venda por esse número (<c>DFeReferenciado</c>), então ele tem de ser
/// o mesmo do XML autorizado. Renumera 1..n a cada tentativa — antes da autorização linhas ainda
/// entram e saem, e a sequência não pode ter buracos — mantendo a ordem anterior.
/// </summary>
/// <remarks>
/// A ordem é a do número já gravado e, para as linhas novas, a da <c>Key</c> comparada em MEMÓRIA
/// (<see cref="Guid"/> do .NET): o SQL Server ordena <c>uniqueidentifier</c> de outro jeito.
/// </remarks>
public static class SalesInvoiceNfeItemNumbering
{
    public static IReadOnlyList<SalesInvoiceItem> Ordered(IEnumerable<SalesInvoiceItem> items) =>
        items.OrderBy(i => i.NfeItemNumber ?? int.MaxValue).ThenBy(i => i.Key).ToList();

    public static void Renumber(IEnumerable<SalesInvoiceItem> items)
    {
        var ordered = Ordered(items);

        for (var i = 0; i < ordered.Count; i++)
            ordered[i].NfeItemNumber = i + 1;
    }

    /// <summary>
    /// Número da linha vendida na NF-e de venda. Venda autorizada antes desta numeração existir: com
    /// um item só, ele é o 1; com vários, não há como saber — nulo.
    /// </summary>
    public static int? OriginNumber(SalesInvoiceItem originItem, int originItemCount) =>
        originItem.NfeItemNumber ?? (originItemCount == 1 ? 1 : null);
}
```

Em `NfeIssueInputAssembler.Build`, trocar `var items = invoice.Items.ToList();` por:

```csharp
        var items = SalesInvoiceNfeItemNumbering.Ordered(invoice.Items);
```

e trocar `Items = items.Select((item, index) => ToItem(item, index + 1) with` por:

```csharp
            Items = items.Select((item, index) => ToItem(item, item.NfeItemNumber ?? index + 1) with
```

Em `SalesInvoicesNfeIssueService.ExecuteAsync`, logo depois de `await ReserveNumberAsync(invoice, context.Settings);`:

```csharp

        // nItem fixo e gravado junto com o XML assinado (o SaveChanges do "Em processamento" ou da
        // rejeição local leva os números): a devolução referencia o item da venda por ele.
        SalesInvoiceNfeItemNumbering.Renumber(invoice.Items);
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Nfe"`
Expected: PASS (novos e os testes de NF-e existentes).

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application/Services/Nfe/SalesInvoiceNfeItemNumbering.cs SiagroB1.Application.Tests/Nfe/SalesInvoiceNfeItemNumberingTests.cs
git commit -F <msg> -- SiagroB1.Application/Services/Nfe/SalesInvoiceNfeItemNumbering.cs SiagroB1.Application/Services/Nfe/NfeIssueInputAssembler.cs SiagroB1.Application/Services/Nfe/SalesInvoicesNfeIssueService.cs SiagroB1.Application.Tests/Nfe/SalesInvoiceNfeItemNumberingTests.cs SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeIssueServiceTests.cs
```

Mensagem: `feat(invoice): número do item da NF-e gravado na emissão`.

---

### Task 4: Cálculo e conferência da devolução própria

**Files:**
- Create: `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeReturnConference.cs`
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesTaxApplyService.cs`
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesNfeReturnTaxApplyTests.cs` (novo)

**Interfaces:**
- Consumes: `SalesInvoice.IsNfeReturn`, `UsageModel` (Task 1).
- Produces: `SalesInvoicesTaxApplyService.IsOwnNfeReturn(SalesInvoice) : bool` (static, public); `IsActiveForAsync` passa a aceitar a devolução própria; `SalesInvoiceNfeReturnConference.Ensure(SalesInvoiceItem returned, SalesInvoiceItem sold, string usageName)`.

- [ ] **Step 1: Testes que falham**

Criar `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesNfeReturnTaxApplyTests.cs` (`git add`):

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Devolução própria (spec §7): o mesmo motor da venda com a natureza de ENTRADA, IBS/CBS pela data
/// da venda e a conferência contra a linha vendida.
/// </summary>
public class SalesInvoicesNfeReturnTaxApplyTests
{
    private const string CardBa = "C-BA";
    private const string CardSp = "C-SP";

    private static FakeBusinessPartnerService Partners() => new(
        names: new() { [CardBa] = "CLIENTE BA", [CardSp] = "CLIENTE SP" },
        states: new() { [CardBa] = "BA", [CardSp] = "SP" });

    private sealed record Scenario(UnitOfWork Db, int SaleUsage, int ReturnUsage, SalesInvoice Sale);

    /// <summary>Venda de grãos com ICMS 51 + cBenef e IBS/CBS 200, já calculada e confirmada.</summary>
    private static async Task<Scenario> SeedAsync(
        string card = CardSp, Action<UsageModel>? tweakReturn = null, bool withLaterRate = false)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "CEAGUI", StateCode = "SP", TaxRegime = TaxRegime.Normal, IssuesNfe = true });
        db.Context.Items.Add(new Item { ItemCode = "SOJA", ItemName = "SOJA", GoodsOrigin = 0, Ncm = "12019000" });
        db.Context.IbsCbsRates.Add(new IbsCbsRate { StartDate = new DateOnly(2026, 1, 1), CbsRate = 0.9m, IbsStateRate = 0.1m, IbsMunicipalRate = 0m });
        if (withLaterRate)
            db.Context.IbsCbsRates.Add(new IbsCbsRate { StartDate = new DateOnly(2026, 10, 5), CbsRate = 1.0m, IbsStateRate = 0.2m, IbsMunicipalRate = 0m });
        await db.SaveChangesAsync();

        var usages = new UsageService(db, NullLogger<UsageService>.Instance);
        var returnModel = new UsageModel
        {
            Name = "Entrada devolução", Direction = UsageDirection.Incoming,
            CfopIncomingInState = "1202", CfopIncomingOutState = "2202",
            IcmsInStateCst = "51", IcmsInStateRate = 18m, IcmsInStateDeferral = 100m, IcmsInStateBenefitCode = "SP053521",
            IcmsOutStateCst = "00", PisCst = "72", CofinsCst = "72",
            IbsCbsCst = "200", IbsCbsClassCode = "200036", IbsRateReduction = 60m, CbsRateReduction = 60m,
            RequiresQuantity = true,
        };
        tweakReturn?.Invoke(returnModel);
        var ret = await usages.CreateAsync(returnModel);

        var sale = await usages.CreateAsync(new UsageModel
        {
            Name = "Venda suspensão", Direction = UsageDirection.Outgoing,
            CfopOutgoingInState = "5102", CfopOutgoingOutState = "6102",
            IcmsInStateCst = "51", IcmsInStateRate = 18m, IcmsInStateDeferral = 100m, IcmsInStateBenefitCode = "SP053521",
            IcmsOutStateCst = "00", PisCst = "09", CofinsCst = "09",
            IbsCbsCst = "200", IbsCbsClassCode = "200036", IbsRateReduction = 60m, CbsRateReduction = 60m,
            RequiresQuantity = true, ReturnUsageCode = ret.Code,
        });

        var origin = new SalesInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = card, InvoiceDate = new DateTime(2026, 10, 1),
            Items = [new SalesInvoiceItem { Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 30000m, UnitPrice = 2m, UsageCode = sale.Code }],
        };
        await TaxTestServices.Apply(db, Partners()).ApplyAsync(origin, origin.Items);
        origin.InvoiceStatus = InvoiceStatus.Confirmed;
        db.Context.SalesInvoices.Add(origin);
        await db.SaveChangesAsync();

        return new Scenario(db, sale.Code, ret.Code, origin);
    }

    private static SalesInvoice OwnReturn(Scenario s, decimal quantity = 30000m, int? usageCode = -1) => new()
    {
        Key = Guid.NewGuid(), BranchCode = "01", CardCode = s.Sale.CardCode, InvoiceDate = new DateTime(2026, 10, 10),
        InvoiceType = SalesInvoiceType.Return, IsNfeReturn = true, SalesInvoiceOriginKey = s.Sale.Key,
        Items =
        [
            new SalesInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = quantity, UnitPrice = 2m,
                UsageCode = usageCode == -1 ? s.ReturnUsage : usageCode,
                SalesInvoiceItemOriginKey = s.Sale.Items.Single().Key,
            },
        ],
    };

    private static async Task<SalesInvoiceItem> Apply(Scenario s, SalesInvoice invoice)
    {
        await TaxTestServices.Apply(s.Db, Partners()).ApplyAsync(invoice, invoice.Items);
        return invoice.Items.Single();
    }

    private static async Task<string> Rejects(Scenario s, SalesInvoice invoice)
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Apply(s, invoice));
        return ex.Message;
    }

    [Fact]
    public async Task Own_return_in_state_uses_the_incoming_cfop_and_the_return_usage()
    {
        var s = await SeedAsync();

        var item = await Apply(s, OwnReturn(s));

        Assert.Equal("1202", item.Cfop);
        Assert.Equal("51", item.CstIcms);
        Assert.Equal("72", item.CstPis);
        Assert.Equal("Entrada devolução", item.UsageName);
    }

    [Fact]
    public async Task Own_return_out_of_state_uses_the_interstate_incoming_cfop()
    {
        var s = await SeedAsync(CardBa);

        Assert.Equal("2202", (await Apply(s, OwnReturn(s))).Cfop);
    }

    [Fact]
    public async Task Ibs_cbs_rates_come_from_the_sale_date()
    {
        var s = await SeedAsync(withLaterRate: true);

        var item = await Apply(s, OwnReturn(s));

        Assert.Equal(0.9m, item.CbsRate);
        Assert.Equal(0.1m, item.IbsStateRate);
    }

    [Fact]
    public async Task Partial_return_values_follow_the_returned_quantity()
    {
        var s = await SeedAsync();

        var item = await Apply(s, OwnReturn(s, quantity: 10000m));

        Assert.Equal(20000m, item.IcmsBase);
        Assert.Equal(3600m, item.IcmsOperationValue);
    }

    [Fact]
    public async Task Outgoing_usage_on_an_own_return_is_rejected()
    {
        var s = await SeedAsync();

        Assert.Contains("é de saída e não pode ser usada na devolução", await Rejects(s, OwnReturn(s, usageCode: s.SaleUsage)));
    }

    [Fact]
    public async Task Own_return_line_without_usage_is_rejected()
    {
        var s = await SeedAsync();

        Assert.Contains("está sem natureza de devolução", await Rejects(s, OwnReturn(s, usageCode: null)));
    }

    [Fact]
    public async Task Different_icms_rate_is_refused_by_the_conference()
    {
        var s = await SeedAsync(tweakReturn: u => u.IcmsInStateRate = 12m);

        Assert.Equal(
            "Item SOJA: a natureza de devolução Entrada devolução não reproduz a tributação da venda — alíquota do ICMS: venda 18, devolução 12.",
            await Rejects(s, OwnReturn(s)));
    }

    [Fact]
    public async Task Missing_benefit_code_is_refused_by_the_conference()
    {
        var s = await SeedAsync(tweakReturn: u => u.IcmsInStateBenefitCode = null);

        Assert.Contains("cBenef: venda SP053521, devolução (vazio)", await Rejects(s, OwnReturn(s)));
    }

    [Fact]
    public async Task Different_ibs_cbs_class_is_refused_by_the_conference()
    {
        var s = await SeedAsync(tweakReturn: u => u.IbsCbsClassCode = "200034");

        Assert.Contains("classificação do IBS/CBS: venda 200036, devolução 200034", await Rejects(s, OwnReturn(s)));
    }

    [Fact]
    public async Task Return_without_the_flag_is_still_skipped()
    {
        var s = await SeedAsync();
        var plain = OwnReturn(s);
        plain.IsNfeReturn = false;
        plain.Items.Single().IcmsValue = 123m;

        Assert.Equal(123m, (await Apply(s, plain)).IcmsValue);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoicesNfeReturnTaxApplyTests"`
Expected: FAIL — os testes da devolução própria falham (a devolução é pulada; os valores ficam vazios) e as recusas não acontecem.

- [ ] **Step 3: A conferência**

Criar `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeReturnConference.cs` (`git add`):

```csharp
using System.Globalization;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Conferência da devolução própria contra a linha vendida (spec §7, abordagem A): a devolução é
/// calculada pela natureza de entrada, e esta conferência garante que o resultado reproduz a
/// tributação da venda. PIS/COFINS ficam de fora de propósito: na entrada o CST é outro.
/// </summary>
public static class SalesInvoiceNfeReturnConference
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static void Ensure(SalesInvoiceItem returned, SalesInvoiceItem sold, string usageName)
    {
        Text(returned, usageName, "CST do ICMS", sold.CstIcms, returned.CstIcms);
        Number(returned, usageName, "alíquota do ICMS", sold.IcmsRate, returned.IcmsRate);
        Number(returned, usageName, "redução da base do ICMS", sold.IcmsBaseReduction, returned.IcmsBaseReduction);
        Number(returned, usageName, "diferimento do ICMS", sold.IcmsDeferral, returned.IcmsDeferral);
        Text(returned, usageName, "cBenef", sold.IcmsBenefitCode, returned.IcmsBenefitCode);
        Text(returned, usageName, "CST do IBS/CBS", sold.IbsCbsCst, returned.IbsCbsCst);
        Text(returned, usageName, "classificação do IBS/CBS", sold.IbsCbsClassCode, returned.IbsCbsClassCode);
        Number(returned, usageName, "alíquota da CBS", sold.CbsRate, returned.CbsRate);
        Number(returned, usageName, "redução da CBS", sold.CbsRateReduction, returned.CbsRateReduction);
        Number(returned, usageName, "alíquota do IBS estadual", sold.IbsStateRate, returned.IbsStateRate);
        Number(returned, usageName, "alíquota do IBS municipal", sold.IbsMunicipalRate, returned.IbsMunicipalRate);
        Number(returned, usageName, "redução do IBS", sold.IbsRateReduction, returned.IbsRateReduction);
    }

    private static void Text(SalesInvoiceItem returned, string usageName, string field, string? sale, string? ret)
    {
        var a = Blank(sale);
        var b = Blank(ret);

        if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            Fail(returned, usageName, field, a ?? "(vazio)", b ?? "(vazio)");
    }

    private static void Number(SalesInvoiceItem returned, string usageName, string field, decimal sale, decimal ret)
    {
        if (sale != ret)
            Fail(returned, usageName, field, sale.ToString("0.####", PtBr), ret.ToString("0.####", PtBr));
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Fail(SalesInvoiceItem returned, string usageName, string field, string sale, string ret) =>
        throw new DefaultException(
            $"Item {returned.ItemCode}: a natureza de devolução {usageName} não reproduz a tributação da venda — " +
            $"{field}: venda {sale}, devolução {ret}.");
}
```

- [ ] **Step 4: O cálculo da devolução própria**

Em `SalesInvoicesTaxApplyService`, trocar `IsActiveForAsync` e `ApplyAsync` por:

```csharp
    public async Task<bool> IsActiveForAsync(SalesInvoice invoice) =>
        (invoice.InvoiceType == SalesInvoiceType.Normal || IsOwnNfeReturn(invoice))
        && await gate.IsActiveAsync(invoice.BranchCode);

    /// <summary>Devolução criada pelo "Devolver" da venda: sai com NF-e própria de entrada (spec §7).</summary>
    public static bool IsOwnNfeReturn(SalesInvoice invoice) =>
        invoice.InvoiceType == SalesInvoiceType.Return && invoice.IsNfeReturn;

    public async Task ApplyAsync(SalesInvoice invoice, IEnumerable<SalesInvoiceItem> items)
    {
        if (invoice.InvoiceStatus is not (null or InvoiceStatus.Pending))
            return;

        if (!await IsActiveForAsync(invoice))
            return;

        var lines = items.ToList();
        var ownReturn = IsOwnNfeReturn(invoice);
        var branch = await LoadBranchAsync(invoice.BranchCode);
        var customerState = await LoadCustomerStateAsync(invoice.CardCode);
        var inState = string.Equals(branch.StateCode, customerState, StringComparison.OrdinalIgnoreCase);

        // Devolução: IBS/CBS pelas alíquotas da DATA DA VENDA — ela anula o tributo daquela operação,
        // inclusive se a vigência virou entre a venda e a devolução.
        var rateDate = ownReturn
            ? await OriginDateAsync(invoice)
            : DateOnly.FromDateTime(invoice.InvoiceDate ?? DateTime.Today);
        var origins = ownReturn ? await LoadOriginItemsAsync(lines) : new Dictionary<Guid, SalesInvoiceItem>();
        var usages = new Dictionary<int, UsageModel>();

        foreach (var item in lines)
        {
            var usage = await ResolveUsageAsync(item, usages, ownReturn);
            var product = await LoadProductAsync(item.ItemCode);
            var cfop = ResolveCfop(usage, inState, incoming: ownReturn);
            var icms = ResolveIcmsRule(usage, inState, branch.TaxRegime!.Value);
            var pisCofins = ResolvePisCofinsRule(usage);
            var (ibsCbs, rates) = await ResolveIbsCbsAsync(usage, rateDate);

            var result = TaxCalculator.Calculate(new TaxCalculationInput(
                item.Total, inState, branch.StateCode!, customerState, branch.TaxRegime!.Value,
                product.GoodsOrigin!.Value, icms, pisCofins, ibsCbs, rates));

            item.UsageCode = usage.Code;
            item.UsageName = usage.Name;
            item.Cfop = cfop;
            item.Ncm = product.Ncm;
            item.GoodsOrigin = product.GoodsOrigin;
            item.MovesFiscalInventory = usage.MovesFiscalInventory;
            item.CreatesFinancialDocument = usage.CreatesFinancialDocument;

            SalesInvoiceTaxSnapshot.Write(item, result);

            if (ownReturn)
                SalesInvoiceNfeReturnConference.Ensure(item, OriginOf(item, origins), usage.Name);
        }
    }

    private async Task<DateOnly> OriginDateAsync(SalesInvoice invoice)
    {
        var date = await db.Context.SalesInvoices.AsNoTracking()
            .Where(i => i.Key == invoice.SalesInvoiceOriginKey)
            .Select(i => i.InvoiceDate)
            .FirstOrDefaultAsync();

        return DateOnly.FromDateTime(date ?? throw new DefaultException("A devolução está sem a venda de origem."));
    }

    private async Task<Dictionary<Guid, SalesInvoiceItem>> LoadOriginItemsAsync(IEnumerable<SalesInvoiceItem> lines)
    {
        var keys = lines.Where(l => l.SalesInvoiceItemOriginKey != null)
            .Select(l => l.SalesInvoiceItemOriginKey!.Value).Distinct().ToList();

        return await db.Context.SalesInvoicesItems.AsNoTracking()
            .Where(i => i.Key != null && keys.Contains(i.Key.Value))
            .ToDictionaryAsync(i => i.Key!.Value);
    }

    private static SalesInvoiceItem OriginOf(SalesInvoiceItem item, Dictionary<Guid, SalesInvoiceItem> origins) =>
        item.SalesInvoiceItemOriginKey is { } key && origins.TryGetValue(key, out var origin)
            ? origin
            : throw new DefaultException($"O item {item.ItemCode} da devolução não aponta um item da venda.");
```

Trocar `ResolveUsageAsync` por:

```csharp
    private async Task<UsageModel> ResolveUsageAsync(
        SalesInvoiceItem item, Dictionary<int, UsageModel> cache, bool ownReturn)
    {
        UsageModel usage;

        if (item.UsageCode is { } code)
        {
            if (!cache.TryGetValue(code, out usage!))
            {
                usage = await usageService.GetByIdAsync(code)
                        ?? throw new DefaultException("Natureza de operação não encontrada.");
                cache[code] = usage;
            }
        }
        else if (ownReturn)
        {
            // A linha da devolução nasce com a natureza de devolução da linha vendida; sem ela, a
            // natureza padrão (de SAÍDA) daria CFOP de venda numa nota de entrada.
            throw new DefaultException($"O item {item.ItemCode} da devolução está sem natureza de devolução.");
        }
        else
        {
            usage = (await usageService.GetAllAsync()).FirstOrDefault(u => u is { IsDefault: true, Inactive: false })
                    ?? throw new DefaultException(
                        $"O item {item.ItemCode} está sem natureza de operação e não há natureza padrão cadastrada.");
        }

        if (ownReturn && usage.Direction != UsageDirection.Incoming)
            throw new DefaultException(
                $"A natureza de operação {usage.Name} é de saída e não pode ser usada na devolução.");

        if (!ownReturn && usage.Direction == UsageDirection.Incoming)
            throw new DefaultException(
                $"A natureza de operação {usage.Name} é de entrada e não pode ser usada no documento de saída.");

        if (usage.Inactive)
            throw new DefaultException($"Natureza de operação {usage.Name} está inativa.");

        return usage;
    }
```

Trocar `ResolveCfop` por:

```csharp
    private static string ResolveCfop(UsageModel usage, bool inState, bool incoming)
    {
        var cfop = incoming
            ? inState ? usage.CfopIncomingInState : usage.CfopIncomingOutState
            : inState ? usage.CfopOutgoingInState : usage.CfopOutgoingOutState;
        var kind = incoming ? "entrada" : "saída";

        return string.IsNullOrWhiteSpace(cfop)
            ? throw new DefaultException(inState
                ? $"Natureza de operação {usage.Name} está sem CFOP de {kind} dentro do estado."
                : $"Natureza de operação {usage.Name} está sem CFOP de {kind} fora do estado.")
            : cfop;
    }
```

E atualizar o resumo da classe (`/// Só age com a regra ativa ..., em documento Normal e ...`) para "em documento Normal ou na devolução própria (InvoiceType Return + IsNfeReturn)".

- [ ] **Step 5: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoicesNfeReturnTaxApplyTests|FullyQualifiedName~SalesInvoicesTaxApplyServiceTests|FullyQualifiedName~SalesInvoicesCreateTaxationTests"`
Expected: PASS (as mensagens de saída ficaram idênticas).

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeReturnConference.cs SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesNfeReturnTaxApplyTests.cs
git commit -F <msg> -- SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeReturnConference.cs SiagroB1.Application/Services/SalesInvoices/SalesInvoicesTaxApplyService.cs SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesNfeReturnTaxApplyTests.cs
```

Mensagem: `feat(invoice): tributos da devolução pela natureza de entrada, conferidos com a venda`.

---

### Task 5: "Devolver" — criar a devolução própria

**Files:**
- Create: `SiagroB1.Application/Services/Nfe/SalesInvoiceNfeReturnBalance.cs`, `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeReturnCreateService.cs`, `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeReturnableItemsService.cs`, `SiagroB1.Domain/Dtos/SalesInvoiceNfeReturnableItemDto.cs`
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCreateService.cs`
- Create (teste): `SiagroB1.Application.Tests/Support/NfeReturnTestSeed.cs`
- Test: `SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeReturnCreateServiceTests.cs` (novo)

**Interfaces:**
- Consumes: `SalesInvoiceNfeItemNumbering.OriginNumber` (Task 3), `SalesInvoicesTaxApplyService` (Task 4).
- Produces:
  - `record SalesInvoiceNfeReturnItem(Guid OriginItemKey, decimal Quantity)`; `record SalesInvoiceNfeReturnRequest(Guid SalesInvoiceKey, IReadOnlyList<SalesInvoiceNfeReturnItem> Items, string Reason)`;
  - `SalesInvoicesNfeReturnCreateService(IUnitOfWork, TaxCalculationGate, SalesInvoicesCreateService, ILogger<…>, Func<DateTimeOffset>? clock = null, TimeZoneInfo? storageZone = null).ExecuteAsync(SalesInvoiceNfeReturnRequest, string userName) : Task<SalesInvoice>`;
  - `SalesInvoicesNfeReturnableItemsService(IUnitOfWork).ExecuteAsync(Guid key) : Task<IReadOnlyList<SalesInvoiceNfeReturnableItemDto>>`;
  - `SalesInvoiceNfeReturnBalance.ReturnedByOriginItemAsync(AppDbContext, Guid originInvoiceKey, Guid? excludingReturnKey) : Task<Dictionary<Guid, decimal>>` e `.EnsureWithinAsync(AppDbContext, SalesInvoice returnInvoice, IEnumerable<SalesInvoiceItem> lines) : Task`;
  - `SalesInvoicesCreateService.ExecuteAsync(SalesInvoice, string userName, CommitMode commitMode = Auto, bool nfeReturn = false)`;
  - teste: `NfeReturnTestSeed.SeedAsync() : Task<NfeReturnScenario>`, `NfeReturnTestSeed.CreateService(UnitOfWork, string erp = "STANDALONE")`.

- [ ] **Step 1: Semente de teste reaproveitável**

Criar `SiagroB1.Application.Tests/Support/NfeReturnTestSeed.cs` (`git add`):

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Support;

public sealed record NfeReturnScenario(NfeScenario Sale, int ReturnUsageCode, Guid SaleItemKey);

/// <summary>
/// A venda do <see cref="NfeTestSeed"/> já AUTORIZADA (série 1 nº 1) e confirmada, com uma natureza
/// de devolução de entrada (1202/2202) que reproduz a tributação dela, vinculada à natureza da venda.
/// </summary>
public static class NfeReturnTestSeed
{
    public const string SaleAccessKey = "35261012345678000195550010000000011481516230";

    public static async Task<NfeReturnScenario> SeedAsync()
    {
        var sale = await NfeTestSeed.SeedAsync();
        var context = sale.Db.Context;

        context.Items.Add(new Item { ItemCode = "SOJA", ItemName = "SOJA EM GRAOS", Ncm = "12019000", GoodsOrigin = 0 });
        context.IbsCbsRates.Add(new IbsCbsRate { StartDate = new DateOnly(2026, 1, 1), CbsRate = 0.9m, IbsStateRate = 0.1m, IbsMunicipalRate = 0m });
        await sale.Db.SaveChangesAsync();

        var returnUsage = await new UsageService(sale.Db, NullLogger<UsageService>.Instance).CreateAsync(new UsageModel
        {
            Name = "Entrada devolução", Direction = UsageDirection.Incoming,
            CfopIncomingInState = "1202", CfopIncomingOutState = "2202", InvoiceOperationText = "DEVOLUCAO DE VENDA",
            IcmsInStateCst = "00", IcmsInStateRate = 18m, IcmsOutStateCst = "00",
            PisCst = "72", CofinsCst = "72", IbsCbsCst = "000", IbsCbsClassCode = "000001",
            RequiresQuantity = true,
        });

        var saleUsage = await context.Usages.SingleAsync(u => u.Name == "Venda de grãos");
        saleUsage.ReturnUsageCode = returnUsage.Code;

        var invoice = await context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == sale.InvoiceKey);
        invoice.InvoiceStatus = InvoiceStatus.Confirmed;
        invoice.NfeStatus = NfeStatus.Authorized;
        invoice.TaxDocumentNumber = "000000001";
        invoice.TaxDocumentSeries = "1";
        invoice.ChaveNFe = SaleAccessKey;
        invoice.NfeRandomCode = "48151623";
        invoice.Items.Single().NfeItemNumber = 1;
        await sale.Db.SaveChangesAsync();

        return new NfeReturnScenario(sale, returnUsage.Code, invoice.Items.Single().Key!.Value);
    }

    public static SalesInvoicesNfeReturnCreateService CreateService(UnitOfWork db, string erp = "STANDALONE")
    {
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);
        var partners = new FakeBusinessPartnerService(
            names: new() { [NfeTestSeed.CardCode] = "CLIENTE BA LTDA" },
            states: new() { [NfeTestSeed.CardCode] = "BA" });

        var create = new SalesInvoicesCreateService(
            db, partners, new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }),
            new FakeDocNumberSequenceService(), new SalesInvoicesUsageGuardService(usages),
            new SalesInvoicesCfopResolveService(db, usages, partners), TaxTestServices.Apply(db, partners, erp),
            NullLogger<SalesInvoicesCreateService>.Instance);

        return new SalesInvoicesNfeReturnCreateService(
            db, TaxTestServices.Gate(db, erp), create, NullLogger<SalesInvoicesNfeReturnCreateService>.Instance,
            NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone);
    }

    public static Task<SalesInvoice> CreateReturnAsync(NfeReturnScenario scenario, decimal quantity, string reason = "Carga recusada") =>
        CreateService(scenario.Sale.Db).ExecuteAsync(
            new SalesInvoiceNfeReturnRequest(
                scenario.Sale.InvoiceKey, [new SalesInvoiceNfeReturnItem(scenario.SaleItemKey, quantity)], reason),
            "tester");
}
```

- [ ] **Step 2: Testes que falham**

Criar `SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeReturnCreateServiceTests.cs` (`git add`):

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>"Devolver" (spec §6): cria a devolução própria Pendente a partir da venda autorizada.</summary>
public class SalesInvoicesNfeReturnCreateServiceTests
{
    private static async Task<string> Rejects(NfeReturnScenario s, decimal quantity = 30000m, string reason = "Carga recusada")
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() => NfeReturnTestSeed.CreateReturnAsync(s, quantity, reason));
        return ex.Message;
    }

    [Fact]
    public async Task Total_return_creates_a_pending_own_return_from_the_sale()
    {
        var s = await NfeReturnTestSeed.SeedAsync();

        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);

        var saved = await s.Sale.Db.Context.SalesInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == created.Key);
        Assert.Equal(SalesInvoiceType.Return, saved.InvoiceType);
        Assert.Equal(InvoiceStatus.Pending, saved.InvoiceStatus);
        Assert.True(saved.IsNfeReturn);
        Assert.Equal(s.Sale.InvoiceKey, saved.SalesInvoiceOriginKey);
        Assert.Equal(new DateTime(2026, 10, 2), saved.InvoiceDate!.Value.Date);
        Assert.Equal(30000m, saved.GrossWeight);
        Assert.Equal(30000m, saved.NetWeight);
        Assert.Null(saved.PaymentConditionCode);
        Assert.Null(saved.TaxPayerComments);
        Assert.StartsWith("Devolução da NF-e 1 série 1", saved.Comments);
        Assert.Contains("Motivo: Carga recusada", saved.Comments);

        var line = Assert.Single(saved.Items);
        Assert.Equal(s.ReturnUsageCode, line.UsageCode);
        Assert.Equal(s.SaleItemKey, line.SalesInvoiceItemOriginKey);
        Assert.Equal("2202", line.Cfop);
        Assert.Equal("00", line.CstIcms);
        Assert.Equal(7m, line.IcmsRate);

        var sale = await s.Sale.Db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == s.Sale.InvoiceKey);
        Assert.Equal(InvoiceStatus.Confirmed, sale.InvoiceStatus);
    }

    [Fact]
    public async Task Partial_returns_are_accepted_until_the_sold_quantity()
    {
        var s = await NfeReturnTestSeed.SeedAsync();

        await NfeReturnTestSeed.CreateReturnAsync(s, 10000m);
        await NfeReturnTestSeed.CreateReturnAsync(s, 20000m);

        Assert.Contains("passa do saldo devolvível (0,000)", await Rejects(s, 1m));
    }

    [Fact]
    public async Task Cancelled_return_frees_the_balance()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var first = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);
        first.InvoiceStatus = InvoiceStatus.Cancelled;
        await s.Sale.Db.SaveChangesAsync();

        var second = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);

        Assert.True(second.IsNfeReturn);
    }

    [Fact]
    public async Task Branch_that_does_not_issue_nfe_is_refused()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        (await s.Sale.Db.Context.Branchs.SingleAsync()).IssuesNfe = false;
        await s.Sale.Db.SaveChangesAsync();

        Assert.Equal("A filial 01 não emite NF-e pelo Siagro.", await Rejects(s));
    }

    [Fact]
    public async Task Sale_without_authorized_nfe_is_refused()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        (await s.Sale.Db.Context.SalesInvoices.SingleAsync()).NfeStatus = NfeStatus.None;
        await s.Sale.Db.SaveChangesAsync();

        Assert.Contains("não tem NF-e autorizada pelo Siagro", await Rejects(s));
    }

    [Fact]
    public async Task Sale_of_a_load_is_refused()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        (await s.Sale.Db.Context.SalesInvoices.SingleAsync()).ShipmentLoadKey = Guid.NewGuid();
        await s.Sale.Db.SaveChangesAsync();

        Assert.Equal("Documento com romaneio/carga: a devolução com NF-e ainda não é suportada.", await Rejects(s));
    }

    [Fact]
    public async Task Reason_is_required()
    {
        var s = await NfeReturnTestSeed.SeedAsync();

        Assert.Equal("Informe o motivo da devolução.", await Rejects(s, reason: " "));
    }

    [Fact]
    public async Task At_least_one_quantity_is_required()
    {
        var s = await NfeReturnTestSeed.SeedAsync();

        Assert.Equal("Informe a quantidade a devolver de ao menos um item.", await Rejects(s, 0m));
    }

    [Fact]
    public async Task Sale_usage_without_return_usage_is_refused()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        (await s.Sale.Db.Context.Usages.SingleAsync(u => u.Name == "Venda de grãos")).ReturnUsageCode = null;
        await s.Sale.Db.SaveChangesAsync();

        Assert.Contains("da venda não tem natureza de devolução cadastrada", await Rejects(s));
    }

    [Fact]
    public async Task Legacy_single_item_sale_without_number_is_accepted()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        (await s.Sale.Db.Context.SalesInvoicesItems.SingleAsync(i => i.Key == s.SaleItemKey)).NfeItemNumber = null;
        await s.Sale.Db.SaveChangesAsync();

        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);

        Assert.True(created.IsNfeReturn);
    }

    [Fact]
    public async Task Legacy_multi_item_sale_without_numbers_is_refused()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var sale = await s.Sale.Db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == s.Sale.InvoiceKey);
        sale.Items.Single().NfeItemNumber = null;
        var extra = sale.Items.Single();
        sale.Items.Add(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 2m,
            UsageCode = extra.UsageCode,
        });
        await s.Sale.Db.SaveChangesAsync();

        Assert.Contains("antes da numeração dos itens", await Rejects(s));
    }

    [Fact]
    public async Task Api_body_cannot_mark_a_return_as_nfe_return()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var db = s.Sale.Db;
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);
        var partners = new FakeBusinessPartnerService(
            names: new() { [NfeTestSeed.CardCode] = "CLIENTE BA LTDA" }, states: new() { [NfeTestSeed.CardCode] = "BA" });

        // O caminho da API (OData POST) chega ao SalesInvoicesCreateService sem o parâmetro nfeReturn.
        var body = new SalesInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = NfeTestSeed.CardCode, InvoiceType = SalesInvoiceType.Return,
            SalesInvoiceOriginKey = s.Sale.InvoiceKey, IsNfeReturn = true, InvoiceDate = new DateTime(2026, 10, 2),
            Items = [new SalesInvoiceItem { ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 2m }],
        };

        await new SalesInvoicesCreateService(
                db, partners, new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA" }),
                new FakeDocNumberSequenceService(), new SalesInvoicesUsageGuardService(usages),
                new SalesInvoicesCfopResolveService(db, usages, partners), TaxTestServices.InactiveApply(db),
                NullLogger<SalesInvoicesCreateService>.Instance)
            .ExecuteAsync(body, "tester");

        Assert.False((await db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == body.Key)).IsNfeReturn);
    }

    [Fact]
    public async Task Returnable_items_show_sold_returned_and_balance()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        await NfeReturnTestSeed.CreateReturnAsync(s, 10000m);

        var row = Assert.Single(await new SalesInvoicesNfeReturnableItemsService(s.Sale.Db).ExecuteAsync(s.Sale.InvoiceKey));

        Assert.Equal(s.SaleItemKey.ToString(), row.OriginItemKey);
        Assert.Equal(30000d, row.SoldQuantity);
        Assert.Equal(10000d, row.ReturnedQuantity);
        Assert.Equal(20000d, row.Returnable);
    }
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoicesNfeReturnCreateServiceTests"`
Expected: FAIL de compilação (`SalesInvoicesNfeReturnCreateService`, `SalesInvoiceNfeReturnRequest`, `SalesInvoicesNfeReturnableItemsService` não existem).

- [ ] **Step 4: DTO do diálogo**

Criar `SiagroB1.Domain/Dtos/SalesInvoiceNfeReturnableItemDto.cs` (`git add`):

```csharp
using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Linha do diálogo "Devolver": um item da venda e quanto dele ainda pode voltar. Quantidades em
/// <c>double</c> pelo mesmo motivo das actions: o UI5 lê <c>Edm.Decimal</c> como string. Todas as
/// propriedades carregam <c>[JsonPropertyName]</c> em PascalCase — sem isso a resposta sai em
/// camelCase e a tela encontra tudo <c>undefined</c>.
/// </summary>
public class SalesInvoiceNfeReturnableItemDto
{
    [JsonPropertyName("OriginItemKey")]
    public required string OriginItemKey { get; set; }

    [JsonPropertyName("ItemCode")]
    public required string ItemCode { get; set; }

    [JsonPropertyName("ItemName")]
    public string? ItemName { get; set; }

    [JsonPropertyName("UnitOfMeasureCode")]
    public string? UnitOfMeasureCode { get; set; }

    [JsonPropertyName("SoldQuantity")]
    public double SoldQuantity { get; set; }

    /// <summary>Em devoluções não canceladas (Pendentes inclusive).</summary>
    [JsonPropertyName("ReturnedQuantity")]
    public double ReturnedQuantity { get; set; }

    [JsonPropertyName("Returnable")]
    public double Returnable { get; set; }
}
```

- [ ] **Step 5: Saldo devolvível**

Criar `SiagroB1.Application/Services/Nfe/SalesInvoiceNfeReturnBalance.cs` (`git add`):

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Quanto de cada item da venda já está em devolução — Pendente ou Confirmada; Cancelada não conta.
/// Pendente conta de propósito: duas devoluções abertas da mesma venda somariam mais que o vendido.
/// </summary>
public static class SalesInvoiceNfeReturnBalance
{
    private const decimal Tolerance = 0.001m;
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static Task<Dictionary<Guid, decimal>> ReturnedByOriginItemAsync(
        AppDbContext context, Guid originInvoiceKey, Guid? excludingReturnKey) =>
        context.SalesInvoicesItems.AsNoTracking()
            .Where(i => i.SalesInvoiceItemOriginKey != null
                        && i.SalesInvoice!.SalesInvoiceOriginKey == originInvoiceKey
                        && i.SalesInvoice.InvoiceType == SalesInvoiceType.Return
                        && i.SalesInvoice.InvoiceStatus != InvoiceStatus.Cancelled
                        && i.SalesInvoiceKey != excludingReturnKey)
            .GroupBy(i => i.SalesInvoiceItemOriginKey!.Value)
            .Select(g => new { g.Key, Quantity = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Quantity);

    /// <summary>
    /// As <paramref name="lines"/> desta devolução, somadas às OUTRAS devoluções não canceladas da
    /// mesma venda, não passam do vendido por item.
    /// </summary>
    public static async Task EnsureWithinAsync(
        AppDbContext context, SalesInvoice returnInvoice, IEnumerable<SalesInvoiceItem> lines)
    {
        var originKey = returnInvoice.SalesInvoiceOriginKey
                        ?? throw new DefaultException("A devolução está sem a venda de origem.");
        var returned = await ReturnedByOriginItemAsync(context, originKey, returnInvoice.Key);
        var list = lines.ToList();
        var originItemKeys = list.Where(l => l.SalesInvoiceItemOriginKey != null)
            .Select(l => l.SalesInvoiceItemOriginKey!.Value).ToList();
        var sold = await context.SalesInvoicesItems.AsNoTracking()
            .Where(i => i.Key != null && originItemKeys.Contains(i.Key.Value))
            .ToDictionaryAsync(i => i.Key!.Value, i => i.Quantity);

        foreach (var line in list)
        {
            var key = line.SalesInvoiceItemOriginKey
                      ?? throw new DefaultException($"O item {line.ItemCode} da devolução não aponta um item da venda.");
            var available = sold.GetValueOrDefault(key) - returned.GetValueOrDefault(key);

            if (line.Quantity - available > Tolerance)
                throw new DefaultException(
                    $"Item {line.ItemCode}: a devolução ({line.Quantity.ToString("N3", PtBr)}) passa do saldo " +
                    $"devolvível da venda ({available.ToString("N3", PtBr)}).");
        }
    }
}
```


- [ ] **Step 6: Parâmetro `nfeReturn` no create**

Em `SalesInvoicesCreateService`, trocar a assinatura por:

```csharp
    public async Task ExecuteAsync(
        SalesInvoice salesInvoice, string userName, CommitMode commitMode = CommitMode.Auto, bool nfeReturn = false)
```

Logo depois de `SalesInvoiceNfeLock.ResetIssuanceFields(salesInvoice);`:

```csharp

        // Só o "Devolver" (SalesInvoicesNfeReturnCreateService) marca a devolução com NF-e própria;
        // o corpo da API nunca — senão um POST tiraria a nota da confirmação direta.
        salesInvoice.IsNfeReturn = nfeReturn && salesInvoice.InvoiceType == SalesInvoiceType.Return;
```

No laço `foreach (var (item, usage) in lineUsages)`, envolver as duas linhas do CFOP:

```csharp
            // A devolução própria usa natureza de ENTRADA: o CFOP (1202/2202) sai do cálculo, e a
            // resolução de saída abaixo recusaria a natureza.
            if (!salesInvoice.IsNfeReturn)
            {
                var cfop = await ResolveCfopAsync(salesInvoice, usage, fromShipmentBilling && !taxActive);

                if (cfop != null)
                {
                    cfopByItem[item] = cfop;
                }
            }
```

E trocar `salesInvoice.PaymentConditionCode ??= customer?.PaymentConditionCode;` por:

```csharp
            // A devolução própria não tem pagamento (tPag 90): a condição do cliente só confundiria.
            if (!salesInvoice.IsNfeReturn)
                salesInvoice.PaymentConditionCode ??= customer?.PaymentConditionCode;
```

- [ ] **Step 7: O serviço "Devolver"**

Criar `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeReturnCreateService.cs` (`git add`):

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesInvoices.Factories;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

public sealed record SalesInvoiceNfeReturnItem(Guid OriginItemKey, decimal Quantity);

public sealed record SalesInvoiceNfeReturnRequest(
    Guid SalesInvoiceKey, IReadOnlyList<SalesInvoiceNfeReturnItem> Items, string Reason);

/// <summary>
/// "Devolver" (spec §6): a partir de uma venda autorizada pelo Siagro, cria a devolução Pendente que
/// sai com NF-e PRÓPRIA de entrada (finalidade 4). Não confirma: quem confirma é a emissão — e é na
/// confirmação que a venda recebe a quantidade devolvida, como em toda devolução.
/// </summary>
public class SalesInvoicesNfeReturnCreateService(
    IUnitOfWork db,
    TaxCalculationGate gate,
    SalesInvoicesCreateService createService,
    ILogger<SalesInvoicesNfeReturnCreateService> logger,
    Func<DateTimeOffset>? clock = null,
    TimeZoneInfo? storageZone = null)
{
    private const decimal Tolerance = 0.001m;
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly Func<DateTimeOffset> _now = clock ?? NfeIssueInputAssembler.BrasiliaNow;

    // Mesmo fuso em que o OData grava o InvoiceDate (o do servidor): a emissão o converte de volta
    // para Brasília antes de exigir "hoje".
    private readonly TimeZoneInfo _storageZone = storageZone ?? TimeZoneInfo.Local;

    public async Task<SalesInvoice> ExecuteAsync(SalesInvoiceNfeReturnRequest request, string userName)
    {
        var origin = await db.Context.SalesInvoices
                         .Include(i => i.Items)
                         .Include(i => i.SalesTransactions)
                         .FirstOrDefaultAsync(i => i.Key == request.SalesInvoiceKey)
                     ?? throw new NotFoundException("Documento de saída não encontrado.");

        // TODA a validação antes de qualquer escrita.
        await ValidateOriginAsync(origin);

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new DefaultException("Informe o motivo da devolução.");

        var quantities = await ResolveQuantitiesAsync(origin, request.Items);
        var returnUsages = await ResolveReturnUsagesAsync(origin, quantities.Keys);
        EnsureSaleItemNumbers(origin, quantities.Keys);

        var returnInvoice = SalesInvoiceReturnFactory.CreateFrom(origin, userName, quantities);
        returnInvoice.InvoiceDate = TimeZoneInfo.ConvertTime(_now(), _storageZone).DateTime;
        returnInvoice.PaymentConditionCode = null;
        returnInvoice.TaxPayerComments = null;
        returnInvoice.TaxComments = null;
        returnInvoice.VolumeQuantity = origin.VolumeQuantity;
        returnInvoice.VolumeSpecies = origin.VolumeSpecies;
        returnInvoice.VolumeBrand = origin.VolumeBrand;
        returnInvoice.VolumeNumbering = origin.VolumeNumbering;
        returnInvoice.Comments =
            $"Devolução da NF-e {NumberText(origin.TaxDocumentNumber)} série {origin.TaxDocumentSeries} " +
            $"(doc.saída {origin.InvoiceNumber}). Motivo: {request.Reason.Trim()}";

        foreach (var item in returnInvoice.Items)
            item.UsageCode = returnUsages[item.SalesInvoiceItemOriginKey!.Value];

        try
        {
            await db.BeginTransactionAsync();

            await createService.ExecuteAsync(returnInvoice, userName, CommitMode.Deferred, nfeReturn: true);

            await db.SaveChangesAsync();
            await db.CommitAsync();

            return returnInvoice;
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e, "Erro ao criar a devolução do documento de saída {Number}", origin.InvoiceNumber);
            throw;
        }
    }

    private async Task ValidateOriginAsync(SalesInvoice origin)
    {
        if (!await gate.IsActiveAsync(origin.BranchCode))
            throw new DefaultException($"A filial {origin.BranchCode} não emite NF-e pelo Siagro.");

        if (origin.InvoiceType != SalesInvoiceType.Normal)
            throw new DefaultException("Só um documento de venda (Normal) pode ser devolvido por aqui.");

        if (origin.InvoiceStatus == InvoiceStatus.Returned)
            throw new DefaultException($"O documento {origin.InvoiceNumber} já foi devolvido por inteiro.");

        if (origin.InvoiceStatus != InvoiceStatus.Confirmed || origin.NfeStatus != NfeStatus.Authorized ||
            origin.ChaveNFe is not { Length: 44 })
            throw new DefaultException(
                $"O documento {origin.InvoiceNumber} não tem NF-e autorizada pelo Siagro: " +
                "a devolução com NF-e parte de uma venda autorizada.");

        if (origin.ShipmentLoadKey != null || origin.SalesTransactions.Count > 0)
            throw new DefaultException("Documento com romaneio/carga: a devolução com NF-e ainda não é suportada.");
    }

    private async Task<Dictionary<Guid, decimal>> ResolveQuantitiesAsync(
        SalesInvoice origin, IReadOnlyList<SalesInvoiceNfeReturnItem> items)
    {
        if (items.Any(i => i.Quantity < 0))
            throw new DefaultException("A quantidade a devolver não pode ser negativa.");

        var returned = await SalesInvoiceNfeReturnBalance.ReturnedByOriginItemAsync(db.Context, origin.Key, null);
        var result = new Dictionary<Guid, decimal>();

        foreach (var requested in items.Where(i => i.Quantity > 0))
        {
            var sold = origin.Items.FirstOrDefault(i => i.Key == requested.OriginItemKey)
                       ?? throw new DefaultException("Item informado não pertence ao documento de saída.");
            var available = sold.Quantity - returned.GetValueOrDefault(requested.OriginItemKey);

            if (requested.Quantity - available > Tolerance)
                throw new DefaultException(
                    $"Item {sold.ItemCode}: a quantidade a devolver ({requested.Quantity.ToString("N3", PtBr)}) " +
                    $"passa do saldo devolvível ({available.ToString("N3", PtBr)}).");

            result[requested.OriginItemKey] = requested.Quantity;
        }

        return result.Count == 0
            ? throw new DefaultException("Informe a quantidade a devolver de ao menos um item.")
            : result;
    }

    private async Task<Dictionary<Guid, int>> ResolveReturnUsagesAsync(SalesInvoice origin, IEnumerable<Guid> originItemKeys)
    {
        var result = new Dictionary<Guid, int>();

        foreach (var key in originItemKeys)
        {
            var sold = origin.Items.First(i => i.Key == key);
            var usage = sold.UsageCode is { } code
                ? await db.Context.Usages.AsNoTracking().FirstOrDefaultAsync(u => u.Code == code)
                : null;

            if (usage?.ReturnUsageCode is not { } returnCode)
                throw new DefaultException(usage is null
                    ? $"O item {sold.ItemCode} da venda está sem natureza de operação."
                    : $"A natureza {usage.Code} {usage.Name} da venda não tem natureza de devolução cadastrada.");

            result[key] = returnCode;
        }

        return result;
    }

    private static void EnsureSaleItemNumbers(SalesInvoice origin, IEnumerable<Guid> originItemKeys)
    {
        foreach (var key in originItemKeys)
        {
            if (SalesInvoiceNfeItemNumbering.OriginNumber(origin.Items.First(i => i.Key == key), origin.Items.Count) is null)
                throw new DefaultException(
                    "A NF-e de venda foi emitida antes da numeração dos itens; a devolução com NF-e não está disponível para ela.");
        }
    }

    private static string? NumberText(string? number) =>
        long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value.ToString(CultureInfo.InvariantCulture)
            : number;
}
```

- [ ] **Step 8: Itens devolvíveis**

Criar `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeReturnableItemsService.cs` (`git add`):

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Itens da venda com vendido, já devolvido e saldo — alimenta o diálogo "Devolver".</summary>
public class SalesInvoicesNfeReturnableItemsService(IUnitOfWork db)
{
    public async Task<IReadOnlyList<SalesInvoiceNfeReturnableItemDto>> ExecuteAsync(Guid key)
    {
        var origin = await db.Context.SalesInvoices.AsNoTracking().Include(i => i.Items)
                         .FirstOrDefaultAsync(i => i.Key == key)
                     ?? throw new NotFoundException("Documento de saída não encontrado.");

        var returned = await SalesInvoiceNfeReturnBalance.ReturnedByOriginItemAsync(db.Context, key, null);

        return SalesInvoiceNfeItemNumbering.Ordered(origin.Items)
            .Select(item =>
            {
                var back = returned.GetValueOrDefault(item.Key!.Value);

                return new SalesInvoiceNfeReturnableItemDto
                {
                    OriginItemKey = item.Key!.Value.ToString(),
                    ItemCode = item.ItemCode,
                    ItemName = item.ItemName,
                    UnitOfMeasureCode = item.UnitOfMeasureCode,
                    SoldQuantity = (double)item.Quantity,
                    ReturnedQuantity = (double)back,
                    Returnable = (double)Math.Max(0m, item.Quantity - back),
                };
            })
            .ToList();
    }
}
```

- [ ] **Step 9: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoicesNfeReturnCreateServiceTests|FullyQualifiedName~SalesInvoicesReturnServiceTests|FullyQualifiedName~SalesInvoicesCreateTaxationTests"`
Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add SiagroB1.Domain/Dtos/SalesInvoiceNfeReturnableItemDto.cs SiagroB1.Application/Services/Nfe/SalesInvoiceNfeReturnBalance.cs SiagroB1.Application/Services/Nfe/SalesInvoicesNfeReturnCreateService.cs SiagroB1.Application/Services/Nfe/SalesInvoicesNfeReturnableItemsService.cs SiagroB1.Application.Tests/Support/NfeReturnTestSeed.cs SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeReturnCreateServiceTests.cs
git commit -F <msg> -- SiagroB1.Domain/Dtos/SalesInvoiceNfeReturnableItemDto.cs SiagroB1.Application/Services/Nfe/SalesInvoiceNfeReturnBalance.cs SiagroB1.Application/Services/Nfe/SalesInvoicesNfeReturnCreateService.cs SiagroB1.Application/Services/Nfe/SalesInvoicesNfeReturnableItemsService.cs SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCreateService.cs SiagroB1.Application.Tests/Support/NfeReturnTestSeed.cs SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeReturnCreateServiceTests.cs
```

Mensagem: `feat(invoice): devolução com NF-e criada a partir da venda autorizada`.

---

### Task 6: Prontidão, montagem e emissão da devolução

**Files:**
- Modify: `SiagroB1.Application/Services/Nfe/NfeIssueContext.cs`, `NfeReadinessValidator.cs`, `NfeIssueInputAssembler.cs`, `SalesInvoicesNfeIssueService.cs`
- Modify (teste): `SiagroB1.Application.Tests/Nfe/NfeReadinessValidatorTests.cs` (linha `context.PaymentCondition.Name` → `context.PaymentCondition!.Name`)
- Test: `SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeReturnIssueTests.cs` (novo)

**Interfaces:**
- Consumes: Tasks 2–5.
- Produces: `record NfeReturnOrigin(string AccessKey, string Number, string Series, DateTime? IssuedOn, IReadOnlyDictionary<Guid, int> ItemNumbers)`; `NfeIssueContext.PaymentCondition` passa a `PaymentCondition?`; novo último parâmetro `NfeReturnOrigin? ReturnOrigin = null`.

- [ ] **Step 1: Testes que falham**

Criar `SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeReturnIssueTests.cs` (`git add`):

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>"Emitir NF-e" da devolução própria (spec §8–§9): entrada, finalidade 4, venda referenciada.</summary>
public class SalesInvoicesNfeReturnIssueTests
{
    private static SalesInvoicesNfeIssueService Issue(NfeScenario scenario, FakeNfeSefazClient sefaz)
    {
        var config = NfeTestSeed.Config();
        var options = new NfeOptions(config);

        return new SalesInvoicesNfeIssueService(
            scenario.Db, new TaxCalculationGate(scenario.Db, config), new NfeReadinessValidator(scenario.Db, options),
            new BranchNfeSettingsService(scenario.Db, options, sefaz), new FakeNfeNumberReservationService(), sefaz,
            new SalesInvoiceNfeResultHandler(scenario.Db, new RecordingConfirmService(scenario.Db), NullLogger<SalesInvoiceNfeResultHandler>.Instance),
            options, NullLogger<SalesInvoicesNfeIssueService>.Instance, NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone);
    }

    private static async Task<string> SignedXmlAsync(NfeScenario scenario, Guid invoiceKey) =>
        (await scenario.Db.Context.SalesInvoiceNfeXmls.AsNoTracking()
            .SingleAsync(x => x.SalesInvoiceKey == invoiceKey && x.Kind == SalesInvoiceNfeXmlKind.Signed)).Xml;

    [Fact]
    public async Task Own_return_is_issued_as_an_incoming_return_referencing_the_sale()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 10000m);
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var outcome = await Issue(s.Sale, sefaz).ExecuteAsync(created.Key, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        var xml = await SignedXmlAsync(s.Sale, created.Key);
        Assert.Contains("<tpNF>0</tpNF>", xml);
        Assert.Contains("<finNFe>4</finNFe>", xml);
        Assert.Contains($"<refNFe>{NfeReturnTestSeed.SaleAccessKey}</refNFe>", xml);
        Assert.Contains($"<DFeReferenciado><chaveAcesso>{NfeReturnTestSeed.SaleAccessKey}</chaveAcesso><nItem>1</nItem></DFeReferenciado>", xml);
        Assert.Contains("<CFOP>2202</CFOP>", xml);
        Assert.Contains("<tPag>90</tPag>", xml);
        Assert.DoesNotContain("<cobr>", xml);
        Assert.Contains("<natOp>DEVOLUCAO DE VENDA</natOp>", xml);
        Assert.Contains($"Devolução da NF-e nº 1, série 1, de 02/10/2026, chave {NfeReturnTestSeed.SaleAccessKey}.", xml);
    }

    [Fact]
    public async Task Returning_only_the_second_item_references_item_2_of_the_sale()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var sale = await s.Sale.Db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == s.Sale.InvoiceKey);
        var first = sale.Items.Single();
        var second = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "SOJA", ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "KG",
            Quantity = 5000m, UnitPrice = 2m, UsageCode = first.UsageCode, Cfop = first.Cfop, Ncm = first.Ncm,
            GoodsOrigin = 0, CstIcms = first.CstIcms, IcmsRate = first.IcmsRate, CstPis = first.CstPis,
            CstCofins = first.CstCofins, IbsCbsCst = first.IbsCbsCst, IbsCbsClassCode = first.IbsCbsClassCode,
            CbsRate = first.CbsRate, IbsStateRate = first.IbsStateRate, NfeItemNumber = 2,
        };
        sale.Items.Add(second);
        await s.Sale.Db.SaveChangesAsync();

        var created = await NfeReturnTestSeed.CreateService(s.Sale.Db).ExecuteAsync(
            new SalesInvoiceNfeReturnRequest(s.Sale.InvoiceKey, [new SalesInvoiceNfeReturnItem(second.Key!.Value, 5000m)], "Recusa"),
            "tester");
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        await Issue(s.Sale, sefaz).ExecuteAsync(created.Key, "tester");

        var xml = await SignedXmlAsync(s.Sale, created.Key);
        Assert.Contains("<det nItem=\"1\">", xml);
        Assert.Contains("<nItem>2</nItem></DFeReferenciado>", xml);
    }

    [Fact]
    public async Task Balance_exceeded_at_issue_time_is_refused_before_reserving_a_number()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);
        (await s.Sale.Db.Context.SalesInvoicesItems.SingleAsync(i => i.Key == s.SaleItemKey)).Quantity = 10000m;
        await s.Sale.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Issue(s.Sale, new FakeNfeSefazClient()).ExecuteAsync(created.Key, "tester"));

        Assert.Contains("passa do saldo devolvível da venda", ex.Message);
        Assert.Null((await s.Sale.Db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == created.Key)).TaxDocumentNumber);
    }

    [Fact]
    public async Task Sale_no_longer_confirmed_blocks_the_issue()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);
        (await s.Sale.Db.Context.SalesInvoices.SingleAsync(i => i.Key == s.Sale.InvoiceKey)).InvoiceStatus = InvoiceStatus.Pending;
        await s.Sale.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Issue(s.Sale, new FakeNfeSefazClient()).ExecuteAsync(created.Key, "tester"));

        Assert.Contains("Venda de origem", ex.Message);
        Assert.Contains("não está confirmada", ex.Message);
    }

    [Fact]
    public async Task Own_return_does_not_need_a_payment_condition()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);
        var invoice = await s.Sale.Db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == created.Key);

        var context = await new NfeReadinessValidator(s.Sale.Db, new NfeOptions(NfeTestSeed.Config())).ValidateAsync(invoice);

        Assert.Null(context.PaymentCondition);
        Assert.Equal(NfeReturnTestSeed.SaleAccessKey, context.ReturnOrigin!.AccessKey);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoicesNfeReturnIssueTests"`
Expected: FAIL de compilação (`context.ReturnOrigin` não existe).

- [ ] **Step 3: Contexto**

Em `NfeIssueContext.cs`, trocar o record por:

```csharp
/// <summary>Cadastro já carregado e conferido pela prontidão — tudo o que a montagem da entrada usa.</summary>
/// <param name="PaymentCondition">Nula só na devolução própria, que não tem pagamento (tPag 90).</param>
/// <param name="ReturnOrigin">Na devolução própria, a venda referenciada; nula na venda.</param>
public sealed record NfeIssueContext(
    Branch Branch,
    Municipality BranchMunicipality,
    BranchNfeSettings Settings,
    BusinessPartner Customer,
    Address CustomerAddress,
    Municipality CustomerMunicipality,
    BusinessPartner? DeliveryPartner,
    Address? DeliveryAddress,
    Municipality? DeliveryMunicipality,
    BusinessPartner? Carrier,
    Address? CarrierAddress,
    string? TruckPlate,
    string? TruckState,
    PaymentCondition? PaymentCondition,
    IReadOnlyDictionary<int, Usage> Usages,
    IReadOnlyDictionary<string, string?> ItemCests,
    NfeReturnOrigin? ReturnOrigin = null);

/// <summary>
/// A venda que a devolução própria referencia, já conferida: a chave vai no <c>NFref</c> e, com o
/// número de cada item vendido (<c>ItemNumbers</c>, pela chave do item da venda), no
/// <c>DFeReferenciado</c> de cada item.
/// </summary>
public sealed record NfeReturnOrigin(
    string AccessKey, string Number, string Series, DateTime? IssuedOn, IReadOnlyDictionary<Guid, int> ItemNumbers);
```

Em `NfeReadinessValidatorTests.cs`, trocar `context.PaymentCondition.Name` por `context.PaymentCondition!.Name`.

- [ ] **Step 4: Prontidão**

Em `NfeReadinessValidator.ValidateAsync`, trocar o `if` dentro do laço do CFOP por:

```csharp
                // Venda: 5xxx dentro, 6xxx fora. Devolução própria (entrada): 1xxx dentro, 2xxx fora.
                var inStatePrefix = invoice.IsNfeReturn ? '1' : '5';
                var outStatePrefix = invoice.IsNfeReturn ? '2' : '6';

                if ((first == inStatePrefix && !sameState) || (first == outStatePrefix && sameState))
                    problems.Add($"Item {line}: CFOP {item.Cfop} não confere com o destino ({destinationState}) — salve o item de novo para recalcular.");
```

Envolver o bloco da condição de pagamento:

```csharp
        // A devolução própria não tem pagamento (tPag 90, rejeição 871 com qualquer outro meio).
        PaymentCondition? condition = null;
        if (!invoice.IsNfeReturn)
        {
            if (invoice.PaymentConditionCode is null)
            {
                problems.Add("Documento: condição de pagamento");
            }
            else
            {
                condition = await db.Context.PaymentConditions.AsNoTracking().FirstOrDefaultAsync(c => c.Code == invoice.PaymentConditionCode);
                if (condition is null)
                    problems.Add($"Documento: condição de pagamento {invoice.PaymentConditionCode} não encontrada");
                else if (condition.Inactive)
                    problems.Add($"Documento: a condição de pagamento {condition.Name} está inativa");
            }
        }

        var returnOrigin = invoice.IsNfeReturn ? await LoadReturnOriginAsync(invoice, problems) : null;
```

E o `return` final passa a:

```csharp
        return new NfeIssueContext(
            branch, branch.Municipality!, settings!, customer!, customerAddress!, customerAddress!.Municipality!,
            deliveryPartner, deliveryAddress, deliveryAddress?.Municipality,
            carrier, carrier is null ? null : BillingAddress(carrier),
            plate, truckState, condition, usages, itemCests, returnOrigin);
```

Método novo (antes de `LoadPartnerAsync`):

```csharp
    /// <summary>A venda da devolução própria: confirmada, com NF-e autorizada e o nItem de cada item devolvido.</summary>
    private async Task<NfeReturnOrigin?> LoadReturnOriginAsync(SalesInvoice invoice, List<string> problems)
    {
        var origin = await db.Context.SalesInvoices.AsNoTracking().Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Key == invoice.SalesInvoiceOriginKey);

        if (origin is null)
        {
            problems.Add("Documento: venda de origem não encontrada");
            return null;
        }

        if (origin.NfeStatus != NfeStatus.Authorized || origin.ChaveNFe is not { Length: 44 })
        {
            problems.Add($"Venda de origem {origin.InvoiceNumber}: NF-e não autorizada");
            return null;
        }

        // Confirmada (ou já Devolvida por outras devoluções): uma venda estornada depois de a devolução
        // nascer faria a confirmação pós-autorização falhar com a NF-e já emitida.
        if (origin.InvoiceStatus is not (InvoiceStatus.Confirmed or InvoiceStatus.Returned))
        {
            problems.Add($"Venda de origem {origin.InvoiceNumber}: não está confirmada");
            return null;
        }

        var numbers = new Dictionary<Guid, int>();
        foreach (var item in invoice.Items)
        {
            var sold = origin.Items.FirstOrDefault(o => o.Key == item.SalesInvoiceItemOriginKey);
            var number = sold is null ? null : SalesInvoiceNfeItemNumbering.OriginNumber(sold, origin.Items.Count);

            if (number is null)
                problems.Add($"Item {item.ItemCode}: sem o item correspondente da NF-e de venda");
            else
                numbers[sold!.Key!.Value] = number.Value;
        }

        return new NfeReturnOrigin(origin.ChaveNFe, origin.TaxDocumentNumber!, origin.TaxDocumentSeries!, origin.InvoiceDate, numbers);
    }
```

- [ ] **Step 5: Montador**

Em `NfeIssueInputAssembler.Build` (acrescentar `using SiagroB1.Domain.Exceptions;`), no começo do método, depois de `var items = ...`:

```csharp
        var returnOrigin = invoice.IsNfeReturn
            ? context.ReturnOrigin ?? throw new DefaultException("A devolução está sem a venda de origem.")
            : null;
```

No inicializador do `NfeIssueInput`, depois de `RandomCode = invoice.NfeRandomCode!,`:

```csharp
            Purpose = returnOrigin is null ? NfePurpose.Sale : NfePurpose.Return,
            ReferencedKeys = returnOrigin is null ? [] : [returnOrigin.AccessKey],
```

No `with` dos itens, depois de `Cest = ...,`:

```csharp
                // VC02-14: o número do item NA VENDA, não a posição na devolução.
                Reference = returnOrigin is null
                    ? null
                    : new NfeItemReference(returnOrigin.AccessKey, returnOrigin.ItemNumbers[item.SalesInvoiceItemOriginKey!.Value]),
```

Trocar `Payment = PaymentInstallmentCalculator.Calculate(...)` por:

```csharp
            // Devolução: tPag 90 com vPag 0 (rejeição 871) e sem cobr.
            Payment = returnOrigin is not null
                ? new PaymentPlan(PaymentMeansCodes.NoPayment, null, 0m, [])
                : PaymentInstallmentCalculator.Calculate(
                    context.PaymentCondition!.Days, context.PaymentCondition.StartRule, context.PaymentCondition.PaymentMeans,
                    total, DateOnly.FromDateTime(issuedAt.Date)),
```

Trocar `AdditionalInfo = AdditionalInfo(items, context.Usages, invoice.TaxPayerComments),` por:

```csharp
            AdditionalInfo = AdditionalInfo(items, context.Usages, invoice.TaxPayerComments, ReturnReference(returnOrigin)),
```

E trocar o método `AdditionalInfo` + acrescentar `ReturnReference`:

```csharp
    /// <summary>
    /// <c>infCpl</c>: na devolução, a referência à venda primeiro; depois os textos padrão distintos das
    /// naturezas, na ordem das linhas, e as informações do contribuinte.
    /// </summary>
    private static string? AdditionalInfo(
        IEnumerable<SalesInvoiceItem> items, IReadOnlyDictionary<int, Usage> usages, string? taxPayerComments,
        string? returnReference = null)
    {
        var texts = items
            .Select(i => i.UsageCode is { } code && usages.TryGetValue(code, out var usage) ? usage.DefaultAdditionalInfo : null)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .Distinct()
            .ToList();

        if (returnReference is not null)
            texts.Insert(0, returnReference);

        if (!string.IsNullOrWhiteSpace(taxPayerComments))
            texts.Add(taxPayerComments.Trim());

        return texts.Count == 0 ? null : string.Join(" | ", texts);
    }

    private static string? ReturnReference(NfeReturnOrigin? origin) =>
        origin is null
            ? null
            : $"Devolução da NF-e nº {NumberText(origin.Number)}, série {origin.Series}, " +
              $"de {origin.IssuedOn?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}, chave {origin.AccessKey}.";

    private static string NumberText(string number) =>
        long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value.ToString(CultureInfo.InvariantCulture)
            : number;
```

- [ ] **Step 6: Emissão**

Em `SalesInvoicesNfeIssueService.EnsurePreconditionsAsync`, trocar o `if (invoice.InvoiceType != SalesInvoiceType.Normal)` por:

```csharp
        if (invoice.InvoiceType != SalesInvoiceType.Normal && !SalesInvoicesTaxApplyService.IsOwnNfeReturn(invoice))
            throw new DefaultException(
                "Só o documento Normal e a devolução criada pelo Devolver são emitidos como NF-e por aqui.");
```

(acrescentar `using SiagroB1.Application.Services.SalesInvoices;`) e, no fim do método (depois da recusa de `uncalculated`):

```csharp

        // Devolução: o saldo da venda ANTES de reservar o número — a conferência da confirmação roda
        // depois da autorização, tarde demais para impedir uma NF-e de quantidade a mais.
        if (invoice.IsNfeReturn)
            await SalesInvoiceNfeReturnBalance.EnsureWithinAsync(db.Context, invoice, invoice.Items);
```

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Nfe"`
Expected: PASS (novos + todos os de NF-e; `Return_document_is_refused` continua passando: devolução sem `IsNfeReturn` segue recusada e a mensagem contém "Normal").

- [ ] **Step 8: Commit**

```bash
git add SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeReturnIssueTests.cs
git commit -F <msg> -- SiagroB1.Application/Services/Nfe/NfeIssueContext.cs SiagroB1.Application/Services/Nfe/NfeReadinessValidator.cs SiagroB1.Application/Services/Nfe/NfeIssueInputAssembler.cs SiagroB1.Application/Services/Nfe/SalesInvoicesNfeIssueService.cs SiagroB1.Application.Tests/Nfe/NfeReadinessValidatorTests.cs SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeReturnIssueTests.cs
```

Mensagem: `feat(invoice): emissão da NF-e de devolução referenciando a venda`.

---

### Task 7: Travas da devolução própria

**Files:**
- Create: `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeReturnLock.cs`
- Modify: `SalesInvoicesConfirmService.cs`, `SalesInvoiceNfeLock.cs`, `SalesInvoicesUpdateService.cs`, `SalesInvoicesItemsUpdateService.cs`, `SalesInvoicesItemsCreateService.cs`, `SalesInvoicesReturnService.cs` (todos em `SiagroB1.Application/Services/SalesInvoices/`), `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsRefuseService.cs`
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesConfirmNfeGuardTests.cs` (mod), `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesNfeReturnLockTests.cs` (novo)

**Interfaces:**
- Consumes: `SalesInvoiceNfeReturnBalance.EnsureWithinAsync` (Task 5), `SalesInvoicesTaxApplyService.IsOwnNfeReturn` (Task 4), `NfeReturnTestSeed` (Task 5).
- Produces: `SalesInvoiceNfeReturnLock.RestoreHeader(EntityEntry<SalesInvoice>)`, `.RestoreLine(EntityEntry<SalesInvoiceItem>)`; construtores de `SalesInvoicesReturnService` e `ShipmentLoadsRefuseService` ganham o último parâmetro opcional `TaxCalculationGate? gate = null`.

- [ ] **Step 1: Testes que falham — confirmação**

Em `SalesInvoicesConfirmNfeGuardTests.cs`, acrescentar o parâmetro `bool nfeReturn = false` ao `SeedAsync` e, logo depois de `invoice.NfeStatus = nfe;`, a linha `invoice.IsNfeReturn = nfeReturn;`. Acrescentar:

```csharp
    [Fact]
    public async Task Rule_active_refuses_direct_confirmation_of_an_own_return()
    {
        var (db, invoice) = await SeedAsync(type: SalesInvoiceType.Return, nfeReturn: true);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Confirm(db, "STANDALONE").ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal("Na filial que emite NF-e pelo Siagro, confirme emitindo a NF-e.", ex.Message);
    }

    [Fact]
    public async Task Own_return_with_authorized_nfe_is_confirmed()
    {
        var (db, invoice) = await SeedAsync(type: SalesInvoiceType.Return, nfeReturn: true, nfe: NfeStatus.Authorized);

        await Confirm(db, "STANDALONE").ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, await StatusAsync(db, invoice.Key));
    }
```

- [ ] **Step 2: Testes que falham — demais travas**

Criar `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesNfeReturnLockTests.cs` (`git add`):

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>Travas da devolução própria (spec §7 e §9.4).</summary>
public class SalesInvoicesNfeReturnLockTests
{
    private static FakeBusinessPartnerService Partners() => new(
        names: new() { [NfeTestSeed.CardCode] = "CLIENTE BA LTDA", ["C2"] = "OUTRO" },
        states: new() { [NfeTestSeed.CardCode] = "BA", ["C2"] = "SP" });

    private static SalesInvoicesUpdateService HeaderUpdate(UnitOfWork db) =>
        new(db, Partners(), TaxTestServices.Apply(db, Partners()), NullLogger<SalesInvoicesUpdateService>.Instance);

    private static SalesInvoicesItemsUpdateService ItemUpdate(UnitOfWork db) =>
        new(db, new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            TaxTestServices.Apply(db, Partners()), NullLogger<SalesInvoicesUpdateService>.Instance);

    [Fact]
    public async Task Informar_nota_fiscal_is_refused_for_an_own_return()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            new SalesInvoicesSetDocumentNumberService(s.Sale.Db, new SalesInvoicesChangeLogService(s.Sale.Db.Context))
                .ExecuteAsync(created.Key, "000000010", "1", null, "tester"));

        Assert.Equal("Número, série e chave da devolução vêm da emissão da NF-e pelo Siagro.", ex.Message);
    }

    [Fact]
    public async Task Patch_cannot_turn_a_sale_into_an_own_return()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var sale = await s.Sale.Db.Context.SalesInvoices.SingleAsync(i => i.Key == s.Sale.InvoiceKey);
        sale.IsNfeReturn = true;

        await HeaderUpdate(s.Sale.Db).ExecuteAsync(sale.Key, sale, "tester");

        Assert.False((await s.Sale.Db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == sale.Key)).IsNfeReturn);
    }

    [Fact]
    public async Task Own_return_header_keeps_customer_and_branch()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);
        created.CardCode = "C2";
        created.BranchCode = "99";

        await HeaderUpdate(s.Sale.Db).ExecuteAsync(created.Key, created, "tester");

        var saved = await s.Sale.Db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == created.Key);
        Assert.Equal(NfeTestSeed.CardCode, saved.CardCode);
        Assert.Equal("01", saved.BranchCode);
    }

    [Fact]
    public async Task Own_return_line_keeps_price_and_usage_and_recalculates_the_quantity()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);
        var line = await s.Sale.Db.Context.SalesInvoicesItems.SingleAsync(i => i.SalesInvoiceKey == created.Key);
        line.Quantity = 10000m;
        line.UnitPrice = 9m;
        line.UsageCode = 999;

        await ItemUpdate(s.Sale.Db).ExecuteAsync(line.Key!.Value, line, "tester");

        var saved = await s.Sale.Db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == line.Key);
        Assert.Equal(10000m, saved.Quantity);
        Assert.Equal(2m, saved.UnitPrice);
        Assert.Equal(s.ReturnUsageCode, saved.UsageCode);
        Assert.Equal(20000m, saved.IcmsBase);
    }

    [Fact]
    public async Task Own_return_line_above_the_balance_is_refused()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);
        var line = await s.Sale.Db.Context.SalesInvoicesItems.SingleAsync(i => i.SalesInvoiceKey == created.Key);
        line.Quantity = 30001m;

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ItemUpdate(s.Sale.Db).ExecuteAsync(line.Key!.Value, line, "tester"));

        Assert.Contains("passa do saldo devolvível da venda", ex.Message);
    }

    [Fact]
    public async Task Own_return_line_with_zero_quantity_is_refused()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);
        var line = await s.Sale.Db.Context.SalesInvoicesItems.SingleAsync(i => i.SalesInvoiceKey == created.Key);
        line.Quantity = 0m;

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ItemUpdate(s.Sale.Db).ExecuteAsync(line.Key!.Value, line, "tester"));

        Assert.Equal("Item SOJA: informe a quantidade a devolver.", ex.Message);
    }

    [Fact]
    public async Task Own_return_refuses_new_lines()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 10000m);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            new SalesInvoicesItemsCreateService(s.Sale.Db, new FakeItemService(), TaxTestServices.Apply(s.Sale.Db, Partners()),
                    NullLogger<SalesInvoicesItemsCreateService>.Instance)
                .ExecuteAsync(new SalesInvoiceItem { SalesInvoiceKey = created.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 2m }, "tester"));

        Assert.StartsWith("Na devolução com NF-e, os itens vêm da venda", ex.Message);
    }

    [Fact]
    public async Task Retornar_is_refused_for_a_sale_authorized_by_the_siagro()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var service = new SalesInvoicesReturnService(s.Sale.Db, null!, null!, null!, null!, null!, null!,
            NullLogger<SalesInvoicesReturnService>.Instance, TaxTestServices.Gate(s.Sale.Db, "STANDALONE"));

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(
            new SalesInvoiceReturnRequest(s.Sale.InvoiceKey, [], RefusalDestination.Rebilling, null, "Recusa"), "tester"));

        Assert.Equal("Na filial que emite NF-e pelo Siagro, a devolução de documento com romaneio ou carga ainda não é suportada.", ex.Message);
    }

    [Fact]
    public async Task Retornar_outside_the_rule_keeps_the_old_validation()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var service = new SalesInvoicesReturnService(s.Sale.Db, null!, null!, null!, null!, null!, null!,
            NullLogger<SalesInvoicesReturnService>.Instance, TaxTestServices.Gate(s.Sale.Db, "SAPB1"));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(
            new SalesInvoiceReturnRequest(s.Sale.InvoiceKey, [], RefusalDestination.Rebilling, null, "Recusa"), "tester"));

        Assert.Equal("Selecione ao menos um romaneio a devolver.", ex.Message);
    }

    [Fact]
    public async Task Load_refusal_is_refused_for_a_sale_authorized_by_the_siagro()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var service = new ShipmentLoadsRefuseService(s.Sale.Db, null!, null!, null!, null!, null!, null!, null!,
            NullLogger<ShipmentLoadsRefuseService>.Instance, TaxTestServices.Gate(s.Sale.Db, "STANDALONE"));

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(
            new RefusalRequest(Guid.NewGuid(), [new RefusalLine(s.Sale.InvoiceKey, 1m)], RefusalDestination.Rebilling, null, "Recusa"), "tester"));

        Assert.Equal("Na filial que emite NF-e pelo Siagro, a devolução de documento com romaneio ou carga ainda não é suportada.", ex.Message);
    }
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoicesNfeReturnLockTests|FullyQualifiedName~SalesInvoicesConfirmNfeGuardTests"`
Expected: FAIL de compilação (construtores sem `gate`); depois de compilar, as travas não acontecem.

- [ ] **Step 4: Confirmação e "Informar Nota Fiscal"**

Em `SalesInvoicesConfirmService`, trocar `invoice.InvoiceType == SalesInvoiceType.Normal &&` (na guarda das linhas 82-85) por:

```csharp
            (invoice.InvoiceType == SalesInvoiceType.Normal || invoice.IsNfeReturn) &&
```

e o comentário acima para "o documento Normal e a devolução própria só confirmam com a NF-e autorizada — é a emissão que chama esta confirmação. As demais devoluções seguem como sempre."

Em `SalesInvoiceNfeLock.EnsureManualTaxDocument`, no começo:

```csharp
        if (invoice.IsNfeReturn)
            throw new DefaultException("Número, série e chave da devolução vêm da emissão da NF-e pelo Siagro.");
```

Em `SalesInvoiceNfeLock.RestoreIssuanceFields`, ao final:

```csharp

        // A marca da devolução própria nasce no Devolver e nunca muda pela API.
        entry.Property(nameof(SalesInvoice.IsNfeReturn)).CurrentValue = entry.OriginalValues[nameof(SalesInvoice.IsNfeReturn)];
```

- [ ] **Step 5: Travas de cabeçalho e linha**

Criar `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeReturnLock.cs` (`git add`):

```csharp
using Microsoft.EntityFrameworkCore.ChangeTracking;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Travas da devolução própria (spec §7): ela é a imagem da venda — cliente, filial, produto, preço
/// e natureza vêm de lá. Como as travas do sub-projeto 1, sobrescreve com o gravado em vez de
/// recusar: o PATCH da tela reenvia a entidade inteira.
/// </summary>
public static class SalesInvoiceNfeReturnLock
{
    private static readonly string[] HeaderFields =
    [
        nameof(SalesInvoice.CardCode), nameof(SalesInvoice.CardName), nameof(SalesInvoice.BranchCode),
        nameof(SalesInvoice.InvoiceType), nameof(SalesInvoice.SalesInvoiceOriginKey),
    ];

    private static readonly string[] LineFields =
    [
        nameof(SalesInvoiceItem.ItemCode), nameof(SalesInvoiceItem.UnitOfMeasureCode), nameof(SalesInvoiceItem.UnitPrice),
        nameof(SalesInvoiceItem.UsageCode), nameof(SalesInvoiceItem.SalesInvoiceItemOriginKey),
        nameof(SalesInvoiceItem.SalesContractKey),
    ];

    /// <summary>Chamado DEPOIS do SetValues; só age se o gravado é uma devolução própria.</summary>
    public static void RestoreHeader(EntityEntry<SalesInvoice> entry)
    {
        if (entry.OriginalValues[nameof(SalesInvoice.IsNfeReturn)] is not true)
            return;

        foreach (var field in HeaderFields)
            entry.Property(field).CurrentValue = entry.OriginalValues[field];
    }

    /// <summary>Chamado DEPOIS do SetValues da linha de uma devolução própria: só a quantidade muda.</summary>
    public static void RestoreLine(EntityEntry<SalesInvoiceItem> entry)
    {
        foreach (var field in LineFields)
            entry.Property(field).CurrentValue = entry.OriginalValues[field];
    }
}
```

Em `SalesInvoicesUpdateService.ExecuteAsync`, depois de `SalesInvoiceNfeLock.RestoreIssuanceFields(entry);`:

```csharp
            SalesInvoiceNfeReturnLock.RestoreHeader(entry);
```

Em `SalesInvoicesItemsUpdateService.ApplyTaxLockAsync`, trocar o bloco do `if` Pendente por:

```csharp
        if (invoice.InvoiceStatus is null or InvoiceStatus.Pending && invoice.NfeStatus != NfeStatus.Authorized)
        {
            if (SalesInvoicesTaxApplyService.IsOwnNfeReturn(invoice))
            {
                SalesInvoiceNfeReturnLock.RestoreLine(db.Context.Entry(item));

                if (item.Quantity <= 0)
                    throw new DefaultException($"Item {item.ItemCode}: informe a quantidade a devolver.");

                await SalesInvoiceNfeReturnBalance.EnsureWithinAsync(db.Context, invoice, [item]);
            }

            await taxApply.ApplyAsync(invoice, [item]);
            return;
        }
```

(acrescentar `using SiagroB1.Application.Services.Nfe;`). Atenção: `ApplyTaxLockAsync` roda fora de `try`/`catch` de `DefaultException`? Está dentro do `try` que só captura `DbUpdateConcurrencyException` — a `DefaultException` sobe como 400, como as demais guardas.

Em `SalesInvoicesItemsCreateService.ExecuteAsync`, dentro do `if (invoice is not null)`, antes de `SalesInvoiceNfeLock.EnsureLinesChangeable(...)`:

```csharp
            if (SalesInvoicesTaxApplyService.IsOwnNfeReturn(invoice))
                throw new DefaultException(
                    "Na devolução com NF-e, os itens vêm da venda: para devolver outro item, use o Devolver da venda.");
```

- [ ] **Step 6: "Retornar" e "Recusa de carga"**

Em `SalesInvoicesReturnService`, acrescentar o parâmetro ao construtor primário (último): `TaxCalculationGate? gate = null` (e `using SiagroB1.Application.Services.Taxes;`). Em `ExecuteAsync`, logo antes de `Validate(originInvoice, request);`:

```csharp
        await EnsureNotIssuedBySiagroAsync(originInvoice);
```

Método novo:

```csharp
    /// <summary>
    /// Na filial que emite NF-e pelo Siagro, a venda autorizada só volta com NF-e de devolução — e o
    /// caminho com romaneio ainda não a emite (spec §3/§9.4). Sem a regra ativa, nada muda.
    /// </summary>
    private async Task EnsureNotIssuedBySiagroAsync(SalesInvoice origin)
    {
        if (gate is not null && origin.NfeStatus == NfeStatus.Authorized && await gate.IsActiveAsync(origin.BranchCode))
            throw new DefaultException(
                "Na filial que emite NF-e pelo Siagro, a devolução de documento com romaneio ou carga ainda não é suportada.");
    }
```

Em `ShipmentLoadsRefuseService`, mesmo parâmetro opcional no construtor (último) e, como **primeira** linha de `ExecuteAsync` (antes de carregar a carga):

```csharp
        await EnsureNotIssuedBySiagroAsync(request);
```

Método novo:

```csharp
    /// <summary>Mesma regra de <c>SalesInvoicesReturnService</c>: nota autorizada pelo Siagro não volta sem NF-e.</summary>
    private async Task EnsureNotIssuedBySiagroAsync(RefusalRequest request)
    {
        if (gate is null)
            return;

        var keys = request.Lines.Select(l => l.SalesInvoiceKey).ToList();
        var branches = await db.Context.SalesInvoices.AsNoTracking()
            .Where(i => keys.Contains(i.Key) && i.NfeStatus == NfeStatus.Authorized)
            .Select(i => i.BranchCode)
            .Distinct()
            .ToListAsync();

        foreach (var branch in branches)
        {
            if (await gate.IsActiveAsync(branch))
                throw new DefaultException(
                    "Na filial que emite NF-e pelo Siagro, a devolução de documento com romaneio ou carga ainda não é suportada.");
        }
    }
```

(O DI injeta o `TaxCalculationGate`, já registrado; os testes antigos constroem sem ele e a regra fica inativa — mesmo desenho do `SalesInvoicesConfirmService`.)

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests`
Expected: PASS na suíte inteira (2709 anteriores + os novos das Tasks 1–7).

- [ ] **Step 8: Commit**

```bash
git add SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeReturnLock.cs SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesNfeReturnLockTests.cs
git commit -F <msg> -- SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeReturnLock.cs SiagroB1.Application/Services/SalesInvoices/SalesInvoicesConfirmService.cs SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeLock.cs SiagroB1.Application/Services/SalesInvoices/SalesInvoicesUpdateService.cs SiagroB1.Application/Services/SalesInvoices/SalesInvoicesItemsUpdateService.cs SiagroB1.Application/Services/SalesInvoices/SalesInvoicesItemsCreateService.cs SiagroB1.Application/Services/SalesInvoices/SalesInvoicesReturnService.cs SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsRefuseService.cs SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesConfirmNfeGuardTests.cs SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesNfeReturnLockTests.cs
```

Mensagem: `feat(invoice): travas da devolução com NF-e própria`.

---

### Task 8: Endpoints, EDM e DI

**Files:**
- Create: `SiagroB1.Web/Actions/Nfe/SalesInvoicesCreateNfeReturnController.cs`, `SiagroB1.Web/Functions/Nfe/SalesInvoicesNfeReturnableItemsController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/Nfe/NfeReturnEdmModelTests.cs` (novo)

**Interfaces:**
- Consumes: `SalesInvoicesNfeReturnCreateService`, `SalesInvoicesNfeReturnableItemsService` (Task 5).
- Produces: `POST odata/SalesInvoicesCreateNfeReturn` `{ Key: Guid, OriginItemKeys: Guid[], Quantities: double[], Reason: string }` → `Edm.Guid` (a chave da devolução); `GET odata/SalesInvoicesNfeReturnableItems(Key={key})` → coleção de `SalesInvoiceNfeReturnableItemDto`.

- [ ] **Step 1: Teste do EDM que falha**

Criar `SiagroB1.Application.Tests/Nfe/NfeReturnEdmModelTests.cs` (`git add`), copiando os `using` de `Taxes/TaxationEdmModelTests.cs`:

```csharp
namespace SiagroB1.Application.Tests.Nfe;

/// <summary>O EDM real expõe o que o "Devolver" consome.</summary>
public class NfeReturnEdmModelTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void Create_nfe_return_is_an_action_with_parallel_double_quantities()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == "SalesInvoicesCreateNfeReturn");

        Assert.Equal("Edm.Guid", action.Parameters.Single(p => p.Name == "Key").Type.FullName());
        Assert.Equal("Collection(Edm.Guid)", action.Parameters.Single(p => p.Name == "OriginItemKeys").Type.FullName());
        Assert.Equal("Collection(Edm.Double)", action.Parameters.Single(p => p.Name == "Quantities").Type.FullName());
        Assert.Equal("Edm.String", action.Parameters.Single(p => p.Name == "Reason").Type.FullName());
        Assert.Equal("Edm.Guid", action.ReturnType.FullName());
    }

    [Fact]
    public void Returnable_items_is_a_function_with_the_key()
    {
        var function = Model().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == "SalesInvoicesNfeReturnableItems");

        Assert.Equal("Edm.Guid", function.Parameters.Single(p => p.Name == "Key").Type.FullName());
        Assert.True(function.ReturnType.IsCollection());
    }

    [Fact]
    public void New_columns_are_exposed()
    {
        var model = Model();

        Assert.NotNull(model.EntityContainer.FindEntitySet("SalesInvoices")!.EntityType.FindProperty("IsNfeReturn"));
        Assert.NotNull(model.EntityContainer.FindEntitySet("SalesInvoicesItems")!.EntityType.FindProperty("NfeItemNumber"));
        Assert.NotNull(model.EntityContainer.FindEntitySet("Usages")!.EntityType.FindProperty("ReturnUsageCode"));
        Assert.NotNull(model.EntityContainer.FindEntitySet("Usages")!.EntityType.FindProperty("ReturnUsageName"));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeReturnEdmModelTests"`
Expected: FAIL nos dois primeiros (`Single` sem elemento); o terceiro já passa (as colunas entram pelo convention builder).

- [ ] **Step 3: EDM**

Em `ODataConfigurations.cs`, logo depois do bloco `salesInvoicesCompleteNfeConfirmation`:

```csharp

        // NF-e de devolução (spec 2026-10-04): cria a devolução própria a partir da venda autorizada.
        // ⚠️ Quantities em double, paralelo a OriginItemKeys, como no SalesInvoicesReturn: o UI5
        // serializa Edm.Decimal como string e o 400 não nomeia o campo.
        var salesInvoicesCreateNfeReturn = modelBuilder.Action("SalesInvoicesCreateNfeReturn");
        salesInvoicesCreateNfeReturn.Parameter<Guid>("Key");
        salesInvoicesCreateNfeReturn.CollectionParameter<Guid>("OriginItemKeys");
        salesInvoicesCreateNfeReturn.CollectionParameter<double>("Quantities");
        salesInvoicesCreateNfeReturn.Parameter<string>("Reason");
        salesInvoicesCreateNfeReturn.Returns<Guid>();

        var salesInvoicesNfeReturnableItems = modelBuilder.Function("SalesInvoicesNfeReturnableItems");
        salesInvoicesNfeReturnableItems.Parameter<Guid>("Key");
        salesInvoicesNfeReturnableItems.ReturnsCollection<SalesInvoiceNfeReturnableItemDto>();
```

- [ ] **Step 4: Controllers**

Criar `SiagroB1.Web/Actions/Nfe/SalesInvoicesCreateNfeReturnController.cs` (`git add`):

```csharp
using System.Collections;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Nfe;

/// <summary>
/// "Devolver": cria a devolução própria Pendente e devolve a chave dela. <c>Quantities</c> é um array
/// PARALELO a <c>OriginItemKeys</c> (contagens diferentes = erro de montagem, recusado aqui).
/// </summary>
public class SalesInvoicesCreateNfeReturnController(SalesInvoicesNfeReturnCreateService service) : ODataController
{
    [HttpPost("odata/SalesInvoicesCreateNfeReturn")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        // ⚠️ ODataActionParameters chega NULO quando falta um parâmetro declarado no EDM.
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || !Guid.TryParse(keyObj?.ToString(), out var key))
            return BadRequest("Parâmetro obrigatório: Key.");

        var itemKeys = parameters.TryGetValue("OriginItemKeys", out var keysObj) && keysObj is IEnumerable<Guid> keys
            ? keys.ToList()
            : [];
        var quantities = Quantities(parameters);

        if (itemKeys.Count == 0)
            return BadRequest("Informe a quantidade a devolver de ao menos um item.");

        if (quantities.Count != itemKeys.Count)
            return BadRequest("A lista de itens e a de quantidades têm tamanhos diferentes.");

        // ⚠️ TryGetValue devolve true com valor NULO: o ToString direto estoura.
        var reason = parameters.TryGetValue("Reason", out var reasonObj) ? reasonObj?.ToString() ?? string.Empty : string.Empty;

        try
        {
            var created = await service.ExecuteAsync(
                new SalesInvoiceNfeReturnRequest(
                    key, itemKeys.Select((itemKey, i) => new SalesInvoiceNfeReturnItem(itemKey, quantities[i])).ToList(), reason),
                User.Identity?.Name ?? "Unknown");

            return Ok(created.Key);
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
        catch (ApplicationException e)
        {
            return BadRequest(e.Message);
        }
    }

    private static List<decimal> Quantities(ODataActionParameters parameters)
    {
        if (!parameters.TryGetValue("Quantities", out var value) || value is not IEnumerable sequence)
            return [];

        var result = new List<decimal>();

        foreach (var item in sequence)
        {
            result.Add(item switch
            {
                double d => (decimal)d,
                decimal m => m,
                int i => i,
                long l => l,
                float f => (decimal)f,
                _ => decimal.TryParse(item?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : throw new ApplicationException($"Quantidade inválida: {item}"),
            });
        }

        return result;
    }
}
```

Criar `SiagroB1.Web/Functions/Nfe/SalesInvoicesNfeReturnableItemsController.cs` (`git add`):

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.Nfe;

/// <summary>Itens da venda com o saldo devolvível — a grade do diálogo "Devolver".</summary>
public class SalesInvoicesNfeReturnableItemsController(SalesInvoicesNfeReturnableItemsService service) : ODataController
{
    /// <summary>Rota declarada à mão, como todas as funções deste projeto: a forma não declarada toma 404.</summary>
    [EnableQuery]
    [HttpGet("odata/SalesInvoicesNfeReturnableItems(Key={key})")]
    public async Task<ActionResult<IEnumerable<SalesInvoiceNfeReturnableItemDto>>> Get([FromRoute] Guid key)
    {
        try
        {
            return Ok(await service.ExecuteAsync(key));
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
    }
}
```

- [ ] **Step 5: DI**

Em `ServiceCollectionExtensions.cs`, junto dos serviços `SalesInvoicesNfe*` (linhas ~343-355):

```csharp
        services.AddScoped<SalesInvoicesNfeReturnCreateService>();
        services.AddScoped<SalesInvoicesNfeReturnableItemsService>();
```

- [ ] **Step 6: Rodar e ver passar; build**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeReturnEdmModelTests|FullyQualifiedName~TaxationEdmModelTests"` → PASS.
Run: `dotnet build SiagroB1.sln` → 0 erros.

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Web/Actions/Nfe/SalesInvoicesCreateNfeReturnController.cs SiagroB1.Web/Functions/Nfe/SalesInvoicesNfeReturnableItemsController.cs SiagroB1.Application.Tests/Nfe/NfeReturnEdmModelTests.cs
git commit -F <msg> -- SiagroB1.Web/Actions/Nfe/SalesInvoicesCreateNfeReturnController.cs SiagroB1.Web/Functions/Nfe/SalesInvoicesNfeReturnableItemsController.cs SiagroB1.Web/ODataConfig/ODataConfigurations.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/Nfe/NfeReturnEdmModelTests.cs
```

Mensagem: `feat(invoice): endpoints do Devolver com NF-e`.

---

### Task 9: Frontend — helpers do diálogo e "Informar Nota Fiscal"

**Files (a partir de `siagro-b1-frontend/webapp/`):**
- Create: `helpers/NfeReturnHelpers.ts`, `test/unit/helpers/NfeReturnHelpers.qunit.ts`
- Modify: `helpers/NfeHelpers.ts`, `test/unit/helpers/NfeHelpers.qunit.ts`, `test/unit/unitTests.qunit.ts`

**Interfaces:**
- Produces: `type NfeReturnRow`, `type NfeReturnPayload`, `prefillNfeReturnRows(rows)`, `hasReturnableBalance(rows)`, `buildNfeReturnPayload(rows, reason)`; `isManualTaxDocumentBlocked(nfeStatus, taxLocked, invoiceType, isNfeReturn = false)`.

- [ ] **Step 1: Testes que falham**

Criar `test/unit/helpers/NfeReturnHelpers.qunit.ts` (`git add`):

```ts
import { prefillNfeReturnRows, hasReturnableBalance, buildNfeReturnPayload, NfeReturnRow } from "siagrob1/helpers/NfeReturnHelpers";

function row(overrides: Partial<NfeReturnRow> = {}): NfeReturnRow {
	return {
		OriginItemKey: "k1", ItemCode: "1", ItemName: "TRIGO", SoldQuantity: 30000, ReturnedQuantity: 10000,
		Returnable: 20000, ReturnQuantity: 20000, ...overrides,
	};
}

QUnit.module("NfeReturnHelpers");

QUnit.test("abre com o saldo de cada item preenchido", function (assert) {
	const rows = prefillNfeReturnRows([row({ ReturnQuantity: 0 }), row({ OriginItemKey: "k2", Returnable: 0, ReturnQuantity: 5 })]);
	assert.strictEqual(rows[0].ReturnQuantity, 20000);
	assert.strictEqual(rows[1].ReturnQuantity, 0);
});

QUnit.test("saldo a devolver existe se algum item tem saldo", function (assert) {
	assert.true(hasReturnableBalance([row({ Returnable: 0 }), row({ Returnable: 1 })]));
	assert.false(hasReturnableBalance([row({ Returnable: 0 })]));
});

QUnit.test("monta o corpo só com os itens que voltam", function (assert) {
	const result = buildNfeReturnPayload([row(), row({ OriginItemKey: "k2", ReturnQuantity: 0 })], "  Carga recusada ");
	assert.deepEqual(result, { ok: true, payload: { OriginItemKeys: ["k1"], Quantities: [20000], Reason: "Carga recusada" } });
});

QUnit.test("motivo é obrigatório", function (assert) {
	assert.deepEqual(buildNfeReturnPayload([row()], " "), { ok: false, message: "Informe o motivo da devolução." });
});

QUnit.test("ao menos um item precisa voltar", function (assert) {
	assert.deepEqual(buildNfeReturnPayload([row({ ReturnQuantity: 0 })], "x"),
		{ ok: false, message: "Informe a quantidade a devolver de ao menos um item." });
});

QUnit.test("quantidade acima do saldo é recusada", function (assert) {
	assert.deepEqual(buildNfeReturnPayload([row({ ReturnQuantity: 20001 })], "x"),
		{ ok: false, message: "Item 1: a quantidade a devolver passa do saldo (20000)." });
});

QUnit.test("quantidade negativa é recusada", function (assert) {
	assert.deepEqual(buildNfeReturnPayload([row({ ReturnQuantity: -1 })], "x"),
		{ ok: false, message: "Item 1: quantidade inválida." });
});

QUnit.test("campo apagado (null) fica de fora, sem erro", function (assert) {
	const result = buildNfeReturnPayload([row({ ReturnQuantity: null }), row({ OriginItemKey: "k2" })], "x");
	assert.deepEqual(result, { ok: true, payload: { OriginItemKeys: ["k2"], Quantities: [20000], Reason: "x" } });
});
```

Em `test/unit/helpers/NfeHelpers.qunit.ts`, no teste "regra de bloqueio por situação, trava e tipo", acrescentar:

```ts
	assert.strictEqual(isManualTaxDocumentBlocked("None", true, "Return", true), true, "CEAGUI devolução própria");
	assert.strictEqual(isManualTaxDocumentBlocked("None", false, "Return", true), true, "devolução própria vem da emissão");
```

Em `test/unit/unitTests.qunit.ts`, acrescentar ao final: `import "./helpers/NfeReturnHelpers.qunit";`

- [ ] **Step 2: Ver falhar**

Run: `yarn ts-typecheck`
Expected: FAIL — `Cannot find module 'siagrob1/helpers/NfeReturnHelpers'` e o 4º argumento de `isManualTaxDocumentBlocked`.

- [ ] **Step 3: Implementar**

Criar `helpers/NfeReturnHelpers.ts` (`git add`):

```ts
/** Linha do diálogo "Devolver": um item da venda e quanto dele ainda pode voltar. */
export type NfeReturnRow = {
	OriginItemKey: string;
	ItemCode: string;
	ItemName: string;
	SoldQuantity: number;
	ReturnedQuantity: number;
	Returnable: number;
	/** O que o usuário digitou; o tipo Float do campo entrega número, ou null se apagado. */
	ReturnQuantity: number | null;
};

/** Corpo de SalesInvoicesCreateNfeReturn, sem a Key (arrays PARALELOS). */
export type NfeReturnPayload = { OriginItemKeys: string[]; Quantities: number[]; Reason: string };

/** Abre o diálogo com o saldo de cada item já preenchido: a devolução total é o caso comum. */
export function prefillNfeReturnRows(rows: NfeReturnRow[]): NfeReturnRow[] {
	return rows.map((r) => ({ ...r, ReturnQuantity: r.Returnable }));
}

export function hasReturnableBalance(rows: NfeReturnRow[]): boolean {
	return rows.some((r) => Number(r.Returnable) > 0);
}

/**
 * Valida o diálogo e monta o corpo da action. Item com zero (ou apagado) fica de fora. Quem decide de
 * verdade é o servidor; isto só evita uma ida que voltaria recusada.
 */
export function buildNfeReturnPayload(
	rows: NfeReturnRow[], reason: string
): { ok: true; payload: NfeReturnPayload } | { ok: false; message: string } {
	const text = (reason ?? "").trim();

	if (text === "") {
		return { ok: false, message: "Informe o motivo da devolução." };
	}

	const keys: string[] = [];
	const quantities: number[] = [];

	for (const row of rows) {
		const quantity = row.ReturnQuantity === null || row.ReturnQuantity === undefined ? 0 : Number(row.ReturnQuantity);

		if (Number.isNaN(quantity) || quantity < 0) {
			return { ok: false, message: `Item ${row.ItemCode}: quantidade inválida.` };
		}

		if (quantity > Number(row.Returnable) + 0.0005) {
			return { ok: false, message: `Item ${row.ItemCode}: a quantidade a devolver passa do saldo (${row.Returnable}).` };
		}

		if (quantity > 0) {
			keys.push(row.OriginItemKey);
			quantities.push(quantity);
		}
	}

	if (keys.length === 0) {
		return { ok: false, message: "Informe a quantidade a devolver de ao menos um item." };
	}

	return { ok: true, payload: { OriginItemKeys: keys, Quantities: quantities, Reason: text } };
}
```

Em `helpers/NfeHelpers.ts`, trocar `isManualTaxDocumentBlocked` e seu comentário por:

```ts
/**
 * "Informar Nota Fiscal" manual não vale quando a NF-e já foi emitida, no documento Normal de filial
 * que emite pelo Siagro, nem na devolução própria (criada pelo Devolver, sai com NF-e do Siagro). As
 * demais devoluções seguem manuais: o cliente emite a NF-e dele.
 */
export function isManualTaxDocumentBlocked(
	nfeStatus: string, taxLocked: boolean, invoiceType: string, isNfeReturn = false
): boolean {
	return isEmittedNfeStatus(nfeStatus) || isNfeReturn === true || (taxLocked === true && invoiceType === "Normal");
}
```

- [ ] **Step 4: Ver passar**

Run: `yarn ts-typecheck` e `yarn lint` → exit 0.
Run (QUnit): `npx ui5 serve --port 8080` em segundo plano; `npx ui5-test-runner --url http://localhost:8080/test/testsuite.qunit.html` → todos passam (154 anteriores + 10 novos). Encerrar o `ui5 serve` pelo PID.

- [ ] **Step 5: Commit (frontend)**

```bash
git add webapp/helpers/NfeReturnHelpers.ts webapp/test/unit/helpers/NfeReturnHelpers.qunit.ts
git commit -F <msg> -- webapp/helpers/NfeReturnHelpers.ts webapp/helpers/NfeHelpers.ts webapp/test/unit/helpers/NfeReturnHelpers.qunit.ts webapp/test/unit/helpers/NfeHelpers.qunit.ts webapp/test/unit/unitTests.qunit.ts
```

Mensagem: `feat(invoice): validação do diálogo Devolver e bloqueio do número manual na devolução`.

---

### Task 10: Frontend — campo "Natureza de devolução"

**Files (a partir de `siagro-b1-frontend/webapp/`):**
- Create: `controller/usages/BaseController.ts`
- Modify: `controller/usages/Add.controller.ts`, `controller/usages/Edit.controller.ts`, `view/usages/fragments/Form.fragment.xml`

**Interfaces:**
- Consumes: `UsageModel.ReturnUsageCode`/`ReturnUsageName` (Task 1), `DialogHelper.openTableSelectDialog(controller, "UsagesSelectDialog", filters, defaultFilters, elementPath, staticFilter)`.
- Produces: handlers `onReturnUsageValueHelp`, `onReturnUsageChange`.

- [ ] **Step 1: Base das telas de natureza**

Criar `controller/usages/BaseController.ts` (`git add`):

```ts
import { Input$ChangeEvent, Input$ValueHelpRequestEvent } from "sap/m/Input";
import Context from "sap/ui/model/odata/v4/Context";
import Filter from "sap/ui/model/Filter";
import FilterOperator from "sap/ui/model/FilterOperator";
import DialogHelper from "siagrob1/dialogs/DialogHelper";
import RootBaseController from "../BaseController";

/**
 * Base das telas de natureza (inclusão e edição): o value help da natureza de devolução.
 * @namespace siagrob1.controller.usages
 */
export default class BaseController extends RootBaseController {

	/**
	 * Só natureza de ENTRADA ativa: o enum vai como $filter estático (o Filter do UI5 não formata
	 * enum). setProperty sem await: no update group diferido a Promise só resolve no submit.
	 */
	async onReturnUsageValueHelp(ev: Input$ValueHelpRequestEvent) {
		const oTarget = ev.getSource().getBindingContext() as Context;

		const oSelected = await DialogHelper.openTableSelectDialog(
			this, "UsagesSelectDialog", ["Name", "Description"],
			[new Filter("Inactive", FilterOperator.EQ, false)], undefined, "Direction eq 'Incoming'");

		if (!oSelected) {
			return;
		}

		void oTarget.setProperty("ReturnUsageCode", oSelected.getProperty("Code"));
		void oTarget.setProperty("ReturnUsageName", oSelected.getProperty("Name"));
	}

	/** Limpar o código (ícone de limpar) limpa também o nome exibido ao lado. */
	onReturnUsageChange(ev: Input$ChangeEvent) {
		if (ev.getParameter("value")) {
			return;
		}

		const oTarget = ev.getSource().getBindingContext() as Context;
		void oTarget.setProperty("ReturnUsageName", null);
	}
}
```

Em `controller/usages/Add.controller.ts` e `controller/usages/Edit.controller.ts`, trocar `import BaseController from "../BaseController";` por `import BaseController from "./BaseController";`.

Em `controller/usages/Add.controller.ts`, no objeto do `oBinding.create({...})`, depois de `CfopIncomingOutState: null,`:

```ts
			// Natureza de devolução (NF-e de devolução de venda) — só natureza de Saída.
			ReturnUsageCode: null,
			ReturnUsageName: null,
```

- [ ] **Step 2: Campo no formulário**

Em `view/usages/fragments/Form.fragment.xml`, logo depois do `Input` de `InvoiceOperationText` (o "Texto na nota (natOp)"):

```xml
          <!-- Natureza de devolução (NF-e de devolução de venda): só natureza de Saída, só STANDALONE.
               A devolução criada pelo Devolver herda esta natureza em cada linha. -->
          <Label text="Natureza de devolução"
                 visible="{= ${ui>/identityEditable} === true &amp;&amp; ${path: 'Direction', targetType: 'any'} !== 'Incoming' }" />
          <Input
              value="{ReturnUsageCode}"
              showValueHelp="true"
              valueHelpOnly="true"
              showClearIcon="true"
              valueHelpRequest=".onReturnUsageValueHelp"
              change=".onReturnUsageChange"
              visible="{= ${ui>/identityEditable} === true &amp;&amp; ${path: 'Direction', targetType: 'any'} !== 'Incoming' }">
            <layoutData>
              <l:GridData span="XL2 L2 M3 S4" />
            </layoutData>
          </Input>
          <Input
              value="{ReturnUsageName}"
              editable="false"
              visible="{= ${ui>/identityEditable} === true &amp;&amp; ${path: 'Direction', targetType: 'any'} !== 'Incoming' }" />
```

- [ ] **Step 3: Gates**

Run: `yarn ts-typecheck`, `yarn lint` → exit 0; `npx ui5lint` → **920** problemas (nenhum novo em `usages`).

- [ ] **Step 4: Commit (frontend)**

```bash
git add webapp/controller/usages/BaseController.ts
git commit -F <msg> -- webapp/controller/usages/BaseController.ts webapp/controller/usages/Add.controller.ts webapp/controller/usages/Edit.controller.ts webapp/view/usages/fragments/Form.fragment.xml
```

Mensagem: `feat(master-data): campo Natureza de devolução na natureza de saída`.

---

### Task 11: Frontend — "Devolver", diálogo e travas da devolução própria

**Files (a partir de `siagro-b1-frontend/webapp/`):**
- Create: `view/salesInvoices/fragments/NfeReturnDialog.fragment.xml`
- Modify: `model/ServerRoutes.ts`, `controller/salesInvoices/BaseController.ts`, `controller/salesInvoices/Detail.controller.ts`, `controller/salesInvoices/Edit.controller.ts`, `controller/salesInvoices/Main.controller.ts`, `view/salesInvoices/Detail.view.xml`, `view/salesInvoices/Main.view.xml`, `view/salesInvoices/fragments/Form.fragment.xml`, `view/salesInvoices/fragments/Items.fragment.xml`

**Interfaces:**
- Consumes: Task 8 (endpoints), Task 9 (helpers).
- Produces: `ServerRoutes.salesInvoicesCreateNfeReturn = '/SalesInvoicesCreateNfeReturn(...)'`, `ServerRoutes.salesInvoicesNfeReturnableItems = '/odata/SalesInvoicesNfeReturnableItems'`; flag `ui>/nfeReturn`.

- [ ] **Step 1: Rotas**

Em `model/ServerRoutes.ts`, no bloco "NF-e STANDALONE no documento de saída", depois de `danfeReport`:

```ts
  // NF-e de devolução: "Devolver" cria a devolução própria (bindContext); os itens devolvíveis vêm por fetch.
  salesInvoicesCreateNfeReturn: '/SalesInvoicesCreateNfeReturn(...)',
  salesInvoicesNfeReturnableItems: '/odata/SalesInvoicesNfeReturnableItems',
```

- [ ] **Step 2: Flag da devolução própria**

Em `controller/salesInvoices/BaseController.ts`, logo depois de `refreshNfeHeaderFromContext`:

```ts
    /** Devolução com NF-e própria (criada pelo Devolver): a grade trava produto, preço e natureza. */
    protected async refreshNfeReturnFromContext() {
      const uiModel = this.getModel("ui") as JSONModel;
      uiModel.setProperty("/nfeReturn", false);
      const oContext = this.getView().getBindingContext() as Context;
      const nfeReturn = oContext ? await oContext.requestProperty("IsNfeReturn") === true : false;
      uiModel.setProperty("/nfeReturn", nfeReturn);
    }
```

Em `controller/salesInvoices/Detail.controller.ts` e `controller/salesInvoices/Edit.controller.ts`, logo depois de `void this.refreshNfeHeaderFromContext();`:

```ts
			void this.refreshNfeReturnFromContext();
```

- [ ] **Step 3: Botões do detalhe**

Em `view/salesInvoices/Detail.view.xml`, trocar o `visible` do "Confirmar" por:

```xml
            visible="{= ${ui>/taxLocked} !== true || (${path: 'InvoiceType', targetType: 'any'} !== 'Normal' &amp;&amp; ${IsNfeReturn} !== true) }"
```

trocar o `visible` do "Emitir NF-e" por:

```xml
            visible="{= ${ui>/taxLocked} === true &amp;&amp; (${path: 'InvoiceType', targetType: 'any'} === 'Normal' || ${IsNfeReturn} === true) }"
```

e acrescentar, logo depois do botão "XML":

```xml
          <!-- NF-e de devolução: a venda autorizada (sem carga) gera a devolução própria, que é
               emitida pelo mesmo "Emitir NF-e". -->
          <Button text="Devolver" icon="sap-icon://undo" press=".onNfeReturn"
            visible="{= ${ui>/taxLocked} === true &amp;&amp; ${path: 'InvoiceType', targetType: 'any'} === 'Normal' &amp;&amp; ${path: 'InvoiceStatus', targetType: 'any'} === 'Confirmed' &amp;&amp; ${path: 'NfeStatus', targetType: 'any'} === 'Authorized' &amp;&amp; !${ShipmentLoadKey} }" />
```

Atualizar o comentário acima do "Confirmar" para "emitir é o que confirma o documento Normal e a devolução própria".

- [ ] **Step 4: Diálogo**

Criar `view/salesInvoices/fragments/NfeReturnDialog.fragment.xml` (`git add`):

```xml
<core:FragmentDefinition
	xmlns="sap.m"
	xmlns:core="sap.ui.core"
	xmlns:f="sap.ui.layout.form">
	<Dialog
		core:require="{ Float: 'sap/ui/model/type/Float' }"
		title="Devolver Documento de Saída"
		contentWidth="60rem"
		class="sapUiSizeCompact"
		busy="{nfeReturn>/busy}"
		busyIndicatorDelay="0">
		<content>
			<!-- A quantidade a devolver nasce igual ao saldo de cada item: a devolução total é o caso
			     comum. Item com zero fica de fora da devolução. -->
			<Table items="{nfeReturn>/rows}">
				<columns>
					<Column><Text text="Produto" /></Column>
					<Column><Text text="Descrição" /></Column>
					<Column hAlign="End"><Text text="Vendido" /></Column>
					<Column hAlign="End"><Text text="Já devolvido" /></Column>
					<Column hAlign="End" width="12rem"><Text text="A devolver" /></Column>
				</columns>
				<items>
					<ColumnListItem>
						<cells>
							<Text text="{nfeReturn>ItemCode}" />
							<Text text="{nfeReturn>ItemName}" />
							<Text text="{ path: 'nfeReturn>SoldQuantity', type: 'Float', formatOptions: { minFractionDigits: 3, maxFractionDigits: 3 } }" />
							<Text text="{ path: 'nfeReturn>ReturnedQuantity', type: 'Float', formatOptions: { minFractionDigits: 3, maxFractionDigits: 3 } }" />
							<Input
								textAlign="End"
								value="{ path: 'nfeReturn>ReturnQuantity', type: 'Float', formatOptions: { maxFractionDigits: 3 } }" />
						</cells>
					</ColumnListItem>
				</items>
			</Table>
			<f:SimpleForm editable="true" layout="ResponsiveGridLayout" labelSpanL="2" labelSpanM="3">
				<f:content>
					<Label text="Motivo da devolução" required="true" />
					<TextArea
						rows="3"
						maxLength="400"
						placeholder="Ex.: carga recusada pelo cliente"
						value="{nfeReturn>/reason}" />
				</f:content>
			</f:SimpleForm>
		</content>
		<beginButton>
			<Button text="Devolver" type="Emphasized" press=".onConfirmNfeReturn" />
		</beginButton>
		<endButton>
			<Button text="Cancelar" press=".onCloseNfeReturn" />
		</endButton>
	</Dialog>
</core:FragmentDefinition>
```

- [ ] **Step 5: Controller do detalhe**

Em `controller/salesInvoices/Detail.controller.ts`, acrescentar o import:

```ts
import { NfeReturnRow, prefillNfeReturnRows, hasReturnableBalance, buildNfeReturnPayload } from "siagrob1/helpers/NfeReturnHelpers";
```

e os métodos (depois de `onNfeXml`):

```ts
  private _nfeReturnDialog: Dialog;

  /** "Devolver": abre o diálogo com os itens da venda e o saldo devolvível de cada um. */
  async onNfeReturn() {
    const ctx = this.getView().getBindingContext() as Context;
    if (!ctx) {
      return;
    }

    let rows: NfeReturnRow[] = [];
    this.setBusy(true);
    try {
      const result = await sendJson(
        "GET", `${ServerRoutes.salesInvoicesNfeReturnableItems}(Key=${ctx.getProperty("Key") as string})`);

      if (!result.ok) {
        MessageBox.error(result.message);
        return;
      }

      rows = odataValue<NfeReturnRow[]>(result.data) ?? [];
    } finally {
      this.setBusy(false);
    }

    if (!hasReturnableBalance(rows)) {
      MessageBox.information("Esta venda não tem saldo a devolver.");
      return;
    }

    this.getView().setModel(new JSONModel({ rows: prefillNfeReturnRows(rows), reason: "", busy: false }), "nfeReturn");

    this._nfeReturnDialog ??= await DialogHelper.createDialog(
      this, "siagrob1.view.salesInvoices.fragments.NfeReturnDialog");
    this._nfeReturnDialog.open();
  }

  onCloseNfeReturn() {
    this._nfeReturnDialog?.close();
  }

  /** Cria a devolução própria e abre a tela dela, onde fica o "Emitir NF-e". */
  async onConfirmNfeReturn() {
    const ctx = this.getView().getBindingContext() as Context;
    const model = this.getView().getModel("nfeReturn") as JSONModel;
    const built = buildNfeReturnPayload(
      model.getProperty("/rows") as NfeReturnRow[], model.getProperty("/reason") as string);

    if (!built.ok) {
      MessageBox.warning(built.message);
      return;
    }

    model.setProperty("/busy", true);
    try {
      const action = (ctx.getModel() as ODataModel).bindContext(ServerRoutes.salesInvoicesCreateNfeReturn);
      action.setParameter("Key", ctx.getProperty("Key"));
      action.setParameter("OriginItemKeys", built.payload.OriginItemKeys);
      action.setParameter("Quantities", built.payload.Quantities);
      action.setParameter("Reason", built.payload.Reason);
      await action.invoke();

      const key = action.getBoundContext().getProperty("value") as string;
      this._nfeReturnDialog.close();
      MessageToast.show("Devolução criada. Confira os dados e emita a NF-e.", { closeOnBrowserNavigation: false });
      this.navTo("salesInvoicesDetail", { id: key });
    } catch {
      // O handler global de mensagens do OData (Component) já mostrou o erro do servidor.
    } finally {
      model.setProperty("/busy", false);
    }
  }
```

- [ ] **Step 6: Travas visuais e "Informar Nota Fiscal"**

Em `view/salesInvoices/fragments/Form.fragment.xml`:
- nos três `Input` de Número/Série/Chave (`editable="{= ${ui>/editable} === true && !(${ui>/taxLocked} === true && ... 'Normal') && ...}"`), acrescentar `&amp;&amp; ${IsNfeReturn} !== true` logo depois de `${ui>/editable} === true`;
- no `Select` da Filial e no `Input` do Cliente, trocar `editable="{ui>/editable}"` por `editable="{= ${ui>/editable} === true &amp;&amp; ${IsNfeReturn} !== true }"`.

Em `view/salesInvoices/fragments/Items.fragment.xml`:
- no botão "Incluir", trocar `visible="{ui>/editable}"` por `visible="{= ${ui>/editable} === true &amp;&amp; ${ui>/nfeReturn} !== true }"`;
- nos `Input` das colunas Produto (`{ItemCode}`), Und.Med. (`{UnitOfMeasureCode}`), Valor Unitário (`UnitPrice`), Contrato e Natureza de Operação (`{UsageName}`), trocar `editable="{ui>/editable}"` por `editable="{= ${ui>/editable} === true &amp;&amp; ${ui>/nfeReturn} !== true }"`. A Quantidade continua `{ui>/editable}`.

Em `view/salesInvoices/Main.view.xml`, no `$select` do binding da tabela (linha ~48), acrescentar `,IsNfeReturn` ao fim da lista e mencionar no comentário acima que ele é lido pelo "Informar Nota Fiscal".

Em `controller/salesInvoices/Main.controller.ts`, em `onNotaFiscal`, trocar a chamada por:

```ts
    if (isManualTaxDocumentBlocked(nfeStatus, taxLocked, invoiceType, ctx.getProperty("IsNfeReturn") === true)) {
```

e o comentário acima ("Devolução fica de fora...") por: "A devolução própria (Devolver) também vem da emissão; as demais devoluções seguem manuais: o cliente emite a NF-e dele e o número é digitado."

- [ ] **Step 7: Gates**

Run: `yarn ts-typecheck`, `yarn lint` → exit 0; `npx ui5lint` → **920** (o `core:require` evita o global do tipo Float); QUnit como na Task 9 → todos passam.

- [ ] **Step 8: Commit (frontend)**

```bash
git add webapp/view/salesInvoices/fragments/NfeReturnDialog.fragment.xml
git commit -F <msg> -- webapp/model/ServerRoutes.ts webapp/controller/salesInvoices/BaseController.ts webapp/controller/salesInvoices/Detail.controller.ts webapp/controller/salesInvoices/Edit.controller.ts webapp/controller/salesInvoices/Main.controller.ts webapp/view/salesInvoices/Detail.view.xml webapp/view/salesInvoices/Main.view.xml webapp/view/salesInvoices/fragments/Form.fragment.xml webapp/view/salesInvoices/fragments/Items.fragment.xml webapp/view/salesInvoices/fragments/NfeReturnDialog.fragment.xml
```

Mensagem: `feat(invoice): botão Devolver e travas da devolução com NF-e`.

---

### Task 12: Verificação ponta a ponta e memória

**Files:** nenhum código. Memória: `C:\Users\Penalva\.claude\projects\C--Projetos-SiagroB1\memory\nfe-sales-return-issuance-feature.md` (+ índice).

- [ ] **Step 1: Suítes completas**

Run (backend): `dotnet build SiagroB1.sln`; `dotnet test SiagroB1.Fiscal.Tests`; `dotnet test SiagroB1.Application.Tests` → 0 erros, tudo verde (anote os totais).
Run (frontend): `yarn ts-typecheck`, `yarn lint`, `npx ui5lint` (920), QUnit → verde.

- [ ] **Step 2: Subir a stack `ceagui`**

Como em `run-stack-locally` (memória): `Nfe__CertificateKey` vem da variável de ambiente do USUÁRIO do Windows; Gateway e Web com `--launch-profile ceagui`; frontend `npx ui5 serve --port 8080`. O Web recusa subir com migration pendente — a da Task 1 já está aplicada no `CEAGUI_SIAGRO_DEV`. Login admin/1234, filial 01.

- [ ] **Step 3: Caminho do usuário (Playwright)**

1. Naturezas → natureza 2 "VENDA DE MERCADORIA (SUSPENSAO PIS COFINS)" → Natureza de devolução = 5 → Salvar. Conferir que a natureza 5 (Entrada) não mostra o campo.
2. Documentos de Saída → DS000146 (NF-e série 9 nº 3, autorizada em homologação em 04/10) → "Devolver" aparece. Abrir: a grade traz 13.000 de saldo; digitar 5.000; motivo "Teste de devolução parcial" → Devolver. A tela navega para a devolução nova: Pendente, tipo Retorno, CFOP 1202, ICMS 51 + SP053521, IBS/CBS 200/200036, peso 5.000, sem condição de pagamento; Produto/Preço/Natureza travados; Quantidade editável; "Emitir NF-e" visível, "Confirmar" oculto.
3. "Informar Nota Fiscal" na lista com a devolução selecionada → mensagem de bloqueio.
4. Se alguma conferência recusar (mensagem "não reproduz a tributação da venda"), anote o campo: é dado de cadastro das naturezas 2/5 — reporte ao usuário em vez de mudar o cadastro por conta própria.
5. **Emissão em homologação: só com pedido explícito do usuário na hora** (dado real). Com o pedido, "Emitir NF-e" → autorizada; baixar o XML e conferir `tpNF 0`, `finNFe 4`, `refNFe` = chave do DS000146, `DFeReferenciado` com `nItem 1`, `tPag 90`, sem `cobr`, `infCpl` com a referência. Comparar com `dados-ceagui-efiscloud/xml/1507.xml`. DANFE abre. A venda DS000146 fica com 5.000 devolvidos (selo de devolução parcial) e continua Confirmada.
6. Encerrar Gateway/Web/ui5 serve pelo PID; **não** encerrar o `SiagroB1.Reports` aberto pelo Rider do usuário. Apagar `C:\Projetos\SiagroB1\.playwright-mcp` (prints com dado real).

- [ ] **Step 4: Memória**

Atualizar `nfe-sales-return-issuance-feature.md`: estado (implementado, commits, verificado em tela, emissão feita ou não), armadilhas aprendidas; linha do índice em `MEMORY.md`.

- [ ] **Step 5: Relatório ao usuário**

Commits dos dois repos, o que foi verificado e como, o que ficou de fora (romaneio/carga, venda de fora do Siagro, cancelamento/CC-e da devolução), e o lembrete de que o merge é decisão dele.
