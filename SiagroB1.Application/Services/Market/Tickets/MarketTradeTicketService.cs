using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Market.Pricing;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Tickets;

/// <summary>Corpo da criação da boleta (os números do preço vêm sempre do servidor, nunca do cliente).</summary>
public sealed record MarketTradeTicketInput(
    int PriceTableLineId, string MunicipalityCode, string CardCode, decimal VolumeKg, decimal NegotiatedPrice,
    string WarehouseCode, int AgentCode, string BranchCode, string LogisticRegionCode, string? Notes);

/// <summary>
/// Boleta de compra: criação (congela a linha da grade), alçada (comprador → aprovador → diretoria) e geração do
/// contrato já aprovado. A aprovação grava os carimbos ANTES de gerar o contrato: esse UPDATE é o "pedido de vez"
/// protegido pelo RowVersion, então dois aprovadores simultâneos não geram dois contratos.
/// </summary>
public class MarketTradeTicketService(
    AppDbContext context,
    MarketPriceGridService grid,
    MarketTradeTicketContractService contracts,
    IBusinessPartnerService businessPartners,
    IWarehouseService warehouses,
    IAgentService agents,
    IUserPermissions permissions,
    MarketPermissionGuard guard,
    TimeProvider time)
{
    public const string ConcurrencyMessage = "A boleta foi alterada por outro usuário. Atualize e tente de novo.";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly JsonSerializerOptions StepsJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    public async Task<MarketTradeTicket> CreateAsync(
        MarketTradeTicketInput input, string userName, DateOnly today, CancellationToken ct)
    {
        if (input.VolumeKg <= 0)
            throw new BusinessException("O volume deve ser maior que zero.");
        if (input.NegotiatedPrice <= 0)
            throw new BusinessException("O preço negociado deve ser maior que zero.");
        if (string.IsNullOrWhiteSpace(input.MunicipalityCode))
            throw new BusinessException("Informe a cidade.");

        var settings = await context.MarketTradingSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(settings?.ContractUnitOfMeasureCode))
            throw new BusinessException("Informe a unidade de medida do contrato nos parâmetros do Mercado.");
        if (string.IsNullOrWhiteSpace(settings.ContractFreightUmCode))
            throw new BusinessException("Informe a unidade de medida do frete nos parâmetros do Mercado.");

        var cardCode = Required(input.CardCode, "o parceiro");
        var partner = await businessPartners.GetByIdAsync(cardCode)
                      ?? throw new BusinessException("Parceiro não encontrado.");
        var warehouseCode = Required(input.WarehouseCode, "o armazém");
        var warehouse = await warehouses.GetByIdAsync(warehouseCode)
                        ?? throw new BusinessException("Armazém não encontrado.");
        var agent = await agents.GetByIdAsync(input.AgentCode)
                    ?? throw new BusinessException("Agente não encontrado.");
        var branchCode = Required(input.BranchCode, "a filial");
        if (!await context.Branchs.AnyAsync(b => b.Code == branchCode, ct))
            throw new BusinessException("Filial não encontrada.");
        var regionCode = Required(input.LogisticRegionCode, "a região logística");
        if (!await context.LogisticRegions.AnyAsync(r => r.Code == regionCode, ct))
            throw new BusinessException("Região logística não encontrada.");

        var row = await grid.GetRowAsync(input.PriceTableLineId, input.MunicipalityCode.Trim(), today, ct)
                  ?? throw new BusinessException("Linha sem preço: linha da tabela de preços inexistente ou inativa.");
        if (row.Status != MarketPriceStatus.Ok || row.Price is not > 0)
            throw new BusinessException($"Linha sem preço: {StatusText(row.Status)}.");
        if (row.DeliveryStartDate == default || row.DeliveryEndDate == default)
            throw new BusinessException("A linha da tabela de preços não tem período de entrega.");

        var calculated = row.Price.Value;
        var deviation = Math.Round((input.NegotiatedPrice - calculated) / calculated * 100m, 4, MidpointRounding.AwayFromZero);
        var reasons = new List<string>();
        var tons = input.VolumeKg / 1000m;
        if (settings.BoardApprovalVolumeTons is { } volumeLimit && tons >= volumeLimit)
            reasons.Add($"Volume de {tons.ToString("N3", PtBr)} t acima do limite de {volumeLimit.ToString("N3", PtBr)} t");
        if (settings.BoardApprovalDeviationPercent is { } deviationLimit && Math.Abs(deviation) >= deviationLimit)
            reasons.Add($"desvio de preço de {deviation.ToString("N2", PtBr)} % acima do limite de {deviationLimit.ToString("N2", PtBr)} %");
        var reason = reasons.Count == 0 ? null : string.Join("; ", reasons);
        if (reason is not null)
            reason = char.ToUpper(reason[0], PtBr) + reason[1..];

        var ticket = new MarketTradeTicket
        {
            Status = MarketTradeTicketStatus.Pending,
            CreatedAt = UtcNow,
            CreatedBy = userName,
            PriceTableId = row.PriceTableId,
            PriceTableLineId = row.LineId,
            PriceTableDescription = row.PriceTableDescription,
            LineDescription = row.LineDescription,
            ItemCode = row.ItemCode,
            ItemName = row.ItemName,
            HarvestSeasonCode = row.HarvestSeasonCode,
            MunicipalityCode = input.MunicipalityCode.Trim(),
            DestinationId = row.DestinationId,
            DestinationName = row.DestinationName,
            InstrumentCode = row.InstrumentCode,
            ExchangeQuote = row.ExchangeQuote ?? 0,
            Premium = row.Premium,
            NetFx = row.NetFx,
            SpotFx = row.SpotFx,
            FreightPerTon = row.FreightPerTon ?? 0,
            PortPrice = Math.Round(row.PortPrice ?? 0, 2, MidpointRounding.AwayFromZero),
            CalculatedPrice = calculated,
            StepsJson = JsonSerializer.Serialize(row.Steps, StepsJsonOptions),
            QuotedAt = row.QuotedAt,
            QuoteSource = row.QuoteSource,
            DeliveryStartDate = ToDateTime(row.DeliveryStartDate),
            DeliveryEndDate = ToDateTime(row.DeliveryEndDate),
            ReceiptDate = ToDateTime(row.ReceiptDate),
            PaymentDate = ToDateTime(row.PaymentDate),
            CardCode = cardCode,
            CardName = partner.CardName,
            VolumeKg = input.VolumeKg,
            NegotiatedPrice = input.NegotiatedPrice,
            WarehouseCode = warehouseCode,
            WarehouseName = warehouse.Name,
            AgentCode = input.AgentCode,
            AgentName = agent.Name,
            BranchCode = branchCode,
            LogisticRegionCode = regionCode,
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
            DeviationPercent = deviation,
            RequiresBoardApproval = reason is not null,
            BoardApprovalReason = reason,
        };
        context.MarketTradeTickets.Add(ticket);
        await context.SaveChangesAsync(ct);
        return ticket;
    }

    public async Task<MarketTradeTicket> ApproveAsync(int id, string userName, CancellationToken ct)
    {
        var ticket = await LoadAsync(id, ct);
        switch (ticket.Status)
        {
            case MarketTradeTicketStatus.Pending:
                await guard.EnsureAsync(userName, PermissionCodes.MarketTicketApprove);
                if (ticket.ApprovedBy is null)
                {
                    ticket.ApprovedBy = userName;
                    ticket.ApprovedAt = UtcNow;
                }

                if (ticket.RequiresBoardApproval && ticket.BoardApprovedBy is null)
                {
                    if (!await permissions.HasAsync(userName, PermissionCodes.MarketBoardApprove))
                    {
                        ticket.Status = MarketTradeTicketStatus.PendingBoard;
                        await SaveAsync(ct);
                        return ticket;
                    }

                    StampBoard(ticket, userName);
                }

                break;
            case MarketTradeTicketStatus.PendingBoard:
                await guard.EnsureAsync(userName, PermissionCodes.MarketBoardApprove);
                if (ticket.BoardApprovedBy is null)
                    StampBoard(ticket, userName);
                break;
            default:
                throw new BusinessException($"A boleta nº {id} não está pendente de aprovação.");
        }

        // "Pedido de vez": força o UPDATE (mesmo numa retomada sem campo novo) para o RowVersion barrar o segundo aprovador.
        context.Entry(ticket).Property(t => t.Status).IsModified = true;
        await SaveAsync(ct);

        try
        {
            await contracts.GenerateAsync(ticket, userName, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // O que o Create deixou rastreado (contrato revertido) não pode ir num SaveChanges futuro.
            context.ChangeTracker.Clear();
            throw new BusinessException(
                $"Não foi possível gerar o contrato da boleta nº {id}: {e.Message} Aprove de novo para continuar.");
        }

        ticket.Status = MarketTradeTicketStatus.Approved;
        await SaveAsync(ct);
        return ticket;
    }

    public async Task<MarketTradeTicket> RejectAsync(int id, string reason, string userName, CancellationToken ct)
    {
        var ticket = await LoadAsync(id, ct);
        await EnsureCurrentLevelAsync(ticket, userName);
        if (string.IsNullOrWhiteSpace(reason))
            throw new BusinessException("Informe o motivo da rejeição.");

        ticket.Status = MarketTradeTicketStatus.Rejected;
        ticket.RejectedBy = userName;
        ticket.RejectedAt = UtcNow;
        ticket.RejectionReason = reason.Trim();
        await SaveAsync(ct);
        return ticket;
    }

    public async Task<MarketTradeTicket> CancelAsync(int id, string userName, CancellationToken ct)
    {
        var ticket = await LoadAsync(id, ct);
        EnsureOpen(ticket);
        if (!string.Equals(ticket.CreatedBy, userName, StringComparison.OrdinalIgnoreCase))
            await guard.EnsureAsync(userName, PermissionCodes.MarketTicketApprove);

        ticket.Status = MarketTradeTicketStatus.Canceled;
        ticket.CanceledBy = userName;
        ticket.CanceledAt = UtcNow;
        await SaveAsync(ct);
        return ticket;
    }

    private async Task EnsureCurrentLevelAsync(MarketTradeTicket ticket, string userName)
    {
        EnsureOpen(ticket);
        await guard.EnsureAsync(userName, ticket.Status == MarketTradeTicketStatus.PendingBoard
            ? PermissionCodes.MarketBoardApprove
            : PermissionCodes.MarketTicketApprove);
    }

    private static void EnsureOpen(MarketTradeTicket ticket)
    {
        if (ticket.Status is not (MarketTradeTicketStatus.Pending or MarketTradeTicketStatus.PendingBoard))
            throw new BusinessException($"A boleta nº {ticket.Id} não está pendente.");
    }

    private void StampBoard(MarketTradeTicket ticket, string userName)
    {
        ticket.BoardApprovedBy = userName;
        ticket.BoardApprovedAt = UtcNow;
    }

    private async Task<MarketTradeTicket> LoadAsync(int id, CancellationToken ct) =>
        await context.MarketTradeTickets.FirstOrDefaultAsync(t => t.Id == id, ct)
        ?? throw new KeyNotFoundException($"Boleta {id} não encontrada.");

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            throw new BusinessException(ConcurrencyMessage);
        }
    }

    private static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new BusinessException($"Informe {name}.") : value.Trim();

    private static DateTime? ToDateTime(DateOnly date) => date == default ? null : date.ToDateTime(TimeOnly.MinValue);

    private static string StatusText(MarketPriceStatus status) => status switch
    {
        MarketPriceStatus.MissingQuote => "sem cotação",
        MarketPriceStatus.MissingFx => "sem dólar",
        MarketPriceStatus.MissingFreight => "sem frete vigente para a cidade",
        MarketPriceStatus.InvalidSetup => "configuração inválida",
        _ => "sem preço calculado",
    };
}
