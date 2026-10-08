# Tipo e País no Grid de Endereços do Parceiro — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** No grid de endereços de `business-partners/new` e `/{id}/edit`, o Tipo vira um Select
(Cobrança/Entrega) e o País um value help sobre uma tabela nova `COUNTRIES`; o backend recusa
tipo fora de `B`/`S`; o grid deixa de herdar `ui>/editable` de outra tela.

**Architecture:** `COUNTRIES` é tabela de referência só leitura, no molde exato de
`MUNICIPALITIES` (entidade exposta direto no EDM, seed por recurso embutido, serviço concreto
registrado nos dois modos). Sem FK em `Address.Country`, sem OCRY. A validação do tipo é um helper
estático chamado nos dois caminhos de inclusão de endereço. O frontend troca dois Inputs do
fragment e reaproveita `applyValueHelp`.

**Tech Stack:** .NET 10, EF Core (SQL Server; InMemory nos testes), OData v4, xUnit; OpenUI5 1.141 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-08-partner-address-type-country-design.md` (backend).

## Global Constraints

- Branch `feature/partner-address-type-country` nos **dois** repos (o backend já está nele; o
  frontend ainda não — criar na Task 4). Conferir o branch antes de cada commit; **nunca push**,
  nunca merge.
- Todo arquivo novo: `git add` logo depois de criar. Commit com pathspec explícito.
- Mensagem: `tipo(escopo): descrição pt-BR imperativa minúscula sem ponto` — escopo `partner`
  (`master-data` para a tabela de países). Rodapé
  `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`. Commit com migration
  leva o trailer `DB: CreateCountries`.
- Identificadores em inglês; texto que o usuário lê em pt-BR.
- `AdresType` só aceita `"B"` (Cobrança) e `"S"` (Entrega). É parte da chave do endereço
  (`CardCode + AddressName + AdresType`).
- Erro de negócio = `DefaultException` (é a única que os controllers do parceiro convertem em 400).
- País default em endereço novo: `"BR"`. Sem FK, sem alterar o tipo da coluna `Address.Country`.
- NF-e não muda (`NfeXmlBuilder` continua com 1058/BRASIL fixos).
- Migration só em banco **localhost**, sempre com `ASPNETCORE_ENVIRONMENT` explícito. Nunca o
  profile `db-migration`.

## Review Focus

1. **Linha lida do servidor no `/edit`** — o Select de Tipo deve vir travado. `@$ui5.context.isTransient`
   é `undefined` nela (não `false`); uma expressão `!== false` deixaria livre. Coberto no E2E da Task 5, passo 2.
2. **Linha incluída no `/edit` e salva** — depois do Salvar ela passa a `false` e trava; antes do
   Salvar fica livre. E2E Task 5, passo 2.
3. **Value help de País aberto numa linha transiente sem `Country` no payload** — "Must not change
   a property before it has been read". O `onAddAddress` leva `Country: "BR"` e `AdresType: null`. E2E Task 5, passo 1.
4. **Tipo inválido vindo por deep insert do parceiro** (não só pelo POST de endereço) — tem de ser
   400 com a mensagem, sem gravar parceiro nenhum. Teste na Task 3.
5. **Seed com ISO-2 ou BACEN duplicado** (dois nomes BACEN casando com o mesmo ISO) — quebra a PK
   na migration só no banco. Teste de unicidade na Task 1.

---

### Task 1: Arquivo de países + `CountrySeed`

**Files:**
- Create (descartável, fora do repo): `<scratchpad>/countries-gen/gen.cs`
- Create: `SiagroB1.Migrations/Seeds/countries.txt`
- Create: `SiagroB1.Migrations/Seeds/CountrySeed.cs`
- Modify: `SiagroB1.Migrations/SiagroB1.Migrations.csproj` (EmbeddedResource, ao lado da linha 20)
- Test: `SiagroB1.Application.Tests/Countries/CountrySeedTests.cs`

**Interfaces:**
- Produces: `SiagroB1.Migrations.Seeds.CountrySeed.Read(): IReadOnlyList<(string Code, string BacenCode, string Name)>`
  e `CountrySeed.InsertBatches(int batchSize = 500): IEnumerable<string>` (SQL `INSERT INTO COUNTRIES (Code, BacenCode, Name) VALUES ...;`).
- Formato do arquivo: `ISO2|BACEN4|NOME`, UTF-8, uma linha por país, ordenado por nome.

- [ ] **Step 1: Gerar o casamento BACEN → ISO-2**

Fonte: `C:\Projetos\EfisCloud\backend\src\main\resources\ibge\paises.txt` (linhas `00000 NOME`).
Script em `<scratchpad>/countries-gen/gen.cs` (rodar com `dotnet run gen.cs -- <paises.txt> <saida>`):

```cs
using System.Globalization;
using System.Text;

