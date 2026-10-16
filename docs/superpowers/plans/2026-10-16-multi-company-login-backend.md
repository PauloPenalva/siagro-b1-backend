# Seleção de Empresa no Login — Plano de Implementação (backend)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Um deploy (Gateway/Web/Reports) serve várias empresas, cada uma com seu próprio banco de negócio, escolhida na tela de login e carregada na sessão.

**Architecture:** A empresa da sessão (`USER_SESSIONS.CompanyCode`) é resolvida pelo `CookieAuthMiddleware` (que já roda nos três hosts) e publicada num `ICompanyContext` scoped; `AppDbContext`, o `IDbConnection` do Dapper, o lock de emissão da NF-e e o FastReport leem a connection string desse contexto em vez de `ConnectionStrings:SiagroDB`. Jobs do Hangfire recebem a empresa por `AsyncLocal` (filtro) ou iteram todas as empresas (runner). `SIAGRO_COMMON` é o catálogo (`COMPANIES`), e os perfis passam a ser por empresa (`USERS_PROFILES.CompanyCode`), o que define quem entra onde.

**Tech Stack:** .NET 10, EF Core 10 (SQL Server + InMemory nos testes), ASP.NET Core, Hangfire 1.8.23, Microsoft.Data.SqlClient, FastReport.OpenSource, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-16-multi-company-login-design.md`

## Global Constraints

- Identificadores de código, tabelas e colunas em **inglês**; mensagens de negócio, comentários e textos para o usuário em **pt-BR**.
- Branch de trabalho: `feature/multi-company-login` (já criada). Todo arquivo novo é `git add`ado logo após criado. Commits conforme `CLAUDE.md` do repo (tipo/escopo fechados; trailer `DB:` obrigatório em commit com migration). **Nunca push.**
- `Erp` (SAPB1/STANDALONE) continua por deploy. `Company.SapCompanyDb` fica, mas não é usado.
- Sem empresa resolvível (design-time `dotnet ef`, scope raiz, endpoint anônimo) a conexão cai em `ConnectionStrings:SiagroDB` (fallback). Com empresa **ambient** (job) desconhecida no catálogo, **lança** — nunca cai no fallback em silêncio.
- Acesso à empresa = `IsAdmin` **ou** ≥1 linha em `USERS_PROFILES` com aquele `CompanyCode`. Não existe tabela `USER_COMPANIES`.
- Hangfire: storage em `SiagroCommon`; `sap-user-sync` continua job único; os outros três recorrentes rodam por empresa; jobs enfileirados carregam `companyCode` como **primeiro** parâmetro `string` chamado exatamente `companyCode`.
- Testes: `SiagroB1.Application.Tests` (xUnit + EF InMemory). Rodar `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Classe>"` por tarefa e a suíte inteira antes do commit final.
- Migrations geradas com `dotnet ef migrations add <Nome> --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web` e **revisadas** antes do commit. O profile `db-migration` aponta para o banco do `appsettings.json` (produção da Yokotobi!) — gere migrations com `ASPNETCORE_ENVIRONMENT=Ceagui-Development` explícito.

## Review Focus

1. **Empresa definida depois de o `AppDbContext` do scope já existir** — a conexão fica no fallback e o dado vai para o banco errado. Teste em Task 1 (`Set_after_connection_consumed_logs_warning_and_keeps_connection`) e ordenação middleware→controller garantida em Task 4.
2. **Sessão antiga (criada antes do deploy) sem `CompanyCode`** — tem de cair na empresa padrão, não em 401. Teste em Task 4 (`Legacy_session_without_company_uses_default_company`).
3. **Login com um único cadastro e `CompanyCode` ausente** (frontend antigo em cache, `login/basic`) — tem de entrar na única empresa. Teste em Task 5 (`Login_without_company_code_and_single_company_selects_it`).
4. **Job enfileirado com código de empresa que foi excluída** — tem de falhar barulhento, não processar no fallback. Teste em Task 1 (`Ambient_company_unknown_to_catalog_throws`).
5. **Cadastro de empresa apontando para o próprio banco COMMON ou com senha mascarada** — recusado. Testes em Task 9.

---

### Task 1: Infra de contexto de empresa (`SiagroB1.Infra/Companies`)

**Files:**
- Create: `SiagroB1.Infra/Companies/CompanyInfo.cs`
- Create: `SiagroB1.Infra/Companies/CompanyAmbient.cs`
- Create: `SiagroB1.Infra/Companies/ICompanyCatalog.cs`
- Create: `SiagroB1.Infra/Companies/CompanyCatalog.cs`
- Create: `SiagroB1.Infra/Companies/ICompanyContext.cs`
- Create: `SiagroB1.Infra/Companies/CompanyContext.cs`
- Create: `SiagroB1.Infra/Companies/CompanyServiceCollectionExtensions.cs`
- Create: `SiagroB1.Application.Tests/Support/FakeCompanyContext.cs`
- Create: `SiagroB1.Application.Tests/Support/FakeCompanyCatalog.cs`
- Test: `SiagroB1.Application.Tests/Companies/CompanyContextTests.cs`
- Test: `SiagroB1.Application.Tests/Companies/CompanyCatalogTests.cs`

**Interfaces:**
- Produces:
  - `record CompanyInfo(string Code, string CompanyName, string ConnectionString)`
  - `static class CompanyAmbient { string? CompanyCode; IDisposable Use(string? code); void Set(string? code); }`
  - `interface ICompanyCatalog { Task<IReadOnlyList<CompanyInfo>> GetAllAsync(CancellationToken ct = default); Task<CompanyInfo?> GetByCodeAsync(string code, CancellationToken ct = default); CompanyInfo? TryGetCached(string code); string DefaultCompanyCode { get; } void Invalidate(); }`
  - `interface ICompanyContext { CompanyInfo? Current { get; } bool IsResolved { get; } string CompanyCode { get; } string CompanyName { get; } string ConnectionString { get; } void Set(CompanyInfo company); }`
  - `IServiceCollection AddCompanyContext(this IServiceCollection services)` (singleton `ICompanyCatalog`, scoped `ICompanyContext`)
  - `FakeCompanyContext(string code = "0001", string? connectionString = null)` com `static FakeCompanyContext Unresolved()`; `FakeCompanyCatalog(params CompanyInfo[] companies)`.

- [ ] **Step 1: Escrever os tipos de domínio do contexto (sem lógica ainda)**

`SiagroB1.Infra/Companies/CompanyInfo.cs`:
```csharp
namespace SiagroB1.Infra.Companies;

/// <summary>Empresa do catálogo (COMPANIES), já projetada: nunca se cacheia a entidade rastreada.</summary>
public sealed record CompanyInfo(string Code, string CompanyName, string ConnectionString);
```

`SiagroB1.Infra/Companies/CompanyAmbient.cs`:
```csharp
namespace SiagroB1.Infra.Companies;

/// <summary>
/// Empresa "ambiente" do fluxo de execução corrente — o canal dos jobs do Hangfire, onde não há
/// HttpContext. O filtro de job grava aqui ANTES de o activator abrir o escopo de DI, e escopos
/// aninhados (ContractDraftsReconcileJob abre um por minuta) herdam o valor pelo ExecutionContext.
/// </summary>
public static class CompanyAmbient
{
    private static readonly AsyncLocal<string?> Current = new();

    public static string? CompanyCode => Current.Value;

    /// <summary>Define e devolve o restaurador — use com <c>using</c> no runner.</summary>
    public static IDisposable Use(string? code)
    {
        var previous = Current.Value;
        Current.Value = code;
        return new Restore(previous);
    }

    /// <summary>Para o filtro do Hangfire, que não tem onde guardar o IDisposable entre OnPerforming e OnPerformed.</summary>
    public static void Set(string? code) => Current.Value = code;

    private sealed class Restore(string? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
```

`SiagroB1.Infra/Companies/ICompanyCatalog.cs`:
```csharp
namespace SiagroB1.Infra.Companies;

/// <summary>Catálogo de empresas (COMPANIES na COMMON), cacheado por processo.</summary>
public interface ICompanyCatalog
{
    Task<IReadOnlyList<CompanyInfo>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Miss no cache recarrega UMA vez antes de devolver nulo: empresa cadastrada no Web aparece no Gateway/Reports sem esperar o TTL.</summary>
    Task<CompanyInfo?> GetByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>Leitura síncrona do snapshot — para o caminho dos jobs, que resolve a empresa dentro de um getter.</summary>
    CompanyInfo? TryGetCached(string code);

    /// <summary><c>Company:Code</c> da configuração; padrão "0001".</summary>
    string DefaultCompanyCode { get; }

    void Invalidate();
}
```

`SiagroB1.Infra/Companies/ICompanyContext.cs`:
```csharp
namespace SiagroB1.Infra.Companies;

/// <summary>
/// Empresa da execução corrente (scoped). Ordem de resolução: valor explícito do escopo (HTTP,
/// runner de jobs) → <see cref="CompanyAmbient"/> (filtro do Hangfire) → fallback da configuração
/// (<c>ConnectionStrings:SiagroDB</c>: design-time, guard de migrations, endpoints anônimos).
/// </summary>
public interface ICompanyContext
{
    /// <summary>Nulo quando nada foi resolvido (o fallback da configuração está em uso).</summary>
    CompanyInfo? Current { get; }

    bool IsResolved { get; }

    /// <summary>Código da empresa resolvida, ou o código padrão do catálogo.</summary>
    string CompanyCode { get; }

    string CompanyName { get; }

    /// <summary>Connection string do banco de negócio desta execução.</summary>
    string ConnectionString { get; }

    void Set(CompanyInfo company);
}
```

- [ ] **Step 2: Escrever os fakes de teste**

`SiagroB1.Application.Tests/Support/FakeCompanyContext.cs`:
```csharp
using SiagroB1.Infra.Companies;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Contexto de empresa fixo. <see cref="Unresolved"/> simula o fallback da configuração.</summary>
public sealed class FakeCompanyContext(string code = "0001", string? connectionString = null) : ICompanyContext
{
    public CompanyInfo? Current { get; private set; } =
        new(code, $"Empresa {code}", connectionString ?? $"Server=.;Database=DB_{code};Integrated Security=True");

    public static FakeCompanyContext Unresolved() => new() { Current = null };

    public bool IsResolved => Current is not null;
    public string CompanyCode => Current?.Code ?? "0001";
    public string CompanyName => Current?.CompanyName ?? string.Empty;
    public string ConnectionString => Current?.ConnectionString ?? "Server=.;Database=FALLBACK;Integrated Security=True";

    public void Set(CompanyInfo company) => Current = company;
}
```

`SiagroB1.Application.Tests/Support/FakeCompanyCatalog.cs`:
```csharp
using SiagroB1.Infra.Companies;

namespace SiagroB1.Application.Tests.Support;

public sealed class FakeCompanyCatalog(params CompanyInfo[] companies) : ICompanyCatalog
{
    public List<CompanyInfo> Companies { get; } = [.. companies];
    public int Invalidations { get; private set; }
    public string DefaultCompanyCode { get; set; } = "0001";

    public Task<IReadOnlyList<CompanyInfo>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CompanyInfo>>(Companies.OrderBy(c => c.Code).ToList());

    public Task<CompanyInfo?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        Task.FromResult(TryGetCached(code));

    public CompanyInfo? TryGetCached(string code) =>
        Companies.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

    public void Invalidate() => Invalidations++;
}
```

- [ ] **Step 3: Escrever os testes do `CompanyContext` (falham: classe não existe)**

`SiagroB1.Application.Tests/Companies/CompanyContextTests.cs`:
```csharp
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Infra.Companies;

namespace SiagroB1.Application.Tests.Companies;

/// <summary>
/// Ordem de resolução da empresa: explícita (HTTP) → ambiente (jobs) → fallback da configuração.
/// Empresa ambiente desconhecida LANÇA: processar um job no banco errado é pior que falhar.
/// </summary>
public class CompanyContextTests
{
    private static readonly CompanyInfo A = new("0001", "CEAGUI", "Server=.;Database=A");
    private static readonly CompanyInfo B = new("0002", "LM CEREAIS", "Server=.;Database=B");

    private static CompanyContext Create(FakeCompanyCatalog? catalog = null) =>
        new(catalog ?? new FakeCompanyCatalog(A, B),
            TestConfiguration.With(("ConnectionStrings:SiagroDB", "Server=.;Database=FALLBACK"), ("CompanyName", "DA CONFIG")),
            NullLogger<CompanyContext>.Instance);

    [Fact]
    public void Without_anything_set_uses_configuration_fallback()
    {
        var context = Create();

        Assert.False(context.IsResolved);
        Assert.Null(context.Current);
        Assert.Equal("Server=.;Database=FALLBACK", context.ConnectionString);
        Assert.Equal("0001", context.CompanyCode);
        Assert.Equal("DA CONFIG", context.CompanyName);
    }

    [Fact]
    public void Explicit_company_wins()
    {
        var context = Create();
        context.Set(B);

        Assert.True(context.IsResolved);
        Assert.Equal("Server=.;Database=B", context.ConnectionString);
        Assert.Equal("0002", context.CompanyCode);
        Assert.Equal("LM CEREAIS", context.CompanyName);
    }

    [Fact]
    public void Ambient_company_is_used_when_nothing_explicit()
    {
        using var _ = CompanyAmbient.Use("0002");
        var context = Create();

        Assert.Equal("Server=.;Database=B", context.ConnectionString);
    }

    [Fact]
    public void Explicit_beats_ambient()
    {
        using var _ = CompanyAmbient.Use("0002");
        var context = Create();
        context.Set(A);

        Assert.Equal("Server=.;Database=A", context.ConnectionString);
    }

    [Fact]
    public void Ambient_company_unknown_to_catalog_throws()
    {
        using var _ = CompanyAmbient.Use("9999");
        var context = Create();

        var ex = Assert.Throws<InvalidOperationException>(() => context.ConnectionString);
        Assert.Contains("9999", ex.Message);
    }

    [Fact]
    public void Ambient_Use_restores_previous_value()
    {
        Assert.Null(CompanyAmbient.CompanyCode);
        using (CompanyAmbient.Use("0001"))
        {
            Assert.Equal("0001", CompanyAmbient.CompanyCode);
            using (CompanyAmbient.Use("0002"))
                Assert.Equal("0002", CompanyAmbient.CompanyCode);
            Assert.Equal("0001", CompanyAmbient.CompanyCode);
        }
        Assert.Null(CompanyAmbient.CompanyCode);
    }

    [Fact]
    public void Set_after_connection_consumed_logs_warning_and_keeps_new_company()
    {
        var logger = new TestLogger<CompanyContext>();
        var context = new CompanyContext(new FakeCompanyCatalog(A, B),
            TestConfiguration.With(("ConnectionStrings:SiagroDB", "Server=.;Database=FALLBACK")), logger);

        _ = context.ConnectionString;   // AppDbContext do escopo já nasceu no fallback
        context.Set(B);

        Assert.Contains(logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("0002"));
        Assert.Equal("0002", context.CompanyCode);
    }
}
```

Confira a API de `TestLogger<T>` em `SiagroB1.Application.Tests/Support/TestLogger.cs` antes de usar `Entries`/`Level`/`Message`; adapte os nomes ao que existe lá (o teste precisa apenas verificar que houve um log de Warning contendo "0002").

- [ ] **Step 4: Escrever os testes do `CompanyCatalog` (falham)**

`SiagroB1.Application.Tests/Companies/CompanyCatalogTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Companies;

public class CompanyCatalogTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private ServiceProvider BuildProvider(string? defaultCode = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TestConfiguration.With(("Company:Code", defaultCode)));
        services.AddDbContext<CommonDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddCompanyContext();
        return services.BuildServiceProvider();
    }

    private void Seed(ServiceProvider provider, params (string Code, string Name)[] companies)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommonDbContext>();
        foreach (var (code, name) in companies)
            db.Companies.Add(new Company { Code = code, CompanyName = name, ConnectionString = $"Server=.;Database={code}" });
        db.SaveChanges();
    }

    [Fact]
    public async Task GetAll_projects_companies_ordered_by_code()
    {
        using var provider = BuildProvider();
        Seed(provider, ("0002", "LM"), ("0001", "CEAGUI"));

        var all = await provider.GetRequiredService<ICompanyCatalog>().GetAllAsync();

        Assert.Equal(["0001", "0002"], all.Select(c => c.Code));
        Assert.Equal("Server=.;Database=0002", all[1].ConnectionString);
    }

    [Fact]
    public async Task GetByCode_miss_reloads_once_and_finds_a_company_registered_after_the_first_load()
    {
        using var provider = BuildProvider();
        Seed(provider, ("0001", "CEAGUI"));
        var catalog = provider.GetRequiredService<ICompanyCatalog>();
        Assert.Empty((await catalog.GetAllAsync()).Where(c => c.Code == "0002"));

        Seed(provider, ("0002", "LM"));

        Assert.NotNull(await catalog.GetByCodeAsync("0002"));
        Assert.Null(await catalog.GetByCodeAsync("9999"));
    }

    [Fact]
    public async Task TryGetCached_only_reads_the_snapshot()
    {
        using var provider = BuildProvider();
        Seed(provider, ("0001", "CEAGUI"));
        var catalog = provider.GetRequiredService<ICompanyCatalog>();

        Assert.Null(catalog.TryGetCached("0001"));      // nada carregado ainda
        await catalog.GetAllAsync();
        Assert.NotNull(catalog.TryGetCached("0001"));
        Assert.NotNull(catalog.TryGetCached("0001".ToLowerInvariant()));
    }

    [Fact]
    public async Task Invalidate_forces_the_next_read_to_hit_the_database()
    {
        using var provider = BuildProvider();
        Seed(provider, ("0001", "CEAGUI"));
        var catalog = provider.GetRequiredService<ICompanyCatalog>();
        await catalog.GetAllAsync();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CommonDbContext>();
            var row = db.Companies.Single();
            row.CompanyName = "CEAGUI RENOMEADA";
            db.SaveChanges();
        }
        Assert.Equal("CEAGUI", (await catalog.GetAllAsync()).Single().CompanyName);   // ainda no cache

        catalog.Invalidate();

        Assert.Equal("CEAGUI RENOMEADA", (await catalog.GetAllAsync()).Single().CompanyName);
    }

    [Fact]
    public void Default_company_code_comes_from_configuration_or_0001()
    {
        using var provider = BuildProvider("0007");
        Assert.Equal("0007", provider.GetRequiredService<ICompanyCatalog>().DefaultCompanyCode);

        using var provider2 = BuildProvider();
        Assert.Equal("0001", provider2.GetRequiredService<ICompanyCatalog>().DefaultCompanyCode);
    }
}
```

- [ ] **Step 5: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Companies.Company" `
Expected: falha de compilação ("CompanyContext/CompanyCatalog/AddCompanyContext não existe").

- [ ] **Step 6: Implementar `CompanyCatalog`**

