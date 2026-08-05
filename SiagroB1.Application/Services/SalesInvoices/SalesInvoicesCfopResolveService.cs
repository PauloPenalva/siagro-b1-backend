using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Resolve o CFOP da linha do documento comparando a UF da FILIAL do documento com a UF do
/// DESTINATÁRIO (endereço de faturamento do parceiro). Iguais, usa o CFOP de saída dentro do
/// estado; diferentes, o interestadual.
///
/// Vive num serviço próprio para que o caminho avulso e o faturamento de romaneio resolvam o
/// CFOP no mesmo lugar. Nenhum ramo devolve vazio: UF ausente (filial ou parceiro) e CFOP não
/// cadastrado na natureza são erros de negócio em pt-BR — gravar CFOP vazio em silêncio só
/// apareceria na hora de emitir a NF-e.
/// </summary>
public class SalesInvoicesCfopResolveService(
    IUnitOfWork db,
    IUsage usageService,
    IBusinessPartnerService businessPartnerService)
{
    public async Task<string> ResolveAsync(int usageCode, string? branchCode, string cardCode)
    {
        var usage = await usageService.GetByIdAsync(usageCode)
                    ?? throw new DefaultException("Natureza de operação não encontrada.");

        var branch = await db.Context.Branchs
                         .AsNoTracking()
                         .FirstOrDefaultAsync(b => b.Code == branchCode)
                     ?? throw new DefaultException(
                         $"Filial {branchCode} do documento não encontrada.");

        if (string.IsNullOrWhiteSpace(branch.StateCode))
        {
            throw new DefaultException(
                $"Filial {branch.Code} está sem UF cadastrada. " +
                "Informe a UF da filial antes de emitir o documento.");
        }

        var partner = await businessPartnerService.GetByIdAsync(cardCode)
                      ?? throw new DefaultException($"Parceiro {cardCode} não encontrado.");

        var partnerState = ResolvePartnerState(partner);

        if (string.IsNullOrWhiteSpace(partnerState))
        {
            throw new DefaultException(
                $"Parceiro {cardCode} está sem UF no endereço de faturamento.");
        }

        var sameState = string.Equals(
            branch.StateCode, partnerState, StringComparison.OrdinalIgnoreCase);

        var cfop = sameState ? usage.CfopOutgoingInState : usage.CfopOutgoingOutState;

        if (string.IsNullOrWhiteSpace(cfop))
        {
            throw new DefaultException(sameState
                ? $"Natureza de operação {usage.Name} está sem CFOP de saída dentro do estado."
                : $"Natureza de operação {usage.Name} está sem CFOP de saída fora do estado.");
        }

        return cfop;
    }

    /// <summary>
    /// UF do destinatário: endereço de FATURAMENTO primeiro, qualquer outro com UF depois.
    ///
    /// Os dois modos entregam coleções diferentes — em SAPB1 o serviço de parceiro já filtra
    /// o endereço de faturamento e devolve um só; em STANDALONE devolve todos. Escolher aqui,
    /// e não confiar na ordem da coleção, é o que faz o CFOP sair igual nos dois.
    /// </summary>
    private static string? ResolvePartnerState(BusinessPartnerModel partner)
    {
        var withState = partner.Addresses
            .Where(a => !string.IsNullOrWhiteSpace(a.State))
            .ToList();

        var billing = withState.FirstOrDefault(a =>
            string.Equals(a.AdresType, "B", StringComparison.OrdinalIgnoreCase)
            || string.Equals(a.AddressName, "FATURAMENTO", StringComparison.OrdinalIgnoreCase));

        return (billing ?? withState.FirstOrDefault())?.State;
    }
}
