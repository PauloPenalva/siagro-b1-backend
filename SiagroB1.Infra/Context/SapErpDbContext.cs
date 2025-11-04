using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities.SAP;

namespace SiagroB1.Infra.Context
{
    public class SapErpDbContext(DbContextOptions<SapErpDbContext> options) : DbContext(options)
    {
        public  DbSet<BusinessPartner> Participantes { get; set; }
        public  DbSet<Item> Produtos { get; set; }
    }
}