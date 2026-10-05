# Frete, seguro, desconto e outras despesas na linha dos documentos de entrada e saída — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cada linha do Documento de Saída e do Documento de Entrada ganha frete, seguro, desconto e outras despesas; o total geral (itens + frete + seguro + outras despesas − desconto) passa a ser a base dos tributos, o `vNF`/pagamento/fatura da NF-e, o valor declarado da entrada e o total mostrado nas telas; a devolução traz os valores na proporção do que volta.

**Architecture:** Backend: quatro colunas `DECIMAL(18,2)` nas duas tabelas de linha (migration única), `GrandTotal` `[NotMapped]` na linha e no cabeçalho (exposto no EDM), uma regra estática compartilhada (`InvoiceLineChargeRules`: centavos, validação R2 e proporção D4) chamada por todos os caminhos que gravam linha; o motor de tributos recebe `GrandTotal` como base; `NfeItem`/`INfeTaxedLine` levam os quatro valores ao `det/prod` e ao `ICMSTot`; o leitor do XML do fornecedor lê `vFrete/vSeg/vDesc/vOutro`. Frontend: um helper puro (`InvoiceChargeTotalsHelpers`) soma linha e documento no cliente; a grade ganha quatro colunas `Double`, a coluna "Total" vira o total geral, o Detail ganha a seção "Totais" e as listas mostram `GrandTotal`.

**Tech Stack:** .NET 10, EF Core (SQL Server; testes com InMemory), OData v4, xUnit, Zeus.Net.NFe.NFCe; OpenUI5 1.141 TypeScript, QUnit (ui5-test-runner).

**Spec:** `docs/superpowers/specs/2026-10-05-invoice-line-charges-design.md` (commit 28ba41e).

## Global Constraints

- Repos e branch: backend `C:\Projetos\SiagroB1\siagro-b1-backend` e frontend `C:\Projetos\SiagroB1\siagro-b1-frontend`, ambos já no branch `feature/invoice-line-charges` (criados de `main`). Conferir o branch (`git branch --show-current`) antes de cada commit.
- **Regra ativa** = `Erp == "STANDALONE"` **e** `Branch.IssuesNfe`, sempre perguntada ao `TaxCalculationGate.IsActiveAsync(branchCode)` (ou `taxApply.IsBranchActiveAsync` / `IsActiveForAsync`). Os quatro campos valem em **todas** as filiais (R1, padrão 0 — nada muda onde não são preenchidos); o efeito nos tributos, na NF-e e no valor declarado só existe com a regra ativa. Sem ela (Yokotobi/SAPB1, MH Agro): a linha só guarda os valores.
- Definições (spec §4.2, D2): `Total` da linha continua = `Round(Quantity × UnitPrice, 2)` (é o `vProd`); `GrandTotal` da linha = `Total + FreightValue + InsuranceValue + OtherExpensesValue − DiscountValue`; `GrandTotal` do documento = Σ `GrandTotal` das linhas.
- Mensagens ao usuário em pt-BR, exatamente como na spec: `"Item {código}: frete, seguro, desconto e outras despesas não podem ser negativos."` e `"Item {código}: o desconto passa do valor da linha."`; rótulos de tela "Frete", "Seguro", "Desconto", "Outras despesas", "Total", "Totais", "Total dos itens", "(−) Desconto", "Total geral", "Total do Documento". Identificadores, tabelas e colunas em inglês; comentários em pt-BR.
- Migration única `AddInvoiceLineCharges` (`AppDbContext`). Gerar a partir de `siagro-b1-backend/` com `ASPNETCORE_ENVIRONMENT=Ceagui-Development`. **Ler a migration gerada antes de seguir.** Aplicar só no `CEAGUI_SIAGRO_DEV` (Task 9), nunca em outro banco.
- Commits: mensagem no padrão do `CLAUDE.md` do repo (`tipo(escopo): descrição em pt-BR`; escopo `invoice`); o commit da migration leva o trailer `DB: AddInvoiceLineCharges`; acrescentar no fim da mensagem as linhas de atribuição que o harness pedir. Sempre com pathspec explícito: **nunca** incluir os arquivos já staged `docs/superpowers/{specs,plans}/2026-10-01-nfe-standalone-taxation*` (backend) nem `.vscode/.advpl/*.tlpp` (frontend). Todo arquivo novo é `git add` logo depois de criado. **Nunca dar push.** Um assunto por commit; a mudança que cruza os dois repos são dois commits.
- Testes de backend: `dotnet build SiagroB1.sln` com 0 erros; `dotnet test SiagroB1.Application.Tests` e `dotnet test SiagroB1.Fiscal.Tests` inteiros verdes ao fim de cada task de backend. As asserções dos testes existentes não mudam, **exceto** as listadas nas tasks (cada uma com o motivo) — os valores novos nascem 0, então todo teste antigo (ex.: `vNF` = Σ produtos, base = quantidade × preço) continua valendo como está.
- **Arquivo travado:** o usuário costuma rodar a stack dele a partir das pastas `bin` dos repos, o que trava `bin/Debug`. Se `dotnet build`/`dotnet test` falhar com erro de arquivo em uso (MSB3027/MSB3021/CS2012), repetir com `--artifacts-path "$TEMP/siagrob1-artifacts"` (ex.: `dotnet test SiagroB1.Application.Tests --artifacts-path "$TEMP/siagrob1-artifacts"`). O `dotnet ef` lê o `bin/Debug` do `SiagroB1.Web`: se a geração da migration esbarrar no travamento, pedir ao usuário para derrubar a stack dele — nunca matar processo alheio.
- Frontend: `yarn ts-typecheck`, `yarn lint`, `npx ui5lint` (≤ 919 problemas) e a suíte QUnit de unidade: subir `npx ui5 serve --port 8081` em background a partir de `siagro-b1-frontend/` e rodar `npx ui5-test-runner --url "http://localhost:8081/test/Test.qunit.html?testsuite=test-resources/siagrob1/testsuite.qunit&test=unit/unitTests" --report-dir "$TEMP/siagrob1-qunit-report"` (relatório FORA do repo; não deixar pasta `report/` no repo); parar o servidor pelo PID no fim. **Nunca mexer na porta 8080 nem em processos que você não subiu** — o usuário costuma ter a própria stack de pé.
- UI5: campo de ESCRITA de decimal usa `sap.ui.model.odata.type.Double` (nunca `Decimal`: o backend recusa Edm.Decimal em string, 400 sem nome do campo); expressões/partes de binding sobre propriedades OData com `targetType: 'any'`; nada de `--` dentro de comentário XML; o modelo `ui` é do componente (Add/Edit/Detail das duas telas escrevem nele).

## Review Focus

1. **Desconto do valor inteiro da linha** (bonificação: desconto = itens + frete + seguro + outras despesas): o operador espera que grave, com o total geral da linha em 0 — só o desconto ACIMA disso é recusado. (Task 2)
2. **Devolução parcial que não divide exato:** frete 100,00 devolvendo 1/3 vira 33,33; outras despesas 0,05 devolvendo a metade vira 0,03 (`AwayFromZero`, e não 0,02 do arredondamento bancário); devolvendo tudo, os valores vêm exatamente iguais aos da origem. (Task 6)
3. **Documento sem nenhum dos quatro valores** (toda nota que existe hoje): o XML sai como hoje — `det/prod` sem `vFrete/vSeg/vDesc/vOutro`, `ICMSTot` com esses totais em 0 e `vNF` = Σ `vProd`. (Task 4)
4. **Linha com redução de base do ICMS (CST 20) e frete:** a redução incide sobre o total geral da linha (itens + frete + seguro + outras despesas − desconto), não só sobre o produto. (Task 3)
5. **Devolução Pendente com frete editado pelo operador:** o PATCH da grade (que reenvia a linha inteira) mantém o frete/desconto digitados — a trava da devolução volta produto, preço e natureza, não os quatro valores (spec D4: "editável enquanto Pendente"). (Task 2; base dos tributos conferida na Task 3)

---

### Task 1: Colunas, totais calculados e EDM (+ migration)

**Files:**
- Modify: `SiagroB1.Domain/Entities/SalesInvoiceItem.cs` (depois de `Total`, ~linha 36)
- Modify: `SiagroB1.Domain/Entities/PurchaseInvoiceItem.cs` (depois de `Total`, ~linha 194)
- Modify: `SiagroB1.Domain/Entities/SalesInvoice.cs` (depois de `TotalInvoiceIbsCbs`, ~linha 198)
- Modify: `SiagroB1.Domain/Entities/PurchaseInvoice.cs` (depois de `TotalInvoiceIbsCbs`, ~linha 203)
- Modify: `SiagroB1.Domain/Interfaces/INfeTaxedLine.cs` (depois de `decimal Total { get; }`)
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (~linhas 251-305)
- Create (gerado): `SiagroB1.Migrations/AppContext/<timestamp>_AddInvoiceLineCharges.cs` (+ Designer, snapshot)
- Test: `SiagroB1.Application.Tests/Invoices/InvoiceLineChargesModelTests.cs`

**Interfaces:**
- Produces: em `SalesInvoiceItem` e `PurchaseInvoiceItem`: `decimal FreightValue`, `decimal InsuranceValue`, `decimal DiscountValue`, `decimal OtherExpensesValue` (colunas `DECIMAL(18,2)`, padrão 0) e `[NotMapped] decimal GrandTotal`. Em `INfeTaxedLine`: os quatro com `{ get; set; }` e `decimal GrandTotal { get; }`. Em `SalesInvoice` e `PurchaseInvoice`: `[NotMapped] decimal TotalFreight`, `TotalInsurance`, `TotalOtherExpenses`, `TotalDiscount`, `GrandTotal`. EDM: todas essas propriedades em `SalesInvoices`, `SalesInvoicesItems`, `PurchaseInvoices`, `PurchaseInvoicesItems` (`Edm.Decimal`).

- [ ] **Step 1: Escrever os testes que falham**

`SiagroB1.Application.Tests/Invoices/InvoiceLineChargesModelTests.cs` (pasta nova; `git add` logo depois de criar):

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Invoices;

/// <summary>
/// Frete, seguro, desconto e outras despesas da linha (spec 2026-10-05 §4): colunas novas nas duas linhas, total geral
/// da linha e do documento (D2) e as propriedades calculadas no EDM.
/// </summary>
public class InvoiceLineChargesModelTests
{
    private static SalesInvoiceItem SalesLine(
        decimal quantity, decimal price, decimal freight = 0m, decimal insurance = 0m, decimal discount = 0m, decimal other = 0m) => new()
    {
        Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = quantity, UnitPrice = price,
        FreightValue = freight, InsuranceValue = insurance, DiscountValue = discount, OtherExpensesValue = other,
    };

    private static PurchaseInvoiceItem PurchaseLine(
        decimal quantity, decimal price, decimal freight = 0m, decimal insurance = 0m, decimal discount = 0m, decimal other = 0m) => new()
    {
        Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = quantity, UnitPrice = price,
        FreightValue = freight, InsuranceValue = insurance, DiscountValue = discount, OtherExpensesValue = other,
    };

    [Fact]
    public void Sales_line_grand_total_adds_freight_insurance_and_other_expenses_and_subtracts_the_discount()
    {
        var line = SalesLine(1000m, 1.5m, freight: 100m, insurance: 20m, discount: 50m, other: 30m);

        Assert.Equal((1500m, 1600m), (line.Total, line.GrandTotal));
    }

    [Fact]
    public void Purchase_line_grand_total_adds_freight_insurance_and_other_expenses_and_subtracts_the_discount()
    {
        var line = PurchaseLine(1000m, 1.5m, freight: 100m, insurance: 20m, discount: 50m, other: 30m);

        Assert.Equal((1500m, 1600m), (line.Total, line.GrandTotal));
    }

    [Fact]
    public void Line_without_charges_keeps_the_grand_total_equal_to_the_products()
    {
        Assert.Equal(60000m, SalesLine(30000m, 2m).GrandTotal);
        Assert.Equal(1500m, PurchaseLine(1000m, 1.5m).GrandTotal);
    }

    [Fact]
    public void Sales_document_totals_sum_the_lines()
    {
        var invoice = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1" };
        invoice.AddItem(SalesLine(1000m, 1.5m, freight: 100m, insurance: 20m, discount: 50m, other: 30m));
        invoice.AddItem(SalesLine(10m, 2m, freight: 5m, discount: 1m));

        Assert.Equal((1520m, 105m, 20m, 30m, 51m, 1624m),
            (invoice.TotalInvoiceItems, invoice.TotalFreight, invoice.TotalInsurance, invoice.TotalOtherExpenses,
                invoice.TotalDiscount, invoice.GrandTotal));
    }

    [Fact]
    public void Purchase_document_totals_sum_the_lines()
    {
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F1" };
        invoice.AddItem(PurchaseLine(1000m, 1.5m, freight: 100m, insurance: 20m, discount: 50m, other: 30m));
        invoice.AddItem(PurchaseLine(10m, 2m, freight: 5m, discount: 1m));

        Assert.Equal((1520m, 105m, 20m, 30m, 51m, 1624m),
            (invoice.TotalInvoiceItems, invoice.TotalFreight, invoice.TotalInsurance, invoice.TotalOtherExpenses,
                invoice.TotalDiscount, invoice.GrandTotal));
    }

    [Fact]
    public async Task Charges_are_persisted_on_both_lines()
    {
        var db = TestDb.CreateUnitOfWork();
        var sale = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1" };
        sale.AddItem(SalesLine(1m, 1m, freight: 1.1m, insurance: 2.2m, discount: 0.3m, other: 4.4m));
        var purchase = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F1" };
        purchase.AddItem(PurchaseLine(1m, 1m, freight: 1.1m, insurance: 2.2m, discount: 0.3m, other: 4.4m));
        db.Context.SalesInvoices.Add(sale);
        db.Context.PurchaseInvoices.Add(purchase);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var s = await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync();
        var p = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync();
        Assert.Equal((1.1m, 2.2m, 0.3m, 4.4m), (s.FreightValue, s.InsuranceValue, s.DiscountValue, s.OtherExpensesValue));
        Assert.Equal((1.1m, 2.2m, 0.3m, 4.4m), (p.FreightValue, p.InsuranceValue, p.DiscountValue, p.OtherExpensesValue));
    }

    [Theory]
    [InlineData("SalesInvoices", "TotalFreight,TotalInsurance,TotalOtherExpenses,TotalDiscount,GrandTotal")]
    [InlineData("SalesInvoicesItems", "FreightValue,InsuranceValue,DiscountValue,OtherExpensesValue,GrandTotal")]
    [InlineData("PurchaseInvoices", "TotalFreight,TotalInsurance,TotalOtherExpenses,TotalDiscount,GrandTotal")]
    [InlineData("PurchaseInvoicesItems", "FreightValue,InsuranceValue,DiscountValue,OtherExpensesValue,GrandTotal")]
    public void Edm_exposes_the_charges_and_the_totals(string entitySet, string properties)
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        var type = builder.GetEdmModel().EntityContainer.FindEntitySet(entitySet)!.EntityType;

        foreach (var name in properties.Split(','))
            Assert.Equal("Edm.Decimal", type.FindProperty(name)?.Type.FullName());
    }
}
```

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~InvoiceLineChargesModelTests"`. Esperado: erro de compilação (`FreightValue`/`GrandTotal` não existem).

- [ ] **Step 3: Implementar**

Em `SalesInvoiceItem.cs` **e** em `PurchaseInvoiceItem.cs`, logo depois da propriedade `Total` (o mesmo bloco nas duas):

```csharp
    // --- Frete, seguro, desconto e outras despesas da linha (spec 2026-10-05, D1). Padrão 0 em toda filial (R1). ---

    /// <summary>Frete cobrado na linha (<c>det/prod/vFrete</c>).</summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal FreightValue { get; set; }

    /// <summary>Seguro cobrado na linha (<c>det/prod/vSeg</c>).</summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal InsuranceValue { get; set; }

    /// <summary>
    /// Desconto incondicional da linha (<c>det/prod/vDesc</c>). Nunca passa de itens + frete + seguro + outras despesas
    /// (<c>InvoiceLineChargeRules</c>).
    /// </summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal DiscountValue { get; set; }

    /// <summary>Outras despesas acessórias da linha (<c>det/prod/vOutro</c>).</summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal OtherExpensesValue { get; set; }

    /// <summary>
    /// Total geral da linha (spec D2): <see cref="Total"/> (o vProd) + frete + seguro + outras despesas − desconto. É a
    /// base dos tributos (D3) e a parte da linha no vNF. Derivado, sem coluna.
    /// </summary>
    [NotMapped]
    public decimal GrandTotal => Total + FreightValue + InsuranceValue + OtherExpensesValue - DiscountValue;
```

Em `INfeTaxedLine.cs`, logo depois de `decimal Total { get; }`:

```csharp

    /// <summary>Frete, seguro, desconto e outras despesas da linha (spec 2026-10-05 D1); padrão 0.</summary>
    decimal FreightValue { get; set; }
    decimal InsuranceValue { get; set; }
    decimal DiscountValue { get; set; }
    decimal OtherExpensesValue { get; set; }

    /// <summary><see cref="Total"/> + frete + seguro + outras despesas − desconto: base dos tributos e parte da linha no vNF.</summary>
    decimal GrandTotal { get; }
```