`SiagroB1.Infra/Companies/CompanyCatalog.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiagroB1.Infra.Context;

namespace SiagroB1.Infra.Companies;

/// <summary>
/// Singleton com snapshot de COMPANIES e TTL curto. Lê a COMMON por um escopo próprio porque o
/// <see cref="CommonDbContext"/> é scoped. Edição de connection string existente demora até
/// <see cref="Ttl"/> para chegar aos outros processos (Gateway/Reports); empresa NOVA é imediata
/// porque o miss recarrega.
/// </summary>
public sealed class CompanyCatalog(IServiceScopeFactory scopeFactory, IConfiguration configuration) : ICompanyCatalog
{
    public const string DefaultCodeConfigurationKey = "Company:Code";
    public const string FallbackDefaultCode = "0001";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyDictionary<string, CompanyInfo> _snapshot =
        new Dictionary<string, CompanyInfo>(StringComparer.OrdinalIgnoreCase);
    private DateTime _loadedAt = DateTime.MinValue;

    public string DefaultCompanyCode =>
        (configuration[DefaultCodeConfigurationKey] ?? FallbackDefaultCode).Trim().ToUpperInvariant();

    public async Task<IReadOnlyList<CompanyInfo>> GetAllAsync(CancellationToken ct = default)
    {
        await EnsureFreshAsync(force: false, ct);
        return _snapshot.Values.OrderBy(c => c.Code, StringComparer.Ordinal).ToList();
    }

    public async Task<CompanyInfo?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        await EnsureFreshAsync(force: false, ct);
        if (_snapshot.TryGetValue(code, out var hit)) return hit;

        await EnsureFreshAsync(force: true, ct);
        return _snapshot.TryGetValue(code, out hit) ? hit : null;
    }

    public CompanyInfo? TryGetCached(string code) => _snapshot.TryGetValue(code, out var hit) ? hit : null;

    public void Invalidate() => _loadedAt = DateTime.MinValue;

    private async Task EnsureFreshAsync(bool force, CancellationToken ct)
    {
        if (!force && DateTime.UtcNow - _loadedAt < Ttl) return;

        await _gate.WaitAsync(ct);
        try
        {
            if (!force && DateTime.UtcNow - _loadedAt < Ttl) return;

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CommonDbContext>();

            var rows = await db.Companies.AsNoTracking()
                .Select(c => new CompanyInfo(c.Code, c.CompanyName, c.ConnectionString))
                .ToListAsync(ct);

            _snapshot = rows.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
            _loadedAt = DateTime.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }
}
```

- [ ] **Step 7: Implementar `CompanyContext` e a extensão de DI**

`SiagroB1.Infra/Companies/CompanyContext.cs`:
```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SiagroB1.Infra.Companies;

public sealed class CompanyContext(
    ICompanyCatalog catalog,
    IConfiguration configuration,
    ILogger<CompanyContext> logger) : ICompanyContext
{
    public const string FallbackConnectionStringName = "SiagroDB";

    private CompanyInfo? _explicit;
    private bool _consumed;

    public CompanyInfo? Current => _explicit ?? ResolveAmbient();

    public bool IsResolved => Current is not null;

    public string CompanyCode => Current?.Code ?? catalog.DefaultCompanyCode;

    public string CompanyName => Current?.CompanyName ?? configuration["CompanyName"] ?? string.Empty;

    public string ConnectionString
    {
        get
        {
            _consumed = true;
            return Current?.ConnectionString
                   ?? configuration.GetConnectionString(FallbackConnectionStringName)
                   ?? throw new InvalidOperationException(
                       "Nenhuma empresa resolvida e ConnectionStrings:SiagroDB ausente na configuração.");
        }
    }

    public void Set(CompanyInfo company)
    {
        // Pega bug de ORDEM: alguém resolveu o AppDbContext deste escopo antes de a empresa ser
        // definida. O contexto já nasceu com a conexão anterior e não muda mais.
        if (_consumed && !string.Equals(_explicit?.Code, company.Code, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Empresa {To} definida depois de a conexão deste escopo já ter sido resolvida ({From}). " +
                "O AppDbContext deste escopo continua na conexão anterior.",
                company.Code, _explicit?.Code ?? "fallback");
        }

        _explicit = company;
    }

    private CompanyInfo? ResolveAmbient()
    {
        var code = CompanyAmbient.CompanyCode;
        if (code is null) return null;

        // Jobs: o filtro só conhece o código. Cache frio (processo recém-subido) carrega de forma
        // síncrona — worker do Hangfire não tem SynchronizationContext, então não há deadlock.
        return catalog.TryGetCached(code)
               ?? catalog.GetByCodeAsync(code).GetAwaiter().GetResult()
               ?? throw new InvalidOperationException(
                   $"A empresa {code} do job não está cadastrada em COMPANIES. O job não pode rodar no banco padrão.");
    }
}
```

`SiagroB1.Infra/Companies/CompanyServiceCollectionExtensions.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;

namespace SiagroB1.Infra.Companies;

public static class CompanyServiceCollectionExtensions
{
    /// <summary>Catálogo (singleton) + contexto da execução (scoped). Registrar nos três hosts.</summary>
    public static IServiceCollection AddCompanyContext(this IServiceCollection services)
    {
        services.AddSingleton<ICompanyCatalog, CompanyCatalog>();
        services.AddScoped<ICompanyContext, CompanyContext>();
        return services;
    }
}
```

Se `Microsoft.Extensions.Logging`/`Configuration` não resolverem no projeto Infra, adicione `<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.1" />` e `Microsoft.Extensions.Configuration.Abstractions` ao `SiagroB1.Infra.csproj` (mesma versão major do EF já usado).

- [ ] **Step 8: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Companies.Company"`
Expected: todos PASS.

- [ ] **Step 9: Commit**

```bash
git add SiagroB1.Infra/Companies SiagroB1.Application.Tests/Companies SiagroB1.Application.Tests/Support/FakeCompanyContext.cs SiagroB1.Application.Tests/Support/FakeCompanyCatalog.cs
git commit -m "feat(platform): contexto de empresa por execução e catálogo de COMPANIES

Base da seleção de empresa no login (spec 2026-10-16): a conexão do banco de
negócio deixa de ser fixa por processo e passa a ser resolvida por execução
(sessão HTTP, job do Hangfire ou fallback da configuração).

Atenção: empresa ambiente desconhecida LANÇA de propósito — um job no banco
errado é pior que um job falho.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: Schema — `CompanyCode` em sessões e perfis, `COMPANIES` ajustada

**Files:**
- Modify: `SiagroB1.Domain/Entities/Common/Company.cs`
- Modify: `SiagroB1.Domain/Entities/Common/UserSession.cs`
- Modify: `SiagroB1.Domain/Entities/Common/UserProfile.cs`
- Create (gerado): `SiagroB1.Migrations/CommonContext/<timestamp>_AddCompanyCodeToUserSessionsAndUsersProfiles.cs` (+ `.Designer.cs`, snapshot atualizado)

**Interfaces:**
- Produces: `Company.SapCompanyDb` anulável; `UserSession.CompanyCode: string?`; `UserProfile.CompanyCode: string?` + nav `Company`.

- [ ] **Step 1: Alterar as entidades**

`Company.cs` — trocar os dois últimos atributos:
```csharp
    /// <summary>String de conexão do banco de negócio desta empresa (informada no cadastro, como no SAP B1).</summary>
    [Column(TypeName = "VARCHAR(500) NOT NULL")]
    public required string ConnectionString { get; set; }

    /// <summary>Reservado: banco da empresa no SAP B1. Não é usado hoje (o modo SAPB1 continua por deploy).</summary>
    [Column(TypeName = "VARCHAR(100)")]
    public string? SapCompanyDb { get; set; }
```

`UserSession.cs` — acrescentar após `BranchCode`:
```csharp
    /// <summary>
    /// Empresa escolhida no login. Sem FK de propósito: sessão é histórico e não pode impedir a
    /// exclusão de uma empresa. Nulo só em sessões anteriores ao recurso (o middleware cai na padrão).
    /// </summary>
    [Column(TypeName = "VARCHAR(4)")]
    public string? CompanyCode { get; set; }
```

`UserProfile.cs` — índice e coluna:
```csharp
[Table("USERS_PROFILES")]
[Index(nameof(UserId), nameof(ProfileCode), nameof(CompanyCode), IsUnique = true)]
public class UserProfile
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public virtual User? User { get; set; }

    [Column(TypeName = "VARCHAR(50)")]
    [ForeignKey(nameof(Profile))]
    public required string ProfileCode { get; set; }

    public virtual Profile? Profile { get; set; }

    /// <summary>
    /// Empresa em que o perfil vale. Ter ao menos um perfil numa empresa é o que autoriza o login nela
    /// (admin passa por cima). Anulável no schema porque é o bootstrap do startup — não a migration —
    /// que conhece a empresa padrão; os serviços sempre preenchem.
    /// </summary>
    [Column(TypeName = "VARCHAR(4)")]
    [ForeignKey(nameof(Company))]
    public string? CompanyCode { get; set; }

    public virtual Company? Company { get; set; }
}
```

- [ ] **Step 2: Gerar a migration**

Run (PowerShell):
```powershell
$env:ASPNETCORE_ENVIRONMENT="Ceagui-Development"
dotnet ef migrations add AddCompanyCodeToUserSessionsAndUsersProfiles --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```
Expected: arquivo criado em `SiagroB1.Migrations/CommonContext/`.

- [ ] **Step 3: Revisar o `Up()` gerado**

Deve conter exatamente (ordem pode variar): `AddColumn CompanyCode` em `USER_SESSIONS` e `USERS_PROFILES` (VARCHAR(4), nullable); `DropIndex IX_USERS_PROFILES_UserId_ProfileCode`; `CreateIndex IX_USERS_PROFILES_UserId_ProfileCode_CompanyCode` unique; `CreateIndex IX_USERS_PROFILES_CompanyCode`; `AddForeignKey FK_USERS_PROFILES_COMPANIES_CompanyCode` (`onDelete: ReferentialAction.Restrict` ou `NoAction` — manter o que foi gerado, ver memória "Restrict e NoAction não são intercambiáveis"); `AlterColumn ConnectionString` para `VARCHAR(500)`; `AlterColumn SapCompanyDb` nullable. **Não** pode haver `AlterColumn` em `Code`. Se houver, reverta o atributo que o causou. Remova qualquer `DropIndex` anterior que o SQL Server recuse por o índice único antigo ter filtro — não há filtro aqui, então não deve ser o caso.

- [ ] **Step 4: Compilar**

Run: `dotnet build SiagroB1.sln`
Expected: sucesso (Reports/Gateway também compilam, pois só a entidade mudou).

- [ ] **Step 5: Aplicar no banco local da CEAGUI e confirmar**

Run (PowerShell):
```powershell
$env:ASPNETCORE_ENVIRONMENT="Ceagui-Development"
dotnet ef database update --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```
Expected: `Applying migration '..._AddCompanyCodeToUserSessionsAndUsersProfiles'. Done.`

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Domain/Entities/Common/Company.cs SiagroB1.Domain/Entities/Common/UserSession.cs SiagroB1.Domain/Entities/Common/UserProfile.cs SiagroB1.Migrations/CommonContext
git commit -m "feat(security): empresa na sessão e no perfil do usuário

A sessão passa a registrar em que empresa o usuário entrou, e cada perfil
atribuído passa a valer para UMA empresa — é isso que define onde o usuário
pode logar (spec 2026-10-16). COMPANIES ganha espaço para a connection string
completa e SapCompanyDb vira anulável de fato.

Atenção: CompanyCode nasce anulável porque quem preenche as linhas antigas é o
bootstrap do startup, não a migration (só ele conhece a empresa padrão).

DB: AddCompanyCodeToUserSessionsAndUsersProfiles

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: DI por host — `AppDbContext`, Dapper e interceptors resolvem a conexão pela empresa

**Files:**
- Modify: `SiagroB1.Web/Program.cs:53-83` (registro dos contextos e do `IDbConnection`)
- Modify: `SiagroB1.Gateway/Program.cs:22-38`
- Modify: `SiagroB1.Reports/Program.cs:24-73`
- Test: `SiagroB1.Application.Tests/Companies/AppDbContextPerCompanyScopeTests.cs`

**Interfaces:**
- Consumes: `ICompanyContext.ConnectionString`, `AddCompanyContext()` (Task 1).
- Produces: nos três hosts, `AppDbContext` e `IDbConnection` abrem a conexão da empresa do escopo; interceptors registrados como singletons.

- [ ] **Step 1: Escrever o teste do mecanismo (falha enquanto o padrão não é aplicado — serve para provar a avaliação por escopo)**

`SiagroB1.Application.Tests/Companies/AppDbContextPerCompanyScopeTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Companies;

/// <summary>
/// Prova o mecanismo que o Program.cs dos três hosts usa: AddDbContext com o overload (sp, options)
/// avalia as opções POR ESCOPO, então cada escopo abre o banco da sua empresa. Aqui o "banco" é um
/// InMemory nomeado pela connection string.
/// </summary>
public class AppDbContextPerCompanyScopeTests
{
    private static readonly CompanyInfo A = new("0001", "A", "db-a-" + Guid.NewGuid());
    private static readonly CompanyInfo B = new("0002", "B", "db-b-" + Guid.NewGuid());
    private static readonly string Fallback = "db-fallback-" + Guid.NewGuid();

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TestConfiguration.With(("ConnectionStrings:SiagroDB", Fallback)));
        services.AddSingleton<ICompanyCatalog>(new FakeCompanyCatalog(A, B));
        services.AddScoped<ICompanyContext, CompanyContext>();
        services.AddDbContext<AppDbContext>((sp, options) =>
            options.UseInMemoryDatabase(sp.GetRequiredService<ICompanyContext>().ConnectionString));
        return services.BuildServiceProvider();
    }

    private static int CountCountries(ServiceProvider provider, CompanyInfo? company, Action<AppDbContext>? act = null)
    {
        using var scope = provider.CreateScope();
        if (company is not null)
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        act?.Invoke(db);
        return db.Countries.Count();
    }

    [Fact]
    public void Each_scope_opens_the_database_of_its_company()
    {
        using var provider = Build();

        CountCountries(provider, A, db =>
        {
            db.Countries.Add(new Country { Code = "BR", Name = "Brasil", BacenCode = "1058" });
            db.SaveChanges();
        });

        Assert.Equal(1, CountCountries(provider, A));
        Assert.Equal(0, CountCountries(provider, B));
        Assert.Equal(0, CountCountries(provider, null));   // fallback da configuração
    }
}
```

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~AppDbContextPerCompanyScopeTests"`
Expected: PASS já nesta etapa (o teste não depende do Program.cs). Ele fica como guarda do padrão.

- [ ] **Step 2: Web — trocar o registro do `AppDbContext`, do `IDbConnection` e dos interceptors**

Em `SiagroB1.Web/Program.cs`, adicionar `using SiagroB1.Infra.Companies;` e substituir o bloco das linhas 63-83 por:
```csharp
builder.Services.AddCompanyContext();

// Singletons de propósito: com o overload (sp, options) o lambda roda a cada ESCOPO, e um `new`
// de interceptor por escopo mudaria o hash das opções e faria o EF montar um service provider
// interno por requisição (ManyServiceProvidersCreatedWarning).
builder.Services.AddSingleton<FinishedContractMutationGuardInterceptor>();
builder.Services.AddSingleton(new PurchaseFiscalReconciliationCleanupInterceptor(ErpMode.IsStandalone(builder.Configuration)));

// A connection string vem da EMPRESA do escopo (sessão ou job); sem empresa resolvida cai em
// ConnectionStrings:SiagroDB — é o que o `dotnet ef` e o guard de migrations usam.
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    {
        options.UseSqlServer(
            sp.GetRequiredService<ICompanyContext>().ConnectionString,
            b =>
            {
                b.MigrationsAssembly("SiagroB1.Migrations");
                b.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });
        options.AddInterceptors(
            sp.GetRequiredService<FinishedContractMutationGuardInterceptor>(),
            sp.GetRequiredService<PurchaseFiscalReconciliationCleanupInterceptor>());
        options.EnableSensitiveDataLogging();
    }
);

builder.Services.AddScoped<IDbConnection>(sp =>
    new SqlConnection(sp.GetRequiredService<ICompanyContext>().ConnectionString));
```
Verifique se `FinishedContractMutationGuardInterceptor` tem construtor sem parâmetros (`SiagroB1.Infra/Interceptors/`); se tiver dependências, registre-as antes.

- [ ] **Step 3: Gateway — mesmo padrão**

Em `SiagroB1.Gateway/Program.cs`, adicionar `using SiagroB1.Infra.Companies;` e substituir o `AddDbContext<AppDbContext>` (linhas 31-38) por:
```csharp
builder.Services.AddCompanyContext();

// O Gateway abre o banco de negócio só para a filial da sessão (BranchService): a empresa vem do
// cookie, resolvida pelo CookieAuthMiddleware antes de o controller ser ativado.
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseSqlServer(
        sp.GetRequiredService<ICompanyContext>().ConnectionString,
        b =>
        {
            b.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        })
);
```

- [ ] **Step 4: Reports — mesmo padrão + `IDbConnection`**

Em `SiagroB1.Reports/Program.cs`, adicionar `using SiagroB1.Infra.Companies;`, substituir o `AddDbContext<AppDbContext>` (linhas 33-43) por:
```csharp
builder.Services.AddCompanyContext();

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    {
        options.UseSqlServer(
            sp.GetRequiredService<ICompanyContext>().ConnectionString,
            b =>
            {
                b.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });
        options.EnableSensitiveDataLogging();
    }
);
```
e o `AddScoped<IDbConnection>` (linhas 68-73) por:
```csharp
builder.Services.AddScoped<IDbConnection>(sp =>
    new SqlConnection(sp.GetRequiredService<ICompanyContext>().ConnectionString));
```
(mantém `System.Data.SqlClient`, que é o que o Reports já usa.)

- [ ] **Step 5: Compilar e subir o Web para checar o aviso do EF**

Run: `dotnet build SiagroB1.sln`
Expected: sucesso.

Run (PowerShell, em segundo plano por ~20 s): `dotnet run --project SiagroB1.Web --launch-profile ceagui`
Expected: sobe normalmente (ainda sem empresas: tudo cai no fallback) e o log **não** contém `ManyServiceProvidersCreatedWarning`. Encerre o processo pelo PID.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Web/Program.cs SiagroB1.Gateway/Program.cs SiagroB1.Reports/Program.cs SiagroB1.Application.Tests/Companies/AppDbContextPerCompanyScopeTests.cs
git commit -m "feat(platform): AppDbContext e Dapper abrem o banco da empresa do escopo

Nos três hosts a conexão do banco de negócio deixa de ser lida da configuração
no startup e passa a vir do ICompanyContext do escopo (spec 2026-10-16). Sem
empresa resolvida o comportamento é o de antes (ConnectionStrings:SiagroDB).

Atenção: os interceptors viraram singletons porque o lambda (sp, options) roda
por escopo — um new por escopo faria o EF criar um provider interno por request.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: Resolução por requisição — `CookieAuthMiddleware` e `BasicAuthenticationHandler`

**Files:**
- Modify: `SiagroB1.Security/Middlewares/CookieAuthMiddleware.cs`
- Modify: `SiagroB1.Security/Authentication/BasicAuthenticationHandler.cs`
- Create: `SiagroB1.Security/Shared/CompanyAccessPolicy.cs`
- Create: `SiagroB1.Security/Shared/CompanyClaims.cs`
- Test: `SiagroB1.Application.Tests/Security/CookieAuthMiddlewareCompanyTests.cs`
- Test: `SiagroB1.Application.Tests/Security/CompanyAccessPolicyTests.cs`

**Interfaces:**
- Consumes: `ICompanyCatalog`, `ICompanyContext` (Task 1); `UserSession.CompanyCode`, `UserProfile.CompanyCode` (Task 2).
- Produces:
  - `static class CompanyClaims { const string CompanyCode = "CompanyCode"; }`
  - `static class CompanyAccessPolicy { Task<bool> CanLoginAsync(CommonDbContext db, User user, string companyCode, CancellationToken ct = default); string NoAccessMessage(string companyName); }`
  - O middleware grava a empresa da sessão em `context.RequestServices.GetRequiredService<ICompanyContext>()` e adiciona a claim `CompanyCode`.

- [ ] **Step 1: Escrever o teste da política de acesso (falha)**

`SiagroB1.Application.Tests/Security/CompanyAccessPolicyTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Shared;

namespace SiagroB1.Application.Tests.Security;

/// <summary>Entra na empresa quem é admin ou tem ao menos um perfil nela. Não há tabela de acesso à parte.</summary>
public class CompanyAccessPolicyTests
{
    private static CommonDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<CommonDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task User_with_a_profile_in_the_company_can_login_there_and_nowhere_else()
    {
        using var db = CreateDb();
        var user = new User { Username = "joao", FullName = "João" };
        db.Users.Add(user);
        db.Profiles.Add(new Profile { Code = "OPER", Description = "Operador" });
        db.UserProfiles.Add(new UserProfile { UserId = user.Id, ProfileCode = "OPER", CompanyCode = "0002" });
        await db.SaveChangesAsync();

        Assert.True(await CompanyAccessPolicy.CanLoginAsync(db, user, "0002"));
        Assert.False(await CompanyAccessPolicy.CanLoginAsync(db, user, "0001"));
    }

    [Fact]
    public async Task Admin_can_login_anywhere_even_without_profiles()
    {
        using var db = CreateDb();
        var admin = new User { Username = "admin", FullName = "Admin", IsAdmin = true };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        Assert.True(await CompanyAccessPolicy.CanLoginAsync(db, admin, "0002"));
    }

    [Fact]
    public void Message_names_the_company()
    {
        Assert.Equal("Usuário sem acesso à empresa LM CEREAIS.", CompanyAccessPolicy.NoAccessMessage("LM CEREAIS"));
    }
}
```

