using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra.Context;

namespace SiagroB1.Web.Sockets.TruckScale;

/// <summary>
/// Monta a configuração que o Client recebe ao conectar. O cadastro é a fonte única: trocar o IP
/// de uma balança não exige acesso à máquina onde o Client roda.
/// </summary>
public class ScaleConfigProvider(AppDbContext db)
{
    public async Task<ScaleConfigPayload?> GetAsync(string scaleCode)
    {
        var scale = await db.TruckScales
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code == scaleCode);

        if (scale == null)
            return null;

        return new ScaleConfigPayload
        {
            Ip = scale.IpAddress,
            Port = scale.Port,
            Protocol = scale.Protocol.ToString(),
            FramePrefixLength = scale.FramePrefixLength ?? 1,
            WeightLength = scale.WeightLength ?? 6,
            DecimalPlaces = scale.DecimalPlaces ?? 0,
            FrameTerminator = scale.FrameTerminator ?? "\n",
            FramePattern = scale.FramePattern,
            LogRawFrames = scale.LogRawFrames
        };
    }
}
