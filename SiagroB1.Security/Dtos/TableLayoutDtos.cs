namespace SiagroB1.Security.Dtos;

/// <summary>Uma coluna dentro do layout. A posição é a do array, não um campo.</summary>
public class TableColumnLayoutDto
{
    public string Key { get; set; } = string.Empty;

    /// <summary>Nulo quando a coluna está na largura padrão declarada no XML.</summary>
    public string? Width { get; set; }
}

/// <summary>Layout de uma tabela, como o frontend o recebe no boot.</summary>
public class UserTableLayoutDto
{
    public string TableKey { get; set; } = string.Empty;

    public int Version { get; set; }

    public List<TableColumnLayoutDto> Columns { get; set; } = [];

    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Resposta do GET. Envelopada num objeto (e não um array cru) para caber campo novo depois sem
/// quebrar o cliente; e lista, não dicionário, porque as chaves de coluna contêm <c>/</c>, <c>::</c>
/// e <c>#</c>.
/// </summary>
public class UserTableLayoutsResponse
{
    public List<UserTableLayoutDto> Layouts { get; set; } = [];
}

/// <summary>
/// Corpo do PUT. A chave da tabela vem no corpo, e não na URL, justamente para não precisar escapar
/// <c>::</c>, <c>--</c> e <c>/</c> num segmento de rota.
/// </summary>
public class SaveTableLayoutRequest
{
    public string? TableKey { get; set; }

    public List<TableColumnLayoutDto>? Columns { get; set; }
}

/// <summary>O que o layout guardado contém de fato na coluna <c>LayoutJson</c>.</summary>
public class TableLayoutDocument
{
    public int Version { get; set; } = 1;

    public List<TableColumnLayoutDto> Columns { get; set; } = [];
}
