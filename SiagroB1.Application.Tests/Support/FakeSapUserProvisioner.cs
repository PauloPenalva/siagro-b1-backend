using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Registra as chamadas de provisionamento e, opcionalmente, executa uma ação (para simular o
/// usuário aparecendo no banco vindo do OUSR).
/// </summary>
public class FakeSapUserProvisioner : ISapUserProvisioner
{
    public List<string> Calls { get; } = [];

    public Func<string, Task>? OnEnsure { get; set; }

    public async Task EnsureAsync(string usernameOrEmail, CancellationToken ct = default)
    {
        Calls.Add(usernameOrEmail);

        if (OnEnsure is not null)
        {
            await OnEnsure(usernameOrEmail);
        }
    }
}