static string Norm(string s)
{
    var d = s.Normalize(NormalizationForm.FormD);
    var sb = new StringBuilder();
    foreach (var c in d)
        if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
    return sb.ToString().ToUpperInvariant().Replace("-", " ").Trim();
}

CultureInfo.CurrentUICulture = new CultureInfo("pt-BR");

// Todas as regiões ISO-2 que o ICU conhece, com o nome em pt-BR.
var regions = new Dictionary<string, string>(); // nome normalizado -> ISO-2
for (var a = 'A'; a <= 'Z'; a++)
for (var b = 'A'; b <= 'Z'; b++)
{
    var code = $"{a}{b}";
    try
    {
        var r = new RegionInfo(code);
        if (r.TwoLetterISORegionName != code) continue;
        regions.TryAdd(Norm(r.DisplayName), code);
        regions.TryAdd(Norm(r.EnglishName), code);
    }
    catch (ArgumentException) { }
}

var matched = new List<(string Iso, string Bacen, string Name)>();
var unmatched = new List<string>();

foreach (var line in File.ReadAllLines(args[0], Encoding.Latin1).Where(l => l.Trim().Length > 6))
{
    var bacen = line[1..5];                 // "01058" -> "1058"
    var name = line[6..].Trim();
    var head = Norm(name.Split(',')[0]);    // "ALBANIA, REPUBLICA DA" -> "ALBANIA"
    if (regions.TryGetValue(Norm(name), out var iso) || regions.TryGetValue(head, out iso))
        matched.Add((iso, bacen, name));
    else
        unmatched.Add($"{bacen} {name}");
}

foreach (var dup in matched.GroupBy(m => m.Iso).Where(g => g.Count() > 1))
    Console.WriteLine($"CONFLITO {dup.Key}: {string.Join(" / ", dup.Select(d => d.Bacen + " " + d.Name))}");
Console.WriteLine($"casados {matched.Count}, sem casamento {unmatched.Count}:");
unmatched.ForEach(Console.WriteLine);

File.WriteAllLines(args[1], matched.OrderBy(m => m.Name).Select(m => $"{m.Iso}|{m.Bacen}|{m.Name}"), new UTF8Encoding(false));
```

Run: `cd <scratchpad>/countries-gen && dotnet run gen.cs -- "C:/Projetos/EfisCloud/backend/src/main/resources/ibge/paises.txt" countries.txt`
Expected: a linha `BR|1058|BRASIL` no arquivo; uma lista de `CONFLITO` e de "sem casamento" no console.

- [ ] **Step 2: Revisão manual**

Para cada linha "sem casamento": atribuir o ISO-2 à mão quando o país existir na ISO 3166
(ex.: nomes BACEN como "COREIA (DO SUL), REPUBLICA DA" → `KR`, "ESTADOS UNIDOS" → `US`,
"REINO UNIDO" → `GB` — conferir cada um no próprio `RegionInfo`). Para cada `CONFLITO`: manter
só a entrada BACEN que é o país soberano atual. Território extinto/agrupamento sem ISO-2 fica
**de fora**: anotar a lista (código + nome) para o corpo do commit.
Copiar o resultado para `SiagroB1.Migrations/Seeds/countries.txt` e `git add` nele.

- [ ] **Step 3: Escrever o teste que falha**

`SiagroB1.Application.Tests/Countries/CountrySeedTests.cs`:

```cs
using SiagroB1.Migrations.Seeds;

namespace SiagroB1.Application.Tests.Countries;

