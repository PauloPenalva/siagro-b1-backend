using System.Net.NetworkInformation;
using SiagroB1.Installer.Core.Settings;

namespace SiagroB1.Installer.Core.Services;

public static class PortChecker
{
    /// <summary>Mensagens em pt-BR; lista vazia = portas válidas.</summary>
    public static IReadOnlyList<string> Validate(
        IReadOnlyDictionary<ServiceKind, int> ports,
        Func<int, bool> isInUse,
        IReadOnlySet<int> ownPorts)
    {
        var errors = new List<string>();
        var ordered = ports.OrderBy(p => Order(p.Key)).ToList();

        foreach (var (kind, port) in ordered)
        {
            if (port is < 1 or > 65535)
            {
                errors.Add($"A porta do {kind} ({port}) deve estar entre 1 e 65535.");
            }
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            for (var j = i + 1; j < ordered.Count; j++)
            {
                if (ordered[i].Value == ordered[j].Value)
                {
                    errors.Add($"As portas do {ordered[i].Key} e do {ordered[j].Key} não podem ser iguais.");
                }
            }
        }

        foreach (var port in ordered.Select(p => p.Value).Distinct())
        {
            if (port is >= 1 and <= 65535 && !ownPorts.Contains(port) && isInUse(port))
            {
                errors.Add($"A porta {port} já está em uso por outro programa.");
            }
        }

        return errors;
    }

    public static bool IsInUse(int port) =>
        IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == port);

    private static int Order(ServiceKind kind) => kind switch
    {
        ServiceKind.Gateway => 0,
        ServiceKind.Web => 1,
        _ => 2
    };
}
