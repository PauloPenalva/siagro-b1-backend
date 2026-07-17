using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Infra;

public class AppDbContextModelTests
{
    [Fact]
    public void AllColumnTypes_HaveBalancedParentheses()
    {
        // Guarda contra TypeName malformado como "DECIMAL(18,3) DEFAULT 0)" —
        // parêntese sobrando gera DDL inválido e obriga edição manual de migrations.
        // Usa o provider SqlServer apenas para materializar o modelo relacional;
        // nenhuma conexão é aberta.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options;

        using var context = new AppDbContext(options);

        // Inspeciona a annotation crua (o que o snapshot/migrations serializam),
        // não GetColumnType() — o type mapper normaliza e esconderia o defeito
        var malformed = context.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties(), (e, p) => new
            {
                Entity = e.ClrType.Name,
                Property = p.Name,
                ColumnType = (string?)p.FindAnnotation("Relational:ColumnType")?.Value,
            })
            .Where(x => x.ColumnType != null
                        && x.ColumnType.Count(c => c == '(') != x.ColumnType.Count(c => c == ')'))
            .Select(x => $"{x.Entity}.{x.Property}: \"{x.ColumnType}\"")
            .ToList();

        Assert.True(malformed.Count == 0,
            "Colunas com tipo malformado (parênteses desbalanceados):\n" + string.Join("\n", malformed));
    }
}
