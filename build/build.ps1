$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root 'src'
$assets = Join-Path $root 'assets'
$outDir = Join-Path $root 'build\out'
$distDir = Join-Path $root 'dist'
$icon = Join-Path $assets 'app.ico'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

function Ensure-Utf8Bom([string]$path) {
    $text = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    $encoding = New-Object System.Text.UTF8Encoding($true)
    [System.IO.File]::WriteAllText($path, $text, $encoding)
}

function Compile-Executable {
    param(
        [string[]]$Sources,
        [string]$OutputAssembly,
        [string]$Manifest,
        [string]$Resources
    )

    foreach ($source in $Sources) {
        Ensure-Utf8Bom $source
    }

    $provider = New-Object Microsoft.CSharp.CSharpCodeProvider
    $parameters = New-Object System.CodeDom.Compiler.CompilerParameters
    $parameters.GenerateExecutable = $true
    $parameters.GenerateInMemory = $false
    $parameters.OutputAssembly = $OutputAssembly
    $parameters.IncludeDebugInformation = $false
    $parameters.TempFiles = New-Object System.CodeDom.Compiler.TempFileCollection

    $references = @(
        (Join-Path $framework 'System.dll'),
        (Join-Path $framework 'System.Core.dll'),
        (Join-Path $framework 'System.Drawing.dll'),
        (Join-Path $framework 'System.Windows.Forms.dll'),
        (Join-Path $framework 'System.Runtime.Serialization.dll'),
        (Join-Path $framework 'System.Xml.dll'),
        (Join-Path $framework 'Microsoft.CSharp.dll')
    )
    foreach ($reference in $references) {
        [void]$parameters.ReferencedAssemblies.Add($reference)
    }

    $options = '/target:winexe /platform:anycpu /optimize+ /codepage:65001 ' +
        '/win32icon:"' + $icon + '" /win32manifest:"' + $Manifest + '"'
    if ($Resources) {
        $options += ' ' + $Resources
    }
    $parameters.CompilerOptions = $options

    $results = $provider.CompileAssemblyFromFile($parameters, $Sources)
    try {
        if ($results.Errors.HasErrors) {
            $message = New-Object System.Text.StringBuilder
            [void]$message.AppendLine('C# compilation failed:')
            foreach ($error in $results.Errors) {
                [void]$message.AppendLine([string]$error)
            }
            throw $message.ToString()
        }
    }
    finally {
        $provider.Dispose()
    }
}

$appSource = @(
    (Join-Path $src 'MacAddressGenerator\Program.cs'),
    (Join-Path $src 'MacAddressGenerator\AssemblyInfo.cs')
)
$appOutput = Join-Path $outDir 'MacAddressGenerator.exe'
Compile-Executable `
    -Sources $appSource `
    -OutputAssembly $appOutput `
    -Manifest (Join-Path $src 'MacAddressGenerator\app.manifest')

$installerSource = @(
    (Join-Path $src 'Installer\Installer.cs'),
    (Join-Path $src 'Installer\AssemblyInfo.cs')
)
$installerOutput = Join-Path $outDir 'MAC地址生成器_安装程序.exe'
$payloadResource = '/resource:"' + $appOutput + '",Payload.MacAddressGenerator.exe'
Compile-Executable `
    -Sources $installerSource `
    -OutputAssembly $installerOutput `
    -Manifest (Join-Path $src 'Installer\installer.manifest') `
    -Resources $payloadResource

$deliveryPath = Join-Path $distDir 'MAC地址生成器_安装程序.exe'
Copy-Item -LiteralPath $installerOutput -Destination $deliveryPath -Force

$appInfo = Get-Item $appOutput
$installerInfo = Get-Item $deliveryPath
Write-Output ('APP      ' + $appInfo.FullName + '  ' + $appInfo.Length + ' bytes')
Write-Output ('INSTALLER ' + $installerInfo.FullName + '  ' + $installerInfo.Length + ' bytes')

$check = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($installerOutput))
Write-Output ('RESOURCE  ' + (($check.GetManifestResourceNames()) -join ', '))
