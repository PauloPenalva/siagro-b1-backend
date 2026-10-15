# Unidade tributável de exportação e precisão do DANFE — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Itens com CFOP de exportação saem com `uTrib`/`qTrib`/`vUnTrib` na unidade tributável configurada no complemento do produto (ex.: TON), e o DANFE imprime quantidade e valor unitário com a precisão do XML — e as duas unidades quando diferem.

**Architecture:** O cadastro ganha duas colunas em `ITEM_COMPLEMENTS`. A prontidão (`NfeReadinessValidator`) carrega a configuração dos itens no `NfeIssueContext`; o `NfeIssueInputAssembler` aplica a conversão (função pura em `SiagroB1.Fiscal`) só quando o CFOP é de exportação; o `NfeXmlBuilder` usa os campos tributáveis quando existem. O DANFE calcula casas e modo de impressão a partir do `nfeProc` numa classe pura, sem tocar no `.frx` da Zeus.

**Tech Stack:** .NET 10, EF Core (SQL Server / InMemory nos testes), OData (Microsoft.AspNetCore.OData), Zeus.Net.NFe.NFCe, FastReport.OpenSource, xUnit; frontend OpenUI5 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-14-nfe-export-taxable-unit-design.md`

## Global Constraints

- Identificadores em inglês; tudo o que o usuário lê em pt-BR (mensagens, labels).
- Backend: worktree `C:\Projetos\SiagroB1\siagro-b1-backend-export-unit`, branch `feature/nfe-export-taxable-unit`. Frontend: worktree `C:\Projetos\SiagroB1\siagro-b1-frontend-export-unit`, mesmo nome de branch (criado na Task 6). Dois repositórios = commits separados. **Nunca push, nunca merge.**
- Todo arquivo novo: `git add` logo após criar.
- Mensagem de commit: `tipo(escopo): descrição pt-BR` (tipos `feat fix refactor perf chore docs test`; escopos usados aqui: `invoice`, `master-data`, `reports`), corpo com o porquê, trailer `DB: <Migration>` no commit que tiver migration, e por último `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`.
- CFOPs de exportação: qualquer `7xxx`, e `5501`, `6501`, `5502`, `6502`, `5504`, `6504`, `5505`, `6505`.
- `qTrib = round(Quantity ÷ TaxableFactor, 4)`; `vUnTrib = qTrib == 0 ? 0 : round(Total ÷ qTrib, 10)`; `MidpointRounding.AwayFromZero`.
- DANFE: casas de quantidade em `[2, 4]`, de valor unitário em `[2, 10]`; `Ambos` se algum item tem `uTrib ≠ uCom` ou `qTrib ≠ qCom`, senão `Comercial`.
- `TaxableUnitOfMeasureCode` `VARCHAR(6)`, `TaxableFactor` `DECIMAL(18,6)`, ambos anuláveis, andam juntos, fator > 0, unidade em maiúsculas e sem espaços nas pontas.
- Na action `ItemsSetComplement`, os dois parâmetros novos são `Optional()`; ausentes do corpo = inalterados; fator é `double?` no EDM.
- O `.frx` da Zeus e os fontes em `ThirdParty/Zeus-LGPL` não são alterados.
- Testes: `dotnet test SiagroB1.Application.Tests` e `dotnet test SiagroB1.Fiscal.Tests` (filtro `--filter "FullyQualifiedName~Classe"`). O worktree já tem `SiagroB1.Reports/wwwroot` copiado (sem ele, 13 testes de relatório falham com "Logo fixture missing" — não é regressão).

## Review Focus

1. **O diálogo de faturamento grava o complemento sem os campos novos** — a action chega sem `TaxableUnitOfMeasureCode`/`TaxableFactor` e a configuração de exportação deve continuar intacta (teste na Task 5).
2. **XML autorizado antes desta mudança, com zeros à direita** (`qCom` "30000.0000", `vUnCom` "2.0000000000") — o DANFE deve continuar com 2 casas, não 4/10 (teste na Task 4).
3. **Quantidade minúscula que arredonda `qTrib` para 0** (ou nota de complemento com quantidade 0) — `vUnTrib` 0, sem divisão por zero (teste na Task 1).
4. **CFOP gravado com espaços ou nulo/curto** — não é exportação e não estoura (teste na Task 1).
5. **Unidade digitada em minúsculas ou com espaços** (" ton ") — grava "TON"; e o XML deve passar no XSD com `qTrib` de 4 casas e `vUnTrib` de 10 (testes nas Tasks 2 e 5).

---

### Task 1: Regras fiscais puras — CFOP de exportação e conversão

**Files:**
- Create: `SiagroB1.Fiscal/Nfe/ExportCfops.cs`
- Create: `SiagroB1.Fiscal/Nfe/NfeTaxableUnit.cs`
- Test: `SiagroB1.Fiscal.Tests/Nfe/ExportCfopsTests.cs`
- Test: `SiagroB1.Fiscal.Tests/Nfe/NfeTaxableUnitTests.cs`

**Interfaces:**
- Produces: `ExportCfops.Contains(string? cfop): bool`; `record NfeTaxableUnitSetting(string UnitOfMeasure, decimal Factor)`; `NfeTaxableUnit.Convert(decimal quantity, decimal total, decimal factor): (decimal Quantity, decimal UnitPrice)`.

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Fiscal.Tests/Nfe/ExportCfopsTests.cs`:

```csharp
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Fiscal.Tests.Nfe;

/// <summary>CFOPs em que o grupo tributável sai na unidade de exportação do produto.</summary>
public class ExportCfopsTests
{
    [Theory]
    [InlineData("7101")]
    [InlineData("7102")]
    [InlineData("7949")]
    [InlineData("5501")]
    [InlineData("6501")]
    [InlineData("5502")]
    [InlineData("6502")]
    [InlineData("5504")]
    [InlineData("6504")]
    [InlineData("5505")]
    [InlineData("6505")]
    [InlineData(" 5504 ")]
    public void Export_and_specific_purpose_cfops_are_export(string cfop) =>
        Assert.True(ExportCfops.Contains(cfop));

    [Theory]
    [InlineData("5102")]
    [InlineData("6102")]
    [InlineData("5905")]
    [InlineData("5503")]
    [InlineData("1501")]
    [InlineData("7")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_cfops_are_not_export(string? cfop) =>
        Assert.False(ExportCfops.Contains(cfop));
}
```

`SiagroB1.Fiscal.Tests/Nfe/NfeTaxableUnitTests.cs`:

