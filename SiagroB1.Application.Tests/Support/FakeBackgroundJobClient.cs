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

    /// <summary>Argumentos do primeiro parâmetro de cada job — no caso, a Key da outbox.</summary>
    public List<Guid> EnqueuedOutboxKeys =>
        [.. Enqueued.Where(job => job.Args.Count > 0 && job.Args[0] is Guid)
                    .Select(job => (Guid)job.Args[0]!)];

    public string Create(Job job, IState state)
    {
        Enqueued.Add(job);
        return Guid.NewGuid().ToString();
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => true;
}
