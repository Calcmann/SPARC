using System.Reflection;
using Android.Hardware.Usb;
using NetworkDevice.Core.Session;
using UsbSerialForAndroid.Net;
using UsbSerialForAndroid.Net.Drivers;
using UsbSerialForAndroid.Net.Enums;
using UsbSerialForAndroid.Net.Helper;

namespace NetworkDevice.Android.Services;

public sealed class AndroidUsbSerialTransport : ITransport
{
    private UsbDevice _device;
    private int _baudRate;
    private UsbDriverBase? _driver;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private volatile bool _isOpen;
    private bool _disposed;

    public AndroidUsbSerialTransport(UsbDevice device, int baudRate = 9600)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _baudRate = baudRate;
    }

    public bool IsOpen => _isOpen && _driver != null;

    public int BaudRate => _baudRate;

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_isOpen && _driver != null)
            return;

        // Auto-reconciliação do dispositivo caso o DeviceId tenha mudado por re-enumeração
        try
        {
            var allDevices = UsbManagerHelper.GetAllUsbDevices()?.ToList() ?? new List<UsbDevice>();
            if (allDevices.Count > 0)
            {
                var match = allDevices.FirstOrDefault(d => d.DeviceId == _device.DeviceId)
                         ?? allDevices.FirstOrDefault(d => d.VendorId == _device.VendorId && d.ProductId == _device.ProductId)
                         ?? allDevices.FirstOrDefault(d => d.DeviceName == _device.DeviceName)
                         ?? allDevices[0];
                _device = match;
            }
        }
        catch { }

        if (!UsbManagerHelper.HasPermission(_device))
        {
            throw new InvalidOperationException($"Permissão USB pendente para o dispositivo {_device.DeviceName}. Confirme o aviso na tela.");
        }

        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            if (_isOpen && _driver != null)
                return;

            _driver = UsbDriverFactory.CreateUsbDriver(_device.DeviceId);
            if (_driver == null)
            {
                throw new NotSupportedException($"Driver serial USB não suportado para VID: 0x{_device.VendorId:X4}, PID: 0x{_device.ProductId:X4}.");
            }

            _driver.ReadTimeout = 500;
            _driver.WriteTimeout = 1000;
            await _driver.OpenAsync(_baudRate, 8, StopBits.One, Parity.None);
            try { _driver.SetDtrEnabled(true); } catch { }
            try { _driver.SetRtsEnabled(true); } catch { }
            _isOpen = true;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <summary>
    /// Troca a taxa serial de forma confiável: fecha e reabre o driver no novo baud.
    /// (A tentativa anterior via reflection em SetBaudRate/SetParameters falhava de forma
    /// silenciosa dependendo do driver — FTDI/CP2102/CH340/PL2303 — e a identificação
    /// Fortinet a 115200 bps nunca ocorria de fato.)
    /// </summary>
    public async Task ChangeBaudRateAsync(int newBaudRate, CancellationToken cancellationToken = default)
    {
        if (_baudRate == newBaudRate && _isOpen && _driver != null)
            return;

        _baudRate = newBaudRate;
        if (_driver == null || !_isOpen)
            return;

        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            if (_driver != null)
            {
                try { await _driver.CloseAsync(); } catch { }
                _driver = null;
            }
            _isOpen = false;
        }
        finally
        {
            _ioLock.Release();
        }

        await Task.Delay(150, cancellationToken);
        await OpenAsync(cancellationToken);
    }

    public async Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!_isOpen || _driver == null)
            return 0;

        if (cancellationToken.IsCancellationRequested)
            return 0;

        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            if (!_isOpen || _driver == null)
                return 0;

            // Timeout defensivo de 200ms para polling serial não travar caso nada chegue
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(200);

            var temp = System.Buffers.ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                var read = await _driver.ReadAsync(temp, 0, buffer.Length, timeoutCts.Token);
                if (read > 0)
                {
                    temp.AsSpan(0, read).CopyTo(buffer.Span);
                    return read;
                }
                return 0;
            }
            catch (OperationCanceledException)
            {
                // Timeout expirou sem bytes disponíveis
                return 0;
            }
            catch (Exception)
            {
                return 0;
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(temp);
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!_isOpen || _driver == null)
            throw new DeviceSessionException("Porta serial USB não conectada.");

        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            if (!_isOpen || _driver == null)
                throw new DeviceSessionException("Porta serial USB não conectada.");

            var array = buffer.ToArray();
            try
            {
                await _driver.WriteAsync(array, 0, array.Length, cancellationToken);
            }
            catch
            {
                // Fallback para Write síncrono caso o canal assíncrono tenha rejeitado
                try
                {
                    _driver.Write(array);
                }
                catch (Exception ex)
                {
                    throw new DeviceSessionException($"Falha ao transmitir bytes pela porta serial USB: {ex.Message}", ex);
                }
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <summary>
    /// Envia sinal Break para interceptação de boot (ROMMON Cisco 1900/841).
    /// A lib USB-serial não expõe break elétrico; usa break por enquadramento
    /// (framing break): 1200 bps + 25x 0x00 ≈ 208ms de nível LOW contínuo, a mesma
    /// técnica do Windows para CH340. A troca de baud é in-place (SetBaudRate/
    /// SetBaudrate/SetParameter existem nos 4 drivers: QinHeng/Silabs/FTDI/Prolific),
    /// sem fechar a porta. Fallback: pulso DTR/RTS.
    /// </summary>
    public async Task SendBreakAsync(CancellationToken cancellationToken = default)
    {
        var baudDirty = false;

        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            if (!_isOpen || _driver == null)
                return;

            var drv = _driver;
            try
            {
                if (TrySetBaudInPlace(drv, 1200))
                {
                    try { drv.Write(new byte[25]); }
                    catch { /* ainda tenta restaurar o baud abaixo */ }

                    try { await Task.Delay(250, cancellationToken); }
                    catch (OperationCanceledException) { }

                    if (TrySetBaudInPlace(drv, _baudRate))
                        return;

                    // Baud ficou em 1200 — sinaliza reopen fora do lock
                    baudDirty = true;
                    return;
                }
            }
            catch { }

            // Fallback: pulso DTR/RTS (não é break real, mas acorda algumas consoles)
            try
            {
                _driver?.SetDtrEnabled(false);
                _driver?.SetRtsEnabled(false);
            }
            catch { }
            try { await Task.Delay(300, cancellationToken); }
            catch (OperationCanceledException) { }
            try
            {
                _driver?.SetDtrEnabled(true);
                _driver?.SetRtsEnabled(true);
            }
            catch { }
        }
        finally
        {
            _ioLock.Release();
        }

        if (baudDirty)
            await ReopenAsync(cancellationToken);
    }

    /// <summary>
    /// Troca o baud sem fechar a porta (via SetBaudRate/SetBaudrate/SetParameter do
    /// driver concreto). Retorna false se o driver não expuser nenhuma das APIs.
    /// </summary>
    private static bool TrySetBaudInPlace(object driver, int baudRate)
    {
        try
        {
            var driverType = driver.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var baudMethod = driverType.GetMethod("SetBaudRate", flags)
                          ?? driverType.GetMethod("SetBaudrate", flags);
            if (baudMethod != null)
            {
                baudMethod.Invoke(driver, new object[] { baudRate });
                return true;
            }

            var paramMethod = driverType.GetMethod("SetParameter", flags)
                           ?? driverType.GetMethod("SetParameters", flags);
            if (paramMethod != null && paramMethod.GetParameters().Length == 4)
            {
                paramMethod.Invoke(driver, new object[] { baudRate, (byte)8, StopBits.One, Parity.None });
                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>Fecha e reabre a porta no baud atual (recuperação de baud inconsistente).</summary>
    private async Task ReopenAsync(CancellationToken cancellationToken)
    {
        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            if (_driver != null)
            {
                try { await _driver.CloseAsync(); } catch { }
                _driver = null;
            }
            _isOpen = false;
        }
        finally
        {
            _ioLock.Release();
        }

        await Task.Delay(150, cancellationToken);
        await OpenAsync(cancellationToken);
    }

    public async Task CloseAsync()
    {
        _isOpen = false;
        await _ioLock.WaitAsync();
        try
        {
            if (_driver != null)
            {
                try { await _driver.CloseAsync(); } catch { }
                _driver = null;
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await CloseAsync();
            _ioLock.Dispose();
        }
    }
}
