using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Companies;

/// <summary>Logo da empresa para a home sem painel. Os relatórios continuam usando CompanyLogoPath.</summary>
public class CompanyLogoService(CommonDbContext db)
{
    public const int MaxBytes = 1024 * 1024;
    private static readonly string[] AllowedTypes = ["image/png", "image/jpeg"];

    public async Task<(byte[] Content, string ContentType)?> GetAsync(string code)
    {
        var logo = await db.CompanyLogos.AsNoTracking().FirstOrDefaultAsync(l => l.CompanyCode == code);
        return logo is null ? null : (logo.Content, logo.ContentType);
    }

    public Task<bool> ExistsAsync(string code) => db.CompanyLogos.AnyAsync(l => l.CompanyCode == code);

    public async Task SetAsync(string code, string? contentType, string? base64)
    {
        if (!AllowedTypes.Contains(contentType ?? string.Empty))
            throw new DefaultException("O logo deve ser uma imagem PNG ou JPG.");

        byte[] content;
        try
        {
            content = Convert.FromBase64String(base64 ?? string.Empty);
        }
        catch (FormatException)
        {
            throw new DefaultException("Arquivo de imagem inválido.");
        }

        if (content.Length == 0) throw new DefaultException("Arquivo de imagem inválido.");
        if (content.Length > MaxBytes) throw new DefaultException("O logo deve ter no máximo 1 MB.");

        if (!await db.Companies.AnyAsync(c => c.Code == code))
            throw new NotFoundException("Empresa não encontrada.");

        var existing = await db.CompanyLogos.FirstOrDefaultAsync(l => l.CompanyCode == code);
        if (existing is null)
        {
            db.CompanyLogos.Add(new CompanyLogo { CompanyCode = code, Content = content, ContentType = contentType! });
        }
        else
        {
            existing.Content = content;
            existing.ContentType = contentType!;
        }

        await db.SaveChangesAsync();
    }

    public async Task RemoveAsync(string code)
    {
        var existing = await db.CompanyLogos.FirstOrDefaultAsync(l => l.CompanyCode == code);
        if (existing is null) return;

        db.CompanyLogos.Remove(existing);
        await db.SaveChangesAsync();
    }
}