Em `SalesInvoice.cs` **e** em `PurchaseInvoice.cs`, logo depois de `TotalInvoiceIbsCbs` (o mesmo bloco nas duas):

```csharp

    /// <summary>Soma do frete das linhas (spec 2026-10-05 §4.2). Derivado, como <see cref="TotalInvoiceItems"/>.</summary>
    [NotMapped]
    public decimal TotalFreight => Items.Sum(i => i.FreightValue);

    /// <summary>Soma do seguro das linhas.</summary>
    [NotMapped]
    public decimal TotalInsurance => Items.Sum(i => i.InsuranceValue);

    /// <summary>Soma das outras despesas das linhas.</summary>
    [NotMapped]
    public decimal TotalOtherExpenses => Items.Sum(i => i.OtherExpensesValue);

    /// <summary>Soma do desconto das linhas.</summary>
    [NotMapped]
    public decimal TotalDiscount => Items.Sum(i => i.DiscountValue);

    /// <summary>Total geral do documento (spec D2): Σ total geral das linhas — o vNF da NF-e.</summary>
    [NotMapped]
    public decimal GrandTotal => Items.Sum(i => i.GrandTotal);
```

Em `ODataConfigurations.cs`, logo depois da linha `.AddProperty(typeof(SalesInvoice).GetProperty(nameof(SalesInvoice.TotalInvoiceIbsCbs)));`:

```csharp
        // Frete, seguro, desconto e outras despesas (spec 2026-10-05 §4.2): calculadas, entram no EDM à mão como o
        // TotalInvoiceItems — sem estas linhas o $select devolve 400.
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(SalesInvoice))
            .AddProperty(typeof(SalesInvoice).GetProperty(nameof(SalesInvoice.TotalFreight)));
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(SalesInvoice))
            .AddProperty(typeof(SalesInvoice).GetProperty(nameof(SalesInvoice.TotalInsurance)));
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(SalesInvoice))
            .AddProperty(typeof(SalesInvoice).GetProperty(nameof(SalesInvoice.TotalOtherExpenses)));
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(SalesInvoice))
            .AddProperty(typeof(SalesInvoice).GetProperty(nameof(SalesInvoice.TotalDiscount)));
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(SalesInvoice))
            .AddProperty(typeof(SalesInvoice).GetProperty(nameof(SalesInvoice.GrandTotal)));
```

Logo depois da linha `.AddProperty(typeof(SalesInvoiceItem).GetProperty(nameof(SalesInvoiceItem.TotalIbsCbs)));`:

```csharp
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(SalesInvoiceItem))
            .AddProperty(typeof(SalesInvoiceItem).GetProperty(nameof(SalesInvoiceItem.GrandTotal)));
```

Logo depois da linha `.AddProperty(typeof(PurchaseInvoiceItem).GetProperty(nameof(PurchaseInvoiceItem.TotalIbsCbs)));` (a última do bloco calculado da entrada):

```csharp
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(PurchaseInvoiceItem))
            .AddProperty(typeof(PurchaseInvoiceItem).GetProperty(nameof(PurchaseInvoiceItem.GrandTotal)));
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(PurchaseInvoice))
            .AddProperty(typeof(PurchaseInvoice).GetProperty(nameof(PurchaseInvoice.TotalFreight)));
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(PurchaseInvoice))
            .AddProperty(typeof(PurchaseInvoice).GetProperty(nameof(PurchaseInvoice.TotalInsurance)));
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(PurchaseInvoice))
            .AddProperty(typeof(PurchaseInvoice).GetProperty(nameof(PurchaseInvoice.TotalOtherExpenses)));
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(PurchaseInvoice))
            .AddProperty(typeof(PurchaseInvoice).GetProperty(nameof(PurchaseInvoice.TotalDiscount)));
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(PurchaseInvoice))
            .AddProperty(typeof(PurchaseInvoice).GetProperty(nameof(PurchaseInvoice.GrandTotal)));
```

(As quatro colunas mapeadas entram no EDM sozinhas pela convenção.)

Gerar a migration (de `siagro-b1-backend/`):

```bash
dotnet build SiagroB1.sln
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add AddInvoiceLineCharges --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --no-build
```

Ler o arquivo gerado: tem de conter só **oito** `AddColumn<decimal>` — `FreightValue`, `InsuranceValue`, `DiscountValue`, `OtherExpensesValue` em `SALES_INVOICES_ITEMS` e em `PURCHASE_INVOICES_ITEMS` — com `nullable: false, defaultValue: 0m`, e os oito `DropColumn` no `Down`. Se o `type:` vier `"DECIMAL(18,2) DEFAULT 0"`, trocar por `"DECIMAL(18,2)"` em cada `AddColumn` (mantendo `defaultValue: 0m`) — é a prática das migrations anteriores (ex.: `20261005035545_AddPurchaseInvoiceNfe.cs`): com os dois, o SQL sairia com `DEFAULT` duplicado. O snapshot fica com o `TypeName` da entidade, como as demais colunas. Qualquer outra operação no arquivo = drift: parar e relatar. Conferir:

```bash
dotnet build SiagroB1.sln
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --no-build
```

Esperado: "No changes have been made to the model since the last migration."

