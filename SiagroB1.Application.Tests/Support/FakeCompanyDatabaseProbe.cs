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
