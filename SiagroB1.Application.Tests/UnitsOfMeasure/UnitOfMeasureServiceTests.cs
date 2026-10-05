using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Tests.UnitsOfMeasure;

/// <summary>
/// Cadastro de unidade de medida (STANDALONE). Os value helps só listam unidade com <c>Locked = 'N'</c>:
/// a que nasce sem a flag (importação, POST avulso) some de todas as telas.
/// </summary>
public class UnitOfMeasureServiceTests
{
    [Fact]
    public async Task Create_without_locked_flag_saves_unlocked()
    {
        var db = TestDb.CreateUnitOfWork();

        await new UnitOfMeasureService(db.Context, new TestLogger<UnitOfMeasureService>())
            .CreateAsync(new UnitOfMeasureModel { Code = "KG", Description = "KILOGRAMA", Locked = null });

        Assert.Equal("N", (await db.Context.UnitsOfMeasure.SingleAsync()).Locked);
    }

    [Fact]
    public async Task Create_keeps_an_informed_locked_flag()
    {
        var db = TestDb.CreateUnitOfWork();

        await new UnitOfMeasureService(db.Context, new TestLogger<UnitOfMeasureService>())
            .CreateAsync(new UnitOfMeasureModel { Code = "SC", Description = "SACO", Locked = "Y" });

        Assert.Equal("Y", (await db.Context.UnitsOfMeasure.SingleAsync()).Locked);
    }
}