```csharp
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Fiscal.Tests.Nfe;

/// <summary>KG da linha → unidade tributável: qTrib com 4 casas, vUnTrib com 10 (limites do leiaute).</summary>
public class NfeTaxableUnitTests
{
    [Fact]
    public void Exact_tons() =>
        Assert.Equal((30m, 2000m), NfeTaxableUnit.Convert(30000m, 60000m, 1000m));

    [Fact]
    public void Quantity_keeps_four_decimals() =>
        Assert.Equal(30.0005m, NfeTaxableUnit.Convert(30000.5m, 60001m, 1000m).Quantity);

    [Fact]
    public void Quantity_is_rounded_to_four_decimals() =>
        Assert.Equal(1.2346m, NfeTaxableUnit.Convert(1234.56789m, 2469.14m, 1000m).Quantity);

    [Fact]
    public void Unit_price_is_the_total_over_the_rounded_quantity_with_ten_decimals()
    {
        var (quantity, unitPrice) = NfeTaxableUnit.Convert(33333m, 100000m, 1000m);

        Assert.Equal(33.333m, quantity);
        Assert.Equal(3000.0300003000m, unitPrice);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.00004)]
    public void Quantity_that_rounds_to_zero_has_zero_unit_price(double quantity) =>
        Assert.Equal((0m, 0m), NfeTaxableUnit.Convert((decimal)quantity, 150m, 1000m));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~ExportCfopsTests|FullyQualifiedName~NfeTaxableUnitTests"`
Expected: build FAIL — `ExportCfops` / `NfeTaxableUnit` não existem.

- [ ] **Step 3: Implement**

`SiagroB1.Fiscal/Nfe/ExportCfops.cs`:

```csharp
namespace SiagroB1.Fiscal.Nfe;

/// <summary>
/// CFOPs de exportação direta (7xxx) e de remessa com fim específico de exportação / formação de lote
/// (5501/6501, 5502/6502, 5504/6504, 5505/6505). Neles o grupo tributável do item sai na unidade de
/// exportação configurada no complemento do produto (spec 2026-10-14 §3). Regra fiscal: fica no código.
/// </summary>
public static class ExportCfops
{
    private static readonly HashSet<string> SpecificPurpose =
        ["5501", "6501", "5502", "6502", "5504", "6504", "5505", "6505"];

    public static bool Contains(string? cfop)
    {
        var code = cfop?.Trim();
        if (code is not { Length: 4 })
            return false;

        return code[0] == '7' || SpecificPurpose.Contains(code);
    }
}
```

`SiagroB1.Fiscal/Nfe/NfeTaxableUnit.cs`:

```csharp
namespace SiagroB1.Fiscal.Nfe;

/// <summary>Unidade tributável de exportação do produto: <c>uTrib</c> e quantas unidades da linha (KG) cabem nela.</summary>
public sealed record NfeTaxableUnitSetting(string UnitOfMeasure, decimal Factor);

/// <summary>Conversão da linha (KG) para o grupo tributável (spec 2026-10-14 §4).</summary>
public static class NfeTaxableUnit
{
    /// <summary>
    /// <c>qTrib</c> = quantidade ÷ fator com 4 casas (<c>TDec_1104v</c>); <c>vUnTrib</c> = <c>vProd</c> ÷ <c>qTrib</c> com
    /// 10 casas (<c>TDec_1110v</c>). <c>qTrib</c> zero (nota de complemento, quantidade ínfima) dá <c>vUnTrib</c> zero.
    /// </summary>
    public static (decimal Quantity, decimal UnitPrice) Convert(decimal quantity, decimal total, decimal factor)
    {
        var taxableQuantity = Math.Round(quantity / factor, 4, MidpointRounding.AwayFromZero);
        var unitPrice = taxableQuantity == 0m ? 0m : Math.Round(total / taxableQuantity, 10, MidpointRounding.AwayFromZero);

        return (taxableQuantity, unitPrice);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~ExportCfopsTests|FullyQualifiedName~NfeTaxableUnitTests"`
Expected: PASS (todos).

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Fiscal/Nfe/ExportCfops.cs SiagroB1.Fiscal/Nfe/NfeTaxableUnit.cs SiagroB1.Fiscal.Tests/Nfe/ExportCfopsTests.cs SiagroB1.Fiscal.Tests/Nfe/NfeTaxableUnitTests.cs
git commit -m "feat(invoice): regras de CFOP de exportação e conversão para unidade tributável

Base para o grupo tributável da NF-e sair na unidade de exportação do
produto (TON para a soja): a lista de CFOPs é regra fiscal e fica no código;
a conversão respeita as 4 casas de qTrib e as 10 de vUnTrib do leiaute.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: `NfeItem` e `NfeXmlBuilder` com grupo tributável próprio

**Files:**
- Modify: `SiagroB1.Fiscal/Nfe/NfeIssueInput.cs` (record `NfeItem`, ~linha 93-103)
- Modify: `SiagroB1.Fiscal/Nfe/NfeXmlBuilder.cs:264-266`
- Test: `SiagroB1.Fiscal.Tests/Nfe/NfeXmlBuilderTests.cs` (acrescentar ao final da classe)
- Test: `SiagroB1.Fiscal.Tests/Nfe/NfeSignerTests.cs` (acrescentar um teste de XSD)

**Interfaces:**
- Consumes: nada da Task 1 diretamente.
- Produces: `NfeItem.TaxableUnitOfMeasure: string?`, `NfeItem.TaxableQuantity: decimal?`, `NfeItem.TaxableUnitPrice: decimal?` (nulos = iguais aos comerciais).

- [ ] **Step 1: Write the failing tests**

Ao final de `NfeXmlBuilderTests`:

```csharp
    [Fact]
    public void Taxable_group_copies_the_commercial_one_by_default()
    {
        var prod = Build(NfeTestData.Input()).infNFe.det[0].prod;

        Assert.Equal("KG", prod.uTrib);
        Assert.Equal(30000m, prod.qTrib);
        Assert.Equal(2m, prod.vUnTrib);
    }

    [Fact]
    public void Export_item_sends_its_own_taxable_group()
    {
        var item = NfeTestData.Item() with
        {
            Cfop = "5504", TaxableUnitOfMeasure = "TON", TaxableQuantity = 30m, TaxableUnitPrice = 2000m,
        };

        var prod = Build(NfeTestData.Input() with { Items = [item] }).infNFe.det[0].prod;

        Assert.Equal("TON", prod.uTrib);
        Assert.Equal(30m, prod.qTrib);
        Assert.Equal(2000m, prod.vUnTrib);
        // O grupo comercial não muda: a validação 629 (qCom × vUnCom = vProd) continua sobre ele.
        Assert.Equal("KG", prod.uCom);
        Assert.Equal(30000m, prod.qCom);
        Assert.Equal(2m, prod.vUnCom);
        Assert.Equal(60000m, prod.vProd);
    }
```

Em `NfeSignerTests`, abra o arquivo e copie o padrão do teste existente que chama `NfeSigner.BuildSignAndValidate` (certificado de `TestCertificates.CreatePfx()`, `NfeServiceSettings(NfeEnvironment.Homologation, "SP", certificate, NfeServiceSettings.DefaultSchemasDirectory)`). Acrescente:

