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
  report             every C# line still containing ZeroAlloc.Jev, literal or not, for a manual decision
"""
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(subprocess.run(["git", "rev-parse", "--show-toplevel"], capture_output=True, text=True,
                                   check=True).stdout.strip())

# History, measured data, the shipped API and rules, and this tool are never rewritten.
# True history and measured data: never moved and never rewritten.
HISTORY_PATH = re.compile(r"^(CHANGELOG\.md|docs/(planning|plans|superpowers)/|benchmarks/compare/results/|tools/rename/)")
# The shipped API and rules: moved with their folder, but their contents are never rewritten.
SHIPPED_PATH = re.compile(r"(^|/)(AnalyzerReleases\.Shipped\.md|PublicAPI\.Shipped\.txt)$")
PROTECTED_PATH = re.compile(HISTORY_PATH.pattern + "|" + SHIPPED_PATH.pattern)
BUILD_SUFFIXES = {".csproj", ".props", ".targets", ".slnx", ".yml", ".yaml", ".ps1", ".sh", ".py", ".json", ".txt"}
# Project name suffixes, longest first so Analyzers.Tests wins over Analyzers.
PROJECT_SUFFIXES = (r"(?:Analyzers\.Tests|AotSmoke\.Tests|Benchmarks\.Tests|DependencyInjection\.Tests|Docs\.Tests"
                    r"|Generator\.Tests|Integration\.Tests|Live\.Tests|Samples\.Tests|PackTests|DependencyInjection"
                    r"|Samples(?:\.[A-Za-z]+)?|Benchmarks(?:\.[A-Za-z]+)?|CodeFixes|AotSurface|AotSmoke|Analyzers"
                    r"|Generator|Tests)")

_FILE_NAME = re.compile(r"ZeroAlloc\.Jev((?:\.[A-Za-z]+)*)\.(slnx|csproj|dll|nupkg|xml)\b")


_REPO_URL = re.compile(r"ZeroAlloc-Net/ZeroAlloc\.Jev(?![A-Za-z0-9-])")
_ALIAS = re.compile(r"(\busing\s+\w+\s*=\s*)ZeroAlloc\.Jev\b")
_HTTP_CLIENT = re.compile(r"\bHttpClient\.ZeroAlloc\.Jev\b")
_PROJECT_NAME = re.compile(r"\bZeroAlloc\.Jev\.(" + PROJECT_SUFFIXES + r")(?![A-Za-z0-9_])")


def _project_or_namespace(text: str) -> str:
    """ZeroAlloc.Jev.<Project> followed by a dot and an uppercase letter is a namespace, so Minos.<Project>.
    Anything else names a project, package, path or assembly, so Minos.NET.<Project>."""
    def swap(m: re.Match) -> str:
        namespace = re.match(r"\.[A-Z]", text[m.end():m.end() + 2]) is not None
        return ("Minos." if namespace else "Minos.NET.") + m.group(1)
    text = _PROJECT_NAME.sub(swap, text)
    # Any other qualified name, in code or in C# source held in a string, is a namespace.
    return re.sub(r"\bZeroAlloc\.Jev(?=\.[A-Z])", "Minos", text)


def stage_a(text: str, kind: str) -> str:
    # The repository moves out of the ZeroAlloc-Net organisation, so its URL is not a project rename.
    text = _REPO_URL.sub("MarcelRoozekrans/Minos.NET", text)
    text = _ALIAS.sub(r"\1Minos", text)
    text = _HTTP_CLIENT.sub("HttpClient.Minos.NET", text)  # the logger category of the named HttpClient
    text = _FILE_NAME.sub(lambda m: f"Minos.NET{m.group(1)}.{m.group(2)}", text)
    if kind == "build":
        text = re.sub(r"<RootNamespace>ZeroAlloc\.Jev", "<RootNamespace>Minos", text)
        text = re.sub(r'(<Using Include=")ZeroAlloc\.Jev', r"\1Minos", text)
        return text.replace("ZeroAlloc.Jev", "Minos.NET")
    if kind == "md":
        text = re.sub(r"(?:\b|(?<=\\[nrt]))(namespace|using)(\s+(?:static\s+)?)ZeroAlloc\.Jev\b", r"\1\2Minos", text)
        text = _project_or_namespace(text)
        return text.replace("ZeroAlloc.Jev", "Minos.NET")
    text = re.sub(r"(?:\b|(?<=\\[nrt]))(namespace|using)(\s+(?:static\s+)?)ZeroAlloc\.Jev\b", r"\1\2Minos", text)
    text = text.replace('"ZeroAlloc.Jev/', '"Minos.NET/')
    return _project_or_namespace(text)


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
    ("JevCheckReleaseVersion", "MinosCheckReleaseVersion"), ("JevLocalVersion", "MinosLocalVersion"),
]
# Project-owned names that are safe even in a file that also uses a third-party Jev library.
_SAFE_IN_THIRD_PARTY = {"SetupZeroAllocJevAsync", "ZeroAllocJev", "ZeroAlloc_Jev", "JevAdapter"}
# A file that imports or qualifies JevSharp or Jev.Net names types we must not rename.
_THIRD_PARTY = re.compile(r"\bJevSharp\.[A-Z]|\busing\s+(?:[\w.]+\s*=\s*)?Jev\.Net\b|\bJev\.Net\.[A-Z]")
# Third-party clients and TypeSafe's model aliases keep their names.
# Configuration keys such as Jev__ApiKey and Jev:ApiKey keep their section name.
_PROTECTED_WORD = re.compile(r"\w*(?:JevSharp|JevNet|JevLatest)\w*|Jev\.Net\b|Jev__\w*|Jev:\w*")
_PROTECTED_SLUG = re.compile(r"\w*(?:jevsharp|jevnet|jevlatest)\w*|jev__\w*")
_MASK = "\x00{}\x00"


def is_third_party(text: str) -> bool:
    return bool(_THIRD_PARTY.search(text))


def _apply_b(text: str, protected: re.Pattern, type_map, generic: str, lower: bool) -> str:
    masked: list[str] = []

    def mask(m: re.Match) -> str:
        masked.append(m.group(0))
        return _MASK.format(len(masked) - 1)

    text = protected.sub(mask, text)
    for old, new in type_map:
        text = text.replace(old, new)
    # Any other identifier with Jev in it: Jev next to another identifier character, not followed by a digit,
    # which is an analyzer ID for stage c. The bare word Jev, as in "TypeSafe's Jev", is prose and stays.
    text = re.sub(generic, "decision" if lower else "Decision", text)
    return re.sub(r"\x00(\d+)\x00", lambda m: masked[int(m.group(1))], text)


_GENERIC = r"(?<=[A-Za-z_])Jev(?![0-9])|Jev(?=[A-Z_s][A-Za-z_]*)"
_GENERIC_SLUG = r"(?<=[a-z_])jev(?![0-9])|jev(?=[a-z_]+)"
_SLUG_MAP = sorted(((o.lower(), n.lower()) for o, n in TYPE_MAP), key=lambda t: -len(t[0]))


def stage_b(text: str) -> str:
    return _apply_b(text, _PROTECTED_WORD, TYPE_MAP, _GENERIC, False)


def stage_b_file(text: str) -> str:
    """Stage b for a C# file: a file using JevSharp or Jev.Net types gets only the project-owned safe renames."""
    if is_third_party(text):
        safe = [(o, n) for o, n in TYPE_MAP if o in _SAFE_IN_THIRD_PARTY]
        return _apply_b(text, _PROTECTED_WORD, safe, r"(?!)", False)
    return stage_b(text)