- [ ] **Step 4: Rodar e ver passar** — o filtro da Step 2, depois `dotnet build SiagroB1.sln` e as duas suítes completas.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application.Tests/Invoices/InvoiceLineChargesModelTests.cs SiagroB1.Migrations/AppContext/*AddInvoiceLineCharges*.cs
git commit -m "feat(invoice): frete, seguro, desconto e outras despesas na linha dos documentos" -m "DB: AddInvoiceLineCharges" -- SiagroB1.Domain/Entities/SalesInvoiceItem.cs SiagroB1.Domain/Entities/PurchaseInvoiceItem.cs SiagroB1.Domain/Entities/SalesInvoice.cs SiagroB1.Domain/Entities/PurchaseInvoice.cs SiagroB1.Domain/Interfaces/INfeTaxedLine.cs SiagroB1.Web/ODataConfig/ODataConfigurations.cs SiagroB1.Application.Tests/Invoices/InvoiceLineChargesModelTests.cs SiagroB1.Migrations/AppContext/
```

(`SiagroB1.Migrations/AppContext/` inclui o `AppDbContextModelSnapshot.cs` alterado; conferir com `git status` que nada além disso entrou.)

---

### Task 2: Validação (R2), cópias e travas das linhas

**Files:**
- Create: `SiagroB1.Application/Services/InvoiceLineChargeRules.cs`
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCreateService.cs` (depois de `if (salesInvoice.Items.Count == 0) ...`)
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesItemsCreateService.cs` (início do `ExecuteAsync`)
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesItemsUpdateService.cs` (dentro do `try`, depois de `entity.ItemName = ...`)
- Modify: `SiagroB1.Application/Services/SalesInvoices/Factories/SalesInvoiceCopyFactory.cs:44-52`
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeLock.cs:31-35` (`ItemFiscalFields`)
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeReturnLock.cs:21-26,40` (só os comentários)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCreateService.cs` (depois de `if (invoice.Items.Count == 0) ...`)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesUpdateService.cs` (`SyncItemsAsync`, ~linhas 171-240)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesItemsCreateService.cs` (início do `ExecuteAsync`)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesItemsUpdateService.cs` (~linhas 31, 56-65)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceNfeLock.cs:20-24,46-53` (`ItemFiscalFields`; comentário de `ReturnLineFields`)
- Test: `SiagroB1.Application.Tests/Invoices/InvoiceLineChargeRulesTests.cs`
- Test (acrescentar): `SiagroB1.Application.Tests/SalesInvoices/SalesInvoiceNfeLockTests.cs`, `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesNfeReturnLockTests.cs`, `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceNfeLockTests.cs`

**Interfaces:**
- Consumes: os quatro campos e `GrandTotal` de `INfeTaxedLine` (Task 1).
- Produces: `public static class InvoiceLineChargeRules` (namespace `SiagroB1.Application.Services`) com `public static void Ensure(INfeTaxedLine line)` — arredonda os quatro valores em centavos (`AwayFromZero`) e lança `DefaultException` com as mensagens da spec. Chamado por: criação do documento de saída e de entrada, inclusão e alteração de linha nas duas, `SyncItems` da alteração do documento de entrada. As travas de NF-e autorizada/em processamento passam a proteger os quatro campos; as travas da devolução (`SalesInvoiceNfeReturnLock.LineFields`, `PurchaseInvoiceNfeLock.ReturnLineFields`) **não** os restauram (editáveis enquanto Pendente, spec §9).

- [ ] **Step 1: Escrever os testes que falham**

`SiagroB1.Application.Tests/Invoices/InvoiceLineChargeRulesTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesInvoices.Factories;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Invoices;

/// <summary>
/// Frete, seguro, desconto e outras despesas na gravação da linha (spec 2026-10-05 §5, R2), nos seis caminhos que gravam
/// linha das duas entidades, e as cópias campo a campo (§4.3). Regra inativa de propósito: a validação vale em toda filial.
/// </summary>
public class InvoiceLineChargeRulesTests
{
    private const string Negative = "frete, seguro, desconto e outras despesas não podem ser negativos.";

    /// <summary>10 × 2,00 = 20,00 de produtos.</summary>
    private static SalesInvoiceItem SalesLine(
        decimal freight = 0m, decimal insurance = 0m, decimal discount = 0m, decimal other = 0m) => new()
    {
        Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 10m, UnitPrice = 2m,
        FreightValue = freight, InsuranceValue = insurance, DiscountValue = discount, OtherExpensesValue = other,
    };

    /// <summary>10 × 2,00 = 20,00 de produtos.</summary>
    private static PurchaseInvoiceItem PurchaseLine(
        decimal freight = 0m, decimal insurance = 0m, decimal discount = 0m, decimal other = 0m) => new()
    {
        Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 10m, UnitPrice = 2m,
        FreightValue = freight, InsuranceValue = insurance, DiscountValue = discount, OtherExpensesValue = other,
    };

    private static SalesInvoicesCreateService SalesCreate(UnitOfWork db)
    {
        var partners = new FakeBusinessPartnerService();
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);

        return new SalesInvoicesCreateService(
            db, partners, new FakeItemService(), new FakeDocNumberSequenceService(),
            new SalesInvoicesUsageGuardService(usages), new SalesInvoicesCfopResolveService(db, usages, partners),
            TaxTestServices.InactiveApply(db), NullLogger<SalesInvoicesCreateService>.Instance);
    }

    private static SalesInvoicesItemsUpdateService SalesItemUpdate(UnitOfWork db) =>
        new(db, new FakeItemService(),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            TaxTestServices.InactiveApply(db), NullLogger<SalesInvoicesUpdateService>.Instance);

    private static async Task<(UnitOfWork Db, SalesInvoiceItem Line)> SeedSalesAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1", InvoiceStatus = InvoiceStatus.Pending };
        invoice.AddItem(SalesLine());
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return (db, invoice.Items.Single());
    }

    private static async Task<(UnitOfWork Db, PurchaseInvoice Invoice)> SeedPurchaseAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F1" };
        invoice.AddItem(PurchaseLine());
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        return (db, invoice);
    }

    // --- a regra ---

    [Theory]
    [InlineData(-0.01, 0, 0, 0)]
    [InlineData(0, -0.01, 0, 0)]
    [InlineData(0, 0, -0.01, 0)]
    [InlineData(0, 0, 0, -0.01)]
    public void Negative_value_is_refused(double freight, double insurance, double discount, double other)
    {
        var line = SalesLine((decimal)freight, (decimal)insurance, (decimal)discount, (decimal)other);

        var e = Assert.Throws<DefaultException>(() => InvoiceLineChargeRules.Ensure(line));

        Assert.Equal($"Item SOJA: {Negative}", e.Message);
    }

    [Fact]
    public void Discount_above_the_line_is_refused()
    {
        // Linha: 20,00 de produtos + frete 5,00 + seguro 1,00 + outras despesas 0,50 = 26,50.
        var e = Assert.Throws<DefaultException>(() => InvoiceLineChargeRules.Ensure(SalesLine(5m, 1m, 26.51m, 0.5m)));

        Assert.Equal("Item SOJA: o desconto passa do valor da linha.", e.Message);
    }

    [Fact]
    public void Discount_of_the_whole_line_is_accepted_and_the_line_totals_zero()
    {
        // Review Focus 1: bonificação — o desconto leva a linha inteira, e só o que passar disso é recusado.
        var line = SalesLine(5m, 1m, 26.50m, 0.5m);

        InvoiceLineChargeRules.Ensure(line);

        Assert.Equal(0m, line.GrandTotal);
    }

    [Fact]
    public void Values_are_rounded_to_cents_before_the_check()
    {
        var line = PurchaseLine(freight: 10.005m, insurance: 0.004m);

        InvoiceLineChargeRules.Ensure(line);

        Assert.Equal((10.01m, 0m), (line.FreightValue, line.InsuranceValue));
    }

    // --- documento de saída ---

    [Fact]
    public async Task Sales_document_create_refuses_a_negative_charge()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = "C1", InvoiceType = SalesInvoiceType.Normal,
            Items = [SalesLine(insurance: -1m)],
        };

        var e = await Assert.ThrowsAsync<DefaultException>(() => SalesCreate(db).ExecuteAsync(invoice, "tester"));

        Assert.Equal($"Item SOJA: {Negative}", e.Message);
    }

    [Fact]
    public async Task Sales_line_create_refuses_a_discount_above_the_line()
    {
        var db = TestDb.CreateUnitOfWork();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new SalesInvoicesItemsCreateService(db, new FakeItemService(), TaxTestServices.InactiveApply(db),
                NullLogger<SalesInvoicesItemsCreateService>.Instance).ExecuteAsync(SalesLine(discount: 20.01m), "tester"));

        Assert.Equal("Item SOJA: o desconto passa do valor da linha.", e.Message);
    }

    [Fact]
    public async Task Sales_line_update_refuses_a_discount_above_the_line()
    {
        var (db, line) = await SeedSalesAsync();
        line.DiscountValue = 20.01m;

        var e = await Assert.ThrowsAsync<DefaultException>(() => SalesItemUpdate(db).ExecuteAsync(line.Key!.Value, line, "tester"));

        Assert.Equal("Item SOJA: o desconto passa do valor da linha.", e.Message);
    }

    [Fact]
    public async Task Sales_line_update_saves_the_charges_in_cents()
    {
        var (db, line) = await SeedSalesAsync();
        (line.FreightValue, line.InsuranceValue, line.DiscountValue, line.OtherExpensesValue) = (12.345m, 1m, 2m, 3m);

        await SalesItemUpdate(db).ExecuteAsync(line.Key!.Value, line, "tester");

        var saved = await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync();
        Assert.Equal((12.35m, 1m, 2m, 3m), (saved.FreightValue, saved.InsuranceValue, saved.DiscountValue, saved.OtherExpensesValue));
    }

    [Fact]
    public void Document_copy_keeps_the_line_charges()
    {
        var original = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1" };
        original.AddItem(SalesLine(5m, 1m, 2m, 0.5m));

        var item = SalesInvoiceCopyFactory.CreateFrom(original, "tester").Items.Single();

        Assert.Equal((5m, 1m, 2m, 0.5m), (item.FreightValue, item.InsuranceValue, item.DiscountValue, item.OtherExpensesValue));
    }

    // --- documento de entrada ---

    [Fact]
    public async Task Purchase_document_create_refuses_a_discount_above_the_line()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F1" };
        invoice.AddItem(PurchaseLine(discount: 20.01m));

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesCreateService(db, new FakeBusinessPartnerService(), new FakeItemService(),
                TaxTestServices.InactivePurchaseApply(db)).ExecuteAsync(invoice, "tester"));

        Assert.Equal("Item TRIGO: o desconto passa do valor da linha.", e.Message);
    }

    [Fact]
    public async Task Purchase_document_update_refuses_a_negative_charge()
    {
        var (db, saved) = await SeedPurchaseAsync();
        var incoming = new PurchaseInvoice { CardCode = "F1" };
        var line = PurchaseLine(other: -1m);
        line.Key = saved.Items.Single().Key;
        incoming.AddItem(line);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesUpdateService(db, new FakeBusinessPartnerService(), new FakeItemService(),
                TaxTestServices.InactivePurchaseApply(db)).ExecuteAsync(saved.Key, incoming, "tester"));

        Assert.Equal($"Item TRIGO: {Negative}", e.Message);
    }

    [Fact]
    public async Task Purchase_document_update_copies_the_charges_of_changed_and_new_lines()
    {
        var (db, saved) = await SeedPurchaseAsync();
        var incoming = new PurchaseInvoice { CardCode = "F1" };
        var changed = PurchaseLine(freight: 10m, insurance: 1m, discount: 2m, other: 3m);
        changed.Key = saved.Items.Single().Key;
        incoming.AddItem(changed);
        var added = PurchaseLine(freight: 4m, insurance: 5m, discount: 6m, other: 7m);
        added.Key = null;
        incoming.AddItem(added);

        await new PurchaseInvoicesUpdateService(db, new FakeBusinessPartnerService(), new FakeItemService(),
            TaxTestServices.InactivePurchaseApply(db)).ExecuteAsync(saved.Key, incoming, "tester");

        var lines = await db.Context.PurchaseInvoicesItems.AsNoTracking().Where(i => i.PurchaseInvoiceKey == saved.Key).ToListAsync();
        var first = lines.Single(i => i.Key == changed.Key);
        var second = lines.Single(i => i.Key != changed.Key);
        Assert.Equal((10m, 1m, 2m, 3m), (first.FreightValue, first.InsuranceValue, first.DiscountValue, first.OtherExpensesValue));
        Assert.Equal((4m, 5m, 6m, 7m), (second.FreightValue, second.InsuranceValue, second.DiscountValue, second.OtherExpensesValue));
    }

    [Fact]
    public async Task Purchase_line_create_refuses_a_negative_charge()
    {
        var db = TestDb.CreateUnitOfWork();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsCreateService(db, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db))
                .ExecuteAsync(PurchaseLine(freight: -1m), "tester"));

        Assert.Equal($"Item TRIGO: {Negative}", e.Message);
    }

    [Fact]
    public async Task Purchase_line_update_refuses_a_discount_above_the_line_and_saves_valid_charges()
    {
        var (db, saved) = await SeedPurchaseAsync();
        var key = saved.Items.Single().Key!.Value;
        var service = new PurchaseInvoicesItemsUpdateService(db, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db));

        var refused = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == key);
        refused.DiscountValue = 20.01m;
        var e = await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(key, refused, "tester"));
        Assert.Equal("Item TRIGO: o desconto passa do valor da linha.", e.Message);

        db.Context.ChangeTracker.Clear();
        var valid = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == key);
        (valid.FreightValue, valid.InsuranceValue, valid.DiscountValue, valid.OtherExpensesValue) = (3m, 2m, 1m, 0.5m);
        await service.ExecuteAsync(key, valid, "tester");

        var reloaded = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == key);
        Assert.Equal((3m, 2m, 1m, 0.5m), (reloaded.FreightValue, reloaded.InsuranceValue, reloaded.DiscountValue, reloaded.OtherExpensesValue));
    }
}
```

Em `SalesInvoiceNfeLockTests.cs`, acrescentar no fim da classe (usa `SeedAsync`/`ItemUpdate` que o arquivo já tem):

```csharp
    [Fact]
    public async Task Authorized_line_cannot_change_the_freight()
    {
        var (db, _) = await SeedAsync(NfeStatus.Authorized, InvoiceStatus.Confirmed);
        var item = await db.Context.SalesInvoicesItems.SingleAsync();
        item.FreightValue = 10m;

        var e = await Assert.ThrowsAsync<DefaultException>(() => ItemUpdate(db).ExecuteAsync(item.Key!.Value, item, "tester"));

        Assert.Equal("A NF-e deste documento já foi autorizada: os dados que foram para a nota não podem mudar.", e.Message);
    }
```

Em `SalesInvoicesNfeReturnLockTests.cs`, acrescentar no fim da classe (usa `ItemUpdate` do arquivo):

```csharp
    [Fact]
    public async Task Own_return_line_keeps_the_edited_charges()
    {
        // Review Focus 5: na devolução Pendente os quatro valores são editáveis (spec D4); a trava volta só preço/produto/natureza.
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);
        var line = await s.Sale.Db.Context.SalesInvoicesItems.SingleAsync(i => i.SalesInvoiceKey == created.Key);
        (line.FreightValue, line.DiscountValue, line.UnitPrice) = (150m, 20m, 9m);

        await ItemUpdate(s.Sale.Db).ExecuteAsync(line.Key!.Value, line, "tester");

        var saved = await s.Sale.Db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == line.Key);
        Assert.Equal((150m, 20m, 2m), (saved.FreightValue, saved.DiscountValue, saved.UnitPrice));
    }
```

Em `PurchaseInvoiceNfeLockTests.cs`, acrescentar no fim da classe (usa `SeedAsync` do arquivo):

```csharp
    [Fact]
    public async Task Authorized_line_cannot_change_the_discount()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized);
        var changed = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == invoice.Items.Single().Key);
        changed.DiscountValue = 10m;

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsUpdateService(db, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db))
                .ExecuteAsync(changed.Key!.Value, changed, "tester"));

        Assert.Equal("A NF-e deste documento já foi autorizada: os dados que foram para a nota não podem mudar.", e.Message);
    }

    [Fact]
    public async Task Purchase_return_line_keeps_the_edited_charges()
    {
        // Review Focus 5: na devolução Pendente os quatro valores são editáveis (spec D4); a trava volta preço e produto.
        var (db, invoice) = await SeedAsync(NfeStatus.None, nfeReturn: true);
        var line = invoice.Items.Single();
        var changed = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == line.Key);
        (changed.FreightValue, changed.DiscountValue, changed.UnitPrice) = (15m, 2m, 9m);

        await new PurchaseInvoicesItemsUpdateService(db, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db))
            .ExecuteAsync(line.Key!.Value, changed, "tester");

        var saved = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == line.Key);
        Assert.Equal((15m, 2m, 1.5m), (saved.FreightValue, saved.DiscountValue, saved.UnitPrice));
    }
```

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~InvoiceLineChargeRulesTests|FullyQualifiedName~SalesInvoiceNfeLockTests|FullyQualifiedName~SalesInvoicesNfeReturnLockTests|FullyQualifiedName~PurchaseInvoiceNfeLockTests"`. Esperado: erro de compilação (`InvoiceLineChargeRules` não existe); depois dela, falham as recusas, as cópias e as travas novas. (Os dois testes do Review Focus 5 já passam antes da implementação — eles fixam que a trava da devolução NÃO deve ganhar os quatro campos.)

- [ ] **Step 3: Implementar**

`SiagroB1.Application/Services/InvoiceLineChargeRules.cs` (`git add` logo depois de criar):

```csharp
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services;

/// <summary>
/// Frete, seguro, desconto e outras despesas da linha do documento de saída e do de entrada (spec 2026-10-05 §5, R2).
/// Valem em TODAS as filiais (R1): a regra é de gravação, não de tributação. Mora num lugar só porque são seis os
/// caminhos que gravam linha — inclusão e alteração do documento e da linha, nas duas entidades — e a grade da tela
/// alcança cada um por uma ação diferente.
/// </summary>
public static class InvoiceLineChargeRules
{
    /// <summary>
    /// Arredonda os quatro valores em centavos — a coluna é DECIMAL(18,2), e o cálculo dos tributos não pode ver uma casa
    /// que o banco descartaria — e recusa valor negativo ou desconto acima de itens + frete + seguro + outras despesas.
    /// Desconto IGUAL ao valor da linha passa (bonificação): o total geral fica 0.
    /// </summary>
    public static void Ensure(INfeTaxedLine line)
    {
        line.FreightValue = Cents(line.FreightValue);
        line.InsuranceValue = Cents(line.InsuranceValue);
        line.DiscountValue = Cents(line.DiscountValue);
        line.OtherExpensesValue = Cents(line.OtherExpensesValue);

        if (line.FreightValue < 0 || line.InsuranceValue < 0 || line.DiscountValue < 0 || line.OtherExpensesValue < 0)
            throw new DefaultException(
                $"Item {line.ItemCode}: frete, seguro, desconto e outras despesas não podem ser negativos.");

        if (line.DiscountValue > line.Total + line.FreightValue + line.InsuranceValue + line.OtherExpensesValue)
            throw new DefaultException($"Item {line.ItemCode}: o desconto passa do valor da linha.");
    }

    private static decimal Cents(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
```

`SalesInvoicesCreateService.cs` — logo depois de `throw new ApplicationException("Items can not be empty.");` (fora do `try`, para chegar à tela como 400):

```csharp

        // Frete, seguro, desconto e outras despesas da linha (spec 2026-10-05 §5): recusa antes de qualquer gravação,
        // em toda filial.
        foreach (var item in salesInvoice.Items)
            InvoiceLineChargeRules.Ensure(item);
```

`SalesInvoicesItemsCreateService.cs` — primeira instrução do `ExecuteAsync`:

```csharp
        // Frete, seguro, desconto e outras despesas da linha (spec 2026-10-05 §5). Fora do try: 400 com a mensagem.
        InvoiceLineChargeRules.Ensure(salesInvoiceItem);

```

`SalesInvoicesItemsUpdateService.cs` — dentro do `try`, logo depois de `entity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;` (o `SetValues` adiante copia os quatro campos já arredondados; o `catch` só pega concorrência):

```csharp

            // Frete, seguro, desconto e outras despesas da linha (spec 2026-10-05 §5).
            InvoiceLineChargeRules.Ensure(entity);
```

`SalesInvoiceCopyFactory.cs` — no `new SalesInvoiceItem { ... }`, depois de `SalesContractKey =  item.SalesContractKey,`:

```csharp
                FreightValue = item.FreightValue,
                InsuranceValue = item.InsuranceValue,
                DiscountValue = item.DiscountValue,
                OtherExpensesValue = item.OtherExpensesValue,
```

`SalesInvoiceNfeLock.cs` — `ItemFiscalFields` passa a ser:

```csharp
    /// <summary>Campos da linha que vão para o XML (a Conferência de entregas mexe em outros).</summary>
    private static readonly string[] ItemFiscalFields =
    [
        nameof(SalesInvoiceItem.ItemCode), nameof(SalesInvoiceItem.UnitOfMeasureCode), nameof(SalesInvoiceItem.Quantity),
        nameof(SalesInvoiceItem.UnitPrice), nameof(SalesInvoiceItem.UsageCode), nameof(SalesInvoiceItem.FreightValue),
        nameof(SalesInvoiceItem.InsuranceValue), nameof(SalesInvoiceItem.DiscountValue), nameof(SalesInvoiceItem.OtherExpensesValue),
    ];
```

`SalesInvoiceNfeReturnLock.cs` — só documentação (a lista `LineFields` NÃO muda). Acima de `private static readonly string[] LineFields`:

```csharp
    /// <summary>
    /// Restaurados na linha da devolução. Quantidade e os quatro valores da linha (frete, seguro, desconto, outras
    /// despesas) ficam de fora de propósito: nascem da venda na proporção do que volta e são editáveis enquanto a
    /// devolução está Pendente (spec 2026-10-05 D4).
    /// </summary>
```

e o summary de `RestoreLine` vira `/// <summary>Chamado DEPOIS do SetValues da linha de uma devolução própria: só a quantidade e os quatro valores da linha mudam.</summary>`.

`PurchaseInvoicesCreateService.cs` — logo depois de `throw new DefaultException("Informe ao menos um item no documento de entrada.");`:

```csharp

        // Frete, seguro, desconto e outras despesas da linha (spec 2026-10-05 §5): em toda filial.
        foreach (var item in invoice.Items)
            InvoiceLineChargeRules.Ensure(item);
```

`PurchaseInvoicesUpdateService.cs`, em `SyncItemsAsync`:
- logo depois de `var incoming = entity.Items.ToList();`:

```csharp

        // Frete, seguro, desconto e outras despesas da linha (spec 2026-10-05 §5): antes de qualquer cópia.
        foreach (var line in incoming)
            InvoiceLineChargeRules.Ensure(line);
```

- no `new PurchaseInvoiceItem { ... }` da linha nova, depois de `UsageCode = line.UsageCode,`:

```csharp
                    FreightValue = line.FreightValue,
                    InsuranceValue = line.InsuranceValue,
                    DiscountValue = line.DiscountValue,
                    OtherExpensesValue = line.OtherExpensesValue,
```

- na linha existente, depois de `current.UsageCode = line.UsageCode;`:

```csharp
            current.FreightValue = line.FreightValue;
            current.InsuranceValue = line.InsuranceValue;
            current.DiscountValue = line.DiscountValue;
            current.OtherExpensesValue = line.OtherExpensesValue;
```

- o comentário `// Na devolução de compra só a quantidade muda (a descrição re-resolvida também volta).` vira `// Na devolução de compra só a quantidade e os quatro valores da linha mudam (a descrição re-resolvida também volta).`

`PurchaseInvoicesItemsCreateService.cs` — primeira instrução do `ExecuteAsync`:

```csharp
        // Frete, seguro, desconto e outras despesas da linha (spec 2026-10-05 §5).
        InvoiceLineChargeRules.Ensure(item);

```

`PurchaseInvoicesItemsUpdateService.cs`:
- logo depois de `await PurchaseInvoiceLineGuard.EnsureParentIsPendingAsync(db, existing.PurchaseInvoiceKey);`:

```csharp

        // Frete, seguro, desconto e outras despesas da linha (spec 2026-10-05 §5): antes de qualquer cópia.
        InvoiceLineChargeRules.Ensure(entity);
```

- depois de `existing.UsageCode = entity.UsageCode;`:

```csharp
        existing.FreightValue = entity.FreightValue;
        existing.InsuranceValue = entity.InsuranceValue;
        existing.DiscountValue = entity.DiscountValue;
        existing.OtherExpensesValue = entity.OtherExpensesValue;
```

- o comentário `// Travas da NF-e: devolução de compra só muda a quantidade; emitido/em processamento trava a linha.` vira `// Travas da NF-e: devolução de compra só muda a quantidade e os quatro valores; emitido/em processamento trava a linha.`

`PurchaseInvoiceNfeLock.cs` — `ItemFiscalFields` passa a ser:

```csharp
    private static readonly string[] ItemFiscalFields =
    [
        nameof(PurchaseInvoiceItem.ItemCode), nameof(PurchaseInvoiceItem.UnitOfMeasureCode), nameof(PurchaseInvoiceItem.Quantity),
        nameof(PurchaseInvoiceItem.UnitPrice), nameof(PurchaseInvoiceItem.UsageCode), nameof(PurchaseInvoiceItem.FreightValue),
        nameof(PurchaseInvoiceItem.InsuranceValue), nameof(PurchaseInvoiceItem.DiscountValue), nameof(PurchaseInvoiceItem.OtherExpensesValue),
    ];
```

e o summary de `ReturnLineFields` vira `/// <summary>Na linha da devolução de compra só a quantidade e os quatro valores da linha (spec 2026-10-05 D4) são editáveis.</summary>` (a lista NÃO muda).

- [ ] **Step 4: Rodar e ver passar** — o filtro da Step 2, depois `dotnet build SiagroB1.sln` e as duas suítes completas.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application/Services/InvoiceLineChargeRules.cs SiagroB1.Application.Tests/Invoices/InvoiceLineChargeRulesTests.cs
git commit -m "feat(invoice): validar e copiar frete, seguro, desconto e outras despesas da linha" -- SiagroB1.Application/Services/InvoiceLineChargeRules.cs SiagroB1.Application/Services/SalesInvoices/ SiagroB1.Application/Services/PurchaseInvoices/ SiagroB1.Application.Tests/Invoices/InvoiceLineChargeRulesTests.cs SiagroB1.Application.Tests/SalesInvoices/SalesInvoiceNfeLockTests.cs SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesNfeReturnLockTests.cs SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceNfeLockTests.cs
```

---

### Task 3: Total geral como base dos tributos (D3)

**Files:**
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesTaxApplyService.cs:65-69`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesTaxApplyService.cs:77-81`
- Test (acrescentar): `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesTaxApplyServiceTests.cs`, `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesTaxApplyTests.cs`, `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesNfeReturnLockTests.cs`

**Interfaces:**
- Consumes: `GrandTotal` da linha (Task 1); `InvoiceLineChargeRules.Ensure` já chamado antes do cálculo (Task 2).
- Produces: `TaxLineRequest.Amount = item.GrandTotal` nos dois serviços. O motor (`TaxCalculator`) não muda: redução de base do ICMS, exclusão do ICMS da base do PIS/COFINS e deduções da base do IBS/CBS incidem sobre esse valor.

- [ ] **Step 1: Escrever os testes que falham**

Em `SalesInvoicesTaxApplyServiceTests.cs`, acrescentar no fim da classe (usa `Seed`, `Invoice`, `Apply`, `Partners`, `CardBa` do arquivo). Conta de referência: 30.000 × 2,00 = 60.000,00 + frete 1.000,00 + seguro 100,00 + outras 400,00 − desconto 500,00 = **61.000,00**; ICMS 7% (SP→BA) = 4.270,00; PIS/COFINS sem ICMS na base = 56.730,00 → PIS 1,65% = 936,05 e COFINS 7,6% = 4.311,48; IBS/CBS = 61.000 − 4.270 − 936,05 − 4.311,48 = 51.482,47 → CBS 0,9% = 463,34 e IBS UF 0,1% = 51,48.

```csharp
    // --- Frete, seguro, desconto e outras despesas na base (spec 2026-10-05 D3) ---

    private static SalesInvoice InvoiceWithCharges(int usageCode)
    {
        var invoice = Invoice(CardBa, usageCode);
        var item = invoice.Items.Single();
        (item.FreightValue, item.InsuranceValue, item.DiscountValue, item.OtherExpensesValue) = (1000m, 100m, 500m, 400m);
        return invoice;
    }

    [Fact]
    public async Task Charges_enter_the_base_of_every_tax()
    {
        var (db, code) = await Seed();

        var item = await Apply(db, InvoiceWithCharges(code));

        Assert.Equal(61000m, item.GrandTotal);
        Assert.Equal((61000m, 4270.00m), (item.IcmsBase, item.IcmsValue));
        Assert.Equal((56730.00m, 936.05m), (item.PisBase, item.PisValue));
        Assert.Equal((56730.00m, 4311.48m), (item.CofinsBase, item.CofinsValue));
        Assert.Equal((51482.47m, 463.34m, 51.48m), (item.IbsCbsBase, item.CbsValue, item.IbsStateValue));
    }

    [Fact]
    public async Task Icms_base_reduction_applies_to_the_grand_total_of_the_line()
    {
        // Review Focus 4: CST 20 com 40% de redução — 61.000,00 × 60% = 36.600,00; 7% = 2.562,00.
        var (db, code) = await Seed(tweak: u =>
        {
            u.IcmsOutStateCst = "20";
            u.IcmsOutStateBaseReduction = 40m;
        });

        var item = await Apply(db, InvoiceWithCharges(code));

        Assert.Equal((40m, 36600.00m, 2562.00m), (item.IcmsBaseReduction, item.IcmsBase, item.IcmsValue));
    }

    [Fact]
    public async Task Branch_without_the_rule_only_keeps_the_charges()
    {
        var (db, code) = await Seed();
        var invoice = InvoiceWithCharges(code);

        await TaxTestServices.Apply(db, Partners(), "SAPB1").ApplyAsync(invoice, invoice.Items);

        var item = invoice.Items.Single();
        Assert.Equal((1000m, 0m, (string?)null), (item.FreightValue, item.IcmsBase, item.CstIcms));
    }
```

Em `PurchaseInvoicesTaxApplyTests.cs`, acrescentar no fim da classe (usa `SeedAsync`, `OwnEntry`, `Create`). Conta: 1.000 × 1,50 = 1.500,00 + 100 + 20 + 30 − 50 = 1.600,00; ICMS 51 18% diferido 100% → vICMSOp = vICMSDif = 288,00.

```csharp
    // --- Frete, seguro, desconto e outras despesas na base (spec 2026-10-05 D3) ---

    private static PurchaseInvoice OwnEntryWithCharges(int usageCode)
    {
        var invoice = OwnEntry(usageCode);
        var item = invoice.Items.Single();
        (item.FreightValue, item.InsuranceValue, item.DiscountValue, item.OtherExpensesValue) = (100m, 20m, 50m, 30m);
        return invoice;
    }

    [Fact]
    public async Task Line_charges_enter_the_icms_base_of_the_entry()
    {
        var seed = await SeedAsync();

        await Create(seed).ExecuteAsync(OwnEntryWithCharges(seed.PurchaseUsage), "tester");

        var line = await seed.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync();
        Assert.Equal((1600m, 288.00m, 288.00m, 0m), (line.IcmsBase, line.IcmsOperationValue, line.IcmsDeferredValue, line.IcmsValue));
    }

    [Fact]
    public async Task Branch_without_the_rule_only_keeps_the_charges()
    {
        var seed = await SeedAsync();

        await Create(seed, "SAPB1").ExecuteAsync(OwnEntryWithCharges(seed.PurchaseUsage), "tester");

        var line = await seed.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync();
        Assert.Equal((100m, (string?)null, 0m), (line.FreightValue, line.Cfop, line.IcmsBase));
    }
```

Em `SalesInvoicesNfeReturnLockTests.cs`, acrescentar no fim da classe:

```csharp
    [Fact]
    public async Task Own_return_line_tax_base_follows_the_edited_charges()
    {
        // Review Focus 5 + D3: o frete editado na devolução Pendente entra na base (60.000 + 150 − 20).
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);
        var line = await s.Sale.Db.Context.SalesInvoicesItems.SingleAsync(i => i.SalesInvoiceKey == created.Key);
        (line.FreightValue, line.DiscountValue) = (150m, 20m);

        await ItemUpdate(s.Sale.Db).ExecuteAsync(line.Key!.Value, line, "tester");

        Assert.Equal(60130m, (await s.Sale.Db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == line.Key)).IcmsBase);
    }
```

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoicesTaxApplyServiceTests|FullyQualifiedName~PurchaseInvoicesTaxApplyTests|FullyQualifiedName~SalesInvoicesNfeReturnLockTests"`. Esperado: as bases saem 60.000,00 / 1.500,00 / 60.000,00 (falham as asserções de base); os dois "Branch_without_the_rule" já passam.

- [ ] **Step 3: Implementar**

`SalesInvoicesTaxApplyService.cs` — no laço do `ApplyAsync`, trocar o bloco do `CalculateAsync` por:

```csharp
            // Venda e devolução de venda: a mercadoria sai da filial para o cliente (na devolução, a operação
            // é a da venda, que ela anula). Base = total geral da linha: itens + frete + seguro + outras despesas
            // − desconto (spec 2026-10-05 D3; LC 87/96 art. 13 e LC 214).
            var line = await _lines.CalculateAsync(new TaxLineRequest(
                usage, item.ItemCode, item.GrandTotal, inState, branch.StateCode!, customerState,
                branch.TaxRegime!.Value, rateDate, IncomingCfop: ownReturn));
```

`PurchaseInvoicesTaxApplyService.cs` — idem:

```csharp
            // A mercadoria vem do FORNECEDOR para a filial; a devolução repete a operação da compra, para
            // reproduzir a alíquota creditada (12% de BA→SP, e não 7% de SP→BA). Base = total geral da linha
            // (spec 2026-10-05 D3).
            var line = await _lines.CalculateAsync(new TaxLineRequest(
                usage, item.ItemCode, item.GrandTotal, inState, supplierState, branch.StateCode!,
                branch.TaxRegime!.Value, rateDate, IncomingCfop: !ownReturn));
```

- [ ] **Step 4: Rodar e ver passar** — o filtro da Step 2, depois as duas suítes completas.

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(invoice): total geral da linha como base dos tributos" -- SiagroB1.Application/Services/SalesInvoices/SalesInvoicesTaxApplyService.cs SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesTaxApplyService.cs SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesTaxApplyServiceTests.cs SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesTaxApplyTests.cs SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesNfeReturnLockTests.cs
```

---

### Task 4: NF-e — `det/prod`, `ICMSTot`, `vNF`, pagamento e fatura

**Files:**
- Modify: `SiagroB1.Fiscal/Nfe/NfeIssueInput.cs` (`NfeItem`, depois de `Total`, ~linha 102)
- Modify: `SiagroB1.Fiscal/Nfe/NfeXmlBuilder.cs:58,262,417-445,530`
- Modify: `SiagroB1.Application/Services/Nfe/NfeIssueInputAssembler.cs:103,224`
- Test (acrescentar): `SiagroB1.Fiscal.Tests/Nfe/NfeXmlBuilderTests.cs`, `SiagroB1.Fiscal.Tests/Nfe/NfeSignerTests.cs`, `SiagroB1.Application.Tests/Nfe/NfeIssueInputAssemblerTests.cs`, `SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeIssueServiceTests.cs`

**Interfaces:**
- Consumes: os quatro campos e `GrandTotal` de `INfeTaxedLine` (Task 1).
- Produces: em `NfeItem`: `decimal FreightValue`, `InsuranceValue`, `DiscountValue`, `OtherExpensesValue` (`init`, padrão 0) e `decimal GrandTotal => Total + FreightValue + InsuranceValue + OtherExpensesValue - DiscountValue`. Builder: `det/prod/vFrete|vSeg|vDesc|vOutro` omitidos quando 0; `ICMSTot` com as somas e `vNF` = vProd + vFrete + vSeg + vOutro − vDesc; guarda do pagamento, `cobr/fat` (`vOrig`, `vLiq`) e `detPag/vPag` pelo total geral. Montador: `ToItem` leva os quatro; o total passado ao `PaymentInstallmentCalculator` é Σ `GrandTotal`.

- [ ] **Step 1: Escrever os testes que falham**

Em `NfeXmlBuilderTests.cs`, acrescentar no fim da classe:

```csharp
    // --- Frete, seguro, desconto e outras despesas da linha (spec 2026-10-05 §7) ---

    /// <summary>60.000,00 de produtos + frete 1.000,00 + seguro 100,00 + outras 400,00 − desconto 500,00 = 61.000,00.</summary>
    private static NfeIssueInput InputWithCharges(decimal paid = 61000m) => NfeTestData.Input() with
    {
        Items = [NfeTestData.Item() with { FreightValue = 1000m, InsuranceValue = 100m, DiscountValue = 500m, OtherExpensesValue = 400m }],
        Payment = PaymentInstallmentCalculator.Calculate("30,60", PaymentStartRule.IssueDate, "15", paid, new DateOnly(2026, 10, 2)),
    };

    [Fact]
    public void Line_charges_go_to_det_prod()
    {
        var prod = Build(InputWithCharges()).infNFe.det.Single().prod;

        Assert.Equal((60000m, (decimal?)1000m, (decimal?)100m, (decimal?)500m, (decimal?)400m),
            (prod.vProd, prod.vFrete, prod.vSeg, prod.vDesc, prod.vOutro));
    }

    [Fact]
    public void Totals_carry_the_charges_and_vNF_is_the_grand_total()
    {
        var total = Build(InputWithCharges()).infNFe.total.ICMSTot;

        Assert.Equal((60000m, 1000m, 100m, 500m, 400m, 61000m),
            (total.vProd, total.vFrete, total.vSeg, total.vDesc, total.vOutro, total.vNF));
    }

    [Fact]
    public void Billing_and_payment_use_the_grand_total()
    {
        var nfe = Build(InputWithCharges());

        Assert.Equal(((decimal?)61000m, (decimal?)61000m), (nfe.infNFe.cobr.fat.vOrig, nfe.infNFe.cobr.fat.vLiq));
        Assert.Equal(61000m, nfe.infNFe.cobr.dup.Sum(d => d.vDup));
        Assert.Equal(61000m, nfe.infNFe.pag.Single().detPag.Single().vPag);
    }

    [Fact]
    public void Payment_of_the_products_only_is_refused_when_the_line_has_charges()
    {
        var e = Assert.Throws<SiagroB1.Domain.Exceptions.DefaultException>(() => Build(InputWithCharges(paid: 60000m)));

        Assert.Equal("O total do pagamento não confere com o valor da nota.", e.Message);
    }

    [Fact]
    public void Line_without_charges_omits_them_and_keeps_the_totals_of_today()
    {
        // Review Focus 3: toda nota de hoje (os quatro valores em 0) sai como saía.
        var nfe = Build(NfeTestData.Input());

        var prod = nfe.infNFe.det.Single().prod;
        Assert.Equal(((decimal?)null, (decimal?)null, (decimal?)null, (decimal?)null), (prod.vFrete, prod.vSeg, prod.vDesc, prod.vOutro));
        var total = nfe.infNFe.total.ICMSTot;
        Assert.Equal((0m, 0m, 0m, 0m, 60000m), (total.vFrete, total.vSeg, total.vDesc, total.vOutro, total.vNF));
    }
```

Em `NfeSignerTests.cs`, acrescentar `using System.Globalization;` e `using System.Xml.Linq;` no topo e, no fim da classe:

```csharp
    private static readonly XNamespace Ns = "http://www.portalfiscal.inf.br/nfe";

    private static decimal Number(XElement? element) => decimal.Parse(element!.Value, CultureInfo.InvariantCulture);

    [Fact]
    public void Line_charges_validate_against_the_official_schema()
    {
        using var certificate = Certificate();
        var input = NfeTestData.Input() with
        {
            Items = [NfeTestData.Item() with { FreightValue = 1000m, InsuranceValue = 100m, DiscountValue = 500m, OtherExpensesValue = 400m }],
            Payment = PaymentInstallmentCalculator.Calculate("30,60", PaymentStartRule.IssueDate, "15", 61000m,
                DateOnly.FromDateTime(NfeTestData.IssuedAt.Date)),
        };

        var xml = XDocument.Parse(NfeSigner.BuildSignAndValidate(input, Settings(certificate)).Xml);

        var prod = xml.Descendants(Ns + "det").Single().Element(Ns + "prod")!;
        Assert.Equal((1000m, 100m, 500m, 400m),
            (Number(prod.Element(Ns + "vFrete")), Number(prod.Element(Ns + "vSeg")), Number(prod.Element(Ns + "vDesc")),
                Number(prod.Element(Ns + "vOutro"))));
        Assert.Equal(61000m, Number(xml.Descendants(Ns + "ICMSTot").Single().Element(Ns + "vNF")));
        Assert.Equal(61000m, Number(xml.Descendants(Ns + "vPag").Single()));
    }

    [Fact]
    public void Line_without_charges_signs_without_the_optional_prod_values()
    {
        // Review Focus 3: o det/prod não ganha vFrete/vSeg/vDesc/vOutro zerados.
        using var certificate = Certificate();

        var xml = XDocument.Parse(NfeSigner.BuildSignAndValidate(NfeTestData.Input(), Settings(certificate)).Xml);

        var prod = xml.Descendants(Ns + "det").Single().Element(Ns + "prod")!;
        Assert.Empty(prod.Elements().Where(e => e.Name.LocalName is "vFrete" or "vSeg" or "vDesc" or "vOutro"));
        Assert.Equal(60000m, Number(xml.Descendants(Ns + "ICMSTot").Single().Element(Ns + "vNF")));
    }
```

Em `NfeIssueInputAssemblerTests.cs`, acrescentar no fim da classe (usa `BuildAsync` do arquivo):

```csharp
    [Fact]
    public async Task Line_charges_reach_the_item_and_the_payment_uses_the_grand_total()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var input = await BuildAsync(scenario, invoice =>
        {
            var item = invoice.Items.Single();
            (item.FreightValue, item.InsuranceValue, item.DiscountValue, item.OtherExpensesValue) = (1000m, 100m, 500m, 400m);
        });

        var line = input.Items.Single();
        Assert.Equal((1000m, 100m, 500m, 400m, 61000m),
            (line.FreightValue, line.InsuranceValue, line.DiscountValue, line.OtherExpensesValue, line.GrandTotal));
        Assert.Equal(61000m, input.Payment.PaidAmount);
        Assert.Equal(61000m, input.Payment.Installments.Sum(i => i.Amount));
    }
```

Em `SalesInvoicesNfeIssueServiceTests.cs`, acrescentar `using System.Globalization;` e `using System.Xml.Linq;` no topo e, no fim da classe (usa `Issue` do arquivo):

```csharp
    [Fact]
    public async Task Line_charges_are_issued_with_the_grand_total()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var item = await scenario.Db.Context.SalesInvoicesItems.SingleAsync();
        (item.FreightValue, item.InsuranceValue, item.DiscountValue, item.OtherExpensesValue) = (1000m, 100m, 500m, 400m);
        await scenario.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var outcome = await Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db)).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        XNamespace ns = "http://www.portalfiscal.inf.br/nfe";
        decimal N(XElement? e) => decimal.Parse(e!.Value, CultureInfo.InvariantCulture);
        var xml = XDocument.Parse(Assert.Single(sefaz.Sent).Xml);
        var prod = xml.Descendants(ns + "det").Single().Element(ns + "prod")!;
        Assert.Equal((1000m, 100m, 500m, 400m),
            (N(prod.Element(ns + "vFrete")), N(prod.Element(ns + "vSeg")), N(prod.Element(ns + "vDesc")), N(prod.Element(ns + "vOutro"))));
        var total = xml.Descendants(ns + "ICMSTot").Single();
        Assert.Equal((60000m, 61000m), (N(total.Element(ns + "vProd")), N(total.Element(ns + "vNF"))));
        var fat = xml.Descendants(ns + "fat").Single();
        Assert.Equal((61000m, 61000m), (N(fat.Element(ns + "vOrig")), N(fat.Element(ns + "vLiq"))));
        Assert.Equal(61000m, xml.Descendants(ns + "vDup").Sum(N));
        Assert.Equal(61000m, N(xml.Descendants(ns + "vPag").Single()));
    }
```

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~NfeXmlBuilderTests|FullyQualifiedName~NfeSignerTests"` e `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeIssueInputAssemblerTests|FullyQualifiedName~SalesInvoicesNfeIssueServiceTests"`. Esperado: erro de compilação (`NfeItem.FreightValue` não existe).

- [ ] **Step 3: Implementar**

`NfeIssueInput.cs`, em `NfeItem`, logo depois de `public required decimal Total { get; init; }`:

```csharp

    /// <summary>Frete, seguro, desconto e outras despesas da linha (spec 2026-10-05 §7); 0 = omitido no <c>det/prod</c>.</summary>
    public decimal FreightValue { get; init; }
    public decimal InsuranceValue { get; init; }
    public decimal DiscountValue { get; init; }
    public decimal OtherExpensesValue { get; init; }

    /// <summary>vProd + vFrete + vSeg + vOutro − vDesc: a parte da linha no vNF, no pagamento e na fatura.</summary>
    public decimal GrandTotal => Total + FreightValue + InsuranceValue + OtherExpensesValue - DiscountValue;
```

`NfeXmlBuilder.cs`:
- em `Build`, trocar `var noteTotal = input.Items.Sum(i => i.Total);` por `var noteTotal = input.Items.Sum(i => i.GrandTotal);`
- em `BuildItem`, logo depois de `vUnTrib = item.UnitPrice,`:

```csharp
            // Frete, seguro, desconto e outras despesas da linha: omitidos quando 0 (o leiaute permite, e a nota sem eles
            // sai idêntica à de antes).
            vFrete = Charge(item.FreightValue),
            vSeg = Charge(item.InsuranceValue),
            vDesc = Charge(item.DiscountValue),
            vOutro = Charge(item.OtherExpensesValue),
```

- logo depois do método `BuildItem` (antes de `BuildIcms`):

```csharp
    /// <summary>Valor da linha no <c>det/prod</c>: nulo (não serializado) quando zero.</summary>
    private static decimal? Charge(decimal value) => value == 0m ? null : value;
```

- em `BuildTotal`, depois de `var products = items.Sum(i => i.Total);`:

```csharp
        var freight = items.Sum(i => i.FreightValue);
        var insurance = items.Sum(i => i.InsuranceValue);
        var discount = items.Sum(i => i.DiscountValue);
        var otherExpenses = items.Sum(i => i.OtherExpensesValue);
```

  e, no `ICMSTot`, trocar `vFrete = 0,` por `vFrete = freight,`, `vSeg = 0,` por `vSeg = insurance,`, `vDesc = 0,` por `vDesc = discount,`, `vOutro = 0,` por `vOutro = otherExpenses,` e `vNF = products,` por:

```csharp
                // W16: vNF = vProd − vDesc + vFrete + vSeg + vOutro (sem ST, IPI, II e serviços, que o Siagro não trata).
                vNF = products + freight + insurance + otherExpenses - discount,
```

- em `BuildBilling`, trocar `var total = input.Items.Sum(i => i.Total);` por `var total = input.Items.Sum(i => i.GrandTotal);` (o `fat.vDesc` continua 0: o desconto já está no `vNF`, `vLiq` = `vOrig`).

`NfeIssueInputAssembler.cs`:
- no `Build(NfeDocumentView ...)`, trocar `var total = items.Sum(i => i.Total);` por:

```csharp
        // O total geral (itens + frete + seguro + outras despesas − desconto) é o vNF: é ele que se parcela.
        var total = items.Sum(i => i.GrandTotal);
```

- em `ToItem`, depois de `Total = item.Total,`:

```csharp
        FreightValue = item.FreightValue,
        InsuranceValue = item.InsuranceValue,
        DiscountValue = item.DiscountValue,
        OtherExpensesValue = item.OtherExpensesValue,
```

- [ ] **Step 4: Rodar e ver passar** — os filtros da Step 2, depois `dotnet build SiagroB1.sln` e as duas suítes completas.

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(invoice): frete, seguro, desconto e outras despesas no xml da nf-e" -- SiagroB1.Fiscal/Nfe/NfeIssueInput.cs SiagroB1.Fiscal/Nfe/NfeXmlBuilder.cs SiagroB1.Application/Services/Nfe/NfeIssueInputAssembler.cs SiagroB1.Fiscal.Tests/Nfe/NfeXmlBuilderTests.cs SiagroB1.Fiscal.Tests/Nfe/NfeSignerTests.cs SiagroB1.Application.Tests/Nfe/NfeIssueInputAssemblerTests.cs SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeIssueServiceTests.cs
```

---

### Task 5: XML do fornecedor e valor declarado (R3)

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/SupplierNfeXmlReader.cs:9-17,73-96`
- Modify: `SiagroB1.Domain/Dtos/PurchaseInvoiceDraftDto.cs` (classe `PurchaseInvoiceDraftItemDto`, no fim do arquivo)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesImportXmlService.cs:56-66`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceDeclaredTotal.cs:20-34`
- Modify: `SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeIssueService.cs:62-64`
- Modify: `SiagroB1.Application.Tests/Support/SupplierNfeXml.cs` (`Det` + helper `Charges`)
- Test (acrescentar): `SiagroB1.Application.Tests/PurchaseInvoices/SupplierNfeXmlReaderTests.cs`, `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesImportXmlServiceTests.cs`, `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceDeclaredTotalTests.cs`, `SiagroB1.Application.Tests/Nfe/PurchaseInvoicesNfeIssueServiceTests.cs`

**Interfaces:**
- Consumes: `GrandTotal` da linha (Task 1); o XML da NF-e própria com o total geral (Task 4).
- Produces: `SupplierNfeItem` ganha, no fim, `decimal FreightValue = 0m, decimal InsuranceValue = 0m, decimal DiscountValue = 0m, decimal OtherExpensesValue = 0m`; `PurchaseInvoiceDraftItemDto` ganha `FreightValue`, `InsuranceValue`, `DiscountValue`, `OtherExpensesValue` (`decimal`) — no EDM pelo complexo do retorno da action `PurchaseInvoicesImportXml` (o frontend os lê na Task 7); `PurchaseInvoiceDeclaredTotal.Apply/ApplyAsync` somam `GrandTotal`; a emissão própria grava `TotalDocumentValue = Σ GrandTotal`. Test support: `SupplierNfeXml.Det(..., string? charges = null)` e `SupplierNfeXml.Charges(decimal freight = 0m, decimal insurance = 0m, decimal discount = 0m, decimal other = 0m)`.

- [ ] **Step 1: Suporte de teste**

Em `SupplierNfeXml.cs`, acrescentar o parâmetro `string? charges = null` no FIM da assinatura de `Det` e trocar o trecho `$"<vProd>{F2(quantity * unitPrice)}</vProd></prod><imposto><ICMS>{icms}</ICMS>{ipi}" +` por `$"<vProd>{F2(quantity * unitPrice)}</vProd>{charges}</prod><imposto><ICMS>{icms}</ICMS>{ipi}" +`. Acrescentar o helper:

```csharp
    /// <summary>Frete, seguro, desconto e outras despesas do det/prod, na ordem do leiaute; zero fica de fora (o leiaute permite).</summary>
    public static string Charges(decimal freight = 0m, decimal insurance = 0m, decimal discount = 0m, decimal other = 0m) =>
        (freight > 0 ? $"<vFrete>{F2(freight)}</vFrete>" : string.Empty) +
        (insurance > 0 ? $"<vSeg>{F2(insurance)}</vSeg>" : string.Empty) +
        (discount > 0 ? $"<vDesc>{F2(discount)}</vDesc>" : string.Empty) +
        (other > 0 ? $"<vOutro>{F2(other)}</vOutro>" : string.Empty);
```

- [ ] **Step 2: Escrever os testes que falham**

Em `SupplierNfeXmlReaderTests.cs`, acrescentar no fim da classe:

```csharp
    [Fact]
    public void Reads_the_line_charges_and_absent_ones_are_zero()
    {
        var nfe = SupplierNfeXmlReader.Read(SupplierNfeXml.Bytes(SupplierNfeXml.Build(
            SupplierNfeXml.Det(1, "TRG", "TRIGO", 1000m, 1.5m, SupplierNfeXml.Icms51(1500m),
                charges: SupplierNfeXml.Charges(100m, 20m, 50m, 30m)),
            SupplierNfeXml.Det(2, "MLH", "MILHO", 500m, 1m, SupplierNfeXml.Icms00(500m, 12m)))));

        var first = nfe.Items.Single(i => i.ItemNumber == 1);
        var second = nfe.Items.Single(i => i.ItemNumber == 2);
        Assert.Equal((100m, 20m, 50m, 30m), (first.FreightValue, first.InsuranceValue, first.DiscountValue, first.OtherExpensesValue));
        Assert.Equal((0m, 0m, 0m, 0m), (second.FreightValue, second.InsuranceValue, second.DiscountValue, second.OtherExpensesValue));
    }
```

Em `PurchaseInvoicesImportXmlServiceTests.cs`, acrescentar no fim da classe (usa `Service` do arquivo; o emitente de `SupplierNfeXml` é o CPF 52998224725):

```csharp
    [Fact]
    public async Task Draft_lines_carry_the_line_charges_of_the_xml()
    {
        var xml = SupplierNfeXml.Build(
            SupplierNfeXml.Det(1, "TRG", "TRIGO", 1000m, 1.5m, SupplierNfeXml.Icms51(1500m),
                charges: SupplierNfeXml.Charges(100m, 20m, 50m, 30m)),
            SupplierNfeXml.Det(2, "MLH", "MILHO", 500m, 1m, SupplierNfeXml.Icms00(500m, 12m)));

        var draft = await Service(PurchaseNfeTestSeed.Supplier, "529.982.247-25").ExecuteAsync(SupplierNfeXml.Bytes(xml), "nfe.xml");

        var (first, second) = (draft.Items[0], draft.Items[1]);
        Assert.Equal((100m, 20m, 50m, 30m), (first.FreightValue, first.InsuranceValue, first.DiscountValue, first.OtherExpensesValue));
        Assert.Equal((0m, 0m, 0m, 0m), (second.FreightValue, second.InsuranceValue, second.DiscountValue, second.OtherExpensesValue));
    }
```

Em `PurchaseInvoiceDeclaredTotalTests.cs`, acrescentar no fim da classe (usa `SeedAsync`, `ThirdParty`, `Create`, `Apply`, `DeclaredAsync`):

```csharp
    [Fact]
    public async Task Declared_value_is_the_grand_total_of_the_lines()
    {
        var (db, usage) = await SeedAsync();
        var invoice = ThirdParty(usage);
        var line = invoice.Items.Single();
        (line.FreightValue, line.InsuranceValue, line.DiscountValue, line.OtherExpensesValue) = (100m, 20m, 50m, 30m);

        await Create(db).ExecuteAsync(invoice, "tester");

        Assert.Equal(1600m, await DeclaredAsync(db, invoice.Key));
    }

    [Fact]
    public async Task Changing_the_freight_of_a_line_recalculates_the_declared_value()
    {
        var (db, usage) = await SeedAsync();
        var invoice = ThirdParty(usage);
        await Create(db).ExecuteAsync(invoice, "tester");
        db.Context.ChangeTracker.Clear();
        var patch = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.PurchaseInvoiceKey == invoice.Key);
        patch.FreightValue = 250m;

        await new PurchaseInvoicesItemsUpdateService(db, new FakeItemService(), Apply(db)).ExecuteAsync(patch.Key!.Value, patch, "tester");

        Assert.Equal(1750m, await DeclaredAsync(db, invoice.Key));
    }
```

Em `PurchaseInvoicesNfeIssueServiceTests.cs`, acrescentar `using System.Globalization;` e `using System.Xml.Linq;` no topo e, no fim da classe (usa `ChangeAsync`, `Issue`, `ReloadAsync`):

```csharp
    [Fact]
    public async Task Own_entry_declared_value_and_vNF_are_the_grand_total()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await ChangeAsync(scenario, invoice =>
        {
            var line = invoice.Items.Single();
            (line.FreightValue, line.InsuranceValue, line.DiscountValue, line.OtherExpensesValue) = (100m, 20m, 50m, 30m);
        });
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        await Issue(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(1600m, (await ReloadAsync(scenario)).TotalDocumentValue);
        XNamespace ns = "http://www.portalfiscal.inf.br/nfe";
        var vNF = XDocument.Parse(Assert.Single(sefaz.Sent).Xml).Descendants(ns + "vNF").Single().Value;
        Assert.Equal(1600m, decimal.Parse(vNF, CultureInfo.InvariantCulture));
    }
```

- [ ] **Step 3: Rodar e ver falhar** — `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SupplierNfeXmlReaderTests|FullyQualifiedName~PurchaseInvoicesImportXmlServiceTests|FullyQualifiedName~PurchaseInvoiceDeclaredTotalTests|FullyQualifiedName~PurchaseInvoicesNfeIssueServiceTests"`. Esperado: erro de compilação (`SupplierNfeItem.FreightValue` e `PurchaseInvoiceDraftItemDto.FreightValue` não existem); depois dela, o valor declarado sai 1.500,00 em vez de 1.600,00.

- [ ] **Step 4: Implementar**

`SupplierNfeXmlReader.cs` — o summary do record vira `/// <summary>Item (det) da NF-e do fornecedor: identificação, quantidade, frete/seguro/desconto/outras despesas da linha e a tributação que o fornecedor destacou.</summary>` e a última linha da declaração `decimal CbsRate, decimal CbsRateReduction, decimal CbsValue);` vira:

```csharp
    decimal CbsRate, decimal CbsRateReduction, decimal CbsValue,
    decimal FreightValue = 0m, decimal InsuranceValue = 0m, decimal DiscountValue = 0m, decimal OtherExpensesValue = 0m);
```

No `ReadItem`, o último argumento `CbsValue: Number(gCbs, "vCBS"));` vira:

```csharp
            CbsValue: Number(gCbs, "vCBS"),
            // Frete, seguro, desconto e outras despesas do det/prod (spec 2026-10-05 §8); ausente = 0.
            FreightValue: Number(prod, "vFrete"), InsuranceValue: Number(prod, "vSeg"),
            DiscountValue: Number(prod, "vDesc"), OtherExpensesValue: Number(prod, "vOutro"));
```

`PurchaseInvoiceDraftItemDto` — depois de `NfeItemNumber`:

```csharp

    /// <summary>Frete da linha no XML do fornecedor (<c>det/prod/vFrete</c>); 0 quando ausente.</summary>
    public decimal FreightValue { get; set; }

    /// <summary>Seguro da linha (<c>det/prod/vSeg</c>).</summary>
    public decimal InsuranceValue { get; set; }

    /// <summary>Desconto da linha (<c>det/prod/vDesc</c>).</summary>
    public decimal DiscountValue { get; set; }

    /// <summary>Outras despesas da linha (<c>det/prod/vOutro</c>).</summary>
    public decimal OtherExpensesValue { get; set; }
```

`PurchaseInvoicesImportXmlService.cs` — no `new PurchaseInvoiceDraftItemDto { ... }`, depois de `NfeItemNumber = item.ItemNumber,`:

```csharp
                FreightValue = item.FreightValue,
                InsuranceValue = item.InsuranceValue,
                DiscountValue = item.DiscountValue,
                OtherExpensesValue = item.OtherExpensesValue,
```

`PurchaseInvoiceDeclaredTotal.cs` — no summary da classe, trocar "a soma das linhas" por "o total geral das linhas (itens + frete + seguro + outras despesas − desconto, spec 2026-10-05 R3)"; em `Apply`, `lines.Sum(l => l.Total)` vira `lines.Sum(l => l.GrandTotal)`; em `ApplyAsync`, a última linha vira:

```csharp
        invoice.TotalDocumentValue = others.Sum(i => i.GrandTotal) + (line?.GrandTotal ?? 0m);
```

`PurchaseInvoicesNfeIssueService.cs` — `BeforeSigning` vira:

```csharp
    /// <summary>O total da nota própria é o vNF (total geral das linhas, spec 2026-10-05 R3): o "valor declarado" passa a ser o emitido.</summary>
    protected override void BeforeSigning(PurchaseInvoice invoice) =>
        invoice.TotalDocumentValue = invoice.Items.Sum(i => i.GrandTotal);
```

- [ ] **Step 5: Rodar e ver passar** — o filtro da Step 3, depois `dotnet build SiagroB1.sln` e as duas suítes completas.

- [ ] **Step 6: Commit**

```bash
git commit -m "feat(invoice): ler frete, seguro, desconto e outras despesas do xml e no valor declarado" -- SiagroB1.Application/Services/PurchaseInvoices/SupplierNfeXmlReader.cs SiagroB1.Domain/Dtos/PurchaseInvoiceDraftDto.cs SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesImportXmlService.cs SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceDeclaredTotal.cs SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeIssueService.cs SiagroB1.Application.Tests/Support/SupplierNfeXml.cs SiagroB1.Application.Tests/PurchaseInvoices/SupplierNfeXmlReaderTests.cs SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesImportXmlServiceTests.cs SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceDeclaredTotalTests.cs SiagroB1.Application.Tests/Nfe/PurchaseInvoicesNfeIssueServiceTests.cs
```

---

### Task 6: Devolução proporcional (D4)

**Files:**
- Modify: `SiagroB1.Application/Services/InvoiceLineChargeRules.cs` (método novo `Proportional`)
- Modify: `SiagroB1.Application/Services/SalesInvoices/Factories/SalesInvoiceReturnFactory.cs:59-67`
- Modify: `SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeReturnCreateService.cs:96-108`
- Test (acrescentar): `SiagroB1.Application.Tests/Invoices/InvoiceLineChargeRulesTests.cs`, `SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeReturnCreateServiceTests.cs`, `SiagroB1.Application.Tests/Nfe/PurchaseInvoicesNfeReturnTests.cs`

**Interfaces:**
- Consumes: `InvoiceLineChargeRules` (Task 2); base dos tributos pelo `GrandTotal` (Task 3); emissão com os quatro valores (Task 4 — a origem de compra dos testes é emitida com frete).
- Produces: `public static decimal InvoiceLineChargeRules.Proportional(decimal value, decimal returnedQuantity, decimal originalQuantity)` = `Round(value × devolvida ÷ original, 2, AwayFromZero)` (origem sem quantidade → 0). Usado pelo `SalesInvoiceReturnFactory` — que serve o "Devolver" (NF-e), a devolução antiga (`SalesInvoicesReturnService`) e a recusa de carga (`ShipmentLoadsRefuseService`): as três passam a trazer os valores na proporção — e pelo `PurchaseInvoicesNfeReturnCreateService` (devolução de compra própria e de terceiro).

- [ ] **Step 1: Escrever os testes que falham**

Em `InvoiceLineChargeRulesTests.cs`, acrescentar `using System.Globalization;` no topo e, no fim da classe:

```csharp
    // --- proporção da devolução (D4) ---

    [Theory]
    [InlineData("100.00", "10000", "30000", "33.33")]
    [InlineData("0.05", "15000", "30000", "0.03")]
    [InlineData("50.00", "10000", "30000", "16.67")]
    [InlineData("100.00", "30000", "30000", "100.00")]
    [InlineData("100.00", "0", "0", "0")]
    public void Proportional_value_is_rounded_away_from_zero_in_cents(string value, string returned, string original, string expected)
    {
        decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

        Assert.Equal(D(expected), InvoiceLineChargeRules.Proportional(D(value), D(returned), D(original)));
    }
```

Em `SalesInvoicesNfeReturnCreateServiceTests.cs`, acrescentar no fim da classe. Venda do `NfeReturnTestSeed`: 30.000 kg × 2,00.

```csharp
    // --- Frete, seguro, desconto e outras despesas na proporção do que volta (spec 2026-10-05 D4) ---

    private static async Task SetSaleChargesAsync(NfeReturnScenario s, decimal freight, decimal insurance, decimal discount, decimal other)
    {
        var item = await s.Sale.Db.Context.SalesInvoicesItems.SingleAsync(i => i.Key == s.SaleItemKey);
        (item.FreightValue, item.InsuranceValue, item.DiscountValue, item.OtherExpensesValue) = (freight, insurance, discount, other);
        await s.Sale.Db.SaveChangesAsync();
    }

    private static Task<SalesInvoiceItem> ReturnLineAsync(NfeReturnScenario s, Guid returnKey) =>
        s.Sale.Db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync(i => i.SalesInvoiceKey == returnKey);

    [Fact]
    public async Task Partial_return_brings_the_charges_in_proportion_and_taxes_them()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        await SetSaleChargesAsync(s, 100m, 10m, 50m, 0.05m);

        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 10000m);

        var line = await ReturnLineAsync(s, created.Key);
        Assert.Equal((33.33m, 3.33m, 16.67m, 0.02m), (line.FreightValue, line.InsuranceValue, line.DiscountValue, line.OtherExpensesValue));
        // Base = 20.000,00 + 33,33 + 3,33 + 0,02 − 16,67 (D3 na devolução).
        Assert.Equal(20020.01m, line.IcmsBase);
    }

    [Fact]
    public async Task Half_cent_of_the_proportion_rounds_away_from_zero()
    {
        // Review Focus 2: 0,05 na metade = 0,025 → 0,03 (e não 0,02); 0,01 na metade = 0,005 → 0,01.
        var s = await NfeReturnTestSeed.SeedAsync();
        await SetSaleChargesAsync(s, 0m, 0m, 0.01m, 0.05m);

        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 15000m);

        var line = await ReturnLineAsync(s, created.Key);
        Assert.Equal((0.01m, 0.03m), (line.DiscountValue, line.OtherExpensesValue));
    }

    [Fact]
    public async Task Total_return_brings_exactly_the_charges_of_the_sale()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        await SetSaleChargesAsync(s, 100m, 10m, 50m, 0.05m);

        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);

        var line = await ReturnLineAsync(s, created.Key);
        Assert.Equal((100m, 10m, 50m, 0.05m), (line.FreightValue, line.InsuranceValue, line.DiscountValue, line.OtherExpensesValue));
    }
```

Em `PurchaseInvoicesNfeReturnTests.cs`, acrescentar no fim da classe (usa `Returns`, `Request` do arquivo). Entrada do `PurchaseNfeTestSeed`: 1.000 kg × 1,50.

```csharp
    // --- Frete, seguro, desconto e outras despesas na proporção do que volta (spec 2026-10-05 D4) ---

    /// <summary>A entrada do cenário com os quatro valores na linha, autorizada e confirmada pela emissão.</summary>
    private static async Task<(PurchaseNfeScenario Scenario, PurchaseInvoice Origin)> AuthorizedOriginWithChargesAsync(
        decimal freight, decimal insurance, decimal discount, decimal other)
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var line = await scenario.Db.Context.PurchaseInvoicesItems.SingleAsync(i => i.PurchaseInvoiceKey == scenario.InvoiceKey);
        (line.FreightValue, line.InsuranceValue, line.DiscountValue, line.OtherExpensesValue) = (freight, insurance, discount, other);
        await scenario.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        await PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");
        scenario.Db.Context.ChangeTracker.Clear();

        return (scenario, await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario));
    }

    [Fact]
    public async Task Partial_purchase_return_brings_the_charges_in_proportion_and_taxes_them()
    {
        var (scenario, origin) = await AuthorizedOriginWithChargesAsync(90m, 0.05m, 30m, 0m);

        var created = await Returns(scenario).ExecuteAsync(Request(origin, 400m), "tester");

        var line = (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key)).Items.Single();
        Assert.Equal((36m, 0.02m, 12m, 0m), (line.FreightValue, line.InsuranceValue, line.DiscountValue, line.OtherExpensesValue));
        // Base = 600,00 + 36,00 + 0,02 − 12,00 (D3 na devolução).
        Assert.Equal(624.02m, line.IcmsBase);
    }

    [Fact]
    public async Task Half_cent_of_the_purchase_return_proportion_rounds_away_from_zero()
    {
        // Review Focus 2.
        var (scenario, origin) = await AuthorizedOriginWithChargesAsync(0m, 0.05m, 0m, 0m);

        var created = await Returns(scenario).ExecuteAsync(Request(origin, 500m), "tester");

        Assert.Equal(0.03m, (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key)).Items.Single().InsuranceValue);
    }

    [Fact]
    public async Task Total_purchase_return_brings_exactly_the_charges_of_the_entry()
    {
        var (scenario, origin) = await AuthorizedOriginWithChargesAsync(90m, 0.05m, 30m, 7m);

        var created = await Returns(scenario).ExecuteAsync(Request(origin, 1000m), "tester");

        var line = (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key)).Items.Single();
        Assert.Equal((90m, 0.05m, 30m, 7m), (line.FreightValue, line.InsuranceValue, line.DiscountValue, line.OtherExpensesValue));
    }
```

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~InvoiceLineChargeRulesTests|FullyQualifiedName~SalesInvoicesNfeReturnCreateServiceTests|FullyQualifiedName~PurchaseInvoicesNfeReturnTests"`. Esperado: erro de compilação (`Proportional` não existe); depois dela, as devoluções nascem com os quatro valores em 0.

- [ ] **Step 3: Implementar**

`InvoiceLineChargeRules.cs` — acrescentar, depois de `Ensure`:

```csharp
    /// <summary>
    /// Valor da linha de origem na proporção do que volta (spec 2026-10-05 D4): valor × devolvida ÷ original, em centavos
    /// (<see cref="MidpointRounding.AwayFromZero"/>). Devolvendo a quantidade inteira o valor vem exato; a soma de várias
    /// devoluções parciais pode diferir 1 centavo da origem (risco aceito, a última não é ajustada). Origem sem quantidade:
    /// nada volta.
    /// </summary>
    public static decimal Proportional(decimal value, decimal returnedQuantity, decimal originalQuantity) =>
        originalQuantity == 0m
            ? 0m
            : decimal.Round(value * returnedQuantity / originalQuantity, 2, MidpointRounding.AwayFromZero);
```

`SalesInvoiceReturnFactory.cs` — no `returnInvoice.AddItem(new SalesInvoiceItem { ... })`, depois de `SalesContractKey = item.SalesContractKey,`:

```csharp
                // Frete, seguro, desconto e outras despesas na proporção do que volta (spec 2026-10-05 D4); editáveis
                // enquanto a devolução está Pendente.
                FreightValue = InvoiceLineChargeRules.Proportional(item.FreightValue, quantity, item.Quantity),
                InsuranceValue = InvoiceLineChargeRules.Proportional(item.InsuranceValue, quantity, item.Quantity),
                DiscountValue = InvoiceLineChargeRules.Proportional(item.DiscountValue, quantity, item.Quantity),
                OtherExpensesValue = InvoiceLineChargeRules.Proportional(item.OtherExpensesValue, quantity, item.Quantity),
```

`PurchaseInvoicesNfeReturnCreateService.cs` — no laço que monta as linhas, trocar o corpo por:

```csharp
        foreach (var bought in NfeItemNumbering.Ordered(origin.Items).Where(i => quantities.ContainsKey(i.Key!.Value)))
        {
            var quantity = quantities[bought.Key!.Value];

            returnInvoice.AddItem(new PurchaseInvoiceItem
            {
                Key = Guid.NewGuid(),
                ItemCode = bought.ItemCode,
                ItemName = bought.ItemName,
                UnitOfMeasureCode = bought.UnitOfMeasureCode,
                Quantity = quantity,
                UnitPrice = bought.UnitPrice,
                UsageCode = returnUsages[bought.Key!.Value],
                PurchaseInvoiceItemOriginKey = bought.Key,
                // Frete, seguro, desconto e outras despesas na proporção do que volta (spec 2026-10-05 D4); editáveis
                // enquanto a devolução está Pendente.
                FreightValue = InvoiceLineChargeRules.Proportional(bought.FreightValue, quantity, bought.Quantity),
                InsuranceValue = InvoiceLineChargeRules.Proportional(bought.InsuranceValue, quantity, bought.Quantity),
                DiscountValue = InvoiceLineChargeRules.Proportional(bought.DiscountValue, quantity, bought.Quantity),
                OtherExpensesValue = InvoiceLineChargeRules.Proportional(bought.OtherExpensesValue, quantity, bought.Quantity),
            });
        }
```

- [ ] **Step 4: Rodar e ver passar** — o filtro da Step 2, depois as duas suítes completas (inclui `SalesInvoicesReturnServiceTests`, `SalesInvoicesReturnWeightTests`, `ShipmentLoads*` e `PurchaseInvoicesThirdPartyReturnTests`, que passam pela mesma montagem com os valores em 0).

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(invoice): devolução com frete, seguro, desconto e outras despesas proporcionais" -- SiagroB1.Application/Services/InvoiceLineChargeRules.cs SiagroB1.Application/Services/SalesInvoices/Factories/SalesInvoiceReturnFactory.cs SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeReturnCreateService.cs SiagroB1.Application.Tests/Invoices/InvoiceLineChargeRulesTests.cs SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeReturnCreateServiceTests.cs SiagroB1.Application.Tests/Nfe/PurchaseInvoicesNfeReturnTests.cs
```

---

### Task 7: Totais no cliente, payloads e rascunho (frontend)

**Files (repo `siagro-b1-frontend`, branch `feature/invoice-line-charges`; conferir com `git status` que só os `.tlpp` estão modificados antes de começar):**
- Create: `webapp/helpers/InvoiceChargeTotalsHelpers.ts`
- Create: `webapp/test/unit/helpers/InvoiceChargeTotalsHelpers.qunit.ts`
- Modify: `webapp/test/unit/unitTests.qunit.ts` (registrar o teste novo)
- Modify: `webapp/helpers/PurchaseInvoiceDraftHelpers.ts`
- Modify: `webapp/test/unit/helpers/PurchaseInvoiceDraftHelpers.qunit.ts`
- Modify: `webapp/helpers/PurchaseInvoiceNfeHelpers.ts:147-153` (`PURCHASE_ITEM_SELECT`)
- Modify: `webapp/controller/salesInvoices/BaseController.ts` (import + `refreshDocumentTotal`, ~linhas 16, 205-246)
- Modify: `webapp/controller/salesInvoices/Detail.controller.ts:19,40,45`
- Modify: `webapp/controller/salesInvoices/Add.controller.ts:153-177` (`onAddItem`)
- Modify: `webapp/controller/purchaseInvoices/BaseController.ts` (import + `refreshDocumentTotal`, ~linhas 14, 290-333)
- Modify: `webapp/controller/purchaseInvoices/Detail.controller.ts` (import + reset, ~linha 58)
- Modify: `webapp/controller/purchaseInvoices/Add.controller.ts:52`
- Modify: `webapp/controller/purchaseInvoices/Edit.controller.ts:80-84` (`onAddItem`)
- Modify: `webapp/controller/shipmentBilling/Main.controller.ts:309-317` (linha da saída do faturamento)
- Modify: `webapp/view/purchaseInvoices/fragments/Items.fragment.xml:27-31` (total da barra)

**Interfaces:**
- Consumes: propriedades `FreightValue`, `InsuranceValue`, `DiscountValue`, `OtherExpensesValue` (Edm.Decimal) das linhas e do rascunho da importação (Tasks 1 e 5).
- Produces (`siagrob1/helpers/InvoiceChargeTotalsHelpers`): `type ChargeLine`, `type ChargeTotalRow = { Label: string; Value: number; Emphasized: boolean }`, `type ChargeTotals = { items; freight; insurance; otherExpenses; discount; grandTotal: number; rows: ChargeTotalRow[] }`, `LINE_CHARGES_SELECT`, `lineItemsTotal(q, p): number`, `lineGrandTotal(q, p, freight, insurance, discount, other): number`, `formatAmount(value: number): string`, `formatLineGrandTotal(q, p, freight, insurance, discount, other): string`, `chargeLineOf(ctx: { getProperty(path: string): unknown }): ChargeLine`, `summarizeInvoiceCharges(lines: ChargeLine[]): ChargeTotals`. Modelo `ui`: `/documentTotal` (texto pt-BR do total geral — saída e entrada) e `/chargeTotals` (o `ChargeTotals`), que a Task 8 liga na grade e na seção "Totais".

- [ ] **Step 1: Escrever os testes que falham**

`webapp/test/unit/helpers/InvoiceChargeTotalsHelpers.qunit.ts` (`git add` logo depois de criar):

```ts
import {
	ChargeLine, chargeLineOf, formatAmount, formatLineGrandTotal, LINE_CHARGES_SELECT, lineGrandTotal, summarizeInvoiceCharges,
} from "siagrob1/helpers/InvoiceChargeTotalsHelpers";
import { PURCHASE_ITEM_SELECT } from "siagrob1/helpers/PurchaseInvoiceNfeHelpers";

QUnit.module("InvoiceChargeTotalsHelpers - frete, seguro, desconto e outras despesas");

/** Linha zerada; cada teste liga só o que importa. */
function line(values: Partial<ChargeLine>): ChargeLine {
	return { Quantity: 0, UnitPrice: 0, FreightValue: 0, InsuranceValue: 0, DiscountValue: 0, OtherExpensesValue: 0, ...values };
}

