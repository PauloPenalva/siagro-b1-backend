using System.Collections.Concurrent;

namespace SiagroB1.Web.Sockets;

public class PendingRequestStore
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new();

    public Task<string> Create(string requestId)
    {
        var tcs = new TaskCompletionSource<string>();
        _pending[requestId] = tcs;
        return tcs.Task;
    }

    public void Resolve(string requestId, string payload)
    {
        if (_pending.TryRemove(requestId, out var tcs))
            tcs.SetResult(payload);
    }
}