```csharp
    [Fact]
    public void Export_item_with_four_decimal_qtrib_and_ten_decimal_vuntrib_passes_the_schema()
    {
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);
        var settings = new NfeServiceSettings(NfeEnvironment.Homologation, "SP", certificate, NfeServiceSettings.DefaultSchemasDirectory);
        var item = NfeTestData.Item() with
        {
            Cfop = "5504", Quantity = 33333m, Total = 66666m,
            TaxableUnitOfMeasure = "TON", TaxableQuantity = 33.333m, TaxableUnitPrice = 2000.0000000000m,
        };

        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.Input() with { Items = [item] }, settings);

        Assert.Contains("<uTrib>TON</uTrib>", signed.Xml);
        Assert.Contains("<qTrib>33.3330</qTrib>", signed.Xml);
    }
```

(Se `Quantity = 33333m` com `UnitPrice = 2m` e `Total = 66666m` quebrar algum total do `NfeTestData.Input()` — pagamento de 60000 — ajuste também `Payment` para o total novo, copiando a chamada `PaymentInstallmentCalculator.Calculate(...)` do `NfeTestData` com `66666m`. Se a asserção de `qTrib` falhar só pela formatação — ex.: Zeus serializa `33.333` — troque pelo texto que o XML de fato contém, desde que o XSD passe; o que este teste trava é a validação do schema.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~NfeXmlBuilderTests|FullyQualifiedName~NfeSignerTests"`
Expected: build FAIL — `TaxableUnitOfMeasure` não existe em `NfeItem`.

- [ ] **Step 3: Implement**

Em `NfeIssueInput.cs`, no record `NfeItem`, logo depois de `public required decimal Total { get; init; }`:

```csharp

    /// <summary>
    /// Grupo tributável (<c>uTrib</c>/<c>qTrib</c>/<c>vUnTrib</c>) quando difere do comercial: item com CFOP de exportação e
    /// unidade de exportação configurada no complemento do produto (spec 2026-10-14 §4). Nulos = iguais aos comerciais.
    /// </summary>
    public string? TaxableUnitOfMeasure { get; init; }
    public decimal? TaxableQuantity { get; init; }
    public decimal? TaxableUnitPrice { get; init; }
```

Em `NfeXmlBuilder.BuildItem`, troque:

```csharp
            uTrib = Truncate(item.UnitOfMeasure, 6),
            qTrib = item.Quantity,
            vUnTrib = item.UnitPrice,
```

por:

```csharp
            uTrib = Truncate(item.TaxableUnitOfMeasure ?? item.UnitOfMeasure, 6),
            qTrib = item.TaxableQuantity ?? item.Quantity,
            vUnTrib = item.TaxableUnitPrice ?? item.UnitPrice,
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SiagroB1.Fiscal.Tests`
Expected: PASS (suíte inteira — confirma que nenhuma nota existente mudou).

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Fiscal/Nfe/NfeIssueInput.cs SiagroB1.Fiscal/Nfe/NfeXmlBuilder.cs SiagroB1.Fiscal.Tests/Nfe/NfeXmlBuilderTests.cs SiagroB1.Fiscal.Tests/Nfe/NfeSignerTests.cs
git commit -m "feat(invoice): grupo tributável próprio no item da NF-e

O XML copiava uCom/qCom/vUnCom para o grupo tributável. Na exportação o
item precisa de uTrib/qTrib/vUnTrib na unidade de exportação; sem os campos
novos, a nota continua idêntica à de antes.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Cadastro + montagem — colunas no complemento, contexto e assembler

**Files:**
- Modify: `SiagroB1.Domain/Entities/ItemComplement.cs`
- Create (gerado): migration `AddItemComplementTaxableUnit` em `SiagroB1.Migrations/AppContext/`
- Modify: `SiagroB1.Application/Services/Nfe/NfeIssueContext.cs`
- Modify: `SiagroB1.Application/Services/Nfe/NfeReadinessValidator.cs:209-220`
- Modify: `SiagroB1.Application/Services/Nfe/NfeIssueInputAssembler.cs:171-180`
- Test: `SiagroB1.Application.Tests/Nfe/NfeIssueInputAssemblerTests.cs`

**Interfaces:**
- Consumes: `ExportCfops.Contains`, `NfeTaxableUnitSetting`, `NfeTaxableUnit.Convert` (Task 1); `NfeItem.Taxable*` (Task 2).
- Produces: `ItemComplement.TaxableUnitOfMeasureCode: string?`, `ItemComplement.TaxableFactor: decimal?`; `NfeIssueContext.TaxableUnits: IReadOnlyDictionary<string, NfeTaxableUnitSetting>?` (último parâmetro, default `null`).

- [ ] **Step 1: Write the failing tests**

Ao final de `NfeIssueInputAssemblerTests` (o seed tem um item `SOJA`, KG, `Quantity = 30000`, `UnitPrice = 2`, CFOP `6102`):

```csharp
    private static async Task<NfeScenario> SeedWithTaxableUnitAsync(string? unit = "TON", decimal? factor = 1000m)
    {
        var scenario = await NfeTestSeed.SeedAsync();
        scenario.Db.Context.ItemComplements.Add(new ItemComplement
        {
            ItemCode = "SOJA", TaxableUnitOfMeasureCode = unit, TaxableFactor = factor,
        });
        await scenario.Db.SaveChangesAsync();
        return scenario;
    }

    [Theory]
    [InlineData("5504")]
    [InlineData("6501")]
    [InlineData("7101")]
    public async Task Export_line_goes_out_in_the_product_taxable_unit(string cfop)
    {
        var input = await BuildAsync(await SeedWithTaxableUnitAsync(), i => i.Items.Single().Cfop = cfop);

        var item = input.Items[0];
        Assert.Equal("TON", item.TaxableUnitOfMeasure);
        Assert.Equal(30m, item.TaxableQuantity);
        Assert.Equal(2000m, item.TaxableUnitPrice);
        Assert.Equal("KG", item.UnitOfMeasure);
        Assert.Equal(30000m, item.Quantity);
    }

    [Fact]
    public async Task Domestic_line_keeps_the_commercial_unit_even_with_the_product_configured()
    {
        var input = await BuildAsync(await SeedWithTaxableUnitAsync());

        Assert.Null(input.Items[0].TaxableUnitOfMeasure);
        Assert.Null(input.Items[0].TaxableQuantity);
        Assert.Null(input.Items[0].TaxableUnitPrice);
    }

    [Fact]
    public async Task Export_line_without_configuration_keeps_the_commercial_unit()
    {
        var input = await BuildAsync(await NfeTestSeed.SeedAsync(), i => i.Items.Single().Cfop = "5504");

        Assert.Null(input.Items[0].TaxableUnitOfMeasure);
    }

    [Fact]
    public async Task Complement_with_only_the_commercial_unit_is_not_a_taxable_configuration()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        scenario.Db.Context.ItemComplements.Add(new ItemComplement
        {
            ItemCode = "SOJA", CommercialUnitOfMeasureCode = "SC", CommercialFactor = 60m,
        });
        await scenario.Db.SaveChangesAsync();

        var input = await BuildAsync(scenario, i => i.Items.Single().Cfop = "5504");

        Assert.Null(input.Items[0].TaxableUnitOfMeasure);
    }
```

(Se o seed tiver mais de um item ou `Items` não for `ICollection` com `Single()`, use o mesmo acesso que os testes vizinhos usam para mudar a linha — ex.: `i.Items.First()`. Se a prontidão recusar o CFOP trocado — ex.: conferência de CFOP × UF do destinatário, o cliente do seed é de outra UF —, use nos testes os CFOPs compatíveis com o seed (`6501`, `6504`, `7101` conforme o que ela aceitar) e registre no relatório da task qual regra recusou; não afrouxe a prontidão.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeIssueInputAssemblerTests"`
Expected: build FAIL — `TaxableUnitOfMeasureCode` não existe em `ItemComplement`.

- [ ] **Step 3: Entity + migration**

Em `ItemComplement.cs`, depois de `CommercialFactor`:

```csharp

    /// <summary>
    /// <c>uTrib</c> da NF-e nos CFOPs de exportação (ex.: "TON" para a soja). Nula junto com
    /// <see cref="TaxableFactor"/> = o grupo tributável sai igual ao comercial. Independe de
    /// <see cref="CommercialUnitOfMeasureCode"/>, que é só de exibição.
    /// </summary>
    [Column(TypeName = "VARCHAR(6)")]
    public string? TaxableUnitOfMeasureCode { get; set; }

    /// <summary>Unidades da linha da NF-e (KG) por unidade tributável (ex.: 1000 para TON). Nula junto com a unidade.</summary>
    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal? TaxableFactor { get; set; }
```

Gere a migration (na raiz do worktree):

Run: `dotnet ef migrations add AddItemComplementTaxableUnit --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web -o AppContext`
Expected: dois arquivos novos + snapshot alterado. Abra o `.cs` gerado e confirme que o `Up` só faz dois `AddColumn` em `ITEM_COMPLEMENTS` (`VARCHAR(6)` e `DECIMAL(18,6)`, `nullable: true`) e o `Down` os dois `DropColumn`. Qualquer outra operação = drift do snapshot: pare e reporte. `git add` dos dois arquivos novos.

- [ ] **Step 4: Context, readiness and assembler**

`NfeIssueContext.cs`: acrescente `using SiagroB1.Fiscal.Nfe;` e, como **último** parâmetro do record (depois de `string? CustodyReferencedKey = null`):

```csharp
    string? CustodyReferencedKey = null,
    IReadOnlyDictionary<string, NfeTaxableUnitSetting>? TaxableUnits = null);
```

e no `<summary>`/params acrescente:

```csharp
/// <param name="TaxableUnits">Unidade tributável de exportação por ItemCode (complemento do produto); só os itens configurados.</param>
```

`NfeReadinessValidator.cs`, logo depois do bloco `itemCests` (linha ~214):

```csharp
        var taxableUnits = await db.Context.ItemComplements.AsNoTracking()
            .Where(c => itemCodes.Contains(c.ItemCode) && c.TaxableUnitOfMeasureCode != null && c.TaxableFactor != null)
            .ToDictionaryAsync(c => c.ItemCode, c => new NfeTaxableUnitSetting(c.TaxableUnitOfMeasureCode!, c.TaxableFactor!.Value));
```

e no `return new NfeIssueContext(...)` passe-o nomeado no final:

```csharp
            plate, truckState, condition, usages, itemCests, returnOrigin, TaxableUnits: taxableUnits);
```

(acrescente `using SiagroB1.Fiscal.Nfe;` se ainda não houver).

`NfeIssueInputAssembler.cs`: envolva o item montado. Troque

```csharp
            Items = items.Select((item, index) => ToItem(item, item.NfeItemNumber ?? index + 1) with
            {
```

por

```csharp
            Items = items.Select((item, index) => WithTaxableUnit(ToItem(item, item.NfeItemNumber ?? index + 1) with
            {
```

e feche o parêntese extra no fim do inicializador: troque o `}).ToList(),` que encerra esse `Select` (logo depois da linha `: new NfeItemReference(returnOrigin.AccessKey, ...)`) por `}, context.TaxableUnits)).ToList(),`. Acrescente o método junto aos outros `private static`:

```csharp
    /// <summary>
    /// CFOP de exportação + produto com unidade tributável configurada → grupo tributável convertido
    /// (spec 2026-10-14 §4); senão o item sai como está (tributável = comercial).
    /// </summary>
    private static NfeItem WithTaxableUnit(NfeItem item, IReadOnlyDictionary<string, NfeTaxableUnitSetting>? taxableUnits)
    {
        if (!ExportCfops.Contains(item.Cfop) || taxableUnits?.GetValueOrDefault(item.ItemCode) is not { } setting)
            return item;

        var (quantity, unitPrice) = NfeTaxableUnit.Convert(item.Quantity, item.Total, setting.Factor);
        return item with { TaxableUnitOfMeasure = setting.UnitOfMeasure, TaxableQuantity = quantity, TaxableUnitPrice = unitPrice };
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Nfe"`
Expected: PASS (inclusive os testes de NF-e existentes — `StorageCustodyNfeTests` usa `context with {...}` e não é afetado pelo parâmetro com default).

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Domain/Entities/ItemComplement.cs SiagroB1.Migrations/AppContext SiagroB1.Application/Services/Nfe/NfeIssueContext.cs SiagroB1.Application/Services/Nfe/NfeReadinessValidator.cs SiagroB1.Application/Services/Nfe/NfeIssueInputAssembler.cs SiagroB1.Application.Tests/Nfe/NfeIssueInputAssemblerTests.cs
git commit -m "feat(invoice): unidade tributável de exportação do produto na NF-e

Soja (NCM 12019000) e outros produtos vendidos para exportação direta,
fim específico ou formação de lote devem informar qTrib/vUnTrib em TON.
A unidade e o fator ficam no complemento do produto (configuração do
usuário); só valem quando o CFOP do item é de exportação.

DB: AddItemComplementTaxableUnit

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: DANFE — precisão do XML e as duas unidades

**Files:**
- Create: `SiagroB1.Reports/Services/DanfeItemPrecision.cs`
- Modify: `SiagroB1.Reports/Services/DanfeReportService.cs` (método `Render`)
- Test: `SiagroB1.Application.Tests/Reports/DanfeItemPrecisionTests.cs`
- Test: `SiagroB1.Application.Tests/Reports/DanfeReportServiceTests.cs` (um teste novo)

**Interfaces:**
- Consumes: `NfeItem.Taxable*` (Task 2), para o teste de renderização.
- Produces: `DanfeItemPrecision.From(IEnumerable<prod> products): DanfeItemPrecision` com `int QuantityDecimals`, `int UnitPriceDecimals`, `bool BothUnits`.

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/Reports/DanfeItemPrecisionTests.cs`:

```csharp
using NFe.Classes.Informacoes.Detalhe;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>Casas e modo de unidade do DANFE tirados do XML autorizado (spec 2026-10-14 §1).</summary>
public class DanfeItemPrecisionTests
{
    private static prod Prod(decimal qCom, decimal vUnCom, string uTrib = "KG", decimal? qTrib = null, decimal? vUnTrib = null) => new()
    {
        uCom = "KG", qCom = qCom, vUnCom = vUnCom,
        uTrib = uTrib, qTrib = qTrib ?? qCom, vUnTrib = vUnTrib ?? vUnCom,
    };

    [Fact]
    public void Whole_values_keep_the_minimum_of_two_decimals()
    {
        var precision = DanfeItemPrecision.From([Prod(30000m, 2m)]);

        Assert.Equal(2, precision.QuantityDecimals);
        Assert.Equal(2, precision.UnitPriceDecimals);
        Assert.False(precision.BothUnits);
    }

    /// <summary>XML já autorizado volta do parse com a escala do texto ("30000.0000"): zeros à direita não contam.</summary>
    [Fact]
    public void Trailing_zeros_from_the_xml_do_not_count()
    {
        var precision = DanfeItemPrecision.From([Prod(30000.0000m, 2.0000000000m)]);

        Assert.Equal(2, precision.QuantityDecimals);
        Assert.Equal(2, precision.UnitPriceDecimals);
    }

    [Fact]
    public void Largest_precision_among_all_items_and_both_groups_wins()
    {
        var precision = DanfeItemPrecision.From(
        [
            Prod(100m, 1.5m),
            Prod(30000.5m, 2m, "TON", 30.0005m, 3000.0300003m),
        ]);

        Assert.Equal(4, precision.QuantityDecimals);
        Assert.Equal(7, precision.UnitPriceDecimals);
        Assert.True(precision.BothUnits);
    }

    [Fact]
    public void Precision_is_capped_by_the_layout()
    {
        var precision = DanfeItemPrecision.From([Prod(1.123456m, 1.123456789012m)]);

        Assert.Equal(4, precision.QuantityDecimals);
        Assert.Equal(10, precision.UnitPriceDecimals);
    }

    [Fact]
    public void Same_unit_with_a_different_quantity_still_prints_both()
    {
        Assert.True(DanfeItemPrecision.From([Prod(30000m, 2m, "KG", 30m, 2000m)]).BothUnits);
    }
}
```

Em `DanfeReportServiceTests`, um teste que exercita o script do `.frx` no modo `Ambos` (copie a montagem do teste `Authorized_nfe_renders_a_pdf`, trocando só a entrada):

```csharp
    [Fact]
    public async Task Export_nfe_with_taxable_unit_renders_both_units()
    {
        var db = TestDb.CreateUnitOfWork();
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);
        var settings = new NfeServiceSettings(NfeEnvironment.Homologation, "SP", certificate, NfeServiceSettings.DefaultSchemasDirectory);
        var item = NfeTestData.Item() with
        {
            Cfop = "5504", TaxableUnitOfMeasure = "TON", TaxableQuantity = 30m, TaxableUnitPrice = 2000m,
        };
        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.Input() with { Items = [item] }, settings);
        var invoiceKey = Guid.NewGuid();
        db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoiceKey, Kind = NfeXmlKind.Authorized, CreatedAt = DateTime.Now,
            Xml = NfeProcComposer.Compose(signed.Xml, FakeNfeSefazClient.Authorized(signed.AccessKey).ProtocolXml!),
        });
        await db.SaveChangesAsync();

        var contentRoot = Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CompanyLogoPath"] = "wwwroot/images/logo.png" })
            .Build();
        var environment = new TestWebHostEnvironment(contentRoot);
        var header = new ReportHeaderService(environment, configuration, NullLogger<ReportHeaderService>.Instance);

        var (pdf, _) = await new DanfeReportService(db, environment, header).GeneratePdfAsync(invoiceKey);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~DanfeItemPrecisionTests"`
