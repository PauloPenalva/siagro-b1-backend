using SiagroB1.Application.Services;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Scales;

public class TruckScaleValidatorTests
{
    private static TruckScale Scale(ScaleConnectionType type) => new()
    {
        Code = "TS01",
        Name = "Balança",
        Localization = "Pátio",
        ConnectionType = type
    };

    [Fact]
    public void Tcp_scale_without_any_connection_data_is_accepted()
    {
        // Linhas antigas sem IP continuam editáveis: a validação nova só vale para Serial.
        TruckScaleValidator.Validate(Scale(ScaleConnectionType.Tcp));
    }

    [Fact]
    public void Tcp_scale_ignores_invalid_serial_fields()
    {
        var scale = Scale(ScaleConnectionType.Tcp);
        scale.DataBits = 9;
        scale.StopBits = 3;
        scale.BaudRate = 0;

        TruckScaleValidator.Validate(scale);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Serial_scale_without_port_is_rejected(string? portName)
    {
        var scale = Scale(ScaleConnectionType.Serial);
        scale.SerialPortName = portName;

        var ex = Assert.Throws<DefaultException>(() => TruckScaleValidator.Validate(scale));
        Assert.Equal("Informe a porta serial da balança.", ex.Message);
    }

    [Fact]
    public void Serial_scale_with_port_and_default_parameters_is_accepted()
    {
        var scale = Scale(ScaleConnectionType.Serial);
        scale.SerialPortName = "COM3";

        TruckScaleValidator.Validate(scale);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    public void Serial_scale_accepts_seven_or_eight_data_bits(int dataBits)
    {
        var scale = Scale(ScaleConnectionType.Serial);
        scale.SerialPortName = "COM3";
        scale.DataBits = dataBits;

        TruckScaleValidator.Validate(scale);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(9)]
    public void Serial_scale_with_invalid_data_bits_is_rejected(int dataBits)
    {
        var scale = Scale(ScaleConnectionType.Serial);
        scale.SerialPortName = "COM3";
        scale.DataBits = dataBits;

        var ex = Assert.Throws<DefaultException>(() => TruckScaleValidator.Validate(scale));
        Assert.Equal("Bits de dados deve ser 7 ou 8.", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Serial_scale_with_invalid_stop_bits_is_rejected(int stopBits)
    {
        var scale = Scale(ScaleConnectionType.Serial);
        scale.SerialPortName = "COM3";
        scale.StopBits = stopBits;

        var ex = Assert.Throws<DefaultException>(() => TruckScaleValidator.Validate(scale));
        Assert.Equal("Bits de parada deve ser 1 ou 2.", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-9600)]
    public void Serial_scale_with_non_positive_baud_rate_is_rejected(int baudRate)
    {
        var scale = Scale(ScaleConnectionType.Serial);
        scale.SerialPortName = "COM3";
        scale.BaudRate = baudRate;

        var ex = Assert.Throws<DefaultException>(() => TruckScaleValidator.Validate(scale));
        Assert.Equal("Baud rate deve ser maior que zero.", ex.Message);
    }
}
