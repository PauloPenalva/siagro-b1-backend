using Microsoft.EntityFrameworkCore;

using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Warehouses;

public class WarehouseComplementServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private WarehouseComplementService Service() => new(_db);

    [Fact]
    public async Task GetAsync_ReturnsNullWhenWarehouseHasNoComplement()
    {
        Assert.Null(await Service().GetAsync("ARM01"));
    }

    [Fact]
    public async Task SetAsync_CreatesTheComplementOnFirstCall()
    {
        var result = await Service().SetAsync("ARM01", true, true, null);

        Assert.Equal("ARM01", result.WarehouseCode);
        Assert.True(result.IsParticipant);
        Assert.True(result.IsOwn);

        var stored = await Service().GetAsync("ARM01");
        Assert.True(stored!.IsParticipant);
        Assert.True(stored.IsOwn);
    }

    [Fact]
    public async Task SetAsync_UpdatesInPlaceInsteadOfCreatingASecondRow()
    {
        await Service().SetAsync("ARM01", true, true, null);
        await Service().SetAsync("ARM01", false, true, null);

        Assert.Equal(1, await _db.Context.WarehouseComplements.CountAsync(x => x.WarehouseCode == "ARM01"));

        var stored = await Service().GetAsync("ARM01");
        Assert.False(stored!.IsParticipant);
        Assert.True(stored.IsOwn);
    }

    /// <summary>
    /// Desligar os dois flags tem de gravar false, não simplesmente "não gravar": o caminho de
    /// volta para NÃO é o que mais falha em campo booleano.
    /// </summary>
    [Fact]
    public async Task SetAsync_TurnsBothFlagsOff()
    {
        await Service().SetAsync("ARM01", true, true, null);

        var result = await Service().SetAsync("ARM01", false, false, null);

        Assert.False(result.IsParticipant);
        Assert.False(result.IsOwn);
        Assert.False((await Service().GetAsync("ARM01"))!.IsParticipant);
    }

    [Fact]
    public async Task SetAsync_KeepsTheTwoFlagsIndependent()
    {
        var result = await Service().SetAsync("ARM01", true, false, null);

        Assert.True(result.IsParticipant);
        Assert.False(result.IsOwn);
    }

    [Fact]
    public async Task SetAsync_StoresAndUpdatesTheNotes()
    {
        await Service().SetAsync("ARM01", true, false, "Armazém alugado até 2027");

        Assert.Equal("Armazém alugado até 2027", (await Service().GetAsync("ARM01"))!.Notes);

        await Service().SetAsync("ARM01", true, false, "Contrato renovado");

        Assert.Equal("Contrato renovado", (await Service().GetAsync("ARM01"))!.Notes);
    }

    /// <summary>
    /// Campo esvaziado na tela chega como "" (ou só espaços) e não como null: guardar isso deixaria
    /// dois valores diferentes significando "sem observação".
    /// </summary>
    [Fact]
    public async Task SetAsync_TreatsBlankNotesAsNotInformed()
    {
        await Service().SetAsync("ARM01", true, false, "algo");

        var result = await Service().SetAsync("ARM01", true, false, "   ");

        Assert.Null(result.Notes);
        Assert.Null((await Service().GetAsync("ARM01"))!.Notes);
    }

    [Fact]
    public async Task SetAsync_KeepsTheNotesIndependentFromTheFlags()
    {
        await Service().SetAsync("ARM01", true, true, "observação");

        var result = await Service().SetAsync("ARM01", false, false, "observação");

        Assert.Equal("observação", result.Notes);
        Assert.False(result.IsParticipant);
    }

    [Fact]
    public async Task GetAsync_IsScopedToTheWarehouse()
    {
        await Service().SetAsync("ARM01", true, true, null);
        await Service().SetAsync("ARM02", false, true, null);

        Assert.False((await Service().GetAsync("ARM02"))!.IsParticipant);
    }

    [Fact]
    public async Task GetOwnAsync_ReturnsEmptyWhenNoWarehouseIsMarked()
    {
        await Service().SetAsync("ARM01", true, false, null);

        Assert.Empty(await Service().GetOwnAsync());
    }

    /// <summary>
    /// Os dois flags são independentes: participante não implica próprio, e é o próprio que
    /// qualifica o contrato na transferência de titularidade.
    /// </summary>
    [Fact]
    public async Task GetOwnAsync_ReturnsOnlyTheOwnWarehouses()
    {
        await Service().SetAsync("ARM01", false, true, null);
        await Service().SetAsync("ARM02", true, false, null);
        await Service().SetAsync("ARM03", true, true, null);

        var result = (await Service().GetOwnAsync()).ToList();

        Assert.Equal(["ARM01", "ARM03"], result.Select(x => x.WarehouseCode));
    }

    [Fact]
    public async Task GetOwnAsync_StopsReturningAWarehouseThatIsTurnedOff()
    {
        await Service().SetAsync("ARM01", true, true, null);
        await Service().SetAsync("ARM01", true, false, null);

        Assert.Empty(await Service().GetOwnAsync());
    }

    [Fact]
    public async Task SetAsync_AcceptsAWarehouseThatDoesNotExistInTheLocalMaster()
    {
        // Em modo SAPB1 o armazém é um parceiro do OCRD, então a tabela não tem FK para WAREHOUSES.
        Assert.Empty(await _db.Context.Warehouses.ToListAsync());

        var result = await Service().SetAsync("ARM-SO-DO-SAP", true, false, null);

        Assert.Equal("ARM-SO-DO-SAP", result.WarehouseCode);
    }
}
