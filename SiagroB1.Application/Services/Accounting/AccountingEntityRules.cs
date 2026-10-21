using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.Accounting;

/// <summary>
/// Regras de hierarquia e vigência compartilhadas por Centro de Custo (CTT) e Item Contábil (CTD).
/// A comparação de códigos é case-insensitive porque podem existir códigos legados em minúsculas.
/// </summary>
public static class AccountingEntityRules
{
    public const int CodeMaxLength = 20;

    public static string NormalizeCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    public static string? NormalizeOptionalCode(string? code)
    {
        var normalized = NormalizeCode(code);
        return normalized.Length == 0 ? null : normalized;
    }

    public static void ValidateCode(string code)
    {
        if (code.Length == 0)
        {
            throw new DefaultException("Informe o código.");
        }

        if (code.Length > CodeMaxLength)
        {
            throw new DefaultException($"O código pode ter no máximo {CodeMaxLength} caracteres.");
        }
    }

    public static void ValidateValidity(DateTime? validFrom, DateTime? validTo)
    {
        if (validFrom.HasValue && validTo.HasValue && validTo.Value.Date < validFrom.Value.Date)
        {
            throw new DefaultException("A vigência final deve ser posterior à inicial.");
        }
    }

    /// <summary>
    /// Valida pai (existe, é sintético, sem ciclo) e a troca para analítico (sem subordinados).
    /// <paramref name="code"/> e <paramref name="parentCode"/> já vêm normalizados.
    /// </summary>
    public static async Task ValidateAsync<T>(
        IQueryable<T> set,
        string code,
        AccountingEntityClass cls,
        string? parentCode,
        Func<T, string?> parentOf,
        Func<T, AccountingEntityClass> classOf,
        Func<T, string> codeOf) where T : class
    {
        var all = (await set.AsNoTracking().ToListAsync()).ToList();
        var byCode = all.ToDictionary(x => codeOf(x).ToUpperInvariant());

        if (cls == AccountingEntityClass.Analytic
            && all.Any(x => string.Equals(parentOf(x), code, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(codeOf(x), code, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DefaultException($"Não é possível tornar analítico: {code} tem subordinados.");
        }

        if (parentCode == null)
        {
            return;
        }

        if (string.Equals(parentCode, code, StringComparison.OrdinalIgnoreCase))
        {
            throw new DefaultException("O código não pode ser superior dele mesmo.");
        }

        if (!byCode.TryGetValue(parentCode.ToUpperInvariant(), out var parent))
        {
            throw new DefaultException($"O superior {parentCode} não existe.");
        }

        if (classOf(parent) != AccountingEntityClass.Synthetic)
        {
            throw new DefaultException("O superior precisa ser sintético.");
        }

        // Sobe a cadeia: se o próprio código aparece acima do pai, a troca fecharia um ciclo.
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = parent;
        while (true)
        {
            var up = parentOf(current);
            if (string.IsNullOrWhiteSpace(up) || !visited.Add(up))
            {
                break;
            }

            if (string.Equals(up, code, StringComparison.OrdinalIgnoreCase))
            {
                throw new DefaultException("O código não pode ser superior dele mesmo.");
            }

            if (!byCode.TryGetValue(up.ToUpperInvariant(), out current!))
            {
                break;
            }
        }
    }

    public static async Task EnsureNoChildrenAsync<T>(
        IQueryable<T> set, string code, Func<T, string?> parentOf) where T : class
    {
        var all = await set.AsNoTracking().ToListAsync();
        if (all.Any(x => string.Equals(parentOf(x), code, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DefaultException($"Não é possível excluir: {code} tem subordinados.");
        }
    }
}
