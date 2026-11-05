using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Market;

public class MarketInstrumentServiceTests
{
    [Fact]
    public async Task CreateSamples_IsIdempotent()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new MarketInstrumentService(db.Context, new TestLogger<MarketInstrumentService>());

        var first = await service.CreateSamplesAsync(CancellationToken.None);
        var second = await service.CreateSamplesAsync(CancellationToken.None);

        Assert.Equal(16, first);
        Assert.Equal(0, second);
        Assert.Equal(16, await db.Context.MarketInstruments.CountAsync());
        var soy = await db.Context.MarketInstruments.SingleAsync(i => i.Code == "ZSF7");
        Assert.Equal(0.36745m, soy.TonFactor);
        Assert.Equal(MarketExchange.Cbot, soy.Exchange);
        var curve = await db.Context.MarketInstruments.SingleAsync(i => i.Code == "CURVA DE DOLAR 90D");
        Assert.Equal(90, curve.CurveDays);
        Assert.Equal(MarketInstrumentType.CurvePoint, curve.Type);
    }
}
