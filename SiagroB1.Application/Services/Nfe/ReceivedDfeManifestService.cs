using System.Xml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Manifestação do destinatário sobre um DF-e recebido. A Ciência é a etapa inicial; Confirmação, Desconhecimento e
/// Operação não Realizada são conclusivas (depois delas nada mais é aceito). Depois da Ciência ou da Confirmação a
/// SEFAZ libera o XML completo, que é baixado em seguida (falha aí não desfaz a manifestação).
/// </summary>
public class ReceivedDfeManifestService(
    IUnitOfWork db,
    BranchNfeSettingsService settingsService,
    INfeSefazClient sefaz,
    ILogger<ReceivedDfeManifestService> logger,
    Func<DateTimeOffset>? clock = null)
{
    public const int MinJustification = 15;
    public const int MaxJustification = 255;

    private readonly Func<DateTimeOffset> _now = clock ?? NfeIssueInputAssembler.BrasiliaNow;

    public async Task<ReceivedDfe> ExecuteAsync(int id, NfeManifestationType type, string? justification, CancellationToken ct = default)
    {
        var row = await LoadAsync(id, ct);

        Validate(row, type);

        var text = justification?.Trim();

        if (type == NfeManifestationType.NotPerformed)
        {
            if (text is null || text.Length < MinJustification || text.Length > MaxJustification)
                throw new DefaultException($"Informe uma justificativa entre {MinJustification} e {MaxJustification} caracteres.");
        }
        else
        {
            text = null;
        }

        var taxId = await TaxIdAsync(row.BranchCode, ct);

        using var service = await settingsService.OpenAsync(row.BranchCode);

        var result = await sefaz.SendManifestationAsync(
            new NfeManifestationRequest(row.AccessKey, type, taxId, text, _now()), service.Settings, ct);

        if (!NfeStatusCodes.IsManifestationRegistered(result.StatusCode) && result.StatusCode != NfeStatusCodes.DuplicateEvent)
            throw new DefaultException($"{result.StatusCode} - {result.Reason}");

        row.ManifestationStatus = StatusOf(type);
        // 573 (duplicidade) costuma vir sem protocolo/procEvento: nunca apaga a prova já guardada.
        row.ManifestationProtocol = result.Protocol ?? row.ManifestationProtocol;
        row.ManifestedAt = result.RegisteredAt ?? row.ManifestedAt ?? _now();
        row.ManifestationJustification = text ?? row.ManifestationJustification;
        row.ManifestationProcEventXml = result.ProcEventXml ?? row.ManifestationProcEventXml;
        await db.SaveChangesAsync();

        if (type is NfeManifestationType.Acknowledgement or NfeManifestationType.Confirmation && row.Xml is null)
        {
            try
            {
                await DownloadAsync(row, taxId, service.Settings, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // A manifestação já está registrada na SEFAZ: o XML pode ser baixado depois.
                logger.LogWarning(e, "Download do XML do DF-e {Key} falhou após a manifestação.", row.AccessKey);
            }
        }

        return row;
    }

    /// <summary>Consulta o XML completo por chave. Devolve o DF-e (com <c>Xml</c> preenchido quando a SEFAZ já o liberou).</summary>
    public async Task<ReceivedDfe> DownloadXmlAsync(int id, CancellationToken ct = default)
    {
        var row = await LoadAsync(id, ct);
        var taxId = await TaxIdAsync(row.BranchCode, ct);

        using var service = await settingsService.OpenAsync(row.BranchCode);
        await DownloadAsync(row, taxId, service.Settings, ct);

        return row;
    }

    private async Task DownloadAsync(ReceivedDfe row, string taxId, NfeServiceSettings settings, CancellationToken ct)
    {
        // A consulta por chave é o mesmo serviço da distribuição por NSU: respeita (e grava) a espera da filial.
        var state = await db.Context.BranchDfeSyncStates.FirstOrDefaultAsync(s => s.BranchCode == row.BranchCode, ct);

        if (state?.NextQueryAllowedAt is { } allowed && allowed > _now())
            throw new DefaultException(WaitMessage(allowed));

        var result = await sefaz.DistributionByKeyAsync(taxId, row.AccessKey, settings, ct);

        if (result.StatusCode == NfeStatusCodes.ExcessiveUse)
        {
            if (state is null)
            {
                state = new BranchDfeSyncState { BranchCode = row.BranchCode };
                db.Context.BranchDfeSyncStates.Add(state);
            }

            state.NextQueryAllowedAt = _now() + Wait;
            await db.SaveChangesAsync();
            throw new DefaultException(WaitMessage(state.NextQueryAllowedAt.Value));
        }

        if (result.StatusCode == NfeStatusCodes.NoDocuments)
            return;

        if (result.StatusCode != NfeStatusCodes.DocumentsFound)
            throw new DefaultException($"{result.StatusCode} - {result.Reason}");

        foreach (var document in result.Documents)
        {
            DfeParsed parsed;

            try
            {
                parsed = NfeDistributionDocumentParser.Parse(document);
            }
            catch (XmlException e)
            {
                logger.LogWarning(e, "DF-e {Key}: XML da consulta por chave ilegível.", row.AccessKey);
                throw new DefaultException("O XML devolvido pela SEFAZ está ilegível. Tente baixar novamente mais tarde.");
            }

            if (parsed.Kind != DfeKind.FullInvoice || parsed.FullXml is null)
                continue;

            row.Xml = parsed.FullXml;
            row.XmlDownloadedAt = _now();
            await db.SaveChangesAsync();
            return;
        }
    }

    private static void Validate(ReceivedDfe row, NfeManifestationType type)
    {
        var status = row.ManifestationStatus;

        if (type == NfeManifestationType.Acknowledgement)
        {
            if (status != DfeManifestationStatus.None)
                throw new DefaultException("A Ciência da Operação só pode ser registrada uma vez, antes de qualquer outra manifestação.");

            return;
        }

        if (status is not (DfeManifestationStatus.None or DfeManifestationStatus.Acknowledged))
            throw new DefaultException("Este documento já recebeu uma manifestação conclusiva; não é possível manifestar novamente.");

        if (type == NfeManifestationType.Confirmation && row.Situation == DfeInvoiceSituation.Cancelled)
            throw new DefaultException("A NF-e está cancelada e não aceita Confirmação da Operação.");
    }

    public async Task<ReceivedDfe> LoadAsync(int id, CancellationToken ct) =>
        await db.Context.ReceivedDfes.FirstOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new DefaultException("Documento recebido não encontrado.");

    private async Task<string> TaxIdAsync(string branchCode, CancellationToken ct)
    {
        var branch = await db.Context.Branchs.AsNoTracking().FirstOrDefaultAsync(b => b.Code == branchCode, ct)
                     ?? throw new DefaultException($"Filial {branchCode} não encontrada.");

        var taxId = NfeText.AlphaNumeric(branch.TaxId);

        return taxId.Length == 0 ? throw new DefaultException($"A filial {branchCode} está sem CNPJ.") : taxId;
    }

    private static readonly TimeSpan Wait = TimeSpan.FromHours(1);

    private static string WaitMessage(DateTimeOffset allowed) =>
        $"Aguarde até {TimeZoneInfo.ConvertTime(allowed, NfeIssueInputAssembler.BrasiliaZone).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)} para consultar novamente.";

    private static DfeManifestationStatus StatusOf(NfeManifestationType type) => type switch
    {
        NfeManifestationType.Acknowledgement => DfeManifestationStatus.Acknowledged,
        NfeManifestationType.Confirmation => DfeManifestationStatus.Confirmed,
        NfeManifestationType.Unknown => DfeManifestationStatus.Unknown,
        _ => DfeManifestationStatus.NotPerformed,
    };
}