Expected: build FAIL — `DanfeItemPrecision` não existe.

- [ ] **Step 3: Implement**

`SiagroB1.Reports/Services/DanfeItemPrecision.cs`:

```csharp
using NFe.Classes.Informacoes.Detalhe;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Casas de quantidade e valor unitário do DANFE e se ele imprime as duas unidades, tiradas dos
/// <c>det/prod</c> do XML autorizado (spec 2026-10-14 §1). O layout da Zeus só aceita uma configuração
/// por nota: vale a maior precisão entre todos os itens, nos grupos comercial e tributável.
/// </summary>
public sealed record DanfeItemPrecision(int QuantityDecimals, int UnitPriceDecimals, bool BothUnits)
{
    private const int MinDecimals = 2;
    private const int MaxQuantityDecimals = 4;   // TDec_1104v
    private const int MaxUnitPriceDecimals = 10; // TDec_1110v

    public static DanfeItemPrecision From(IEnumerable<prod> products)
    {
        var list = products.ToList();

        return new DanfeItemPrecision(
            Decimals(list.SelectMany(p => new[] { p.qCom, p.qTrib }), MaxQuantityDecimals),
            Decimals(list.SelectMany(p => new[] { p.vUnCom, p.vUnTrib }), MaxUnitPriceDecimals),
            list.Any(p => p.uTrib != p.uCom || p.qTrib != p.qCom));
    }

    private static int Decimals(IEnumerable<decimal> values, int max) =>
        Math.Clamp(values.Select(SignificantDecimals).DefaultIfEmpty(0).Max(), MinDecimals, max);

    /// <summary>Casas sem os zeros à direita: o parse do XML preserva a escala do texto ("2.0000000000").</summary>
    private static int SignificantDecimals(decimal value)
    {
        var normalized = value / 1.0000000000000000000000000000m;
        return (decimal.GetBits(normalized)[3] >> 16) & 0xFF;
    }
}
```

