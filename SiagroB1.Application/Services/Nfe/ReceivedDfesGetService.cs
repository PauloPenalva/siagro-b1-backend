using SiagroB1.Domain.Entities;
using SiagroB1.Infra;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Consultas somente leitura da manifestação do destinatário (DF-e recebidos e estado da consulta por filial).</summary>
public class ReceivedDfesGetService(IUnitOfWork db)
{
    public IQueryable<ReceivedDfe> QueryAll() => db.Context.ReceivedDfes.AsNoTracking();

    public IQueryable<BranchDfeSyncState> QuerySyncStates() => db.Context.BranchDfeSyncStates.AsNoTracking();
}

/// <summary>Conteúdo do XML do DF-e para a tela (download e importação no Documento de Entrada).</summary>
public sealed record ReceivedDfeXmlDto(string XmlContent, string FileName);
