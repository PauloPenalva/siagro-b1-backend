using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Commons.Scales;
using SiagroB1.Web.Sockets.TruckScale;

namespace SiagroB1.Web.Controllers;

[ApiController]
[Authorize]
[Route("scales")]
public class ScalesController(
    TruckScaleHub hub,
    CaptureStore captures,
    ILogger<ScalesController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Peso ao vivo. Emite a cada 250 ms enquanto a tela estiver aberta; o navegador reconecta
    /// sozinho pelo EventSource quando a conexão cai.
    /// </summary>
    [HttpGet("{code}/live")]
    public async Task Live([FromRoute] string code, CancellationToken cancellationToken)
    {
        Response.Headers.ContentType = "text/event-stream";
        // "no-transform" não é decoração: sem ele os middlewares de compressão no caminho (o do
        // servidor de desenvolvimento do UI5, entre outros) seguram os chunks até encher o buffer,
        // e como cada leitura tem ~50 bytes o navegador ficava sem receber nada.
        Response.Headers.CacheControl = "no-cache, no-transform";
        // Proxies intermediários (YARP, nginx) precisam ser instruídos a não segurar o corpo,
        // senão o peso só chega ao navegador quando o buffer enche.
        Response.Headers["X-Accel-Buffering"] = "no";

        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        while (!cancellationToken.IsCancellationRequested)
        {
            var live = hub.GetLive(code);

            await Response.WriteAsync(
                $"data: {JsonSerializer.Serialize(live, JsonOptions)}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);

            await Task.Delay(250, cancellationToken);
        }
    }

    /// <summary>
    /// Aguarda o peso estabilizar e emite o comprovante. O peso nasce aqui, no servidor - é o que
    /// permite ao serviço de pesagem distinguir peso capturado de peso digitado.
    /// </summary>
    [HttpPost("{code}/capture")]
    public async Task<IActionResult> Capture([FromRoute] string code, CancellationToken cancellationToken)
    {
        var username = User.Identity?.Name;

        if (string.IsNullOrEmpty(username))
            return Unauthorized();

        if (!hub.IsConnected(code))
            return Conflict("Balança offline. Verifique o serviço de captura da balança.");

        var deadline = DateTime.Now.AddSeconds(30);

        while (DateTime.Now < deadline && !cancellationToken.IsCancellationRequested)
        {
            var live = hub.GetLive(code);

            if (!live.Online)
                return Conflict("Balança offline. Verifique o serviço de captura da balança.");

            if (live.Stable)
            {
                var capture = captures.Create(code, live.Weight, username, DateTime.Now);

                logger.LogInformation(
                    "Peso capturado na balança {ScaleCode} por {Username}: {Weight} kg.",
                    code, username, live.Weight);

                return Ok(new { captureId = capture.CaptureId, weight = capture.Weight });
            }

            await Task.Delay(250, cancellationToken);
        }

        return StatusCode(StatusCodes.Status408RequestTimeout,
            "O peso não estabilizou. Aguarde o veículo parar e tente novamente.");
    }
}