(Se `prod.qTrib`/`vUnTrib` forem `decimal?` na versão da Zeus em uso, troque por `p.qTrib ?? p.qCom` e `p.vUnTrib ?? p.vUnCom` — confira pelo erro de compilação.)

Em `DanfeReportService.Render`, substitua a criação do `DanfeFrNfe` para usar a configuração calculada:

```csharp
        // Precisão e unidades vêm do próprio XML (spec 2026-10-14 §1): o padrão da Zeus (2 casas) arredonda o que foi autorizado.
        var precision = DanfeItemPrecision.From(proc.NFe.infNFe.det.Select(d => d.prod));
        var configuration = new ConfiguracaoDanfeNfe(DanfeLogo.FitToBox(header.LogoBytes()), documentoCancelado: cancelled)
        {
            DecimaisQuantidadeItem = precision.QuantityDecimals,
            DecimaisValorUnitario = precision.UnitPriceDecimals,
            ImprimirUnidQtdeValor = precision.BothUnits ? ImprimirUnidQtdeValor.Ambos : ImprimirUnidQtdeValor.Comercial,
        };

        // NF-e cancelada: a Zeus estampa "DOCUMENTO CANCELADO" no layout.
        var danfe = new DanfeFrNfe(proc, configuration,
            desenvolvedor: "IDX Consultoria e Sistemas", arquivoRelatorio: template);
```

