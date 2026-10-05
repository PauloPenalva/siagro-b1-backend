using System.Runtime.CompilerServices;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Todo teste de modelo FastReport roda com a mesma configuração de script da produção (o Program.cs do
/// Reports não executa aqui), independente da ordem dos testes.
/// </summary>
internal static class FastReportScriptConfig
{
    [ModuleInitializer]
    internal static void Init() => DanfeReportService.DisableScriptStubs();
}
