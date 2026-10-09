"""Tests for the Phase 6.1 rename tool. Run: python -m unittest discover -s tools/rename -v"""
import unittest

import rename_to_minos as r


class StageA(unittest.TestCase):
    def test_namespace_and_using_become_minos(self):
        self.assertEqual("namespace Minos.Transport;", r.stage_a("namespace ZeroAlloc.Jev.Transport;", "cs"))
        self.assertEqual("using Minos;\nusing static Minos.X;", r.stage_a("using ZeroAlloc.Jev;\nusing static ZeroAlloc.Jev.X;", "cs"))

    def test_qualified_type_in_code_and_embedded_source_becomes_minos(self):
        self.assertEqual('var t = typeof(Minos.Noul);', r.stage_a('var t = typeof(ZeroAlloc.Jev.Noul);', "cs"))
        self.assertEqual('"[Minos.Choice(\\"x\\")]"', r.stage_a('"[ZeroAlloc.Jev.Choice(\\"x\\")]"', "cs"))
        self.assertEqual('"Minos.JevQuestionsAttribute"', r.stage_a('"ZeroAlloc.Jev.JevQuestionsAttribute"', "cs"))

    def test_files_and_paths_become_minos_dot_net(self):
        self.assertEqual('"Minos.NET.slnx"', r.stage_a('"ZeroAlloc.Jev.slnx"', "cs"))
        self.assertEqual('"lib/net10.0/Minos.NET.DependencyInjection.dll"', r.stage_a('"lib/net10.0/ZeroAlloc.Jev.DependencyInjection.dll"', "cs"))
        self.assertEqual('"Minos.NET/1.0"', r.stage_a('"ZeroAlloc.Jev/1.0"', "cs"))

    def test_exact_project_name_literals_and_bare_literals_are_left_for_the_report(self):
        for literal in ['"ZeroAlloc.Jev"', '"ZeroAlloc.Jev.Generator"', '"ZeroAlloc.Jev:openrouter"', '"ZeroAlloc.Jev\'s mean"']:
            self.assertEqual(literal, r.stage_a(literal, "cs"))

    def test_build_files_use_minos_dot_net_but_root_namespace_and_usings_use_minos(self):
        self.assertEqual('<ProjectReference Include="..\\..\\src\\Minos.NET\\Minos.NET.csproj" />',
                         r.stage_a('<ProjectReference Include="..\\..\\src\\ZeroAlloc.Jev\\ZeroAlloc.Jev.csproj" />', "build"))
        self.assertEqual("<RootNamespace>Minos.Generator</RootNamespace>", r.stage_a("<RootNamespace>ZeroAlloc.Jev.Generator</RootNamespace>", "build"))
        self.assertEqual('<Using Include="Minos.Tests" />', r.stage_a('<Using Include="ZeroAlloc.Jev.Tests" />', "build"))
        self.assertEqual('<InternalsVisibleTo Include="Minos.NET.Tests" />', r.stage_a('<InternalsVisibleTo Include="ZeroAlloc.Jev.Tests" />', "build"))

    def test_markdown_code_uses_minos_and_package_names_use_minos_dot_net(self):
        self.assertEqual("using Minos;", r.stage_a("using ZeroAlloc.Jev;", "md"))
        self.assertEqual("`Minos.Noul`", r.stage_a("`ZeroAlloc.Jev.Noul`", "md"))
        self.assertEqual("dotnet add package Minos.NET.DependencyInjection", r.stage_a("dotnet add package ZeroAlloc.Jev.DependencyInjection", "md"))


