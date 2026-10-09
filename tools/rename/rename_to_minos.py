"""Phase 6.1: rename ZeroAlloc.Jev to Minos. Standard library only; run from anywhere inside the repository.

Stages, each run over every tracked text file except the protected ones:
  paths-a            git mv every path containing ZeroAlloc.Jev -> Minos.NET
  paths-b            git mv file names containing renamed type names (JevClient.cs -> DecisionClient.cs)
  a                  project, assembly and namespace names
  b                  type and member names
  c                  analyzer IDs
  publicapi-a        PublicAPI.Unshipped.txt = removed + renamed Shipped lines, through stage a
  publicapi-ab       the same through stages a and b
  analyzer-releases  AnalyzerReleases.Unshipped.md lists MIN rules as new and JEV rules as removed
  report             every C# string literal still containing ZeroAlloc.Jev, for a manual decision
"""
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(subprocess.run(["git", "rev-parse", "--show-toplevel"], capture_output=True, text=True,
                                   check=True).stdout.strip())

# History, measured data, the shipped API and rules, and this tool are never rewritten.
PROTECTED_PATH = re.compile(
    r"^(CHANGELOG\.md|docs/(planning|plans|superpowers)/|benchmarks/compare/results/|tools/rename/)"
    r"|(^|/)(AnalyzerReleases\.Shipped\.md|PublicAPI\.Shipped\.txt)$")
BUILD_SUFFIXES = {".csproj", ".props", ".targets", ".slnx", ".yml", ".yaml", ".ps1", ".sh", ".py", ".json", ".txt"}
PROJECT_SUFFIXES = (r"(?:Generator|Analyzers|CodeFixes|DependencyInjection|AotSmoke|AotSurface|Samples(?:\.[A-Za-z]+)?"
                    r"|Benchmarks(?:\.[A-Za-z]+)?|Tests|Analyzers\.Tests|AotSmoke\.Tests|Benchmarks\.Tests"
                    r"|DependencyInjection\.Tests|Docs\.Tests|Generator\.Tests|Integration\.Tests|Live\.Tests|PackTests"
                    r"|Samples\.Tests)")

_FILE_NAME = re.compile(r"ZeroAlloc\.Jev((?:\.[A-Za-z]+)*)\.(slnx|csproj|dll|nupkg|xml)\b")


def stage_a(text: str, kind: str) -> str:
    text = _FILE_NAME.sub(lambda m: f"Minos.NET{m.group(1)}.{m.group(2)}", text)
    if kind == "build":
        text = re.sub(r"<RootNamespace>ZeroAlloc\.Jev", "<RootNamespace>Minos", text)
        text = re.sub(r'(<Using Include=")ZeroAlloc\.Jev', r"\1Minos", text)
        return text.replace("ZeroAlloc.Jev", "Minos.NET")
    if kind == "md":
        text = re.sub(r"\b(namespace|using)(\s+(?:static\s+)?)ZeroAlloc\.Jev\b", r"\1\2Minos", text)
        text = re.sub(r"\bZeroAlloc\.Jev(?=\.[A-Z])(?!\." + PROJECT_SUFFIXES + r"\b)", "Minos", text)
        return text.replace("ZeroAlloc.Jev", "Minos.NET")
    text = re.sub(r"\b(namespace|using)(\s+(?:static\s+)?)ZeroAlloc\.Jev\b", r"\1\2Minos", text)
    text = text.replace('"ZeroAlloc.Jev/', '"Minos.NET/')
    # A qualified name, in code or in C# source held in a string. An exact project-name literal such as
    # "ZeroAlloc.Jev.Generator" may be an assembly name, so it is left for the report.
    return re.sub(r"\bZeroAlloc\.Jev(?=\.[A-Z])(?!\." + PROJECT_SUFFIXES + r'")', "Minos", text)


TYPE_MAP = [
    ("JevQuestionsAttribute", "QuestionsAttribute"), ("IJevQuestionSet", "IQuestionSet"),
    ("JevQuestionSetBuilder", "QuestionSetBuilder"), ("JevQuestionSet", "QuestionSet"),
    ("JevQuestionFailure", "QuestionFailure"), ("JevQuestions", "Questions"), ("JevQuestion", "Question"),
    ("JevAnswerReader", "AnswerReader"), ("JevAnswers", "Answers"), ("JevAnswer", "Answer"),
    ("JevCriterion", "Criterion"), ("IJevClient", "IDecisionClient"), ("JevClientOptions", "DecisionClientOptions"),
    ("JevClient", "DecisionClient"), ("JevErrorKind", "DecisionErrorKind"), ("JevError", "DecisionError"),
    ("JevContent", "DecisionContent"), ("JevUsage", "DecisionUsage"), ("JevOptionSet", "DecisionOptionSet"),
    ("JevDefaults", "DecisionDefaults"), ("JevProvider", "DecisionProvider"),
    ("JevServiceCollectionExtensions", "DecisionServiceCollectionExtensions"), ("AddJevClient", "AddDecisionClient"),
    ("SetupZeroAllocJevAsync", "SetupMinosAsync"), ("ZeroAllocJev", "Minos"), ("ZeroAlloc_Jev", "Minos"),
    ("JevAdapter", "MinosAdapter"), ("JevRelease", "MinosRelease"),
]
# Third-party clients and TypeSafe's model aliases keep their names.
_PROTECTED_WORD = re.compile(r"\w*(?:JevSharp|JevNet|JevLatest)\w*|Jev\.Net\b")
_MASK = "\x00{}\x00"


