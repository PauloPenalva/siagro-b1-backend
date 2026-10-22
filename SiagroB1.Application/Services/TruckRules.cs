using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services;

/// <summary>
/// Coerência do cadastro de veículo na gravação. Nenhum campo do CT-e/MDF-e é obrigatório aqui:
/// quem cobra é a emissão. Em SAPB1 não há veículo de terceiro (o parceiro vive no SAP).
/// </summary>
public static class TruckRules
{
    public static void Validate(Truck truck, bool standalone)
    {
        if (truck.Kind == TruckKind.Trailer && truck.WheelType is not null)
            throw new DefaultException("Reboque não tem tipo de rodado: o rodado é só do veículo de tração.");

        if (truck.CapacityKg < 0 || truck.CapacityM3 < 0 || truck.TareWeight < 0)
            throw new DefaultException("Tara e capacidade não podem ser negativas.");

        truck.Renavam = Blank(truck.Renavam);
        if (truck.Renavam is not null && (truck.Renavam.Length is < 9 or > 11 || !truck.Renavam.All(char.IsAsciiDigit)))
            throw new DefaultException("O RENAVAM tem de 9 a 11 dígitos.");

        if (!standalone || truck.OwnerType == TruckOwnerType.Own)
        {
            truck.OwnerType = TruckOwnerType.Own;
            truck.OwnerCardCode = null;
            truck.OwnerRntrc = null;
            truck.OwnerKind = null;
            return;
        }

        truck.OwnerCardCode = Blank(truck.OwnerCardCode);
        if (truck.OwnerCardCode is null)
            throw new DefaultException("Informe o parceiro proprietário do veículo de terceiro.");

        truck.OwnerRntrc = Blank(truck.OwnerRntrc);
        if (truck.OwnerRntrc is not null && (truck.OwnerRntrc.Length != 8 || !truck.OwnerRntrc.All(char.IsAsciiDigit)))
            throw new DefaultException("O RNTRC tem 8 dígitos.");
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
