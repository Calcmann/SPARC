# Avaliação Arquitetural e Roadmap para Versão Android (SPARC)

Este documento estabelece a análise técnica, compatibilidade de componentes e diretrizes arquiteturais para a portabilidade e desenvolvimento da futura versão Android do SPARC.

---

## 1. Diagnóstico do Código Atual (.NET 8)

### Componentes 100% Reutilizáveis (Sem Alterações)
| Componente / Módulo | Tecnologia | Status no Android |
|---------------------|------------|-------------------|
| `NetworkDevice.Core.Provisioning` (SaipParser, SaipCircuitData, IpCalculator) | .NET 8 / PdfPig | ✅ **100% Compatível** (Funciona diretamente em .NET MAUI / .NET Android). |
| `NetworkDevice.Core.Recovery` (RecoveryStateMachine, BootMonitor, BootInterruptScheduler) | .NET 8 Tasks / Regex | ✅ **100% Compatível** |
| `NetworkDevice.Core.Session` (DeviceSession, RegexPromptMatcher, ExecMode) | .NET 8 Streams | ✅ **100% Compatível** |
| `NetworkDevice.Protocols.Tftp` (EmbeddedTftpServer) | `UdpClient` / Sockets | ✅ **100% Compatível** (Requer permissões de rede no AndroidManifest). |
| `NetworkDevice.Protocols.Ssh` (SshTransport) | `SSH.NET` | ✅ **100% Compatível** |
| `NetworkDevice.Core.Diagnostics` (ConnectivityService, BandwidthTestService) | `System.Net.NetworkInformation.Ping` e `HttpClient` | ✅ **100% Compatível** |

---

## 2. Pontos Críticos e Soluções Arquiteturais para Android

### A. Comunicação Serial USB (Console do Roteador)
* **Desafio no Android**: A biblioteca padrão `System.IO.Ports` acessa dispositivos COM via Win32 API ou `/dev/ttyUSB*` direto no Linux. No Android, o kernel restringe o acesso direto a nós de dispositivo `/dev/` para aplicativos não-root.
* **Solução Arquitetural**:
  * Utilizar a API oficial do Android **USB Host** (`android.hardware.usb.UsbManager` / `UsbDeviceConnection`).
  * Utilizar a biblioteca padrão da indústria **`usb-serial-for-android`** (ou porte em C# / MAUI).
  * Como o SPARC utiliza a interface `ITransport` no `DeviceSession`, basta implementar um **`AndroidUsbSerialTransport : ITransport`**:
    ```csharp
    public class AndroidUsbSerialTransport : ITransport
    {
        // Conecta ao chip FTDI / CP2102 / CH340 / PL2303 via cabo USB OTG
        // Implementa ReadAsync, WriteAsync, SendBreakAsync, CloseAsync
    }
    ```

### B. Configuração de IP na Placa de Rede / Dispositivo de Teste
* **Desafio no Android**: O comando Windows `netsh` não existe no Android. Em dispositivos móveis, a conexão com a LAN do roteador ocorre tipicamente via **Adaptador USB-C para Ethernet RJ45**. A alteração do endereço IP estático sem intervenção do usuário requer permissões de Device Owner / VPN Service ou configuração pelo usuário.
* **Solução Arquitetural**:
  * Implementar a abstração `IHostNetworkService`.
  * **Modo Windows**: Executa `netsh` silenciosamente.
  * **Modo Android**:
    1. Calcula o IP Host, Máscara e Gateway exatos a partir da Ficha SAIP.
    2. Exibe uma interface gráfica com botão "Copiar IP", "Copiar Máscara" e "Copiar Gateway".
    3. Dispara um Intent direto para as configurações de rede do Android (`android.provider.Settings.ACTION_WIRELESS_SETTINGS` ou `ACTION_WIFI_SETTINGS` ou `ACTION_ETHERNET_SETTINGS`).

### C. Teste de Banda e Conectividade
* **Desafio no Android**: Executáveis externos como `speedtest.exe` não rodam no Android.
* **Solução Arquitetural**:
  * O novo serviço `BandwidthTestService` foi desenhado com um motor nativo HTTP em C# (`HttpClient`), que mede a taxa de transferência em megabits por segundo (Mbps), latência e jitter sem precisar de nenhum binário externo.
  * O teste de ping ICMP via `ConnectivityService` utiliza `System.Net.NetworkInformation.Ping` que é suportado nativamente pelo Mono / .NET Runtime no Android.

---

## 3. Estratégia de Framework para o Front-end Android

Recomendamos **.NET MAUI** (Multi-platform App UI) ou **Avalonia UI**:
1. **Reaproveitamento de 90%+ do código C#** existente na pasta `src/NetworkDevice.Core` e `src/NetworkDevice.Protocols`.
2. **Mesma linguagem (C#)** e padrão de binding / XAML.
3. Capacidade de gerar build para Android (APK/AAB) e manter paralelamente a versão Windows.

---

## 4. Diagrama da Arquitetura Multiplataforma

```text
+-----------------------------------------------------------------------+
|                       Camada de Apresentação (UI)                     |
|    +-----------------------------+   +-----------------------------+  |
|    |       SPARC WPF (Win)       |   |     SPARC MAUI (Android)    |  |
|    +-----------------------------+   +-----------------------------+  |
+-----------------------------------+-----------------------------------+
                                    |
                                    v
+-----------------------------------------------------------------------+
|                     NetworkDevice.Core (.NET 8.0)                     |
|  - SaipParser / IpCalculator / SaipCircuitData                        |
|  - RecoveryStateMachine / CiscoIOSRecovery / HpeComwareRecovery       |
|  - DeviceSession / RegexPromptMatcher                                 |
|  - ConnectivityService (ICMP Ping)                                    |
|  - BandwidthTestService (HTTP SpeedTest)                              |
|  - IHostNetworkService (Abstração de IP)                              |
+-----------------------------------+-----------------------------------+
                                    |
                                    v
+-----------------------------------------------------------------------+
|                    Camada de Transporte / Protocolos                  |
|    +-----------------------------+   +-----------------------------+  |
|    | Windows: SerialTransport    |   | Android: UsbSerialTransport |  |
|    | (System.IO.Ports - Win32)   |   | (UsbManager / OTG FTDI/CH34)|  |
|    +-----------------------------+   +-----------------------------+  |
|    | EmbeddedTftpServer (UDP)    |   | SSHTransport (SSH.NET)      |  |
|    +-----------------------------+   +-----------------------------+  |
+-----------------------------------------------------------------------+
```
