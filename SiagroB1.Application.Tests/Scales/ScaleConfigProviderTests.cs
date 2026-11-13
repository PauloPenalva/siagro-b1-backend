using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;
using SiagroB1.Web.Sockets.TruckScale;

namespace SiagroB1.Application.Tests.Scales;

public class ScaleConfigProviderTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static async Task<ScaleConfigPayload?> ConfigFor(TruckScale scale)
    {
        await using var context = CreateContext();
        context.TruckScales.Add(scale);
        await context.SaveChangesAsync();

        return await new ScaleConfigProvider(context).GetAsync(scale.Code);
    }

    [Fact]
    public async Task Tcp_scale_sends_ip_and_port()
    {
        var config = await ConfigFor(new TruckScale
        {
            Code = "TS01", Name = "Balança", Localization = "Pátio",
            IpAddress = "192.168.0.10", Port = 4001
        });

        Assert.NotNull(config);
        Assert.Equal("Tcp", config.ConnectionType);
        Assert.Equal("192.168.0.10", config.Ip);
        Assert.Equal(4001, config.Port);
    }

    [Fact]
    public async Task Serial_scale_with_empty_parameters_sends_the_defaults()
    {
        var config = await ConfigFor(new TruckScale
        {
            Code = "TS02", Name = "Balança", Localization = "Pátio",
            ConnectionType = ScaleConnectionType.Serial, SerialPortName = "COM3"
        });

        Assert.NotNull(config);
        Assert.Equal("Serial", config.ConnectionType);
        Assert.Equal("COM3", config.SerialPortName);
        Assert.Equal(9600, config.BaudRate);
        Assert.Equal(8, config.DataBits);
        Assert.Equal("None", config.Parity);
        Assert.Equal(1, config.StopBits);
    }

    [Fact]
    public async Task Serial_scale_sends_the_registered_parameters()
    {
        var config = await ConfigFor(new TruckScale
        {
            Code = "TS03", Name = "Balança", Localization = "Pátio",
            ConnectionType = ScaleConnectionType.Serial, SerialPortName = "COM5",
            BaudRate = 4800, DataBits = 7, Parity = ScaleSerialParity.Even, StopBits = 2
        });

        Assert.NotNull(config);
        Assert.Equal("COM5", config.SerialPortName);
        Assert.Equal(4800, config.BaudRate);
        Assert.Equal(7, config.DataBits);
        Assert.Equal("Even", config.Parity);
        Assert.Equal(2, config.StopBits);
    }
}
