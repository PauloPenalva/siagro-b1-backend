using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities.SAP;

namespace SiagroB1.Infra.Context
{
    public class SapCommonDbContext(DbContextOptions<SapCommonDbContext> options) : DbContext(options)
    {
    }
}