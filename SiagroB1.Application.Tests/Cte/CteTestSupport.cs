using System.Globalization;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Cte;

/// <summary>Cadastro de parceiros em memória com IE e endereço IBGE (o fake geral não traz IE).</summary>
public sealed class CtePartnerCatalog : IBusinessPartnerService
{
    private readonly Dictionary<string, BusinessPartnerModel> _partners = new();

    public CtePartnerCatalog Add(string cardCode, string name, string taxId, string? municipalityCode, string state,
        string? stateRegistration = "123456789", int? paymentConditionCode = null)
    {
        _partners[cardCode] = new BusinessPartnerModel
        {
            CardCode = cardCode, CardName = name, TaxId = taxId, StateRegistration = stateRegistration,
            PaymentConditionCode = paymentConditionCode,
            Addresses =
            [
                new AddressModel
                {
                    CardCode = cardCode, AddressName = "FATURAMENTO", AdresType = "B", Street = "RUA A", StreetNumber = "1",
                    Block = "CENTRO", ZipCode = "01001000", City = "CIDADE", State = state, MunicipalityCode = municipalityCode,
                },
            ],
        };
        return this;
    }

    public Task<BusinessPartnerModel?> GetByIdAsync(string code) => Task.FromResult(_partners.GetValueOrDefault(code));
    public IQueryable<BusinessPartnerModel> QueryAll() => _partners.Values.AsQueryable();
    public Task<IEnumerable<BusinessPartnerModel>> GetAllAsync() => Task.FromResult<IEnumerable<BusinessPartnerModel>>(_partners.Values);
    public Task<BusinessPartnerModel> CreateAsync(BusinessPartnerModel entity) => throw new NotImplementedException();
    public Task<BusinessPartnerModel?> UpdateAsync(string code, BusinessPartnerModel entity) => throw new NotImplementedException();
    public Task<bool> DeleteAsync(string code) => throw new NotImplementedException();
    public Task<bool> DeleteAsyncWithTransaction(string code, Func<BusinessPartnerModel, Task>? preDeleteAction = null) =>
        throw new NotImplementedException();
    public Task<Dictionary<string, SupplierInfo>> LoadSuppliersAsync(IReadOnlyCollection<string> cardCodes) =>
        throw new NotImplementedException();
}

public static class CteTestKeys
{
    /// <summary>Chave de NF-e modelo 55 sintética com DV correto.</summary>
    public static string Nfe(int number, string cnpj = "11222333000181")
    {
        var body = "35" + "2610" + cnpj + "55" + "001" + number.ToString("000000000", CultureInfo.InvariantCulture) + "1" + "12345678";
        var sum = 0;
        var weight = 2;
        for (var i = body.Length - 1; i >= 0; i--)
        {
            sum += (body[i] - '0') * weight;
            weight = weight == 9 ? 2 : weight + 1;
        }
        var rest = sum % 11;
        return body + (rest < 2 ? 0 : 11 - rest);
    }

    public static async Task SeedBranchAndMunicipalitiesAsync(UnitOfWork db, string defaultUnit = "00")
    {
        db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "TRANSPORTADORA", ShortName = "TRANSP", TaxId = "12345678000195", StateCode = "SP",
            StateRegistration = "111222333444", TaxRegime = TaxRegime.Normal, IssuesNfe = true,
        });
        db.Context.BranchCteSettings.Add(new BranchCteSettings { BranchCode = "01", Enabled = true, DefaultQuantityUnit = defaultUnit });
        db.Context.Municipalities.AddRange(
            new Municipality { Code = "3550308", Name = "SAO PAULO", StateAbbreviation = "SP" },
            new Municipality { Code = "3509502", Name = "CAMPINAS", StateAbbreviation = "SP" },
            new Municipality { Code = "4106902", Name = "CURITIBA", StateAbbreviation = "PR" },
            new Municipality { Code = "2927408", Name = "SALVADOR", StateAbbreviation = "BA" });
        await db.SaveChangesAsync();
    }
}
