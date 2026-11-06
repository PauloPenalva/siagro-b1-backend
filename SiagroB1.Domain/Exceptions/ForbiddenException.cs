namespace SiagroB1.Domain.Exceptions;

/// <summary>Usuário autenticado sem a permissão exigida pela operação. A API responde 403 com a mensagem.</summary>
public class ForbiddenException(string message) : Exception(message);
