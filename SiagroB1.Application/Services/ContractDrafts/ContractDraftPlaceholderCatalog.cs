using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>
/// Catálogo dos placeholders que um modelo pode usar. Os nomes são pt-BR de propósito: é texto
/// que o usuário digita no editor, e coincidem com os da Tagui para a migração dos modelos ser um
/// replace de <c>$x</c> por <c>{{x}}</c>. Quem preenche os valores é
/// <see cref="ContractDraftPlaceholderResolver"/>; os dois têm de andar juntos — o teste
/// <c>Resolver_covers_every_catalog_name</c> cobra isso.
/// </summary>
public static class ContractDraftPlaceholderCatalog
{
    private static readonly ContractDraftPlaceholderDto[] Common =
    [
        new("numero", "Número do contrato"),
        new("complemento", "Complemento do contrato"),
        new("emissao", "Data de emissão (dd/mm/aaaa)"),
        new("emissao_extenso", "Data de emissão por extenso"),
        new("data_inicio_entrega", "Início da entrega"),
        new("data_termino_entrega", "Término da entrega"),
        new("representante_nome", "Nome do representante"),
        new("local_entrega", "Local(is) de entrega"),
        new("produto_descricao", "Descrição do produto"),
        new("safra_descricao", "Safra"),
        new("quantidade", "Quantidade contratada"),
        new("unidade_medida", "Unidade de medida"),
        new("preco", "Preço unitário"),
        new("moeda", "Moeda (R$ / US$)"),
        new("valor_total", "Valor total"),
        new("valor_total_extenso", "Valor total por extenso"),
        new("tipo_frete", "Tipo de frete (CIF / FOB / Terceiro / Nenhum)"),
        new("condicao_pagamento", "Condição de pagamento"),
        new("data_pagamento", "Data de pagamento"),
        new("empresa_razao_social", "Razão social da empresa"),
        new("empresa_cnpj", "CNPJ da filial"),
        new("filial_nome", "Nome da filial"),
        new("assinaturas_empresa", "Bloco de assinaturas da empresa"),
        new("assinaturas_parceiro", "Bloco de assinaturas do parceiro"),
    ];

    private static readonly ContractDraftPlaceholderDto[] PurchaseOnly =
    [
        new("corretor_nome", "Nome do corretor"),
        new("corretor_comissao", "Comissão do corretor"),
        new("fornecedor_razao_social", "Razão social do fornecedor"),
        new("fornecedor_nome_fantasia", "Nome fantasia do fornecedor"),
        new("fornecedor_cnpj", "CNPJ/CPF do fornecedor"),
        new("fornecedor_endereco", "Endereço do fornecedor"),
        new("fornecedor_bairro", "Bairro do fornecedor"),
        new("fornecedor_cep", "CEP do fornecedor"),
        new("fornecedor_cidade", "Cidade do fornecedor"),
        new("fornecedor_uf", "UF do fornecedor"),
    ];

    private static readonly ContractDraftPlaceholderDto[] SalesOnly =
    [
        new("cliente_razao_social", "Razão social do cliente"),
        new("cliente_nome_fantasia", "Nome fantasia do cliente"),
        new("cliente_cnpj", "CNPJ/CPF do cliente"),
        new("cliente_endereco", "Endereço do cliente"),
        new("cliente_bairro", "Bairro do cliente"),
        new("cliente_cep", "CEP do cliente"),
        new("cliente_cidade", "Cidade do cliente"),
        new("cliente_uf", "UF do cliente"),
    ];

    public static IReadOnlyList<ContractDraftPlaceholderDto> For(ContractTemplateScope scope) => scope switch
    {
        ContractTemplateScope.Purchase => [.. Common, .. PurchaseOnly],
        ContractTemplateScope.Sales => [.. Common, .. SalesOnly],
        _ => Common,
    };

    public static IReadOnlySet<string> NamesFor(ContractTemplateScope scope) =>
        For(scope).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
}
