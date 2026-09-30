using NetworkDevice.Core.Session;
using NetworkDevice.Tests.TestDoubles;

namespace NetworkDevice.Tests;

/// <summary>
/// Regressão do 841 zerado: o IOS inicia no System Configuration Dialog
/// ("Would you like to enter the initial configuration dialog? [yes/no]:"), seguido de
/// "Would you like to terminate autoinstall? [yes]:" e "Press RETURN to get started".
/// O ConnectAsync precisa atravessar a sequência (no -&gt; ENTER -&gt; ENTER) até o prompt.
/// </summary>
public sealed class CiscoSetupDialogTests
{
    [Fact]
    public async Task ConnectAsync_WalksThroughSetupDialog_ToPrivilegedPrompt()
    {
        var emptyWrites = 0;
        var transport = new ScriptedTransport(
            cmd =>
            {
                if (cmd == "no")
                    return "Would you like to terminate autoinstall? [yes]: ";
                if (cmd == string.Empty)
                {
                    emptyWrites++;
                    return emptyWrites switch
                    {
                        1 => "", // eco do wake-up inicial: nada a enfileirar
                        2 => "Press RETURN to get started!",
                        _ => "Router#\r\n",
                    };
                }
                return "Router#\r\n";
            },
            initialOutput: "System Configuration Dialog ---\nWould you like to enter the initial configuration dialog? [yes/no]: ");

        await using var session = new DeviceSession(transport, new SessionOptions());
        await session.ConnectAsync();

        Assert.Equal(ExecMode.PrivilegedExec, session.Mode);
        Assert.Equal("Router#", session.CurrentPrompt);
        Assert.Contains("no", transport.Commands);
    }

    [Fact]
    public async Task ConnectAsync_AnswersTerminateAutoinstall_WithEnter()
    {
        var transport = new ScriptedTransport(
            cmd => cmd switch
            {
                "" => "Router#\r\n",
                _ => "Router#\r\n",
            },
            initialOutput: "Would you like to terminate autoinstall? [yes]: ");

        await using var session = new DeviceSession(transport, new SessionOptions());
        await session.ConnectAsync();

        Assert.Equal(ExecMode.PrivilegedExec, session.Mode);
        // ENTER (string vazia) aceita o default [yes]; jamais "N"
        Assert.DoesNotContain(transport.Commands, c => c.Equals("N", StringComparison.OrdinalIgnoreCase));
    }
}
