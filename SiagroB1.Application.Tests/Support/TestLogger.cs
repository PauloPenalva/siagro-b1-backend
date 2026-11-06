using Microsoft.Extensions.Logging;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Minimal ILogger that captures formatted log messages for assertions.
/// </summary>
public class TestLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    /// <summary>Exceção passada a cada entrada, na mesma ordem de <see cref="Entries"/> (nula quando não houve).</summary>
    public List<Exception?> Exceptions { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception)));
        Exceptions.Add(exception);
    }
}
