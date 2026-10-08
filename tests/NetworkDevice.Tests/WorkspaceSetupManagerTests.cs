using System.IO;
using NetworkDevice.Core.Installation;
using Xunit;

namespace NetworkDevice.Tests;

public class WorkspaceManagerTests
{
    [Fact]
    public void IsDevelopmentOrTestEnvironment_IdentifiesSrcAndBin()
    {
        Assert.True(WorkspaceManager.IsDevelopmentOrTestEnvironment(@"C:\SPARC\src\NetworkDevice.UI\bin\Debug\net8.0-windows\NetworkDevice.UI.exe"));
        Assert.True(WorkspaceManager.IsDevelopmentOrTestEnvironment(@"C:\SPARC\src\NetworkDevice.UI\bin\Release\net8.0-windows\NetworkDevice.UI.exe"));
        Assert.True(WorkspaceManager.IsDevelopmentOrTestEnvironment(@"D:\Projetos\SPARC\src\NetworkDevice.UI\App.exe"));

        Assert.False(WorkspaceManager.IsDevelopmentOrTestEnvironment(@"C:\Users\User\Downloads\SPARC-Beta-Testes-0.8.72.exe"));
        Assert.False(WorkspaceManager.IsDevelopmentOrTestEnvironment(@"E:\Pendrive\SPARC.exe"));
        Assert.False(WorkspaceManager.IsDevelopmentOrTestEnvironment(@"C:\SPARC\SPARC.exe"));
    }

    [Fact]
    public void IsRunningFromWorkspace_ComparesNormalizedPaths()
    {
        Assert.True(WorkspaceManager.IsRunningFromWorkspace(@"C:\SPARC\SPARC.exe", @"C:\SPARC"));
        Assert.True(WorkspaceManager.IsRunningFromWorkspace(@"C:\SPARC\SPARC.exe", @"C:\SPARC\"));
        Assert.True(WorkspaceManager.IsRunningFromWorkspace(@"c:\sparc\SPARC.exe", @"C:\SPARC"));

        Assert.False(WorkspaceManager.IsRunningFromWorkspace(@"C:\Users\User\Downloads\SPARC.exe", @"C:\SPARC"));
        Assert.False(WorkspaceManager.IsRunningFromWorkspace(@"D:\Outro\SPARC.exe", @"C:\SPARC"));
    }

    [Fact]
    public void GetDefaultWorkspaceRoot_ReturnsValidPathEndingInSPARC()
    {
        var root = WorkspaceManager.GetDefaultWorkspaceRoot();
        Assert.False(string.IsNullOrWhiteSpace(root));
        Assert.True(root.EndsWith("SPARC", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SaveWorkspaceRoot_AndGetDefaultWorkspaceRoot_RoundtripsSuccessfully()
    {
        var tempWorkspace = Path.Combine(Path.GetTempPath(), "SPARC_UnitTest_Workspace");
        try
        {
            Directory.CreateDirectory(tempWorkspace);
            WorkspaceManager.SaveWorkspaceRoot(tempWorkspace);

            var retrieved = WorkspaceManager.GetDefaultWorkspaceRoot();
            Assert.Equal(tempWorkspace, retrieved);
        }
        finally
        {
            try { Directory.Delete(tempWorkspace, true); } catch { }
            // Restaura para o padrão deletando o arquivo de teste em LocalAppData
            try
            {
                var cfg = WorkspaceManager.GetGlobalConfigPath();
                if (File.Exists(cfg)) File.Delete(cfg);
            }
            catch { }
        }
    }
}
