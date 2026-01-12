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
        
        public DbSet<Usage> Usages { get; set; }
    }
}