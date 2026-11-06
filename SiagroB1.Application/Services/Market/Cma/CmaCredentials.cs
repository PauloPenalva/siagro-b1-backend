namespace SiagroB1.Application.Services.Market.Cma;

/// <summary>Credenciais da CMA com a senha aberta. Uso interno: nunca serialize em resposta de API.</summary>
public sealed record CmaCredentials(string Host, string UserName, string Password);
