using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Web.Hooks;

/// <summary>
/// Recebe os eventos do D4Sign. O payload NÃO é fonte de verdade: o D4Sign não assina o webhook,
/// então qualquer um que descubra a URL poderia postar "assinado". Do form só se usa o uuid, para
/// achar a minuta; a situação vem sempre de <see cref="IESignatureProvider.GetStateAsync"/>.
///
/// Fail-closed: segredo não configurado ⇒ 401 sempre.
/// </summary>
public static class D4SignWebhookEndpoint
{
    public const string SecretKey = "Signature:D4Sign:WebhookSecret";

    /// <summary>Comparação em tempo fixo. Segredo vazio dos dois lados NÃO é igualdade.</summary>
    public static bool SecretMatches(string? configured, string? received)
    {
        if (string.IsNullOrWhiteSpace(configured) || string.IsNullOrWhiteSpace(received))
            return false;

        var a = Encoding.UTF8.GetBytes(configured);
        var b = Encoding.UTF8.GetBytes(received);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static void MapD4SignWebhook(this WebApplication app)
    {
        app.MapPost("/hooks/d4sign/{secret}", async (
            string secret,
            HttpRequest request,
            IConfiguration configuration,
            AppDbContext context,
            IESignatureProvider provider,
            ContractDraftsApplyProviderStateService apply,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("D4SignWebhook");

            if (!SecretMatches(configuration[SecretKey], secret))
            {
                logger.LogWarning("Webhook do D4Sign recusado: segredo inválido ou não configurado");
                return Results.Unauthorized();
            }

            string? uuid = null;
            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync(ct);
                uuid = form["uuid"].ToString();
            }

            if (string.IsNullOrWhiteSpace(uuid))
            {
                // 200 pelo mesmo motivo do ramo de uuid desconhecido: não gastar as 7 tentativas
                // do D4Sign com algo que não vai melhorar sozinho. Mas tem que aparecer no log —
                // sem isto, um evento que chega sem form (ou com o campo renomeado numa versão
                // nova da API) some sem rastro e a minuta só "nunca atualiza".
                logger.LogWarning("Webhook do D4Sign sem uuid no corpo da requisição; evento ignorado");
                return Results.Ok();
            }

            var draftKey = await context.ContractDrafts
                .Where(d => d.ExternalDocumentId == uuid)
                .Select(d => (Guid?)d.Key)
                .FirstOrDefaultAsync(ct);

            if (draftKey is null)
            {
                // Pode ser documento criado à mão no cofre. 200 para não gastar as 7 tentativas do D4Sign.
                logger.LogWarning("Webhook do D4Sign para documento desconhecido {Uuid}", uuid);
                return Results.Ok();
            }

            var state = await provider.GetStateAsync(uuid, ct);
            await apply.ExecuteAsync(draftKey.Value, state, "d4sign-webhook", ct);

            return Results.Ok();
        }).AllowAnonymous();
    }
}
