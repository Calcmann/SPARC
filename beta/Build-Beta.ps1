# Gera a variante BETA (node-locking + chave RSA + sincronizacao online + ofuscacao) em EXE UNICO,
# a partir de uma COPIA isolada em TEMP. Nao altera C:\SPARC\src.
# Padrao: framework-dependent single-file (~20 MB, exige .NET 8 instalado - igual ao exe padrao).
param([int]$Dias = 365, [string]$Tag = "", [string]$Versao = "", [switch]$FrameworkDependent, [switch]$SemOfuscacao)

$ErrorActionPreference = "Stop"

# Se $Versao nao for fornecida, extrai automaticamente de NetworkDevice.UI.csproj (ex.: 0.8.1)
if ([string]::IsNullOrWhiteSpace($Versao)) {
    $uiCsproj = "C:\SPARC\src\NetworkDevice.UI\NetworkDevice.UI.csproj"
    if (Test-Path $uiCsproj) {
        $xml = [xml](Get-Content $uiCsproj)
        $Versao = $xml.Project.PropertyGroup.Version
    }
    if ([string]::IsNullOrWhiteSpace($Versao)) {
        $Versao = "0.8.1"
    }
}

$running = Get-Process | Where-Object { $_.ProcessName -like "*SPARC-Beta*" -or $_.ProcessName -eq "NetworkDevice.UI" }
if ($running) {
    Write-Host "Processos em execucao - tentando fechar..."
    $running | ForEach-Object { try { $_.CloseMainWindow() | Out-Null; $_.Kill() } catch { } }
    Start-Sleep -Seconds 2
}
$betaRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$keysDir = Join-Path $betaRoot "keys"
$tool = Join-Path $betaRoot "tools\LicenseTool\LicenseTool.csproj"
$outDir = "C:\SPARC\dist-beta"
$tmp = Join-Path ([IO.Path]::GetTempPath()) "sparc-beta-build"

if ([string]::IsNullOrWhiteSpace($Tag)) { $Tag = "BETA-TESTES-$Versao-" + (Get-Date).ToString("yyyyMMdd") }
$expiresIso = ([DateTime]::UtcNow.AddDays($Dias)).ToString("yyyy-MM-ddTHH:mm:ssZ")
if ($FrameworkDependent) { $sc = "false" } else { $sc = "true" }

Write-Host "== [1/6] Chaves RSA..."
New-Item -ItemType Directory -Force -Path $keysDir | Out-Null
& dotnet run --project $tool -- new-key --dir $keysDir
if ($LASTEXITCODE -ne 0) { throw "falha ao gerar chaves" }
$pubPem = [IO.File]::ReadAllText((Join-Path $keysDir "public.pem")).Replace("`r`n", "`n").Trim()

Write-Host "== [2/6] Copiando base para area isolada..."
if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
& robocopy C:\SPARC\src "$tmp\src" /MIR /XD bin obj .vs /NFL /NDL /NJH /NJS | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy falhou ($LASTEXITCODE)" }
Copy-Item C:\SPARC\Manual_Instrucoes_Operador_SPARC.pdf $tmp\

Write-Host "== [3/6] Injetando guard (copia) + config..."
Copy-Item (Join-Path $betaRoot "guard\*.cs") "$tmp\src\NetworkDevice.UI\"
$cfg = Join-Path $tmp "src\NetworkDevice.UI\BetaConfig.cs"
$c = [IO.File]::ReadAllText($cfg)
$tokenPath = Join-Path $betaRoot "github_token.txt"
$ghToken = ""
if (Test-Path $tokenPath) {
    $ghToken = [IO.File]::ReadAllText($tokenPath).Trim()
}

