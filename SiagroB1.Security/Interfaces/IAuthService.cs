using SiagroB1.Security.Dtos;

namespace SiagroB1.Security.Interfaces;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(string username, string password);
    Task<LoginResponse> LoginWithBasicAuthAsync(string authorizationHeader);
    Task<bool> LogoutAsync(string sessionId);
    Task<UserInfo?> GetUserInfoAsync(string username);
}