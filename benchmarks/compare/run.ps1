#Requires -Version 7
<#
.SYNOPSIS
Runs the client comparison: builds and starts the mock, runs the .NET, Node and Python harnesses one after another, then
stops the mock and merges the results.

.DESCRIPTION
Usage: run.ps1 [--smoke] [--machine <name>] [--project <name>] [--port <n>] [--mock-cores <list>] [--client-cores <list>]

The mock and the harnesses run on separate cores. On a hybrid CPU the harnesses get the performance cores and the mock
the efficiency cores; with one core type the mock gets the lower half and the harnesses the upper half. --mock-cores
and --client-cores, lists such as 12-19 or 0-3,8, override the split. Results go to results/<machine>/, and the
merged table to results/<machine>/comparison.md. The script exits with 1 if a build or a harness failed, and with 2 on
a wrong command line.
#>

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$usage = 'Usage: run.ps1 [--smoke] [--machine <name>] [--project <name>] [--port <n>] [--mock-cores <list>] [--client-cores <list>]'

function Exit-Usage([string] $message) {
    [Console]::Error.WriteLine($message)
    [Console]::Error.WriteLine($usage)
    exit 2
}

# --- Command line -------------------------------------------------------------------------------------------------

$smoke = $false
$machine = [Environment]::MachineName
$project = 'ZeroAlloc.Jev'
$port = 5005
$mockCoresArg = $null
$clientCoresArg = $null
for ($i = 0; $i -lt $args.Count; $i++) {
    $arg = [string] $args[$i]
    if ($arg -ceq '--smoke') {
        $smoke = $true
        continue
    }

    if ($arg -cnotin '--machine', '--project', '--port', '--mock-cores', '--client-cores') {
        Exit-Usage "Unknown argument: $arg"
    }

    if ($i + 1 -ge $args.Count) {
        Exit-Usage "$arg needs a value."
    }

    $value = [string] $args[++$i]
    switch -CaseSensitive ($arg) {
        '--machine' { $machine = $value }
        '--project' { $project = $value }
        '--port' {
            $parsed = 0
            if (-not [int]::TryParse($value, [ref] $parsed) -or $parsed -lt 1 -or $parsed -gt 65535) {
                Exit-Usage '--port must be a number from 1 to 65535.'
            }

            $port = $parsed
        }
        '--mock-cores' { $mockCoresArg = $value }
        '--client-cores' { $clientCoresArg = $value }
    }
}

if ([string]::IsNullOrWhiteSpace($machine)) { Exit-Usage '--machine must not be blank.' }
if ([string]::IsNullOrWhiteSpace($project)) { Exit-Usage '--project must not be blank.' }
$machine = $machine.Trim()
$project = $project.Trim()

# --- Core lists ---------------------------------------------------------------------------------------------------

# Expands a list such as 0-3,8 into sorted core indexes.
function ConvertFrom-CoreList([string] $text, [string] $name) {
    if ($text -notmatch '^\d+(-\d+)?(,\d+(-\d+)?)*$') {
        Exit-Usage "$name must be a core list such as 0-3,8, not '$text'."
    }

    $cores = [Collections.Generic.SortedSet[int]]::new()
    foreach ($part in $text.Split(',')) {
        $bounds = $part.Split('-')
        $first = [int] $bounds[0]
        $last = [int] $bounds[-1]
        if ($last -lt $first) { Exit-Usage "$name has a descending range: $part." }
        for ($core = $first; $core -le $last; $core++) { [void] $cores.Add($core) }
    }

    if ($cores.Max -ge [Environment]::ProcessorCount -or $cores.Max -gt 63) {
        Exit-Usage "$name names core $($cores.Max), but this machine has cores 0 to $([Environment]::ProcessorCount - 1)."
    }

    return , [int[]] @($cores)
}

# Writes sorted core indexes as a canonical list such as 0-3,8, the form the .NET harness writes.
function ConvertTo-CoreList([int[]] $cores) {
    $parts = [Collections.Generic.List[string]]::new()
    $i = 0
    while ($i -lt $cores.Count) {
        $j = $i
        while ($j + 1 -lt $cores.Count -and $cores[$j + 1] -eq $cores[$j] + 1) { $j++ }
        $parts.Add($(if ($j -gt $i) { "$($cores[$i])-$($cores[$j])" } else { "$($cores[$i])" }))
        $i = $j + 1
    }

    return $parts -join ','
}