- [ ] **Step 2: Escrever o teste do middleware (falha)**

`SiagroB1.Application.Tests/Security/CookieAuthMiddlewareCompanyTests.cs`:
```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Middlewares;
using SiagroB1.Security.Shared;

namespace SiagroB1.Application.Tests.Security;

/// <summary>
/// O middleware é quem transforma o cookie de sessão em identidade nos três hosts; agora também
/// publica a EMPRESA da sessão no escopo da requisição — e tem de ser no escopo da requisição
/// (RequestServices), não no escopo interno que ele abre para ler a COMMON.
/// </summary>
public class CookieAuthMiddlewareCompanyTests
{
    private static readonly CompanyInfo A = new("0001", "CEAGUI", "Server=.;Database=A");
    private static readonly CompanyInfo B = new("0002", "LM CEREAIS", "Server=.;Database=B");

    private readonly string _dbName = Guid.NewGuid().ToString();

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TestConfiguration.With(("ConnectionStrings:SiagroDB", "Server=.;Database=FALLBACK")));
        services.AddDbContext<CommonDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddSingleton<ICompanyCatalog>(new FakeCompanyCatalog(A, B));
        services.AddScoped<ICompanyContext, CompanyContext>();
        return services.BuildServiceProvider();
    }

    private static string SeedSession(ServiceProvider provider, string? companyCode)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommonDbContext>();
        var user = new User { Username = "joao", FullName = "João" };
        db.Users.Add(user);
        var sessionId = Guid.NewGuid().ToString();
        db.UserSessions.Add(new UserSession
        {
            SessionId = sessionId, UserId = user.Id, IsActive = true,
            ExpiresAt = DateTime.UtcNow.AddHours(1), CompanyCode = companyCode
        });
        db.SaveChanges();
        return sessionId;
    }

    private static async Task<(HttpContext Http, ICompanyContext Company)> Invoke(ServiceProvider provider, string sessionId)
    {
        var scope = provider.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Headers.Cookie = $"SIAGROB1.Session={sessionId}";

        var middleware = new CookieAuthMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(http, provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ICompanyCatalog>());

        return (http, scope.ServiceProvider.GetRequiredService<ICompanyContext>());
    }

    [Fact]
    public async Task Session_company_is_published_in_the_request_scope_and_as_a_claim()
    {
        using var provider = BuildProvider();
        var sessionId = SeedSession(provider, "0002");

        var (http, company) = await Invoke(provider, sessionId);

        Assert.True(http.User.Identity!.IsAuthenticated);
        Assert.Equal("0002", company.Current?.Code);
        Assert.Equal("Server=.;Database=B", company.ConnectionString);
        Assert.Equal("0002", http.User.FindFirst(CompanyClaims.CompanyCode)?.Value);
    }

    [Fact]
    public async Task Legacy_session_without_company_uses_default_company()
    {
        using var provider = BuildProvider();
        var sessionId = SeedSession(provider, null);

        var (_, company) = await Invoke(provider, sessionId);

        Assert.Equal("0001", company.Current?.Code);
    }

    [Fact]
    public async Task Unknown_cookie_leaves_the_request_unresolved()
    {
        using var provider = BuildProvider();

        var (http, company) = await Invoke(provider, Guid.NewGuid().ToString());

        Assert.False(http.User?.Identity?.IsAuthenticated ?? false);
        Assert.False(company.IsResolved);
    }
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~CompanyAccessPolicyTests|FullyQualifiedName~CookieAuthMiddlewareCompanyTests"`
Expected: falha de compilação (`CompanyAccessPolicy`, `CompanyClaims`, assinatura do `InvokeAsync`).

- [ ] **Step 4: Implementar `CompanyClaims` e `CompanyAccessPolicy`**

`SiagroB1.Security/Shared/CompanyClaims.cs`:
```csharp
namespace SiagroB1.Security.Shared;

public static class CompanyClaims
{
    /// <summary>Só diagnóstico/log. Acesso a dados vai SEMPRE pelo ICompanyContext, nunca por esta claim.</summary>
    public const string CompanyCode = "CompanyCode";
}
```

`SiagroB1.Security/Shared/CompanyAccessPolicy.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;

namespace SiagroB1.Security.Shared;

/// <summary>
/// Quem pode entrar em qual empresa: administrador em todas; os demais, onde têm ao menos um
/// perfil atribuído. Derivado dos perfis de propósito — uma tabela de acesso à parte ficaria
/// dessincronizada, e um usuário sem perfil na empresa não faria nada nela mesmo.
/// </summary>
public static class CompanyAccessPolicy
{
    public static Task<bool> CanLoginAsync(CommonDbContext db, User user, string companyCode, CancellationToken ct = default) =>
        user.IsAdmin
            ? Task.FromResult(true)
            : db.UserProfiles.AnyAsync(up => up.UserId == user.Id && up.CompanyCode == companyCode, ct);

    public static string NoAccessMessage(string companyName) => $"Usuário sem acesso à empresa {companyName}.";
}
```

- [ ] **Step 5: Alterar o `CookieAuthMiddleware`**

Substituir `InvokeAsync` e `CreateUserPrincipal` (manter o resto do arquivo):
```csharp
    public async Task InvokeAsync(
        HttpContext context,
        IServiceScopeFactory scopeFactory,
        ICompanyCatalog catalog)
    {
        // Se já está autenticado, não precisa fazer nada
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            // Verificar se tem cookie de sessão
            if (context.Request.Cookies.TryGetValue("SIAGROB1.Session", out var sessionId))
            {
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<CommonDbContext>();

                // Buscar sessão válida
                var session = await dbContext.UserSessions
                    .Include(s => s.User)
                    .FirstOrDefaultAsync(s => s.SessionId == sessionId &&
                                            s.ExpiresAt > DateTime.UtcNow &&
                                            s.IsActive);

                if (session?.User != null)
                {
                    // Empresa da sessão. Sessão anterior ao recurso não tem CompanyCode: cai na padrão.
                    // Gravada no escopo da REQUISIÇÃO (RequestServices), não no escopo interno acima —
                    // é o escopo da requisição que vai resolver o AppDbContext do controller.
                    var company = await catalog.GetByCodeAsync(session.CompanyCode ?? catalog.DefaultCompanyCode);

                    if (company is not null)
                    {
                        context.RequestServices.GetRequiredService<ICompanyContext>().Set(company);
                    }

                    // Criar identidade do usuário
                    context.User = CreateUserPrincipal(session.User, company?.Code);

                    // Atualizar atividade
                    session.LastActivityAt = DateTime.UtcNow;
                    await dbContext.SaveChangesAsync();
                }
            }
        }

        await _next(context);
    }

    private static ClaimsPrincipal CreateUserPrincipal(User user, string? companyCode)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.GivenName, user.FullName),
            new("IsAdmin", user.IsAdmin.ToString()),
        };

        if (companyCode is not null)
        {
            claims.Add(new Claim(CompanyClaims.CompanyCode, companyCode));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookie"));
    }
```
Adicionar `using SiagroB1.Infra.Companies;` e `using SiagroB1.Security.Shared;`.

- [ ] **Step 6: Alterar o `BasicAuthenticationHandler`**

Construtor: acrescentar `ICompanyCatalog catalog, ICompanyContext companyContext` aos parâmetros primários (depois de `IHttpContextAccessor httpContextAccessor`).

No caminho **cookie** (`TryAuthenticateFromCookieAsync`), logo após `session.LastActivityAt = DateTime.UtcNow; await context.SaveChangesAsync();`:
```csharp
            var company = await catalog.GetByCodeAsync(session.CompanyCode ?? catalog.DefaultCompanyCode);
            if (company is not null)
            {
                companyContext.Set(company);
            }
```
e em `CreateUserClaims(session.User)` trocar para `CreateUserClaims(session.User, company?.Code)`.

No caminho **Basic** (`HandleAuthenticateAsync`), substituir o trecho entre `var user = await ValidateUserAsync(...)` / `if (user == null) {...}` e `await CreateSessionCookiesAsync(user);` por:
```csharp
            var user = await ValidateUserAsync(username, password);
            if (user == null)
            {
                _logger.LogWarning("Failed login attempt for user: {Username}", username);
                return AuthenticateResult.Fail("Invalid Username or Password");
            }

            // Empresa: header X-Company; sem header, só há resposta certa quando existe UMA empresa.
            var requestedCompany = httpContext?.Request.Headers["X-Company"].ToString();
            var company = await ResolveCompanyAsync(requestedCompany);
            if (company is null)
            {
                return AuthenticateResult.Fail("Informe a empresa no cabeçalho X-Company.");
            }

            if (!await CompanyAccessPolicy.CanLoginAsync(context, user, company.Code))
            {
                return AuthenticateResult.Fail(CompanyAccessPolicy.NoAccessMessage(company.CompanyName));
            }

            companyContext.Set(company);

            // ✅ 3. CRIAR COOKIES DE SESSÃO
            await CreateSessionCookiesAsync(user, company.Code);
```
e trocar `var claims = CreateUserClaims(user);` por `var claims = CreateUserClaims(user, company.Code);`.

Novo método privado:
```csharp
    private async Task<CompanyInfo?> ResolveCompanyAsync(string? requestedCode)
    {
        if (!string.IsNullOrWhiteSpace(requestedCode))
            return await catalog.GetByCodeAsync(requestedCode.Trim());

        var all = await catalog.GetAllAsync();
        return all.Count == 1 ? all[0] : null;
    }
```

`CreateSessionCookiesAsync(User user)` → `CreateSessionCookiesAsync(User user, string companyCode)` e, no `new UserSession { ... }`, acrescentar `CompanyCode = companyCode`. Apague o bloco comentado "✅ 5. Se usuário tem empresa padrão".

`CreateUserClaims(User user)` → `CreateUserClaims(User user, string? companyCode)`; apague o bloco comentado de `UserCompanyAccesses`/`DefaultCompanyCode` e acrescente ao final, antes do `return claims;`:
```csharp
        if (companyCode is not null)
        {
            claims.Add(new Claim(CompanyClaims.CompanyCode, companyCode));
        }
```
Adicionar `using SiagroB1.Infra.Companies;`.

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~CompanyAccessPolicyTests|FullyQualifiedName~CookieAuthMiddlewareCompanyTests"`
Expected: PASS. Depois `dotnet build SiagroB1.sln` — sucesso (o handler é registrado por DI, os novos parâmetros resolvem via `AddCompanyContext()` da Task 3).

- [ ] **Step 8: Commit**

```bash
git add SiagroB1.Security/Middlewares/CookieAuthMiddleware.cs SiagroB1.Security/Authentication/BasicAuthenticationHandler.cs SiagroB1.Security/Shared/CompanyAccessPolicy.cs SiagroB1.Security/Shared/CompanyClaims.cs SiagroB1.Application.Tests/Security/CookieAuthMiddlewareCompanyTests.cs SiagroB1.Application.Tests/Security/CompanyAccessPolicyTests.cs
git commit -m "feat(security): middleware de cookie publica a empresa da sessão no escopo

A empresa escolhida no login viaja na sessão; o middleware que já valida o
cookie nos três hosts agora resolve a empresa e a entrega ao escopo da
requisição, de onde o AppDbContext lê a conexão (spec 2026-10-16). O acesso
deriva dos perfis: admin em todas, os demais onde têm perfil.

Atenção: a empresa vai em RequestServices, não no escopo interno que o
middleware abre para ler a COMMON — nesse escopo ela não serviria para nada.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: Login com empresa; permissões, papéis e menu por empresa; endpoints do Gateway

**Files:**
- Modify: `SiagroB1.Security/Dtos/LoginRequest.cs`
- Modify: `SiagroB1.Security/Dtos/UserInfo.cs`
- Create: `SiagroB1.Security/Dtos/CompanyOption.cs`
- Modify: `SiagroB1.Security/Interfaces/IAuthService.cs`
- Modify: `SiagroB1.Security/Services/AuthService.cs`
- Modify: `SiagroB1.Security/Services/MenuService.cs`
- Modify: `SiagroB1.Application/Services/Security/UserPermissionsService.cs`
- Modify: `SiagroB1.Gateway/Controllers/AuthController.cs`
- Modify: `SiagroB1.Application.Tests/Security/UserPermissionsServiceTests.cs` (construtor + 1 teste novo)
- Modify: `SiagroB1.Application.Tests/Security/MenuServiceStandaloneOnlyTests.cs` (construtor + seed)
- Modify: `SiagroB1.Application.Tests/SalesContracts/SalesContractFiscalComplementServiceTests.cs` (construtor do `UserPermissionsService`, se usado lá)
- Test: `SiagroB1.Application.Tests/Security/AuthServiceCompanyTests.cs`
- Test: `SiagroB1.Application.Tests/Security/MenuServiceCompanyScopeTests.cs`

**Interfaces:**
- Consumes: `ICompanyCatalog`, `ICompanyContext`, `CompanyAccessPolicy`, `CompanyClaims`.
- Produces:
  - `LoginRequest.CompanyCode: string?`; `UserInfo.CompanyCode: string?`, `UserInfo.CompanyName: string?`; `CompanyOption { string Code; string CompanyName; }`
  - `IAuthService.LoginAsync(string username, string password, string? companyCode)`, `LoginWithBasicAuthAsync(string authorizationHeader, string? companyCode)`, `ListCompaniesAsync()`
  - `MenuService(CommonDbContext, IConfiguration, ICompanyContext)`; `UserPermissionsService(CommonDbContext, ICompanyContext)`
  - Gateway: `GET security/auth/companies` (anônimo) → `[{code, companyName}]`; `POST login` aceita `CompanyCode`; `status`/`info` devolvem `CompanyCode`/`CompanyName`.

- [ ] **Step 1: Atualizar os testes existentes para os novos construtores (ainda falham: construtores não existem)**

Em `UserPermissionsServiceTests.cs`: trocar todas as 9 ocorrências de `new UserPermissionsService(db)` por `new UserPermissionsService(db, new FakeCompanyContext("0001"))`, e em `GrantPermission` o `UserProfile` passa a ter `CompanyCode = "0001"`. Acrescentar `using SiagroB1.Application.Tests.Support;` e o teste:
```csharp
    [Fact]
    public async Task A_profile_in_another_company_does_not_leak_into_this_session()
    {
        using var db = CreateDb();
        GrantPermission(db, "joao", "WEIGHING_MANUAL_ENTRY");                 // perfil na 0001
        db.Roles.Add(new Role { Code = "SUPERVISOR" });
        db.RolesPermissions.Add(new RolePermission { RoleCode = "SUPERVISOR", PermissionCode = "OTHER_COMPANY_ONLY" });
        db.Profiles.Add(new Profile { Code = "SUP", Description = "Supervisor" });
        db.ProfileRoles.Add(new ProfileRole { ProfileCode = "SUP", RoleCode = "SUPERVISOR" });
        var userId = db.Users.Single(u => u.Username == "joao").Id;
        db.UserProfiles.Add(new UserProfile { UserId = userId, ProfileCode = "SUP", CompanyCode = "0002" });
        await db.SaveChangesAsync();

        var inCompany1 = new UserPermissionsService(db, new FakeCompanyContext("0001"));
        var inCompany2 = new UserPermissionsService(db, new FakeCompanyContext("0002"));

        Assert.Equal(["WEIGHING_MANUAL_ENTRY"], await inCompany1.GetAsync("joao"));
        Assert.False(await inCompany1.HasRoleAsync("joao", "SUPERVISOR"));
        Assert.Equal(["OTHER_COMPANY_ONLY"], await inCompany2.GetAsync("joao"));
        Assert.True(await inCompany2.HasRoleAsync("joao", "SUPERVISOR"));
    }
```

Em `MenuServiceStandaloneOnlyTests.cs`: no seed, `new UserProfile { UserId = UserId, ProfileCode = "P1", CompanyCode = "0001" }`; a construção vira `new MenuService(db, TaxTestServices.Config(erp), new FakeCompanyContext("0001"))`.

Procure outras construções: `grep -rn "new UserPermissionsService(\|new MenuService(" SiagroB1.Application.Tests` e ajuste do mesmo jeito.

- [ ] **Step 2: Escrever `MenuServiceCompanyScopeTests` (falha)**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Services;

namespace SiagroB1.Application.Tests.Security;

/// <summary>O menu da sessão vem só dos perfis da empresa em que o usuário entrou.</summary>
public class MenuServiceCompanyScopeTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static async Task<CommonDbContext> SeedAsync()
    {
        var db = new CommonDbContext(new DbContextOptionsBuilder<CommonDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        db.UserProfiles.Add(new UserProfile { UserId = UserId, ProfileCode = "COMPRAS", CompanyCode = "0001" });
        db.UserProfiles.Add(new UserProfile { UserId = UserId, ProfileCode = "ADMIN", CompanyCode = "0002" });
        db.ProfileRoles.Add(new ProfileRole { ProfileCode = "COMPRAS", RoleCode = "COMPRADOR" });
        db.ProfileRoles.Add(new ProfileRole { ProfileCode = "ADMIN", RoleCode = "ADMIN" });
        db.MenuItems.AddRange(
            new MenuItem { Key = "purchases", Title = "Compras", Order = 1 },
            new MenuItem { Key = "admin", Title = "Administração", Order = 2 });
        db.RolesMenus.AddRange(
            new RoleMenu { RoleCode = "COMPRADOR", MenuItemKey = "purchases" },
            new RoleMenu { RoleCode = "ADMIN", MenuItemKey = "admin" });
        await db.SaveChangesAsync();
        return db;
    }

    [Theory]
    [InlineData("0001", "purchases")]
    [InlineData("0002", "admin")]
    public async Task Menu_contains_only_the_items_of_the_session_company(string company, string expectedRoot)
    {
        var db = await SeedAsync();

        var menu = await new MenuService(db, TaxTestServices.Config("STANDALONE"), new FakeCompanyContext(company))
            .GetMenuAsync(UserId);

        Assert.Equal(expectedRoot, Assert.Single(menu.Navigation).Key);
    }
}
```

- [ ] **Step 3: Escrever `AuthServiceCompanyTests` (falha)**

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Services;
using SiagroB1.Security.Shared;

namespace SiagroB1.Application.Tests.Security;

/// <summary>
/// Login como no SAP B1: escolhe-se a empresa, depois usuário e senha. A sessão grava a empresa, e
/// permissões/papéis da resposta são só os da empresa escolhida.
/// </summary>
public class AuthServiceCompanyTests
{
    private static readonly CompanyInfo A = new("0001", "CEAGUI", "Server=.;Database=A");
    private static readonly CompanyInfo B = new("0002", "LM CEREAIS", "Server=.;Database=B");