/// <summary>
/// Países da tabela do BACEN (a do <c>cPais</c>/<c>xPais</c> da NF-e), chaveados pelo ISO-2 que o
/// endereço do parceiro já guarda (<c>BR</c>). ISO-2 ou BACEN repetido quebra a PK/índice único
/// só quando a migration roda no banco — o InMemory não acusa.
/// </summary>
public class CountrySeedTests
{
    [Fact]
    public void Seed_has_unique_iso_and_bacen_codes()
    {
        var rows = CountrySeed.Read();

        Assert.True(rows.Count >= 190, $"só {rows.Count} países");
        Assert.All(rows, r => Assert.Matches("^[A-Z]{2}$", r.Code));
        Assert.All(rows, r => Assert.Matches("^[0-9]{4}$", r.BacenCode));
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Name)));
        Assert.Equal(rows.Count, rows.Select(r => r.Code).Distinct().Count());
        Assert.Equal(rows.Count, rows.Select(r => r.BacenCode).Distinct().Count());
    }

    [Fact]
    public void Seed_has_brazil_with_the_nfe_code()
    {
        var rows = CountrySeed.Read();

        Assert.Contains(rows, r => r is { Code: "BR", BacenCode: "1058", Name: "BRASIL" });
    }

    [Fact]
    public void Insert_batches_cover_every_row()
    {
        var rows = CountrySeed.Read();
        var batches = CountrySeed.InsertBatches(100).ToList();

        Assert.Equal((rows.Count + 99) / 100, batches.Count);
        Assert.All(batches, b => Assert.StartsWith("INSERT INTO COUNTRIES (Code, BacenCode, Name) VALUES", b));
        Assert.Contains(batches, b => b.Contains("('BR', '1058', N'BRASIL')"));
    }
}
```

- [ ] **Step 4: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~CountrySeedTests"`
Expected: FAIL de compilação — `CountrySeed` não existe.

- [ ] **Step 5: Implementar `CountrySeed` e embutir o recurso**

`SiagroB1.Migrations/SiagroB1.Migrations.csproj`, ao lado do `municipalities.txt`:

```xml
      <EmbeddedResource Include="Seeds\countries.txt" LogicalName="SiagroB1.Migrations.Seeds.countries.txt" />
```

`SiagroB1.Migrations/Seeds/CountrySeed.cs`:

```cs
using System.Text;

namespace SiagroB1.Migrations.Seeds;

/// <summary>
/// Países (<c>ISO-2|BACEN|nome</c>), embarcados no assembly das migrations. Nome e código BACEN
/// vêm do <c>ibge/paises.txt</c> do EfisCloud (a tabela do <c>cPais</c>/<c>xPais</c> da NF-e); o
/// ISO-2 foi casado uma vez, ao gerar o arquivo. Lido pela migration <c>CreateCountries</c>.
/// </summary>
public static class CountrySeed
{
    private const string ResourceName = "SiagroB1.Migrations.Seeds.countries.txt";

    public static IReadOnlyList<(string Code, string BacenCode, string Name)> Read()
    {
        using var stream = typeof(CountrySeed).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"Recurso {ResourceName} não encontrado.");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var rows = new List<(string Code, string BacenCode, string Name)>();

        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split('|');
            rows.Add((parts[0].Trim(), parts[1].Trim(), parts[2].Trim()));
        }

        return rows;
    }

    /// <summary>INSERTs de até <paramref name="batchSize"/> linhas; <c>N'...'</c> e apóstrofo dobrado.</summary>
    public static IEnumerable<string> InsertBatches(int batchSize = 500) =>
        Read()
            .Chunk(batchSize)
            .Select(chunk =>
                "INSERT INTO COUNTRIES (Code, BacenCode, Name) VALUES " +
                string.Join(", ", chunk.Select(r => $"('{r.Code}', '{r.BacenCode}', N'{r.Name.Replace("'", "''")}')")) +
                ";");
}
```

`git add SiagroB1.Migrations/Seeds/CountrySeed.cs SiagroB1.Application.Tests/Countries/CountrySeedTests.cs`

- [ ] **Step 6: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~CountrySeedTests"`
Expected: 3 PASS. Se falhar a unicidade, voltar ao Step 2 (conflito não resolvido).

- [ ] **Step 7: Commit**

