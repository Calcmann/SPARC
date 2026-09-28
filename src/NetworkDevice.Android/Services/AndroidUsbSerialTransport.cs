using Android.Hardware.Usb;
using NetworkDevice.Core.Session;
using UsbSerialForAndroid.Net;
using UsbSerialForAndroid.Net.Drivers;
using UsbSerialForAndroid.Net.Enums;
using UsbSerialForAndroid.Net.Helper;

namespace NetworkDevice.Android.Services;

public sealed class AndroidUsbSerialTransport : ITransport
{
    private readonly UsbDevice _device;
    private readonly int _baudRate;
    private UsbDriverBase? _driver;
    private bool _disposed;

    public AndroidUsbSerialTransport(UsbDevice device, int baudRate = 9600)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _baudRate = baudRate;
    }

    public bool IsOpen => _driver?.Connected == true;

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!UsbManagerHelper.HasPermission(_device))
        {
            throw new InvalidOperationException($"Permissão USB negada para o dispositivo {_device.DeviceName}. Solicite permissão ao usuário.");
        }

        _driver = UsbDriverFactory.CreateUsbDriver(_device.DeviceId);
        if (_driver == null)
        {
            throw new NotSupportedException($"Driver serial USB não suportado para VID: 0x{_device.VendorId:X4}, PID: 0x{_device.ProductId:X4}.");
        }

        _driver.ReadTimeout = 200;
        _driver.WriteTimeout = 500;
        _driver.Open(_baudRate, 8, StopBits.One, Parity.None);
        _driver.SetDtrEnabled(true);
        _driver.SetRtsEnabled(true);

        return Task.CompletedTask;
    }

    public Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_driver == null || !_driver.Connected)
            return Task.FromResult(0);

        try
        {
            var data = _driver.Read();
            if (data == null || data.Length == 0)
                return Task.FromResult(0);

            var toCopy = Math.Min(data.Length, buffer.Length);
            data.AsSpan(0, toCopy).CopyTo(buffer.Span);
            return Task.FromResult(toCopy);
        }
        catch (Exception)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(0);
            return Task.FromResult(0);
        }
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_driver == null || !_driver.Connected)
            throw new DeviceSessionException("Porta serial USB não conectada.");

        try
        {
            _driver.Write(buffer.ToArray());
            return ValueTask.CompletedTask;
        }
        catch (Exception ex)
        {
            throw new DeviceSessionException($"Falha ao transmitir bytes pela porta serial USB: {ex.Message}", ex);
        }
    }

    public Task SendBreakAsync(CancellationToken cancellationToken = default)
    {
        // Envia sequencia de quebra de boot (Ctrl+Break / DTR toggle)
        try
        {
            _driver?.SetDtrEnabled(false);
            _driver?.SetRtsEnabled(false);
            Thread.Sleep(250);
            _driver?.SetDtrEnabled(true);
            _driver?.SetRtsEnabled(true);
        }
        catch { }
        return Task.CompletedTask;
    }

    public Task CloseAsync()
    {
        try
        {
            _driver?.Close();
        }
        catch { }
        finally
        {
            _driver = null;
        }
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            try { _driver?.Close(); } catch { }
            _driver = null;
        }
        return ValueTask.CompletedTask;
    }
}
