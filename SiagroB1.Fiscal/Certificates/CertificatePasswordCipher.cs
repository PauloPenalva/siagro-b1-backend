using System.Security.Cryptography;
using System.Text;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Fiscal.Certificates;

/// <summary>
/// AES-GCM da senha do certificado A1. Formato gravado: nonce (12) + tag (16) + texto cifrado.
/// A chave (32 bytes, base64) fica só em <c>Nfe:CertificateKey</c> do appsettings do servidor:
/// quem lê o banco não abre o .pfx sozinho.
/// </summary>
public sealed class CertificatePasswordCipher
{
    public const string ConfigurationKey = "Nfe:CertificateKey";

    public const string MissingKeyMessage =
        "Configure no servidor a chave Nfe:CertificateKey (base64 de 32 bytes) antes de enviar o certificado ou emitir NF-e.";

    private const string DecryptFailedMessage =
        "Não foi possível abrir a senha do certificado: a chave Nfe:CertificateKey do servidor mudou ou o dado gravado está corrompido. Envie o certificado de novo.";

    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public CertificatePasswordCipher(byte[] key)
    {
        if (key.Length != 32)
            throw new DefaultException(MissingKeyMessage);

        _key = key;
    }

    public static CertificatePasswordCipher FromBase64(string? keyBase64)
    {
        if (string.IsNullOrWhiteSpace(keyBase64))
            throw new DefaultException(MissingKeyMessage);

        try
        {
            return new CertificatePasswordCipher(Convert.FromBase64String(keyBase64));
        }
        catch (FormatException)
        {
            throw new DefaultException(MissingKeyMessage);
        }
    }

    public byte[] Encrypt(string password)
    {
        var plain = Encoding.UTF8.GetBytes(password);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        return [.. nonce, .. tag, .. cipher];
    }

    public string Decrypt(byte[] payload)
    {
        if (payload.Length < NonceSize + TagSize)
            throw new DefaultException(DecryptFailedMessage);

        var nonce = payload[..NonceSize];
        var tag = payload[NonceSize..(NonceSize + TagSize)];
        var cipher = payload[(NonceSize + TagSize)..];
        var plain = new byte[cipher.Length];

        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);
        }
        catch (CryptographicException e)
        {
            throw new DefaultException($"{DecryptFailedMessage} (detalhe técnico: {e.Message})");
        }

        return Encoding.UTF8.GetString(plain);
    }
}
