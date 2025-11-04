using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Infra.Context
{
    public class CommonDbContext(DbContextOptions<CommonDbContext> options) : DbContext(options)
    {
    }
}