using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Consulta à SEFAZ da NF-e do fornecedor no confirmar do documento eletrônico de terceiro (spec terceiro-chave §8).
/// Só com o ambiente NF-e da filial em Produção (D4); a chave já foi conferida localmente pelo
/// <c>SupplierNfeKeyGuard</c>. Consulta Situação (consSitNFe) na SEFAZ autorizadora da UF da CHAVE, com o certificado
/// da filial. Sem resposta, recusa e pede para tentar de novo (D5).
/// </summary>
public class SupplierNfeAuthorizationService(IUnitOfWork db, BranchNfeSettingsService settingsService, INfeSefazClient sefaz)
{
    /// <summary>Código IBGE da UF (2 primeiros dígitos da chave) → sigla usada pela configuração da SEFAZ.</summary>
    private static readonly Dictionary<string, string> States = new()
    {
        ["11"] = "RO", ["12"] = "AC", ["13"] = "AM", ["14"] = "RR", ["15"] = "PA", ["16"] = "AP", ["17"] = "TO",
        ["21"] = "MA", ["22"] = "PI", ["23"] = "CE", ["24"] = "RN", ["25"] = "PB", ["26"] = "PE", ["27"] = "AL",
        ["28"] = "SE", ["29"] = "BA", ["31"] = "MG", ["32"] = "ES", ["33"] = "RJ", ["35"] = "SP", ["41"] = "PR",
        ["42"] = "SC", ["43"] = "RS", ["50"] = "MS", ["51"] = "MT", ["52"] = "GO", ["53"] = "DF",
    };

    /// <summary>
    /// Fora de Produção não faz nada. Autorizada: grava protocolo e data no documento (rastreado; quem salva é o
    /// confirmar). Qualquer outra situação: <see cref="DefaultException"/> com a mensagem da spec.
    /// </summary>
    public async Task EnsureAuthorizedAsync(PurchaseInvoice invoice)
    {
        var environment = await db.Context.BranchNfeSettings.AsNoTracking()
            .Where(s => s.BranchCode == invoice.BranchCode)
            .Select(s => (NfeEnvironment?)s.Environment)
            .FirstOrDefaultAsync();

        if (environment != NfeEnvironment.Production)
            return;

        var key = invoice.ChaveNFe!;

        if (!States.TryGetValue(key[..2], out var state))
            throw new DefaultException($"Chave de acesso inválida: UF {key[..2]} desconhecida.");

        // Certificado ausente/inválido e qualquer falha de rede ou de leitura da resposta são, para quem confirma, a
        // mesma coisa: a SEFAZ não respondeu. Só a recusa de negócio (DefaultException) passa como veio.
        NfeSefazResult result;
        try
        {
            using var context = await settingsService.OpenAsync(invoice.BranchCode!);
            result = await sefaz.ConsultProtocolAsync(key, context.Settings with { IssuerState = state });
        }
        catch (DefaultException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new DefaultException("A SEFAZ não respondeu. Tente novamente.");
        }

        if (NfeStatusCodes.IsAuthorized(result.StatusCode))
        {
            invoice.SupplierNfeProtocol = result.Protocol;
            invoice.SupplierNfeCheckedAt = DateTime.Now;
            return;
        }

        throw new DefaultException(result.StatusCode switch
        {
            101 or 151 or 155 => "A NF-e do fornecedor está cancelada na SEFAZ.",
            _ when NfeStatusCodes.IsDenied(result.StatusCode) => "A NF-e do fornecedor teve o uso denegado na SEFAZ.",
            NfeStatusCodes.NotFound => "A chave de acesso não consta na SEFAZ.",
            _ => $"A SEFAZ não confirmou a NF-e do fornecedor ({result.StatusCode} – {result.Reason}). Tente novamente.",
        });
    }
}
