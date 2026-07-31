namespace SiagroB1.Domain.Interfaces;

/// <summary>
/// Traz um usuário do cadastro do SAP B1 (OUSR) para o SiagroB1 sob demanda, no login e no pedido
/// de recuperação de senha.
///
/// Existe para fechar a janela entre "usuário foi criado no SAP" e "a varredura periódica rodou":
/// sem isso, quem acabou de ser cadastrado no SAP não conseguiria nem pedir a primeira senha.
///
/// A implementação em modo STANDALONE é no-op, então quem chama nunca precisa saber em que modo
/// o sistema está rodando.
/// </summary>
public interface ISapUserProvisioner
{
    Task EnsureAsync(string usernameOrEmail, CancellationToken ct = default);
}
