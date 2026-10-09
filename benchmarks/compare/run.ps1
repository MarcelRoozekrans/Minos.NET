#Requires -Version 7
<#
.SYNOPSIS
Runs the client comparison: builds and starts the mock, runs the .NET harness and whichever of the Node and Python
harnesses exist under the bench root, one after another, then stops the mock and merges the results. See README.md next
to this script, and run it with --help for the options.
#>

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$usage = @'
Usage: run.ps1 [options]

  --smoke                 A short run that checks every harness works; its numbers mean nothing.
  --machine <name>        The machine name in the results and their folder. Default: this computer's name.
  --project <name>        The project under test. It names the .NET projects <name>.Benchmarks.Mock and
                          <name>.Benchmarks.Compare under the bench root, and marks the project's rows in the table.
                          Default: Minos.NET.
  --bench-root <dir>      The folder holding those two projects, which are required, and compare-js and compare-py,
                          which are run when present. Default: the benchmarks folder of this repository.
  --results <dir>         Where results go, in a <machine> subfolder. Default: results next to this script.
  --port <n>              The mock's port. Default: 5005.
  --mock-cores <list>     The mock's cores, such as 12-19 or 0-3,8. Default: see the core split in README.md.
  --client-cores <list>   The harnesses' cores. Given one of the two, the other set gets the remaining cores.
  --help                  Prints this text.

Exits with 0 on success, 1 if a build or a harness failed or the mock did not start, and 2 on a wrong command line.
'@

function Exit-Usage([string] $message) {
    [Console]::Error.WriteLine($message)
    [Console]::Error.WriteLine($usage)
    exit 2
}

# --- Command line -------------------------------------------------------------------------------------------------

$smoke = $false
$machine = [Environment]::MachineName
$project = 'Minos.NET'
$benchRoot = Join-Path $PSScriptRoot '..'
$resultsRoot = Join-Path $PSScriptRoot 'results'
$port = 5005
$coreArgs = [Collections.Generic.List[string]]::new()
$valued = '--machine', '--project', '--bench-root', '--results', '--port', '--mock-cores', '--client-cores'
for ($i = 0; $i -lt $args.Count; $i++) {
    $arg = [string] $args[$i]
    if ($arg -ceq '--smoke') { $smoke = $true; continue }
    if ($arg -ceq '--help') { Write-Host $usage; exit 0 }
    if ($arg -cnotin $valued) { Exit-Usage "Unknown argument: $arg" }
    if ($i + 1 -ge $args.Count) { Exit-Usage "$arg needs a value." }

    $value = [string] $args[++$i]
    switch -CaseSensitive ($arg) {
        '--machine' { $machine = $value }
        '--project' { $project = $value }
        '--bench-root' { $benchRoot = $value }
        '--results' { $resultsRoot = $value }
        '--port' {
            $parsed = 0
            if (-not [int]::TryParse($value, [ref] $parsed) -or $parsed -lt 1 -or $parsed -gt 65535) {
                Exit-Usage '--port must be a number from 1 to 65535.'
            }

            $port = $parsed
        }
        default { $coreArgs.Add($arg); $coreArgs.Add($value) }
    }
}

if ([string]::IsNullOrWhiteSpace($machine)) { Exit-Usage '--machine must not be blank.' }
if ([string]::IsNullOrWhiteSpace($project)) { Exit-Usage '--project must not be blank.' }
$machine = $machine.Trim()
$project = $project.Trim()
$benchRoot = [IO.Path]::GetFullPath($benchRoot, $PWD.Path)
$resultsRoot = [IO.Path]::GetFullPath($resultsRoot, $PWD.Path)

# --- Paths --------------------------------------------------------------------------------------------------------

$mockName = "$project.Benchmarks.Mock"
$compareName = "$project.Benchmarks.Compare"
$mockProject = Join-Path $benchRoot "$mockName/$mockName.csproj"
$compareProject = Join-Path $benchRoot "$compareName/$compareName.csproj"
$jsDir = Join-Path $benchRoot 'compare-js'
$pyDir = Join-Path $benchRoot 'compare-py'

# The mock and the .NET harness are the comparison; the Node and Python harnesses are optional, since most projects have
# no JS or Python counterpart to compare against.
foreach ($path in $mockProject, $compareProject) {
    if (-not (Test-Path $path)) { Exit-Usage "Not found: $path. Check --project and --bench-root." }
}

$hasJs = Test-Path (Join-Path $jsDir 'bench.mjs')
$hasPy = Test-Path (Join-Path $pyDir 'bench.py')
if (-not $hasJs) { Write-Host "Skipping the Node harness: no $(Join-Path $jsDir 'bench.mjs')." }
if (-not $hasPy) { Write-Host "Skipping the Python harness: no $(Join-Path $pyDir 'bench.py')." }

$venvPython = if ($IsWindows) { Join-Path $pyDir '.venv/Scripts/python.exe' } else { Join-Path $pyDir '.venv/bin/python' }
$safeMachine = $machine -replace '[^A-Za-z0-9._-]', '-'
$outDir = Join-Path $resultsRoot $safeMachine
$baseUrl = "http://127.0.0.1:$port"

# --- Python, for the core split, the harness venv and the merge ---------------------------------------------------

$python = $null
foreach ($candidate in $(if ($IsWindows) { 'python', 'python3' } else { 'python3', 'python' })) {
    if (Get-Command $candidate -ErrorAction SilentlyContinue) {
        & $candidate -c 'import sys; sys.exit(sys.version_info < (3, 10))' 2>$null
        if ($LASTEXITCODE -eq 0) { $python = $candidate; break }
    }
}

