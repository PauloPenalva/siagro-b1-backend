namespace SiagroB1.Application.Jobs;

public interface ISapUserSyncJob
{
    Task ExecuteAsync(CancellationToken ct = default);
}