function ConvertTo-CoreMask([int[]] $cores) {
    [uint64] $mask = 0
    foreach ($core in $cores) { $mask = $mask -bor ([uint64] 1 -shl $core) }
    return $mask
}

# Returns each logical core's type as a dictionary from core index to a rank, where a higher rank is a faster core
# type. Windows reports each physical core's EfficiencyClass through GetLogicalProcessorInformationEx; Linux lists the
# hybrid core types in /sys/devices/cpu_core and /sys/devices/cpu_atom. Returns $null when the types are unknown.
function Get-CoreRanks {
    if ($IsWindows) {
        if (-not ('JevBench.CpuTopology' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace JevBench
{
    public static class CpuTopology
    {
        private const int RelationProcessorCore = 0;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);

        // One entry per physical core in processor group 0: its EfficiencyClass, then its logical processors' mask.
        public static long[][] Cores()
        {
            uint length = 0;
            GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref length);
            var buffer = Marshal.AllocHGlobal((int)length);
            try
            {
                if (!GetLogicalProcessorInformationEx(RelationProcessorCore, buffer, ref length))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                var cores = new List<long[]>();
                for (var offset = 0; offset < length;)
                {
                    var entry = buffer + offset;
                    var size = Marshal.ReadInt32(entry, 4);
                    // PROCESSOR_RELATIONSHIP: Flags at 8, EfficiencyClass at 9, GroupCount at 30, then GROUP_AFFINITY
                    // entries at 32, each a KAFFINITY mask followed by its group number.
                    var efficiencyClass = Marshal.ReadByte(entry, 9);
                    var groupCount = Marshal.ReadInt16(entry, 30);
                    for (var g = 0; g < groupCount; g++)
                    {
                        var mask = Marshal.ReadInt64(entry, 32 + (g * 16));
                        var group = Marshal.ReadInt16(entry, 40 + (g * 16));
                        if (group == 0)
                        {
                            cores.Add(new long[] { efficiencyClass, mask });
                        }
                    }

                    offset += size;
                }

                return cores.ToArray();
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
'@
        }

        $ranks = @{}
        foreach ($core in [JevBench.CpuTopology]::Cores()) {
            for ($bit = 0; $bit -lt 64; $bit++) {
                if (([uint64] $core[1] -shr $bit) -band 1) { $ranks[$bit] = [int] $core[0] }
            }
        }

        return $(if ($ranks.Count -eq [Environment]::ProcessorCount) { $ranks } else { $null })
    }

    if ($IsLinux -and (Test-Path /sys/devices/cpu_core/cpus) -and (Test-Path /sys/devices/cpu_atom/cpus)) {
        $ranks = @{}
        foreach ($core in (ConvertFrom-CoreList (Get-Content /sys/devices/cpu_core/cpus).Trim() 'cpu_core')) { $ranks[$core] = 1 }
        foreach ($core in (ConvertFrom-CoreList (Get-Content /sys/devices/cpu_atom/cpus).Trim() 'cpu_atom')) { $ranks[$core] = 0 }
        return $ranks
    }

    return $null
}

$allCores = [int[]] @(0..([Math]::Min([Environment]::ProcessorCount, 64) - 1))
if ($allCores.Count -lt 2) {
    Exit-Usage 'The mock and the clients need separate cores, and this machine has one.'
}

$split = 'override'
if ($null -ne $mockCoresArg -or $null -ne $clientCoresArg) {
    $mockCores = if ($null -ne $mockCoresArg) { ConvertFrom-CoreList $mockCoresArg '--mock-cores' } else { $null }
    $clientCores = if ($null -ne $clientCoresArg) { ConvertFrom-CoreList $clientCoresArg '--client-cores' } else { $null }
    if ($null -eq $mockCores) { $mockCores = [int[]] @($allCores | Where-Object { $_ -notin $clientCores }) }
    if ($null -eq $clientCores) { $clientCores = [int[]] @($allCores | Where-Object { $_ -notin $mockCores }) }
}
else {
    $ranks = Get-CoreRanks
    $distinct = if ($null -ne $ranks) { @($ranks.Values | Sort-Object -Unique) } else { @() }
    if ($distinct.Count -gt 1) {
        $fastest = $distinct[-1]
        $clientCores = [int[]] @($allCores | Where-Object { $ranks[$_] -eq $fastest })
        $mockCores = [int[]] @($allCores | Where-Object { $ranks[$_] -ne $fastest })
        $split = 'hybrid: performance cores for the clients, the other cores for the mock'
    }
    else {
        $half = [int] [Math]::Floor($allCores.Count / 2)
        $mockCores = [int[]] @($allCores[0..($half - 1)])
        $clientCores = [int[]] @($allCores[$half..($allCores.Count - 1)])
        $split = $(if ($null -eq $ranks) { 'even: the core types are unknown' } else { 'even: one core type' })
    }
}

if ($mockCores.Count -eq 0 -or $clientCores.Count -eq 0) {
    Exit-Usage 'The mock and the clients each need at least one core; pass --mock-cores and --client-cores.'
}

if (@($mockCores | Where-Object { $_ -in $clientCores }).Count -gt 0) {
    Exit-Usage '--mock-cores and --client-cores overlap.'
}

$mockList = ConvertTo-CoreList $mockCores
$clientList = ConvertTo-CoreList $clientCores
Write-Host "Cores ($split): mock $mockList, clients $clientList"

# --- Paths --------------------------------------------------------------------------------------------------------

$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$mockProject = Join-Path $root 'benchmarks/ZeroAlloc.Jev.Benchmarks.Mock/ZeroAlloc.Jev.Benchmarks.Mock.csproj'
$mockDll = Join-Path $root 'benchmarks/ZeroAlloc.Jev.Benchmarks.Mock/bin/Release/net10.0/ZeroAlloc.Jev.Benchmarks.Mock.dll'
$compareProject = Join-Path $root 'benchmarks/ZeroAlloc.Jev.Benchmarks.Compare/ZeroAlloc.Jev.Benchmarks.Compare.csproj'
$jsDir = Join-Path $root 'benchmarks/compare-js'
$pyDir = Join-Path $root 'benchmarks/compare-py'
$venvPython = if ($IsWindows) { Join-Path $pyDir '.venv/Scripts/python.exe' } else { Join-Path $pyDir '.venv/bin/python' }
$safeMachine = $machine -replace '[^A-Za-z0-9._-]', '-'
$outDir = Join-Path $PSScriptRoot "results/$safeMachine"
$baseUrl = "http://127.0.0.1:$port"

function Invoke-Checked([string] $what, [scriptblock] $command) {
    Write-Host "==> $what"
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$what failed with exit code $LASTEXITCODE." }
}

# --- Build and set up ---------------------------------------------------------------------------------------------

try {
    Invoke-Checked 'Build the mock' { dotnet build $mockProject -c Release --nologo -v quiet }
    Invoke-Checked 'Build the .NET harness' { dotnet build $compareProject -c Release --nologo -v quiet }
    Invoke-Checked 'Install the Node harness' { Push-Location $jsDir; try { npm ci --no-audit --no-fund } finally { Pop-Location } }
    if (-not (Test-Path $venvPython)) {
        $python = if ($IsWindows) { 'python' } else { 'python3' }
        Invoke-Checked 'Create the Python venv' { & $python -m venv (Join-Path $pyDir '.venv') }
    }

    Invoke-Checked 'Install the Python harness' { & $venvPython -m pip install --quiet --disable-pip-version-check -r (Join-Path $pyDir 'requirements.txt') }
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}

# A previous run's files would be merged as if they were this run's.
New-Item -ItemType Directory -Force $outDir | Out-Null
Get-ChildItem $outDir -Filter '*.json' | Remove-Item -Force

# --- Run ----------------------------------------------------------------------------------------------------------

$failed = [Collections.Generic.List[string]]::new()
$mock = $null
$self = [Diagnostics.Process]::GetCurrentProcess()
$originalAffinity = $self.ProcessorAffinity
try {
    # The mock pins itself with --cores. Its stdin stays open: closing it is how the mock is told to stop.
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    foreach ($a in @($mockDll, '--port', "$port", '--cores', $mockList)) { $start.ArgumentList.Add($a) }
    $start.UseShellExecute = $false
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    Write-Host "==> Start the mock on $baseUrl"
    $mock = [Diagnostics.Process]::Start($start)
    $mockErrors = $mock.StandardError.ReadToEndAsync()
    $line = $mock.StandardOutput.ReadLineAsync()
    if (-not $line.Wait([TimeSpan]::FromSeconds(60)) -or $line.Result -cne 'ready') {
        $mock.WaitForExit(2000) | Out-Null
        $detail = if ($mock.HasExited) { $mockErrors.Result.Trim() } else { 'it did not print ready within 60 s' }
        throw "The mock did not start: $detail"
    }

    # The harnesses and everything they start run on the clients' cores. Node and Python inherit this process's
    # affinity, which also covers the Windows venv launcher's child interpreter. The .NET harness pins itself too.
    if ($IsWindows) {
        $self.ProcessorAffinity = [IntPtr] [int64] (ConvertTo-CoreMask $clientCores)
        $pinPrefix = @()
    }
    elseif (Get-Command taskset -ErrorAction SilentlyContinue) {
        $pinPrefix = @('taskset', '-c', $clientList)
    }
    else {
        Write-Warning 'taskset is missing: Node and Python run unpinned.'
        $pinPrefix = $null
    }

    $common = @('--base-url', $baseUrl, '--out', $outDir, '--machine', $machine, '--mock-cores', $mockList)
    if ($smoke) { $common += '--smoke' }
    $recorded = if ($null -ne $pinPrefix) { @('--cores', $clientList) } else { @() }

    $harnesses = [ordered]@{
        '.NET'   = @('dotnet', 'run', '--no-build', '-c', 'Release', '--project', $compareProject, '--') + $common + @('--cores', $clientList)
        'Node'   = @($pinPrefix) + @('node', (Join-Path $jsDir 'bench.mjs')) + $common + $recorded
        'Python' = @($pinPrefix) + @($venvPython, (Join-Path $pyDir 'bench.py')) + $common + $recorded
    }
    foreach ($name in $harnesses.Keys) {
        $command = @($harnesses[$name] | Where-Object { $null -ne $_ })
        Write-Host "==> Run the $name harness"
        try {
            & $command[0] @($command | Select-Object -Skip 1)
            if ($LASTEXITCODE -ne 0) { throw "exit code $LASTEXITCODE" }
        }
        catch {
            [Console]::Error.WriteLine("The $name harness failed: $($_.Exception.Message)")
            $failed.Add($name)
        }
    }
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    $failed.Add('runner')
}
finally {
    $self.ProcessorAffinity = $originalAffinity
    if ($null -ne $mock) {
        if (-not $mock.HasExited) {
            $mock.StandardInput.Close()
            if (-not $mock.WaitForExit(10000)) {
                [Console]::Error.WriteLine('The mock did not stop after its stdin closed; killing it.')
                $mock.Kill($true)
                $mock.WaitForExit()
            }
        }

        Write-Host "==> The mock stopped (exit code $($mock.ExitCode))"
        $mock.Dispose()
    }
}

if ($failed.Count -gt 0) {
    [Console]::Error.WriteLine("Failed: $($failed -join ', '). The results in $outDir are incomplete.")
    exit 1
}

# --- Merge --------------------------------------------------------------------------------------------------------

$files = @(Get-ChildItem $outDir -Filter '*.json' | Sort-Object Name | ForEach-Object FullName)
# merge.py writes UTF-8; read it as UTF-8, whatever the console's code page.
$consoleEncoding = [Console]::OutputEncoding
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
try {
    $table = & $venvPython (Join-Path $PSScriptRoot 'merge.py') @files --project $project
}
finally {
    [Console]::OutputEncoding = $consoleEncoding
}

if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine('merge.py failed.')
    exit 1
}

$tablePath = Join-Path $outDir 'comparison.md'
[IO.File]::WriteAllText($tablePath, (($table -join "`n") + "`n"))
Write-Host ''
$table | ForEach-Object { Write-Host $_ }
Write-Host ''
Write-Host "Wrote $tablePath"
exit 0
