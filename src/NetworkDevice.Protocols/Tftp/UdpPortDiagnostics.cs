using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace NetworkDevice.Protocols.Tftp;

public sealed record UdpPortConflict(
    int Port,
    int ProcessId,
    string ProcessName,
    string? ProcessPath,
    bool IsCurrentProcess);

/// <summary>
/// Utilitário para diagnóstico de portas UDP em uso (especialmente UDP 69 para TFTP).
/// Identifica processos conflitantes (ex: Tftpd32, Tftpd64, SolarWinds, instâncias órfãs do SPARC)
/// e fornece instruções claras de resolução ou fechamento automático.
/// </summary>
public static class UdpPortDiagnostics
{
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable,
        ref int pdwSize,
        bool bOrder,
        int ulAf,
        int TableClass,
        uint Reserved = 0);

    private const int AfInet = 2; // AF_INET (IPv4)
    private const int UdpTableOwnerPid = 1; // UDP_TABLE_OWNER_PID

    /// <summary>
    /// Localiza qual processo do sistema operacional está escutando na porta UDP especificada.
    /// </summary>
    public static UdpPortConflict? FindProcessUsingUdpPort(int port)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        int? pid = null;

        // 1. Tenta via Windows IP Helper API nativa (GetExtendedUdpTable - ultrarrápido)
        try
        {
            pid = FindPidViaIpHelper(port);
        }
        catch { }

        // 2. Fallback via netstat caso a API nativa não retorne
        if (!pid.HasValue || pid.Value <= 0)
        {
            try
            {
                pid = FindPidViaNetstat(port);
            }
            catch { }
        }

        if (!pid.HasValue || pid.Value <= 0)
        {
            // 3. Fallback heurístico: verifica se processos clássicos de TFTP estão rodando
            try
            {
                var known = Process.GetProcesses()
                    .FirstOrDefault(p => p.ProcessName.Contains("tftpd", StringComparison.OrdinalIgnoreCase) ||
                                         p.ProcessName.Equals("tftp", StringComparison.OrdinalIgnoreCase));
                if (known != null)
                {
                    pid = known.Id;
                }
            }
            catch { }
        }

        if (!pid.HasValue || pid.Value <= 0)
            return null;

        var targetPid = pid.Value;
        var currentPid = Environment.ProcessId;
        var isCurrent = targetPid == currentPid;

        string procName = $"Processo-{targetPid}";
        string? procPath = null;

        try
        {
            using var proc = Process.GetProcessById(targetPid);
            procName = proc.ProcessName;
            try { procPath = proc.MainModule?.FileName; } catch { }
        }
        catch { }

        return new UdpPortConflict(port, targetPid, procName, procPath, isCurrent);
    }

    private static int? FindPidViaIpHelper(int targetPort)
    {
        int size = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref size, false, AfInet, UdpTableOwnerPid, 0);
        if (size <= 0)
            return null;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedUdpTable(buffer, ref size, false, AfInet, UdpTableOwnerPid, 0) != 0)
                return null;

            int numEntries = Marshal.ReadInt32(buffer);
            var rowPtr = IntPtr.Add(buffer, 4);

            for (int i = 0; i < numEntries; i++)
            {
                int portBytes = Marshal.ReadInt32(rowPtr, 4);
                int port = ((portBytes & 0xFF) << 8) | ((portBytes >> 8) & 0xFF);
                int pid = Marshal.ReadInt32(rowPtr, 8);

                if (port == targetPort && pid > 0)
                    return pid;

                rowPtr = IntPtr.Add(rowPtr, 12);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return null;
    }

    private static int? FindPidViaNetstat(int targetPort)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netstat",
            Arguments = "-ano -p udp",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc is null)
            return null;

        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(1500);

        var regex = new Regex($@"^\s*UDP\s+(?:(?:\d{{1,3}}\.){{3}}\d{{1,3}}|\[[a-fA-F0-9:]+\]):{targetPort}\s+.*?\s+(\d+)\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        var match = regex.Match(output);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var pid))
            return pid;

        return null;
    }

    /// <summary>
    /// Gera mensagem em português amigável e explicativa para o operador quando a porta está ocupada.
    /// </summary>
    public static string BuildFriendlyConflictMessage(int port, UdpPortConflict? conflict)
    {
        if (conflict != null)
        {
            if (conflict.IsCurrentProcess)
            {
                return $"A porta UDP {port} (TFTP) está retida por uma tarefa anterior do próprio SPARC.\n\n" +
                       "👉 Como resolver:\n" +
                       "1. Feche e reabra o SPARC se a falha persistir;\n" +
                       "2. Inicie novamente o processo de atualização.";
            }

            if (conflict.ProcessName.Contains("tftpd", StringComparison.OrdinalIgnoreCase) ||
                conflict.ProcessName.Contains("solarwinds", StringComparison.OrdinalIgnoreCase))
            {
                return $"A porta de atualização de firmware (UDP {port}) já está em uso pelo aplicativo '{conflict.ProcessName}' (PID {conflict.ProcessId}).\n\n" +
                       "👉 COMO RESOLVER:\n" +
                       $"1. Localize o '{conflict.ProcessName}' aberto no seu computador (verifique na barra de tarefas o ícone com a letra 'T' ou perto do relógio);\n" +
                       $"2. Feche a janela do '{conflict.ProcessName}' (o SPARC possui servidor TFTP integrado de alta velocidade e não precisa de programas externos);\n" +
                       "3. Clique novamente em 'Iniciar Provisionamento Automático'.";
            }

            return $"A porta de rede UDP {port} (TFTP) já está sendo utilizada pelo programa '{conflict.ProcessName}' (PID {conflict.ProcessId}).\n\n" +
                   "👉 COMO RESOLVER:\n" +
                   $"1. Feche o programa '{conflict.ProcessName}' (ou finalize o processo no Gerenciador de Tarefas);\n" +
                   "2. O SPARC utilizará a porta UDP 69 para transferir o firmware para o equipamento;\n" +
                   "3. Tente novamente após liberar a porta.";
        }

        return $"A porta UDP {port} (TFTP) necessária para atualizar o firmware já está em uso por outro aplicativo no computador (ex.: Tftpd32, Tftpd64, SolarWinds TFTP ou outra instância do SPARC).\n\n" +
               "👉 COMO RESOLVER:\n" +
               "1. Verifique se o Tftpd32/64 ou outro servidor TFTP está aberto e feche-o;\n" +
               "2. O SPARC possui seu próprio servidor TFTP embutido e não requer programas externos;\n" +
               "3. Em seguida, tente novamente o provisionamento.";
    }

    /// <summary>
    /// Tenta encerrar processos conhecidos de servidores TFTP externos (Tftpd32/64) que estejam bloqueando a porta.
    /// Não encerra o próprio processo.
    /// </summary>
    public static bool TryCloseConflictingProcess(UdpPortConflict conflict)
    {
        if (conflict.IsCurrentProcess)
            return false;

        var name = conflict.ProcessName.ToLowerInvariant();
        var isKnownTftp = name.Contains("tftpd") ||
                          name.Contains("tftp32") ||
                          name.Contains("tftp64") ||
                          name.Contains("solarwinds");

        if (!isKnownTftp)
            return false;

        try
        {
            using var proc = Process.GetProcessById(conflict.ProcessId);
            proc.Kill();
            proc.WaitForExit(2000);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