    private readonly CommonDbContext _db = new(new DbContextOptionsBuilder<CommonDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FakeCompanyContext _companyContext = FakeCompanyContext.Unresolved();

    private AuthService CreateService(params CompanyInfo[] companies) =>
        new(_db,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            new FakeSapUserProvisioner(),
            NullLogger<AuthService>.Instance,
            new FakeCompanyCatalog(companies),
            _companyContext);

    private User SeedUser(string username, bool isAdmin = false, params string[] profileCompanies)
    {
        var user = new User { Username = username, FullName = username, IsAdmin = isAdmin, PasswordHash = PasswordHasher.Hash("senha") };
        _db.Users.Add(user);
        if (!_db.Profiles.Any(p => p.Code == "OPER"))
        {
            _db.Profiles.Add(new Profile { Code = "OPER", Description = "Operador" });
            _db.Roles.Add(new Role { Code = "OPERADOR" });
            _db.ProfileRoles.Add(new ProfileRole { ProfileCode = "OPER", RoleCode = "OPERADOR" });
            _db.Permissions.Add(new Permission { Code = "WEIGHING_MANUAL_ENTRY", Description = "x" });
            _db.RolesPermissions.Add(new RolePermission { RoleCode = "OPERADOR", PermissionCode = "WEIGHING_MANUAL_ENTRY" });
        }
        foreach (var company in profileCompanies)
            _db.UserProfiles.Add(new UserProfile { UserId = user.Id, ProfileCode = "OPER", CompanyCode = company });
        _db.SaveChanges();
        return user;
    }

    [Fact]
    public async Task User_without_profile_in_the_company_is_refused_with_the_company_name()
    {
        SeedUser("joao", profileCompanies: "0001");

        var result = await CreateService(A, B).LoginAsync("joao", "senha", "0002");

        Assert.False(result.Success);
        Assert.Equal("Usuário sem acesso à empresa LM CEREAIS.", result.Message);
        Assert.Empty(_db.UserSessions);
    }

    [Fact]
    public async Task Successful_login_stores_the_company_in_the_session_and_scopes_permissions()
    {
        SeedUser("joao", profileCompanies: "0002");

        var result = await CreateService(A, B).LoginAsync("joao", "senha", "0002");

        Assert.True(result.Success, result.Message);
        Assert.Equal("0002", _db.UserSessions.Single().CompanyCode);
        Assert.Equal("0002", result.User!.CompanyCode);
        Assert.Equal("LM CEREAIS", result.User.CompanyName);
        Assert.Equal(["WEIGHING_MANUAL_ENTRY"], result.User.Permissions);
        Assert.Equal(["OPERADOR"], result.User.Roles);
        Assert.Equal("0002", _companyContext.Current?.Code);
        Assert.Contains(result.Claims!, c => c.Type == CompanyClaims.CompanyCode && c.Value == "0002");
    }

    [Fact]
    public async Task Permissions_of_another_company_do_not_come_back()
    {
        SeedUser("joao", profileCompanies: ["0001", "0002"]);
        _db.Roles.Add(new Role { Code = "SUPERVISOR" });
        _db.Profiles.Add(new Profile { Code = "SUP", Description = "Sup" });
        _db.ProfileRoles.Add(new ProfileRole { ProfileCode = "SUP", RoleCode = "SUPERVISOR" });
        _db.UserProfiles.Add(new UserProfile { UserId = _db.Users.Single().Id, ProfileCode = "SUP", CompanyCode = "0001" });
        _db.SaveChanges();

        var result = await CreateService(A, B).LoginAsync("joao", "senha", "0002");

        Assert.DoesNotContain("SUPERVISOR", result.User!.Roles);
    }

    [Fact]
    public async Task Admin_logs_into_any_company()
    {
        SeedUser("admin", isAdmin: true);

        var result = await CreateService(A, B).LoginAsync("admin", "senha", "0002");

        Assert.True(result.Success, result.Message);
    }

    [Fact]
    public async Task Login_without_company_code_and_single_company_selects_it()
    {
        SeedUser("joao", profileCompanies: "0001");

        var result = await CreateService(A).LoginAsync("joao", "senha", null);

        Assert.True(result.Success, result.Message);
        Assert.Equal("0001", _db.UserSessions.Single().CompanyCode);
    }

    [Fact]
    public async Task Login_without_company_code_and_several_companies_asks_for_one()
    {
        SeedUser("joao", profileCompanies: "0001");

        var result = await CreateService(A, B).LoginAsync("joao", "senha", null);

        Assert.False(result.Success);
        Assert.Equal("Selecione a empresa.", result.Message);
    }

    [Fact]
    public async Task Unknown_company_code_is_refused_before_checking_credentials()
    {
        var result = await CreateService(A, B).LoginAsync("ninguem", "x", "9999");

        Assert.False(result.Success);
        Assert.Equal("Empresa inválida.", result.Message);
    }

    [Fact]
    public async Task Wrong_password_keeps_the_generic_message()
    {
        SeedUser("joao", profileCompanies: "0001");

        var result = await CreateService(A).LoginAsync("joao", "errada", "0001");

        Assert.Equal("Usuário ou Senha inválidos", result.Message);
    }

    [Fact]
    public async Task ListCompanies_exposes_only_code_and_name_ordered_by_name()
    {
        var list = await CreateService(B, A).ListCompaniesAsync();

        Assert.Equal(["CEAGUI", "LM CEREAIS"], list.Select(c => c.CompanyName));
        Assert.Equal(["0001", "0002"], list.Select(c => c.Code));
    }

    [Fact]
    public async Task GetUserInfo_uses_the_company_of_the_request_scope()
    {
        SeedUser("joao", profileCompanies: ["0001", "0002"]);
        _companyContext.Set(B);

        var info = await CreateService(A, B).GetUserInfoAsync("joao");

        Assert.Equal("0002", info!.CompanyCode);
        Assert.Equal("LM CEREAIS", info.CompanyName);
    }
}
```
Confira o nome do método de hash em `SiagroB1.Security/Shared/PasswordHasher.cs` (`Hash(string)`); ajuste se for outro.

- [ ] **Step 4: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~AuthServiceCompanyTests|FullyQualifiedName~MenuServiceCompanyScopeTests|FullyQualifiedName~UserPermissionsServiceTests"`
Expected: falha de compilação.

- [ ] **Step 5: DTOs e interface**

`LoginRequest.cs` — acrescentar:
```csharp
    /// <summary>Empresa escolhida na tela de login. Nulo só entra quando há uma única empresa cadastrada.</summary>
    public string? CompanyCode { get; set; }
```
`UserInfo.cs` — acrescentar:
```csharp
    /// <summary>Empresa da sessão (código e nome). A shell mostra o nome; a tela de usuários usa o código como padrão do perfil.</summary>
    public string? CompanyCode { get; set; }
    public string? CompanyName { get; set; }
```
`CompanyOption.cs` (novo):
```csharp
namespace SiagroB1.Security.Dtos;

/// <summary>Item do seletor da tela de login. Só código e nome: o endpoint é anônimo.</summary>
public class CompanyOption
{
    public string Code { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
}
```
`IAuthService.cs`:
```csharp
public interface IAuthService
{
    Task<LoginResponse> LoginAsync(string username, string password, string? companyCode);
    Task<LoginResponse> LoginWithBasicAuthAsync(string authorizationHeader, string? companyCode);
    Task<bool> LogoutAsync(string sessionId);
    Task<UserInfo?> GetUserInfoAsync(string username);
    Task<IReadOnlyList<CompanyOption>> ListCompaniesAsync();
}
```

- [ ] **Step 6: `AuthService`**

Construtor: `AuthService(CommonDbContext db, IHttpContextAccessor httpContextAccessor, ISapUserProvisioner sapUserProvisioner, ILogger<AuthService> logger, ICompanyCatalog catalog, ICompanyContext companyContext)`. Adicionar `using SiagroB1.Infra.Companies;`.

`LoginAsync(string username, string password, string? companyCode)` — no início do `try`, antes do `EnsureAsync`:
```csharp
                var (company, companyError) = await ResolveLoginCompanyAsync(companyCode);
                if (company is null)
                {
                    return new LoginResponse { Success = false, Message = companyError! };
                }
```
Depois da verificação de senha (e do rehash), antes de "Criar sessão":
```csharp
                // Depois da senha, de propósito: antes dela, a mensagem revelaria que o usuário existe.
                if (!await CompanyAccessPolicy.CanLoginAsync(db, user, company.Code))
                {
                    return new LoginResponse { Success = false, Message = CompanyAccessPolicy.NoAccessMessage(company.CompanyName) };
                }
```
No `new UserSession { ... }` acrescentar `CompanyCode = company.Code,`.
Antes de `var claims = CreateClaims(user);`:
```csharp
                // Permissões, papéis e menu desta sessão são os da empresa escolhida.
                companyContext.Set(company);
```
`CreateClaims(user)` → `CreateClaims(user, company.Code)`; `ToUserInfo(user)` continua, mas em seguida `loggedUser.CompanyCode = company.Code; loggedUser.CompanyName = company.CompanyName;`.

`LoginWithBasicAuthAsync(string authorizationHeader, string? companyCode)` — o `return await LoginAsync(username, password);` vira `return await LoginAsync(username, password, companyCode);`.

`GetUserInfoAsync`: dentro do `if (info != null)`, acrescentar `info.CompanyCode = companyContext.Current?.Code; info.CompanyName = companyContext.Current?.CompanyName;`.

`GetRolesAsync`/`GetPermissionsAsync`: `where up.UserId == userId && up.CompanyCode == companyContext.CompanyCode`.

`CreateClaims(User user, string companyCode)`: acrescentar `new Claim(CompanyClaims.CompanyCode, companyCode)`.

Novos métodos:
```csharp
        public async Task<IReadOnlyList<CompanyOption>> ListCompaniesAsync()
        {
            var all = await catalog.GetAllAsync();
            return all
                .Select(c => new CompanyOption { Code = c.Code, CompanyName = c.CompanyName })
                .OrderBy(c => c.CompanyName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Empresa do login. Código informado tem de existir; sem código, só há resposta certa quando
        /// existe UMA empresa (frontend antigo em cache, login/basic sem X-Company).
        /// </summary>
        private async Task<(CompanyInfo? Company, string? Error)> ResolveLoginCompanyAsync(string? companyCode)
        {
            var companies = await catalog.GetAllAsync();

            if (!string.IsNullOrWhiteSpace(companyCode))
            {
                var found = companies.FirstOrDefault(c =>
                    string.Equals(c.Code, companyCode.Trim(), StringComparison.OrdinalIgnoreCase));
                return found is null ? (null, "Empresa inválida.") : (found, null);
            }

            return companies.Count switch
            {
                1 => (companies[0], null),
                0 => (null, "Nenhuma empresa cadastrada. Contate o administrador."),
                _ => (null, "Selecione a empresa."),
            };
        }
```

- [ ] **Step 7: `MenuService` e `UserPermissionsService`**

`MenuService(CommonDbContext context, IConfiguration configuration, ICompanyContext companyContext)`; na query: `where up.UserId == userId && up.CompanyCode == companyContext.CompanyCode`. `using SiagroB1.Infra.Companies;`.

`UserPermissionsService(CommonDbContext db, ICompanyContext companyContext)`; em `GetAsync` e `HasRoleAsync` acrescentar `&& up.CompanyCode == companyContext.CompanyCode` ao `where`. `IsAdminAsync` e `HasAsync` inalterados.

- [ ] **Step 8: Gateway `AuthController`**

Construtor: acrescentar `ICompanyContext companyContext` (e `using SiagroB1.Infra.Companies;`).

Novo endpoint, antes de `Login`:
```csharp
    /// <summary>Lista do seletor de empresa da tela de login. Anônimo e mínimo (código e nome).</summary>
    [HttpGet("companies")]
    public async Task<IActionResult> GetCompanies() => Ok(await authService.ListCompaniesAsync());
```
`Login`: `authService.LoginAsync(request.Username, request.Password, request.CompanyCode)`; no log de sucesso incluir a empresa: `logger.LogInformation("Login bem-sucedido para: {Username} na empresa {Company}", request.Username, result.User?.CompanyCode);`.

`LoginBasic`: assinatura `LoginBasic([FromHeader(Name = "Authorization")] string authorization, [FromHeader(Name = "X-Company")] string? companyCode)` e chamada `LoginWithBasicAuthAsync(authorization, companyCode)`.

`GetStatus`: nos dois objetos anônimos de resposta autenticada acrescentar `CompanyCode = userInfo.CompanyCode, CompanyName = userInfo.CompanyName,` (no segundo ramo: `userInfoFromDb?.CompanyCode`, `userInfoFromDb?.CompanyName`).

`GetSystemInfo`:
```csharp
            CompanyName = companyContext.Current?.CompanyName ?? configuration["CompanyName"] ?? "COMPANY NAME",
            CompanyCode = companyContext.Current?.Code,
```

- [ ] **Step 9: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Security"`
Expected: PASS em todos (inclusive `PasswordResetServiceTests` e outros já existentes). Depois `dotnet build SiagroB1.sln`.

- [ ] **Step 10: Commit**

```bash
git add SiagroB1.Security SiagroB1.Application/Services/Security/UserPermissionsService.cs SiagroB1.Gateway/Controllers/AuthController.cs SiagroB1.Application.Tests/Security SiagroB1.Application.Tests/SalesContracts
git commit -m "feat(security): login com seleção de empresa e perfis por empresa

Como no SAP B1: o usuário escolhe a empresa e então informa usuário e senha.
A sessão grava a empresa; permissões, papéis e menu passam a vir só dos
perfis daquela empresa; admin entra em qualquer uma (spec 2026-10-16).

Atenção: a checagem de acesso à empresa vem DEPOIS da senha — antes dela a
mensagem denunciaria que o usuário existe.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: Bootstrap da empresa padrão e guard de migrations por empresa

**Files:**
- Create: `SiagroB1.Security/Startup/CompanyBootstrap.cs`
- Modify: `SiagroB1.Web/Startup/PendingMigrationsGuard.cs`
- Modify: `SiagroB1.Web/Program.cs` (bloco após `builder.Build()`)
- Modify: `SiagroB1.Gateway/Program.cs` (após `builder.Build()`)
- Test: `SiagroB1.Application.Tests/Security/CompanyBootstrapTests.cs`
- Modify: `SiagroB1.Application.Tests/Startup/PendingMigrationsGuardTests.cs` (1 teste novo)

**Interfaces:**
- Produces:
  - `static class CompanyBootstrap { const string CodeKey = "Company:Code"; const string NameKey = "Company:Name"; Task RunAsync(IServiceProvider services, CancellationToken ct = default); Task<CompanyBootstrapResult> RunAsync(CommonDbContext db, IConfiguration configuration, CancellationToken ct = default); }`
  - `record CompanyBootstrapResult(bool CreatedDefault, string DefaultCode, int ProfilesAssigned, int SessionsAssigned, string? Warning)`
  - `PendingMigrationsGuard.EnsureNoPendingCommonMigrationsAsync(WebApplication)` e `EnsureNoPendingCompanyMigrationsAsync(WebApplication)`; `Evaluate` inalterado.

- [ ] **Step 1: Escrever `CompanyBootstrapTests` (falha)**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Startup;

namespace SiagroB1.Application.Tests.Security;

/// <summary>
/// Deploys existentes têm COMPANIES vazia e perfis sem empresa. O bootstrap transforma a instalação
/// numa instalação de uma empresa sem passo manual, e é idempotente porque Web e Gateway o rodam.
/// </summary>
public class CompanyBootstrapTests
{
    private readonly CommonDbContext _db = new(new DbContextOptionsBuilder<CommonDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Microsoft.Extensions.Configuration.IConfiguration Config(string? code = null, string? name = null) =>
        TestConfiguration.With(
            ("ConnectionStrings:SiagroDB", "Server=.;Database=CEAGUI_SIAGRO_DEV"),
            ("CompanyName", "CEAGUI"),
            ("Company:Code", code),
            ("Company:Name", name));

    private Guid SeedUserWithProfile(string? companyCode)
    {
        var user = new User { Username = Guid.NewGuid().ToString("N"), FullName = "x" };
        _db.Users.Add(user);
        _db.UserProfiles.Add(new UserProfile { UserId = user.Id, ProfileCode = "P", CompanyCode = companyCode });
        _db.SaveChanges();
        return user.Id;
    }

    [Fact]
    public async Task Empty_catalog_registers_the_default_company_from_configuration_and_assigns_orphans()
    {
        var userId = SeedUserWithProfile(null);
        _db.UserSessions.Add(new UserSession { SessionId = "s1", UserId = userId, IsActive = true, ExpiresAt = DateTime.Now.AddHours(1) });
        _db.UserSessions.Add(new UserSession { SessionId = "s2", UserId = userId, IsActive = false, ExpiresAt = DateTime.Now.AddHours(-1) });
        _db.SaveChanges();

        var result = await CompanyBootstrap.RunAsync(_db, Config());

        Assert.True(result.CreatedDefault);
        var company = Assert.Single(_db.Companies);
        Assert.Equal("0001", company.Code);
        Assert.Equal("CEAGUI", company.CompanyName);
        Assert.Equal("Server=.;Database=CEAGUI_SIAGRO_DEV", company.ConnectionString);
        Assert.Equal("0001", _db.UserProfiles.Single().CompanyCode);
        Assert.Equal("0001", _db.UserSessions.Single(s => s.SessionId == "s1").CompanyCode);
        Assert.Null(_db.UserSessions.Single(s => s.SessionId == "s2").CompanyCode);   // inativa fica como está
        Assert.Equal(1, result.ProfilesAssigned);
        Assert.Equal(1, result.SessionsAssigned);
    }

    [Fact]
    public async Task Configured_code_and_name_are_used()
    {
        await CompanyBootstrap.RunAsync(_db, Config(code: "ceag", name: "CEAGUI COOPERATIVA"));

        var company = Assert.Single(_db.Companies);
        Assert.Equal("CEAG", company.Code);
        Assert.Equal("CEAGUI COOPERATIVA", company.CompanyName);
    }

    [Fact]
    public async Task Second_run_is_a_no_op()
    {
        await CompanyBootstrap.RunAsync(_db, Config());

        var result = await CompanyBootstrap.RunAsync(_db, Config());

        Assert.False(result.CreatedDefault);
        Assert.Single(_db.Companies);
        Assert.Equal(0, result.ProfilesAssigned);
    }

    [Fact]
    public async Task Non_empty_catalog_only_assigns_orphans_to_the_configured_code_when_it_exists()
    {
        _db.Companies.Add(new Company { Code = "0001", CompanyName = "A", ConnectionString = "a" });
        _db.Companies.Add(new Company { Code = "0002", CompanyName = "B", ConnectionString = "b" });
        _db.SaveChanges();
        SeedUserWithProfile(null);
        SeedUserWithProfile("0002");

        var result = await CompanyBootstrap.RunAsync(_db, Config(code: "0002"));

        Assert.False(result.CreatedDefault);
        Assert.Equal(2, _db.Companies.Count());
        Assert.Equal(2, _db.UserProfiles.Count(p => p.CompanyCode == "0002"));
        Assert.Null(result.Warning);
    }

    [Fact]
    public async Task Configured_code_missing_from_a_non_empty_catalog_falls_back_to_the_first_and_warns()
    {
        _db.Companies.Add(new Company { Code = "0005", CompanyName = "E", ConnectionString = "e" });
        _db.SaveChanges();
        SeedUserWithProfile(null);

        var result = await CompanyBootstrap.RunAsync(_db, Config(code: "0001"));

        Assert.Equal("0005", result.DefaultCode);
        Assert.NotNull(result.Warning);
        Assert.Equal("0005", _db.UserProfiles.Single().CompanyCode);
    }
}
```

- [ ] **Step 2: Acrescentar um teste ao `PendingMigrationsGuardTests` (falha: a mensagem ainda não cita `--connection`)**

```csharp
    [Fact]
    public void Evaluate_WithPerCompanyKeys_NamesTheCompanyInTheMessage()
    {
        var decision = PendingMigrationsGuard.Evaluate(
            Pending(("AppDbContext[0001]", []), ("AppDbContext[0002]", ["20261016_X"]), ("CommonDbContext", [])),
            allowPending: false);

        Assert.Equal(PendingMigrationsOutcome.Block, decision.Outcome);
        Assert.Contains("AppDbContext[0002]: 20261016_X", decision.Message);
        Assert.Contains("--connection", decision.Message);
    }
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~CompanyBootstrapTests|FullyQualifiedName~PendingMigrationsGuardTests"`
Expected: falha (`CompanyBootstrap` não existe; `--connection` ausente da mensagem).

- [ ] **Step 4: Implementar `CompanyBootstrap`**

`SiagroB1.Security/Startup/CompanyBootstrap.cs`:
```csharp
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Security.Startup;

public sealed record CompanyBootstrapResult(
    bool CreatedDefault, string DefaultCode, int ProfilesAssigned, int SessionsAssigned, string? Warning);

/// <summary>
/// Faz um deploy existente virar uma instalação de UMA empresa sem passo manual: com COMPANIES
/// vazia, cadastra a empresa padrão a partir da configuração (<c>Company:Code</c>, <c>Company:Name</c>
/// ou <c>CompanyName</c>, <c>ConnectionStrings:SiagroDB</c>) e atribui a ela os perfis e as sessões
/// ativas que ainda não têm empresa (linhas anteriores à migration).
///
/// Idempotente e tolerante a corrida: Web e Gateway sobem juntos e os dois rodam isto.
/// Sem <c>ExecuteUpdateAsync</c> de propósito: o provider InMemory dos testes não o suporta e as
/// linhas são poucas.
/// </summary>
public static class CompanyBootstrap
{
    public const string CodeKey = "Company:Code";
    public const string NameKey = "Company:Name";

    public static async Task RunAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(CompanyBootstrap));

        try
        {
            var result = await RunAsync(
                scope.ServiceProvider.GetRequiredService<CommonDbContext>(),
                scope.ServiceProvider.GetRequiredService<IConfiguration>(),
                ct);

            if (result.CreatedDefault)
                logger.LogInformation("Empresa padrão {Code} cadastrada a partir da configuração.", result.DefaultCode);

            if (result.ProfilesAssigned > 0 || result.SessionsAssigned > 0)
                logger.LogInformation("{Profiles} perfil(is) e {Sessions} sessão(ões) sem empresa atribuídos à {Code}.",
                    result.ProfilesAssigned, result.SessionsAssigned, result.DefaultCode);

            if (result.Warning is not null)
                logger.LogWarning("{Warning}", result.Warning);

            scope.ServiceProvider.GetRequiredService<ICompanyCatalog>().Invalidate();
        }
        catch (DbException ex)
        {
            // Mesma postura do guard de migrations: banco fora do ar no boot não derruba o host.
            logger.LogError(ex, "Bootstrap de empresas não executado (banco COMMON inacessível ou migration pendente). O boot segue.");
        }
    }

    public static async Task<CompanyBootstrapResult> RunAsync(
        CommonDbContext db, IConfiguration configuration, CancellationToken ct = default)
    {
        var configuredCode = (configuration[CodeKey] ?? CompanyCatalog.FallbackDefaultCode).Trim().ToUpperInvariant();
        var created = false;

        if (!await db.Companies.AnyAsync(ct))
        {
            var connectionString = configuration.GetConnectionString(CompanyContext.FallbackConnectionStringName)
                ?? throw new InvalidOperationException("ConnectionStrings:SiagroDB ausente: não há como cadastrar a empresa padrão.");

            db.Companies.Add(new Company
            {
                Code = configuredCode,
                CompanyName = configuration[NameKey] ?? configuration["CompanyName"] ?? configuredCode,
                ConnectionString = connectionString,
            });

            try
            {
                await db.SaveChangesAsync(ct);
                created = true;
            }
            catch (DbUpdateException)
            {
                // Web e Gateway correram; o outro venceu. Segue com o que está no banco.
                db.ChangeTracker.Clear();
                if (!await db.Companies.AnyAsync(ct)) throw;
            }
        }

        var codes = await db.Companies.Select(c => c.Code).ToListAsync(ct);
        string? warning = null;
        var defaultCode = codes.FirstOrDefault(c => string.Equals(c, configuredCode, StringComparison.OrdinalIgnoreCase));

        if (defaultCode is null)
        {
            defaultCode = codes.Order(StringComparer.Ordinal).First();
            warning = $"{CodeKey}={configuredCode} não existe em COMPANIES; perfis e sessões sem empresa foram atribuídos à {defaultCode}.";
        }

        var profiles = await db.UserProfiles.Where(p => p.CompanyCode == null).ToListAsync(ct);
        foreach (var profile in profiles) profile.CompanyCode = defaultCode;

        var sessions = await db.UserSessions.Where(s => s.CompanyCode == null && s.IsActive).ToListAsync(ct);
        foreach (var session in sessions) session.CompanyCode = defaultCode;

        if (profiles.Count > 0 || sessions.Count > 0)
            await db.SaveChangesAsync(ct);

        return new CompanyBootstrapResult(created, defaultCode, profiles.Count, sessions.Count, warning);
    }
}
```

- [ ] **Step 5: Dividir o `PendingMigrationsGuard`**

Substituir `EnsureNoPendingMigrationsAsync` e `PendingOf` por:
```csharp
    /// <summary>Só a COMMON — roda ANTES do bootstrap de empresas, que precisa das colunas novas dela.</summary>
    public static async Task EnsureNoPendingCommonMigrationsAsync(
        WebApplication app, CancellationToken cancellationToken = default)
    {
        var logger = CreateLogger(app);
        Dictionary<string, IReadOnlyList<string>> pending;

        try
        {
            using var scope = app.Services.CreateScope();
            pending = new Dictionary<string, IReadOnlyList<string>>
            {
                [nameof(CommonDbContext)] = await PendingOf<CommonDbContext>(scope, cancellationToken)
            };
        }
        catch (DbException ex)
        {
            logger.LogError(ex, "Não foi possível verificar as migrations pendentes da COMMON (banco inacessível). O boot segue.");
            return;
        }

        Decide(app, logger, pending);
    }

    /// <summary>
    /// O AppDbContext de CADA empresa cadastrada, mais o banco de ConnectionStrings:SiagroDB quando ele
    /// não está entre elas. Uma empresa inacessível só gera log — indisponibilidade não é o que esta
    /// trava protege.
    /// </summary>
    public static async Task EnsureNoPendingCompanyMigrationsAsync(
        WebApplication app, CancellationToken cancellationToken = default)
    {
        var logger = CreateLogger(app);
        var pending = new Dictionary<string, IReadOnlyList<string>>();

        List<CompanyInfo> targets;
        try
        {
            targets = [.. await app.Services.GetRequiredService<ICompanyCatalog>().GetAllAsync(cancellationToken)];
        }
        catch (DbException ex)
        {
            logger.LogError(ex, "Não foi possível ler as empresas para verificar as migrations (banco inacessível). O boot segue.");
            return;
        }

        var fallback = app.Configuration.GetConnectionString(CompanyContext.FallbackConnectionStringName);
        if (fallback is not null && !targets.Any(t => SameDatabase(t.ConnectionString, fallback)))
        {
            targets.Add(new CompanyInfo("CFG", "ConnectionStrings:SiagroDB", fallback));
        }

        foreach (var company in targets)
        {
            try
            {
                using var scope = app.Services.CreateScope();
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);
                pending[$"{nameof(AppDbContext)}[{company.Code}]"] = await PendingOf<AppDbContext>(scope, cancellationToken);
            }
            catch (DbException ex)
            {
                logger.LogError(ex, "Banco da empresa {Code} inacessível; o boot segue sem verificar as migrations dela.", company.Code);
            }
        }

        Decide(app, logger, pending);
    }

    private static void Decide(WebApplication app, ILogger logger, Dictionary<string, IReadOnlyList<string>> pending)
    {
        var decision = Evaluate(pending, app.Configuration.GetValue<bool>(AllowPendingConfigurationKey));

        switch (decision.Outcome)
        {
            case PendingMigrationsOutcome.Block:
                logger.LogCritical("{Message}", decision.Message);
                throw new InvalidOperationException(decision.Message);

            case PendingMigrationsOutcome.Warn:
                logger.LogWarning("{Message}", decision.Message);
                break;

            default:
                logger.LogInformation("Nenhuma migration pendente em {Contexts}.", string.Join(", ", pending.Keys));
                break;
        }
    }

    private static ILogger CreateLogger(WebApplication app) =>
        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(PendingMigrationsGuard));

    /// <summary>Mesmo servidor e mesmo catálogo; a comparação textual falharia por diferença de ordem ou caixa.</summary>
    private static bool SameDatabase(string a, string b)
    {
        try
        {
            var x = new SqlConnectionStringBuilder(a);
            var y = new SqlConnectionStringBuilder(b);
            return string.Equals(x.DataSource, y.DataSource, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.InitialCatalog, y.InitialCatalog, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task<IReadOnlyList<string>> PendingOf<TContext>(
        IServiceScope scope, CancellationToken cancellationToken) where TContext : DbContext
    {
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        return (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
    }
```
Usings a acrescentar: `Microsoft.Data.SqlClient`, `SiagroB1.Infra.Companies`. Na mensagem de `Block` do `Evaluate`, trocar o trecho `apontando para o banco deste ambiente ` por `apontando para o banco deste ambiente (para o banco de uma empresa, acrescente --connection \"<string>\") `. Atualize o `<summary>` da classe: agora são todos os bancos de negócio cadastrados.

- [ ] **Step 6: Ordem de boot no Web e bootstrap no Gateway**

`SiagroB1.Web/Program.cs` — substituir `await PendingMigrationsGuard.EnsureNoPendingMigrationsAsync(app);` por:
```csharp
// Ordem importa: a COMMON primeiro (o bootstrap precisa das colunas novas dela), depois o bootstrap
// cadastra a empresa padrão, e só então há como saber QUAIS bancos de negócio verificar.
await PendingMigrationsGuard.EnsureNoPendingCommonMigrationsAsync(app);
await CompanyBootstrap.RunAsync(app.Services);
await PendingMigrationsGuard.EnsureNoPendingCompanyMigrationsAsync(app);
```
com `using SiagroB1.Security.Startup;`.

`SiagroB1.Gateway/Program.cs` — logo após `var app = builder.Build();`:
```csharp
// O login lista as empresas de COMPANIES: um deploy antigo (tabela vazia) precisa da empresa padrão
// antes do primeiro login, e o Gateway pode subir antes do Web.
await CompanyBootstrap.RunAsync(app.Services);
```
com `using SiagroB1.Security.Startup;`.

- [ ] **Step 7: Rodar, compilar e subir**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~CompanyBootstrapTests|FullyQualifiedName~PendingMigrationsGuardTests"` → PASS.
Run: `dotnet build SiagroB1.sln` → sucesso.
Run (~30 s, encerrar pelo PID): `dotnet run --project SiagroB1.Web --launch-profile ceagui`
Expected no log: `Empresa padrão 0001 cadastrada a partir da configuração.` e `Nenhuma migration pendente em AppDbContext[0001]`. Confirme no banco: `SELECT * FROM CEAGUI_SIAGRO_COMMON_DEV.dbo.COMPANIES` tem uma linha e `SELECT COUNT(*) FROM USERS_PROFILES WHERE CompanyCode IS NULL` é 0.

- [ ] **Step 8: Commit**

```bash
git add SiagroB1.Security/Startup/CompanyBootstrap.cs SiagroB1.Web/Startup/PendingMigrationsGuard.cs SiagroB1.Web/Program.cs SiagroB1.Gateway/Program.cs SiagroB1.Application.Tests/Security/CompanyBootstrapTests.cs SiagroB1.Application.Tests/Startup/PendingMigrationsGuardTests.cs
git commit -m "feat(platform): bootstrap da empresa padrão e guard de migrations por empresa

Deploys existentes têm COMPANIES vazia: no boot, Web e Gateway cadastram a
empresa padrão a partir da configuração e atribuem a ela perfis e sessões sem
empresa, então nada muda para quem só tem uma (spec 2026-10-16). O guard de
migrations passa a checar o banco de CADA empresa cadastrada.

Atenção: a ordem no Web é COMMON → bootstrap → empresas; invertida, o
bootstrap roda sem as colunas novas ou verifica uma lista vazia.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: Hangfire — storage na COMMON, jobs por empresa

**Files:**
- Create: `SiagroB1.Application/Jobs/Companies/CompanyJobFilter.cs`
- Create: `SiagroB1.Application/Jobs/Companies/CompanyScopedJobRunner.cs`
- Modify: `SiagroB1.Domain/Interfaces/Notifications/IContractNotificationJobs.cs`
- Modify: `SiagroB1.Application/Jobs/ContractNotificationDispatchJob.cs`
- Modify: `SiagroB1.Application/Jobs/ContractNotificationSweepJob.cs`
- Modify: `SiagroB1.Application/Services/Notifications/NotificationOutboxResendService.cs`
- Modify: `SiagroB1.Web/Program.cs` (Hangfire storage + registros dos recorrentes)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (registrar o runner)
- Modify: `SiagroB1.Application.Tests/Support/FakeBackgroundJobClient.cs`
- Modify: `SiagroB1.Application.Tests/Notifications/ContractNotificationSweepJobTests.cs`
- Modify: `SiagroB1.Application.Tests/Notifications/ContractNotificationDispatchJobTests.cs` (assinatura do `ExecuteAsync`)
- Test: `SiagroB1.Application.Tests/Jobs/CompanyJobFilterTests.cs`
- Test: `SiagroB1.Application.Tests/Jobs/CompanyScopedJobRunnerTests.cs`

**Interfaces:**
- Consumes: `CompanyAmbient`, `ICompanyCatalog`, `ICompanyContext`.
- Produces:
  - `CompanyJobFilter : JobFilterAttribute, IServerFilter` com `static string? ExtractCompanyCode(Job job)`
  - `CompanyScopedJobRunner` (scoped): `const string StorageDailyCalculation = "storage-daily-calculation"`, `ContractNotificationSweep = "contract-notification-sweep"`, `ContractDraftsReconcile = "contract-drafts-reconcile"`; `Task RunForAllCompaniesAsync(string jobKey, CancellationToken ct)`
  - `IContractNotificationDispatchJob.ExecuteAsync(string companyCode, Guid outboxKey, CancellationToken ct = default)`
  - `FakeBackgroundJobClient.EnqueuedCompanyCodes: List<string>`

- [ ] **Step 1: Escrever `CompanyJobFilterTests` (falha)**

```csharp
using Hangfire.Common;
using SiagroB1.Application.Jobs.Companies;
using SiagroB1.Domain.Interfaces.Notifications;

namespace SiagroB1.Application.Tests.Jobs;

/// <summary>
/// O filtro só conhece os argumentos do job: a empresa viaja como PRIMEIRO parâmetro string chamado
/// exatamente "companyCode". Qualquer outro nome é ignorado — melhor um job sem empresa (que lança
/// ao tocar o banco) do que um job que adivinha.
/// </summary>
public class CompanyJobFilterTests
{
    [Fact]
    public void Extracts_the_companyCode_argument_of_a_dispatch_job()
    {
        var key = Guid.NewGuid();
        var job = Job.FromExpression<IContractNotificationDispatchJob>(j => j.ExecuteAsync("0002", key, CancellationToken.None));

        Assert.Equal("0002", CompanyJobFilter.ExtractCompanyCode(job));
    }

