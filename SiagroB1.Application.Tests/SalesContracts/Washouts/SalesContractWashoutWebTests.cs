using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Web.Actions.SalesContracts;
using SiagroB1.Web.Controllers;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.SalesContracts.Washouts;

public class SalesContractWashoutWebTests
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
        Assert.NotNull(BuildModel().EntityContainer.FindEntitySet("SalesContractsWashouts"));
    }

    [Fact]
    public void The_contract_exposes_the_washed_volumes_and_the_navigation()
    {
        var contractType = BuildModel().SchemaElements.OfType<IEdmEntityType>().Single(t => t.Name == "SalesContract");

        Assert.NotNull(contractType.FindProperty("WashedOutVolume"));
        Assert.NotNull(contractType.FindProperty("WashedOutUnfixedVolume"));
        Assert.NotNull(contractType.FindProperty("Washouts"));
    }

    [Theory]
    [InlineData("SalesContractsWashoutCreate", "SalesContractKey,Washout")]
    [InlineData("SalesContractsWashoutApproval", "Key,Comments")]
    [InlineData("SalesContractsWashoutReject", "Key,Comments")]
    [InlineData("SalesContractsWashoutReverse", "Key,Reason")]
    public void The_actions_declare_their_parameters(string action, string parameters)
    {
        var edmAction = BuildModel().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == action);

        foreach (var parameter in parameters.Split(','))
            Assert.Contains(edmAction.Parameters, p => p.Name == parameter);
    }

    [Fact]
    public async Task The_pending_queue_lists_only_washouts_in_approval()
    {
        var contract = SalesWashoutTestData.Contract(ContractType.ToBeDetermined);
        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesContractsWashouts.AddRange(
            SalesWashoutTestData.Washout(contract, unfixedVolume: 1m, sequence: 1),
            SalesWashoutTestData.Washout(contract, unfixedVolume: 1m, status: SalesContractWashoutStatus.Approved, sequence: 2));
        await _db.Context.SaveChangesAsync();

        var pending = new SalesContractsWashoutsGetService(_db.Context).QueryPending().ToList();

        Assert.Equal(SalesContractWashoutStatus.InApproval, Assert.Single(pending).Status);
    }

    [Fact]
    public async Task Get_by_key_returns_a_single_result_so_expand_works()
    {
        var contract = SalesWashoutTestData.Contract(ContractType.ToBeDetermined);
        var washout = SalesWashoutTestData.Washout(contract, unfixedVolume: 1m);
        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesContractsWashouts.Add(washout);
        await _db.Context.SaveChangesAsync();

        var result = new SalesContractsWashoutsController(new SalesContractsWashoutsGetService(_db.Context))
            .GetByKey(washout.Key);

        var single = Assert.IsType<SingleResult<SalesContractWashout>>(result);
        Assert.Equal(washout.Key, Assert.Single(single.Queryable).Key);
    }

    [Fact]
    public async Task A_business_refusal_returns_an_odata_error_payload()
    {
        var context = _db.Context;
        var controller = new SalesContractsWashoutRejectController(new SalesContractsWashoutRejectService(
            context, new SalesContractsWashedOutVolumeService(context), new SalesContractsChangeLogService(context),
            TestNotificationOutbox.For(context)))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.RejectAsync(new ODataActionParameters { ["Key"] = Guid.NewGuid(), ["Comments"] = null });

        Assert.IsType<BadRequestODataResult>(result);   // "Informe o motivo da rejeição."
    }
}