(o enum `ImprimirUnidQtdeValor` está em `ConfiguracaoDanfeNfe.cs`, mesmo namespace de `ConfiguracaoDanfeNfe` — já importado por `using NFe.Danfe.Base.NFe;`). Atualize o `<summary>` da classe acrescentando: "Casas decimais e unidades impressas saem do XML (<see cref=\"DanfeItemPrecision\"/>)."

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Danfe"`
Expected: PASS (incluindo os testes de DANFE existentes).

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Reports/Services/DanfeItemPrecision.cs SiagroB1.Reports/Services/DanfeReportService.cs SiagroB1.Application.Tests/Reports/DanfeItemPrecisionTests.cs SiagroB1.Application.Tests/Reports/DanfeReportServiceTests.cs
git commit -m "feat(reports): DANFE com a precisão do XML e as duas unidades

O DANFE usava o padrão da Zeus (2 casas) e divergia do XML autorizado. As
casas de quantidade (até 4) e de valor unitário (até 10) passam a sair do
próprio XML, e o item com unidade tributável diferente (TON na exportação)
imprime comercial e tributável. O layout da Zeus não muda.

Atenção: a configuração é por nota; um item com 4 casas leva todos os
itens a 4, e no modo das duas unidades todos os itens saem em duas linhas.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Gravação do complemento — serviço, DTO, action e EDM

**Files:**
- Modify: `SiagroB1.Domain/Dtos/ItemComplementDto.cs`
- Modify: `SiagroB1.Domain/Interfaces/IItemComplementService.cs`
- Modify: `SiagroB1.Application/Services/ItemComplementService.cs`
- Modify: `SiagroB1.Web/Actions/Items/ItemsSetComplementController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs:505-513`
- Test: `SiagroB1.Application.Tests/Items/ItemComplementServiceTests.cs`
- Test: `SiagroB1.Application.Tests/Items/ItemsComplementEdmModelTests.cs`

**Interfaces:**
- Consumes: `ItemComplement.TaxableUnitOfMeasureCode`/`TaxableFactor` (Task 3).
- Produces: `record ItemTaxableUnitInput(string? UnitOfMeasureCode, decimal? Factor)` (em `SiagroB1.Domain/Dtos/ItemComplementDto.cs`); `SetAsync(string itemCode, string? commercialUnitOfMeasureCode, decimal? commercialFactor, bool? controlsRoyalties = null, ItemTaxableUnitInput? taxableUnit = null)` — `taxableUnit` nulo = inalterado; DTO com `TaxableUnitOfMeasureCode`/`TaxableFactor`; action com parâmetros `TaxableUnitOfMeasureCode` (string, opcional) e `TaxableFactor` (`double?`, opcional).

- [ ] **Step 1: Write the failing tests**

Ao final de `ItemComplementServiceTests`:

```csharp
    [Fact]
    public async Task SetAsync_StoresTheTaxableUnitNormalized()
    {
        var result = await Service().SetAsync("SOJA", null, null, taxableUnit: new ItemTaxableUnitInput(" ton ", 1000m));

        Assert.Equal("TON", result.TaxableUnitOfMeasureCode);
        Assert.Equal(1000m, result.TaxableFactor);
        Assert.Equal("TON", (await Service().GetAsync("SOJA"))!.TaxableUnitOfMeasureCode);
    }

    /// <summary>O diálogo de faturamento grava o complemento sem conhecer a unidade tributável: ela não pode sumir.</summary>
    [Fact]
    public async Task SetAsync_WithoutTheTaxableUnitKeepsTheStoredOne()
    {
        await Service().SetAsync("SOJA", null, null, taxableUnit: new ItemTaxableUnitInput("TON", 1000m));

        var result = await Service().SetAsync("SOJA", "SC", 60m);

        Assert.Equal("TON", result.TaxableUnitOfMeasureCode);
        Assert.Equal(1000m, result.TaxableFactor);
    }

    [Fact]
    public async Task SetAsync_ClearsTheTaxableUnitWithEmptyValues()
    {
        await Service().SetAsync("SOJA", null, null, taxableUnit: new ItemTaxableUnitInput("TON", 1000m));

        var result = await Service().SetAsync("SOJA", null, null, taxableUnit: new ItemTaxableUnitInput("  ", null));

        Assert.Null(result.TaxableUnitOfMeasureCode);
        Assert.Null(result.TaxableFactor);
    }

    [Theory]
    [InlineData("TON", null)]
    [InlineData(null, 1000)]
    public async Task SetAsync_RejectsHalfOfTheTaxableUnit(string? unit, double? factor)
    {
        var error = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SetAsync("SOJA", null, null, taxableUnit: new ItemTaxableUnitInput(unit, (decimal?)factor)));

        Assert.Equal("Informe a unidade tributável e o fator juntos.", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SetAsync_RejectsANonPositiveTaxableFactor(double factor)
    {
        var error = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SetAsync("SOJA", null, null, taxableUnit: new ItemTaxableUnitInput("TON", (decimal)factor)));

        Assert.Equal("O fator da unidade tributável deve ser maior que zero.", error.Message);
    }

    [Fact]
    public async Task SetAsync_RejectsATaxableUnitLongerThanTheXmlField()
    {
        var error = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SetAsync("SOJA", null, null, taxableUnit: new ItemTaxableUnitInput("TONELADA", 1000m)));

        Assert.Equal("A unidade tributável deve ter no máximo 6 caracteres.", error.Message);
    }
```

(acrescente `using SiagroB1.Domain.Dtos;` e `using SiagroB1.Domain.Exceptions;` no topo, se faltarem.)

Ao final de `ItemsComplementEdmModelTests`:

```csharp
    [Fact]
    public void Taxable_unit_parameters_are_optional_and_the_factor_is_a_double()
    {
        var action = Model()
            .SchemaElements
            .OfType<IEdmAction>()
            .Single(a => a.Name == "ItemsSetComplement");

        var unit = action.Parameters.Single(p => p.Name == "TaxableUnitOfMeasureCode");
        var factor = action.Parameters.Single(p => p.Name == "TaxableFactor");

        Assert.IsAssignableFrom<IEdmOptionalParameter>(unit);
        Assert.IsAssignableFrom<IEdmOptionalParameter>(factor);
        Assert.Equal("Edm.Double", factor.Type.FullName());
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ItemComplementServiceTests|FullyQualifiedName~ItemsComplementEdmModelTests"`
Expected: build FAIL — `ItemTaxableUnitInput` não existe.

- [ ] **Step 3: Implement**

`ItemComplementDto.cs` — dentro da classe, depois de `CommercialFactor`:

