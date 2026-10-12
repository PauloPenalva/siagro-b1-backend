using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Warehouses;

/// <summary>
/// Em STANDALONE o armazém é o parceiro de negócio com QryGroup23 = 'Y', como no SAPB1. A tabela
/// WAREHOUSES não serve para NF-e (sem IE/endereço) e obrigava a cadastrar o armazém duas vezes
/// para emitir a remessa para depósito.
/// </summary>
public class WarehouseServiceStandaloneTests
{
    private static WarehouseService Service(UnitOfWork db) =>
        new(db.Context, NullLogger<WarehouseService>.Instance);

    private static async Task<UnitOfWork> Seeded()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.BusinessPartners.AddRange(
            new BusinessPartner
            {
                CardCode = "ARM001", CardName = "ARMAZEM GERAL", CardFName = "AG", CardType = "S",
                TaxId = "11222333000181", Notes = "OBS", QryGroup23 = "Y",
            },
            new BusinessPartner { CardCode = "F001", CardName = "PRODUTOR", CardType = "S", QryGroup23 = "N" },
            new BusinessPartner { CardCode = "F002", CardName = "LEGADO", CardType = "S", QryGroup23 = null });
        db.Context.Addresses.AddRange(
            new Address { CardCode = "ARM001", AddressName = "COBRANCA", AdresType = "B", City = "SAO PAULO", State = "SP" },
            new Address { CardCode = "ARM001", AddressName = "Z-ENTREGA", AdresType = "S", City = "CURITIBA", State = "PR" },
            new Address { CardCode = "ARM001", AddressName = "A-ENTREGA", AdresType = "S", City = "ITABERA", State = "SP" });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task QueryAll_lists_only_partners_flagged_as_warehouse()
    {
        var db = await Seeded();

        var codes = await Service(db).QueryAll().Select(w => w.Code).ToListAsync();

        Assert.Equal(new[] { "ARM001" }, codes);
    }

    [Fact]
    public async Task QueryAll_projects_partner_fields_and_first_ship_to_address()
    {
        var db = await Seeded();

        var warehouse = await Service(db).QueryAll().SingleAsync();

        Assert.Equal("ARMAZEM GERAL", warehouse.Name);
        Assert.Equal("AG", warehouse.FName);
        Assert.Equal("11222333000181", warehouse.TaxId);
        Assert.Equal("OBS", warehouse.Notes);
        Assert.Equal("ITABERA", warehouse.City);
        Assert.Equal("SP", warehouse.State);
    }

    [Fact]
    public async Task GetById_returns_null_for_partner_not_flagged()
    {
        var db = await Seeded();

        Assert.Null(await Service(db).GetByIdAsync("F001"));
        Assert.Equal("ARMAZEM GERAL", (await Service(db).GetByIdAsync("ARM001"))!.Name);
    }

    [Fact]
    public async Task GetAll_returns_the_flagged_partners()
    {
        var db = await Seeded();

        var all = (await Service(db).GetAllAsync()).ToList();

        Assert.Single(all);
        Assert.Equal("ARM001", all[0].Code);
    }

    [Fact]
    public async Task Create_update_and_delete_are_refused_with_guidance()
    {
        var db = await Seeded();
        var model = new WarehouseModel { Code = "NOVO", Name = "NOVO" };

        var create = await Assert.ThrowsAsync<DefaultException>(() => Service(db).CreateAsync(model));
        var update = await Assert.ThrowsAsync<DefaultException>(() => Service(db).UpdateAsync("ARM001", model));
        var delete = await Assert.ThrowsAsync<DefaultException>(() => Service(db).DeleteAsync("ARM001"));

        Assert.Equal("Cadastre o armazém no Parceiro de Negócio, marcando 'Armazém'.", create.Message);
        Assert.Equal(create.Message, update.Message);
        Assert.Equal(create.Message, delete.Message);
        Assert.Equal(3, await db.Context.BusinessPartners.CountAsync());
    }
}
