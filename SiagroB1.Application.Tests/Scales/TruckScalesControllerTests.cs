using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;
using SiagroB1.Web.Controllers;

namespace SiagroB1.Application.Tests.Scales;

public class TruckScalesControllerTests
{
    private static AppDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new AppDbContext(options);
    }

    private static TruckScalesController Controller(AppDbContext context) =>
        new(new TruckScaleService(new UnitOfWork(context), NullLogger<TruckScaleService>.Instance));

    /// <summary>O ODataController devolve o motivo no corpo de erro OData, que é o que a tela exibe.</summary>
    private static string RefusalMessage(IActionResult result) =>
        Assert.IsType<BadRequestODataResult>(result).Error.Message;

    private static TruckScale SerialScale(string? portName) => new()
    {
        Code = "TS01",
        Name = "Balança",
        Localization = "Pátio",
        ConnectionType = ScaleConnectionType.Serial,
        SerialPortName = portName
    };

    [Fact]
    public async Task Post_of_serial_scale_without_port_answers_400_with_the_reason()
    {
        // Sem isso a recusa vira 500 e a tela mostra "erro de comunicação" em vez do motivo.
        await using var context = CreateContext(Guid.NewGuid().ToString());

        var result = await Controller(context).Post(SerialScale(null));

        Assert.Equal("Informe a porta serial da balança.", RefusalMessage(result));
        Assert.Empty(context.TruckScales);
    }

    [Fact]
    public async Task Post_of_duplicate_code_answers_400_with_the_reason()
    {
        var database = Guid.NewGuid().ToString();

        await using (var seed = CreateContext(database))
        {
            seed.TruckScales.Add(SerialScale("COM3"));
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext(database);

        var result = await Controller(context).Post(SerialScale("COM4"));

        Assert.Equal("Balança TS01 já cadastrada.", RefusalMessage(result));
    }

    [Fact]
    public async Task Post_of_valid_serial_scale_is_created()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());

        var result = await Controller(context).Post(SerialScale("COM3"));

        Assert.IsNotType<BadRequestODataResult>(result);
        Assert.Equal("COM3", Assert.Single(context.TruckScales).SerialPortName);
    }

    [Fact]
    public async Task Patch_switching_to_serial_without_port_answers_400_and_keeps_the_scale()
    {
        var database = Guid.NewGuid().ToString();

        await using (var seed = CreateContext(database))
        {
            seed.TruckScales.Add(new TruckScale
            {
                Code = "TS01", Name = "Balança", Localization = "Pátio",
                IpAddress = "192.168.0.10", Port = 4001
            });
            await seed.SaveChangesAsync();
        }

        await using (var context = CreateContext(database))
        {
            var patch = new Delta<TruckScale>();
            patch.TrySetPropertyValue(nameof(TruckScale.ConnectionType), ScaleConnectionType.Serial);

            var result = await Controller(context).Patch("TS01", patch);

            Assert.Equal("Informe a porta serial da balança.", RefusalMessage(result));
        }

        await using var check = CreateContext(database);
        Assert.Equal(ScaleConnectionType.Tcp, (await check.TruckScales.SingleAsync()).ConnectionType);
    }
}
