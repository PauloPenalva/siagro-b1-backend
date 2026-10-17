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
