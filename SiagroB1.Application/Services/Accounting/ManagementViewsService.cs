using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Accounting;

public interface IManagementViewsService
{
    IQueryable<ManagementView> QueryAll();

    /// <summary>Grava a visão pelo código, substituindo as linhas.</summary>
    Task<ManagementView> SaveAsync(ManagementView view, string user);
}

/// <summary>Cadastro da visão gerencial (CTS): valida superior, faixa e fórmula antes de gravar.</summary>
public class ManagementViewsService(IUnitOfWork db) : IManagementViewsService
{
    public IQueryable<ManagementView> QueryAll() => db.Context.ManagementViews.AsNoTracking();

    public async Task<ManagementView> SaveAsync(ManagementView view, string user)
    {
        var code = view.Code?.Trim() ?? string.Empty;
        if (code.Length == 0) throw new DefaultException("Informe o código da visão.");
        if (string.IsNullOrWhiteSpace(view.Name)) throw new DefaultException("Informe o nome da visão.");

        Validate(view.Lines);

        var row = await db.Context.ManagementViews.Include(v => v.Lines).FirstOrDefaultAsync(v => v.Code == code);
        if (row is null)
        {
            row = new ManagementView { Code = code };
            db.Context.ManagementViews.Add(row);
        }
        else
        {
            db.Context.ManagementViewLines.RemoveRange(row.Lines);
            row.Lines.Clear();
        }

        row.Name = view.Name.Trim();
        row.Type = view.Type;
        row.UpdatedAt = DateTime.Now;
        row.UpdatedBy = user;
        foreach (var line in view.Lines.OrderBy(l => l.Order).ThenBy(l => l.Code, StringComparer.Ordinal))
        {
            line.Id = Guid.NewGuid();
            line.ViewCode = code;
            row.Lines.Add(line);
            // Chave Guid já preenchida: sem o Add explícito o EF trata a linha como existente (UPDATE).
            db.Context.ManagementViewLines.Add(line);
        }

        await db.SaveChangesAsync();
        return row;
    }

    private static void Validate(IReadOnlyList<ManagementViewLine> lines)
    {
        var byCode = new Dictionary<string, ManagementViewLine>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            line.Code = line.Code?.Trim() ?? string.Empty;
            line.ParentCode = string.IsNullOrWhiteSpace(line.ParentCode) ? null : line.ParentCode.Trim();
            line.AccountFrom = string.IsNullOrWhiteSpace(line.AccountFrom) ? null : line.AccountFrom.Trim();
            line.AccountTo = string.IsNullOrWhiteSpace(line.AccountTo) ? null : line.AccountTo.Trim();
            if (line.Code.Length == 0) throw new DefaultException("Informe o código de todas as linhas.");
            if (!byCode.TryAdd(line.Code, line)) throw new DefaultException($"Código de linha {line.Code} repetido.");
        }

        foreach (var line in lines)
        {
            var at = $"Linha {line.Code}";
            if (line.ParentCode is { } parent &&
                (!byCode.TryGetValue(parent, out var p) || p.Kind != ManagementViewLineKind.Total))
                throw new DefaultException($"{at}: a linha superior {parent} precisa ser do tipo Total.");

            switch (line.Kind)
            {
                // Faixa vazia vale zero (a semente vem sem faixas); só uma ponta é erro de digitação.
                case ManagementViewLineKind.Accounts when (line.AccountFrom is null) != (line.AccountTo is null):
                    throw new DefaultException($"{at}: informe a conta inicial e a final.");
                case ManagementViewLineKind.Formula:
                    if (!ManagementViewFormula.IsValid(line.Formula))
                        throw new DefaultException(
                            $"{at}: fórmula inválida. Use códigos de linha somados ou subtraídos, como 01 - 02 + 05.");
                    foreach (var (_, reference) in ManagementViewFormula.Parse(line.Formula!))
                        if (!byCode.ContainsKey(reference))
                            throw new DefaultException($"{at}: a fórmula cita a linha {reference}, que não existe na visão.");
                    break;
            }
        }

        // Ciclo: fórmulas que dependem de si mesmas, direta ou indiretamente (também via filhas de um Total).
        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 1 visitando, 2 pronto
        foreach (var code in byCode.Keys) Visit(code, []);

        void Visit(string code, List<string> path)
        {
            if (state.GetValueOrDefault(code) == 2) return;
            path.Add(code);
            if (state.GetValueOrDefault(code) == 1)
                throw new DefaultException($"Fórmula circular: {string.Join(" → ", path)}.");
            state[code] = 1;
            var line = byCode[code];
            IEnumerable<string> deps = line.Kind switch
            {
                ManagementViewLineKind.Formula => ManagementViewFormula.Parse(line.Formula!).Select(t => t.Code),
                ManagementViewLineKind.Total => lines.Where(l => l.ParentCode == code).Select(l => l.Code),
                _ => [],
            };
            foreach (var dep in deps) Visit(dep, path);
            path.RemoveAt(path.Count - 1);
            state[code] = 2;
        }
    }
}
