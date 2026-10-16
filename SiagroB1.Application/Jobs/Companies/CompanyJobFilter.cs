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