    [Fact]
    public void Job_without_a_companyCode_parameter_yields_null()
    {
        var job = Job.FromExpression<IContractNotificationSweepJob>(j => j.ExecuteAsync(CancellationToken.None));

        Assert.Null(CompanyJobFilter.ExtractCompanyCode(job));
    }
}
```

- [ ] **Step 2: Escrever `CompanyScopedJobRunnerTests` (falha)**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Jobs.Companies;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Companies;

namespace SiagroB1.Application.Tests.Jobs;

/// <summary>
/// Um job recorrente roda uma vez POR EMPRESA, cada vez num escopo com a empresa definida. A falha
/// de uma empresa não impede as outras, mas o job termina falho para o Hangfire registrar.
/// </summary>
public class CompanyScopedJobRunnerTests
{
    private static readonly CompanyInfo A = new("0001", "A", "Server=.;Database=A");
    private static readonly CompanyInfo B = new("0002", "B", "Server=.;Database=B");

    /// <summary>Job de teste: anota a empresa do escopo em que rodou e, opcionalmente, falha numa delas.</summary>
    private sealed class ProbeJob(ICompanyContext companyContext, List<string> seen, string? failOn) : IStorageAddressesDailyCalculationJob
    {
        public Task ExecuteAsync(DateTime? processingDate = null, CancellationToken ct = default)
        {
            seen.Add($"{companyContext.CompanyCode}:{CompanyAmbient.CompanyCode}");
            if (companyContext.CompanyCode == failOn) throw new InvalidOperationException("boom");
            return Task.CompletedTask;
        }
    }

    private static (CompanyScopedJobRunner Runner, List<string> Seen) Build(string? failOn = null)
    {
        var seen = new List<string>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TestConfiguration.With(("ConnectionStrings:SiagroDB", "Server=.;Database=FALLBACK")));
        services.AddSingleton<ICompanyCatalog>(new FakeCompanyCatalog(A, B));
        services.AddScoped<ICompanyContext, CompanyContext>();
        services.AddScoped<IStorageAddressesDailyCalculationJob>(sp => new ProbeJob(sp.GetRequiredService<ICompanyContext>(), seen, failOn));
        services.AddScoped<CompanyScopedJobRunner>();
        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<CompanyScopedJobRunner>(), seen);
    }

    [Fact]
    public async Task Runs_the_job_once_per_company_with_the_company_set_in_scope_and_ambient()
    {
        var (runner, seen) = Build();

        await runner.RunForAllCompaniesAsync(CompanyScopedJobRunner.StorageDailyCalculation, CancellationToken.None);

        Assert.Equal(["0001:0001", "0002:0002"], seen);
        Assert.Null(CompanyAmbient.CompanyCode);   // restaurado
    }

    [Fact]
    public async Task A_failing_company_does_not_stop_the_others_but_the_job_fails()
    {
        var (runner, seen) = Build(failOn: "0001");

        var ex = await Assert.ThrowsAsync<AggregateException>(() =>
            runner.RunForAllCompaniesAsync(CompanyScopedJobRunner.StorageDailyCalculation, CancellationToken.None));

        Assert.Equal(2, seen.Count);
        Assert.Contains("0001", ex.Message);
    }

    [Fact]
    public async Task Unknown_job_key_throws()
    {
        var (runner, _) = Build();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            runner.RunForAllCompaniesAsync("nao-existe", CancellationToken.None));
    }
}
```

- [ ] **Step 3: Ajustar o fake e os testes dos jobs de notificação (falham: assinatura nova)**

`FakeBackgroundJobClient.cs` — substituir `EnqueuedOutboxKeys` por:
```csharp
    /// <summary>A Key da outbox de cada job: o primeiro argumento Guid (o primeiro string é a empresa).</summary>
    public List<Guid> EnqueuedOutboxKeys =>
        [.. Enqueued.Select(job => job.Args.OfType<Guid>().FirstOrDefault()).Where(key => key != Guid.Empty)];

    /// <summary>A empresa de cada job enfileirado (parâmetro `companyCode`).</summary>
    public List<string> EnqueuedCompanyCodes =>
        [.. Enqueued.Select(job => job.Args.OfType<string>().FirstOrDefault()).Where(code => code is not null)!];
```

`ContractNotificationSweepJobTests.cs`: `CreateJob()` vira `new(_db.Context, _jobClient, new FakeCompanyContext("0002"), NullLogger<ContractNotificationSweepJob>.Instance)` e, no teste `ExecuteAsync_PendingMessagePastGracePeriod_IsEnqueued`, acrescentar `Assert.Equal(["0002"], _jobClient.EnqueuedCompanyCodes);`.

`ContractNotificationDispatchJobTests.cs`: toda chamada `job.ExecuteAsync(key)` / `ExecuteAsync(message.Key)` passa a `job.ExecuteAsync("0001", key)` (o primeiro parâmetro é consumido só pelo filtro).

- [ ] **Step 4: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Jobs.Company|FullyQualifiedName~ContractNotification"`
Expected: falha de compilação.

- [ ] **Step 5: Implementar o filtro e o runner**

`SiagroB1.Application/Jobs/Companies/CompanyJobFilter.cs`:
```csharp
using Hangfire.Common;
using Hangfire.Server;
using SiagroB1.Infra.Companies;

namespace SiagroB1.Application.Jobs.Companies;

/// <summary>
/// Entrega ao job a empresa que veio nos argumentos. <c>OnPerforming</c> roda ANTES de o activator
/// do Hangfire abrir o escopo de DI, no mesmo fluxo síncrono — por isso um AsyncLocal gravado aqui
/// é visível quando o AppDbContext do escopo lê <see cref="ICompanyContext.ConnectionString"/>.
/// Convenção: primeiro parâmetro <c>string</c> chamado exatamente <c>companyCode</c>.
/// </summary>
public sealed class CompanyJobFilter : JobFilterAttribute, IServerFilter
{
    public const string ParameterName = "companyCode";

    public static string? ExtractCompanyCode(Job job)
    {
        var parameters = job.Method.GetParameters();

        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].ParameterType == typeof(string) && parameters[i].Name == ParameterName)
                return job.Args[i] as string;
        }

        return null;
    }

    public void OnPerforming(PerformingContext context) =>
        CompanyAmbient.Set(ExtractCompanyCode(context.BackgroundJob.Job));

    // Limpar evita vazar a empresa para o próximo job do mesmo worker.
    public void OnPerformed(PerformedContext context) => CompanyAmbient.Set(null);
}
```

`SiagroB1.Application/Jobs/Companies/CompanyScopedJobRunner.cs`:
```csharp
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Interfaces.Notifications;
using SiagroB1.Infra.Companies;

namespace SiagroB1.Application.Jobs.Companies;

/// <summary>
/// Executa um job recorrente uma vez POR EMPRESA, cada vez num escopo novo com a empresa definida
/// (explícita no escopo e ambiente, para escopos aninhados como os do ContractDraftsReconcileJob).
/// Chave string em vez de método genérico: o painel do Hangfire mostra o nome legível e não depende
/// da serialização de genéricos.
/// </summary>
public sealed class CompanyScopedJobRunner(
    ICompanyCatalog catalog,
    IServiceScopeFactory scopeFactory,
    ILogger<CompanyScopedJobRunner> logger)
{
    public const string StorageDailyCalculation = "storage-daily-calculation";
    public const string ContractNotificationSweep = "contract-notification-sweep";
    public const string ContractDraftsReconcile = "contract-drafts-reconcile";

    private static readonly IReadOnlyDictionary<string, Func<IServiceProvider, CancellationToken, Task>> Jobs =
        new Dictionary<string, Func<IServiceProvider, CancellationToken, Task>>
        {
            [StorageDailyCalculation] = (sp, ct) => sp.GetRequiredService<IStorageAddressesDailyCalculationJob>().ExecuteAsync(null, ct),
            [ContractNotificationSweep] = (sp, ct) => sp.GetRequiredService<IContractNotificationSweepJob>().ExecuteAsync(ct),
            [ContractDraftsReconcile] = (sp, ct) => sp.GetRequiredService<ContractDraftsReconcileJob>().ExecuteAsync(ct),
        };

    [DisplayName("{0} (todas as empresas)")]
    public async Task RunForAllCompaniesAsync(string jobKey, CancellationToken ct)
    {
        if (!Jobs.TryGetValue(jobKey, out var run))
            throw new ArgumentOutOfRangeException(nameof(jobKey), jobKey, "Job desconhecido para o runner por empresa.");

        var failures = new List<Exception>();

        foreach (var company in await catalog.GetAllAsync(ct))
        {
            try
            {
                using var _ = CompanyAmbient.Use(company.Code);
                using var scope = scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);

                await run(scope.ServiceProvider, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Uma empresa com problema não pode parar as outras — mas o job termina falho.
                logger.LogError(e, "Job {Job} falhou na empresa {Company}", jobKey, company.Code);
                failures.Add(e);
            }
        }

        if (failures.Count > 0)
            throw new AggregateException(
                $"Job {jobKey} falhou em {failures.Count} empresa(s): {string.Join(", ", failures.Select(f => f.Message))}", failures);
    }
}
```
Se `ContractDraftsReconcileJob` não estiver registrado por interface, confirme que `AddApplicationServices()` o registra como `AddScoped<ContractDraftsReconcileJob>()` (já é resolvido pelo Hangfire hoje, então está).

- [ ] **Step 6: Empresa nos jobs enfileirados**

`IContractNotificationJobs.cs`:
```csharp
public interface IContractNotificationDispatchJob
{
    /// <param name="companyCode">Consumido pelo CompanyJobFilter: define o banco em que o job roda.</param>
    Task ExecuteAsync(string companyCode, Guid outboxKey, CancellationToken ct = default);
}
```
`ContractNotificationDispatchJob.ExecuteAsync(string companyCode, Guid outboxKey, CancellationToken ct = default)` — o corpo não muda; acrescente no `<summary>` que o primeiro argumento é a empresa (código, não sensível) e que o filtro é quem o usa.

`ContractNotificationSweepJob`: construtor ganha `ICompanyContext companyContext` (antes do logger); o enqueue vira
```csharp
            var companyCode = companyContext.CompanyCode;
            // ...
            backgroundJobClient.Enqueue<IContractNotificationDispatchJob>(
                job => job.ExecuteAsync(companyCode, outboxKey, CancellationToken.None));
```
`NotificationOutboxResendService`: construtor ganha `ICompanyContext companyContext`; mesmo enqueue com `companyContext.CompanyCode`.

- [ ] **Step 7: Program.cs — storage, filtro, runner e recorrentes**

Em `SiagroB1.Web/Program.cs` (Hangfire):
```csharp
// Storage na COMMON: a fila é do deploy, não de uma empresa. O filtro entrega a empresa dos
// argumentos ao escopo de cada job.
builder.Services.AddHangfire(config => config
    .UseSqlServerStorage(builder.Configuration.GetConnectionString("SiagroCommon"))
    .UseFilter(new CompanyJobFilter()));
```
Em `ServiceCollectionExtensions.AddApplicationServices()`: `services.AddScoped<CompanyScopedJobRunner>();` (com `using SiagroB1.Application.Jobs.Companies;`).

Substituir os três registros recorrentes (mantendo os mesmos ids para o `AddOrUpdate` trocar a definição antiga):
```csharp
RecurringJob.AddOrUpdate<CompanyScopedJobRunner>(
    "storage-daily-calculation-job",
    runner => runner.RunForAllCompaniesAsync(CompanyScopedJobRunner.StorageDailyCalculation, CancellationToken.None),
    "0 1 * * *");

// (comentário existente do varredor permanece)
RecurringJob.AddOrUpdate<CompanyScopedJobRunner>(
    "contract-notification-sweep",
    runner => runner.RunForAllCompaniesAsync(CompanyScopedJobRunner.ContractNotificationSweep, CancellationToken.None),
    Cron.Minutely());
```
e, no ramo `Signature:Enabled`:
```csharp
    RecurringJob.AddOrUpdate<CompanyScopedJobRunner>(
        ContractDraftsReconcileJob.RecurringJobId,
        runner => runner.RunForAllCompaniesAsync(CompanyScopedJobRunner.ContractDraftsReconcile, CancellationToken.None),
        ContractDraftsReconcileJob.CronExpression);
```
`sap-user-sync` **não muda** (espelha OUSR na COMMON; é independente de empresa). Atualize o comentário acima do `UseHangfireDashboard`: os argumentos dos jobs de notificação agora são a empresa (código) e o Guid da outbox — continuam sem telefone nem texto.

- [ ] **Step 8: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Jobs|FullyQualifiedName~Notifications"` → PASS.
Run: `dotnet build SiagroB1.sln` → sucesso.
Run (~40 s): `dotnet run --project SiagroB1.Web --launch-profile ceagui`; abrir `http://localhost:50000/hangfire` (precisa de sessão: logue antes pelo frontend ou use o cookie) e conferir em *Recurring Jobs* que os três mostram `CompanyScopedJobRunner.RunForAllCompaniesAsync("...")`. Confirme que o schema `HangFire` existe agora em `CEAGUI_SIAGRO_COMMON_DEV`.

- [ ] **Step 9: Commit**

```bash
git add SiagroB1.Application/Jobs SiagroB1.Domain/Interfaces/Notifications/IContractNotificationJobs.cs SiagroB1.Application/Services/Notifications/NotificationOutboxResendService.cs SiagroB1.Web/Program.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/Support/FakeBackgroundJobClient.cs SiagroB1.Application.Tests/Notifications SiagroB1.Application.Tests/Jobs
git commit -m "feat(platform): jobs do Hangfire rodam por empresa e o storage vai para a COMMON

Com várias empresas no mesmo deploy, cada recorrente roda uma vez por empresa
num escopo com a empresa definida, e os jobs enfileirados carregam o código da
empresa no primeiro argumento (spec 2026-10-16). sap-user-sync fica único:
espelha o OUSR na COMMON e não depende de empresa.

Atenção: o filtro grava a empresa num AsyncLocal porque OnPerforming roda
ANTES de o activator abrir o escopo de DI — não há escopo para injetar nada.
Jobs em voo no schema HangFire do banco antigo se perdem no deploy (só
dispatch de WhatsApp, revarrido a cada minuto).

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: Conexões cruas e relatórios — lock da NF-e, FastReport, cabeçalho por empresa

**Files:**
- Modify: `SiagroB1.Application/Services/Nfe/NfeNumberReservationService.cs`
- Modify: `SiagroB1.Reports/Services/FastReportService.cs`
- Modify: `SiagroB1.Reports/Services/ReportHeaderService.cs`
- Modify: `SiagroB1.Application.Tests/Nfe/NfeNumberReservationServiceTests.cs`
- Modify: `SiagroB1.Application.Tests/Reports/ReportHeaderServiceTests.cs`
- Modify (só construtores): `SiagroB1.Application.Tests/Reports/{ContractPositionReportsPdfTests,DanfeReportServiceTests,InvoiceReportsPdfTests,LogisticsReportsPdfTests,PrePurchaseContractReportServiceTests,PurchaseContractsByItemPdfTests,ReportTemplateHeaderTests,ReportTemplateRenderSmokeTests,SalesContractsByItemPdfTests}.cs`

**Interfaces:**
- Produces:
  - `NfeNumberReservationService(IDbConnection connection, ICompanyContext companyContext)` (o `FakeNfeNumberReservationService` continua `: NfeNumberReservationService(null!, null!)`)
  - `FastReportService(IWebHostEnvironment env, IConfiguration configuration, ReportHeaderService reportHeader, ICompanyContext companyContext)`
  - `ReportHeaderService(IWebHostEnvironment env, IConfiguration configuration, ILogger<ReportHeaderService> logger, ICompanyContext companyContext)`; logo por convenção `wwwroot/images/logo-{Code}.png` com fallback em `CompanyLogoPath`.

- [ ] **Step 1: Reescrever `NfeNumberReservationServiceTests` (falha)**

```csharp
using Microsoft.Data.SqlClient;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>
/// A trava de emissão (sp_getapplock, dono "Session") só é solta quando a SESSÃO termina: a conexão
/// dela não pode voltar para o pool, e a string tem de trazer a senha — e tem de ser a da EMPRESA
/// da execução, não a da configuração.
/// </summary>
public class NfeNumberReservationServiceTests
{
    [Fact]
    public void Emission_lock_connection_comes_from_the_company_context_and_never_from_the_pool()
    {
        const string company = "Server=db;Database=LMCEREAIS;User Id=app;Password=secret;TrustServerCertificate=True";
        // A conexão da requisição, depois de aberta, devolve a string SEM a senha (Persist Security Info=False).
        using var requestConnection = new SqlConnection("Server=db;Database=LMCEREAIS;User Id=app;TrustServerCertificate=True");

        var lockConnectionString = new NfeNumberReservationService(requestConnection, new FakeCompanyContext("0002", company))
            .EmissionLockConnectionString();

        var builder = new SqlConnectionStringBuilder(lockConnectionString);
        Assert.False(builder.Pooling);
        Assert.Equal("secret", builder.Password);
        Assert.Equal("LMCEREAIS", builder.InitialCatalog);
    }
}
```

- [ ] **Step 2: Acrescentar testes ao `ReportHeaderServiceTests` (falham)**

Trocar o helper `CreateService` por:
```csharp
    private ReportHeaderService CreateService(string? companyName, string? logoPath, ICompanyContext? companyContext = null)
    {
        var settings = new Dictionary<string, string?>();
        if (companyName is not null) settings["CompanyName"] = companyName;
        if (logoPath is not null) settings["CompanyLogoPath"] = logoPath;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new ReportHeaderService(new TestWebHostEnvironment(_contentRoot), configuration,
            new TestLogger<ReportHeaderService>(), companyContext ?? FakeCompanyContext.Unresolved());
    }
```
(`using SiagroB1.Infra.Companies;`). Os testes existentes continuam válidos (contexto não resolvido = comportamento da configuração). Acrescentar:
```csharp
    [Fact]
    public void Apply_UsesTheSessionCompanyName_OverConfiguration()
    {
        var service = CreateService(companyName: "DA CONFIG", logoPath: null,
            companyContext: new FakeCompanyContext("0002") );
        using var report = CreateReport(withLogoObject: false);

        service.Apply(report);

        Assert.Equal("Empresa 0002", report.GetParameterValue("pCompanyName"));
    }

