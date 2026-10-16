namespace SiagroB1.Infra.Companies;

/// <summary>
/// Empresa "ambiente" do fluxo de execução corrente — o canal dos jobs do Hangfire, onde não há
/// HttpContext. O filtro de job grava aqui ANTES de o activator abrir o escopo de DI, e escopos
/// aninhados (ContractDraftsReconcileJob abre um por minuta) herdam o valor pelo ExecutionContext.
/// </summary>
public static class CompanyAmbient
{
    private static readonly AsyncLocal<string?> Current = new();

    public static string? CompanyCode => Current.Value;

    /// <summary>Define e devolve o restaurador — use com <c>using</c> no runner.</summary>
    public static IDisposable Use(string? code)
    {
        var previous = Current.Value;
        Current.Value = code;
        return new Restore(previous);
    }

    /// <summary>Para o filtro do Hangfire, que não tem onde guardar o IDisposable entre OnPerforming e OnPerformed.</summary>
    public static void Set(string? code) => Current.Value = code;

    private sealed class Restore(string? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
