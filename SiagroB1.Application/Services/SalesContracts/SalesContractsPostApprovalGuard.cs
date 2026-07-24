using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Regra única de quem pode sofrer edição pontual (observação, locais de entrega, anexos)
/// depois de aprovado.
///
/// O contrato aprovado é imutável em tudo o mais — <c>SalesContractsUpdateService</c> segue
/// recusando qualquer edição fora de Draft. Estes três pontos são a exceção deliberada: sem
/// eles um contrato aprovado e já faturado nunca conseguiria cadastrar o local de entrega
/// exigido pela liberação de entrega, porque também não pode voltar para Draft.
/// </summary>
public static class SalesContractsPostApprovalGuard
{
    public static void EnsureEditable(SalesContract contract)
    {
        if (contract.Status is ContractStatus.Draft or ContractStatus.Approved)
        {
            return;
        }

        throw new DefaultException(
            "Contrato de venda não permite alteração neste status: só é possível alterar " +
            "observação, locais de entrega e anexos em contrato em rascunho ou aprovado.");
    }
}