    [Fact]
    public void Apply_PrefersThePerCompanyLogo_AndFallsBackToTheConfiguredOne()
    {
        var shared = WriteLogo(Path.Combine("wwwroot", "images", "logo.png"));
        var perCompany = WriteLogo(Path.Combine("wwwroot", "images", "logo-0002.png"));

        var withOwnLogo = CreateService("ACME", "wwwroot/images/logo.png", new FakeCompanyContext("0002"));
        var withoutOwnLogo = CreateService("ACME", "wwwroot/images/logo.png", new FakeCompanyContext("0001"));

        Assert.Equal(File.ReadAllBytes(perCompany), withOwnLogo.LogoBytes());
        Assert.Equal(File.ReadAllBytes(shared), withoutOwnLogo.LogoBytes());
    }

    [Fact]
    public void LogoCache_KeepsOneEntryPerPath_SoTwoCompaniesDoNotEvictEachOther()
    {
        WriteLogo(Path.Combine("wwwroot", "images", "logo-0001.png"));
        WriteLogo(Path.Combine("wwwroot", "images", "logo-0002.png"));
        var a = CreateService("ACME", null, new FakeCompanyContext("0001"));
        var b = CreateService("ACME", null, new FakeCompanyContext("0002"));

        var first = a.LogoBytes();
        _ = b.LogoBytes();

        Assert.Same(first, a.LogoBytes());   // mesma instância do cache, não relido
    }
```
Confira o que `WriteLogo` devolve (caminho completo) e como `CreateReport` é assinado; ajuste as chamadas se preciso. Se os bytes do PNG de 1 pixel forem idênticos para os dois arquivos, o teste do fallback ainda vale porque compara com o arquivo lido — mas para distinguir, escreva um byte extra no fim de `logo-0002.png` após o `WriteLogo` (PNG tolera lixo após IEND).

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeNumberReservationServiceTests|FullyQualifiedName~ReportHeaderServiceTests"`
Expected: falha de compilação.

- [ ] **Step 4: `NfeNumberReservationService`**

```csharp
public class NfeNumberReservationService(IDbConnection connection, ICompanyContext companyContext)
{
    /// <summary>
    /// String da conexão da trava: a da EMPRESA da execução (a da conexão da requisição perde a senha
    /// depois de aberta, com Persist Security Info=False) e SEM pool — com pool, fechar devolve a
    /// conexão ociosa ao pool ainda segurando a trava de sessão, e o documento responderia
    /// "em andamento" até a conexão ser reaproveitada.
    /// </summary>
    public string EmissionLockConnectionString() =>
        new SqlConnectionStringBuilder(companyContext.ConnectionString)
        {
            Pooling = false,
        }.ConnectionString;
```
Remover `using Microsoft.Extensions.Configuration;`, adicionar `using SiagroB1.Infra.Companies;`. O resto não muda.

- [ ] **Step 5: `FastReportService`**

Construtor: `FastReportService(IWebHostEnvironment env, IConfiguration configuration, ReportHeaderService reportHeader, ICompanyContext companyContext)`; a linha `sqlConn.ConnectionString = configuration.GetConnectionString("SiagroDB");` vira `sqlConn.ConnectionString = companyContext.ConnectionString;` (nas duas sobrecargas, se a segunda também tiver). `using SiagroB1.Infra.Companies;`.

- [ ] **Step 6: `ReportHeaderService`**

```csharp
public class ReportHeaderService(
    IWebHostEnvironment env,
    IConfiguration configuration,
    ILogger<ReportHeaderService> logger,
    ICompanyContext companyContext)
{
    public const string CompanyNameParameter = "pCompanyName";
    public const string LogoObjectName = "picLogo";

    // Bytes por CAMINHO: duas empresas com logos diferentes não podem ficar se expulsando de um
    // cache de um slot só. Só caminhos encontrados entram; arquivo ausente é reconsultado.
    private static readonly ConcurrentDictionary<string, byte[]> LogoCache = new(StringComparer.OrdinalIgnoreCase);

    public void Apply(Report report)
    {
        report.SetParameterValue(CompanyNameParameter,
            companyContext.Current?.CompanyName ?? configuration.GetValue<string>("CompanyName") ?? "");
        // ... resto igual
    }

    private byte[]? LoadLogo()
    {
        foreach (var fullPath in LogoCandidates())
        {
            if (LogoCache.TryGetValue(fullPath, out var cached))
                return cached;

            if (!File.Exists(fullPath))
                continue;

            var bytes = File.ReadAllBytes(fullPath);
            LogoCache[fullPath] = bytes;
            return bytes;
        }

        if (LogoCandidates().Any())
            logger.LogWarning("Logo da empresa não encontrado em {Paths}; o relatório sai sem logo.", string.Join(" | ", LogoCandidates()));

        return null;
    }

    /// <summary>Convenção por empresa (wwwroot/images/logo-{Code}.png) primeiro; depois CompanyLogoPath.</summary>
    private IEnumerable<string> LogoCandidates()
    {
        var code = companyContext.Current?.Code;
        if (!string.IsNullOrWhiteSpace(code))
            yield return Path.Combine(env.ContentRootPath, "wwwroot", "images", $"logo-{code}.png");

        var configured = configuration.GetValue<string>("CompanyLogoPath");
        if (!string.IsNullOrWhiteSpace(configured))
            yield return Path.IsPathRooted(configured) ? configured : Path.Combine(env.ContentRootPath, configured);
    }
}
```
Remover `_cachedLogo`/`_cachedLogoPath`/`CacheLock`; `using System.Collections.Concurrent;` e `using SiagroB1.Infra.Companies;`. Se um teste existente esperava um warning específico para logo ausente, mantenha a mensagem compatível.

- [ ] **Step 7: Ajustar os construtores nos testes de relatório**

Nos arquivos listados em **Files**, toda `new ReportHeaderService(<env>, <config>, <logger>)` ganha `, FakeCompanyContext.Unresolved()` e toda `new FastReportService(<env>, <config>, <header>)` ganha `, FakeCompanyContext.Unresolved()`. (Grep: `grep -rn "new ReportHeaderService(\|new FastReportService(" SiagroB1.Application.Tests`.) Nada mais muda: contexto não resolvido reproduz o comportamento anterior baseado na configuração.

- [ ] **Step 8: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Reports|FullyQualifiedName~Nfe"` → PASS (os testes de NF-e usam o `FakeNfeNumberReservationService`, que não muda).
Run: `dotnet build SiagroB1.sln` → sucesso.

- [ ] **Step 9: Commit**

```bash
git add SiagroB1.Application/Services/Nfe/NfeNumberReservationService.cs SiagroB1.Reports/Services/FastReportService.cs SiagroB1.Reports/Services/ReportHeaderService.cs SiagroB1.Application.Tests/Nfe/NfeNumberReservationServiceTests.cs SiagroB1.Application.Tests/Reports
git commit -m "feat(reports): lock da NF-e, FastReport e cabeçalho usam a empresa da sessão

Os três pontos que ainda liam ConnectionStrings:SiagroDB direto passam a usar
a empresa da execução; o nome no cabeçalho dos relatórios vem da empresa da
sessão e o logo segue a convenção wwwroot/images/logo-{Code}.png, com o
CompanyLogoPath da configuração como fallback (spec 2026-10-16).

Atenção: o cache do logo passou a ser por caminho — com um slot só, duas
empresas ficariam se expulsando a cada relatório.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 9: Cadastro de Empresas endurecido, teste de conexão e perfil por empresa no CRUD

**Files:**
- Create: `SiagroB1.Application/Services/Companies/ICompanyDatabaseProbe.cs`
- Create: `SiagroB1.Application/Services/Companies/CompanyDatabaseProbe.cs`
- Create: `SiagroB1.Application/Services/Companies/CompanyConnectionStrings.cs`
- Create: `SiagroB1.Application/Services/Companies/CompanyDatabaseRules.cs`
- Create: `SiagroB1.Application/Services/Companies/CompaniesTestConnectionService.cs`
- Create: `SiagroB1.Domain/Dtos/Common/CompanyTestConnectionResult.cs`
- Modify: `SiagroB1.Application/Services/Companies/{CompaniesCreateService,CompaniesUpdateService,CompaniesDeleteService,CompaniesGetService}.cs`
- Modify: `SiagroB1.Application/Services/UsersProfiles/{UsersProfilesCreateService,UsersProfilesUpdateService}.cs`
- Modify: `SiagroB1.Web/Controllers/CompaniesController.cs`
- Create: `SiagroB1.Web/Actions/Companies/CompaniesTestConnectionController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (action nova)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Create: `SiagroB1.Application.Tests/Support/FakeCompanyDatabaseProbe.cs`
- Test: `SiagroB1.Application.Tests/Companies/CompanyConnectionStringsTests.cs`
- Test: `SiagroB1.Application.Tests/Companies/CompaniesCreateServiceTests.cs`
- Test: `SiagroB1.Application.Tests/Companies/CompaniesUpdateServiceTests.cs`
- Test: `SiagroB1.Application.Tests/Companies/CompaniesDeleteServiceTests.cs`
- Test: `SiagroB1.Application.Tests/Security/UsersProfilesCreateServiceTests.cs`

**Interfaces:**
- Produces:
  - `record CompanyProbeResult(bool CanConnect, string? Error, IReadOnlyList<string> PendingMigrations, string Server, string Database)`
  - `interface ICompanyDatabaseProbe { Task<CompanyProbeResult> ProbeAsync(string connectionString, CancellationToken ct = default); }`
  - `static class CompanyConnectionStrings { const string Mask = "*****"; string MaskPassword(string? cs); bool IsMasked(string? cs); string MergeMaskedPassword(string incoming, string stored); }`
  - `CompaniesCreateService(CommonDbContext db, ICompanyDatabaseProbe probe, ICompanyCatalog catalog, IConfiguration configuration, ILogger<CompaniesCreateService> logger)`; `CompaniesUpdateService(CommonDbContext, ICompanyDatabaseProbe, ICompanyCatalog, ILogger<...>)`; `CompaniesDeleteService(CommonDbContext, ICompanyCatalog, IStringLocalizer<Resource>, ILogger<...>)`; `CompaniesGetService` devolve `ConnectionString` mascarada.
  - `CompaniesTestConnectionService.ExecuteAsync(string? code, string connectionString)` → `CompanyTestConnectionResult { bool Success; string Message; string Server; string Database; List<string> PendingMigrations; }`
  - Action OData `CompaniesTestConnection(Code: string, ConnectionString: string)` → `CompanyTestConnectionResult`, admin-only, em `POST odata/CompaniesTestConnection`.
  - `UsersProfilesCreateService(CommonDbContext, ICompanyContext, IStringLocalizer<Resource>, ILogger<...>)`.

- [ ] **Step 1: Escrever `CompanyConnectionStringsTests` (falha)**

```csharp
using SiagroB1.Application.Services.Companies;

namespace SiagroB1.Application.Tests.Companies;

/// <summary>A string de conexão sai do servidor com a senha mascarada e volta assim na edição; a senha real só é trocada quando o admin digita outra.</summary>
public class CompanyConnectionStringsTests
{
    private const string Real = "Server=db;Database=LM;User Id=app;Password=s3cr3t;TrustServerCertificate=True";

    [Fact]
    public void MaskPassword_replaces_the_password_and_keeps_the_rest()
    {
        var masked = CompanyConnectionStrings.MaskPassword(Real);

        Assert.DoesNotContain("s3cr3t", masked);
        Assert.Contains("*****", masked);
        Assert.Contains("Initial Catalog=LM", masked);
        Assert.True(CompanyConnectionStrings.IsMasked(masked));
        Assert.False(CompanyConnectionStrings.IsMasked(Real));
    }

    [Fact]
    public void MaskPassword_without_password_or_unparseable_returns_input()
    {
        const string integrated = "Server=db;Database=LM;Integrated Security=True";
        Assert.Equal(integrated, CompanyConnectionStrings.MaskPassword(integrated));
        Assert.Equal("isso não é uma string", CompanyConnectionStrings.MaskPassword("isso não é uma string"));
        Assert.Equal("", CompanyConnectionStrings.MaskPassword(null));
    }

    [Fact]
    public void MergeMaskedPassword_restores_the_stored_password_only_when_masked()
    {
        var masked = CompanyConnectionStrings.MaskPassword(Real).Replace("Initial Catalog=LM", "Initial Catalog=LM2");

        var merged = CompanyConnectionStrings.MergeMaskedPassword(masked, Real);

        Assert.Contains("Password=s3cr3t", merged);
        Assert.Contains("Initial Catalog=LM2", merged);
        Assert.Equal("Server=x;Password=nova", CompanyConnectionStrings.MergeMaskedPassword("Server=x;Password=nova", Real));
    }
}
```

- [ ] **Step 2: Escrever o fake do probe e os testes dos serviços de empresa (falham)**

`SiagroB1.Application.Tests/Support/FakeCompanyDatabaseProbe.cs`:
```csharp
using SiagroB1.Application.Services.Companies;

namespace SiagroB1.Application.Tests.Support;

public sealed class FakeCompanyDatabaseProbe : ICompanyDatabaseProbe
{
    public CompanyProbeResult Result { get; set; } = new(true, null, [], "db", "LM");
    public List<string> Probed { get; } = [];

    public Task<CompanyProbeResult> ProbeAsync(string connectionString, CancellationToken ct = default)
    {
        Probed.Add(connectionString);
        return Task.FromResult(Result);
    }
}
```

`CompaniesCreateServiceTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Companies;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Companies;

/// <summary>
/// Cadastrar uma empresa é cadastrar um BANCO: a string tem de abrir, não pode ser a da COMMON e o
/// banco tem de estar com as migrations em dia — senão o próximo boot do Web recusaria subir.
/// </summary>
public class CompaniesCreateServiceTests
{
    private const string Cs = "Server=db;Database=LMCEREAIS_SIAGRO;User Id=app;Password=x";

    private readonly CommonDbContext _db = new(new DbContextOptionsBuilder<CommonDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FakeCompanyDatabaseProbe _probe = new();
    private readonly FakeCompanyCatalog _catalog = new();

    private CompaniesCreateService Create() =>
        new(_db, _probe, _catalog,
            TestConfiguration.With(("ConnectionStrings:SiagroCommon", "Server=db;Database=CEAGUI_SIAGRO_COMMON;User Id=app;Password=x")),
            NullLogger<CompaniesCreateService>.Instance);

    private static Company Entity(string code = "0002", string cs = Cs) =>
        new() { Code = code, CompanyName = "LM CEREAIS", ConnectionString = cs };

    [Fact]
    public async Task Valid_company_is_saved_and_the_catalog_is_invalidated()
    {
        var saved = await Create().ExecuteAsync(Entity(code: "lm02"));

        Assert.Equal("LM02", saved.Code);
        Assert.Single(_db.Companies);
        Assert.Equal(1, _catalog.Invalidations);
        Assert.Equal([Cs], _probe.Probed);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("12345")]
    [InlineData("A-01")]
    public async Task Code_must_be_1_to_4_letters_or_digits(string code)
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Create().ExecuteAsync(Entity(code: code)));
        Assert.Contains("Código", ex.Message);
    }

    [Fact]
    public async Task Masked_connection_string_is_refused()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Create().ExecuteAsync(Entity(cs: CompanyConnectionStrings.MaskPassword(Cs))));
        Assert.Contains("senha", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Connection_string_pointing_to_the_common_database_is_refused()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Create().ExecuteAsync(Entity(cs: "Server=db;Database=CEAGUI_SIAGRO_COMMON;User Id=app;Password=x")));
        Assert.Contains("COMMON", ex.Message);
    }

    [Fact]
    public async Task Unreachable_database_is_refused_with_the_probe_error()
    {
        _probe.Result = new(false, "Login failed for user 'app'.", [], "db", "LM");

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Create().ExecuteAsync(Entity()));
        Assert.Contains("Login failed", ex.Message);
        Assert.Empty(_db.Companies);
    }

    [Fact]
    public async Task Database_with_pending_migrations_is_refused_with_the_command_to_run()
    {
        _probe.Result = new(true, null, ["20261016120000_X", "20261016130000_Y"], "db", "LMCEREAIS_SIAGRO");

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Create().ExecuteAsync(Entity()));

        Assert.Contains("migrations pendentes", ex.Message);
        Assert.Contains("20261016120000_X", ex.Message);
        Assert.Contains("dotnet ef database update --context AppDbContext --connection", ex.Message);
        Assert.Empty(_db.Companies);
    }

    [Fact]
    public async Task Duplicate_code_is_refused()
    {
        await Create().ExecuteAsync(Entity());

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Create().ExecuteAsync(Entity()));
        Assert.Contains("já existe", ex.Message);
    }
}
```

`CompaniesUpdateServiceTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Companies;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Companies;

public class CompaniesUpdateServiceTests
{
    private const string Stored = "Server=db;Database=LM;User Id=app;Password=s3cr3t";

    private readonly CommonDbContext _db = new(new DbContextOptionsBuilder<CommonDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FakeCompanyDatabaseProbe _probe = new();
    private readonly FakeCompanyCatalog _catalog = new();

    public CompaniesUpdateServiceTests()
    {
        _db.Companies.Add(new Company { Code = "0002", CompanyName = "LM", ConnectionString = Stored });
        _db.SaveChanges();
    }

    private CompaniesUpdateService Create() => new(_db, _probe, _catalog, NullLogger<CompaniesUpdateService>.Instance);

    [Fact]
    public async Task Masked_password_keeps_the_stored_one_and_does_not_probe_when_nothing_else_changed()
    {
        var incoming = new Company { Code = "0002", CompanyName = "LM CEREAIS", ConnectionString = CompanyConnectionStrings.MaskPassword(Stored) };

        await Create().ExecuteAsync("0002", incoming);

        var row = _db.Companies.Single();
        Assert.Equal("LM CEREAIS", row.CompanyName);
        Assert.Contains("Password=s3cr3t", row.ConnectionString);
        Assert.Empty(_probe.Probed);
        Assert.Equal(1, _catalog.Invalidations);
    }

    [Fact]
    public async Task Changed_connection_string_is_probed_and_pending_migrations_are_refused()
    {
        _probe.Result = new(true, null, ["20261016_X"], "db", "LM2");
        var incoming = new Company { Code = "0002", CompanyName = "LM", ConnectionString = "Server=db;Database=LM2;User Id=app;Password=nova" };

        await Assert.ThrowsAsync<DefaultException>(() => Create().ExecuteAsync("0002", incoming));

        Assert.Single(_probe.Probed);
        Assert.Equal(Stored, _db.Companies.Single().ConnectionString);
    }

    [Fact]
    public async Task Code_is_immutable()
    {
        var incoming = new Company { Code = "9999", CompanyName = "LM", ConnectionString = Stored };

        await Create().ExecuteAsync("0002", incoming);

        Assert.Equal("0002", _db.Companies.Single().Code);
    }
}
```

`CompaniesDeleteServiceTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Companies;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Companies;

public class CompaniesDeleteServiceTests
{
    private readonly CommonDbContext _db = new(new DbContextOptionsBuilder<CommonDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FakeCompanyCatalog _catalog = new() { DefaultCompanyCode = "0001" };

    public CompaniesDeleteServiceTests()
    {
        _db.Companies.Add(new Company { Code = "0001", CompanyName = "A", ConnectionString = "a" });
        _db.Companies.Add(new Company { Code = "0002", CompanyName = "B", ConnectionString = "b" });
        _db.SaveChanges();
    }

    private CompaniesDeleteService Create() =>
        new(_db, _catalog, new FakeStringLocalizer<Resource>(), NullLogger<CompaniesDeleteService>.Instance);

    [Fact]
    public async Task Default_company_cannot_be_deleted()
    {
        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Create().ExecuteAsync("0001"));
        Assert.Contains("padrão", ex.Message);
    }

    [Fact]
    public async Task Company_with_profiles_or_active_sessions_cannot_be_deleted()
    {
        var user = new User { Username = "u", FullName = "u" };
        _db.Users.Add(user);
        _db.UserProfiles.Add(new UserProfile { UserId = user.Id, ProfileCode = "P", CompanyCode = "0002" });
        _db.SaveChanges();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Create().ExecuteAsync("0002"));
        Assert.Contains("perfis", ex.Message);
    }

    [Fact]
    public async Task Unused_company_is_deleted_and_the_catalog_invalidated()
    {
        await Create().ExecuteAsync("0002");

        Assert.Single(_db.Companies);
        Assert.Equal(1, _catalog.Invalidations);
    }
}
```
`FakeStringLocalizer<T>` é genérico: use `new FakeStringLocalizer<Resource>()` com `using SiagroB1.Commons.Resources;` (já nos snippets).

- [ ] **Step 3: Escrever `UsersProfilesCreateServiceTests` (falha)**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.UsersProfiles;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Security;

/// <summary>Perfil sem empresa informada vale para a empresa da SESSÃO de quem cadastra; o mesmo perfil pode repetir em outra empresa, não na mesma.</summary>
public class UsersProfilesCreateServiceTests
{
    private readonly CommonDbContext _db = new(new DbContextOptionsBuilder<CommonDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Guid _userId;

    public UsersProfilesCreateServiceTests()
    {
        var user = new User { Username = "joao", FullName = "João" };
        _db.Users.Add(user);
        _userId = user.Id;
        _db.Companies.Add(new Company { Code = "0001", CompanyName = "A", ConnectionString = "a" });
        _db.Companies.Add(new Company { Code = "0002", CompanyName = "B", ConnectionString = "b" });
        _db.Profiles.Add(new Profile { Code = "OPER", Description = "Operador" });
        _db.SaveChanges();
    }

    private UsersProfilesCreateService Create(string sessionCompany = "0001") =>
        new(_db, new FakeCompanyContext(sessionCompany), new FakeStringLocalizer<Resource>(), NullLogger<UsersProfilesCreateService>.Instance);

    [Fact]
    public async Task Missing_company_defaults_to_the_session_company()
    {
        var saved = await Create("0002").ExecuteAsync(_userId, new UserProfile { ProfileCode = "OPER" });

        Assert.Equal("0002", saved.CompanyCode);
    }

    [Fact]
    public async Task Unknown_company_is_refused()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Create().ExecuteAsync(_userId, new UserProfile { ProfileCode = "OPER", CompanyCode = "9999" }));
        Assert.Equal("Empresa inválida.", ex.Message);
    }

