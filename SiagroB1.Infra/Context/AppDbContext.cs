using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Infra.Context
{
    public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
    {
        public DbSet<State> States { get; set; }
        public DbSet<Branch> Branchs { get; set; }
        public DbSet<UnitOfMeasure> UnitsOfMeasure { get; set; }
        public DbSet<ProcessingService> WhareHouseServices { get; set; }
        public DbSet<QualityAttrib> QualityAttribs { get; set; }
        public DbSet<ProcessingCost> ProcessingCosts { get; set; }
        public DbSet<ProcessingCostDryingParameter> ProcessingCostDryingParameters { get; set; }
        public DbSet<ProcessingCostDryingDetail> ProcessingCostDryingDetails { get; set; }
        public DbSet<ProcessingCostQualityParameter> ProcessingCostQualityParameters { get; set; }
        public DbSet<ProcessingCostServiceDetail> ProcessingCostServiceDetails { get; set; }
        public DbSet<StorageLot> StorageLots { get; set; }
        public DbSet<Warehouse> WhareHouses { get; set; }
        public DbSet<HarvestSeason> HarvestSeasons { get; set; }
        public DbSet<TruckDriver> TruckDrivers { get; set; }
        public DbSet<Truck> Trucks { get; set; }
    }

}