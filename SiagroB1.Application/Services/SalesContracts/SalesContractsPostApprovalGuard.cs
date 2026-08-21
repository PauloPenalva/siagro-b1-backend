using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Regra única de quem pode sofrer edição pontual (locais de entrega) depois de aprovado.
///
/// O contrato aprovado é imutável em tudo o mais — <c>SalesContractsUpdateService</c> segue
/// recusando qualquer edição fora de Draft. O local de entrega é a exceção deliberada: sem
/// ele um contrato aprovado e já faturado nunca conseguiria cadastrar o local de entrega
/// exigido pela liberação de entrega, porque também não pode voltar para Draft.
///
/// Anexo NÃO passa mais por aqui: é documentação, não movimento, e vale em qualquer status
/// (ver <c>SalesContractsAttachmentsCreateService</c>).
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
            "locais de entrega em contrato em rascunho ou aprovado.");
    }
}