QUnit.test("linha sem os quatro valores: total geral = quantidade x preço", function (assert) {
	assert.strictEqual(lineGrandTotal(1000, 1.5, 0, 0, 0, 0), 1500);
	assert.strictEqual(lineGrandTotal(1000, 1.5, null, undefined, "", null), 1500);
});

QUnit.test("total geral da linha soma frete, seguro e outras despesas e tira o desconto", function (assert) {
	assert.strictEqual(lineGrandTotal(1000, 1.5, 100, 20, 50, 30), 1600);
});

QUnit.test("desconto do valor inteiro da linha deixa o total geral em zero", function (assert) {
	assert.strictEqual(lineGrandTotal(10, 2, 5, 1, 26.5, 0.5), 0);
});

QUnit.test("Edm.Decimal chega em string (IEEE754Compatible): converte antes de somar", function (assert) {
	assert.strictEqual(lineGrandTotal("1000.000", "1.50000000", "100.00", "20.00", "50.00", "30.00"), 1600);
});

QUnit.test("seção Totais soma as linhas na ordem da spec, com o total geral destacado", function (assert) {
	const result = summarizeInvoiceCharges([
		line({ Quantity: 1000, UnitPrice: 1.5, FreightValue: 100, InsuranceValue: 20, DiscountValue: 50, OtherExpensesValue: 30 }),
		line({ Quantity: "10.000", UnitPrice: "2.00", FreightValue: "5.00", DiscountValue: "1.00" }),
	]);

	assert.deepEqual(result.rows, [
		{ Label: "Total dos itens", Value: 1520, Emphasized: false },
		{ Label: "Frete", Value: 105, Emphasized: false },
		{ Label: "Seguro", Value: 20, Emphasized: false },
		{ Label: "Outras despesas", Value: 30, Emphasized: false },
		{ Label: "(−) Desconto", Value: 51, Emphasized: false },
		{ Label: "Total geral", Value: 1624, Emphasized: true },
	]);
	assert.strictEqual(result.grandTotal, 1624);
});

