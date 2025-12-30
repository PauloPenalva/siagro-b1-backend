using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Dtos;
using SiagroB1.Security.Interfaces;
using SiagroB1.Security.Shared;

namespace SiagroB1.Security.Services;

public class AuthService(
    CommonDbContext db,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AuthService> logger) : IAuthService
{
    
    public async Task<LoginResponse> LoginAsync(string username, string password)
        {
            try
            {
                // Validar usuário
                var user = await db.Users
                    .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

                if (user == null)
                {
                    return new LoginResponse()
                    {
                        Success = false,
                        Message = "Usuário ou Senha inválidos"
                    };
                }

                // Validar senha (use BCrypt em produção!)
                var hashedPassword = Utils.HashPassword(password);
                if (user.PasswordHash != hashedPassword)
                {
                    return new LoginResponse()
                    {
                        Success = false,
                        Message = "Usuário ou Senha inválidos"
                    };
                }

                // Criar sessão
                var sessionId = Guid.NewGuid().ToString();
                var session = new UserSession
                {
                    SessionId = sessionId,
                    UserId = user.Id,
                    IpAddress = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = httpContextAccessor.HttpContext?.Request.Headers["User-Agent"].ToString(),
                    CreatedAt = DateTime.Now,
                    LastActivityAt = DateTime.Now,
                    ExpiresAt = DateTime.Now.AddHours(8),
                    IsActive = true
                };

                db.UserSessions.Add(session);
                
                // Atualizar último login
                user.LastLoginAt = DateTime.Now;
                db.Users.Update(user);
                
                await db.SaveChangesAsync();

                // Criar cookies (se tiver contexto HTTP)
                var httpContext = httpContextAccessor.HttpContext;
                if (httpContext != null)
                {
                    // Cookie de sessão
                    httpContext.Response.Cookies.Append("SIAGROB1.Session", sessionId, new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = httpContext.Request.IsHttps,
                        SameSite = SameSiteMode.Strict,
                        Expires = DateTime.Now.AddHours(8),
                        Path = "/"
                    });

                    // Cookie com informações do usuário
                    var userInfo = new
                    {
                        Username = user.Username,
                        FullName = user.FullName,
                        IsAdmin = user.IsAdmin
                    };
                    
                    var encodedUserInfo = Uri.EscapeDataString(
                        System.Text.Json.JsonSerializer.Serialize(userInfo));
                    
                    httpContext.Response.Cookies.Append("SIAGROB1.User", encodedUserInfo, new CookieOptions
                    {
                        HttpOnly = false,
                        Secure = httpContext.Request.IsHttps,
                        SameSite = SameSiteMode.Strict,
                        Expires = DateTime.Now.AddHours(8),
                        Path = "/"
                    });
                }

                // Criar claims para o usuário
                var claims = CreateClaims(user);

                return new LoginResponse()
                {
                    Success = true,
                    Message = "Login realizado com sucesso",
                    SessionId = sessionId,
                    User = new UserInfo
                    {
                        Id = user.Id.ToString(),
                        Username = user.Username,
                        FullName = user.FullName,
                        Email = user.Email,
                        IsAdmin = user.IsAdmin,
                        LastLogin = user.LastLoginAt
                    },
                    Claims = claims,
                    ExpiresAt = session.ExpiresAt
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro durante login para {Username}", username);
                return new LoginResponse()
                {
                    Success = false,
                    Message = "Erro interno no servidor"
                };
            }
        }

        public async Task<LoginResponse> LoginWithBasicAuthAsync(string authorizationHeader)
        {
            if (string.IsNullOrEmpty(authorizationHeader) || 
                !authorizationHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                return new LoginResponse()
                {
                    Success = false,
                    Message = "Cabeçalho de autorização inválido"
                };
            }

            try
            {
                // Decodificar Basic Auth
                var encodedCredentials = authorizationHeader.Substring("Basic ".Length).Trim();
                var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCredentials));
                var separatorIndex = credentials.IndexOf(':');
                
                if (separatorIndex == -1)
                {
                    return new LoginResponse()
                    {
                        Success = false,
                        Message = "Formato Basic Auth inválido"
                    };
                }

                var username = credentials.Substring(0, separatorIndex);
                var password = credentials.Substring(separatorIndex + 1);

                return await LoginAsync(username, password);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro no Basic Auth");
                return new LoginResponse()
                {
                    Success = false,
                    Message = "Erro ao processar autenticação"
                };
            }
        }

        public async Task<bool> LogoutAsync(string sessionId)
        {
            try
            {
                var session = await db.UserSessions
                    .FirstOrDefaultAsync(s => s.SessionId == sessionId);

                if (session != null)
                {
                    session.IsActive = false;
                    session.ExpiresAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                    
                    logger.LogInformation("Sessão invalidada: {SessionId}", sessionId);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao fazer logout para sessão {SessionId}", sessionId);
                return false;
            }
        }

        public async Task<UserInfo?> GetUserInfoAsync(string username)
        {
            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

            if (user == null) return null;

            return new UserInfo
            {
                Id = user.Id.ToString(),
                Username = user.Username,
                FullName = user.FullName,
                Email = user.Email,
                IsAdmin = user.IsAdmin,
                LastLogin = user.LastLoginAt
            };
        }

        private List<Claim> CreateClaims(User user)
        {
            return new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.GivenName, user.FullName),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                new Claim("IsAdmin", user.IsAdmin.ToString())
            };
        }

}