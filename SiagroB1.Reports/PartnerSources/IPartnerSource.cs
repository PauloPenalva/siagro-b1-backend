using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.PartnerSources;

/// <summary>
/// Origem dos dados de parceiro de negócios para os relatórios.
/// A implementação concreta é escolhida em <c>Program.cs</c> pela chave de
/// configuração <c>Erp</c>, espelhando o que o SiagroB1.Web faz com
/// <c>AddSapServices()</c> / <c>AddStandAloneServices()</c>.
/// </summary>
/// <remarks>
/// As implementações NÃO terminam em "Service" de propósito: o Scrutor em
/// <see cref="DI.ServiceCollectionExtensions"/> registra automaticamente toda classe
/// com esse sufixo <c>AsImplementedInterfaces</c>, o que faria as duas disputarem
/// o mesmo contrato. Aqui o registro é explícito e condicional.
/// </remarks>
public interface IPartnerSource
{
    Task<ReportPartnerDto?> GetByCardCodeAsync(string cardCode, CancellationToken ct = default);
}