QUnit.test("documento sem linhas: tudo zero", function (assert) {
	const result = summarizeInvoiceCharges([]);

	assert.deepEqual([result.items, result.freight, result.discount, result.grandTotal], [0, 0, 0, 0]);
});

QUnit.test("linha ainda sem dados (rascunho recém-criado) é ignorada, sem estourar", function (assert) {
	const result = summarizeInvoiceCharges([undefined, line({ Quantity: 1, UnitPrice: 10, FreightValue: 2 }), null] as ChargeLine[]);

	assert.strictEqual(result.grandTotal, 12);
});

QUnit.test("arredonda a soma em centavos, sem o ruído do ponto flutuante", function (assert) {
	const result = summarizeInvoiceCharges([line({ FreightValue: 0.1 }), line({ FreightValue: 0.2 })]);

	assert.strictEqual(result.freight, 0.3);
	assert.strictEqual(result.grandTotal, 0.3);
});

QUnit.test("formatter da coluna Total devolve o total geral em pt-BR", function (assert) {
	assert.strictEqual(formatLineGrandTotal("30000", "2", "1000", "100", "500", "400"), "61.000,00");
	assert.strictEqual(formatAmount(0), "0,00");
	assert.strictEqual(formatAmount(1234.5), "1.234,50");
});

