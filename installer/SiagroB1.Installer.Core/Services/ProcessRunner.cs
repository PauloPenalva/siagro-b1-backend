using System.Diagnostics;
using System.Text;

namespace SiagroB1.Installer.Core.Services;

public sealed record ProcessResult(int ExitCode, string Output);

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string file,
        IReadOnlyList<string> args,
        string? workingDir,
        IDictionary<string, string>? env,
        CancellationToken ct);
}

/// <summary>Roda um processo e junta stdout + stderr numa saída só (vai para o log, já mascarada).</summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string file,
        IReadOnlyList<string> args,
        string? workingDir,
        IDictionary<string, string>? env,
        CancellationToken ct)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir ?? "",
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }
        if (env is not null)
        {
            foreach (var (key, value) in env)
            {
                info.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = info };
        var output = new StringBuilder();
        var gate = new object();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (gate) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (gate) output.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }

        lock (gate)
        {
            return new ProcessResult(process.ExitCode, output.ToString());
        }
    }
}
