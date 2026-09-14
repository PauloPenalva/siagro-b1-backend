using Microsoft.AspNetCore.OData.Results;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Web.Controllers;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.PurchaseContracts.Washouts;

public class PurchaseContractWashoutWebTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void The_entity_set_is_exposed()
    {
        Assert.NotNull(BuildModel().EntityContainer.FindEntitySet("PurchaseContractsWashouts"));
    }

    [Fact]
    public void The_contract_exposes_the_washed_volumes_and_the_navigation()
    {
        var contractType = BuildModel().SchemaElements.OfType<IEdmEntityType>().Single(t => t.Name == "PurchaseContract");

        Assert.NotNull(contractType.FindProperty("WashedOutVolume"));
        Assert.NotNull(contractType.FindProperty("WashedOutUnfixedVolume"));
        Assert.NotNull(contractType.FindProperty("Washouts"));
    }

    [Theory]
    [InlineData("PurchaseContractsWashoutCreate", "PurchaseContractKey,Washout")]
    [InlineData("PurchaseContractsWashoutApproval", "Key,Comments")]
    [InlineData("PurchaseContractsWashoutReject", "Key,Comments")]
    [InlineData("PurchaseContractsWashoutReverse", "Key,Reason")]
    public void The_actions_declare_their_parameters(string action, string parameters)
    {
        var edmAction = BuildModel().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == action);

        foreach (var parameter in parameters.Split(','))
            Assert.Contains(edmAction.Parameters, p => p.Name == parameter);
    }

    [Fact]
    public async Task The_pending_queue_lists_only_washouts_in_approval()
    {
        var contract = WashoutTestData.Contract();
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsWashouts.AddRange(
            WashoutTestData.Washout(contract, unfixedVolume: 1m, sequence: 1),
            WashoutTestData.Washout(contract, unfixedVolume: 1m, status: PurchaseContractWashoutStatus.Approved, sequence: 2));
        await _db.Context.SaveChangesAsync();

        var pending = new PurchaseContractsWashoutsGetService(_db.Context).QueryPending().ToList();

        Assert.Equal(PurchaseContractWashoutStatus.InApproval, Assert.Single(pending).Status);
    }

    [Fact]
    public async Task Get_by_key_returns_a_single_result_so_expand_works()
    {
        var contract = WashoutTestData.Contract();
        var washout = WashoutTestData.Washout(contract, unfixedVolume: 1m);
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsWashouts.Add(washout);
        await _db.Context.SaveChangesAsync();

        var result = new PurchaseContractsWashoutsController(new PurchaseContractsWashoutsGetService(_db.Context))
            .GetByKey(washout.Key);

        var single = Assert.IsType<SingleResult<PurchaseContractWashout>>(result);
        Assert.Equal(washout.Key, Assert.Single(single.Queryable).Key);
    }
}