```bash
git branch --show-current   # feature/partner-address-type-country
git add SiagroB1.Migrations/Seeds/countries.txt SiagroB1.Migrations/Seeds/CountrySeed.cs SiagroB1.Migrations/SiagroB1.Migrations.csproj SiagroB1.Application.Tests/Countries/CountrySeedTests.cs
git commit -m "feat(master-data): adiciona semente de países com ISO-2 e código BACEN" -m "<corpo: origem do arquivo + lista dos códigos BACEN deixados de fora>" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Tabela `COUNTRIES` + migration + entity set `Countries`

**Files:**
- Create: `SiagroB1.Domain/Entities/Country.cs`
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs:47` (DbSet ao lado de `Municipalities`)
- Create: `SiagroB1.Migrations/AppContext/<timestamp>_CreateCountries.cs` (+ `.Designer.cs`, snapshot)
- Create: `SiagroB1.Application/Services/CountryService.cs`
- Create: `SiagroB1.Web/Controllers/CountriesController.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs:342` (registro ao lado de `MunicipalityService`)
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs:55` (entity set ao lado de `Municipalities`)
- Test: `SiagroB1.Application.Tests/Countries/CountryServiceTests.cs`

**Interfaces:**
- Consumes: `CountrySeed.InsertBatches()` (Task 1).
- Produces: entidade `SiagroB1.Domain.Entities.Country { Code, Name, BacenCode }`; OData
  `GET /odata/Countries` (PageSize 300) e `GET /odata/Countries('BR')`; `CountryService.QueryAll()` / `GetByIdAsync(string)`.

- [ ] **Step 1: Teste que falha**

`SiagroB1.Application.Tests/Countries/CountryServiceTests.cs`:

```cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.Countries;

/// <summary>Países: tabela de referência só leitura que alimenta o value help do endereço.</summary>
public class CountryServiceTests
{
    [Fact]
    public async Task Lists_and_finds_countries_by_iso_code()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Countries.Add(new Country { Code = "BR", BacenCode = "1058", Name = "BRASIL" });
        db.Context.Countries.Add(new Country { Code = "AR", BacenCode = "0639", Name = "ARGENTINA" });
        await db.SaveChangesAsync();
        var service = new CountryService(db);

        Assert.Equal(2, await service.QueryAll().CountAsync());
        Assert.Equal("1058", (await service.GetByIdAsync("BR"))!.BacenCode);
        Assert.Null(await service.GetByIdAsync("XX"));
    }
}
```

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~CountryServiceTests"`
Expected: FAIL de compilação (`Country`, `Countries`, `CountryService` inexistentes).

- [ ] **Step 2: Entidade + DbSet**

`SiagroB1.Domain/Entities/Country.cs`:

```cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// País — tabela de referência, somente leitura, semeada pela migration. <see cref="Code"/> é o
/// ISO-2 que o endereço do parceiro guarda em <c>Country</c>; <see cref="BacenCode"/> é o
/// <c>cPais</c> da NF-e (hoje a NF-e ainda usa Brasil fixo).
/// </summary>
[Table("COUNTRIES")]
[Index(nameof(BacenCode), IsUnique = true)]
public class Country
{
    [Key]
    [Column(TypeName = "VARCHAR(2)")]
    public required string Code { get; set; }

    [Column(TypeName = "NVARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    public required string BacenCode { get; set; }
}
```

`SiagroB1.Infra/Context/AppDbContext.cs`, logo depois de `Municipalities`:

```cs
    public DbSet<Country> Countries { get; set; }
```

- [ ] **Step 3: Serviço, controller, DI e EDM**

`SiagroB1.Application/Services/CountryService.cs`:

```cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services;

/// <summary>Países, só leitura (value help do endereço do parceiro).</summary>
public class CountryService(IUnitOfWork db)
{
    public IQueryable<Country> QueryAll() => db.Context.Countries.AsNoTracking();

    public Task<Country?> GetByIdAsync(string code) =>
        db.Context.Countries.AsNoTracking().FirstOrDefaultAsync(c => c.Code == code);
}
```

`SiagroB1.Web/Controllers/CountriesController.cs`:

```cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services;

namespace SiagroB1.Web.Controllers;

/// <summary>Países — somente leitura, nos dois modos (é dado de referência).</summary>
public class CountriesController(CountryService service) : ODataController
{
    [EnableQuery(PageSize = 300)]
    public IActionResult Get() => Ok(service.QueryAll());

    [EnableQuery]
    public async Task<IActionResult> Get([FromRoute] string key)
    {
        var country = await service.GetByIdAsync(key.Trim('\''));

        return country is null ? NotFound() : Ok(country);
    }
}
```