if ($null -eq $python) {
    [Console]::Error.WriteLine('Python 3.10 or later is needed.')
    exit 1
}

# --- Core split ---------------------------------------------------------------------------------------------------

# The physical cores, from GetLogicalProcessorInformationEx's RelationProcessorCore entries: each logical core's
# EfficiencyClass, higher being faster, in core order, for cores.py's --ranks; and each physical core's logical cores,
# for --siblings, so the split never puts two threads of one core on different sides. Either is $null when the entries
# do not cover every logical core. Elsewhere cores.py reads sysfs itself.
function Get-CoreTopology {
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

    $count = [Math]::Min([Environment]::ProcessorCount, 64)
    $ranks = [int[]]::new($count)
    $groups = [Collections.Generic.List[string]]::new()
    $seen = 0
    foreach ($core in [JevBench.CpuTopology]::Cores()) {
        $threads = [Collections.Generic.List[int]]::new()
        for ($bit = 0; $bit -lt $count; $bit++) {
            if (([uint64] $core[1] -shr $bit) -band 1) { $ranks[$bit] = [int] $core[0]; $threads.Add($bit); $seen++ }
        }

        if ($threads.Count -gt 0) { $groups.Add($threads -join ',') }
    }

    if ($seen -ne $count) { return @{ Ranks = $null; Siblings = $null } }
    return @{ Ranks = $ranks -join ','; Siblings = $groups -join ';' }
}

$splitArgs = @((Join-Path $PSScriptRoot 'cores.py'), '--count', [Environment]::ProcessorCount) + $coreArgs
if ($IsWindows) {
    $topology = Get-CoreTopology
    if ($null -ne $topology.Ranks -and $coreArgs.Count -eq 0) { $splitArgs += '--ranks', $topology.Ranks }
    if ($null -ne $topology.Siblings) { $splitArgs += '--siblings', $topology.Siblings }
}

$splitLines = & $python @splitArgs
if ($LASTEXITCODE -ne 0) { Exit-Usage 'The core split failed; see the message above.' }
$split = @{}
foreach ($line in $splitLines) {
    $key, $value = $line -split '=', 2
    $split[$key] = $value
}

$mockList = $split['mock']
$clientList = $split['clients']
Write-Host "Cores ($($split['split'])): mock $mockList, clients $clientList; physical cores $($split['siblings'])"

function Invoke-Checked([string] $what, [scriptblock] $command) {
    Write-Host "==> $what"
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$what failed with exit code $LASTEXITCODE." }
}

# --- Build and set up ---------------------------------------------------------------------------------------------

try {
    Invoke-Checked 'Build the mock' { dotnet build $mockProject -c Release --nologo -v quiet }
    Invoke-Checked 'Build the .NET harness' { dotnet build $compareProject -c Release --nologo -v quiet }
    if ($hasJs) {
        Invoke-Checked 'Install the Node harness' { Push-Location $jsDir; try { npm ci --no-audit --no-fund } finally { Pop-Location } }
    }

    if ($hasPy) {
        if (-not (Test-Path $venvPython)) {
            Invoke-Checked 'Create the Python venv' { & $python -m venv (Join-Path $pyDir '.venv') }
        }

        Invoke-Checked 'Install the Python harness' { & $venvPython -m pip install --quiet --disable-pip-version-check -r (Join-Path $pyDir 'requirements.txt') }
    }
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}

# The mock's own build output, whatever framework it targets.
$mockDll = @(Get-ChildItem (Join-Path $benchRoot "$mockName/bin/Release") -Recurse -Filter "$mockName.dll" -ErrorAction SilentlyContinue |
    Where-Object { Test-Path (Join-Path $_.DirectoryName "$mockName.runtimeconfig.json") })
if ($mockDll.Count -ne 1) {
    [Console]::Error.WriteLine("Expected one built $mockName.dll under bin/Release, found $($mockDll.Count).")
    exit 1
}

$mockDll = $mockDll[0].FullName

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
        $mock.WaitForExit(5000) | Out-Null
        if ($mock.HasExited -and $mock.ExitCode -eq 3) {
            throw "The mock did not start: port $port is already in use. Stop whatever holds it, or pass --port."
        }

        $detail = if ($mock.HasExited) { "exit code $($mock.ExitCode): $($mockErrors.Result.Trim())" } else { 'it did not print ready within 60 s' }
        throw "The mock did not start: $detail"
    }

    # The harnesses and everything they start run on the clients' cores. Node and Python inherit this process's
    # affinity, which also covers the Windows venv launcher's child interpreter. The .NET harness pins itself too.
    # clientmask64 is the mask as a signed 64-bit number, so core 63 does not overflow the conversion.
    if ($IsWindows) {
        $self.ProcessorAffinity = [IntPtr] [int64]::Parse($split['clientmask64'], [Globalization.CultureInfo]::InvariantCulture)
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
        '.NET' = @('dotnet', 'run', '--no-build', '-c', 'Release', '--project', $compareProject, '--') + $common + @('--cores', $clientList)
    }
    if ($hasJs) { $harnesses['Node'] = @($pinPrefix) + @('node', (Join-Path $jsDir 'bench.mjs')) + $common + $recorded }
    if ($hasPy) { $harnesses['Python'] = @($pinPrefix) + @($venvPython, (Join-Path $pyDir 'bench.py')) + $common + $recorded }
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
    $table = & $python (Join-Path $PSScriptRoot 'merge.py') @files --project $project
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
