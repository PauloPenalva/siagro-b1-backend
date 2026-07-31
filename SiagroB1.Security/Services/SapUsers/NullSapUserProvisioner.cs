using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Security.Services.SapUsers;

/// <summary>
/// Implementação usada fora do modo SAPB1: não há OUSR de onde provisionar.
///
/// Existe para que login e recuperação de senha chamem sempre a mesma coisa, sem espalhar
/// verificações de modo de integração pelo código de autenticação.
/// </summary>
public class NullSapUserProvisioner : ISapUserProvisioner
{
    public Task EnsureAsync(string usernameOrEmail, CancellationToken ct = default) => Task.CompletedTask;
}