`ServiceCollectionExtensions.cs`, logo após `services.AddScoped<MunicipalityService>();`:

```cs
        services.AddScoped<CountryService>();
```

`ODataConfigurations.cs`, logo após `modelBuilder.EntitySet<Municipality>("Municipalities");`:

```cs
        modelBuilder.EntitySet<Country>("Countries");
```

`git add` nos 3 arquivos novos. Run o teste do Step 1 → PASS.

- [ ] **Step 4: Gerar a migration**

```bash
dotnet build SiagroB1.sln
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add CreateCountries --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

**Ler a migration gerada**: o `Up()` deve conter só `CreateTable("COUNTRIES")` (PK `PK_COUNTRIES`)
e `CreateIndex("IX_COUNTRIES_BacenCode", unique)`. Qualquer outra operação = drift do snapshot →
parar e reportar. Acrescentar ao fim do `Up()`:

```cs
            // Países da tabela do BACEN com o ISO-2 (ver CountrySeed).
            foreach (var sql in SiagroB1.Migrations.Seeds.CountrySeed.InsertBatches())
            {
                migrationBuilder.Sql(sql);
            }
```

`Down()` fica só com o `DropTable("COUNTRIES")` gerado. `git add` na migration e no Designer.

- [ ] **Step 5: Aplicar nos bancos locais**

Antes, conferir que os dois ambientes apontam para `localhost`:
`grep -n "SiagroDB" SiagroB1.Web/appsettings.Ceagui-Development.json SiagroB1.Web/appsettings.Yokotobi-Development.json`
(ambos `Server=localhost`; se não, NÃO aplicar e reportar).

```bash
dotnet build SiagroB1.sln
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
ASPNETCORE_ENVIRONMENT=Yokotobi-Development dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
sqlcmd -S localhost -E -d CEAGUI_SIAGRO_DEV -I -W -Q "SELECT COUNT(*) FROM COUNTRIES; SELECT * FROM COUNTRIES WHERE Code='BR'"
```

Expected: a contagem igual a `CountrySeed.Read().Count` e `BR 1058 BRASIL`. Se o `IDX_SIAGRO_DEV`
recusar por migration anterior pendente, não aplicar as outras — reportar.

- [ ] **Step 6: Suíte inteira + commit**

Run: `dotnet test SiagroB1.Application.Tests` → tudo verde (ou só as falhas que já existiam no `main`; listar).

```bash
git branch --show-current
git add SiagroB1.Domain/Entities/Country.cs SiagroB1.Infra/Context/AppDbContext.cs SiagroB1.Migrations/AppContext/ SiagroB1.Application/Services/CountryService.cs SiagroB1.Web/Controllers/CountriesController.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Web/ODataConfig/ODataConfigurations.cs SiagroB1.Application.Tests/Countries/CountryServiceTests.cs
git commit -m "feat(master-data): cria a tabela de países e o entity set Countries" -m "<corpo: por que só local, sem OCRY nem FK>" -m "DB: CreateCountries" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Recusar tipo de endereço fora de B/S

**Files:**
- Create: `SiagroB1.Application/Services/AddressTypes.cs`
- Modify: `SiagroB1.Application/Services/BusinessPartnerService.cs` (`CreateAsync`, logo após `ValidateNfeFields(model);`)
- Modify: `SiagroB1.Application/Services/BusinessPartnerAddressService.cs` (`Create`, primeira linha)
- Modify: `SiagroB1.Commons/Resources/Resource.resx`, `Resource.pt-br.resx` (fim do arquivo)
- Test: `SiagroB1.Application.Tests/BusinessPartners/BusinessPartnerAddressServiceTests.cs`,
  `SiagroB1.Application.Tests/BusinessPartners/BusinessPartnerServiceAddressesTests.cs`

**Interfaces:**
- Produces: `AddressTypes.BillTo = "B"`, `AddressTypes.ShipTo = "S"`,
  `AddressTypes.EnsureValid(string? adresType, IStringLocalizer<Resource> resource)` → lança
  `DefaultException(resource["BP_ADDRESS_INVALID_TYPE"])`.

- [ ] **Step 1: Testes que falham**

