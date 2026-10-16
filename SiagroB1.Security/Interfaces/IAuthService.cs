using SiagroB1.Security.Dtos;

namespace SiagroB1.Security.Interfaces;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(string username, string password, string? companyCode);
    Task<LoginResponse> LoginWithBasicAuthAsync(string authorizationHeader, string? companyCode);
    Task<bool> LogoutAsync(string sessionId);
    Task<UserInfo?> GetUserInfoAsync(string username);
    Task<IReadOnlyList<CompanyOption>> ListCompaniesAsync();
}