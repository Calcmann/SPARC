using NetworkDevice.Core.Security;
using Xunit;

namespace NetworkDevice.Tests;

[Collection("EmbeddedTokenVaultTests")]
public class EmbeddedTokenVaultTests
{
    [Fact]
    public void EncryptToken_And_DecryptDirectly_RestoresOriginalToken()
    {
        EmbeddedTokenVault.ResetForTesting();
        var originalToken = "github_pat_11TEST_TOKEN_SECRET_XYZ99";
        var (encryptedB64, salt) = EmbeddedTokenVault.EncryptToken(originalToken);

        Assert.False(string.IsNullOrWhiteSpace(encryptedB64));
        Assert.False(string.IsNullOrWhiteSpace(salt));
        Assert.DoesNotContain("github_pat", encryptedB64); // O token nunca deve aparecer em claro na carga cifrada

        // Testa decriptografia através de reflexão/injeção das propriedades do vault
        EmbeddedTokenVault.EncryptedPayloadBase64 = encryptedB64;
        EmbeddedTokenVault.BuildSalt = salt;

        try
        {
            var decrypted = EmbeddedTokenVault.ResolveEmbeddedToken();
            Assert.Equal(originalToken, decrypted);
        }
        finally
        {
            EmbeddedTokenVault.ResetForTesting();
        }
    }

    [Fact]
    public void ResolveEmbeddedToken_ReturnsNull_WhenPayloadIsPlaceholder()
    {
        EmbeddedTokenVault.ResetForTesting();
        EmbeddedTokenVault.DisableFactoryFallbackForTesting = true;
        var decrypted = EmbeddedTokenVault.ResolveEmbeddedToken();
        Assert.Null(decrypted);
    }

    [Fact]
    public void ResolveEmbeddedToken_ReturnsFactoryToken_WhenFallbackActive()
    {
        EmbeddedTokenVault.ResetForTesting();
        var token = EmbeddedTokenVault.ResolveEmbeddedToken();
        Assert.NotNull(token);
        Assert.StartsWith("ghp_", token);
    }
}
