using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1171 (melhorias): Descarregada se comporta como Faturada em toda trava, e a Concluída da
/// carga Normal como o Completed que já existia para a Remoção, com a mensagem certa para cada tipo.
/// </summary>
public class ShipmentLoadClosureGuardsTests
{
    private static ShipmentLoad Load(ShipmentLoadStatus status, ShipmentLoadType type = ShipmentLoadType.Normal) => new()
    {
        Key = Guid.NewGuid(),
        Code = "CG000071",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        LoadType = type,
        Status = status,
        TotalQuantity = 90_000,
        InvoicedQuantity = 90_000,
    };

    [Fact]
    public void The_undo_hint_depends_on_the_load_type()
    {
        Assert.Equal("Reabra-a", ShipmentLoadCompletionRules.UndoHint(Load(ShipmentLoadStatus.Completed, ShipmentLoadType.Removal)));
        Assert.Equal("Estorne a conferência de entrega", ShipmentLoadCompletionRules.UndoHint(Load(ShipmentLoadStatus.Completed)));
    }

    /// <summary>
    /// Na COMPOSIÇÃO (vincular, desvincular, cancelar) a carga Normal Concluída se destrava
    /// cancelando os documentos de saída, e não estornando a conferência: depois do estorno a
    /// carga volta a Faturada e as notas continuam travando a composição.
    /// </summary>
    [Fact]
    public void The_composition_hint_depends_on_the_load_type()
    {
        Assert.Equal("Reabra-a", ShipmentLoadCompletionRules.CompositionHint(Load(ShipmentLoadStatus.Completed, ShipmentLoadType.Removal)));
        Assert.Equal("Cancele os documentos de saída", ShipmentLoadCompletionRules.CompositionHint(Load(ShipmentLoadStatus.Completed)));
    }

    [Fact]
    public async Task Reopening_a_completed_normal_load_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = Load(ShipmentLoadStatus.Completed);
        db.Context.ShipmentLoads.Add(load);
        await db.Context.SaveChangesAsync();

        var service = new ShipmentLoadsReopenService(
            db, new ShipmentLoadsMovementLogService(db.Context), new ShipmentLoadsChangeLogService(db.Context));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(load.Key, "tester"));

        Assert.Equal(
            "A carga CG000071 é concluída pela conferência de entrega: estorne a conferência para reabri-la.",
            ex.Message);
    }

    /// <summary>
    /// A carga que nunca foi concluída ouve que não está concluída, e não que "é concluída pela
    /// conferência": a trava do tipo só vale para quem está de fato em Completed.
    /// </summary>
    [Fact]
    public async Task Reopening_a_normal_load_that_is_not_completed_says_it_is_not_completed()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = Load(ShipmentLoadStatus.Invoiced);
        db.Context.ShipmentLoads.Add(load);
        await db.Context.SaveChangesAsync();

        var service = new ShipmentLoadsReopenService(
            db, new ShipmentLoadsMovementLogService(db.Context), new ShipmentLoadsChangeLogService(db.Context));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(load.Key, "tester"));

        Assert.Equal("A carga CG000071 não está concluída.", ex.Message);
    }

    [Fact]
    public void Transshipment_is_refused_on_a_completed_load()
    {
        var ex = Assert.Throws<ApplicationException>(
            () => ShipmentLoadTransshipmentRules.EnsureLoadAcceptsTransshipment(Load(ShipmentLoadStatus.Completed)));

        Assert.Equal("A carga CG000071 está encerrada e não aceita transbordo.", ex.Message);
    }

    /// <summary>
    /// A Descarregada tem caminho de volta próprio (o "Desfazer Descarga"), e a mensagem aponta
    /// para ele em vez de dizer só "está encerrada".
    /// </summary>
    [Fact]
    public void Transshipment_on_a_discharged_load_asks_to_undo_the_discharge()
    {
        var load = Load(ShipmentLoadStatus.Discharged);
        load.IsDischarged = true;

        var ex = Assert.Throws<ApplicationException>(
            () => ShipmentLoadTransshipmentRules.EnsureLoadAcceptsTransshipment(load));

        Assert.Equal(
            "A carga CG000071 já foi descarregada no destino. Desfaça a descarga antes de iniciar o transbordo.",
            ex.Message);
    }
}
