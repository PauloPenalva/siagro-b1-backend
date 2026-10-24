namespace SiagroB1.Application.Services.Reinf;

/// <summary>Versão do aplicativo informada em verProc dos eventos.</summary>
public sealed record ReinfAppInfo(string Version);

public interface IReinfService
{
    Task<ReinfPeriodOverview> GetOverviewAsync(string taxIdRoot, string period, CancellationToken ct);

    /// <summary>Gera, assina e envia os eventos pendentes do período. Retorna as chaves dos lotes.</summary>
    Task<IReadOnlyList<Guid>> TransmitAsync(string taxIdRoot, string period, string user, CancellationToken ct);

    Task QueryBatchAsync(Guid batchKey, CancellationToken ct);

    Task<Guid> SendRegistrationAsync(string taxIdRoot, string user, CancellationToken ct);

    Task<Guid> CloseAsync(string taxIdRoot, string period, string user, CancellationToken ct);

    Task<Guid> ReopenAsync(string taxIdRoot, string period, string user, CancellationToken ct);

    /// <summary>
    /// Devolve ao envio os eventos sem confirmação da Receita (Unconfirmed) do período e do R-1000; período nulo
    /// libera só o R-1000. Retorna quantos.
    /// </summary>
    Task<int> ReleaseUnconfirmedAsync(string taxIdRoot, string? period, CancellationToken ct);
}