QUnit.test("chargeLineOf lê os seis campos pelo getProperty do contexto", function (assert) {
	const values: Record<string, unknown> = {
		Quantity: 2, UnitPrice: "3.00", FreightValue: 1, InsuranceValue: 0, DiscountValue: "0.50", OtherExpensesValue: null,
	};

	assert.deepEqual(chargeLineOf({ getProperty: (path: string) => values[path] }), {
		Quantity: 2, UnitPrice: "3.00", FreightValue: 1, InsuranceValue: 0, DiscountValue: "0.50", OtherExpensesValue: null,
	});
});

QUnit.test("o $select das linhas da entrada traz os campos da seção Totais", function (assert) {
	const purchase = PURCHASE_ITEM_SELECT.split(",");

	assert.deepEqual(LINE_CHARGES_SELECT.split(",").filter((field) => !purchase.includes(field)), []);
});
```

Em `unitTests.qunit.ts`, acrescentar no fim: `import "./helpers/InvoiceChargeTotalsHelpers.qunit";`

Em `PurchaseInvoiceDraftHelpers.qunit.ts`:
- o teste "a linha em branco já traz as chaves que a tela edita, nulas" passa a esperar (**asserção existente que muda** — motivo: spec §10, os quatro campos existem desde a linha em branco, com 0):

```ts
	assert.deepEqual(blankItemRow(), {
		ItemCode: "", ItemName: "", UnitOfMeasureCode: "", Quantity: 0, UnitPrice: 0,
		SalesInvoiceItemKey: null, PurchaseContractKey: null, UsageCode: null, UsageName: null,
		NfeItemNumber: null, FreightValue: 0, InsuranceValue: 0, DiscountValue: 0, OtherExpensesValue: 0,
	});