    [Fact]
    public async Task Same_profile_twice_in_the_same_company_is_refused_but_allowed_in_another()
    {
        await Create().ExecuteAsync(_userId, new UserProfile { ProfileCode = "OPER", CompanyCode = "0001" });

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Create().ExecuteAsync(_userId, new UserProfile { ProfileCode = "OPER", CompanyCode = "0001" }));
        Assert.Equal("Este perfil já está atribuído ao usuário nesta empresa.", ex.Message);

        await Create().ExecuteAsync(_userId, new UserProfile { ProfileCode = "OPER", CompanyCode = "0002" });
        Assert.Equal(2, _db.UserProfiles.Count());
    }
}
```

- [ ] **Step 4: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Companies|FullyQualifiedName~UsersProfilesCreateServiceTests"`
Expected: falha de compilação.

- [ ] **Step 5: Probe, máscara e DTO**

`ICompanyDatabaseProbe.cs`:
```csharp
namespace SiagroB1.Application.Services.Companies;

public sealed record CompanyProbeResult(
    bool CanConnect, string? Error, IReadOnlyList<string> PendingMigrations, string Server, string Database);

/// <summary>Abre o banco de uma empresa candidata e diz se está com as migrations em dia. Interface para os testes não precisarem de SQL Server.</summary>
public interface ICompanyDatabaseProbe
{
    Task<CompanyProbeResult> ProbeAsync(string connectionString, CancellationToken ct = default);
}
```

`CompanyDatabaseProbe.cs`:
```csharp
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Companies;

public sealed class CompanyDatabaseProbe : ICompanyDatabaseProbe
{
    public async Task<CompanyProbeResult> ProbeAsync(string connectionString, CancellationToken ct = default)
    {
        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            return new CompanyProbeResult(false, $"String de conexão inválida: {e.Message}", [], string.Empty, string.Empty);
        }

        // Mesmo assembly de migrations do Web: é o que diz se o banco está no esquema que o código espera.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, b => b.MigrationsAssembly("SiagroB1.Migrations").CommandTimeout(15))
            .Options;

        try
        {
            await using var context = new AppDbContext(options);
            var pending = (await context.Database.GetPendingMigrationsAsync(ct)).ToList();
            return new CompanyProbeResult(true, null, pending, builder.DataSource, builder.InitialCatalog);
        }
        catch (Exception e)
        {
            return new CompanyProbeResult(false, e.Message, [], builder.DataSource, builder.InitialCatalog);
        }
    }
}
```

`CompanyConnectionStrings.cs`:
```csharp
using Microsoft.Data.SqlClient;

namespace SiagroB1.Application.Services.Companies;

/// <summary>A senha do banco nunca sai do servidor: GET devolve mascarada, e a edição que volta mascarada mantém a armazenada.</summary>
public static class CompanyConnectionStrings
{
    public const string Mask = "*****";

    public static string MaskPassword(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return connectionString ?? string.Empty;

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrEmpty(builder.Password)) return connectionString;
            builder.Password = Mask;
            return builder.ConnectionString;
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            return connectionString;
        }
    }

    public static bool IsMasked(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return false;

        try
        {
            return new SqlConnectionStringBuilder(connectionString).Password == Mask;
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            return false;
        }
    }

    public static string MergeMaskedPassword(string incoming, string stored)
    {
        if (!IsMasked(incoming)) return incoming;

        var builder = new SqlConnectionStringBuilder(incoming)
        {
            Password = new SqlConnectionStringBuilder(stored).Password,
        };
        return builder.ConnectionString;
    }

    /// <summary>Mesmo servidor e catálogo — para recusar a COMMON como banco de negócio.</summary>
    public static bool SameDatabase(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;

        try
        {
            var x = new SqlConnectionStringBuilder(a);
            var y = new SqlConnectionStringBuilder(b);
            return string.Equals(x.DataSource, y.DataSource, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.InitialCatalog, y.InitialCatalog, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            return false;
        }
    }
}
```

`SiagroB1.Domain/Dtos/Common/CompanyTestConnectionResult.cs`:
```csharp
namespace SiagroB1.Domain.Dtos.Common;

/// <summary>Resposta da action CompaniesTestConnection — o que a tela mostra no "Testar conexão".</summary>
public class CompanyTestConnectionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public List<string> PendingMigrations { get; set; } = [];
}
```

- [ ] **Step 6: Serviços de empresa**