def _slug_b(slug: str) -> str:
    return _apply_b(slug, _PROTECTED_SLUG, _SLUG_MAP, _GENERIC_SLUG, True)


_LINK_TARGET = re.compile(r"(\]\([^)\s#]*#)([^)\s]+)(?=\))")


def stage_b_anchors(text: str) -> str:
    """Markdown link anchors follow the renamed headings: (#jevcontent) becomes (#decisioncontent)."""
    return _LINK_TARGET.sub(lambda m: m.group(1) + _slug_b(m.group(2)), text)


def stage_c(text: str) -> str:
    text = re.sub(r"\bJEV(\d{3})\b", r"MIN\1", text)
    text = re.sub(r"(?<=#)jev(\d{3})\b", r"min\1", text)
    return re.sub(r"Jev(\d{3})", r"Min\1", text)


def rename_path(path: str, stages: str) -> str:
    if HISTORY_PATH.search(path):
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


def analyzer_unshipped(shipped_md: str) -> str | None:
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
    if not live:
        return None
    header = "Rule ID | Category | Severity | Notes\n--------|----------|----------|-------"
    new = [re.sub(r"^JEV(\d{3}) \| [^|]+ \|", r"MIN\1 | Minos |", line) for line in live.values()]
    return ("; Unshipped analyzer release.\n"
            "; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md\n\n"
            f"### New Rules\n\n{header}\n" + "\n".join(sorted(new)) +
            f"\n\n### Removed Rules\n\n{header}\n" + "\n".join(sorted(live.values())) + "\n")


def report_lines(text: str) -> list[tuple[int, str]]:
    """Every line still holding ZeroAlloc.Jev, in a literal, a raw string or code, for a manual decision."""
    return [(n, line.strip()) for n, line in enumerate(text.splitlines(), 1) if "ZeroAlloc.Jev" in line]


def _tracked(for_content: bool = True) -> list[str]:
    """Tracked paths. Moves list everything but history; rewrites also skip the shipped API and rules files."""
    out = subprocess.run(["git", "ls-files"], cwd=ROOT, capture_output=True, text=True, check=True).stdout
    skip = PROTECTED_PATH if for_content else HISTORY_PATH
    return [p for p in out.splitlines() if not skip.search(p)]


def _kind(path: str) -> str | None:
    p = pathlib.PurePosixPath(path)
    if SHIPPED_PATH.search(path):
        return None  # shipped history is never rewritten
    if p.name in ("PublicAPI.Unshipped.txt", "AnalyzerReleases.Unshipped.md"):
        return None  # regenerated by publicapi-* and analyzer-releases
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


def _rewrite_b(dry: bool) -> None:
    for path in _tracked():
        kind = _kind(path)
        if kind is None:
            continue
        file = ROOT / path
        old = file.read_text(encoding="utf-8")
        if kind == "cs":
            if is_third_party(old):
                print(f"skip-b {path} (third-party types)")
            new = stage_b_file(old)
        elif kind == "md":
            new = stage_b_anchors(stage_b(old))
        else:
            new = stage_b(old)
        if new != old:
            print(f"rewrite {path}")
            if not dry:
                file.write_text(new, encoding="utf-8", newline="")


def _move(stages: str, dry: bool) -> None:
    for path in _tracked(for_content=False):
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
        _rewrite_b(dry)
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
            if out is None:
                print(f"skip {shipped.parent.name}: no live rules")
                continue
            print(f"rewrite {shipped.parent.name}/AnalyzerReleases.Unshipped.md")
            if not dry:
                (shipped.parent / "AnalyzerReleases.Unshipped.md").write_text(out, encoding="utf-8", newline="")
    elif cmd == "report":
        for path in _tracked():
            if path.endswith(".cs"):
                for n, line in report_lines((ROOT / path).read_text(encoding="utf-8")):
                    print(f"{path}:{n}: {line}")
    else:
        print(__doc__)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
