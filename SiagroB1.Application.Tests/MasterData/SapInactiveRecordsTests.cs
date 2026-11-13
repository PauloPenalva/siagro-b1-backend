using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Infra.Context;
using SapAgentService = SiagroB1.Application.Services.SAP.AgentService;
using SapBusinessPartnerService = SiagroB1.Application.Services.SAP.BusinessPartnerService;
using SapItemService = SiagroB1.Application.Services.SAP.ItemService;
using SapWarehouseService = SiagroB1.Application.Services.SAP.WarehouseService;

namespace SiagroB1.Application.Tests.MasterData;

/// <summary>
/// GAC-1196: os serviços do modo SAPB1 passam a dizer quando o cadastro está inativo no SAP
/// (validFor/frozenFor com intervalo de datas; OSLP.Active). O registro inativo continua sendo
/// devolvido — documentos antigos precisam resolver o nome —, só que marcado.
/// </summary>
public class SapInactiveRecordsTests
{
    private static readonly DateTime Yesterday = DateTime.Today.AddDays(-1);
    private static readonly DateTime Tomorrow = DateTime.Today.AddDays(1);

    private static readonly string[] InactiveCodes = ["EXPIRED", "FROZEN", "FROZENNOW", "NOTYET"];

    private static SapErpDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SapErpDbContext>().UseInMemoryDatabase($"sap-{Guid.NewGuid()}").Options);

    private static async Task<SapErpDbContext> SeededPartners()
    {
        var sap = NewContext();
        sap.BusinessPartners.AddRange(
            new BusinessPartner { CardCode = "ACTIVE", CardName = "ATIVO", QryGroup23 = "Y", ValidFor = "Y", FrozenFor = "N" },
            new BusinessPartner { CardCode = "NOFLAGS", CardName = "SEM MARCACAO", QryGroup23 = "Y", ValidFor = "N", FrozenFor = "N" },
            new BusinessPartner { CardCode = "FROZEN", CardName = "INATIVO", QryGroup23 = "Y", ValidFor = "N", FrozenFor = "Y" },
            new BusinessPartner { CardCode = "FROZENNOW", CardName = "INATIVO NO PERIODO", QryGroup23 = "Y", FrozenFor = "Y", FrozenFrom = Yesterday, FrozenTo = Tomorrow },
            new BusinessPartner { CardCode = "FROZENPAST", CardName = "INATIVACAO VENCIDA", QryGroup23 = "Y", FrozenFor = "Y", FrozenTo = Yesterday },
            new BusinessPartner { CardCode = "FROZENNEXT", CardName = "INATIVACAO FUTURA", QryGroup23 = "Y", FrozenFor = "Y", FrozenFrom = Tomorrow },
            new BusinessPartner { CardCode = "EXPIRED", CardName = "VALIDADE VENCIDA", QryGroup23 = "Y", ValidFor = "Y", ValidTo = Yesterday },
            new BusinessPartner { CardCode = "NOTYET", CardName = "VALIDADE FUTURA", QryGroup23 = "Y", ValidFor = "Y", ValidFrom = Tomorrow },
            new BusinessPartner { CardCode = "VALIDNOW", CardName = "VALIDO NO PERIODO", QryGroup23 = "Y", ValidFor = "Y", ValidFrom = Yesterday, ValidTo = Tomorrow });
        await sap.SaveChangesAsync();
        return sap;
    }

    [Fact]
    public async Task Partners_are_flagged_inactive_following_the_sap_rule()
    {
        var service = new SapBusinessPartnerService(await SeededPartners(), NullLogger<SapBusinessPartnerService>.Instance);

        var all = await service.QueryAll().ToListAsync();

        Assert.Equal(9, all.Count);
        Assert.Equal(InactiveCodes, all.Where(x => x.Inactive).Select(x => x.CardCode).Order());
    }

    [Fact]
    public async Task Partner_value_help_filter_leaves_only_the_active_ones()
    {
        var service = new SapBusinessPartnerService(await SeededPartners(), NullLogger<SapBusinessPartnerService>.Instance);

        var active = await service.QueryAll().Where(x => x.Inactive == false).Select(x => x.CardCode).ToListAsync();

        Assert.Equal(5, active.Count);
        Assert.DoesNotContain("FROZEN", active);
    }

    [Fact]
    public async Task Inactive_partner_is_still_found_by_code_so_old_documents_resolve_it()
    {
        var service = new SapBusinessPartnerService(await SeededPartners(), NullLogger<SapBusinessPartnerService>.Instance);

        var partner = await service.GetByIdAsync("FROZEN");

        Assert.NotNull(partner);
        Assert.True(partner.Inactive);
        Assert.False((await service.GetByIdAsync("ACTIVE"))!.Inactive);
    }

    [Fact]
    public async Task Warehouses_carry_the_flag_of_their_partner()
    {
        var service = new SapWarehouseService(
            await SeededPartners(), NullLogger<SapWarehouseService>.Instance, new ConfigurationBuilder().Build());

        var all = await service.QueryAll().ToListAsync();

        Assert.Equal(InactiveCodes, all.Where(x => x.Inactive).Select(x => x.Code).Order());
        Assert.True((await service.GetByIdAsync("FROZEN"))!.Inactive);
        Assert.False((await service.GetByIdAsync("ACTIVE"))!.Inactive);
    }

    [Fact]
    public async Task Items_are_flagged_inactive_and_still_found_by_code()
    {
        var sap = NewContext();
        sap.Items.AddRange(
            new Item { ItemCode = "SOJA", ItemName = "SOJA", Enabled = "SIM", ValidFor = "Y", FrozenFor = "N" },
            new Item { ItemCode = "MILHO", ItemName = "MILHO", Enabled = "SIM", ValidFor = "N", FrozenFor = "Y" });
        await sap.SaveChangesAsync();
        var service = new SapItemService(sap, NullLogger<SapItemService>.Instance, new ConfigurationBuilder().Build());

        var all = await service.QueryAll().ToListAsync();

        Assert.Equal(["MILHO"], all.Where(x => x.Inactive).Select(x => x.ItemCode));
        Assert.True((await service.GetByIdAsync("MILHO"))!.Inactive);
        Assert.False((await service.GetByIdAsync("SOJA"))!.Inactive);
    }

    [Fact]
    public async Task Agent_unchecked_as_active_in_sap_is_inactive()
    {
        var sap = NewContext();
        sap.Agents.AddRange(
            new Agent { Code = 1, Name = "ATIVO", Inactive = "N", Active = "Y" },
            new Agent { Code = 2, Name = "DESMARCADO", Inactive = "N", Active = "N" },
            new Agent { Code = 3, Name = "TRAVADO", Inactive = "Y", Active = "Y" },
            new Agent { Code = 4, Name = "SEM VALOR", Inactive = null, Active = null });
        await sap.SaveChangesAsync();
        var service = new SapAgentService(sap, NullLogger<SapAgentService>.Instance);

        var all = (await service.QueryAll().ToListAsync()).ToDictionary(x => x.Code, x => x.Inactive);

        Assert.Equal("N", all[1]);
        Assert.Equal("Y", all[2]);
        Assert.Equal("Y", all[3]);
        Assert.Equal("N", all[4]);
        Assert.Equal("Y", (await service.GetByIdAsync(2))!.Inactive);
        Assert.Equal([1, 4], await service.QueryAll().Where(x => x.Inactive == "N").Select(x => x.Code).ToListAsync());
    }
}
