using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// <see cref="IBackgroundJobClient"/> de teste: só coleta o que foi enfileirado, sem storage.
/// Permite afirmar QUAIS linhas da outbox o varredor mandou processar.
/// </summary>
public sealed class FakeBackgroundJobClient : IBackgroundJobClient
{
    public List<Job> Enqueued { get; } = [];

    /// <summary>A Key da outbox de cada job: o primeiro argumento Guid (o primeiro string é a empresa).</summary>
    public List<Guid> EnqueuedOutboxKeys =>
        [.. Enqueued.Select(job => job.Args.OfType<Guid>().FirstOrDefault()).Where(key => key != Guid.Empty)];

    /// <summary>A empresa de cada job enfileirado (parâmetro `companyCode`).</summary>
    public List<string> EnqueuedCompanyCodes =>
        [.. Enqueued.Select(job => job.Args.OfType<string>().FirstOrDefault()).Where(code => code is not null)!];

    public string Create(Job job, IState state)
    {
        Enqueued.Add(job);
        return Guid.NewGuid().ToString();
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => true;
}
