using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Monta a <see cref="ContractNotificationOutboxService"/> para os testes dos serviços de
/// mutação, que passaram a recebê-la.
///
/// Existe para que os testes que NÃO tratam de notificação não precisem saber montar o
/// construtor inteiro — eles só precisam de uma instância funcional que não atrapalhe.
/// </summary>
public static class TestNotificationOutbox
{
    public static ContractNotificationOutboxService For(
        AppDbContext context, string? appBaseUrl = "https://siagro.teste")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Notifications:AppBaseUrl"] = appBaseUrl })
            .Build();

        return new ContractNotificationOutboxService(
            context, new ContractNotificationPayloadBuilder(configuration));
    }
}
