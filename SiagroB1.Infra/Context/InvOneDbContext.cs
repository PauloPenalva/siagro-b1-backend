using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities.SAP;

namespace SiagroB1.Infra.Context;

/// <summary>Banco do add-on fiscal InvOne (DBInvOne). Somente leitura, sem migrations.</summary>
public class InvOneDbContext(DbContextOptions<InvOneDbContext> options) : DbContext(options)
{
    public DbSet<InvOneProcess> Processes { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Chave só para o EF/InMemory; no banco a tabela não tem chave. Ver InvOneProcess.
        modelBuilder.Entity<InvOneProcess>()
            .HasKey(p => new { p.CompanyId, p.DocType, p.DocEntry, p.StatusId });
    }
}
