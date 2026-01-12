using System.Data;
using System.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;
using SiagroB1.Reports.DI;
using SiagroB1.Security.Middlewares;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment())
{
    builder.Host.UseWindowsService();
    builder.Logging.AddEventLog();
}

builder.Services.AddDbContext<CommonDbContext>(options => 
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("SiagroCommon"),
        b =>
        {
            b.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        })
);

builder.Services.AddDbContext<AppDbContext>(options =>
    {
        options.UseSqlServer(
            builder.Configuration.GetConnectionString("SiagroDB"),
            b =>
            {
                b.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });
        options.EnableSensitiveDataLogging();
    }
);

builder.Services.AddScoped<IDbConnection>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("SiagroDB");
    return new SqlConnection(connectionString);
});

FastReport.Utils.RegisteredObjects.AddConnection(typeof(FastReport.Data.MsSqlDataConnection));

builder.Services.AddScoped<IUnitOfWork,  UnitOfWork>();
builder.Services.AddReportServices();

builder.Services.AddControllers();

var app = builder.Build();

app.UseRouting();
app.UseCookieAuth();
//app.UseAuthentication();
//app.UseAuthorization();

app.UseRouting()
    .UseEndpoints(endpoints => endpoints
        .MapControllers()
    );

app.Run();
