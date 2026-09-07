using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Dtos;

namespace SiagroB1.Security.Services;

/// <summary>
/// Layout das tabelas (largura e ordem das colunas) que o próprio usuário ajustou na UI — GAC-1163.
///
/// Como todo serviço de perfil, é escopado ao usuário da sessão: nenhum método recebe o alvo por
/// parâmetro vindo da requisição.
/// </summary>
public partial class UserTableLayoutService(
    CommonDbContext db,
    ILogger<UserTableLayoutService> logger)
{
    /// <summary>Versão do documento gravado. Bumpar invalida o que estiver guardado.</summary>
    public const int CurrentVersion = 1;

    public const int MaxTableKeyLength = 200;
    public const int MaxColumnKeyLength = 120;

    /// <summary>A maior tabela do app tem 20 colunas; 60 é folga sem abrir a porta.</summary>
    public const int MaxColumns = 60;

    /// <summary>20 colunas dão ~900 caracteres. O teto é só uma trava contra bug do cliente.</summary>
    public const int MaxLayoutJsonLength = 8000;

    /// <summary>
    /// Teto de linhas por usuário. Existem 105 tabelas hoje; o limite impede que uma chave
    /// defeituosa vinda do cliente encha a tabela e o payload do boot.
    /// </summary>
    public const int MaxLayoutsPerUser = 300;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [GeneratedRegex(@"^[A-Za-z0-9_.:#/\-]+$")]
    private static partial Regex TableKeyPattern();

    /// <summary>
    /// A largura vai direto para o atributo <c>style</c> do cabeçalho da tabela — esta é a única
    /// superfície de injeção do recurso, e por isso a validação é no servidor, não só no cliente.
    /// Nada de <c>auto</c>, <c>calc()</c> ou <c>inherit</c>.
    /// </summary>
    [GeneratedRegex(@"^\d{1,5}(\.\d{1,3})?(px|rem|em|%)$")]
    private static partial Regex WidthPattern();

    public async Task<List<UserTableLayoutDto>> GetAllAsync(
        string username, CancellationToken ct = default)
    {
        var rows = await db.UserTableLayouts
            .AsNoTracking()
            .Where(x => x.Username == username)
            .ToListAsync(ct);

        var layouts = new List<UserTableLayoutDto>(rows.Count);

        foreach (var row in rows)
        {
            var document = Deserialize(row);

            // Documento ilegível ou de versão antiga não vira erro: o usuário simplesmente vê a
            // tela no padrão, e a próxima interação dele regrava a linha.
            if (document is null || document.Version != CurrentVersion)
            {
                continue;
            }

            layouts.Add(new UserTableLayoutDto
            {
                TableKey = row.TableKey,
                Version = document.Version,
                Columns = document.Columns,
                UpdatedAt = row.UpdatedAt
            });
        }

        return layouts;
    }

    public async Task<OperationResult> SaveAsync(
        string username, SaveTableLayoutRequest? request, CancellationToken ct = default)
    {
        if (!TryBuildDocument(request, out var tableKey, out var document, out var error))
        {
            return OperationResult.Fail(error);
        }

        var layoutJson = JsonSerializer.Serialize(document, JsonOptions);

        if (layoutJson.Length > MaxLayoutJsonLength)
        {
            return OperationResult.Fail("Layout da tabela grande demais.");
        }

        var existing = await db.UserTableLayouts
            .FirstOrDefaultAsync(x => x.Username == username && x.TableKey == tableKey, ct);

        if (existing is null)
        {
            // O teto só barra CHAVE NOVA: quem já tem a linha continua conseguindo atualizá-la,
            // senão o usuário no limite ficaria com telas travadas sem entender por quê.
            var total = await db.UserTableLayouts.CountAsync(x => x.Username == username, ct);

            if (total >= MaxLayoutsPerUser)
            {
                logger.LogWarning(
                    "Usuário {Username} atingiu o limite de {Max} layouts de tabela.",
                    username, MaxLayoutsPerUser);

                return OperationResult.Fail("Limite de tabelas personalizadas atingido.");
            }

            db.UserTableLayouts.Add(new UserTableLayout
            {
                Username = username,
                TableKey = tableKey,
                LayoutJson = layoutJson
            });
        }
        else
        {
            existing.LayoutJson = layoutJson;
            existing.UpdatedAt = DateTime.Now;
        }

        await db.SaveChangesAsync(ct);

        return OperationResult.Ok("Layout salvo.");
    }

    public async Task<(OperationResult Result, int Removed)> ClearAllAsync(
        string username, CancellationToken ct = default)
    {
        var removed = await db.UserTableLayouts
            .Where(x => x.Username == username)
            .ExecuteDeleteAsync(ct);

        logger.LogInformation(
            "Layout das tabelas restaurado pelo próprio usuário: {Username} ({Removed} linhas).",
            username, removed);

        return (OperationResult.Ok("Layout das tabelas restaurado."), removed);
    }

    private static TableLayoutDocument? Deserialize(UserTableLayout row)
    {
        try
        {
            return JsonSerializer.Deserialize<TableLayoutDocument>(row.LayoutJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryBuildDocument(
        SaveTableLayoutRequest? request,
        out string tableKey,
        out TableLayoutDocument document,
        out string error)
    {
        tableKey = string.Empty;
        document = new TableLayoutDocument();
        error = string.Empty;

        var key = request?.TableKey?.Trim();

        if (string.IsNullOrWhiteSpace(key) ||
            key.Length > MaxTableKeyLength ||
            !TableKeyPattern().IsMatch(key))
        {
            error = "Identificação da tabela inválida.";
            return false;
        }

        var columns = request?.Columns;

        if (columns is null || columns.Count == 0 || columns.Count > MaxColumns)
        {
            error = "Lista de colunas inválida.";
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var column in columns)
        {
            var columnKey = column.Key?.Trim();

            if (string.IsNullOrWhiteSpace(columnKey) || columnKey.Length > MaxColumnKeyLength)
            {
                error = "Identificação de coluna inválida.";
                return false;
            }

            // Chave repetida quebraria a remontagem da ordem no cliente.
            if (!seen.Add(columnKey))
            {
                error = "Coluna repetida no layout.";
                return false;
            }

            var width = string.IsNullOrWhiteSpace(column.Width) ? null : column.Width.Trim();

            if (width is not null && !WidthPattern().IsMatch(width))
            {
                error = "Largura de coluna inválida.";
                return false;
            }

            document.Columns.Add(new TableColumnLayoutDto { Key = columnKey, Width = width });
        }

        tableKey = key;
        document.Version = CurrentVersion;

        return true;
    }
}