class StageB(unittest.TestCase):
    def test_type_map(self):
        cases = {
            "[JevQuestions]": "[Questions]", "JevQuestionsAttribute": "QuestionsAttribute",
            "IJevQuestionSet": "IQuestionSet", "JevQuestionSetBuilder": "QuestionSetBuilder", "JevQuestionSet": "QuestionSet",
            "JevQuestionFailure": "QuestionFailure", "JevQuestion": "Question",
            "JevAnswerReader": "AnswerReader", "JevAnswers": "Answers", "JevAnswer": "Answer", "JevCriterion": "Criterion",
            "IJevClient": "IDecisionClient", "JevClientOptions": "DecisionClientOptions", "JevClient": "DecisionClient",
            "JevErrorKind": "DecisionErrorKind", "JevError": "DecisionError", "JevContent": "DecisionContent",
            "JevUsage": "DecisionUsage", "JevOptionSet": "DecisionOptionSet", "JevDefaults": "DecisionDefaults",
            "JevProvider": "DecisionProvider", "JevServiceCollectionExtensions": "DecisionServiceCollectionExtensions",
            "AddJevClient": "AddDecisionClient", "JevAdapter": "MinosAdapter", "SetupZeroAllocJevAsync": "SetupMinosAsync",
            "JevRelease": "MinosRelease",
        }
        for old, new in cases.items():
            self.assertEqual(new, r.stage_b(old), old)

    def test_identifiers_built_on_mapped_types_follow(self):
        self.assertEqual("IDecisionClientChecks DecisionClientTests QuestionSetBuilderTests",
                         r.stage_b("IJevClientChecks JevClientTests JevQuestionSetBuilderTests"))
        self.assertEqual("x.Questions.g.cs", r.stage_b("x.JevQuestions.g.cs"))

    def test_other_identifiers_take_decision(self):
        self.assertEqual("DecisionLog LoggingDecisionApi IDecisionApi ScriptedDecision __DecisionOptions_Team",
                         r.stage_b("JevLog LoggingJevApi IJevApi ScriptedJev __JevOptions_Team"))

    def test_protected_names_are_untouched(self):
        for text in ["JevSharpClient", "SetupJevSharpAsync", "JevNetAdapter", "Jev.Net 0.4.0", "Model_DefaultsToJevLatest",
                     "jev-latest", "jev-preview", "TypeSafe's Jev model", "a Jev span", "Every_client_but_JevSharp_sends"]:
            self.assertEqual(text, r.stage_b(text), text)

    def test_analyzer_id_identifiers_are_left_to_stage_c(self):
        self.assertEqual("FailsJev106_Once", r.stage_b("FailsJev106_Once"))


class StageC(unittest.TestCase):
    def test_ids(self):
        self.assertEqual("MIN001 MIN107 FailsMin106_Once Min101StubbableSources",
                         r.stage_c("JEV001 JEV107 FailsJev106_Once Jev101StubbableSources"))

    def test_other_text_untouched(self):
        self.assertEqual("JEVX jev-latest", r.stage_c("JEVX jev-latest"))


class Paths(unittest.TestCase):
    def test_paths(self):
        self.assertEqual("src/Minos.NET.Generator/Minos.NET.Generator.csproj",
                         r.rename_path("src/ZeroAlloc.Jev.Generator/ZeroAlloc.Jev.Generator.csproj", "a"))
        self.assertEqual("src/Minos.NET/DecisionClient.cs", r.rename_path("src/Minos.NET/JevClient.cs", "b"))
        self.assertEqual("Snapshots/G.T#Demo.X.Questions.g.verified.cs", r.rename_path("Snapshots/G.T#Demo.X.JevQuestions.g.verified.cs", "b"))
        self.assertEqual("benchmarks/compare/results/ci-run-1.json", r.rename_path("benchmarks/compare/results/ci-run-1.json", "ab"))


