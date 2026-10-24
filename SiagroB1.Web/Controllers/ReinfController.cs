using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Reinf;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Reinf;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Web.Controllers;

/// <summary>EFD-Reinf (R-1000/R-2055/R-2098/R-2099): ações e funções de topo, por raiz de CNPJ. Só STANDALONE.</summary>
public partial class ReinfController(
    IReinfService service, ReinfSettingsService settingsService, AppDbContext context, IConfiguration configuration) : ODataController
{
    private const string StandaloneOnly = "Disponível somente no modo STANDALONE.";
    private const string InvalidPeriod = "Período inválido (use AAAA-MM).";

    [GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])$")] private static partial Regex YearMonth();

    private string User_ => User.Identity?.Name ?? "Unknown";

    [HttpGet("odata/ReinfSettingsGet(taxIdRoot={taxIdRoot})")]
    public async Task<IActionResult> ReinfSettingsGet(string? taxIdRoot)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        try
        {
            var root = ReinfFormat.Root(taxIdRoot ?? string.Empty);
            var s = await settingsService.GetAsync(root, HttpContext.RequestAborted);
            return Ok(ToDto(s, exists: s.Key != Guid.Empty && s.UpdatedAt != default));
        }
        catch (BusinessException e) { return BadRequest(e.Message); }
    }

    [HttpPost("odata/ReinfSettingsSave")]
    public async Task<IActionResult> ReinfSettingsSave(ODataActionParameters? parameters)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        if (parameters is null) return BadRequest("Informe os parâmetros da EFD-Reinf.");
        try
        {
            var input = new ReinfSettings
            {
                TaxIdRoot = Text(parameters, "TaxIdRoot") ?? string.Empty,
                BranchCode = Text(parameters, "BranchCode") ?? string.Empty,
                Environment = (ReinfEnvironment)Int(parameters, "Environment"),
                TaxClassification = Text(parameters, "TaxClassification"),
                KeepsBookkeeping = Bool(parameters, "KeepsBookkeeping"),
                PayrollRelief = Bool(parameters, "PayrollRelief"),
                FineExemptionAgreement = Bool(parameters, "FineExemptionAgreement"),
                CompanySituation = parameters.TryGetValue("CompanySituation", out var cs) && cs is not null && int.TryParse(cs.ToString(), out var csv) ? csv : null,
                ContactName = Text(parameters, "ContactName") ?? string.Empty,
                ContactCpf = Text(parameters, "ContactCpf") ?? string.Empty,
                ContactPhone = Text(parameters, "ContactPhone"),
                ContactEmail = Text(parameters, "ContactEmail"),
                ValidFrom = Text(parameters, "ValidFrom"),
                ClosingResponsibleName = Text(parameters, "ClosingResponsibleName"),
                ClosingResponsibleCpf = Text(parameters, "ClosingResponsibleCpf"),
                ClosingResponsiblePhone = Text(parameters, "ClosingResponsiblePhone"),
                ClosingResponsibleEmail = Text(parameters, "ClosingResponsibleEmail"),
            };
            var saved = await settingsService.SaveAsync(input, User_, HttpContext.RequestAborted);
            return Ok(ToDto(saved, exists: true));
        }
        catch (BusinessException e) { return BadRequest(e.Message); }
    }

    [HttpGet("odata/ReinfContributors()")]
    public async Task<IActionResult> ReinfContributors()
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        var ct = HttpContext.RequestAborted;
        var branches = (await context.Branchs.AsNoTracking().Select(b => new { b.Code, b.BranchName, b.TaxId }).ToListAsync(ct))
            .Select(b => new { b.Code, b.BranchName, Digits = new string((b.TaxId ?? string.Empty).Where(char.IsDigit).ToArray()) })
            .Where(b => b.Digits.Length == 14)
            .ToList();
        var withSettings = (await context.ReinfSettings.AsNoTracking().Select(s => s.TaxIdRoot).ToListAsync(ct)).ToHashSet();

        var list = branches.GroupBy(b => b.Digits[..8]).OrderBy(g => g.Key).Select(g =>
        {
            var head = g.FirstOrDefault(b => b.Digits.Substring(8, 4) == "0001") ?? g.OrderBy(b => b.Code).First();
            return new ReinfContributorDto { TaxIdRoot = g.Key, Name = head.BranchName, HasSettings = withSettings.Contains(g.Key) };
        }).ToList();
        return Ok(list);
    }

    [HttpGet("odata/ReinfOverview(taxIdRoot={taxIdRoot},period={period})")]
    public async Task<IActionResult> ReinfOverview(string? taxIdRoot, string? period)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        if (!ValidPeriod(period)) return BadRequest(InvalidPeriod);
        try
        {
            var ct = HttpContext.RequestAborted;
            var overview = await service.GetOverviewAsync(taxIdRoot ?? string.Empty, period!, ct);
            var closing = await context.ReinfPeriods.AsNoTracking()
                .Where(p => p.TaxIdRoot == overview.TaxIdRoot && p.Period == overview.Period)
                .Select(p => p.ClosingReceipt).FirstOrDefaultAsync(ct);
            return Ok(new ReinfOverviewDto
            {
                TaxIdRoot = overview.TaxIdRoot,
                Period = overview.Period,
                PeriodStatus = overview.PeriodStatus.ToString(),
                ClosingReceipt = closing,
                Rows = overview.Rows.Select(r => new ReinfOverviewRowDto
                {
                    EstablishmentTaxId = r.Row?.EstablishmentTaxId ?? string.Empty,
                    BranchCode = r.Row?.BranchCode ?? string.Empty,
                    CardCode = r.Row?.CardCode ?? string.Empty,
                    CardName = r.Row?.CardName ?? string.Empty,
                    ProducerTaxId = r.Row?.ProducerTaxId ?? string.Empty,
                    PayrollOption = r.Row?.PayrollOption ?? false,
                    Gross = r.Row?.Gross ?? 0m,
                    Inss = r.Row?.Inss ?? 0m,
                    Rat = r.Row?.Rat ?? 0m,
                    Senar = r.Row?.Senar ?? 0m,
                    Situation = r.Situation,
                    Receipt = r.Receipt,
                    Occurrences = r.Occurrences,
                    EventKey = r.EventKey,
                }).ToList(),
                Issues = overview.Issues.Select(i => new ReinfIssueDto { Message = i.Message, Reference = i.Reference }).ToList(),
            });
        }
        catch (BusinessException e) { return BadRequest(e.Message); }
    }

    [HttpGet("odata/ReinfEvents(taxIdRoot={taxIdRoot},period={period})")]
    public async Task<IActionResult> ReinfEvents(string? taxIdRoot, string? period)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        if (!string.IsNullOrWhiteSpace(period) && !ValidPeriod(period)) return BadRequest(InvalidPeriod);
        try
        {
            var root = ReinfFormat.Root(taxIdRoot ?? string.Empty);
            var hasPeriod = !string.IsNullOrWhiteSpace(period);
            // Sem período: eventos do cadastro (R-1000), que não têm período.
            var events = await context.ReinfEvents.AsNoTracking()
                .Where(e => e.TaxIdRoot == root && (hasPeriod ? e.Period == period : e.Period == null))
                .OrderByDescending(e => e.CreatedAt).ToListAsync(HttpContext.RequestAborted);
            var keys = events.Where(e => e.BatchKey != null).Select(e => e.BatchKey!.Value).Distinct().ToList();
            var batches = await context.ReinfBatches.AsNoTracking().Where(b => keys.Contains(b.Key))
                .Select(b => new { b.Key, b.Protocol, b.Status }).ToDictionaryAsync(b => b.Key, HttpContext.RequestAborted);
            return Ok(events.Select(e =>
            {
                batches.TryGetValue(e.BatchKey ?? Guid.Empty, out var batch);
                return new ReinfEventDto
                {
                    Key = e.Key, Type = e.Type.ToString(), Status = e.Status.ToString(), EventId = e.EventId, Period = e.Period,
                    Receipt = e.Receipt, Occurrences = e.Occurrences, CreatedAt = e.CreatedAt, BatchKey = e.BatchKey,
                    BatchProtocol = batch?.Protocol, BatchStatus = batch?.Status.ToString(), Xml = e.Xml,
                };
            }).ToList());
        }
        catch (BusinessException e) { return BadRequest(e.Message); }
    }

    [HttpGet("odata/ReinfBatchXml(batchKey={batchKey})")]
    public async Task<IActionResult> ReinfBatchXml(Guid batchKey)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        var batch = await context.ReinfBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Key == batchKey, HttpContext.RequestAborted);
        if (batch is null) return BadRequest("Lote da EFD-Reinf não encontrado.");
        return Ok(new ReinfBatchXmlDto { RequestXml = batch.RequestXml, ResponseXml = batch.ResponseXml, Error = batch.Error });
    }

    [HttpPost("odata/ReinfTransmit")]
    public Task<IActionResult> ReinfTransmit(ODataActionParameters? parameters) =>
        PeriodAction(parameters, async (root, period, ct) => Ok((await service.TransmitAsync(root, period, User_, ct)).ToList()));

    [HttpPost("odata/ReinfClose")]
    public Task<IActionResult> ReinfClose(ODataActionParameters? parameters) =>
        PeriodAction(parameters, async (root, period, ct) => Ok(await service.CloseAsync(root, period, User_, ct)));

    [HttpPost("odata/ReinfReopen")]
    public Task<IActionResult> ReinfReopen(ODataActionParameters? parameters) =>
        PeriodAction(parameters, async (root, period, ct) => Ok(await service.ReopenAsync(root, period, User_, ct)));

    [HttpPost("odata/ReinfReleaseUnconfirmed")]
    public Task<IActionResult> ReinfReleaseUnconfirmed(ODataActionParameters? parameters) =>
        PeriodAction(parameters, async (root, period, ct) => Ok(await service.ReleaseUnconfirmedAsync(root, period, ct)));

    [HttpPost("odata/ReinfRegistrationSend")]
    public async Task<IActionResult> ReinfRegistrationSend(ODataActionParameters? parameters)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        if (parameters is null) return BadRequest("Informe a raiz do CNPJ.");
        try { return Ok(await service.SendRegistrationAsync(Text(parameters, "TaxIdRoot") ?? string.Empty, User_, HttpContext.RequestAborted)); }
        catch (BusinessException e) { return BadRequest(e.Message); }
    }

    [HttpPost("odata/ReinfBatchQuery")]
    public async Task<IActionResult> ReinfBatchQuery(ODataActionParameters? parameters)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        if (parameters is null || !Guid.TryParse(Text(parameters, "BatchKey"), out var key)) return BadRequest("Informe o lote.");
        try
        {
            await service.QueryBatchAsync(key, HttpContext.RequestAborted);
            return NoContent();
        }
        catch (BusinessException e) { return BadRequest(e.Message); }
    }

    private async Task<IActionResult> PeriodAction(
        ODataActionParameters? parameters, Func<string, string, CancellationToken, Task<IActionResult>> body)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        if (parameters is null) return BadRequest("Informe a raiz do CNPJ e o período.");
        var period = Text(parameters, "Period");
        if (!ValidPeriod(period)) return BadRequest(InvalidPeriod);
        try { return await body(Text(parameters, "TaxIdRoot") ?? string.Empty, period!, HttpContext.RequestAborted); }
        catch (BusinessException e) { return BadRequest(e.Message); }
    }

    private static bool ValidPeriod(string? period) => period is not null && YearMonth().IsMatch(period);

    private static ReinfSettingsDto ToDto(ReinfSettings s, bool exists) => new()
    {
        TaxIdRoot = s.TaxIdRoot, BranchCode = s.BranchCode, Environment = (int)s.Environment, TaxClassification = s.TaxClassification,
        KeepsBookkeeping = s.KeepsBookkeeping, PayrollRelief = s.PayrollRelief, FineExemptionAgreement = s.FineExemptionAgreement,
        CompanySituation = s.CompanySituation, ContactName = s.ContactName, ContactCpf = s.ContactCpf, ContactPhone = s.ContactPhone,
        ContactEmail = s.ContactEmail, ValidFrom = s.ValidFrom, ClosingResponsibleName = s.ClosingResponsibleName,
        ClosingResponsibleCpf = s.ClosingResponsibleCpf, ClosingResponsiblePhone = s.ClosingResponsiblePhone,
        ClosingResponsibleEmail = s.ClosingResponsibleEmail, RegistrationStatus = (int)s.RegistrationStatus,
        RegistrationReceipt = s.RegistrationReceipt, RegistrationUpdatePending = s.RegistrationUpdatePending, Exists = exists,
    };

    private static string? Text(ODataActionParameters p, string name) =>
        p.TryGetValue(name, out var v) ? v?.ToString()?.Trim() : null;

    private static int Int(ODataActionParameters p, string name) => int.TryParse(Text(p, name), out var v) ? v : 0;

    private static bool Bool(ODataActionParameters p, string name) => bool.TryParse(Text(p, name), out var v) && v;
}
