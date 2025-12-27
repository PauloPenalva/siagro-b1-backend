using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities.Common;

namespace SiagroB1.Infra.Context
{
    public class CommonDbContext(DbContextOptions<CommonDbContext> options) : DbContext(options)
    {
        public DbSet<Company> Companies { get; set; }
        public DbSet<User> Users { get; set; }
    }
}