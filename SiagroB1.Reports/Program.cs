using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra.Context;
using SiagroB1.Reports.Services;
using SiagroB1.Security.Middlewares;

var builder = WebApplication.CreateBuilder(args);

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

FastReport.Utils.RegisteredObjects.AddConnection(typeof(FastReport.Data.MsSqlDataConnection));

builder.Services.AddScoped<IFastReportService, FastReportService>();

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