Em `BusinessPartnerServiceAddressesTests` (usa os helpers `Service`, `Partner`, `Address` já existentes):

```cs
    [Fact]
    public async Task Create_rejects_an_address_type_other_than_bill_to_or_ship_to()
    {
        var db = TestDb.CreateUnitOfWork();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).CreateAsync(Partner(Address("FATURAMENTO", "B", "PR"), Address("OUTRO", "X", "SP"))));

        Assert.Equal("BP_ADDRESS_INVALID_TYPE", ex.Message);
        Assert.False(await db.Context.BusinessPartners.AnyAsync(p => p.CardCode == "C90001"));
    }
```

(acrescentar `using SiagroB1.Domain.Exceptions;`)

Em `BusinessPartnerAddressServiceTests`:

```cs
    private static AddressModel NewAddress(string type) => new()
    {
        AddressName = "ENTREGA", AdresType = type, Street = "RUA A", City = "CIDADE", State = "BA", Country = "BR",
    };

    private static async Task<BusinessPartnerAddressService> WithPartner(SiagroB1.Infra.UnitOfWork db)
    {
        db.Context.BusinessPartners.Add(new BusinessPartner { CardCode = "C90002", CardName = "CLIENTE" });
        await db.SaveChangesAsync();

        return new BusinessPartnerAddressService(
            db, NullLogger<BusinessPartnerAddressService>.Instance, new FakeStringLocalizer<Resource>());
    }

    [Fact]
    public async Task Create_rejects_an_address_type_other_than_bill_to_or_ship_to()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = await WithPartner(db);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.Create("C90002", NewAddress("X")));

        Assert.Equal("BP_ADDRESS_INVALID_TYPE", ex.Message);
        Assert.False(await db.Context.Addresses.AnyAsync());
    }

    [Theory]
    [InlineData("B")]
    [InlineData("S")]
    public async Task Create_accepts_bill_to_and_ship_to(string type)
    {
        var db = TestDb.CreateUnitOfWork();
        var service = await WithPartner(db);

        await service.Create("C90002", NewAddress(type));

        Assert.Equal(type, (await db.Context.Addresses.AsNoTracking().SingleAsync()).AdresType);
    }
```

(acrescentar `using SiagroB1.Domain.Exceptions;` e `using SiagroB1.Domain.Models;`. Se
`BusinessPartner` exigir mais `required` além de `CardCode`/`CardName`, preencher com valores fixos.)

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~BusinessPartner"`
Expected: os 2 testes de rejeição FAIL (nenhuma exceção lançada); o `Theory` PASS.

- [ ] **Step 2: Implementar**

`SiagroB1.Application/Services/AddressTypes.cs`:

```cs
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services;

/// <summary>
/// Tipo do endereço do parceiro, herdado do CRD1 do SAP. NF-e, CFOP, minuta e endereço do armazém
/// filtram por esses dois valores — outra letra passaria calada e quebraria todos eles.
/// </summary>
public static class AddressTypes
{
    /// <summary>Cobrança (faturamento).</summary>
    public const string BillTo = "B";

    /// <summary>Entrega.</summary>
    public const string ShipTo = "S";

    public static void EnsureValid(string? adresType, IStringLocalizer<Resource> resource)
    {
        if (adresType is not (BillTo or ShipTo))
            throw new DefaultException(resource["BP_ADDRESS_INVALID_TYPE"]);
    }
}
```

`BusinessPartnerService.CreateAsync`, logo depois de `ValidateNfeFields(model);`:

```cs
        foreach (var address in model.Addresses)
            AddressTypes.EnsureValid(address.AdresType, resource);
```

`BusinessPartnerAddressService.Create`, primeira linha do método:

```cs
        AddressTypes.EnsureValid(addressModel.AdresType, resource);
```

`Resource.pt-br.resx`, antes de `</root>`:

```xml
    <data name="BP_ADDRESS_INVALID_TYPE" xml:space="preserve">
        <value>Tipo de endereço inválido. Use Cobrança ou Entrega.</value>
    </data>
```

`Resource.resx`, antes de `</root>`:

```xml
    <data name="BP_ADDRESS_INVALID_TYPE" xml:space="preserve">
        <value>Invalid address type. Use Bill-to or Ship-to.</value>
    </data>
```

`git add SiagroB1.Application/Services/AddressTypes.cs`

