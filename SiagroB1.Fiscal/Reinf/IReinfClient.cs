using System.Security.Cryptography.X509Certificates;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Fiscal.Reinf;

public interface IReinfClient
{
    Task<ReinfBatchResult> SendAsync(string batchXml, X509Certificate2 cert, ReinfEnvironment env, CancellationToken ct);
    Task<ReinfBatchResult> QueryAsync(string protocol, X509Certificate2 cert, ReinfEnvironment env, CancellationToken ct);
}
