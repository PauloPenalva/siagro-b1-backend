using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Mdfe;

/// <summary>Filial, municípios e CT-e autorizados para os testes do MDF-e.</summary>
public static class MdfeTestSupport
{
    public static IConfiguration Config(string erp = "STANDALONE") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Erp"] = erp }).Build();

    public static async Task SeedBranchAsync(UnitOfWork db, bool requiresCiot = false)
    {
        db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "TRANSP", ShortName = "TRANSP", TaxId = "12345678000195", StateCode = "SP", ZipCode = "01001000",
        });
        db.Context.BranchMdfeSettings.Add(new BranchMdfeSettings { BranchCode = "01", Enabled = true, DefaultCargoType = "05", RequiresCiot = requiresCiot });
        db.Context.BranchTransportSettings.Add(new BranchTransportSettings
        {
            BranchCode = "01", CompanyRntrc = "11382119", InsuranceResponsible = InsuranceResponsible.Issuer,
            InsurerTaxId = "11222333000181", InsurerName = "SEGURADORA", PolicyNumber = "APOL1",
        });
        db.Context.Municipalities.AddRange(
            new Municipality { Code = "3550308", Name = "SAO PAULO", StateAbbreviation = "SP" },
            new Municipality { Code = "3509502", Name = "CAMPINAS", StateAbbreviation = "SP" },
            new Municipality { Code = "4106902", Name = "CURITIBA", StateAbbreviation = "PR" });
        await db.SaveChangesAsync();
    }

    public static async Task<TransportDocument> AddCteAsync(UnitOfWork db, int number, string endCity = "3509502",
        string startCity = "3550308", string startUf = "SP", string plate = "ABC1D23", string unit = "01", decimal quantity = 1000m,
        string takerTaxId = "99888777000166", string takerName = "TOMADOR", decimal cargoValue = 100m,
        CteStatus status = CteStatus.Authorized, string branch = "01")
    {
        var doc = new TransportDocument
        {
            BranchCode = branch, Series = 1, Number = number, Status = status, AccessKey = number.ToString().PadLeft(44, '5'),
            StartMunicipalityCode = startCity, StartState = startUf, EndMunicipalityCode = endCity, EndState = "SP",
            TractionPlate = plate, TrailerPlates = "TRL1A11", DriverCpf = "52998224725", DriverName = "JOAO",
            PredominantProduct = "SOJA", CargoValue = cargoValue, TakerRole = (int)CtePartyRole.Sender,
            VehicleSnapshotJson = JsonSerializer.Serialize(new object[]
            {
                new { Plate = plate, Kind = 0, Renavam = "12345678901", TareWeight = 9000, CapacityKg = 30000, CapacityM3 = 0, WheelType = 3, BodyType = 3, State = "SP" },
                new { Plate = "TRL1A11", Kind = 1, Renavam = "10987654321", TareWeight = 7000, CapacityKg = 35000, CapacityM3 = 0, WheelType = (int?)null, BodyType = 3, State = "SP" },
            }),
        };
        doc.Parties.Add(new TransportDocumentParty { Role = CtePartyRole.Sender, TaxId = takerTaxId, Name = takerName });
        doc.Quantities.Add(new TransportDocumentQuantity { UnitCode = unit, MeasureType = "PESO", Quantity = quantity });
        doc.Endorsements.Add(new TransportDocumentEndorsement { EndorsementNumber = $"AV{number}" });
        db.Context.TransportDocuments.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }
}
