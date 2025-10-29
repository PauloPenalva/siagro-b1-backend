using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Infra.Context
{

    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Participante> Participantes { get; set; }
        public DbSet<Filial> Filiais { get; set; }
        public DbSet<ContaContabil> ContasContabeis { get; set; }
        public DbSet<UnidadeMedida> UnidadesMedida { get; set; }
        public DbSet<Produto> Produtos { get; set; }
        public DbSet<ServicoArmazem> ServicosArmazem { get; set; }
        public DbSet<CaracteristicaQualidade> CaracteristicasQualidade { get; set; }
        public DbSet<TabelaCusto> TabelasCusto { get; set; }
        public DbSet<TabelaCustoDescontoSecagem> TabelasCustoDescontoSecagem { get; set; }
        public DbSet<TabelaCustoValorSecagem> TabelasCustoValorSecagem { get; set; }
        public DbSet<TabelaCustoQualidade> TabelasCustoQualidade { get; set; }
        public DbSet<TabelaCustoServico> TabelasCustoServico { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
        }
    }

}