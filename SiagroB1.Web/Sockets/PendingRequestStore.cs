using System.Collections.Concurrent;

namespace SiagroB1.Web.Sockets;

public class PendingRequestStore
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new();

    public Task<string> Create(string requestId)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = tcs;
        return tcs.Task;
    }

    public void Resolve(string requestId, string payload)
    {
        Console.WriteLine($"Tentando resolver: {requestId}");
        Console.WriteLine($"Pendentes: {string.Join(",", _pending.Keys)}");
        
        if (_pending.TryRemove(requestId, out var tcs))
        {
            Console.WriteLine(">>> RESOLVIDO");
            tcs.SetResult(payload);
        }
        else
        {
            Console.WriteLine(">>> NÃO ENCONTROU REQUEST");
        }
    }
}