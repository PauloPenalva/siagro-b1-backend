using System.Text;
using SiagroB1.Installer.Core.Settings;

namespace SiagroB1.Installer.Core.Execution;

/// <summary>Grava &lt;base&gt;\logs\setup-&lt;data&gt;.log (sempre mascarado) e repassa cada linha para a tela.</summary>
public sealed class SetupLog : IProgress<string>, IDisposable
{
    private readonly StreamWriter _writer;
    private readonly Action<string>? _forward;
    private readonly object _gate = new();

    public SetupLog(string basePath, DateTime startedAt, Action<string>? forward = null)
    {
        var dir = Path.Combine(basePath, "logs");
        Directory.CreateDirectory(dir);
        FilePath = Path.Combine(dir, $"setup-{startedAt:yyyyMMdd-HHmmss}.log");
        _writer = new StreamWriter(FilePath, append: true, new UTF8Encoding(false)) { AutoFlush = true };
        _forward = forward;
    }

    public string FilePath { get; }

    public void Report(string value)
    {
        var masked = SecretMasker.MaskText(value);
        lock (_gate)
        {
            _writer.WriteLine($"{DateTime.Now:HH:mm:ss} {masked}");
        }
        _forward?.Invoke(masked);
    }

    public void Dispose() => _writer.Dispose();
}