class ApiFiles(unittest.TestCase):
    def test_rebuild_unshipped_removes_old_and_adds_new(self):
        shipped = "#nullable enable\nZeroAlloc.Jev.JevClient.Dispose() -> void\nZeroAlloc.Jev.Noul.Probability.get -> double\n"
        out = r.rebuild_unshipped(shipped, lambda line: r.stage_b(r.stage_a(line, "cs")))
        lines = out.splitlines()
        self.assertEqual("#nullable enable", lines[0])
        self.assertIn("Minos.DecisionClient.Dispose() -> void", lines)
        self.assertIn("Minos.Noul.Probability.get -> double", lines)
        self.assertIn("*REMOVED*ZeroAlloc.Jev.JevClient.Dispose() -> void", lines)
        self.assertIn("*REMOVED*ZeroAlloc.Jev.Noul.Probability.get -> double", lines)

    def test_analyzer_unshipped_lists_live_rules_as_new_min_and_removed_jev(self):
        shipped = ("## Release 0.1.0\n\n### New Rules\n\nRule ID | Category | Severity | Notes\n--------|----------|----------|-------\n"
                   "JEV001 | ZeroAlloc.Jev | Error | Diagnostics\nJEV003 | ZeroAlloc.Jev | Warning | Diagnostics\n"
                   "JEV108 | ZeroAlloc.Jev | Error | Diagnostics\n\n## Release 0.4.0\n\n### Removed Rules\n\n"
                   "Rule ID | Category | Severity | Notes\n--------|----------|----------|-------\nJEV108 | ZeroAlloc.Jev | Error | Diagnostics\n")
        out = r.analyzer_unshipped(shipped)
        new = out.split("### New Rules")[1].split("### Removed Rules")[0]
        removed = out.split("### Removed Rules")[1]
        self.assertIn("MIN001 | Minos | Error | Diagnostics", new)
        self.assertIn("MIN003 | Minos | Warning | Diagnostics", new)
        self.assertNotIn("MIN108", out)
        self.assertIn("JEV001 | ZeroAlloc.Jev | Error | Diagnostics", removed)
        self.assertNotIn("JEV108", removed)


