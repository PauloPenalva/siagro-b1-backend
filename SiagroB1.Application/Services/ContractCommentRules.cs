using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services;

/// <summary>
/// Regras comuns aos comentários do contrato de compra, do contrato de venda e do documento de
/// saída. Fica num arquivo único (e não duplicada em cada lado) porque é literalmente a mesma
/// decisão de negócio: quem pode alterar e que texto é aceito.
///
/// Note o que NÃO está aqui: guarda de status. Comentário é anotação e não altera valor, volume nem
/// saldo — é editável a qualquer tempo, inclusive em contrato encerrado ou cancelado e em documento
/// de saída confirmado ou cancelado.
/// </summary>
public static class ContractCommentRules
{
    /// <summary>
    /// Igual a <c>VARCHAR(500)</c> das colunas <c>OldValue</c>/<c>NewValue</c> dos logs de
    /// alterações: nenhum comentário sai truncado no log.
    /// </summary>
    public const int MaxTextLength = 500;

    /// <summary>
    /// Valida e normaliza o texto recebido da tela. Recusa em vez de truncar: cortar em silêncio
    /// esconderia parte do que o usuário escreveu.
    /// </summary>
    public static string NormalizeText(string? text)
    {
        var normalized = text?.Trim();

        if (string.IsNullOrEmpty(normalized))
            throw new DefaultException("Informe o texto do comentário.");

        if (normalized.Length > MaxTextLength)
            throw new DefaultException(
                $"O comentário deve ter no máximo {MaxTextLength} caracteres.");

        return normalized;
    }

    /// <summary>
    /// Só o autor altera ou exclui o próprio comentário; administrador pode qualquer um. A
    /// comparação ignora caixa porque o nome de usuário chega da autenticação como o usuário
    /// digitou no login.
    /// </summary>
    public static void EnsureCanModify(string? author, string userName, bool isAdmin)
    {
        if (isAdmin)
            return;

        if (!string.IsNullOrEmpty(author)
            && string.Equals(author, userName, StringComparison.OrdinalIgnoreCase))
            return;

        throw new DefaultException("Somente o autor do comentário pode alterá-lo.");
    }
}
