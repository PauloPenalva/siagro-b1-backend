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
        public DbSet<Permission> Permissions { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<RolePermission> RolesPermissions { get; set; }
        public DbSet<RoleMenu> RolesMenus { get; set; }
        public DbSet<Profile> Profiles { get; set; }
        public DbSet<ProfileRole> ProfileRoles { get; set; }
        public DbSet<UserProfile>  UserProfiles { get; set; }
        public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }

        /// <summary>Largura e ordem das colunas que cada usuário ajustou nas tabelas da UI.</summary>
        public DbSet<UserTableLayout> UserTableLayouts { get; set; }

        /// <summary>Logo exibido na home sem painel. Tabela à parte para a imagem não viajar no OData de Companies.</summary>
        public DbSet<CompanyLogo> CompanyLogos { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<CompanyLogo>()
                .HasOne<Company>().WithOne().HasForeignKey<CompanyLogo>(l => l.CompanyCode).OnDelete(DeleteBehavior.Cascade);
        }

    }
}