```

- acrescentar no fim:

```ts
QUnit.test("XML importado leva frete, seguro, desconto e outras despesas da linha, em número", function (assert) {
	const rows = draftItemRows([
		{
			ItemCode: "TRIGO", ItemName: "TRIGO", UnitOfMeasureCode: "KG", Quantity: "1000.0000", UnitPrice: "1.5000000000",
			FreightValue: "100.00", InsuranceValue: "20.00", DiscountValue: "50.00", OtherExpensesValue: "30.00",
		},
		{ ItemCode: "MILHO", ItemName: "MILHO", UnitOfMeasureCode: "KG", Quantity: 1, UnitPrice: 1 },
	]);

	assert.deepEqual(rows.map((row) => [row.FreightValue, row.InsuranceValue, row.DiscountValue, row.OtherExpensesValue]),
		[[100, 20, 50, 30], [0, 0, 0, 0]]);
});
```

- [ ] **Step 2: Rodar e ver falhar** — `yarn ts-typecheck`. Esperado: erro (`siagrob1/helpers/InvoiceChargeTotalsHelpers` não existe; `FreightValue` não existe em `ImportedInvoiceItem`).

- [ ] **Step 3: Implementar**

`webapp/helpers/InvoiceChargeTotalsHelpers.ts` (`git add` logo depois de criar):

```ts
/**
 * Frete, seguro, desconto e outras despesas da linha dos documentos de entrada e de saída (spec 2026-10-05 §10): o total
 * geral da linha (coluna "Total"), a seção "Totais" do Detail e o total da barra da grade, somados NO CLIENTE —
 * `GrandTotal` é [NotMapped] e não existe num documento em digitação.
 *
 * Puro para poder ser testado sem view; as duas entidades de linha têm os mesmos campos.
 */
import { toNumber } from "siagrob1/helpers/PurchaseInvoiceDraftHelpers";

/** Valor como chega do modelo: Edm.Decimal vem em string (IEEE754Compatible), o digitado vem em número. */
type Amount = number | string;

/** Campos da linha que o total geral lê. */
export type ChargeLine = {
	Quantity: Amount;
	UnitPrice: Amount;
	FreightValue: Amount;
	InsuranceValue: Amount;
	DiscountValue: Amount;
	OtherExpensesValue: Amount;
};

/** Linha da seção "Totais". */
export type ChargeTotalRow = { Label: string; Value: number; Emphasized: boolean };

/** Seção "Totais" do documento: as somas e as linhas na ordem da spec. */
export type ChargeTotals = {
	items: number;
	freight: number;
	insurance: number;
	otherExpenses: number;
	discount: number;
	grandTotal: number;
	rows: ChargeTotalRow[];
};

/** Campos das linhas que a seção precisa no `$select` (o Detail monta o $select das linhas à mão). */
export const LINE_CHARGES_SELECT = "Quantity,UnitPrice,FreightValue,InsuranceValue,DiscountValue,OtherExpensesValue";

/** Arredonda em centavos; o EPSILON tira o ruído do ponto flutuante (0,1 + 0,2). */
function cents(value: number): number {
	return Math.round((value + Number.EPSILON) * 100) / 100;
}

/** Total dos itens da linha (o vProd): quantidade × preço, em centavos — como o `Total` do servidor. */
export function lineItemsTotal(quantity: unknown, unitPrice: unknown): number {
	return cents(toNumber(quantity) * toNumber(unitPrice));
}

/** Total geral da linha (spec D2): itens + frete + seguro + outras despesas − desconto. */
export function lineGrandTotal(
	quantity: unknown, unitPrice: unknown, freight: unknown, insurance: unknown, discount: unknown, otherExpenses: unknown,
): number {
	return cents(lineItemsTotal(quantity, unitPrice) + toNumber(freight) + toNumber(insurance) +
		toNumber(otherExpenses) - toNumber(discount));
}

