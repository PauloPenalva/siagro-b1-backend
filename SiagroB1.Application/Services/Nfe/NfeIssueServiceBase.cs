using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// "Emitir NF-e" (spec §9.2), comum aos documentos. Na filial com a regra ativa, emitir é o que
/// confirma o documento. Ordem que não pode mudar: reservar o número e SALVAR; assinar e validar;
/// gravar "Em processamento" + XML assinado e SALVAR; só então enviar.
/// </summary>
/// <remarks>
/// Uma cópia só para os dois documentos: a reserva salva antes de assinar, o "Em processamento" com o
/// XML assinado salvo antes do envio, o 539 e a duplicidade conferida pelo digest não podem divergir
/// entre eles. A subclasse diz só o que é do documento: tipo emitível, data, linhas, conferências
/// próprias, prontidão e montagem.
/// </remarks>
public abstract class NfeIssueServiceBase<TDocument>(
    IUnitOfWork db,
    TaxCalculationGate gate,
    INfeDocumentStore<TDocument> store,
    BranchNfeSettingsService settingsService,
    NfeNumberReservationService reservation,
    INfeSefazClient sefaz,
    NfeResultHandlerBase<TDocument> resultHandler,
    NfeOptions options,
    ILogger logger,
    Func<DateTimeOffset>? clock,
    TimeZoneInfo? storageZone)
    where TDocument : class, INfeDocument
{
    private const string NumberUsedHint =
        " — ajuste o Próximo número na Configuração da NF-e se o número já foi usado por outro sistema.";

    private readonly Func<DateTimeOffset> _now = clock ?? NfeIssueInputAssembler.BrasiliaNow;

    // Fuso em que a data do documento está gravada: o OData converte o DateTimeOffset recebido para o
    // fuso do SERVIDOR (TimeZoneInfo.Local), que na VPS é UTC.
    private readonly TimeZoneInfo _storageZone = storageZone ?? TimeZoneInfo.Local;

    public async Task<NfeIssueOutcomeDto> ExecuteAsync(Guid key, string userName)
    {
        // Antes de ler o documento: duas requisições da primeira tentativa transmitiriam duas NF-e.
        await using var emissionLock = await reservation.AcquireEmissionLockAsync(key);

        var invoice = await store.FindWithItemsAsync(key) ?? throw new NotFoundException(store.NotFoundMessage);

        await EnsurePreconditionsAsync(invoice);

        var context = await ValidateReadinessAsync(invoice);

        // A data do documento de origem é gravada no fuso do servidor; o texto cita o dia dela em
        // Brasília, como a conferência do dia de emissão faz.
        if (context.ReturnOrigin is { IssuedOn: { } issuedOn } returnOrigin)
            context = context with
            {
                ReturnOrigin = returnOrigin with
                {
                    IssuedOn = TimeZoneInfo.ConvertTime(
                        DateTime.SpecifyKind(issuedOn, DateTimeKind.Unspecified), _storageZone, NfeIssueInputAssembler.BrasiliaZone),
                },
            };

        using var service = await settingsService.OpenAsync(invoice.BranchCode!);

        await ReserveNumberAsync(invoice, context.Settings);

        // nItem fixo e gravado junto com o XML assinado (o SaveChanges do "Em processamento" ou da
        // rejeição local leva os números): a devolução referencia o item da operação original por ele.
        NfeItemNumbering.Renumber(Lines(invoice));
        BeforeSigning(invoice);

        var input = BuildInput(invoice, context, _now(), options.TechnicalResponsible);

        SignedNfe signed;
        try
        {
            signed = NfeSigner.BuildSignAndValidate(input, service.Settings);
        }
        catch (NfeValidationException e)
        {
            invoice.NfeStatus = NfeStatus.Rejected;
            invoice.NfeStatusCode = null;
            invoice.NfeStatusReason = NfeStatusText.Truncate(
                $"Rejeitada na validação local, nada foi enviado: {e.Message}");
            await db.SaveChangesAsync();

            return NfeIssueOutcomeDto.From(invoice);
        }

        invoice.NfeStatus = NfeStatus.Processing;
        invoice.ChaveNFe = signed.AccessKey;
        invoice.NfeEnvironment = context.Settings.Environment;
        invoice.NfeStatusCode = null;
        invoice.NfeStatusReason = "Enviada à SEFAZ, aguardando o retorno.";
        store.AddXml(invoice, NfeXmlKind.Signed, signed.Xml);
        await db.SaveChangesAsync();

        NfeSefazResult result;
        NfeSefazResult? consulted = null;
        try
        {
            result = await sefaz.AuthorizeAsync(signed, service.Settings);

            if (NfeStatusCodes.IsDuplicate(result.StatusCode) && result.StatusCode != NfeStatusCodes.NumberUsedByAnotherKey)
                consulted = await sefaz.ConsultProtocolAsync(signed.AccessKey, service.Settings);
        }
        catch (NfeCommunicationException)
        {
            invoice.NfeStatusReason = "Sem resposta da SEFAZ — use Consultar situação.";
            await db.SaveChangesAsync();

            return NfeIssueOutcomeDto.From(invoice);
        }
        catch (Exception e)
        {
            // A nota pode já estar autorizada na SEFAZ sem que a resposta tenha sido lida:
            // mantém Em processamento (nunca Rejeitada) e deixa o usuário consultar.
            logger.LogError(e, "Falha inesperada ao transmitir a NF-e do documento {InvoiceKey}.", invoice.Key);
            invoice.NfeStatusReason = NfeStatusText.Truncate(
                $"Sem resposta da SEFAZ — use Consultar situação. (detalhe técnico: {e.Message})");
            await db.SaveChangesAsync();

            return NfeIssueOutcomeDto.From(invoice);
        }

        // 539: o número já existe na SEFAZ com OUTRA chave — não é a mesma nota, então não se
        // consulta. Rejeita e solta a reserva: a próxima tentativa pega um número novo.
        if (result.StatusCode == NfeStatusCodes.NumberUsedByAnotherKey)
        {
            var reason = result.Reason.Length <= 500 - NumberUsedHint.Length
                ? result.Reason
                : result.Reason[..(500 - NumberUsedHint.Length)];

            invoice.NfeStatus = NfeStatus.Rejected;
            invoice.NfeStatusCode = result.StatusCode.ToString(CultureInfo.InvariantCulture);
            invoice.NfeStatusReason = reason + NumberUsedHint;
            invoice.NfeRandomCode = null;
            invoice.TaxDocumentNumber = null;
            invoice.TaxDocumentSeries = null;
            invoice.ChaveNFe = null;
            await db.SaveChangesAsync();

            return NfeIssueOutcomeDto.From(invoice);
        }

        // Fora do try: falha ao gravar/confirmar não é "sem resposta da SEFAZ".
        if (consulted is not null)
        {
            // Duplicidade: o autorizado pode ser o XML de uma tentativa anterior, então todos os
            // assinados do documento (o mais novo primeiro) entram na conferência pelo digest.
            var signedXmls = await store.SignedXmlsAsync(invoice.Key);

            return await resultHandler.ApplyConsultAsync(invoice, signedXmls, consulted, userName);
        }

        return await resultHandler.ApplyAuthorizationAsync(invoice, signed.Xml, result, userName);
    }

    /// <summary>Recusa (<see cref="DefaultException"/>) o tipo de documento que não se emite por aqui.</summary>
    protected abstract void EnsureIssuableType(TDocument document);

    /// <summary>A data do documento, gravada no fuso do servidor; precisa ser a de hoje em Brasília.</summary>
    protected abstract DateTime? DocumentDate(TDocument document);

    /// <summary>As linhas do documento (as mesmas instâncias rastreadas: a numeração grava nelas).</summary>
    protected abstract IReadOnlyList<INfeTaxedLine> Lines(TDocument document);

    /// <summary>Última conferência antes de reservar o número (ex.: o saldo da origem na devolução).</summary>
    protected virtual Task EnsureBeforeReservationAsync(TDocument document) => Task.CompletedTask;

    /// <summary>Confere o cadastro e devolve o que a montagem usa.</summary>
    protected abstract Task<NfeIssueContext> ValidateReadinessAsync(TDocument document);

    protected abstract NfeIssueInput BuildInput(
        TDocument document, NfeIssueContext context, DateTimeOffset issuedAt, NfeTechnicalResponsible? technicalResponsible);

    /// <summary>Depois da numeração dos itens e antes da montagem/assinatura; grava no próximo SaveChanges.</summary>
    protected virtual void BeforeSigning(TDocument document) { }

    /// <summary>
    /// Situação que o documento precisa ter para emitir. Padrão: Pendente — "emitir é o que confirma". O documento de
    /// saída Normal é transmitido já Confirmado (spec 2026-10-06 D1).
    /// </summary>
    protected virtual InvoiceStatus RequiredStatus(TDocument document) => InvoiceStatus.Pending;

    /// <summary>O que o operador faz para corrigir a data do documento; vai na recusa da data.</summary>
    protected virtual string DateFixHint(TDocument document) => "altere a data e salve (os impostos são recalculados).";

    private async Task EnsurePreconditionsAsync(TDocument invoice)
    {
        if (!await gate.IsActiveAsync(invoice.BranchCode))
            throw new DefaultException($"A filial {invoice.BranchCode} não emite NF-e pelo Siagro.");

        EnsureIssuableType(invoice);

        var requiredStatus = RequiredStatus(invoice);
        if (invoice.InvoiceStatus != requiredStatus)
            throw new DefaultException(requiredStatus == InvoiceStatus.Confirmed
                ? "Só documento Confirmado pode ter a NF-e transmitida: confirme o documento antes."
                : "Só documento Pendente pode ser emitido.");

        switch (invoice.NfeStatus)
        {
            case NfeStatus.Processing:
                throw new DefaultException("A NF-e está em processamento na SEFAZ: use Consultar situação.");
            case NfeStatus.Authorized:
                throw new DefaultException("A NF-e deste documento já foi autorizada.");
            case NfeStatus.Denied:
                throw new DefaultException("A NF-e deste documento foi denegada: o número não pode ser reutilizado.");
            case NfeStatus.Cancelled:
                throw new DefaultException("A NF-e deste documento foi cancelada: o número não pode ser reutilizado.");
        }

        // A NF-e leva a data de HOJE (dhEmi); o documento de outro dia teria data e impostos
        // (vigência do IBS/CBS) descolados da nota. Antes de reservar o número. Os dois lados em
        // Brasília: o documento das 22h de Brasília fica gravado no dia seguinte num servidor em UTC.
        var brasilia = NfeIssueInputAssembler.BrasiliaZone;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_now(), brasilia).DateTime);
        DateOnly? invoiceDay = DocumentDate(invoice) is { } stored
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
                DateTime.SpecifyKind(stored, DateTimeKind.Unspecified), _storageZone, brasilia))
            : null;

        if (invoiceDay != today)
            throw new DefaultException(
                $"A data do documento ({invoiceDay:dd/MM/yyyy}) precisa ser a de hoje para emitir a NF-e: " +
                DateFixHint(invoice));

        var lines = Lines(invoice);
        if (lines.Count == 0)
            throw new DefaultException("O documento não tem itens.");

        // Documento de antes da chave, ou que perdeu o cálculo: a emissão não recalcula.
        var uncalculated = lines.FirstOrDefault(i =>
            string.IsNullOrWhiteSpace(i.Cfop) || string.IsNullOrWhiteSpace(i.Ncm) || string.IsNullOrWhiteSpace(i.CstIcms) ||
            string.IsNullOrWhiteSpace(i.CstPis) || string.IsNullOrWhiteSpace(i.CstCofins));

        if (uncalculated is not null)
            throw new DefaultException(
                $"O item {uncalculated.ItemCode} está sem os tributos calculados. Salve o documento para recalcular antes de emitir.");

        await EnsureBeforeReservationAsync(invoice);
    }

    /// <summary>
    /// Primeira tentativa: reserva o número (conexão própria, fora de transação) e gera o cNF. As
    /// seguintes reaproveitam os dois. Salva já: a retentativa depois de uma queda acha o número.
    /// </summary>
    private async Task ReserveNumberAsync(TDocument invoice, BranchNfeSettings settings)
    {
        // Retentativa que não pode reaproveitar a reserva: a série ou o ambiente mudou — a
        // numeração é por ambiente e série. O ambiente é o gravado NA RESERVA, que vale também
        // quando a tentativa anterior parou na validação local (sem XML assinado).
        if (invoice.NfeRandomCode is not null &&
            (invoice.TaxDocumentSeries != settings.Series.ToString(CultureInfo.InvariantCulture) ||
             invoice.NfeEnvironment != settings.Environment))
        {
            invoice.NfeRandomCode = null;
        }

        // Sem cNF = primeira tentativa: número/série digitados à mão não valem, sempre reserva.
        if (invoice.NfeRandomCode is null)
        {
            var number = await reservation.ReserveAsync(invoice.BranchCode!);
            invoice.TaxDocumentNumber = number.ToString("D9", CultureInfo.InvariantCulture);
            invoice.TaxDocumentSeries = settings.Series.ToString(CultureInfo.InvariantCulture);
            invoice.NfeEnvironment = settings.Environment;
        }

        invoice.NfeRandomCode ??= NewRandomCode(invoice.TaxDocumentNumber!);

        await db.SaveChangesAsync();
    }

    /// <summary>8 dígitos aleatórios, diferentes do número da nota (regra da SEFAZ).</summary>
    private static string NewRandomCode(string documentNumber)
    {
        var number = documentNumber.PadLeft(8, '0')[^8..];
        string code;

        do
        {
            code = RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8", CultureInfo.InvariantCulture);
        } while (code == number);

        return code;
    }
}
