using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services;

/// <summary>
/// Regras do cadastro da balança. Só a conexão Serial é validada: o cadastro TCP/IP segue como
/// sempre foi, para não travar a edição de balanças antigas.
/// </summary>
public static class TruckScaleValidator
{
    public static void Validate(TruckScale scale)
    {
        if (scale.ConnectionType != ScaleConnectionType.Serial)
            return;

        if (string.IsNullOrWhiteSpace(scale.SerialPortName))
            throw new DefaultException("Informe a porta serial da balança.");

        if (scale.BaudRate is <= 0)
            throw new DefaultException("Baud rate deve ser maior que zero.");

        if (scale.DataBits is not (null or 7 or 8))
            throw new DefaultException("Bits de dados deve ser 7 ou 8.");

        if (scale.StopBits is not (null or 1 or 2))
            throw new DefaultException("Bits de parada deve ser 1 ou 2.");
    }
}