/** Valor em pt-BR com 2 casas ("1.234,50"); indefinido vira "0,00". */
export function formatAmount(value: number): string {
	return (value ?? 0).toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

/** Formatter da coluna "Total" da grade: seis partes, todas com `targetType: 'any'`. */
export function formatLineGrandTotal(
	quantity: unknown, unitPrice: unknown, freight: unknown, insurance: unknown, discount: unknown, otherExpenses: unknown,
): string {
	return formatAmount(lineGrandTotal(quantity, unitPrice, freight, insurance, discount, otherExpenses));
}

/**
 * Lê os campos da linha pelo `getProperty` do contexto, e não pelo `getObject()`: na inclusão o `getObject()` da linha
 * transiente ainda volta undefined, e a linha recém-digitada sumiria do total.
 */
export function chargeLineOf(context: { getProperty(path: string): unknown }): ChargeLine {
	return {
		Quantity: context.getProperty("Quantity") as Amount,
		UnitPrice: context.getProperty("UnitPrice") as Amount,
		FreightValue: context.getProperty("FreightValue") as Amount,
		InsuranceValue: context.getProperty("InsuranceValue") as Amount,
		DiscountValue: context.getProperty("DiscountValue") as Amount,
		OtherExpensesValue: context.getProperty("OtherExpensesValue") as Amount,
	};
}

/** Seção "Totais" do documento, na ordem da spec; linha nula (rascunho recém-criado) é ignorada. */
export function summarizeInvoiceCharges(allLines: ChargeLine[]): ChargeTotals {
	const lines = allLines.filter((line) => !!line);
	const sum = (pick: (line: ChargeLine) => number) => cents(lines.reduce((total, line) => total + pick(line), 0));

	const items = sum((line) => lineItemsTotal(line.Quantity, line.UnitPrice));
	const freight = sum((line) => toNumber(line.FreightValue));
	const insurance = sum((line) => toNumber(line.InsuranceValue));
	const otherExpenses = sum((line) => toNumber(line.OtherExpensesValue));
	const discount = sum((line) => toNumber(line.DiscountValue));
	const grandTotal = cents(items + freight + insurance + otherExpenses - discount);

	return {
		items, freight, insurance, otherExpenses, discount, grandTotal,
		rows: [
			{ Label: "Total dos itens", Value: items, Emphasized: false },
			{ Label: "Frete", Value: freight, Emphasized: false },
			{ Label: "Seguro", Value: insurance, Emphasized: false },
			{ Label: "Outras despesas", Value: otherExpenses, Emphasized: false },
			{ Label: "(−) Desconto", Value: discount, Emphasized: false },
			{ Label: "Total geral", Value: grandTotal, Emphasized: true },
		],
	};
}
```

`PurchaseInvoiceDraftHelpers.ts`:
- em `InvoiceItemPayload`, depois de `NfeItemNumber: number;`:

```ts
  /** Frete, seguro, desconto e outras despesas da linha (spec 2026-10-05): 0 até o operador informar ou o XML trazer. */
  FreightValue: number;
  InsuranceValue: number;
  DiscountValue: number;
  OtherExpensesValue: number;
```

- em `ImportedInvoiceItem`, depois de `NfeItemNumber?: number;`:

```ts
  /** Frete, seguro, desconto e outras despesas do `det/prod` (Edm.Decimal da action, em string); ausentes = 0. */
  FreightValue?: number | string;
  InsuranceValue?: number | string;
  DiscountValue?: number | string;
  OtherExpensesValue?: number | string;
```

- `blankItemRow` passa a devolver:

```ts
  return {
    ItemCode: "", ItemName: "", UnitOfMeasureCode: "", Quantity: 0, UnitPrice: 0,
    SalesInvoiceItemKey: null, PurchaseContractKey: null, UsageCode: null, UsageName: null,
    NfeItemNumber: null, FreightValue: 0, InsuranceValue: 0, DiscountValue: 0, OtherExpensesValue: 0,
  };
```

- em `draftItemRows`, depois de `NfeItemNumber: item.NfeItemNumber ?? null,`:

```ts
    FreightValue: toNumber(item.FreightValue),
    InsuranceValue: toNumber(item.InsuranceValue),
    DiscountValue: toNumber(item.DiscountValue),
    OtherExpensesValue: toNumber(item.OtherExpensesValue),
```

`PurchaseInvoiceNfeHelpers.ts` — a última linha de `PURCHASE_ITEM_SELECT`, `"IbsStateValue,IbsMunicipalValue,NfeItemNumber";`, vira `"IbsStateValue,IbsMunicipalValue,NfeItemNumber,FreightValue,InsuranceValue,DiscountValue,OtherExpensesValue";`.

`salesInvoices/BaseController.ts`:
- depois de `import { summarizeInvoiceTaxes, TaxLine } from "siagrob1/helpers/InvoiceTaxTotalsHelpers";`: `import { chargeLineOf, formatAmount, summarizeInvoiceCharges } from "siagrob1/helpers/InvoiceChargeTotalsHelpers";`
- o summary e o corpo de `refreshDocumentTotal` passam a ser:

```ts
    /**
     * Total geral do documento (itens + frete + seguro + outras despesas − desconto, spec 2026-10-05 D2), somado NO
     * CLIENTE, e a seção "Totais" do Detail (`/chargeTotals`).
     *
     * `GrandTotal` é [NotMapped]: só existe depois que o servidor responde, então num documento em digitação não haveria
     * total nenhum. Aqui a soma acompanha a grade.
     *
     * Percorre `getAllCurrentContexts()` e não as linhas visíveis: a sap.ui.table é virtualizada, e somar o que está na
     * tela daria um total menor conforme a rolagem.
     *
     * Recalcula também o quadro "Tributos" do Detail (`/taxTotals`), que soma as mesmas linhas.
     */
    protected refreshDocumentTotal() {
      const oTable = this.byId("tableSalesInvoicesItems") as Table;
      const oBinding = oTable?.getBinding("rows") as ODataListBinding;
      const uiModel = this.getModel("ui") as JSONModel;

      if (!oBinding || !uiModel) {
        return;
      }

      const contexts = oBinding.getAllCurrentContexts();
      const charges = summarizeInvoiceCharges(contexts.map((ctx) => chargeLineOf(ctx)));

      uiModel.setProperty("/documentTotal", formatAmount(charges.grandTotal));
      uiModel.setProperty("/chargeTotals", charges);
      uiModel.setProperty("/taxTotals", summarizeInvoiceTaxes(contexts.map((ctx) => ctx.getObject() as TaxLine)));
    }

    /** Quantidade, preço ou um dos quatro valores da linha mudou: o total geral acompanha. */
    onItemAmountChange() {
      this.refreshDocumentTotal();
    }
```

(substitui também o `onItemAmountChange` existente, logo abaixo do método.)

`salesInvoices/Detail.controller.ts`:
- trocar `import { TAX_TOTALS_SELECT } from "siagrob1/helpers/InvoiceTaxTotalsHelpers";` por:

```ts
import { TAX_TOTALS_SELECT } from "siagrob1/helpers/InvoiceTaxTotalsHelpers";
import { LINE_CHARGES_SELECT, summarizeInvoiceCharges } from "siagrob1/helpers/InvoiceChargeTotalsHelpers";
```

- depois de `uiModel.setProperty("/taxTotals", { visible: false, rows: [] });`: `uiModel.setProperty("/chargeTotals", summarizeInvoiceCharges([]));`
- a linha do `bindElement` vira (e o comentário acima ganha "e os quatro valores da seção Totais"):

```ts
			this.bindElement(sPath, { $expand: `Items($select=${TAX_TOTALS_SELECT},${LINE_CHARGES_SELECT})` });
```

`salesInvoices/Add.controller.ts` — no `oBinding.create({ ... })` do `onAddItem`, depois de `UnitPrice: 0,`:

```ts
      FreightValue: 0,
      InsuranceValue: 0,
      DiscountValue: 0,
      OtherExpensesValue: 0,
```

`purchaseInvoices/BaseController.ts`:
- depois de `import { summarizeInvoiceTaxes, TaxLine } from "siagrob1/helpers/InvoiceTaxTotalsHelpers";`: `import { chargeLineOf, formatAmount, summarizeInvoiceCharges } from "siagrob1/helpers/InvoiceChargeTotalsHelpers";`
- `onItemAmountChange` e `refreshDocumentTotal` passam a ser:

```ts
  /** Quantidade, preço ou um dos quatro valores da linha mudou: o "Total geral" acompanha. */
  onItemAmountChange() {
    this.refreshDocumentTotal();
  }

  /**
   * Total geral do documento (itens + frete + seguro + outras despesas − desconto, spec 2026-10-05 D2) e a seção
   * "Totais" do Detail (`/chargeTotals`).
   *
   * Calculado NO CLIENTE porque `GrandTotal` é derivado e, num documento em digitação, o servidor ainda não respondeu
   * nada.
   *
   * Não confundir com `TotalDocumentValue`, que é o total DECLARADO pelo emitente: na filial que emite pelo Siagro o
   * servidor o grava igual a este; fora dela, os dois divergirem é informação de conciliação, não erro.
   *
   * Recalcula também o quadro "Tributos" do Detail (`/taxTotals`), que soma as mesmas linhas.
   */
  protected refreshDocumentTotal() {
    const oTable = this.byId("tablePurchaseInvoiceItems") as Table;
    const oBinding = oTable?.getBinding("rows") as ODataListBinding;
    const uiModel = this.getModel("ui") as JSONModel;

    if (!oBinding || !uiModel) {
      return;
    }

    const contexts = oBinding.getAllCurrentContexts();
    const charges = summarizeInvoiceCharges(contexts.map((ctx) => chargeLineOf(ctx)));

    uiModel.setProperty("/documentTotal", formatAmount(charges.grandTotal));
    uiModel.setProperty("/chargeTotals", charges);
    uiModel.setProperty("/taxTotals", summarizeInvoiceTaxes(contexts.map((ctx) => ctx.getObject() as TaxLine)));
  }
```

`purchaseInvoices/Detail.controller.ts` — importar `import { summarizeInvoiceCharges } from "siagrob1/helpers/InvoiceChargeTotalsHelpers";` e, depois de `uiModel.setProperty("/taxTotals", { visible: false, rows: [] });`, acrescentar `uiModel.setProperty("/chargeTotals", summarizeInvoiceCharges([]));`.

`purchaseInvoices/Add.controller.ts` — `uiModel.setProperty("/totalItems", "0,00");` vira `uiModel.setProperty("/documentTotal", "0,00");`.

`purchaseInvoices/Edit.controller.ts` — o `oBinding.create({ ... })` do `onAddItem` vira:

```ts
    oBinding.create({
      ItemCode: "", ItemName: "", UnitOfMeasureCode: "",
      Quantity: 0, UnitPrice: 0, SalesInvoiceItemKey: null, PurchaseContractKey: null,
      UsageCode: null, UsageName: null,
      FreightValue: 0, InsuranceValue: 0, DiscountValue: 0, OtherExpensesValue: 0,
    }, false, false, false);
```

`shipmentBilling/Main.controller.ts` — na linha de `Items: [ { ... } ]` da saída, depois de `SalesShipmentReleaseKey: release?.SalesShipmentReleaseKey`, acrescentar (com vírgula na linha anterior):

```ts
                // Frete, seguro, desconto e outras despesas da linha (spec 2026-10-05 §10): o faturamento não os informa.
                FreightValue: 0,
                InsuranceValue: 0,
                DiscountValue: 0,
                OtherExpensesValue: 0,
```

`view/purchaseInvoices/fragments/Items.fragment.xml` — o `ObjectNumber` da barra passa a ser:

```xml
          <ObjectNumber
            number="{ui>/documentTotal}"
            unit="Total geral"
            emphasized="true"
            state="None" />
```

- [ ] **Step 4: Rodar e ver passar** — `yarn ts-typecheck`; `yarn lint`; `npx ui5lint` (≤ 919); suíte QUnit de unidade inteira (ver Global Constraints), com os módulos "InvoiceChargeTotalsHelpers - frete, seguro, desconto e outras despesas" e "PurchaseInvoiceDraftHelpers - linhas do rascunho da entrada" verdes.

- [ ] **Step 5: Commit**

```bash
git add webapp/helpers/InvoiceChargeTotalsHelpers.ts webapp/test/unit/helpers/InvoiceChargeTotalsHelpers.qunit.ts
git commit -m "feat(invoice): total geral com frete, seguro, desconto e outras despesas nas telas" -- webapp/helpers/InvoiceChargeTotalsHelpers.ts webapp/test/unit/helpers/InvoiceChargeTotalsHelpers.qunit.ts webapp/test/unit/unitTests.qunit.ts webapp/helpers/PurchaseInvoiceDraftHelpers.ts webapp/test/unit/helpers/PurchaseInvoiceDraftHelpers.qunit.ts webapp/helpers/PurchaseInvoiceNfeHelpers.ts webapp/controller/salesInvoices/BaseController.ts webapp/controller/salesInvoices/Detail.controller.ts webapp/controller/salesInvoices/Add.controller.ts webapp/controller/purchaseInvoices/BaseController.ts webapp/controller/purchaseInvoices/Detail.controller.ts webapp/controller/purchaseInvoices/Add.controller.ts webapp/controller/purchaseInvoices/Edit.controller.ts webapp/controller/shipmentBilling/Main.controller.ts webapp/view/purchaseInvoices/fragments/Items.fragment.xml
```

---

### Task 8: Colunas da grade, seção "Totais" e listas (frontend)

**Files:**
- Create: `webapp/fragments/InvoiceChargeTotals.fragment.xml`
- Modify: `webapp/view/salesInvoices/fragments/Items.fragment.xml` (`core:require` linha 7; colunas depois de "Valor Unitário" ~linha 145; coluna "Total" ~linhas 159-174)
- Modify: `webapp/view/purchaseInvoices/fragments/Items.fragment.xml` (`core:require` linha 5; colunas depois de "Preço Unitário" ~linha 125; coluna "Total" ~linhas 126-142)
- Modify: `webapp/view/salesInvoices/Detail.view.xml` (seção nova entre "Itens do Documento de Saída" e "Tributos", ~linha 104)
- Modify: `webapp/view/purchaseInvoices/Detail.view.xml` (seção nova entre "Itens" e "Tributos", ~linha 64)
- Modify: `webapp/view/salesInvoices/Main.view.xml:164-185` (coluna "Valor Produtos")
- Modify: `webapp/controller/salesInvoices/BaseController.ts` (`createColumnConfig`, ~linha 413)
- Modify: `webapp/view/purchaseInvoices/Main.view.xml` (coluna nova depois de "Valor declarado", ~linha 160)
- Modify: `webapp/controller/purchaseInvoices/Main.controller.ts:219-226` (`createColumnConfig`)

**Interfaces:**
- Consumes: `formatLineGrandTotal`, `formatAmount`, `/chargeTotals`, `/documentTotal`, `onItemAmountChange` (Task 7); `GrandTotal` do cabeçalho no EDM (Task 1).

- [ ] **Step 1: Implementar**

`webapp/fragments/InvoiceChargeTotals.fragment.xml` (`git add` logo depois de criar):

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:core="sap.ui.core"
    core:require="{
      charges: 'siagrob1/helpers/InvoiceChargeTotalsHelpers'
    }"
>
  <!-- Seção "Totais" do Detail da entrada e da saída (spec 2026-10-05 §10): total dos itens, os quatro valores da linha
       somados e o total geral, calculados no cliente (InvoiceChargeTotalsHelpers) e gravados em ui>/chargeTotals pelo
       BaseController, junto do total da barra da grade. -->
  <Table
    id="tableInvoiceChargeTotals"
    class="sapUiSizeCondensed"
    width="30rem"
    items="{ui>/chargeTotals/rows}"
  >
    <columns>
      <Column width="16rem">
        <Text text="Descrição" />
      </Column>
      <Column hAlign="End">
        <Text text="Valor" />
      </Column>
    </columns>
    <items>
      <ColumnListItem>
        <cells>
          <Text text="{ui>Label}" />
          <ObjectNumber
            number="{path: 'ui>Value', formatter: 'charges.formatAmount'}"
            emphasized="{ui>Emphasized}"
            state="None" />
        </cells>
      </ColumnListItem>
    </items>
  </Table>
</core:FragmentDefinition>
```

`view/salesInvoices/fragments/Items.fragment.xml`:
- o `core:require` da raiz vira `core:require="{ charges: 'siagrob1/helpers/InvoiceChargeTotalsHelpers' }"` (o `formatter` só servia ao `formatLineTotal`, que sai daqui);
- o comentário acima do `ObjectNumber` `salesInvoiceDocumentTotal` vira `<!-- Total geral do documento (itens + frete + seguro + outras despesas − desconto). Somado no cliente pelo mesmo motivo do total da linha: GrandTotal é [NotMapped] e não existe antes de salvar. -->`;
- logo depois do `</t:Column>` da coluna "Valor Unitário", inserir:

```xml
      <!-- Frete, seguro, desconto e outras despesas da linha (spec 2026-10-05 §10). Campo de ESCRITA de decimal usa
           `Double`, nunca `Decimal` (o backend recusa Edm.Decimal em string). Editáveis também na devolução Pendente
           (vêm da venda na proporção do que volta). -->
      <t:Column label="Frete" hAlign="End" width="8rem">
        <t:template>
          <Input
              editable="{ui>/editable}"
              textAlign="End"
              change=".onItemAmountChange"
              value="{
                path: 'FreightValue',
                type: 'sap.ui.model.odata.type.Double',
                formatOptions: { decimals: 2, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' }
              }"
          />
        </t:template>
      </t:Column>
      <t:Column label="Seguro" hAlign="End" width="8rem">
        <t:template>
          <Input
              editable="{ui>/editable}"
              textAlign="End"
              change=".onItemAmountChange"
              value="{
                path: 'InsuranceValue',
                type: 'sap.ui.model.odata.type.Double',
                formatOptions: { decimals: 2, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' }
              }"
          />
        </t:template>
      </t:Column>
      <t:Column label="Desconto" hAlign="End" width="8rem">
        <t:template>
          <Input
              editable="{ui>/editable}"
              textAlign="End"
              change=".onItemAmountChange"
              value="{
                path: 'DiscountValue',
                type: 'sap.ui.model.odata.type.Double',
                formatOptions: { decimals: 2, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' }
              }"
          />
        </t:template>
      </t:Column>
      <t:Column label="Outras despesas" hAlign="End" width="9rem">
        <t:template>
          <Input
              editable="{ui>/editable}"
              textAlign="End"
              change=".onItemAmountChange"
              value="{
                path: 'OtherExpensesValue',
                type: 'sap.ui.model.odata.type.Double',
                formatOptions: { decimals: 2, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' }
              }"
          />
        </t:template>
      </t:Column>
```

- o template da coluna "Total" vira:

```xml
      <t:Column label="Total">
        <t:template>
          <!-- Total geral da linha (itens + frete + seguro + outras despesas − desconto), calculado no cliente: `GrandTotal`
               é [NotMapped] e só existe depois que o servidor responde. `targetType: 'any'` em cada parte é obrigatório —
               sem ele o binding composto entrega texto. -->
          <Text
              text="{
                parts: [
                  { path: 'Quantity', targetType: 'any' },
                  { path: 'UnitPrice', targetType: 'any' },
                  { path: 'FreightValue', targetType: 'any' },
                  { path: 'InsuranceValue', targetType: 'any' },
                  { path: 'DiscountValue', targetType: 'any' },
                  { path: 'OtherExpensesValue', targetType: 'any' }
                ],
                formatter: 'charges.formatLineGrandTotal'
              }"
          />
        </t:template>
      </t:Column>
```

`view/purchaseInvoices/fragments/Items.fragment.xml`:
- o `core:require` da raiz vira `core:require="{ formatter: 'siagrob1/model/formatter', charges: 'siagrob1/helpers/InvoiceChargeTotalsHelpers' }"` (o `formatter` continua: `stateInvoiceDifference`);
- logo depois do `</t:Column>` da coluna "Preço Unitário", inserir as mesmas quatro colunas da saída acima (idênticas, com `width` "9rem" em todas);
- o template da coluna "Total" vira o mesmo `Text` de seis partes da saída acima (`formatter: 'charges.formatLineGrandTotal'`), com o comentário: `<!-- Total geral da linha (itens + frete + seguro + outras despesas − desconto), calculado no cliente: GrandTotal é [NotMapped]. targetType 'any' em cada parte: sem ele o binding composto entrega texto e a soma vira NaN. -->`

`view/salesInvoices/Detail.view.xml` — logo antes de `<uxap:ObjectPageSection titleUppercase="false" title="Tributos" ...>`:

```xml
      <uxap:ObjectPageSection titleUppercase="false" title="Totais">
				<uxap:subSections>
					<uxap:ObjectPageSubSection title=" " titleUppercase="false">
						<core:Fragment fragmentName="siagrob1.fragments.InvoiceChargeTotals" type="XML" />
					</uxap:ObjectPageSubSection>
				</uxap:subSections>
			</uxap:ObjectPageSection>
```

`view/purchaseInvoices/Detail.view.xml` — logo antes de `<uxap:ObjectPageSection titleUppercase="false" title="Tributos" ...>`:

```xml
      <uxap:ObjectPageSection titleUppercase="false" title="Totais">
        <uxap:subSections>
          <uxap:ObjectPageSubSection title=" " titleUppercase="false">
            <core:Fragment fragmentName="siagrob1.fragments.InvoiceChargeTotals" type="XML" />
          </uxap:ObjectPageSubSection>
        </uxap:subSections>
      </uxap:ObjectPageSection>
```

`view/salesInvoices/Main.view.xml` — na coluna "Valor Produtos": `label="Valor Produtos"` vira `label="Total geral"` e `path: 'TotalInvoiceItems',` vira `path: 'GrandTotal',` (o restante do template fica igual).

`salesInvoices/BaseController.ts` — em `createColumnConfig`, logo depois do `aCols.push({ label: "Valor Produtos", ... });`:

```ts
      aCols.push({
        label: "Total geral",
        property: "GrandTotal",
        type: EdmType.Number,
        scale: 2,
        delimiter: true
      });
```

`view/purchaseInvoices/Main.view.xml` — logo depois do `</t:Column>` da coluna "Valor declarado":

```xml
					<!-- Total geral (itens + frete + seguro + outras despesas − desconto, spec 2026-10-05 R4): calculado no
					     servidor a partir das linhas que o QueryAll já inclui; sem sortProperty, não é coluna do banco. -->
					<t:Column label="Total geral" width="11rem" hAlign="End">
						<t:template>
							<Text text="{
								path: 'GrandTotal',
								type: 'sap.ui.model.odata.type.Decimal',
								constraints: { precision: 18, scale: 2 },
								formatOptions: {
									decimalSeparator: ',',
									groupingEnabled: true,
									groupingSeparator: '.'
								}
							}" />
						</t:template>
					</t:Column>
```

`purchaseInvoices/Main.controller.ts` — em `createColumnConfig`, logo depois do `aCols.push({ label: "Valor declarado", ... });`:

```ts
    aCols.push({
      label: "Total geral",
      property: "GrandTotal",
      type: EdmType.Number,
      scale: 2,
      delimiter: true,
    });
```

- [ ] **Step 2: Verificar** — `yarn ts-typecheck`; `yarn lint`; `npx ui5lint` (≤ 919; nenhum achado novo em `Items.fragment.xml`, `InvoiceChargeTotals.fragment.xml`, `Detail.view.xml` e `Main.view.xml`); suíte QUnit de unidade inteira verde.

- [ ] **Step 3: Commit**

```bash
git add webapp/fragments/InvoiceChargeTotals.fragment.xml
git commit -m "feat(invoice): colunas de frete, seguro, desconto e outras despesas e seção totais" -- webapp/fragments/InvoiceChargeTotals.fragment.xml webapp/view/salesInvoices/fragments/Items.fragment.xml webapp/view/purchaseInvoices/fragments/Items.fragment.xml webapp/view/salesInvoices/Detail.view.xml webapp/view/purchaseInvoices/Detail.view.xml webapp/view/salesInvoices/Main.view.xml webapp/controller/salesInvoices/BaseController.ts webapp/view/purchaseInvoices/Main.view.xml webapp/controller/purchaseInvoices/Main.controller.ts
```

---

### Task 9: Verificação fim a fim (controlador, não subagente)

- [ ] Conferir as portas 8080/5246/50000/58000/8081: se houver processos que você não subiu (a stack do usuário), pedir ao usuário para derrubá-los antes de subir a deste branch. Nunca matar processo alheio.
- [ ] Aplicar a migration no `CEAGUI_SIAGRO_DEV` (de `siagro-b1-backend/`): `ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`. Conferir (com `-I`, ver memória do índice filtrado): `sqlcmd -S localhost -E -I -d CEAGUI_SIAGRO_DEV -Q "SELECT TABLE_NAME, COLUMN_NAME, NUMERIC_PRECISION, NUMERIC_SCALE, IS_NULLABLE, COLUMN_DEFAULT FROM INFORMATION_SCHEMA.COLUMNS WHERE COLUMN_NAME IN ('FreightValue','InsuranceValue','DiscountValue','OtherExpensesValue') ORDER BY TABLE_NAME, COLUMN_NAME"` → 8 linhas, `18,2`, `NO`, default 0; `SELECT MigrationId FROM __EFMigrationsHistory WHERE MigrationId LIKE '%AddInvoiceLineCharges'` → 1 linha; `SELECT COUNT(*) FROM SALES_INVOICES_ITEMS WHERE FreightValue <> 0 OR DiscountValue <> 0` → 0 (linhas antigas com 0).
- [ ] Subir Gateway (esperar `Loading proxy data from config`), depois Web e Reports com o perfil `ceagui`, e o frontend deste branch (`npx ui5 serve --port 8080`). Login `admin`/`1234`, filial 01 (homologação).
- [ ] Navegador, venda: incluir um Documento de Saída avulso (cliente de teste das emissões anteriores em homologação), uma linha com frete 150,00 e desconto 50,00 (seguro e outras despesas 0). Conferir na grade a coluna "Total" = itens + 100,00 e "Total do Documento" igual; salvar; no Detail, a seção "Totais" (Total dos itens, Frete 150,00, Seguro 0,00, Outras despesas 0,00, (−) Desconto 50,00, Total geral) e o quadro "Tributos" com a base do ICMS = total geral (conferir também `SELECT IcmsBase, FreightValue, DiscountValue FROM SALES_INVOICES_ITEMS WHERE ...`). "Emitir NF-e" → **autorizada** (a SEFAZ confere `vNF`). No XML assinado (`SALES_INVOICE_NFE_XMLS`, Kind Signed): `det/prod` com `<vFrete>` e `<vDesc>`, `ICMSTot/vNF` = total geral, `fat/vLiq` e `detPag/vPag` = total geral. A lista de Documentos de Saída mostra "Total geral" com o mesmo valor.
- [ ] Navegador, devolução parcial: "Devolver" a venda acima com metade da quantidade → a devolução nasce Pendente com frete 75,00 e desconto 25,00 (proporcionais); editar o frete para 70,00 na grade e salvar → mantém 70,00 e a base do ICMS acompanha. "Emitir NF-e" da devolução → autorizada.
- [ ] Navegador, entrada importada: criar uma cópia do XML fictício `C:\Projetos\SiagroB1\nfe-homologacao-testes\fornecedor-ftestepj-nfe-791.xml` como `...-nfe-793.xml` (fora dos repos): `ide/nNF` 793; chave = a do 791 com o número (posições 26–34) `000000793` e o DV recalculado (função abaixo), aplicada em `infNFe/@Id` ("NFe" + chave) e em `protNFe/infProt/chNFe` se houver; no `det nItem="1"`, inserir `<vFrete>150.00</vFrete>` logo antes de `<indTot>`; em `ICMSTot`, `vFrete` = 150.00 e `vNF` = vNF anterior + 150.00. DV (PowerShell, scratchpad):

  ```powershell
  function Get-NfeDv([string]$k43) {
    $sum = 0; $w = 2
    for ($i = 42; $i -ge 0; $i--) { $sum += [int]::Parse([string]$k43[$i]) * $w; $w = if ($w -eq 9) { 2 } else { $w + 1 } }
    $r = $sum % 11; if ($r -lt 2) { 0 } else { 11 - $r }
  }
  ```

  Importar o 793 no Documento de Entrada → a linha 1 do rascunho mostra Frete 150,00 e o "Total geral" da barra inclui o frete; escolher a natureza de Entrada em cada linha → salvar: `PURCHASE_INVOICES_ITEMS.FreightValue` = 150, `IcmsBase` da linha 1 = total geral da linha, `PURCHASE_INVOICES.TotalDocumentValue` = total geral (= `vNF` do XML). Detail: seção "Totais" e lista com "Total geral".
- [ ] Derrubar tudo o que subiu (tasks e PIDs nas portas, conferindo de novo no fim — o file watcher pode ressuscitar a stack), fechar o navegador; ledger e memória atualizados (feature, branch sem merge, migration aplicada só no `CEAGUI_SIAGRO_DEV`).
