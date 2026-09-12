[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [AllowEmptyString()]
    [string] $Query = '',

    [AllowEmptyString()]
    [string] $CurrentFolder = '',

    [ValidateSet('L', 'R')]
    [string] $Pane = 'R',

    [string] $DoubleCommanderPath = 'doublecmd.exe',

    [switch] $DryRun
)

Set-StrictMode -Version Latest

function Normalize-PathInput {
    param([AllowEmptyString()][string] $Value)

    if ($null -eq $Value) {
        return ''
    }

    return $Value.Trim().Trim('"')
}

$queryPath = Normalize-PathInput $Query
$currentPath = Normalize-PathInput $CurrentFolder

if ([string]::IsNullOrWhiteSpace($queryPath)) {
    $queryPath = $currentPath
}

if ([string]::IsNullOrWhiteSpace($queryPath)) {
    throw '没有可打开的路径：请提供 {query}，或从文件资源管理器范围调用并提供 {current_folder}。'
}

if (-not [System.IO.Path]::IsPathFullyQualified($queryPath)) {
    $hasAbsoluteCurrentFolder = -not [string]::IsNullOrWhiteSpace($currentPath) -and
        [System.IO.Path]::IsPathFullyQualified($currentPath)
    if (-not $hasAbsoluteCurrentFolder) {
        throw '相对路径必须配合绝对的 {current_folder} 使用。'
    }

    $queryPath = Join-Path -Path $currentPath -ChildPath $queryPath
}

# Avoid a trailing backslash immediately before the closing quote. Keep a drive root valid.
if ($queryPath -match '^[A-Za-z]:\\$') {
    $queryPath = $queryPath + '.'
} elseif ($queryPath.Length -gt 3) {
    $queryPath = $queryPath.TrimEnd('\')
}

$quotedPath = '"' + $queryPath.Replace('"', '\"') + '"'
$argumentLine = '-C -P {0} {1}' -f $Pane, $quotedPath

if ($DryRun) {
    Write-Output ('File: ' + $DoubleCommanderPath)
    Write-Output ('Arguments: ' + $argumentLine)
    exit 0
}

try {
    Start-Process -FilePath $DoubleCommanderPath -ArgumentList $argumentLine -ErrorAction Stop | Out-Null
} catch {
    [Console]::Error.WriteLine(('无法启动 Double Commander: ' + $_.Exception.Message))
    exit 1
}
