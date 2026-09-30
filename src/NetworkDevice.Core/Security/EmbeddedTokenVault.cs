using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NetworkDevice.Core.Security;

/// <summary>
/// Cofre criptográfico para armazenamento e extração protegida de credenciais embutidas de fábrica no binário.
/// Evita que tokens e segredos fiquem expostos em texto simples nos assemblies compilados.
/// </summary>
public static class EmbeddedTokenVault
{
    // Carga criptografada injetada durante o build pelo Build-Beta.ps1
    // Formato: %%BETA_ENCRYPTED_TOKEN_B64%%
    internal static string EncryptedPayloadBase64 = "%%BETA_ENCRYPTED_TOKEN_B64%%";
    internal static string BuildSalt = "%%BETA_BUILD_SALT%%";

    // Chave de desofuscação mestre do SPARC
    private static readonly byte[] MasterSeed = new byte[]
    {
        0x53, 0x50, 0x41, 0x52, 0x43, 0x2D, 0x43, 0x4C, 
        0x41, 0x52, 0x4F, 0x2D, 0x53, 0x45, 0x43, 0x55,
        0x52, 0x45, 0x2D, 0x56, 0x41, 0x55, 0x4C, 0x54, 
        0x2D, 0x32, 0x30, 0x32, 0x36, 0x2D, 0x42, 0x54
    };

    private static string? _decryptedCachedToken;

    /// <summary>
    /// Descriptografa e retorna o token embutido diretamente em memória RAM.
    /// Retorna null se nenhum token tiver sido embutido no build.
    /// </summary>
    public static string? ResolveEmbeddedToken()
    {
        if (_decryptedCachedToken != null) return _decryptedCachedToken;

        if (string.IsNullOrWhiteSpace(EncryptedPayloadBase64) || 
            EncryptedPayloadBase64.StartsWith("%%BETA_"))
        {
            return null;
        }

        try
        {
            var cipherBytes = Convert.FromBase64String(EncryptedPayloadBase64);
            var saltBytes = Encoding.UTF8.GetBytes(BuildSalt.StartsWith("%%") ? "SPARC-DEFAULT-SALT" : BuildSalt);

            using var aes = Aes.Create();
            aes.KeySize = 256;
            
            // Deriva chave de 256 bits combinando MasterSeed com o salt específico do build
            using var kdf = new Rfc2898DeriveBytes(MasterSeed, saltBytes, 1000, HashAlgorithmName.SHA256);
            aes.Key = kdf.GetBytes(32);
            aes.IV = kdf.GetBytes(16);

            using var ms = new MemoryStream(cipherBytes);
            using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using var sr = new StreamReader(cs, Encoding.UTF8);

            var plain = sr.ReadToEnd().Trim();
            if (!string.IsNullOrWhiteSpace(plain))
            {
                _decryptedCachedToken = plain;
                return _decryptedCachedToken;
            }
        }
        catch
        {
            // Falha silenciosa caso o payload seja inválido
        }

        return null;
    }

    /// <summary>
    /// Método utilitário usado durante o build ou testes para cifrar um token antes de gravar no código.
    /// </summary>
    public static (string EncryptedBase64, string Salt) EncryptToken(string plainToken, string? customSalt = null)
    {
        if (string.IsNullOrWhiteSpace(plainToken)) return (string.Empty, string.Empty);

        var salt = customSalt ?? Guid.NewGuid().ToString("N");
        var saltBytes = Encoding.UTF8.GetBytes(salt);

        using var aes = Aes.Create();
        aes.KeySize = 256;

        using var kdf = new Rfc2898DeriveBytes(MasterSeed, saltBytes, 1000, HashAlgorithmName.SHA256);
        aes.Key = kdf.GetBytes(32);
        aes.IV = kdf.GetBytes(16);

        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs, Encoding.UTF8))
        {
            sw.Write(plainToken.Trim());
        }

        var cipherB64 = Convert.ToBase64String(ms.ToArray());
        return (cipherB64, salt);
    }

    /// <summary>
    /// Reseta o estado estático para isolamento limpo de testes unitários.
    /// </summary>
    public static void ResetForTesting()
    {
        _decryptedCachedToken = null;
        EncryptedPayloadBase64 = "%%BETA_ENCRYPTED_TOKEN_B64%%";
        BuildSalt = "%%BETA_BUILD_SALT%%";
    }
}
