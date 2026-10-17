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
