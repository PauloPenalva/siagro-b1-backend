using Hangfire.Common;
using SiagroB1.Application.Jobs;
using SiagroB1.Application.Jobs.Companies;

namespace SiagroB1.Application.Tests.Accounting.Offline;

public class OfflinePostingJobTests
{
    [Fact]
    public void Job_runs_in_company_scope()
    {
        var job = Job.FromExpression<OfflinePostingJob>(j => j.ExecuteAsync("0002", Guid.Empty, CancellationToken.None));
        Assert.Equal("0002", CompanyJobFilter.ExtractCompanyCode(job));
    }
}
