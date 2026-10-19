using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.NfeEmail;

/// <summary>Histórico de e-mails da NF-e (somente leitura).</summary>
public class NfeEmailMessagesGetService(IUnitOfWork db)
{
    public IQueryable<NfeEmailMessage> QueryAll() => db.Context.NfeEmailMessages.AsNoTracking();
}
