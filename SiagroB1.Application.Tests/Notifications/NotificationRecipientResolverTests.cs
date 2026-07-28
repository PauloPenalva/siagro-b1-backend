using SiagroB1.Application.Services.Notifications;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Notifications;

/// <summary>
/// Quem recebe cada evento. Errar para menos silencia a feature; errar para mais manda
/// mensagem de contrato para quem não deveria vê-la.
/// </summary>
public class NotificationRecipientResolverTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private NotificationRecipientResolver CreateResolver() => new(_db.Context);

    /// <summary>
    /// Cria um grupo com um membro e uma assinatura. Os parâmetros cobrem exatamente os
    /// interruptores que decidem se a mensagem sai.
    /// </summary>
    private NotificationGroup SeedGroup(
        string code,
        string phoneE164,
        NotificationDocumentType documentType,
        NotificationEventType eventType,
        bool groupActive = true,
        bool memberActive = true)
    {
        var group = new NotificationGroup
        {
            Key = Guid.NewGuid(),
            Code = code,
            Name = $"Grupo {code}",
            Active = groupActive,
        };

        _db.Context.NotificationGroups.Add(group);
        _db.Context.NotificationGroupMembers.Add(new NotificationGroupMember
        {
            Key = Guid.NewGuid(),
            NotificationGroupKey = group.Key,
            Name = $"Membro {code}",
            Phone = phoneE164,
            PhoneE164 = phoneE164,
            Active = memberActive,
        });
        _db.Context.NotificationGroupSubscriptions.Add(new NotificationGroupSubscription
        {
            Key = Guid.NewGuid(),
            NotificationGroupKey = group.Key,
            DocumentType = documentType,
            EventType = eventType,
        });

        _db.Context.SaveChanges();

        return group;
    }

    [Fact]
    public async Task ResolveAsync_SubscribedGroup_ReturnsItsMembers()
    {
        SeedGroup("COM", "5566999998888",
            NotificationDocumentType.PurchaseContract, NotificationEventType.Approved);

        var recipients = await CreateResolver().ResolveAsync(
            NotificationDocumentType.PurchaseContract, NotificationEventType.Approved);

        var recipient = Assert.Single(recipients);
        Assert.Equal("5566999998888", recipient.PhoneE164);
        Assert.Equal("Membro COM", recipient.Name);
        Assert.Equal("Grupo COM", recipient.GroupName);
    }

    [Fact]
    public async Task ResolveAsync_SubscriptionIsPerDocumentType()
    {
        SeedGroup("COM", "5566999998888",
            NotificationDocumentType.PurchaseContract, NotificationEventType.Approved);

        var recipients = await CreateResolver().ResolveAsync(
            NotificationDocumentType.SalesContract, NotificationEventType.Approved);

        Assert.Empty(recipients);
    }

    [Fact]
    public async Task ResolveAsync_SubscriptionIsPerEventType()
    {
        SeedGroup("COM", "5566999998888",
            NotificationDocumentType.PurchaseContract, NotificationEventType.Approved);

        var recipients = await CreateResolver().ResolveAsync(
            NotificationDocumentType.PurchaseContract, NotificationEventType.Canceled);

        Assert.Empty(recipients);
    }

    [Fact]
    public async Task ResolveAsync_InactiveGroup_IsExcluded()
    {
        SeedGroup("COM", "5566999998888",
            NotificationDocumentType.PurchaseContract, NotificationEventType.Approved,
            groupActive: false);

        Assert.Empty(await CreateResolver().ResolveAsync(
            NotificationDocumentType.PurchaseContract, NotificationEventType.Approved));
    }

    [Fact]
    public async Task ResolveAsync_InactiveMember_IsExcluded()
    {
        SeedGroup("COM", "5566999998888",
            NotificationDocumentType.PurchaseContract, NotificationEventType.Approved,
            memberActive: false);

        Assert.Empty(await CreateResolver().ResolveAsync(
            NotificationDocumentType.PurchaseContract, NotificationEventType.Approved));
    }

    /// <summary>
    /// Quem está em dois grupos que assinam o mesmo evento recebe UMA mensagem. Duas seriam
    /// ruído — e, num provedor não-oficial, ruído multiplicado é risco de banimento do número.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_SamePhoneInTwoGroups_IsReturnedOnce()
    {
        SeedGroup("COM", "5566999998888",
            NotificationDocumentType.PurchaseContract, NotificationEventType.Approved);
        SeedGroup("DIR", "5566999998888",
            NotificationDocumentType.PurchaseContract, NotificationEventType.Approved);

        var recipients = await CreateResolver().ResolveAsync(
            NotificationDocumentType.PurchaseContract, NotificationEventType.Approved);

        Assert.Single(recipients);
    }

    [Fact]
    public async Task ResolveAsync_NoSubscription_ReturnsEmpty()
    {
        Assert.Empty(await CreateResolver().ResolveAsync(
            NotificationDocumentType.PurchaseContract, NotificationEventType.Created));
    }

    /// <summary>
    /// Membro sem telefone normalizado não pode virar destinatário — o provedor recusaria e a
    /// linha de log diria apenas "erro 4xx", sem explicar que o cadastro é que está torto.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_MemberWithoutNormalizedPhone_IsExcluded()
    {
        SeedGroup("COM", "", NotificationDocumentType.PurchaseContract, NotificationEventType.Approved);

        Assert.Empty(await CreateResolver().ResolveAsync(
            NotificationDocumentType.PurchaseContract, NotificationEventType.Approved));
    }
}