```csharp

    [JsonPropertyName("TaxableUnitOfMeasureCode")]
    public string? TaxableUnitOfMeasureCode { get; set; }

    [JsonPropertyName("TaxableFactor")]
    public decimal? TaxableFactor { get; set; }
```

e, depois da classe, no mesmo arquivo:

```csharp

/// <summary>Unidade tributável de exportação a gravar; os dois vazios limpam a configuração.</summary>
public sealed record ItemTaxableUnitInput(string? UnitOfMeasureCode, decimal? Factor);
```

`IItemComplementService.SetAsync` — nova assinatura e doc:

```csharp
    /// <summary>
    /// Upsert por <c>ItemCode</c>. Os dois campos comerciais são opcionais: passar ambos nulos limpa o
    /// complemento, que é uma operação válida. <paramref name="controlsRoyalties"/> e
    /// <paramref name="taxableUnit"/> nulos mantêm o valor atual (o diálogo de faturamento não conhece
    /// esses campos).
    /// </summary>
    Task<ItemComplementDto> SetAsync(
        string itemCode, string? commercialUnitOfMeasureCode, decimal? commercialFactor,
        bool? controlsRoyalties = null, ItemTaxableUnitInput? taxableUnit = null);
```

`ItemComplementService`:
- assinatura igual à da interface;
- acrescente `using SiagroB1.Domain.Exceptions;`;
- no `GetAsync`, no `Select`, depois de `CommercialFactor = x.CommercialFactor,`: `TaxableUnitOfMeasureCode = x.TaxableUnitOfMeasureCode, TaxableFactor = x.TaxableFactor,`;
- no `SetAsync`, **antes** de buscar a entidade:

```csharp
        var taxable = taxableUnit is null ? null : NormalizeTaxableUnit(taxableUnit);
```

- depois do bloco `if (controlsRoyalties.HasValue) ...`:

```csharp
        if (taxableUnit is not null)
        {
            entity.TaxableUnitOfMeasureCode = taxable?.UnitOfMeasureCode;
            entity.TaxableFactor = taxable?.Factor;
        }
```

- no DTO de retorno, depois de `CommercialFactor = entity.CommercialFactor,`: `TaxableUnitOfMeasureCode = entity.TaxableUnitOfMeasureCode, TaxableFactor = entity.TaxableFactor,`;
- método novo:

```csharp
    /// <summary>
    /// Unidade tributável de exportação (spec 2026-10-14 §2): os dois juntos, fator positivo, unidade de até
    /// 6 caracteres (tamanho do uTrib) em maiúsculas. Os dois vazios = limpar (devolve nulo).
    /// </summary>
    private static ItemTaxableUnitInput? NormalizeTaxableUnit(ItemTaxableUnitInput input)
    {
        var unit = string.IsNullOrWhiteSpace(input.UnitOfMeasureCode) ? null : input.UnitOfMeasureCode.Trim().ToUpperInvariant();

        if (unit is null && input.Factor is null)
            return null;
        if (unit is null || input.Factor is null)
            throw new DefaultException("Informe a unidade tributável e o fator juntos.");
        if (input.Factor <= 0)
            throw new DefaultException("O fator da unidade tributável deve ser maior que zero.");
        if (unit.Length > 6)
            throw new DefaultException("A unidade tributável deve ter no máximo 6 caracteres.");

        return new ItemTaxableUnitInput(unit, input.Factor);
    }
```

`ItemsSetComplementController.PostAsync` — depois de `parameters.TryGetValue("ControlsRoyalties", out var royaltiesObj);`:

```csharp
            // Ausentes do corpo = inalterados (o diálogo de faturamento não os envia); presentes com null = limpar.
            var hasTaxableUnit = parameters.TryGetValue("TaxableUnitOfMeasureCode", out var taxableUomObj);
            var hasTaxableFactor = parameters.TryGetValue("TaxableFactor", out var taxableFactorObj);
            var taxableUnit = hasTaxableUnit || hasTaxableFactor
                ? new ItemTaxableUnitInput(
                    taxableUomObj?.ToString(), taxableFactorObj is null ? null : Convert.ToDecimal(taxableFactorObj))
                : null;
```

e passe `taxableUnit` como último argumento de `service.SetAsync(...)`; `using SiagroB1.Domain.Dtos;` no topo.

`ODataConfigurations.cs` — depois de `itemsSetComplement.Parameter<bool>("ControlsRoyalties").Optional();`:

```csharp
        // Unidade tributável de exportação (spec 2026-10-14 §2). Omitidos = mantém; fator double pelo mesmo motivo acima.
        itemsSetComplement.Parameter<string>("TaxableUnitOfMeasureCode").Optional();
        itemsSetComplement.Parameter<double?>("TaxableFactor").Optional();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ItemComplement|FullyQualifiedName~ItemsComplement"`
Expected: PASS.

- [ ] **Step 5: Full backend gate**

Run: `dotnet build SiagroB1.sln` e `dotnet test SiagroB1.Application.Tests` e `dotnet test SiagroB1.Fiscal.Tests`
Expected: build sem erros; todas as suítes verdes. Anote os totais para o relatório final.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Domain/Dtos/ItemComplementDto.cs SiagroB1.Domain/Interfaces/IItemComplementService.cs SiagroB1.Application/Services/ItemComplementService.cs SiagroB1.Web/Actions/Items/ItemsSetComplementController.cs SiagroB1.Web/ODataConfig/ODataConfigurations.cs SiagroB1.Application.Tests/Items/ItemComplementServiceTests.cs SiagroB1.Application.Tests/Items/ItemsComplementEdmModelTests.cs
git commit -m "feat(master-data): unidade tributável de exportação no complemento do produto

Cadastro da unidade e do fator que a NF-e usa no grupo tributável dos
CFOPs de exportação. Os dois andam juntos e o fator é positivo; omitidos
na action mantêm o valor, porque o diálogo de faturamento grava o
complemento sem conhecê-los.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: Tela Complemento do produto (frontend)

**Files:**
- Modify: `webapp/view/produtos/Complement.view.xml`
- Modify: `webapp/controller/produtos/Complement.controller.ts`

**Interfaces:**
- Consumes: action `ItemsSetComplement` com `TaxableUnitOfMeasureCode`/`TaxableFactor`; function `ItemsGetComplement` devolvendo os mesmos dois campos (Task 5).

- [ ] **Step 1: Worktree do frontend**

```bash
cd /c/Projetos/SiagroB1/siagro-b1-frontend
git worktree add -b feature/nfe-export-taxable-unit ../siagro-b1-frontend-export-unit main
cd ../siagro-b1-frontend-export-unit && yarn install
```

Todos os passos seguintes são dentro de `C:\Projetos\SiagroB1\siagro-b1-frontend-export-unit`.

- [ ] **Step 2: View** — em `Complement.view.xml`, entre o `<Text text="Preenchidos os dois campos, o preço do contrato ..."/>` e `<core:Title text="Royalties (ITS)" .../>`:

