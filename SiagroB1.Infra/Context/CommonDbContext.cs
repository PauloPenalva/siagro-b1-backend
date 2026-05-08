using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities.Common;

namespace SiagroB1.Infra.Context
{
    public class CommonDbContext(DbContextOptions<CommonDbContext> options) : DbContext(options)
    {
        public DbSet<Company> Companies { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<UserSession> UserSessions { get; set; }
        public DbSet<MenuItem> MenuItems { get; set; }
        
        public DbSet<Profile> Profiles { get; set; }
        
        public DbSet<ProfileRole> ProfileRoles { get; set; }
        
        public DbSet<UserProfile>  UserProfiles { get; set; }
    }
}