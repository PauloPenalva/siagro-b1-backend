using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices.Factories;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Um romaneio que voltou, e quanto dele. <see cref="Quantity"/> nula significa o romaneio
/// INTEIRO — o caso comum, e o único que o destino "segue viagem" aceita.
/// </summary>
public sealed record SalesInvoiceReturnShipment(Guid StorageTransactionKey, decimal? Quantity);

/// <summary>Um retorno de documento de saída: quais romaneios voltaram, para onde e por quê.</summary>
public sealed record SalesInvoiceReturnRequest(
    Guid SalesInvoiceKey,
    IReadOnlyList<SalesInvoiceReturnShipment> Shipments,
    RefusalDestination Destination,
    string? DestinationWarehouseCode,
    string Reason);

/// <summary>
/// Retorno de um documento de saída LEGADO — aquele cujos romaneios estão ligados direto à nota
/// (<c>SalesTransactions</c> preenchida, sem carga). Devolve os romaneios escolhidos, total ou
/// parcialmente, e conforme o destino deixa a mercadoria pronta para refaturamento ou a devolve a
/// um armazém.
/// </summary>
/// <remarks>
/// <b>A unidade de escolha é o ROMANEIO; a quantidade é opcional dentro dele.</b> Sem quantidade
/// informada o romaneio volta inteiro, e o volume devolvido é a soma do <c>NetWeight</c> dos
/// escolhidos. Informar quantidade devolve só parte da carreta — o cliente recebeu uma parte e
/// recusou o resto.
/// <para>
/// ⚠️ <b>Quantidade parcial só existe no destino <c>Warehouse</c>.</b> Em <c>Rebilling</c> o
/// romaneio volta INTEIRO ao pool de faturamento, e devolvê-lo pela metade des-faturaria o volume
/// que ficou com o cliente; representar essa metade exigiria partir o romaneio em dois registros,
/// com código, alocação e rastreabilidade próprios — deliberadamente fora de escopo.
/// </para>
/// <para>
/// <b>Os dois destinos, e o que os separa:</b>
/// <list type="bullet">
/// <item><c>Rebilling</c> — o caminhão segue viagem. Os romaneios escolhidos voltam a
/// <c>Confirmed</c> e soltos de nota, reaparecendo no faturamento e na Montagem de Carga. O
/// armazém de origem segue debitado: o grão está no caminhão.</item>
/// <item><c>Warehouse</c> — a mercadoria é descarregada. Nasce um romaneio
/// <see cref="StorageTransactionType.SalesShipmentReturn"/> confirmado no armazém escolhido, que
/// credita o saldo dele, e os romaneios de origem ficam <c>Invoiced</c> — continuam debitando o
/// armazém de onde saíram.</item>
/// </list>
/// </para>
/// <para>
/// ⚠️ <b>Em nenhum dos dois o romaneio de origem pode ficar <c>Returned</c>.</b> Aquele status
/// significa "o embarque não aconteceu": o romaneio sai das consultas de saldo e o armazém de
/// origem é re-creditado sozinho — que é exatamente o comportamento implícito do retorno antigo,
/// e a razão de ele só saber devolver ao armazém de onde o grão saiu. Nos dois destinos daqui o
/// embarque ACONTECEU, então o débito da origem tem de ficar de pé.
/// </para>
/// <para>
/// <b>Cria e CONFIRMA a devolução, tudo numa transação só, e por isso todos os serviços internos
/// são chamados em <see cref="CommitMode.Deferred"/>.</b> <c>UnitOfWork.CommitAsync</c> comita e
/// zera a transação INCONDICIONALMENTE: um único serviço interno em <c>Auto</c> comitaria a
/// transação daqui no meio da operação, e o resto — a entrada no armazém, o recálculo — rodaria
/// desprotegido, com o commit final estourando NRE.
/// </para>
/// </remarks>
public class SalesInvoicesReturnService(
    IUnitOfWork db,
    SalesInvoicesCreateService createService,
    SalesInvoicesConfirmService confirmService,
    StorageTransactionsCreateService storageCreate,
    StorageTransactionsConfirmedService storageConfirm,
    IWarehouseService warehouseService,
    ILogger<SalesInvoicesReturnService> logger)
{
    private const decimal Tolerance = 0.001m;

    /// <summary>Cultura das quantidades que vão para texto lido pelo operador.</summary>
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<SalesInvoice> ExecuteAsync(SalesInvoiceReturnRequest request, string userName)
    {
        var originInvoice = await db.Context.SalesInvoices
                                .Include(x => x.SalesTransactions)
                                .Include(x => x.Items)
                                .FirstOrDefaultAsync(x => x.Key == request.SalesInvoiceKey) ??
                            throw new NotFoundException("Documento de saída não encontrado.");

        // TODA a validação antes de qualquer escrita: um retorno recusado não pode deixar meia
        // devolução no banco.
        Validate(originInvoice, request);

        var warehouse = await ResolveWarehouseAsync(request);
        var shipments = ResolveShipments(originInvoice, request);

        var totalQuantity = decimal.Round(
            shipments.Sum(s => s.Quantity), 3, MidpointRounding.ToEven);

        var quantities = ResolveItemQuantities(originInvoice, totalQuantity);

        var outcomes = shipments.ToDictionary(
            s => s.Shipment.Key,
            _ => request.Destination == RefusalDestination.Warehouse
                ? StorageTransactionsStatus.Invoiced
                : StorageTransactionsStatus.Confirmed);

        try
        {
            await db.BeginTransactionAsync();

            var returnInvoice = SalesInvoiceReturnFactory.CreateFrom(
                originInvoice, userName, quantities);

            returnInvoice.Comments =
                $"Retorno do doc.saída {originInvoice.InvoiceNumber}. " +
                $"Romaneio(s): {Describe(shipments)}. " +
                $"Motivo: {request.Reason}\n";

            await createService.ExecuteAsync(returnInvoice, userName, CommitMode.Deferred);

            // A nota precisa EXISTIR no banco antes do confirm: ele a busca por chave, e as
            // fórmulas de saldo agregam no servidor.
            await db.SaveChangesAsync();

            await confirmService.ExecuteAsync(
                returnInvoice.Key, userName, CommitMode.Deferred, outcomes);

            await db.SaveChangesAsync();

            if (request.Destination == RefusalDestination.Warehouse)
            {
                await ReturnToWarehouseAsync(
                    originInvoice, returnInvoice, warehouse!, shipments, totalQuantity,
                    request.Reason, userName);
            }

            originInvoice.Comments +=
                $"Doc.Saída retornado pelo Doc.Saída {returnInvoice.InvoiceNumber}. " +
                $"Motivo: {request.Reason}\n";

            // Só o retorno TOTAL fecha a origem. Fechá-la numa devolução parcial faria o segundo
            // retorno da mesma nota morrer na validação de documento encerrado, sem saída pela
            // tela. Quem decide é o mesmo predicado que o confirm usa para o status.
            if (await IsFullyReturnedAsync(originInvoice))
            {
                originInvoice.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;

                foreach (var item in originInvoice.Items)
                {
                    item.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;
                    item.DeliveredQuantity = item.Quantity;
                }
            }

            originInvoice.UpdatedAt = DateTime.Now;
            originInvoice.UpdatedBy = userName;

            await db.SaveChangesAsync();

            // Fechar os itens muda o fator efetivo (item com QuantityLoss passa a contar
            // NetQuantity) → recalcula os contratos com alocação nesses itens no ledger. Passar as
            // CHAVES é o que torna o resultado correto: sem elas o SumAsync agregaria no servidor
            // e leria a entrega ainda aberta.
            await SalesContractsRecalculateBalanceService.RecalculateForItemsAsync(
                db.Context,
                originInvoice.Items.Where(i => i.Key != null).Select(i => i.Key!.Value).ToList());

            await db.SaveChangesAsync();
            await db.CommitAsync();

            return returnInvoice;
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e, "Erro ao retornar o documento de saída {Number}", originInvoice.InvoiceNumber);
            throw;
        }
    }

    /// <summary>
    /// Descarrega a mercadoria devolvida no armazém escolhido.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Três chaves que este romaneio NÃO pode carregar</b>, cada uma por um motivo próprio:
    /// <list type="bullet">
    /// <item><c>ShipmentLoadKey</c> — é somada por <c>ShipmentLoadsRecalculateTotalService</c>
    /// como volume EMBARCADO; a devolução aumentaria o total de uma carga.</item>
    /// <item><c>ShipmentReleaseKey</c> — <c>ShipmentReleasesRecalculateShippedService</c> conta o
    /// tipo 12 no eixo das liberações de COMPRA; a devolução moveria um saldo alheio.</item>
    /// <item><c>ReturnInvoiceKey</c> — é o discriminador <c>isNewFlow</c> de
    /// <c>SalesInvoicesReverseConfirmService</c>: com ela, um estorno carimbaria esta entrada
    /// como <c>Invoiced</c> e a anexaria à nota de origem.</item>
    /// </list>
    /// <c>SalesInvoiceKey</c> também fica nula, e por um motivo próprio: aquela significa
    /// "romaneio FATURADO nesta nota" e é o que <c>ShipmentBillingTransactionGuardService</c> lê
    /// para recusar refaturamento. O vínculo certo é
    /// <see cref="StorageTransaction.GeneratedByReturnInvoiceKey"/>, que é o que os guards de
    /// cancelamento/estorno reconhecem.
    /// <para>
    /// <c>StorageAddressCode</c> fica nulo porque a devolução é entrada em nível de ARMAZÉM — o
    /// saldo por ENDEREÇO não credita o tipo 12. Endereçá-la exige acertar os seis serviços que o
    /// <c>remarks</c> de <c>ShipmentLoadsRefuseService.ReturnToWarehouseAsync</c> lista.
    /// </para>
    /// <para>
    /// <b>Uma entrada por RETORNO, não por romaneio:</b> a devolução é um evento físico — o
    /// caminhão voltou com N kg ao armazém X. A rastreabilidade por romaneio vive no
    /// <c>Comments</c> e nos próprios romaneios devolvidos.
    /// </para>
    /// </remarks>
    private async Task ReturnToWarehouseAsync(
        SalesInvoice originInvoice,
        SalesInvoice returnInvoice,
        WarehouseTarget warehouse,
        IReadOnlyList<ResolvedShipment> shipments,
        decimal totalQuantity,
        string reason,
        string userName)
    {
        var first = shipments[0].Shipment;
        var codes = Describe(shipments);

        var entry = new StorageTransaction
        {
            TransactionType = StorageTransactionType.SalesShipmentReturn,
            TransactionStatus = StorageTransactionsStatus.Pending,
            TransactionDate = DateTime.Now.Date,
            BranchCode = originInvoice.BranchCode ?? first.BranchCode,
            ItemCode = first.ItemCode,
            ItemName = first.ItemName,
            UnitOfMeasureCode = first.UnitOfMeasureCode,
            WarehouseCode = warehouse.Code,
            CardCode = originInvoice.CardCode ?? first.CardCode,
            TruckCode = first.TruckCode,
            TruckDriverCode = first.TruckDriverCode,
            GrossWeight = totalQuantity,
            NetWeight = totalQuantity,
            // O documento de RETORNO, e não a nota de origem: uma nota pode ser retornada em
            // parcelas, cada uma com sua devolução, e o estorno de uma delas precisa achar
            // exatamente a sua.
            GeneratedByReturnInvoiceKey = returnInvoice.Key,
            Comments =
                $"Devolução do documento de saída {originInvoice.InvoiceNumber} " +
                $"pelo retorno {returnInvoice.InvoiceNumber}. " +
                $"Motivo: {reason}. Romaneio(s) de origem: {codes}.",
        };

        await storageCreate.ExecuteAsync(
            entry, userName, TransactionCode.StorageTransaction, CommitMode.Deferred);

        await db.SaveChangesAsync();

        await storageConfirm.ExecuteAsync(entry, userName, CommitMode.Deferred);

        await db.SaveChangesAsync();
    }

    private static void Validate(SalesInvoice invoice, SalesInvoiceReturnRequest request)
    {
        if (invoice.InvoiceStatus != InvoiceStatus.Confirmed)
        {
            throw new ApplicationException(
                $"O documento de saída {invoice.InvoiceNumber} está em situação " +
                $"{invoice.InvoiceStatus} — só documentos confirmados podem ser retornados.");
        }

        if (invoice.InvoiceType == SalesInvoiceType.Return)
        {
            throw new ApplicationException(
                $"O documento {invoice.InvoiceNumber} já é uma devolução e não pode ser retornado.");
        }

        // A nota de CARGA tem tela própria de recusa, que sabe mexer no saldo da carga. Aqui ela
        // cairia num caminho que não conhece nenhuma das regras dela — e sua coleção de romaneios
        // é vazia, então nem haveria o que escolher.
        if (invoice.ShipmentLoadKey != null)
        {
            throw new ApplicationException(
                $"O documento {invoice.InvoiceNumber} pertence a uma carga. " +
                "Registre a recusa pela tela de Montagem de Carga.");
        }

        if (invoice.DeliveryStatus == SalesInvoiceDeliveryStatus.Closed)
        {
            throw new ApplicationException(
                $"O documento de saída {invoice.InvoiceNumber} já está encerrado.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ApplicationException("Informe o motivo do retorno.");

        if (request.Shipments.Count == 0)
            throw new ApplicationException("Selecione ao menos um romaneio a devolver.");

        if (request.Destination == RefusalDestination.Warehouse &&
            string.IsNullOrWhiteSpace(request.DestinationWarehouseCode))
        {
            throw new ApplicationException(
                "Informe o armazém de destino da mercadoria devolvida.");
        }
    }

    private async Task<WarehouseTarget?> ResolveWarehouseAsync(SalesInvoiceReturnRequest request)
    {
        if (request.Destination != RefusalDestination.Warehouse)
            return null;

        var code = request.DestinationWarehouseCode!.Trim();

        // Resolvido por IWarehouseService, e não pela tabela local: em modo SAPB1 o "armazém" é
        // parceiro de negócio no OCRD e WAREHOUSES está vazia.
        var warehouse = await warehouseService.GetByIdAsync(code)
                        ?? throw new ApplicationException($"Armazém {code} não encontrado.");

        return new WarehouseTarget(warehouse.Code ?? code, warehouse.Name);
    }

    /// <summary>
    /// Casa cada romaneio pedido com os da nota e resolve quanto voltou de cada um, ANTES de
    /// escrever qualquer coisa.
    /// </summary>
    /// <remarks>
    /// O teto por linha é o <c>NetWeight</c> do próprio romaneio — não existe acumulador por
    /// romaneio, então este limite não desconta o que já voltou dele em retornos anteriores. Quem
    /// impede devolver mais do que a nota vendeu é o teto por ITEM, em
    /// <see cref="ResolveItemQuantities"/> e em <c>SalesInvoicesConfirmService</c>, que somam
    /// todas as devoluções vivas da origem.
    /// </remarks>
    private static IReadOnlyList<ResolvedShipment> ResolveShipments(
        SalesInvoice invoice, SalesInvoiceReturnRequest request)
    {
        var lines = request.Shipments;

        if (lines.Select(x => x.StorageTransactionKey).Distinct().Count() != lines.Count)
            throw new ApplicationException("O mesmo romaneio foi informado duas vezes.");

        var resolved = new List<ResolvedShipment>();

        foreach (var line in lines)
        {
            var shipment = invoice.SalesTransactions
                               .FirstOrDefault(x => x.Key == line.StorageTransactionKey)
                           ?? throw new ApplicationException(
                               $"O romaneio informado não pertence ao documento de saída " +
                               $"{invoice.InvoiceNumber}.");

            if (shipment.TransactionType != StorageTransactionType.SalesShipment)
                throw new ApplicationException(
                    $"O documento {shipment.Code} não é um romaneio de embarque.");

            if (shipment.TransactionStatus != StorageTransactionsStatus.Invoiced)
                throw new ApplicationException(
                    $"O romaneio {shipment.Code} está em situação {shipment.TransactionStatus} " +
                    "e não pode ser devolvido.");

            resolved.Add(new ResolvedShipment(shipment, ResolveQuantity(shipment, line, request)));
        }

        return resolved;
    }

    /// <summary>Quanto voltou deste romaneio: o informado, ou a carreta inteira.</summary>
    private static decimal ResolveQuantity(
        StorageTransaction shipment,
        SalesInvoiceReturnShipment line,
        SalesInvoiceReturnRequest request)
    {
        if (line.Quantity is not { } quantity)
            return shipment.NetWeight;

        // A quantidade inválida vem antes da regra do destino: dizer "só é parcial no armazém"
        // para quem digitou zero manda o operador mexer no lugar errado.
        if (quantity <= Tolerance)
        {
            throw new ApplicationException(
                $"Informe a quantidade a devolver do romaneio {shipment.Code}.");
        }

        // Em "segue viagem" o romaneio volta INTEIRO ao pool de faturamento: não há como
        // representar meia carreta livre sem partir o registro em dois.
        if (Math.Abs(quantity - shipment.NetWeight) > Tolerance &&
            request.Destination != RefusalDestination.Warehouse)
        {
            throw new ApplicationException(
                $"O romaneio {shipment.Code} só pode ser devolvido em quantidade parcial quando " +
                "a mercadoria retorna a um armazém. Com o caminhão seguindo viagem, o romaneio " +
                "volta inteiro.");
        }

        if (quantity > shipment.NetWeight + Tolerance)
        {
            throw new ApplicationException(
                $"A quantidade a devolver do romaneio {shipment.Code} " +
                $"({quantity.ToString("N3", PtBr)}) é maior que o peso líquido dele " +
                $"({shipment.NetWeight.ToString("N3", PtBr)}).");
        }

        return decimal.Round(quantity, 3, MidpointRounding.ToEven);
    }

    /// <summary>
    /// Os romaneios devolvidos como o operador vai lê-los: o parcial mostra quanto voltou de
    /// quanto, porque sem acumulador por romaneio é o <c>Comments</c> que guarda essa conta.
    /// </summary>
    private static string Describe(IReadOnlyList<ResolvedShipment> shipments) =>
        string.Join(", ", shipments.Select(s =>
            Math.Abs(s.Quantity - s.Shipment.NetWeight) <= Tolerance
                ? s.Shipment.Code
                : $"{s.Shipment.Code} ({s.Quantity.ToString("N3", PtBr)} de " +
                  $"{s.Shipment.NetWeight.ToString("N3", PtBr)})"));

    /// <summary>
    /// Distribui o volume devolvido entre os itens da nota, respeitando o que cada um ainda tem a
    /// devolver.
    /// </summary>
    /// <remarks>
    /// A nota do faturamento de expedição tem UM item — o caso de sempre —, e aí a distribuição é
    /// direta. O laço existe para a nota de vários itens não ficar sem resposta: consome na ordem,
    /// item a item, e sobra vira erro em vez de silêncio.
    /// </remarks>
    private Dictionary<Guid, decimal> ResolveItemQuantities(SalesInvoice invoice, decimal quantity)
    {
        var quantities = new Dictionary<Guid, decimal>();
        var remaining = quantity;

        foreach (var item in invoice.Items.Where(i => i.Key != null))
        {
            if (remaining <= Tolerance)
                break;

            var alreadyReturned = db.Context.SalesInvoicesItems
                .Where(x => x.SalesInvoiceItemOriginKey == item.Key!.Value &&
                            x.SalesInvoice!.InvoiceType == SalesInvoiceType.Return &&
                            x.SalesInvoice.InvoiceStatus != InvoiceStatus.Cancelled)
                .Sum(x => (decimal?)x.Quantity) ?? decimal.Zero;

            var returnable = item.Quantity - alreadyReturned;

            if (returnable <= Tolerance)
                continue;

            var take = Math.Min(remaining, returnable);

            quantities[item.Key!.Value] = decimal.Round(take, 3, MidpointRounding.ToEven);
            remaining -= take;
        }

        if (remaining > Tolerance)
        {
            var returnableTotal = decimal.Round(quantity - remaining, 3, MidpointRounding.ToEven);

            throw new ApplicationException(
                $"Volume a devolver ({quantity:N3}) maior que o saldo devolvível do documento " +
                $"de saída {invoice.InvoiceNumber} ({returnableTotal:N3}).");
        }

        return quantities;
    }

    /// <summary>
    /// A origem está inteiramente devolvida? Para CADA item, a soma das devoluções vivas — a que
    /// acabou de ser confirmada inclusive — tem de alcançar a quantidade faturada.
    /// </summary>
    /// <remarks>
    /// Espelha <c>SalesInvoicesConfirmService.IsFullyReturnedAsync</c>, que decide o status pelo
    /// mesmo critério. Os dois precisam concordar: um documento marcado <c>Returned</c> com a
    /// entrega aberta, ou o contrário, é um estado sem saída pela tela.
    /// </remarks>
    private async Task<bool> IsFullyReturnedAsync(SalesInvoice originInvoice)
    {
        var originItems = await db.Context.SalesInvoicesItems
            .AsNoTracking()
            .Where(x => x.SalesInvoiceKey == originInvoice.Key)
            .Select(x => new { x.Key, x.Quantity })
            .ToListAsync();

        if (originItems.Count == 0)
            return true;

        foreach (var originItem in originItems)
        {
            var returned = await db.Context.SalesInvoicesItems
                .AsNoTracking()
                .Where(x => x.SalesInvoiceItemOriginKey == originItem.Key &&
                            x.SalesInvoice!.InvoiceType == SalesInvoiceType.Return &&
                            x.SalesInvoice.InvoiceStatus != InvoiceStatus.Cancelled)
                .SumAsync(x => (decimal?)x.Quantity) ?? decimal.Zero;

            if (returned < originItem.Quantity - Tolerance)
                return false;
        }

        return true;
    }

    private sealed record WarehouseTarget(string Code, string? Name);

    /// <summary>Um romaneio da nota e quanto dele voltou.</summary>
    private sealed record ResolvedShipment(StorageTransaction Shipment, decimal Quantity);
}
