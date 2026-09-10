using NetworkDevice.Core.Provisioning;
using Xunit;

namespace NetworkDevice.Tests;

public sealed class HostNetworkSnapshotTests
{
    [Theory]
    [InlineData(29, "255.255.255.248")]
    [InlineData(30, "255.255.255.252")]
    [InlineData(24, "255.255.255.0")]
    [InlineData(32, "255.255.255.255")]
    [InlineData(0, "0.0.0.0")]
    public void PrefixLengthToMask_Converte(int prefix, string esperado)
    {
        Assert.Equal(esperado, HostNetworkManager.PrefixLengthToMask(prefix));
    }

    [Fact]
    public void Snapshot_RoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sparc-nettest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var snap = new AdapterSnapshot("Ethernet Teste", new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc),
                false, "192.168.0.90", "255.255.255.0", "192.168.0.1", new List<string> { "8.8.8.8", "1.1.1.1" });
            HostNetworkManager.SaveSnapshot(snap, dir);
            var lido = HostNetworkManager.LoadSnapshot("Ethernet Teste", dir);
            Assert.NotNull(lido);
            Assert.Equal("192.168.0.90", lido.IpAddress);
            Assert.Equal("255.255.255.0", lido.SubnetMask);
            Assert.Equal("192.168.0.1", lido.Gateway);
            Assert.False(lido.DhcpEnabled);
            Assert.Equal(new List<string> { "8.8.8.8", "1.1.1.1" }, lido.DnsServers);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Descrever_DhcpEFixo()
    {
        var dhcp = new AdapterSnapshot("Eth", DateTime.UtcNow, true, "10.0.0.5", null, null, new List<string>());
        Assert.Contains("DHCP", dhcp.Descrever());
        var fixo = new AdapterSnapshot("Eth", DateTime.UtcNow, false, "10.10.10.2", "255.255.255.248", "10.10.10.1", new List<string> { "1.1.1.1" });
        Assert.Contains("10.10.10.2", fixo.Descrever());
        Assert.Contains("1.1.1.1", fixo.Descrever());
    }

    [Fact]
    public void LoadSnapshot_Inexistente_RetornaNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sparc-nettest-vazio-" + Guid.NewGuid().ToString("N"));
        Assert.Null(HostNetworkManager.LoadSnapshot("Inexistente", dir));
    }
}

// Fora do namespace propositalmente (anexo ao fim do arquivo); usings do topo continuam valendo.
public sealed class HostNetworkValidityTests
{
    [Fact]
    public void SnapshotEhSparc_IgualAoAplicado()
    {
        var snap = new AdapterSnapshot("Ethernet", DateTime.UtcNow, false, "10.10.10.2",
            "255.255.255.248", "10.10.10.1", new List<string> { "1.1.1.1", "8.8.8.8" });
        Assert.True(HostNetworkManager.SnapshotEhSparc(snap, "10.10.10.2"));
    }

    [Fact]
    public void SnapshotEhSparc_OriginalDiferente()
    {
        var snap = new AdapterSnapshot("Ethernet", DateTime.UtcNow, false, "192.168.0.90",
            "255.255.255.0", "192.168.0.1", new List<string> { "1.1.1.1", "8.8.8.8" });
        Assert.False(HostNetworkManager.SnapshotEhSparc(snap, "10.10.10.2"));
    }

    [Fact]
    public void SnapshotEhSparc_DhcpNuncaESparc()
    {
        var snap = new AdapterSnapshot("Ethernet", DateTime.UtcNow, true, "10.10.10.2",
            null, null, new List<string>());
        Assert.False(HostNetworkManager.SnapshotEhSparc(snap, "10.10.10.2"));
    }
}

// Testes do log/marcador (sem tocar na placa de verdade).
public sealed class HostNetworkExitTests
{
    [Fact]
    public void NetLog_NaoLanca()
    {
        var ex = Record.Exception(() => HostNetworkManager.NetLog("teste-unitario"));
        Assert.Null(ex);
    }

    [Fact]
    public void NeedsRestoreOnExit_Existe()
    {
        _ = HostNetworkManager.NeedsRestoreOnExit;
    }
}
