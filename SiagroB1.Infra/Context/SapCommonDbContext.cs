using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Infra.Context
{
    public class SapCommonDbContext(DbContextOptions<SapCommonDbContext> options) : DbContext(options)
    {
    }
}