using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Fiscal.Certificates;

/// <summary>
/// Abre o .pfx a partir dos bytes (nunca do repositório do Windows). No Windows a chave vai para
/// um contêiner de máquina temporário: o SChannel não usa chave efêmera na autenticação TLS de
/// cliente que a SEFAZ exige. Nos demais sistemas, chave efêmera.
/// </summary>
public static class CertificateLoader
{
    public static X509Certificate2 Load(byte[] pfx, string password)
    {
        var flags = OperatingSystem.IsWindows()
            ? X509KeyStorageFlags.MachineKeySet
            : X509KeyStorageFlags.EphemeralKeySet;

        try
        {
            return X509CertificateLoader.LoadPkcs12(pfx, password, flags);
        }
        catch (CryptographicException e)
        {
            // Mantém a causa: falha de contêiner de chave no servidor também cai aqui.
            throw new DefaultException($"O arquivo não é um certificado A1 (.pfx) válido ou a senha está errada (detalhe técnico: {e.Message}).");
        }
    }
}
