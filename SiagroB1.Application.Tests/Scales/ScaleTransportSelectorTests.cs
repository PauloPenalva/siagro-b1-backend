using SiagroB1.Commons.Scales;

namespace SiagroB1.Application.Tests.Scales;

public class ScaleTransportSelectorTests
{
    [Theory]
    [InlineData("Serial")]
    [InlineData("serial")]
    [InlineData(" SERIAL ")]
    public void Serial_connection_type_selects_the_serial_transport(string connectionType)
    {
        Assert.True(ScaleTransportSelector.IsSerial(connectionType));
    }

    [Theory]
    [InlineData(null)]   // servidor antigo, que não manda o campo
    [InlineData("")]
    [InlineData("Tcp")]
    [InlineData("Bluetooth")]
    public void Anything_else_falls_back_to_tcp(string? connectionType)
    {
        Assert.False(ScaleTransportSelector.IsSerial(connectionType));
    }
}
