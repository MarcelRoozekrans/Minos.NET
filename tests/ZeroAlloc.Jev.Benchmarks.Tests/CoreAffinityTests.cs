using System.Diagnostics;
using ZeroAlloc.Jev.Benchmarks.Compare;
using ZeroAlloc.Jev.Benchmarks.Shared;

namespace ZeroAlloc.Jev.Benchmarks.Tests;

/// <summary>The --cores option the mock and the harness share, through the harness's copy of the one source file.</summary>
public sealed class CoreAffinityTests
{
    [Theory]
    [InlineData("0xF0", 0xF0UL)]
    [InlineData("0Xff", 0xFFUL)]
    [InlineData("0x1", 0x1UL)]
    [InlineData("4-7", 0xF0UL)]
    [InlineData("0,2,4-6", 0x75UL)]
    [InlineData(" 1 , 3 ", 0xAUL)]
    [InlineData("63", 0x8000000000000000UL)]
    [InlineData("0-63", ulong.MaxValue)]
    public void Parse_reads_a_mask_or_a_core_list(string text, ulong expected)
    {
        Assert.Equal(expected, CoreAffinity.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0x")]
    [InlineData("0x0")]
    [InlineData("0xZZ")]
    [InlineData("7-4")]
    [InlineData("64")]
    [InlineData("-1")]
    [InlineData("1,,2")]
    [InlineData("a")]
    [InlineData("1-")]
    public void Parse_rejects_a_malformed_or_empty_value(string text)
    {
        Assert.Throws<ArgumentException>(() => CoreAffinity.Parse(text));
    }

    [Theory]
    [InlineData(0x1UL, "0")]
    [InlineData(0xF0UL, "4-7")]
    [InlineData(0x75UL, "0,2,4-6")]
    [InlineData(ulong.MaxValue, "0-63")]
    public void Describe_writes_a_canonical_core_list(ulong mask, string expected)
    {
        Assert.Equal(expected, CoreAffinity.Describe(mask));
        Assert.Equal(mask, CoreAffinity.Parse(expected));
    }

    [Fact]
    public void Apply_rejects_a_core_this_machine_does_not_have()
    {
        // A machine with 64 or more cores has no core a mask can name beyond its own.
        if (Environment.ProcessorCount > CoreAffinity.MaxCoreIndex)
        {
            return;
        }

        var beyond = 1UL << Environment.ProcessorCount;

        Assert.Throws<ArgumentException>(() => CoreAffinity.Apply(beyond));
    }

    [Fact]
    public void Apply_sets_the_process_affinity()
    {
        // Apply pins the process on Windows and Linux only, so the check runs there only.
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            using var process = Process.GetCurrentProcess();
            var original = process.ProcessorAffinity;
            var lowest = (ulong)original & (~(ulong)original + 1);
            try
            {
                // Pin to the lowest core the process already has, then read the affinity back from a fresh snapshot.
                CoreAffinity.Apply(lowest);

                using var pinned = Process.GetCurrentProcess();
                Assert.Equal(lowest, (ulong)pinned.ProcessorAffinity);
            }
            finally
            {
                process.ProcessorAffinity = original;
            }

            using var restored = Process.GetCurrentProcess();
            Assert.Equal(original, restored.ProcessorAffinity);
        }
    }

    [Fact]
    public void The_harness_command_line_takes_cores()
    {
        var options = CompareOptions.Parse(["--base-url", "http://127.0.0.1:5005", "--out", "results", "--cores", "0-3"], "default");

        Assert.Equal(0xFUL, options.Cores);
    }

    [Fact]
    public void The_harness_runs_on_every_core_without_cores()
    {
        var options = CompareOptions.Parse(["--base-url", "http://127.0.0.1:5005", "--out", "results"], "default");

        Assert.Null(options.Cores);
    }

    [Theory]
    [InlineData("--cores")]
    [InlineData("--cores", "x")]
    public void The_harness_command_line_rejects_bad_cores(params string[] cores)
    {
        string[] args = ["--base-url", "http://127.0.0.1:5005", "--out", "results", .. cores];

        Assert.Throws<ArgumentException>(() => CompareOptions.Parse(args, "default"));
    }
}
