namespace SiagroB1.Installer.Core.Services;

/// <summary>Argumentos do sc.exe. Cada par "opção=" + valor vai como dois argumentos, como o sc.exe exige.</summary>
public static class ServiceCommandBuilder
{
    public static string BinPath(string exe, string environment, int port) =>
        $"\"{exe}\" --environment {environment} --urls http://*:{port}";

    public static IReadOnlyList<string> CreateArgs(string name, string binPath, string displayName) =>
        ["create", name, "binPath=", binPath, "DisplayName=", displayName, "start=", "auto"];

    public static IReadOnlyList<string> ConfigArgs(string name, string binPath, string displayName) =>
        ["config", name, "binPath=", binPath, "DisplayName=", displayName, "start=", "auto"];

    public static IReadOnlyList<string> DescriptionArgs(string name, string description) =>
        ["description", name, description];

    public static IReadOnlyList<string> FailureArgs(string name) =>
        ["failure", name, "reset=", "86400", "actions=", "restart/60000/restart/60000/restart/60000"];

    public static IReadOnlyList<string> DeleteArgs(string name) => ["delete", name];
}