- [ ] **Step 3: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests` → tudo verde.

- [ ] **Step 4: Commit**

```bash
git branch --show-current
git add SiagroB1.Application/Services/AddressTypes.cs SiagroB1.Application/Services/BusinessPartnerService.cs SiagroB1.Application/Services/BusinessPartnerAddressService.cs SiagroB1.Commons/Resources/Resource.resx SiagroB1.Commons/Resources/Resource.pt-br.resx SiagroB1.Application.Tests/BusinessPartners/
git commit -m "feat(partner): recusa tipo de endereço diferente de cobrança ou entrega" -m "<corpo: quem depende de B/S; DefaultException porque é a que vira 400>" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Frontend — Select de Tipo, value help de País, `ui>/editable`

Repo: `C:\Projetos\SiagroB1\siagro-b1-frontend`.

**Files:**
- Create: `webapp/dialogs/fragments/CountriesSelectDialog.fragment.xml`
- Modify: `webapp/controller/common/CommonController.ts:350-356` (método novo ao lado de `openMunicipalitiesValueHelp`)
- Modify: `webapp/view/parceirosNegocio/fragments/Addresses.fragment.xml:53-62` (Tipo) e `:135-144` (País)
- Modify: `webapp/controller/parceirosNegocio/BaseController.ts:10-22` (`onAddAddress`)
- Modify: `webapp/controller/parceirosNegocio/Add.controller.ts` (`newRouteMatched`) e `Edit.controller.ts` (`editRouteMatched`)

**Interfaces:**
- Consumes: OData `/Countries` (`Code`, `Name`, `BacenCode`) da Task 2.
- Produces: `CommonController.openCountriesValueHelp(ev: Input$ValueHelpRequestEvent): void`.

Não há teste unitário de view neste repo (só o sample); a verificação é typecheck/lint aqui e o
E2E da Task 5.

- [ ] **Step 1: Branch**

```bash
git -C /c/Projetos/SiagroB1/siagro-b1-frontend status --short
git -C /c/Projetos/SiagroB1/siagro-b1-frontend switch -c feature/partner-address-type-country
```

- [ ] **Step 2: Diálogo de países**

`webapp/dialogs/fragments/CountriesSelectDialog.fragment.xml` (`git add` em seguida):

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:core="sap.ui.core"
>
<TableSelectDialog
  class="sapUiSizeCompact"
  growing="true"
  growingThreshold="30"
  items="{
    path: '/Countries',
    sorter: { path: 'Name', descending: false }
  }"
  title="Países"
>
<columns>
  <Column width="5rem"><header><Text text="Código" /></header></Column>
  <Column><header><Text text="País" /></header></Column>
</columns>
<ColumnListItem>
  <cells>
    <Text text="{Code}" />
    <Text text="{Name}" />
  </cells>
</ColumnListItem>
</TableSelectDialog>
</core:FragmentDefinition>
```

`CommonController.ts`, logo depois de `openMunicipalitiesValueHelp`:

```ts
  /** País do endereço do parceiro. Grava o ISO-2 (`BR`), que é o que o endereço guarda. */
  openCountriesValueHelp(ev: Input$ValueHelpRequestEvent) {
    void this.applyValueHelp(ev, "CountriesSelectDialog", ["Code", "Name"], "Code");
  }
```

- [ ] **Step 3: Colunas do grid**

`Addresses.fragment.xml`, coluna "Tipo" — trocar o `<Input .../>` por:

```xml
          <!-- AdresType é chave do endereço: só a linha nova (transiente) escolhe o tipo.
               isTransient é undefined na linha lida do servidor, por isso "=== true". -->
          <Select
            selectedKey="{path: 'AdresType', targetType: 'any'}"
            forceSelection="false"
            required="true"
            width="100%"
            enabled="{= ${ui>/editable} === true &amp;&amp; %{@$ui5.context.isTransient} === true }"
          >
            <core:ListItem key="B" text="Cobrança" />
            <core:ListItem key="S" text="Entrega" />
          </Select>
```

Coluna "País" — trocar o `<Input .../>` por:

```xml
          <Input
            editable="{ui>/editable}"
            required="true"
            value="{Country}"
            showValueHelp="true"
            valueHelpOnly="true"
            valueHelpRequest=".openCountriesValueHelp"
          />