```xml
					<core:Title text="Exportação (NF-e)" emphasized="true" />
					<Label text="Unidade tributável" />
					<Input
						id="taxableUnitOfMeasureCode"
						value="{complement>/TaxableUnitOfMeasureCode}"
						maxLength="6"
					/>
					<Label text="Fator (KG por unidade tributável)" />
					<Input
						id="taxableFactor"
						value="{
							path: 'complement>/TaxableFactor',
							type: 'sap.ui.model.type.Float',
							formatOptions: { decimals: 6, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' }
						}"
					/>
					<Label text="" />
					<Text text="Preenchidos os dois campos, a NF-e com CFOP de exportação (7xxx, 5501/6501, 5502/6502, 5504/6504, 5505/6505) informa a quantidade e o valor tributados nesta unidade (ex.: TON com fator 1000). Deixe os dois em branco para usar a unidade do documento." />
```

- [ ] **Step 3: Controller**

- `ComplementForm`: acrescente `TaxableUnitOfMeasureCode?: string, TaxableFactor?: number,`.
- `routeMatched`, no `model.setData({...})`: `TaxableUnitOfMeasureCode: complement?.TaxableUnitOfMeasureCode, TaxableFactor: complement?.TaxableFactor,`.
- Generalize `readFactor` para receber o id do Input (mesma lógica):

```ts
  private readFactor(inputId: string): number {
    const raw = (this.byId(inputId) as Input).getValue().trim();
```

  e troque a chamada existente por `this.readFactor("commercialFactor")`. Ajuste o comentário de doc para "Le um fator direto do Input...".
- `onClear`: depois de zerar os comerciais, zere também:

```ts
    model.setProperty("/TaxableUnitOfMeasureCode", null);
    model.setProperty("/TaxableFactor", null);

    const taxableFactor = this.byId("taxableFactor") as Input;
    taxableFactor.setValue("");
    taxableFactor.setValueState(ValueState.None);
```

  e atualize o doc do método: "Zera a UoM comercial e a unidade tributavel: e a unica forma de voltar a exibir em KG, ja que a unidade comercial e valueHelpOnly."
- `onSave`, depois da validação do fator comercial:

```ts
    const taxableUom = ((model.getProperty("/TaxableUnitOfMeasureCode") as string) || "").trim().toUpperCase() || null;
    const taxableFactor = this.readFactor("taxableFactor");

    if ((taxableUom === null) !== (taxableFactor === null)) {
      MessageBox.warning("Informe a unidade tributável e o fator juntos, ou deixe os dois em branco.");
      return;
    }

    if (taxableFactor !== null && !(taxableFactor > 0)) {
      MessageBox.warning("O fator da unidade tributável deve ser maior que zero.");
      return;
    }
```

  depois de `action.setParameter("ControlsRoyalties", ...)`:

```ts
    action.setParameter("TaxableUnitOfMeasureCode", taxableUom);
    action.setParameter("TaxableFactor", taxableFactor);
```

  e no sucesso, junto aos outros `setProperty`:

```ts
      model.setProperty("/TaxableUnitOfMeasureCode", taxableUom);
      model.setProperty("/TaxableFactor", taxableFactor);
      (this.byId("taxableFactor") as Input).setValueState(ValueState.None);
```

- [ ] **Step 4: Gates**

Run: `yarn ts-typecheck` e `yarn lint`
Expected: ambos sem erros. (`yarn test` não é gate: o limiar de cobertura nunca passa neste repositório.)

- [ ] **Step 5: Verificação pela tela** (antes do commit; ver Task 7 para subir a stack)

Com a stack da Task 7 no ar: abrir `/items`, botão **Complemento** do produto SOJA, preencher `ton` / `1.000`, Salvar → toast; recarregar (F5) → mostra `TON` e `1.000,000000`. Preencher só a unidade → aviso "Informe a unidade tributável e o fator juntos…". **Limpar** + Salvar → recarregar → os dois vazios. Deixar configurado `TON`/`1000` para a Task 7.

- [ ] **Step 6: Commit (repositório do frontend)**

```bash
git add webapp/view/produtos/Complement.view.xml webapp/controller/produtos/Complement.controller.ts
git commit -m "feat(master-data): unidade tributável de exportação na tela de complemento

Seção Exportação (NF-e) com a unidade e o fator que a NF-e usa no grupo
tributável dos CFOPs de exportação (ex.: soja em TON, fator 1000). Limpar
zera também esses campos.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 7: Verificação ponta a ponta em homologação

Nada é commitado aqui além de eventuais correções (cada uma com teste, no repo e na task a que pertence).

- [ ] **Step 1: Banco e stack**

Siga a memória `run-stack-locally` (profile `ceagui` para o banco CEAGUI dev, `yarn start:dev` no frontend), a partir **dos dois worktrees**. Aplique a migration no banco de dev com `ASPNETCORE_ENVIRONMENT` explícito (o profile `db-migration` aponta para o `appsettings.json` — confira a connection string antes):

Run: `dotnet ef database update AddItemComplementTaxableUnit --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web` com `ASPNETCORE_ENVIRONMENT` do banco de dev.
Expected: migration aplicada; o Web sobe (ele recusa subir com migration pendente).

Ao encerrar processos, mate **só pelo PID** que você iniciou (nunca `taskkill /IM node.exe`/`dotnet.exe`).

- [ ] **Step 2: Emitir**

Com SOJA configurada (`TON`/`1000`, Task 6 Step 5): criar pela tela um documento de saída com uma natureza de CFOP 5504 (ou 6504/7101, conforme as naturezas cadastradas no CEAGUI dev), item SOJA com quantidade não múltipla de 1000 (ex.: 30.000,5 KG), confirmar e transmitir em **homologação**.
Expected: autorizada. Baixar o XML: `<uCom>KG</uCom>`, `<qCom>` igual ao documento, `<uTrib>TON</uTrib>`, `<qTrib>30.0005</qTrib>`, `<vUnTrib>` = vProd ÷ 30,0005 com até 10 casas.

Se não houver natureza com CFOP de exportação nem como cadastrar uma no CEAGUI dev, pare e reporte ao usuário em vez de improvisar dados fiscais.

- [ ] **Step 3: DANFE**

Abrir o DANFE pela tela do documento.
Expected: colunas UN/QTD/V.UNIT em duas linhas (KG em cima, TON embaixo); quantidade com 4 casas e valor unitário com as casas do XML. Abrir também o DANFE de uma NF-e antiga (mercado interno): uma linha só, como antes.

- [ ] **Step 4: Relatório**

Reportar ao usuário: chave/numero da NF-e de homologação, trechos do XML, os dois DANFEs observados, totais das suítes da Task 5, e os commits de cada repo (branch `feature/nfe-export-taxable-unit` nos dois; nada mergeado nem publicado). Lembrar que a migration `AddItemComplementTaxableUnit` precisa ser aplicada em cada ambiente antes do deploy do Web.
