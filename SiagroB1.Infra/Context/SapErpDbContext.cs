using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities.SAP;

namespace SiagroB1.Infra.Context
{
    public class SapErpDbContext(DbContextOptions<SapErpDbContext> options) : DbContext(options)
    {
        public DbSet<BusinessPartner> BusinessPartners { get; set; }
        public DbSet<Item> Items { get; set; }
        public DbSet<UnitOfMeasure> UnitsOfMeasure { get; set; }
        public DbSet<Agent> Agents { get; set; }

        /// <summary>Centros de custo do SAP (OPRC), somente leitura.</summary>
        public DbSet<CostCenter> CostCenters { get; set; }

        /// <summary>Plano de contas do SAP (OACT), somente leitura.</summary>
        public DbSet<LedgerAccount> LedgerAccounts { get; set; }

        public DbSet<Usage> Usages { get; set; }
        
        public DbSet<Address> Addresses { get; set; }

        /// <summary>Extensão fiscal do endereço (CRD7): CNPJ, CPF e IE.</summary>
        public DbSet<AddressTaxExtension> AddressTaxExtensions { get; set; }

        /// <summary>Municípios (OCNT), referenciados por CRD1.County.</summary>
        public DbSet<County> Counties { get; set; }

        /// <summary>Pessoas de contato do parceiro (OCPR).</summary>
        public DbSet<ContactPerson> ContactPersons { get; set; }

        /// <summary>Cadastro de usuários do SAP (OUSR), espelhado em USERS quando Erp = SAPB1.</summary>
        public DbSet<SapUser> SapUsers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Address>()
                .HasKey(a => new { a.CardCode, a.AddressName, a.AdresType });

            modelBuilder.Entity<AddressTaxExtension>()
                .HasKey(a => new { a.CardCode, a.AddressName, a.AddressType });

            modelBuilder.Entity<Address>()
                .HasOne(a => a.BusinessPartner)
                .WithMany(bp => bp.Addresses)
                .HasForeignKey(a => a.CardCode);
        }

    }
}