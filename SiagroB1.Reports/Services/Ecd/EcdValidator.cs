using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.Sped;

namespace SiagroB1.Reports.Services.Ecd;

/// <summary>
/// Inconsistências que impedem a geração da ECD (o PVA recusaria o arquivo) e avisos.
/// Estático de propósito: o Scrutor só registra classes terminadas em "Service".
/// </summary>
public static class EcdValidator
{
    /// <summary>Tabela de qualificação do assinante da ECD (COD_ASSIN → IDENT_QUALIF do J930).</summary>
    public static readonly IReadOnlyDictionary<string, string> SignerQualifications = new Dictionary<string, string>
    {
        ["203"] = "Diretor",
        ["204"] = "Conselheiro de Administração",
        ["205"] = "Administrador",
        ["206"] = "Administrador do Grupo",
        ["207"] = "Administrador de Sociedade Filiada",
        ["220"] = "Administrador Judicial – Pessoa Física",
        ["222"] = "Administrador Judicial – Pessoa Jurídica – Profissional Responsável",
        ["223"] = "Administrador Judicial/Gestor",
        ["226"] = "Gestor Judicial",
        ["309"] = "Procurador",
        ["312"] = "Inventariante",
        ["313"] = "Liquidante",
        ["315"] = "Interventor",
        ["401"] = "Titular – Pessoa Física – EIRELI",
        ["801"] = "Empresário",
        ["900"] = "Contador",
        ["999"] = "Outros",
    };

    public static List<EcdIssueDto> Validate(EcdData data, EcdRequest request)
    {
        var issues = new List<EcdIssueDto>();
        void Block(string message, string reference = "") =>
            issues.Add(new EcdIssueDto { Blocking = true, Message = message, Reference = reference });

        if (request.Year < EcdFileService.FirstSupportedYear)
            Block($"ECD disponível a partir do ano-calendário {EcdFileService.FirstSupportedYear} (leiaute 9).");
        if (request.Purpose == 1 && string.IsNullOrWhiteSpace(request.ReplacedBookHash))
            Block("Escrituração substituta: informe o hash do livro substituído.");

        ValidateLedger(data, request.Year, "ECD", issues, bookSettings: true);
        return issues.DistinctBy(i => (i.Blocking, i.Message)).ToList();
    }

