using SiagroB1.Application.Services.MasterData;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Tests.MasterData;

/// <summary>
/// GAC-1196: cadastro inativo no SAP não pode ser escolhido, mas o documento que já o usava
/// antes da inativação continua gravando.
/// </summary>
public class InactiveRecordGuardTests
{
    private static BusinessPartnerModel Partner(bool inactive) =>
        new() { CardCode = "F00010", CardName = "FAZENDA BOA VISTA", Inactive = inactive };

    [Fact]
    public void New_record_cannot_pick_an_inactive_partner()
    {
        var error = Assert.Throws<DefaultException>(() => InactiveRecordGuard.Partner(Partner(inactive: true)));

        Assert.Equal("O parceiro F00010 - FAZENDA BOA VISTA está inativo no SAP e não pode ser usado.", error.Message);
    }

    [Fact]
    public void Switching_to_an_inactive_partner_is_blocked()
    {
        Assert.Throws<DefaultException>(() => InactiveRecordGuard.Partner(Partner(inactive: true), "F00099"));
    }

    [Fact]
    public void Record_that_already_used_the_partner_keeps_saving_after_it_was_inactivated()
    {
        var partner = Partner(inactive: true);

        Assert.Same(partner, InactiveRecordGuard.Partner(partner, "f00010 "));
    }

    [Fact]
    public void Active_and_missing_records_pass_through()
    {
        var partner = Partner(inactive: false);

        Assert.Same(partner, InactiveRecordGuard.Partner(partner));
        Assert.Null(InactiveRecordGuard.Partner(null));
        Assert.Null(InactiveRecordGuard.Item(null));
        Assert.Null(InactiveRecordGuard.Warehouse(null));
        Assert.Null(InactiveRecordGuard.Agent(null));
    }

    [Fact]
    public void Item_warehouse_and_agent_follow_the_same_rule()
    {
        var item = new ItemModel { ItemCode = "SOJA", ItemName = "SOJA EM GRAOS", Inactive = true };
        var warehouse = new WarehouseModel { Code = "ARM001", Name = "ARMAZEM GERAL", Inactive = true };
        var agent = new AgentModel { Code = 7, Name = "JOAO", Inactive = "Y" };

        Assert.Contains("O produto SOJA", Assert.Throws<DefaultException>(() => InactiveRecordGuard.Item(item)).Message);
        Assert.Contains("O armazém ARM001", Assert.Throws<DefaultException>(() => InactiveRecordGuard.Warehouse(warehouse)).Message);
        Assert.Contains("O representante 7", Assert.Throws<DefaultException>(() => InactiveRecordGuard.Agent(agent)).Message);

        Assert.Same(item, InactiveRecordGuard.Item(item, "SOJA"));
        Assert.Same(warehouse, InactiveRecordGuard.Warehouse(warehouse, "ARM001"));
        Assert.Same(agent, InactiveRecordGuard.Agent(agent, 7));
    }
}