def stage_b(text: str) -> str:
    masked: list[str] = []

    def mask(m: re.Match) -> str:
        masked.append(m.group(0))
        return _MASK.format(len(masked) - 1)

    text = _PROTECTED_WORD.sub(mask, text)
    for old, new in TYPE_MAP:
        text = text.replace(old, new)
    # Any other identifier with Jev in it: Jev next to another identifier character, not followed by a digit,
    # which is an analyzer ID for stage c. The bare word Jev, as in "TypeSafe's Jev", is prose and stays.
    text = re.sub(r"(?<=[A-Za-z_])Jev(?![0-9])|Jev(?=[A-Z_s][A-Za-z_]*)", "Decision", text)
    return re.sub(r"\x00(\d+)\x00", lambda m: masked[int(m.group(1))], text)


def stage_c(text: str) -> str:
    text = re.sub(r"\bJEV(\d{3})\b", r"MIN\1", text)
    return re.sub(r"Jev(\d{3})", r"Min\1", text)


def rename_path(path: str, stages: str) -> str:
    if PROTECTED_PATH.search(path):
        return path
    parts = path.split("/")
    if "a" in stages:
        parts = [p.replace("ZeroAlloc.Jev", "Minos.NET") for p in parts]
    if "b" in stages:
        parts = parts[:-1] + [stage_b(parts[-1])]
    return "/".join(parts)


def rebuild_unshipped(shipped: str, transform) -> str:
    lines = [line for line in shipped.splitlines() if line and not line.startswith("#")]
    changed = [line for line in lines if transform(line) != line]
    return "\n".join(["#nullable enable", *sorted(transform(line) for line in changed),
                      *sorted("*REMOVED*" + line for line in changed)]) + "\n"


def analyzer_unshipped(shipped_md: str) -> str:
    live: dict[str, str] = {}
    section = ""
    for line in shipped_md.splitlines():
        if line.startswith("### "):
            section = line
        m = re.match(r"^(JEV\d{3}) \| ([^|]+) \| (\w+) \| (.+)$", line)
        if m:
            if section == "### New Rules":
                live[m.group(1)] = line
            elif section == "### Removed Rules":
                live.pop(m.group(1), None)
    header = "Rule ID | Category | Severity | Notes\n--------|----------|----------|-------"
    new = [re.sub(r"^JEV(\d{3}) \| [^|]+ \|", r"MIN\1 | Minos |", line) for line in live.values()]
    return ("; Unshipped analyzer release.\n"
            "; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md\n\n"
            f"### New Rules\n\n{header}\n" + "\n".join(sorted(new)) +
            f"\n\n### Removed Rules\n\n{header}\n" + "\n".join(sorted(live.values())) + "\n")


def _tracked() -> list[str]:
    out = subprocess.run(["git", "ls-files"], cwd=ROOT, capture_output=True, text=True, check=True).stdout
    return [p for p in out.splitlines() if not PROTECTED_PATH.search(p)]


def _kind(path: str) -> str | None:
    p = pathlib.PurePosixPath(path)
    if p.suffix == ".cs":
        return "cs"
    if p.suffix in (".md", ".mdx"):
        return "md"
    if p.suffix in BUILD_SUFFIXES or p.name in (".editorconfig", "Directory.Build.props", "Directory.Packages.props"):
        return "build"
    return None


def _rewrite(fn, dry: bool) -> None:
    for path in _tracked():
        kind = _kind(path)
        if kind is None:
            continue
        file = ROOT / path
        old = file.read_text(encoding="utf-8")
        new = fn(old, kind)
        if new != old:
            print(f"rewrite {path}")
            if not dry:
                file.write_text(new, encoding="utf-8", newline="")


def _move(stages: str, dry: bool) -> None:
    for path in _tracked():
        target = rename_path(path, stages)
        if target != path:
            print(f"move {path} -> {target}")
            if not dry:
                (ROOT / target).parent.mkdir(parents=True, exist_ok=True)
                subprocess.run(["git", "mv", path, target], cwd=ROOT, check=True)


def main(argv: list[str]) -> int:
    if not argv:
        print(__doc__)
        return 2
    cmd, dry = argv[0], "--dry-run" in argv
    if cmd in ("paths-a", "paths-b"):
        _move(cmd[-1], dry)
    elif cmd == "a":
        _rewrite(stage_a, dry)
    elif cmd == "b":
        _rewrite(lambda t, k: stage_b(t), dry)
    elif cmd == "c":
        _rewrite(lambda t, k: stage_c(t), dry)
    elif cmd in ("publicapi-a", "publicapi-ab"):
        transform = ((lambda line: stage_a(line, "cs")) if cmd == "publicapi-a"
                     else (lambda line: stage_b(stage_a(line, "cs"))))
        for shipped in ROOT.glob("src/*/PublicAPI.Shipped.txt"):
            out = rebuild_unshipped(shipped.read_text(encoding="utf-8"), transform)
            print(f"rewrite {shipped.parent.name}/PublicAPI.Unshipped.txt")
            if not dry:
                (shipped.parent / "PublicAPI.Unshipped.txt").write_text(out, encoding="utf-8", newline="")
    elif cmd == "analyzer-releases":
        for shipped in ROOT.glob("src/*/AnalyzerReleases.Shipped.md"):
            out = analyzer_unshipped(shipped.read_text(encoding="utf-8"))
            print(f"rewrite {shipped.parent.name}/AnalyzerReleases.Unshipped.md")
            if not dry:
                (shipped.parent / "AnalyzerReleases.Unshipped.md").write_text(out, encoding="utf-8", newline="")
    elif cmd == "report":
        for path in _tracked():
            if path.endswith(".cs"):
                for n, line in enumerate((ROOT / path).read_text(encoding="utf-8").splitlines(), 1):
                    for lit in re.findall(r'"[^"\n]*ZeroAlloc\.Jev[^"\n]*"', line):
                        print(f"{path}:{n}: {lit}")
    else:
        print(__doc__)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
