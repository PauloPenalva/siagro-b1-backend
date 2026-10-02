using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services;

/// <summary>
/// Com o município escolhido, cidade e UF do endereço vêm dele — no servidor, porque a tela grava
/// a descrição fora do PATCH e a UF do endereço de faturamento decide CFOP e tributos. Sem
/// município, o endereço fica como foi digitado (é o caminho de todo cadastro anterior).
/// </summary>
public static class AddressMunicipalityResolver
{
    public static async Task ApplyAsync(AppDbContext context, Address address)
    {
        if (string.IsNullOrWhiteSpace(address.MunicipalityCode))
        {
            address.MunicipalityCode = null;
            return;
        }

        var municipality = await context.Municipalities.AsNoTracking()
                               .FirstOrDefaultAsync(m => m.Code == address.MunicipalityCode)
                           ?? throw new DefaultException($"Município {address.MunicipalityCode} não encontrado.");

        address.City = municipality.Name;
        address.State = municipality.StateAbbreviation;
    }
}