class FixRound1(unittest.TestCase):
    JEVSHARP_ADAPTER = (
        "using JevSharpQuestion = JevSharp.Abstractions.Requests.JevQuestion;\n"
        "using JevSharpClient = JevSharp.Core.Clients.JevClient;\n"
        "using JevSharpOptions = JevSharp.Core.Configuration.JevClientOptions;\n"
        "namespace Minos.Benchmarks.Compare.Adapters;\n"
        "var options = new JevSharpOptions().UseCustom(JevProtocol.TypeSafe);\n"
        "var criteria = new Dictionary<string, JevValue>(StringComparer.Ordinal);\n"
        "_request = new JevRequest(null);\n")
    JEVNET_ADAPTER = "using Jev.Net;\nusing JevNetClient = Jev.Net.TypeSafeClient;\nvar o = new JevNetClient();\n"

    def test_third_party_files_keep_their_types(self):
        self.assertTrue(r.is_third_party(self.JEVSHARP_ADAPTER))
        self.assertTrue(r.is_third_party(self.JEVNET_ADAPTER))
        self.assertEqual(self.JEVSHARP_ADAPTER, r.stage_b_file(self.JEVSHARP_ADAPTER))
        self.assertEqual(self.JEVNET_ADAPTER, r.stage_b_file(self.JEVNET_ADAPTER))

    def test_third_party_files_still_take_the_safe_project_renames(self):
        self.assertEqual(self.JEVSHARP_ADAPTER + "Minos.Setup;\n",
                         r.stage_b_file(self.JEVSHARP_ADAPTER + "ZeroAllocJev.Setup;\n"))

    def test_own_files_get_the_full_stage_b(self):
        self.assertFalse(r.is_third_party("var c = new JevClient();"))
        self.assertEqual("var c = new DecisionClient();", r.stage_b_file("var c = new JevClient();"))

    def test_configuration_keys_keep_their_section_name(self):
        text = "The setting is `Jev:ApiKey`, as an environment variable `Jev__ApiKey`, GetSection(\"Jev\")."
        self.assertEqual(text, r.stage_b(text))

    def test_alias_usings_follow_the_namespace(self):
        self.assertEqual("using Jev = Minos;", r.stage_a("using Jev = ZeroAlloc.Jev;", "cs"))
        self.assertEqual("using J = Minos.Noul;", r.stage_a("using J = ZeroAlloc.Jev.Noul;", "cs"))
        self.assertEqual("using Jev = Minos;", r.stage_a("using Jev = ZeroAlloc.Jev;", "md"))

    def test_report_lists_every_remaining_line_including_raw_strings(self):
        source = ('public void F() => V(\n[\n"""\n    using Jev = ZeroAlloc.Jev;\n    public class Q { }\n"""\n],\n'
                  'x);\nvar n = "ZeroAlloc.Jev.Generator";\nvar ok = 1;\n')
        self.assertEqual([(4, "using Jev = ZeroAlloc.Jev;"), (9, 'var n = "ZeroAlloc.Jev.Generator";')],
                         r.report_lines(source))

    def test_markdown_anchors_follow_the_renamed_headings(self):
        cases = {
            "[fake `IJevClient`](testing-your-code.md#way-one-a-fake-ijevclient)":
                "[fake `IJevClient`](testing-your-code.md#way-one-a-fake-idecisionclient)",
            "[typed](typed-evaluation.md#jevcontent)": "[typed](typed-evaluation.md#decisioncontent)",
            "a [`JevContent`](#jevcontent) as": "a [`JevContent`](#decisioncontent) as",
            "[x](testing-your-code.md#way-two-a-real-jevclient-over-a-canned-http-reply)":
                "[x](testing-your-code.md#way-two-a-real-decisionclient-over-a-canned-http-reply)",
            "[x](a.md#jevoddname)": "[x](a.md#decisionoddname)",
        }
        for old, new in cases.items():
            self.assertEqual(new, r.stage_b_anchors(old), old)

    def test_markdown_anchors_keep_prose_and_protected_slugs(self):
        for text in ["[p](dependency-injection.md#using-jev-without-the-package)", "[m](a.md#model-defaults-to-jev-latest)",
                     "[s](a.md#jevsharp-client)", "[k](a.md#jev__apikey)", "[no anchor](page.md)"]:
            self.assertEqual(text, r.stage_b_anchors(text), text)

    def test_diagnostic_anchors_follow_stage_c(self):
        self.assertEqual("[d](diagnostics.md#min001-title)", r.stage_c("[d](diagnostics.md#jev001-title)"))

    def test_no_rules_means_no_unshipped_file(self):
        shipped = "## Release 0.1.0\n\n### New Rules\n\nRule ID | Category | Severity | Notes\n--------|----------|----------|-------\n"
        self.assertIsNone(r.analyzer_unshipped(shipped))
        removed_all = (shipped + "JEV001 | ZeroAlloc.Jev | Error | Diagnostics\n\n## Release 0.2.0\n\n### Removed Rules\n\n"
                       "Rule ID | Category | Severity | Notes\n--------|----------|----------|-------\n"
                       "JEV001 | ZeroAlloc.Jev | Error | Diagnostics\n")
        self.assertIsNone(r.analyzer_unshipped(removed_all))

    def test_regenerated_files_are_not_rewritten(self):
        self.assertIsNone(r._kind("src/Minos.NET/PublicAPI.Unshipped.txt"))
        self.assertIsNone(r._kind("src/Minos.NET.Analyzers/AnalyzerReleases.Unshipped.md"))
        self.assertEqual("build", r._kind("src/Minos.NET/other.txt"))

    def test_msbuild_properties_share_one_prefix(self):
        self.assertEqual("$(MinosReleaseVersion) $(MinosCheckReleaseVersion) $(MinosLocalVersion) <MinosReleaseManifest>",
                         r.stage_b("$(JevReleaseVersion) $(JevCheckReleaseVersion) $(JevLocalVersion) <JevReleaseManifest>"))

    def test_repository_url_moves_to_the_new_owner(self):
        url = "https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev"
        for kind in ("cs", "build", "md"):
            self.assertEqual("https://github.com/MarcelRoozekrans/Minos.NET", r.stage_a(url, kind), kind)
        self.assertEqual("https://github.com/MarcelRoozekrans/Minos.NET/issues",
                         r.stage_a(url + "/issues", "md"))
        self.assertEqual("https://jev.zeroalloc.net/", r.stage_a("https://jev.zeroalloc.net/", "md"))


if __name__ == "__main__":
    unittest.main()