```

⚠️ Comentário XML sem `--` dentro (mata o fragment sem erro em gate nenhum).

- [ ] **Step 4: Linha nova e `ui>/editable`**

`parceirosNegocio/BaseController.ts`, payload do `oBinding.create({...})` em `onAddAddress` —
acrescentar, mantendo as que já existem:

```ts
      // O Select de tipo (targetType 'any') e o value help de país gravam nestas.
      AdresType: null,
      Country: "BR",
```

`Add.controller.ts`, em `newRouteMatched`, logo depois da linha `setProperty("/paymentConditionName", "")`:

```ts
		// O grid de endereços liga em ui>/editable; sem isto herdava o false de um Detail aberto antes.
		(this.getModel("ui") as JSONModel).setProperty("/editable", true);
```

`Edit.controller.ts`, em `editRouteMatched`, logo depois de `void this.refreshStandaloneFlag();`:

```ts
		// O grid de endereços liga em ui>/editable; sem isto herdava o false de um Detail aberto antes.
		(this.getModel("ui") as JSONModel).setProperty("/editable", true);
```

(`JSONModel` já é importado nos dois.)

- [ ] **Step 5: Gates**

Run (em `siagro-b1-frontend`): `yarn ts-typecheck && yarn lint && yarn ui5lint`
Expected: sem erro novo (comparar com `git stash; yarn ui5lint; git stash pop` se aparecer aviso
em arquivo não tocado).

- [ ] **Step 6: Commit**

```bash
git -C /c/Projetos/SiagroB1/siagro-b1-frontend branch --show-current
git -C /c/Projetos/SiagroB1/siagro-b1-frontend add webapp/dialogs/fragments/CountriesSelectDialog.fragment.xml webapp/controller/common/CommonController.ts webapp/view/parceirosNegocio/fragments/Addresses.fragment.xml webapp/controller/parceirosNegocio/BaseController.ts webapp/controller/parceirosNegocio/Add.controller.ts webapp/controller/parceirosNegocio/Edit.controller.ts
git -C /c/Projetos/SiagroB1/siagro-b1-frontend commit -m "feat(partner): escolhe tipo e país do endereço por lista" -m "<corpo: Select travado na linha gravada porque AdresType é chave; ui>/editable herdado>" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Verificação ponta a ponta (STANDALONE)

Sem arquivo novo. Stack CEAGUI local: Gateway e Web com profile `ceagui` (subir o Gateway,
esperar `Loading proxy data from config`, só então o Web), frontend `yarn start:dev`, login
`admin`/`1234`. No navegador automatizado, navegar pelo **menu** (não `goto` de hash).

- [ ] **Step 1: Inclusão** — Parceiros de Negócio → Novo. Incluir endereço: País já `BR`, Tipo
  vazio e habilitado. Escolher "Entrega"; abrir o value help de País, buscar "ARG", escolher;
  preencher o resto; Salvar. Abrir o parceiro em `/edit`: endereço com Tipo "Entrega" e País `AR`.
  Conferir no banco: `SELECT AdresType, Country FROM BUSINESS_PARTNERS_ADDRESSES WHERE CardCode='<código>'` → `S`, `AR`.
- [ ] **Step 2: Trava do tipo** — no `/edit`, o Select da linha gravada está **desabilitado**;
  Incluir outra linha → o Select dela está habilitado; Salvar → passa a desabilitado.
- [ ] **Step 3: `ui>/editable`** — abrir um Detail que põe `editable=false` (ex.: Romaneios de
  pesagem → detalhe) e depois Parceiros → editar: Incluir/Remover visíveis e células editáveis.
- [ ] **Step 4: Recusa no servidor** — pelo `fetch` do devtools na sessão logada,
  `POST /odata/BusinessPartners('<código>')/Addresses` com `AdresType: "X"` → 400 com
  "Tipo de endereço inválido. Use Cobrança ou Entrega." (só a inclusão valida: no PATCH o tipo é
  chave e não muda).
- [ ] **Step 5: Derrubar a stack** — matar por PID o que escuta em 50000, 5246 e 8080 e conferir
  de novo que as portas estão livres.
- [ ] **Step 6: Apagar o parceiro de teste** criado no Step 1 (pela tela ou `DELETE` no banco local).