# Cifra o token para que não fique exposto em texto simples no binário compilado
$tokenVaultPath = Join-Path $tmp "src\NetworkDevice.Core\Security\EmbeddedTokenVault.cs"
if (Test-Path $tokenVaultPath) {
    $tv = [IO.File]::ReadAllText($tokenVaultPath)
    if (![string]::IsNullOrWhiteSpace($ghToken)) {
        $salt = (Get-Date).ToString("yyyyMMddHHmmss")
        $seed = [byte[]]@(0x53, 0x50, 0x41, 0x52, 0x43, 0x2D, 0x43, 0x4C, 0x41, 0x52, 0x4F, 0x2D, 0x53, 0x45, 0x43, 0x55, 0x52, 0x45, 0x2D, 0x56, 0x41, 0x55, 0x4C, 0x54, 0x2D, 0x32, 0x30, 0x32, 0x36, 0x2D, 0x42, 0x54)
        $saltBytes = [System.Text.Encoding]::UTF8.GetBytes($salt)
        $kdf = [System.Security.Cryptography.Rfc2898DeriveBytes]::new($seed, $saltBytes, 1000, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
        $key = $kdf.GetBytes(32)
        $iv = $kdf.GetBytes(16)
        $aes = [System.Security.Cryptography.Aes]::Create()
        $aes.Key = $key
        $aes.IV = $iv
        $enc = $aes.CreateEncryptor()
        $plainBytes = [System.Text.Encoding]::UTF8.GetBytes($ghToken)
        $cipherBytes = $enc.TransformFinalBlock($plainBytes, 0, $plainBytes.Length)
        $b64 = [Convert]::ToBase64String($cipherBytes)
        $tv = $tv.Replace("%%BETA_ENCRYPTED_TOKEN_B64%%", $b64).Replace("%%BETA_BUILD_SALT%%", $salt)
    } else {
        $tv = $tv.Replace("%%BETA_ENCRYPTED_TOKEN_B64%%", "").Replace("%%BETA_BUILD_SALT%%", "")
    }
    [IO.File]::WriteAllText($tokenVaultPath, $tv)
}

$c = $c.Replace("%%BETA_TAG%%", $Tag).Replace("%%BETA_EXPIRES_UTC%%", $expiresIso).Replace("%%BETA_PUBLIC_KEY_PEM%%", $pubPem).Replace("%%BETA_GITHUB_TOKEN%%", $ghToken)
[IO.File]::WriteAllText($cfg, $c)
if ($c.Contains("%%BETA_")) { throw "placeholders nao substituidos" }

Write-Host "== [4/6] Hook no OnStartup (copia) + build..."
$appPath = Join-Path $tmp "src\NetworkDevice.UI\App.xaml.cs"
$t = [IO.File]::ReadAllText($appPath)
$hook = "`r`n        if (!global::NetworkDevice.UI.Beta.BetaLicenseGuard.Enforce(this)) { try { Shutdown(); } catch { } return; }"
$t2 = [regex]::Replace($t, "(protected override void OnStartup\(StartupEventArgs e\)\s*\{)", "`$1" + $hook)
if ($t2 -eq $t) { throw "hook nao aplicado em App.xaml.cs" }
[IO.File]::WriteAllText($appPath, $t2)
$uiCsprojPath = "$tmp\src\NetworkDevice.UI\NetworkDevice.UI.csproj"
$cp = [IO.File]::ReadAllText($uiCsprojPath)
$betaTarget = "<Target Name=`"BetaForceBundleWpfNatives`" BeforeTargets=`"GenerateBundle`">`r`n" +
"  <ItemGroup>`r`n" +
"    <FilesToBundle Include=`"`$(PublishDir)D3DCompiler_47_cor3.dll`"><RelativePath>D3DCompiler_47_cor3.dll</RelativePath></FilesToBundle>`r`n" +
"    <FilesToBundle Include=`"`$(PublishDir)PenImc_cor3.dll`"><RelativePath>PenImc_cor3.dll</RelativePath></FilesToBundle>`r`n" +
"    <FilesToBundle Include=`"`$(PublishDir)PresentationNative_cor3.dll`"><RelativePath>PresentationNative_cor3.dll</RelativePath></FilesToBundle>`r`n" +
"    <FilesToBundle Include=`"`$(PublishDir)vcruntime140_cor3.dll`"><RelativePath>vcruntime140_cor3.dll</RelativePath></FilesToBundle>`r`n" +
"    <FilesToBundle Include=`"`$(PublishDir)wpfgfx_cor3.dll`"><RelativePath>wpfgfx_cor3.dll</RelativePath></FilesToBundle>`r`n" +
"  </ItemGroup>`r`n</Target>`r`n</Project>"
$cp = $cp.Replace("</Project>", $betaTarget)
[IO.File]::WriteAllText($uiCsprojPath, $cp)
$uiProj = "$tmp\src\NetworkDevice.UI\NetworkDevice.UI.csproj"
& dotnet build $uiProj -c Release -r win-x64 --self-contained $sc /p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw "build da copia falhou" }
$binDir = "$tmp\src\NetworkDevice.UI\bin\Release\net8.0-windows\win-x64"
if (-not $FrameworkDependent) {
    & dotnet publish $uiProj -c Release -r win-x64 --self-contained $sc /p:PublishSingleFile=true /p:DebugType=none -o (Join-Path $tmp "pre")
    if ($LASTEXITCODE -ne 0) { throw "pre-publish falhou" }
}

