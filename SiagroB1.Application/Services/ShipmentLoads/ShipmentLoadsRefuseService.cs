using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesInvoices.Factories;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;
using SiagroB1.Application.Services.Nfe;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>Um documento de saída a devolver, e quanto dele.</summary>
public sealed record RefusalLine(Guid SalesInvoiceKey, decimal Quantity);

public sealed record RefusalRequest(
    Guid ShipmentLoadKey,
    IReadOnlyList<RefusalLine> Lines,
    RefusalDestination Destination,
    string? DestinationWarehouseCode,
    string Reason);

/// <summary>
/// Recusa/devolução de uma carga já faturada: devolve os documentos escolhidos (total ou
/// parcialmente) e, conforme o destino, deixa a carga pronta para refaturamento ou devolve a
/// mercadoria a um armazém.
/// </summary>
/// <remarks>
/// <b>Os três destinos e o que os separa</b> (os efeitos físicos de Warehouse e Transshipment são
/// aplicados por <see cref="ShipmentLoadRefusalEffectsService"/>):
/// <list type="bullet">
/// <item><c>Rebilling</c> — o caminhão segue viagem. As devoluções confirmadas devolvem o saldo
/// da carga e ela reaparece no Faturamento de Expedição. Nada muda no físico: os romaneios
/// continuam montados e o armazém de origem segue debitado.</item>
/// <item><c>Warehouse</c> — a mercadoria é descarregada. Além das devoluções, nasce um romaneio
/// <see cref="StorageTransactionType.SalesShipmentReturn"/> confirmado no armazém escolhido, que
/// credita o saldo dele (é o mesmo tipo que <c>GetWarehouseBalanceAsync</c> já somava) e retira
/// o volume da carga pelo terceiro termo do saldo.</item>
/// <item><c>Transshipment</c> (GAC-1181) — a mercadoria segue para um armazém parceiro para
/// padronização antes de seguir viagem. Além das devoluções, abre uma linha de
/// <see cref="ShipmentLoadTransshipment"/> com o volume recusado — o quarto termo do saldo — e a
/// carga NÃO se encerra: <see cref="ShipmentLoadsRecalculateInvoicedService"/> a resolve como
/// <see cref="ShipmentLoadStatus.InTransshipment"/> enquanto o transbordo estiver aberto. Quem
/// credita o armazém é o <c>ShipmentLoadsTransshipmentRegisterEntryService</c> (Task 5), depois,
/// quando o caminhão for pesado lá — aqui NÃO nasce romaneio 12 nem liberação
/// <c>SalesReturn</c>.</item>
/// </list>
/// <para>
/// <b>Tudo numa transação só, e por isso todos os serviços internos são chamados em
/// <see cref="CommitMode.Deferred"/>.</b> <c>UnitOfWork.CommitAsync</c> comita e zera a
/// transação INCONDICIONALMENTE: um único serviço interno em <c>Auto</c> comitaria a transação
/// daqui no meio da operação, e o resto — a entrada no armazém, o recálculo, os movimentos —
/// rodaria desprotegido, com o commit final estourando NRE. A falha desse jeito é a pior
/// possível: devoluções confirmadas, entrada no armazém faltando, e a carga reaberta para
/// faturamento com a mercadoria fisicamente fora.
/// </para>
/// <para>
/// <b>Uma entrada de armazém por RECUSA, não por documento:</b> a recusa é um evento físico —
/// o caminhão voltou com N kg ao armazém X. A rastreabilidade por documento vive nas próprias
/// devoluções, nos movimentos e no <c>Comments</c> do romaneio.
/// </para>
/// </remarks>
public class ShipmentLoadsRefuseService(
    IUnitOfWork db,
    SalesInvoicesCreateService createService,
    SalesInvoicesConfirmService confirmService,
    ShipmentLoadRefusalEffectsService effects,
    ShipmentLoadsMovementLogService movementLog,
    IWarehouseService warehouseService,
    ILogger<ShipmentLoadsRefuseService> logger,
    TaxCalculationGate? gate = null)
{
    private const decimal Tolerance = 0.001m;

    /// <summary>Mesma regra de <c>SalesInvoicesReturnService</c>: nota autorizada pelo Siagro não volta sem NF-e.</summary>
    private async Task EnsureNotIssuedBySiagroAsync(RefusalRequest request)
    {
        if (gate is null)
            return;

        var keys = request.Lines.Select(l => l.SalesInvoiceKey).ToList();
        // Autorizada ou cancelada (NfeLockRules.IsFrozen) — escrito por extenso para o EF traduzir.
        var issued = await db.Context.SalesInvoices.AsNoTracking()
            .Where(i => keys.Contains(i.Key) && (i.NfeStatus == NfeStatus.Authorized || i.NfeStatus == NfeStatus.Cancelled))
            .Select(i => new { i.BranchCode, i.NfeStatus })
            .Distinct()
            .ToListAsync();

        foreach (var invoice in issued)
        {
            if (!await gate.IsActiveAsync(invoice.BranchCode))
                continue;

            if (invoice.NfeStatus == NfeStatus.Cancelled)
                throw new DefaultException(NfeLockRules.CancelledMessage);

            throw new DefaultException(
                "Na filial que emite NF-e pelo Siagro, a devolução de documento com romaneio ou carga ainda não é suportada.");
        }
    }

    public async Task<ShipmentLoad> ExecuteAsync(RefusalRequest request, string userName)
    {
        await EnsureNotIssuedBySiagroAsync(request);

        var load = await db.Context.ShipmentLoads
                       .FirstOrDefaultAsync(x => x.Key == request.ShipmentLoadKey) ??
                   throw new NotFoundException($"Shipment load not found key {request.ShipmentLoadKey}");

        // TODA a validação antes de qualquer escrita: uma recusa recusada não pode deixar
        // efeito no banco, nem meia devolução criada.
        Validate(load, request);

        if (request.Destination == RefusalDestination.Transshipment)
        {
            // GAC-1181: a MESMA invariante que ShipmentLoadsTransshipmentStartService usa para
            // abrir um transbordo (Task 4) — reaproveitada aqui, e não reimplementada, porque
            // HasOpenTransshipmentAsync só enxerga o ÚLTIMO por Sequence: um segundo transbordo
            // aberto tornaria o mais antigo invisível para o saldo/situação da carga, em
            // silêncio. Alcançável por aqui pela recusa PARCIAL repetida.
            ShipmentLoadTransshipmentRules.EnsureLoadAcceptsTransshipment(load);
            await ShipmentLoadTransshipmentRules.EnsureIsLastAsync(db.Context, load);
        }

        var warehouse = await ResolveWarehouseAsync(request);
        var lines = await ResolveLinesAsync(load, request);

        var totalQuantity = decimal.Round(lines.Sum(l => l.Quantity), 3, MidpointRounding.ToEven);
        var firstInvoice = lines[0].Invoice;

        try
        {
            await db.BeginTransactionAsync();

            // Narrativa da recusa: delta zero (o saldo ainda não mudou), mas com o contexto que
            // o financeiro lê para pagar o frete — cliente, local de entrega e motivo.
            movementLog.Register(
                load.Key,
                ShipmentLoadMovementType.Refused,
                decimal.Zero,
                load.AvailableQuantity,
                $"Recusa registrada em {lines.Count} documento(s) de saída: {totalQuantity:N3}. " +
                $"Documentos: {string.Join(", ", lines.Select(l => l.Invoice.InvoiceNumber))}.",
                userName,
                movementContext: ShipmentLoadMovementContext.FromInvoice(firstInvoice, request.Reason));

            await db.SaveChangesAsync();

            foreach (var line in lines)
            {
                await ReturnAsync(load, line, request.Reason, userName);
            }

            await effects.ApplyAsync(
                new RefusalEffectsInput(
                    load, request.Destination, warehouse?.Code, warehouse?.Name,
                    lines.Select(l => l.Invoice).ToList(), totalQuantity, request.Reason),
                userName);

            load.UpdatedAt = DateTime.Now;
            load.UpdatedBy = userName;

            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e, "Erro ao registrar a recusa da carga {Code}", load.Code);
            throw;
        }

        return load;
    }

    /// <summary>
    /// Cria e CONFIRMA a devolução de um documento, na quantidade informada. Os dois passos
    /// juntos, e diferidos: é a confirmação que devolve o saldo à carga, e deixá-la para depois
    /// significaria a carga não voltar a ficar disponível — que é o objetivo do fluxo.
    /// </summary>
    private async Task ReturnAsync(ShipmentLoad load, ResolvedLine line, string reason, string userName)
    {
        var returnInvoice = SalesInvoiceReturnFactory.CreateFrom(
            line.Invoice, userName, line.QuantitiesByOriginItemKey);

        returnInvoice.Comments =
            $"Recusa da carga {load.Code}. Documento de origem {line.Invoice.InvoiceNumber}. " +
            $"Motivo: {reason}";

        await createService.ExecuteAsync(returnInvoice, userName, CommitMode.Deferred);

        // A nota precisa EXISTIR no banco antes do confirm: ele a busca por chave, e a fórmula
        // do saldo da carga agrega no servidor.
        await db.SaveChangesAsync();

        await confirmService.ExecuteAsync(returnInvoice.Key, userName, CommitMode.Deferred);

        await db.SaveChangesAsync();
    }

    private static void Validate(ShipmentLoad load, RefusalRequest request)
    {
        if (load.Status == ShipmentLoadStatus.Cancelled)
            throw new ApplicationException($"A carga {load.Code} está cancelada e não pode ser recusada.");

        ShipmentLoadRefusalRules.EnsureNoPendingRefusal(load);

        if (load.Status == ShipmentLoadStatus.Planned)
            throw new ApplicationException(
                $"A carga {load.Code} ainda está apenas planejada — não há faturamento a recusar.");

        // GAC-1171: a carga descarregada foi ACEITA no destino — há ticket em toda linha entregue.
        // Uma recusa por cima contradiria o ticket, então o caminho é excluí-lo primeiro.
        if (load.Status is ShipmentLoadStatus.Discharged)
            throw new ApplicationException(
                $"A carga {load.Code} já foi descarregada no destino. " +
                "Exclua o ticket de descarga antes de registrar recusa.");

        // A Concluída Normal também foi aceita, mas tem mensagem própria: excluir o ticket não a
        // desfaz (a Concluída vence a Descarregada). O primeiro passo de volta é estornar a conferência.
        if (load.Status == ShipmentLoadStatus.Completed && load.LoadType == ShipmentLoadType.Normal)
            throw new ApplicationException(
                $"A carga {load.Code} já foi concluída. " +
                $"{ShipmentLoadCompletionRules.UndoHint(load)} antes de registrar recusa.");

        // GAC-1175: a recusa devolve mercadoria de um documento de SAÍDA, e a carga de remoção
        // não emite nenhum. O caminho de desfazer dela é desvincular a entrada.
        if (load.LoadType == ShipmentLoadType.Removal)
            throw new ApplicationException(
                $"A carga {load.Code} é do tipo Remoção e não possui documentos de saída a recusar.");

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ApplicationException("Informe o motivo da recusa.");

        if (request.Lines.Count == 0)
            throw new ApplicationException(
                "Informe a quantidade a devolver de ao menos um documento de saída.");

        if (request.Destination is RefusalDestination.Warehouse or RefusalDestination.Transshipment &&
            string.IsNullOrWhiteSpace(request.DestinationWarehouseCode))
        {
            throw new ApplicationException(
                "Informe o armazém de destino da mercadoria devolvida.");
        }
    }

    private async Task<WarehouseTarget?> ResolveWarehouseAsync(RefusalRequest request)
    {
        if (request.Destination is not (RefusalDestination.Warehouse or RefusalDestination.Transshipment))
            return null;

        var code = request.DestinationWarehouseCode!.Trim();

        // Resolvido por IWarehouseService, e não pela tabela local: em modo SAPB1 o "armazém" é
        // parceiro de negócio no OCRD e WAREHOUSES está vazia.
        var warehouse = await warehouseService.GetByIdAsync(code)
                        ?? throw new ApplicationException($"Armazém {code} não encontrado.");

        return new WarehouseTarget(warehouse.Code ?? code, warehouse.Name);
    }

    /// <summary>
    /// Casa cada linha pedida com o documento e valida o volume devolvível, ANTES de escrever.
    /// </summary>
    /// <remarks>
    /// A checagem de volume repete o predicado de
    /// <c>SalesInvoicesConfirmService.ValidateLineItemBalance</c> de propósito: aquele é a
    /// última linha de defesa e estoura lá no fundo com mensagem técnica; aqui o usuário recebe
    /// o número que pode digitar.
    /// </remarks>
    private async Task<IReadOnlyList<ResolvedLine>> ResolveLinesAsync(
        ShipmentLoad load, RefusalRequest request)
    {
        var requested = request.Lines
            .Where(l => l.Quantity > decimal.Zero)
            .ToList();

        if (requested.Count == 0)
            throw new ApplicationException(
                "Informe a quantidade a devolver de ao menos um documento de saída.");

        var duplicated = requested
            .GroupBy(l => l.SalesInvoiceKey)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicated != null)
            throw new ApplicationException("O mesmo documento de saída foi informado duas vezes.");

        var resolved = new List<ResolvedLine>();

        foreach (var line in requested)
        {
            var invoice = await db.Context.SalesInvoices
                              .Include(x => x.Items)
                              .FirstOrDefaultAsync(x => x.Key == line.SalesInvoiceKey) ??
                          throw new NotFoundException(
                              $"Documento de saída {line.SalesInvoiceKey} não encontrado.");

            if (invoice.ShipmentLoadKey != load.Key)
                throw new ApplicationException(
                    $"O documento de saída {invoice.InvoiceNumber} não pertence à carga {load.Code}.");

            if (invoice.InvoiceType != SalesInvoiceType.Normal)
                throw new ApplicationException(
                    $"O documento {invoice.InvoiceNumber} é uma devolução e não pode ser recusado.");

            if (invoice.InvoiceStatus != InvoiceStatus.Confirmed)
                throw new ApplicationException(
                    $"O documento de saída {invoice.InvoiceNumber} está em situação " +
                    $"{invoice.InvoiceStatus} — só documentos confirmados podem ser recusados.");

            var quantities = await ResolveItemQuantitiesAsync(invoice, line.Quantity);

            resolved.Add(new ResolvedLine(invoice, line.Quantity, quantities));
        }

        return resolved;
    }

    /// <summary>
    /// Distribui a quantidade recusada do documento entre os itens dele, respeitando o que cada
    /// item ainda tem a devolver.
    /// </summary>
    /// <remarks>
    /// O documento nascido de carga tem UM item — o caso de todo faturamento de expedição —, e aí
    /// a distribuição é direta. O laço existe para o documento de vários itens não ficar sem
    /// resposta: consome na ordem, item a item, e sobra vira erro em vez de silêncio.
    /// </remarks>
    private async Task<IReadOnlyDictionary<Guid, decimal>> ResolveItemQuantitiesAsync(
        SalesInvoice invoice, decimal quantity)
    {
        var quantities = new Dictionary<Guid, decimal>();
        var remaining = quantity;

        foreach (var item in invoice.Items.Where(i => i.Key != null))
        {
            if (remaining <= Tolerance)
                break;

            var alreadyReturned = await db.Context.SalesInvoicesItems
                .AsNoTracking()
                .Where(x => x.SalesInvoiceItemOriginKey == item.Key!.Value &&
                            x.SalesInvoice!.InvoiceType == SalesInvoiceType.Return &&
                            x.SalesInvoice.InvoiceStatus != InvoiceStatus.Cancelled)
                .SumAsync(x => (decimal?)x.Quantity) ?? decimal.Zero;

            var refusable = item.Quantity - alreadyReturned;

            if (refusable <= Tolerance)
                continue;

            var take = Math.Min(remaining, refusable);

            quantities[item.Key!.Value] = decimal.Round(take, 3, MidpointRounding.ToEven);
            remaining -= take;
        }

        if (remaining > Tolerance)
        {
            var refusableTotal = decimal.Round(quantity - remaining, 3, MidpointRounding.ToEven);

            throw new ApplicationException(
                $"Quantidade a devolver ({quantity:N3}) maior que o saldo devolvível do documento " +
                $"de saída {invoice.InvoiceNumber} ({refusableTotal:N3}).");
        }

        return quantities;
    }

    private sealed record ResolvedLine(
        SalesInvoice Invoice,
        decimal Quantity,
        IReadOnlyDictionary<Guid, decimal> QuantitiesByOriginItemKey);

    private sealed record WarehouseTarget(string Code, string? Name);
}