`CompaniesCreateService`:
```csharp
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Companies;

/// <summary>
/// Cadastrar uma empresa é cadastrar um BANCO. O banco tem de abrir, não pode ser a COMMON e tem de
/// estar com as migrations em dia — o Web se recusa a subir com migration pendente em qualquer
/// empresa cadastrada, então aceitar um banco atrasado derrubaria o próximo deploy.
/// Ordem operacional: criar o banco → dotnet ef database update --connection → cadastrar aqui.
/// </summary>
public partial class CompaniesCreateService(
    CommonDbContext db,
    ICompanyDatabaseProbe probe,
    ICompanyCatalog catalog,
    IConfiguration configuration,
    ILogger<CompaniesCreateService> logger)
{
    [GeneratedRegex("^[A-Z0-9]{1,4}$")]
    private static partial Regex CodePattern();

    public async Task<Company> ExecuteAsync(Company entity)
    {
        entity.Code = (entity.Code ?? string.Empty).Trim().ToUpperInvariant();
        entity.CompanyName = (entity.CompanyName ?? string.Empty).Trim();

        if (!CodePattern().IsMatch(entity.Code))
            throw new DefaultException("Código da empresa: de 1 a 4 letras ou dígitos.");

        if (entity.CompanyName.Length == 0)
            throw new DefaultException("Informe o nome da empresa.");

        if (await db.Companies.AnyAsync(c => c.Code == entity.Code))
            throw new DefaultException($"Já existe uma empresa com o código {entity.Code}.");

        await CompanyDatabaseRules.ValidateConnectionStringAsync(
            entity.ConnectionString, probe, configuration.GetConnectionString("SiagroCommon"));

        try
        {
            await db.Companies.AddAsync(entity);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw new ApplicationException(ex.Message);
        }

        catalog.Invalidate();
        return entity;
    }
}
```

Regras compartilhadas (novo arquivo `SiagroB1.Application/Services/Companies/CompanyDatabaseRules.cs`):
```csharp
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.Companies;

public static class CompanyDatabaseRules
{
    public static async Task<CompanyProbeResult> ValidateConnectionStringAsync(
        string? connectionString, ICompanyDatabaseProbe probe, string? commonConnectionString, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new DefaultException("Informe a string de conexão do banco da empresa.");

        if (CompanyConnectionStrings.IsMasked(connectionString))
            throw new DefaultException("Informe a senha real do banco: a string de conexão está com a senha mascarada.");

        if (CompanyConnectionStrings.SameDatabase(connectionString, commonConnectionString))
            throw new DefaultException("A string de conexão aponta para o banco COMMON. O banco de negócio da empresa tem de ser outro.");

        var result = await probe.ProbeAsync(connectionString, ct);

        if (!result.CanConnect)
            throw new DefaultException($"Não foi possível abrir o banco da empresa: {result.Error}");

        if (result.PendingMigrations.Count > 0)
            throw new DefaultException(
                $"O banco {result.Database} tem migrations pendentes ({string.Join(", ", result.PendingMigrations)}). " +
                "Aplique-as com `dotnet ef database update --context AppDbContext --connection \"<string de conexão>\"` " +
                "antes de cadastrar a empresa.");

        return result;
    }
}
```

`CompaniesUpdateService(CommonDbContext db, ICompanyDatabaseProbe probe, ICompanyCatalog catalog, ILogger<CompaniesUpdateService> logger)`:
```csharp
    public async Task<Company?> ExecuteAsync(string code, Company entity)
    {
        var existing = await db.Companies.FirstOrDefaultAsync(c => c.Code == code)
                       ?? throw new NotFoundException("Empresa não encontrada.");

        var name = (entity.CompanyName ?? string.Empty).Trim();
        if (name.Length == 0) throw new DefaultException("Informe o nome da empresa.");

        // Senha mascarada = o admin não mexeu nela: mantém a armazenada.
        var connectionString = CompanyConnectionStrings.MergeMaskedPassword(entity.ConnectionString ?? string.Empty, existing.ConnectionString);

        if (!string.Equals(connectionString, existing.ConnectionString, StringComparison.Ordinal))
        {
            await CompanyDatabaseRules.ValidateConnectionStringAsync(connectionString, probe, commonConnectionString: null);
        }

        // Code é imutável: é a chave que sessões, perfis e jobs carregam.
        existing.CompanyName = name;
        existing.ConnectionString = connectionString;
        existing.SapCompanyDb = entity.SapCompanyDb;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.Log(LogLevel.Error, "Failed to update entity.");
            throw new ApplicationException("Error updating entity due to concurrency issues.");
        }

        catalog.Invalidate();
        return existing;
    }
```
(Se preferir manter a checagem da COMMON também no update, injete `IConfiguration` como no create.)

`CompaniesDeleteService(CommonDbContext db, ICompanyCatalog catalog, IStringLocalizer<Resource> resource, ILogger<CompaniesDeleteService> logger)`:
```csharp
    public async Task ExecuteAsync(string code)
    {
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Code == code)
            ?? throw new NotFoundException(resource["COMPANY_NOT_FOUND"].Value);

        if (string.Equals(code, catalog.DefaultCompanyCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException("A empresa padrão da instalação não pode ser excluída.");

        if (await db.UserProfiles.AnyAsync(p => p.CompanyCode == code))
            throw new ApplicationException("A empresa tem perfis de usuário atribuídos. Remova-os antes de excluí-la.");

        if (await db.UserSessions.AnyAsync(s => s.CompanyCode == code && s.IsActive))
            throw new ApplicationException("Há sessões ativas nesta empresa. Aguarde ou encerre-as antes de excluí-la.");

        db.Companies.Remove(company);
        await db.SaveChangesAsync();
        catalog.Invalidate();
    }
```

`CompaniesGetService`: `QueryAll()` e `GetByIdAsync` mascaram:
```csharp
    public async Task<Company?> GetByIdAsync(string code)
    {
        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(p => p.Code == code);
        return company is null ? null : Masked(company);
    }

    /// <summary>Poucas linhas: materializa e mascara em memória (o $filter do OData passa a ser aplicado em memória).</summary>
    public IQueryable<Company> QueryAll() =>
        db.Companies.AsNoTracking().AsEnumerable().Select(Masked).AsQueryable();

    private static Company Masked(Company c) => new()
    {
        Code = c.Code,
        CompanyName = c.CompanyName,
        ConnectionString = CompanyConnectionStrings.MaskPassword(c.ConnectionString),
        SapCompanyDb = c.SapCompanyDb,
    };
```
Atenção ao `Patch` do controller: ele faz `GetByIdAsync` (mascarado) → `patch.Patch(t)` → `updateService.ExecuteAsync(code, t)`; como o update funde a senha mascarada com a armazenada, o PATCH continua correto.

`CompaniesTestConnectionService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos.Common;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Companies;

public class CompaniesTestConnectionService(CommonDbContext db, ICompanyDatabaseProbe probe)
{
    /// <param name="code">Empresa já cadastrada (para fundir a senha mascarada), ou nulo no cadastro novo.</param>
    public async Task<CompanyTestConnectionResult> ExecuteAsync(string? code, string connectionString)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            var stored = await db.Companies.AsNoTracking().Where(c => c.Code == code).Select(c => c.ConnectionString).FirstOrDefaultAsync();
            if (stored is not null)
                connectionString = CompanyConnectionStrings.MergeMaskedPassword(connectionString, stored);
        }

        if (CompanyConnectionStrings.IsMasked(connectionString))
            return new CompanyTestConnectionResult { Success = false, Message = "Informe a senha real do banco para testar a conexão." };

        var result = await probe.ProbeAsync(connectionString);

        return new CompanyTestConnectionResult
        {
            Success = result.CanConnect && result.PendingMigrations.Count == 0,
            Server = result.Server,
            Database = result.Database,
            PendingMigrations = [.. result.PendingMigrations],
            Message = !result.CanConnect
                ? $"Falha ao conectar: {result.Error}"
                : result.PendingMigrations.Count > 0
                    ? $"Conectou em {result.Server}/{result.Database}, mas há {result.PendingMigrations.Count} migration(s) pendente(s)."
                    : $"Conexão OK em {result.Server}/{result.Database}. Banco com as migrations em dia.",
        };
    }
}
```

- [ ] **Step 7: Perfil por empresa no CRUD de perfis do usuário**

`UsersProfilesCreateService(CommonDbContext db, ICompanyContext companyContext, IStringLocalizer<Resource> resource, ILogger<UsersProfilesCreateService> logger)` — antes do `try`:
```csharp
        entity.UserId = user.Id;
        entity.CompanyCode = string.IsNullOrWhiteSpace(entity.CompanyCode)
            ? companyContext.CompanyCode
            : entity.CompanyCode.Trim().ToUpperInvariant();

        if (!await db.Companies.AnyAsync(c => c.Code == entity.CompanyCode))
            throw new DefaultException("Empresa inválida.");

        // O índice único (UserId, ProfileCode, CompanyCode) pega isto no SQL Server, mas a mensagem
        // de negócio precisa existir — e o InMemory dos testes não aplica índice nenhum.
        if (await db.UserProfiles.AnyAsync(p => p.UserId == user.Id && p.ProfileCode == entity.ProfileCode && p.CompanyCode == entity.CompanyCode))
            throw new DefaultException("Este perfil já está atribuído ao usuário nesta empresa.");
```
`UsersProfilesUpdateService` (ambas as sobrecargas): após `userProfile.ProfileCode = entity.ProfileCode;` acrescentar
```csharp
        if (!string.IsNullOrWhiteSpace(entity.CompanyCode))
            userProfile.CompanyCode = entity.CompanyCode.Trim().ToUpperInvariant();
```

- [ ] **Step 8: Controllers, action OData e registros**

`SiagroB1.Web/Actions/Companies/CompaniesTestConnectionController.cs`:
```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Companies;
using SiagroB1.Domain.Constants;
using SiagroB1.Web.Extensions;

namespace SiagroB1.Web.Actions.Companies;

public class CompaniesTestConnectionController(
    CompaniesTestConnectionService service,
    IUserPermissions userPermissions) : ODataController
{
    [HttpPost("odata/CompaniesTestConnection")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        if (!User.IsAdmin() && !await userPermissions.HasRoleAsync(User.Identity?.Name ?? string.Empty, Roles.Admin))
            return StatusCode(StatusCodes.Status403Forbidden, "Você não tem permissão de administrador para gerenciar empresas.");

        if (parameters is null || !parameters.ContainsKey("ConnectionString"))
            return BadRequest("Parâmetro obrigatório: ConnectionString.");

        var code = parameters.TryGetValue("Code", out var c) ? c?.ToString() : null;

        return Ok(await service.ExecuteAsync(code, parameters["ConnectionString"]?.ToString() ?? string.Empty));
    }
}
```

`CompaniesController`: injetar `IUserPermissions userPermissions`; acrescentar
```csharp
    /// <summary>Empresas são o catálogo de BANCOS do deploy: só administrador mexe (flag legada ou papel ADMIN).</summary>
    private async Task<bool> IsAdminAsync() =>
        User.IsAdmin() || await userPermissions.HasRoleAsync(User.Identity?.Name ?? string.Empty, Roles.Admin);

    private ObjectResult Forbidden() =>
        StatusCode(StatusCodes.Status403Forbidden, "Você não tem permissão de administrador para gerenciar empresas.");
```
e no início de **cada** action (`Get` ×2, `Post`, `Put`, `Delete`, `Patch`): `if (!await IsAdminAsync()) return Forbidden();` (os dois `Get` passam a `async Task<ActionResult<...>>`). No `Post`, trate também `DefaultException` já tratada e `ApplicationException` → `BadRequest`.

`ODataConfigurations.cs`, junto das outras actions (após `branchNfeSettingsTestConnection`):
```csharp
        var companiesTestConnection = modelBuilder.Action("CompaniesTestConnection");
        companiesTestConnection.Parameter<string>("Code");
        companiesTestConnection.Parameter<string>("ConnectionString");
        companiesTestConnection.Returns<CompanyTestConnectionResult>();
```
(`using SiagroB1.Domain.Dtos.Common;`).

`ServiceCollectionExtensions.AddApplicationServices()`, perto das `Companies*`:
```csharp
        services.AddScoped<ICompanyDatabaseProbe, CompanyDatabaseProbe>();
        services.AddScoped<CompaniesTestConnectionService>();
```

- [ ] **Step 9: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Companies|FullyQualifiedName~UsersProfilesCreateServiceTests"` → PASS.
Run: `dotnet build SiagroB1.sln` → sucesso. Há um teste que inspeciona o EDM (`ODataConfigurations`); rode `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Edm|FullyQualifiedName~OData"` para garantir que a action nova não quebrou nada.

- [ ] **Step 10: Commit**

```bash
git add SiagroB1.Application/Services/Companies SiagroB1.Application/Services/UsersProfiles SiagroB1.Domain/Dtos/Common/CompanyTestConnectionResult.cs SiagroB1.Web/Controllers/CompaniesController.cs SiagroB1.Web/Actions/Companies SiagroB1.Web/ODataConfig/ODataConfigurations.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/Support/FakeCompanyDatabaseProbe.cs SiagroB1.Application.Tests/Companies SiagroB1.Application.Tests/Security/UsersProfilesCreateServiceTests.cs
git commit -m "feat(master-data): cadastro de empresas valida o banco e perfil nasce na empresa da sessão

Cadastrar uma empresa é cadastrar um banco: a string tem de abrir, não pode
ser a COMMON e o banco tem de estar com as migrations em dia, senão o próximo
boot do Web recusaria subir. A senha nunca sai mascarada do servidor, e a
edição que volta mascarada mantém a armazenada (spec 2026-10-16). Só
administrador gerencia empresas; a action CompaniesTestConnection alimenta o
botão da tela.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 10: Menu "Empresas" e verificação final do backend

**Files:**
- Create: `SiagroB1.Migrations/CommonContext/<timestamp>_AddCompaniesMenu.cs` (escrito à mão, sem Designer — como `20261015214856_AddSalesContractWashoutApprovalMenu.cs`; gere o esqueleto com `dotnet ef migrations add AddCompaniesMenu --context CommonDbContext ...` e substitua o corpo, mantendo o Designer gerado)

**Interfaces:**
- Produces: `MENU_ITEMS` key `companies` ("Empresas", pai `admin`, Order 0) + `ROLE_MENUS` ADMIN. A key **tem de ser igual** ao nome da rota `companies` do frontend.

- [ ] **Step 1: Gerar e escrever a migration**

Run (PowerShell): `$env:ASPNETCORE_ENVIRONMENT="Ceagui-Development"; dotnet ef migrations add AddCompaniesMenu --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`
Expected: migration vazia gerada (nenhuma mudança de modelo). Substitua o corpo:
```csharp
    /// <summary>
    /// Menu do cadastro de Empresas (bancos do deploy), no grupo "Administração" (spec 2026-10-16). Order 0 para
    /// aparecer antes de Filiais: empresa vem antes de filial, como no SAP B1.
    ///
    /// A Key PRECISA ser igual ao name da rota no manifest.json do frontend: App.controller.ts navega com
    /// navTo(item.getKey()). Sem ROLE_MENUS o item não aparece.
    /// </summary>
    public partial class AddCompaniesMenu : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey"],
                values: new object[,]
                {
                    { "companies", "Empresas", "sap-icon://building", true, false, 0, "admin" },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "6F2B9C1E-5D3A-4B8F-9E7C-2A1D4F6B8C0E", "ADMIN", "companies" },
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "6F2B9C1E-5D3A-4B8F-9E7C-2A1D4F6B8C0E");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "companies");
        }
    }
```

- [ ] **Step 2: Aplicar e conferir**

Run: `$env:ASPNETCORE_ENVIRONMENT="Ceagui-Development"; dotnet ef database update --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`
Expected: aplicada. `SELECT * FROM MENU_ITEMS WHERE [Key]='companies'` devolve a linha.

- [ ] **Step 3: Suíte inteira e build**

Run: `dotnet build SiagroB1.sln` → sucesso.
Run: `dotnet test SiagroB1.Application.Tests` → todos PASS (anote o total).
Run: `dotnet test SiagroB1.Fiscal.Tests` → PASS (não deve ter sido tocado).

- [ ] **Step 4: Subir a stack e verificar o caminho do usuário (com o frontend da branch correspondente)**

Com `siagro-b1-frontend` na branch `feature/multi-company-login` (plano próprio) e `yarn start:dev`:
1. `dotnet run --project SiagroB1.Web --launch-profile ceagui`, idem Gateway e Reports. Log do Web: bootstrap sem criar (já criada na Task 6), `Nenhuma migration pendente em AppDbContext[0001]`, sem `ManyServiceProvidersCreatedWarning`.
2. Criar o segundo banco local: `CREATE DATABASE LMCEREAIS_SIAGRO_DEV` e `dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --connection "Server=localhost;Database=LMCEREAIS_SIAGRO_DEV;Integrated Security=True;TrustServerCertificate=True"`.
3. Como admin: Administração → Empresas → Incluir `0002` / "LM CEREAIS" com a string acima; "Testar conexão" mostra servidor/banco e "migrations em dia"; salvar. Tentar uma string com senha errada → mensagem de falha; tentar a string da COMMON → recusa.
4. Usuários → usuário não-admin: aba Perfis, incluir perfil com Empresa `0002` apenas.
5. Sair; login: Select mostra CEAGUI e LM CEREAIS; o usuário é recusado em CEAGUI ("Usuário sem acesso à empresa CEAGUI.") e aceito em LM CEREAIS; ShellBar mostra "LM CEREAIS"; menu só com o que o perfil dá.
6. Em LM CEREAIS, cadastrar uma filial e conferir que ela não aparece ao logar em CEAGUI.
7. `/hangfire`: disparar `contract-notification-sweep` manualmente; na aba *Succeeded* o runner mostra as duas empresas no log (ou nenhum erro).
8. Reports: imprimir um PDF em cada empresa; cabeçalho com o nome de cada uma; colocar `wwwroot/images/logo-0002.png` e conferir o logo.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Migrations/CommonContext
git commit -m "feat(master-data): menu Empresas no grupo Administração

Entrada do cadastro de empresas (bancos do deploy) para o papel ADMIN, antes
de Filiais — empresa vem antes de filial, como no SAP B1 (spec 2026-10-16).

Atenção: a Key companies tem de ser igual ao nome da rota no manifest.json
do frontend.

DB: AddCompaniesMenu

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

## Notas de deploy (para o `README`/memória, não para o código)

0. ANTES do deploy, rodar `SELECT * FROM COMPANIES` em cada COMMON: a tabela existe desde a migration Initial. Se tiver linhas antigas, o bootstrap NÃO cadastra a empresa padrão e os perfis/sessões legados são vinculados ao código configurado ou à primeira linha — limpe ou corrija as linhas antes.
1. Aplicar as duas migrations da COMMON (`AddCompanyCodeToUserSessionsAndUsersProfiles`, `AddCompaniesMenu`) antes de subir.
2. Opcional: `Company:Code` e `Company:Name` no `appsettings.<env>.json` do Web e do Gateway (padrão `0001` e `CompanyName`).
3. Primeiro boot: a empresa padrão é cadastrada sozinha; conferir `COMPANIES` e `USERS_PROFILES.CompanyCode`.
4. Hangfire cria o schema `HangFire` na COMMON (o login de runtime precisa de permissão de DDL uma vez, na primeira subida, ou crie o schema `HangFire` antes).
6. Rollback: o `Down()` de `AddCompanyCodeToUserSessionsAndUsersProfiles` recria o índice único sem filtro `(UserId, ProfileCode)` e FALHA assim que um usuário tiver o mesmo perfil em duas empresas — remova as duplicatas antes de reverter.
5. Segunda empresa: criar o banco → `dotnet ef database update --context AppDbContext --connection "<cs>"` → cadastrar na tela → atribuir perfis → (opcional) `wwwroot/images/logo-<Code>.png` no Reports.
   - Dados de referência: o banco novo nasce sem `STATES` (e sem qualquer cadastro); antes da primeira filial copie os 27 estados do banco de uma empresa existente — ex.: `INSERT INTO LMCEREAIS_SIAGRO_DEV.dbo.STATES SELECT * FROM CEAGUI_SIAGRO_DEV.dbo.STATES` (ajuste a lista de colunas se a tabela tiver identity) — ou cadastre-os pela tela Administração → Estados. Só `MUNICIPALITIES` (via `Seeds/MunicipalitySeed.cs`), o menu/perfis (CommonContext) e `SeedShipmentLoadDocNumber` vêm de migration; as demais tabelas de referência (STATES, COUNTRIES, USAGES, TAXES, UNITS_OF_MEASURE...) nascem vazias. Follow-up sugerido: opção 'copiar dados de referência' no cadastro da empresa.