if (-not $SemOfuscacao) {
    Write-Host "== [5/6] Ofuscando (Obfuscar, WPF-safe)..."
    $obfExe = $null
    foreach ($cand in @("obfuscar.console.exe", "obfuscar.exe")) {
        $p = Join-Path $env:USERPROFILE (".dotnet\tools\" + $cand)
        if (Test-Path $p) { $obfExe = $p; break }
    }
    if (-not $obfExe) {
        & dotnet tool install -g Obfuscar.GlobalTool
        if ($LASTEXITCODE -ne 0) { throw "falha ao instalar Obfuscar" }
        foreach ($cand in @("obfuscar.console.exe", "obfuscar.exe")) {
            $p = Join-Path $env:USERPROFILE (".dotnet\tools\" + $cand)
            if (Test-Path $p) { $obfExe = $p; break }
        }
    }
    if (-not $obfExe) { throw "executavel do Obfuscar nao encontrado" }
    $obfOut = Join-Path $tmp "obf"
    New-Item -ItemType Directory -Force -Path $obfOut | Out-Null
    $rts = (& dotnet --list-runtimes) | Where-Object { $_ -match "Microsoft.(WindowsDesktop|NETCore).App (8\.[0-9.]+)" }
    $fxPaths = @()
    foreach ($rt in $rts) {
        if ($rt -match "Microsoft.(WindowsDesktop|NETCore).App (8\.[0-9.]+) \[([^\]]+)\]") {
            $fxPaths += (Join-Path $Matches[3] $Matches[2])
        }
    }
    $fxPaths = $fxPaths | Sort-Object -Unique
    $xml = Join-Path $tmp "obfuscar.xml"
    $fxXml = ""
    foreach ($p in $fxPaths) { $fxXml += "<AssemblySearchPath path=`"" + $p + "`" />`r`n" }
    $obfXml = "<?xml version=`"1.0`"?>`r`n<Obfuscator>`r`n" +
        "<Var name=`"InPath`" value=`"" + $binDir + "`" />`r`n" +
        "<Var name=`"OutPath`" value=`"" + $obfOut + "`" />`r`n" +
        "<Var name=`"KeepPublicApi`" value=`"true`" />`r`n" +
        "<Var name=`"HidePrivateApi`" value=`"true`" />`r`n" +
        "<Var name=`"RenameProperties`" value=`"true`" />`r`n" +
        "<Var name=`"RenameEvents`" value=`"true`" />`r`n" +
        "<Var name=`"RenameFields`" value=`"true`" />`r`n" +
        "<Var name=`"HideStrings`" value=`"true`" />`r`n" +
        "<Var name=`"OptimizeMethods`" value=`"true`" />`r`n" +
        "<Var name=`"ReuseNames`" value=`"true`" />`r`n" +
        $fxXml +
        "<Module file=`"" + $binDir + "\NetworkDevice.Core.dll`" />`r`n" +
        "<Module file=`"" + $binDir + "\NetworkDevice.Protocols.dll`" />`r`n" +
        "<Module file=`"" + $binDir + "\NetworkDevice.Cisco.dll`" />`r`n" +
        "<Module file=`"" + $binDir + "\NetworkDevice.Fortinet.dll`" />`r`n" +
        "<Module file=`"" + $binDir + "\NetworkDevice.UI.dll`">`r`n" +
        "  <SkipType name=`"NetworkDevice.UI.App`" skipFields=`"true`" skipProperties=`"true`" skipMethods=`"true`" skipEvents=`"true`" />`r`n" +
        "  <SkipType name=`"NetworkDevice.UI.MainWindow`" skipFields=`"true`" skipProperties=`"true`" skipMethods=`"true`" skipEvents=`"true`" />`r`n" +
        "  <SkipType name=`"NetworkDevice.UI.CliDiagnosticWindow`" skipFields=`"true`" skipProperties=`"true`" skipMethods=`"true`" skipEvents=`"true`" />`r`n" +
        "  <SkipType name=`"NetworkDevice.UI.PasswordAuthDialog`" skipFields=`"true`" skipProperties=`"true`" skipMethods=`"true`" skipEvents=`"true`" />`r`n" +
        "  <SkipType name=`"NetworkDevice.UI.FortiGateAutoRecoveryWindow`" skipFields=`"true`" skipProperties=`"true`" skipMethods=`"true`" skipEvents=`"true`" />`r`n" +
        "  <SkipType name=`"NetworkDevice.UI.CliDebugWindow`" skipFields=`"true`" skipProperties=`"true`" skipMethods=`"true`" skipEvents=`"true`" />`r`n" +
        "  <SkipType name=`"NetworkDevice.UI.UiBrushes`" skipFields=`"true`" skipProperties=`"true`" skipMethods=`"true`" skipEvents=`"true`" />`r`n" +
        "  <SkipNamespace name=`"NetworkDevice.UI.Beta`" />`r`n" +
        "</Module>`r`n</Obfuscator>`r`n"
    [IO.File]::WriteAllText($xml, $obfXml)
    & $obfExe $xml
    if ($LASTEXITCODE -ne 0) { throw "obfuscar falhou" }
    foreach ($dll in @("NetworkDevice.Core.dll", "NetworkDevice.Protocols.dll", "NetworkDevice.Cisco.dll", "NetworkDevice.Fortinet.dll", "NetworkDevice.UI.dll")) {
        Copy-Item (Join-Path $obfOut $dll) (Join-Path $binDir $dll) -Force
    }
    $coreBytes = [IO.File]::ReadAllBytes((Join-Path $binDir "NetworkDevice.Core.dll"))
    $coreTxt = [Text.Encoding]::UTF8.GetString($coreBytes)
    if ($coreTxt.Contains("HandlePaginationAsync")) { throw "ofuscacao nao renomeou (verificar Obfuscar)" }
    Write-Host "Ofuscacao verificada: simbolos privados renomeados."
} else {
    Write-Host "== [5/6] Ofuscacao pulada (-SemOfuscacao)."
}

Write-Host "== [6/6] Publish single-file (self-contained=$sc)..."
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
& dotnet publish $uiProj --no-build -c Release -r win-x64 --self-contained $sc /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true /p:DebugType=none -o $outDir
if ($LASTEXITCODE -ne 0) { throw "publish falhou" }
Remove-Item (Join-Path $outDir "*.pdb") -ErrorAction SilentlyContinue
Remove-Item (Join-Path $outDir "*_cor3.dll"), (Join-Path $outDir "vcruntime140_cor3.dll") -ErrorAction SilentlyContinue
$built = Join-Path $outDir "NetworkDevice.UI.exe"
$versionedExe = Join-Path $outDir "SPARC-Beta-Testes-$Versao.exe"
if (Test-Path $versionedExe) { Remove-Item -Force $versionedExe }
Move-Item -LiteralPath $built -Destination $versionedExe

# Remove binário genérico legado se existir para não manter duplicatas
$legacyGeneric = Join-Path $outDir "SPARC-Beta-Testes.exe"
if (Test-Path $legacyGeneric) { Remove-Item -Force $legacyGeneric -ErrorAction SilentlyContinue }

Copy-Item (Join-Path $tmp "Manual_Instrucoes_Operador_SPARC.pdf") (Join-Path $outDir "Manual_Instrucoes_Operador_SPARC.pdf") -Force -ErrorAction SilentlyContinue
$mb = [math]::Round((Get-Item $versionedExe).Length / 1MB, 1)

Write-Host ""
Write-Host "BETA PRONTA (VERSAO UNICA): $versionedExe ($mb MB)"
$expiresLocal = ([DateTime]::Parse($expiresIso).ToUniversalTime()).ToLocalTime().ToString("dd/MM/yyyy HH:mm")
Write-Host "Tag: $Tag | Build valido ate: $expiresLocal (horario local) | Ofuscado: $(-not $SemOfuscacao)"
Write-Host "Base C:\SPARC\src: INTACTA (verifique com git status)."
