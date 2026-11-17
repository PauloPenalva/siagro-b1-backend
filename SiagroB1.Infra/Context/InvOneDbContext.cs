using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities.SAP;

namespace SiagroB1.Infra.Context;

/// <summary>Banco do add-on fiscal InvOne (DBInvOne). Somente leitura, sem migrations.</summary>
public class InvOneDbContext(DbContextOptions<InvOneDbContext> options) : DbContext(options)
{
    public DbSet<InvOneProcess> Processes { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var process = modelBuilder.Entity<InvOneProcess>();

        if (Database.IsSqlServer())
        {
            // No SQL Server a tabela não tem chave e todas as colunas são anuláveis: uma chave no
            // modelo tornaria a coluna obrigatória e um NULL derrubaria a leitura inteira.
            process.HasNoKey();
        }
        else
        {
            // Só para os testes (provider em memória não aceita entidade sem chave). A chave é uma
            // propriedade sombra gerada pelo EF, então nenhuma coluna real vira chave.
            process.Property<Guid>("RowId").ValueGeneratedOnAdd();
            process.HasKey("RowId");
        }
    }
}
