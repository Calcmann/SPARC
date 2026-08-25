using System.IO.Ports;

namespace NetworkDevice.Protocols.Serial;

public static class SerialPorts
{
    public static IReadOnlyList<string> Available() =>
        SerialPort.GetPortNames();
}