    /// <summary>
    /// Regras da escrituração contábil do ano, comuns à ECD e à ECF (blocos I/J da ECD = J/K da ECF): contabilidade,
    /// declarante, signatários, meses fechados, lançamentos, plano referencial, visões e resultado apurado.
    /// <paramref name="book"/> nomeia a escrituração nas mensagens; <paramref name="bookSettings"/> liga as regras
    /// dos dados do livro (NIRE, atos constitutivos, CRC, qualificação), que só a ECD usa.
    /// </summary>
    public static void ValidateLedger(EcdData data, int year, string book, List<EcdIssueDto> issues, bool bookSettings)
    {
        void Block(string message, string reference = "") =>
            issues.Add(new EcdIssueDto { Blocking = true, Message = message, Reference = reference });
        void Warn(string message, string reference = "") =>
            issues.Add(new EcdIssueDto { Blocking = false, Message = message, Reference = reference });

        var start = new DateTime(year, 1, 1);
        if (data.Accounting.CutoverDate is not { } cutover)
            Block("Contabilidade desligada: informe a data de corte nos parâmetros da contabilidade.");
        else if (cutover.Date > start)
            Block($"Data de corte da contabilidade ({cutover:dd/MM/yyyy}) depois do início de {year}: a {book} precisa do ano inteiro.");

        var declarant = data.Declarant;
        if (declarant == null)
        {
            var root = data.Branches.Select(b => CompanyBranches.Digits(b.TaxId)).FirstOrDefault(c => c.Length >= 8)?[..8] ?? "";
            Block($"Filiais do CNPJ {root} sem matriz (/0001) cadastrada.", root);
        }
        else
        {
            var code = declarant.Code ?? "";
            if (string.IsNullOrWhiteSpace(declarant.StateCode)) Block($"Filial {code} sem UF.", code);
            if (string.IsNullOrWhiteSpace(declarant.MunicipalityCode) || string.IsNullOrWhiteSpace(data.MunicipalityName))
                Block($"Filial {code} sem município.", code);

            if (data.Settings is not { } s)
                Block(bookSettings ? "Parâmetros da ECD não cadastrados para a filial."
                    : $"Parâmetros da ECD (responsável pela {book}) não cadastrados para a filial.", code);
            else if (!bookSettings)
            {
                foreach (var (value, field) in new[] { (s.SignerName, "nome"), (s.SignerCpf, "CPF"), (s.SignerEmail, "e-mail") })
                    if (string.IsNullOrWhiteSpace(value)) Block($"Responsável pela {book}: informe {field} nos Parâmetros da ECD.", code);
            }
            else
            {
                if (s.HasNire && string.IsNullOrWhiteSpace(s.Nire)) Block("Parâmetros da ECD: informe o NIRE.", code);
                if (s.ConstitutiveActsDate == null)
                    Block("Parâmetros da ECD: informe a data do arquivamento dos atos constitutivos.", code);
                foreach (var (value, field) in new[] { (s.SignerName, "nome"), (s.SignerCpf, "CPF"), (s.SignerEmail, "e-mail") })
                    if (string.IsNullOrWhiteSpace(value)) Block($"Parâmetros da ECD: informe {field} do responsável.", code);
                if (string.IsNullOrWhiteSpace(s.AccountantCrcSequence) || s.AccountantCrcValidUntil == null)
                    Block("Parâmetros da ECD: informe o número sequencial e a validade do CRC do contabilista.", code);
                if (!SignerQualifications.ContainsKey(s.SignerQualificationCode ?? ""))
                    Block($"Parâmetros da ECD: qualificação do assinante {s.SignerQualificationCode} fora da tabela da ECD.", code);
            }

            if (data.Accountant is not { } a) Block("Parâmetros do SPED Fiscal não cadastrados para a filial.", code);
            else
                foreach (var (value, field) in new[]
                         {
                             (a.AccountantName, "nome"), (a.AccountantCpf, "CPF"), (a.AccountantCrc, "CRC"), (a.AccountantEmail, "e-mail"),
                             (a.AccountantPhone, "telefone"),
                         })
                    if (string.IsNullOrWhiteSpace(value)) Block($"Parâmetros do SPED Fiscal: informe {field} do contabilista.", code);
        }

        if (data.Entries.Count == 0) Block($"Nenhum lançamento contábil em {year}.");

        for (var month = 1; month <= 12; month++)
        {
            var period = data.Periods.FirstOrDefault(p => p.Month == month);
            if (period is not { Status: AccountingPeriodStatus.Closed or AccountingPeriodStatus.Blocked })
                Block($"Mês {month:00}/{year} não fechado.", $"{month:00}/{year}");
        }

        foreach (var e in data.Entries)
        {
            var debit = e.Lines.Where(l => l.DebitAccountCode != null).Sum(l => l.Amount);
            var credit = e.Lines.Where(l => l.CreditAccountCode != null).Sum(l => l.Amount);
            if (debit != credit) Block($"Lançamento {e.DocEntry} desbalanceado.", e.DocEntry.ToString());
        }

        if (data.BalanceSheet == null) Block("Visão gerencial do Balanço Patrimonial não cadastrada.");
        if (data.IncomeStatement == null) Block("Visão gerencial da DRE não cadastrada.");
        // J100: o PVA confere o nível 1 do Ativo contra o do Passivo + PL — uma raiz de cada
        if (data.BalanceSheet != null && data.BalanceSheet.Lines.Count(l => l.ParentCode is null) != 2)
            Block("A visão do Balanço deve ter exatamente 2 linhas de primeiro nível (Ativo e Passivo + PL).");

        // Total/Fórmula vira linha "T" no J100/J150, e o PVA confere a soma das filhas: sem filhas não fecha
        foreach (var view in new[] { data.BalanceSheet, data.IncomeStatement }.OfType<ManagementView>())
            foreach (var line in view.Lines.Where(l => l.Kind != ManagementViewLineKind.Accounts)
                         .Where(l => view.Lines.All(c => c.ParentCode != l.Code))
                         .OrderBy(l => l.Order).ThenBy(l => l.Code, StringComparer.Ordinal))
                Block($"Linha {line.Code} da visão {view.Name} sem linhas filhas; ajuste a visão para a {book}.", line.Code);

        var postings = EcdLedger.Postings(data);
        var accounts = data.Accounts.ToDictionary(a => a.Code);
        var centers = data.CostCenters.Select(c => c.Code).ToHashSet();
        foreach (var cc in postings.Select(p => p.CostCenter).Concat(EcdLedger.Opening(data).Keys.Select(k => k.CostCenter))
                     .OfType<string>().Distinct().Where(c => !centers.Contains(c)).Order(StringComparer.Ordinal))
            Block($"Centro de custo {cc} não cadastrado.", cc);

        // Resultado não apurado em 31/12: o J100 não fecharia (o Balanço do Siagro mostraria uma linha calculada)
        var resultAccounts = data.Accounts.Where(a => a.Type is LedgerAccountType.Revenue or LedgerAccountType.Expense)
            .Select(a => a.Code).ToHashSet();
        if (EcdLedger.Balances(data, postings).Any(kv => resultAccounts.Contains(kv.Key) && kv.Value != 0))
            Block($"Resultado de {year} não apurado em 31/12; faça a apuração de dezembro.");

        var moved = postings.Select(p => p.Account).ToHashSet();
        foreach (var code in EcdLedger.UsedAccounts(data, postings))
        {
            if (!accounts.TryGetValue(code, out var account))
            {
                Block($"Conta {code} não cadastrada no plano de contas.", code);
                continue;
            }

            if (!account.AllowsPosting) Block($"Conta sintética {code} com lançamento.", code);
            // I050: o nível 1 é sintético e toda analítica tem COD_CTA_SUP
            else if (account.ParentCode == null || !accounts.ContainsKey(account.ParentCode))
                Block($"Conta {code} analítica sem conta sintética superior; o plano de contas da {book} exige grupo sintético no nível 1.", code);
            if (string.IsNullOrWhiteSpace(account.ReferentialCode)) Block($"Conta {code} sem código do plano referencial.", code);
            if (data.BalanceSheet != null && data.IncomeStatement != null
                && EcdLedger.LineOf(data.BalanceSheet, code) == null && EcdLedger.LineOf(data.IncomeStatement, code) == null)
                Block($"Conta {code} fora do Balanço e da DRE.", code);
            if (account.Inactive && moved.Contains(code)) Warn($"Conta {code} inativa com movimento em {year}.", code);

            // A conta e as superiores vão para o I050: todas precisam da natureza do grupo
            var seen = new HashSet<string>();
            for (var c = account; c != null && seen.Add(c.Code); c = c.ParentCode == null ? null : accounts.GetValueOrDefault(c.ParentCode))
                if (c.Type == null) Block($"Conta {c.Code} sem tipo (natureza do grupo).", c.Code);
        }

        // Conferências sobre os valores do J100/J150: só com o resto em ordem (precisam de visões e plano válidos)
        if (data.BalanceSheet != null && data.IncomeStatement != null && issues.All(i => !i.Blocking))
        {
            var br = new System.Globalization.CultureInfo("pt-BR");
            string Money(decimal v) => v.ToString("N2", br);
            var totals = EcdFileService.BalanceSheetTotals(data, postings);
            var dates = new[] { start.AddDays(-1), new DateTime(year, 12, 31) };
            for (var i = 0; i < 2; i++)
                if (totals[i].Assets != totals[i].Liabilities)
                    Block($"Balanço não fecha em {dates[i]:dd/MM/yyyy}: Ativo {Money(totals[i].Assets)} ≠ Passivo + PL {Money(totals[i].Liabilities)}.");

            foreach (var (view, line, total, children, _) in EcdFileService.TotalMismatches(data, postings))
                Block($"Linha {line} da visão {view}: total {Money(total)} difere da soma das linhas filhas {Money(children)}.", line);
        }
    }
}
