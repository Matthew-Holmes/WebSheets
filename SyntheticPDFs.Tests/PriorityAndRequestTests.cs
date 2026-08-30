using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared;
using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using SyntheticPDFs.Tests.Fakes;

namespace SyntheticPDFs.Tests
{
    // what a pass chooses to do, and how an explicit request cuts ahead of it
    [TestClass]
    public class PriorityAndRequestTests
    {
        private const String A = "latex/worksheets/a";
        private const String B = "latex/worksheets/b";

        private FakeGitRepoManager _git = null!;
        private FakeLLMService _llm = null!;
        private Orchestrator _orchestrator = null!;

        private const String GoodBody =
            "\\begin{document}\\ealpara{Zapisz}{Write the \\ealkey{fraction}}\\end{document}";

        [TestInitialize]
        public void Setup()
        {
            _git = new FakeGitRepoManager();
            _llm = new FakeLLMService { DefaultResponse = GoodBody };

            var options = new L2Options { GenerateVocabularyKeys = true };

            options.Languages["pol"] = new LanguageOptions
                { Font = "Noto Serif", BabelName = "polish" };
            options.Languages["ben"] = new LanguageOptions
                { Font = "Noto Sans Bengali", BabelName = "bengali" };

            options.EagerLanguages.Add("pol");

            _orchestrator = new Orchestrator(
                NullLogger<Orchestrator>.Instance,
                _git,
                _llm,
                Options.Create(new GenerationOptions { MaxFilesPerRun = 30 }),
                Options.Create(options));
        }

        private static readonly VocabTerm[] Terms =
        {
            new()
            {
                English = "fraction", Definition = "part of a whole",
                Translation = "ulamek", TranslatedDefinition = "czesc calosci",
            },
        };

        // a root with everything English finished, its vocabulary, and the Polish key
        private void CompleteThrough(String root, int from = 6)
        {
            _git.AddFile(root + ".tex", from);
            _git.AddFile(root + "_workedSolutions.tex", from - 1);
            _git.AddFile(root + "_solutions.tex", from - 2);
            _git.AddFile(root + "_vocab.tex", from - 3, TexFixtures.VocabularyKey(root, Terms));

            String name = root.Split('/').Last();

            _git.AddFile($"{root}/L2/pol/{name}_polishKey.tex", from - 4,
                TexFixtures.VocabularyKey(root, Terms, TexFixtures.Polish));
        }

        private static String NameOf(SyntheticPDFs.Models.TexSourceModel ts) => ts.FileNameFullPath;

        private String[] Committed() => _git.LastCommit.Select(NameOf).ToArray();

        // ---- the gate ----

        [TestMethod]
        public async Task EnglishAnywhereBlocksVocabularyEverywhere()
        {
            // a is finished, b still owes its worked solutions. the vocabulary for a
            // waits, because a pass does one kind of work
            _git.AddFile(A + ".tex", 3);
            _git.AddFile(A + "_workedSolutions.tex", 2);
            _git.AddFile(A + "_solutions.tex", 1);
            _git.AddFile(B + ".tex", 1);

            await _orchestrator.DoOnePassAsync();

            CollectionAssert.AreEqual(new[] { B + "_workedSolutions.tex" }, Committed());
        }

        [TestMethod]
        public async Task VocabularyEverywhereBlocksTranslationsEverywhere()
        {
            CompleteThrough(A);

            // b has finished English but no vocabulary yet
            _git.AddFile(B + ".tex", 3);
            _git.AddFile(B + "_workedSolutions.tex", 2);
            _git.AddFile(B + "_solutions.tex", 1);

            await _orchestrator.DoOnePassAsync();

            CollectionAssert.AreEqual(new[] { B + "_vocab.tex" }, Committed(),
                "the Polish sheets for a wait until every vocabulary key exists");
        }

        [TestMethod]
        public async Task WithNothingElseOutstandingTheTranslationsAreMade()
        {
            CompleteThrough(A);

            await _orchestrator.DoOnePassAsync();

            CollectionAssert.AreEquivalent(
                new[]
                {
                    A + "/L2/pol/a_polishParallelText.tex",
                    A + "/L2/pol/a_polishTier3Only.tex",
                },
                Committed());
        }

        // ---- requests ----

        private GenerateResult Ask(String root, String language, String type, String rendition) =>
            _orchestrator.RequestGeneration(new GenerateRequest
            {
                RootName = root, Language = language, Type = type, Rendition = rendition,
            });

        [TestMethod]
        public async Task ARequestedFileCutsAheadOfEverythingElse()
        {
            // a is ready for its translations; b has not even started
            CompleteThrough(A);
            _git.AddFile(B + ".tex", 1);

            var result = Ask(A, "pol", "WorkedSolutions", "Tier3Only");

            Assert.AreEqual(GenerateOutcome.Queued, result.Outcome);

            await _orchestrator.DoOnePassAsync();

            CollectionAssert.AreEqual(
                new[] { A + "/L2/pol/a_workedSolutions_polishTier3Only.tex" },
                Committed(),
                "the requested file is made before b's English worked solutions");
        }

        [TestMethod]
        public void ARequestQueuesWhatItDependsOnToo()
        {
            // nothing exists yet beyond the English sheet, so the whole chain is queued
            _git.AddFile(B + ".tex", 1);

            var result = Ask(B, "ben", "Solutions", "ParallelText");

            Assert.AreEqual(GenerateOutcome.Queued, result.Outcome);

            CollectionAssert.AreEqual(
                new[]
                {
                    B + "_workedSolutions.tex",
                    B + "_solutions.tex",
                    B + "_vocab.tex",
                    B + "/L2/ben/b_bengaliKey.tex",
                    B + "/L2/ben/b_solutions_bengaliParallelText.tex",
                },
                result.Queued.ToArray(),
                "everything it is derived from, deepest first, the requested file last");
        }

        [TestMethod]
        public void TheEnglishRootIsNeverQueuedSinceAPersonWritesIt()
        {
            _git.AddFile(B + ".tex", 1);

            var result = Ask(B, "pol", "Root", "ParallelText");

            CollectionAssert.DoesNotContain(result.Queued.ToArray(), B + ".tex");
        }

        [TestMethod]
        public async Task ARequestedChainIsWorkedThroughInOrder()
        {
            _git.AddFile(B + ".tex", 1);

            Ask(B, "ben", "Root", "Tier3Only");

            var made = new List<String>();

            for (int pass = 0; pass < 5; pass++)
            {
                if (await _orchestrator.DoOnePassAsync() != Orchestrator.PassOutcome.Generated) { break; }

                made.AddRange(Committed());
            }

            CollectionAssert.Contains(made, B + "/L2/ben/b_bengaliTier3Only.tex",
                "the requested file is reached in the end");
            CollectionAssert.Contains(made, B + "_vocab.tex",
                "having made what it depends on first");
        }

        [TestMethod]
        public async Task ANonEagerFileIsNotRebuiltOnceItsParentChanges()
        {
            // the rule for anything made on request: removed when stale, and not
            // silently made again
            CompleteThrough(A);

            Ask(A, "pol", "WorkedSolutions", "Tier3Only");

            await _orchestrator.DoOnePassAsync();

            String requested = A + "/L2/pol/a_workedSolutions_polishTier3Only.tex";

            CollectionAssert.Contains(_git.Files.Keys.ToArray(), requested);

            // someone edits the English worked solutions
            _git.AddFile(A + "_workedSolutions.tex", 0);

            Assert.AreEqual(
                Orchestrator.PassOutcome.RemovedStaleFiles, await _orchestrator.DoOnePassAsync());

            CollectionAssert.DoesNotContain(_git.Files.Keys.ToArray(), requested);

            // run to convergence - it must not come back
            for (int pass = 0; pass < 5; pass++)
            {
                if (await _orchestrator.DoOnePassAsync() == Orchestrator.PassOutcome.NothingToDo) { break; }
            }

            CollectionAssert.DoesNotContain(_git.Files.Keys.ToArray(), requested,
                "a file made on request is not rebuilt unasked");
        }

        // ---- requests we cannot honour ----

        [TestMethod]
        [DataRow("", "pol", "Root", "ParallelText", "root name")]
        [DataRow(A, "zzz", "Root", "ParallelText", "zzz")]
        [DataRow(A, "fra", "Root", "ParallelText", "fra")]
        [DataRow(A, "pol", "Nonsense", "ParallelText", "Nonsense")]
        [DataRow(A, "pol", "Root", "Original", "Original")]
        [DataRow(A, "pol", "Root", "Nonsense", "Nonsense")]
        public void ARequestWeCannotHonourSaysWhy(
            String root, String language, String type, String rendition, String mentioned)
        {
            var result = Ask(root, language, type, rendition);

            Assert.AreEqual(GenerateOutcome.NotUnderstood, result.Outcome);
            StringAssert.Contains(result.Message, mentioned);
            Assert.AreEqual(0, result.Queued.Count);
        }

        [TestMethod]
        public void AskingForAFileItsArchetypeCannotHaveIsRefused()
        {
            // a poster has no answers, so there is no translation of them to make
            _git.AddFile("latex/cheatSheets/trig.tex", 1);

            var result = Ask("latex/cheatSheets/trig", "pol", "Solutions", "ParallelText");

            Assert.AreEqual(GenerateOutcome.NotUnderstood, result.Outcome);
        }

        // ---- purging ----

        [TestMethod]
        public async Task PurgingTranslationsLeavesTheEnglishAndItsVocabulary()
        {
            CompleteThrough(A);

            await _orchestrator.DoOnePassAsync();

            var result = await _orchestrator.PurgeAsync(PurgeScope.Translations);

            Assert.IsTrue(result.Removed);

            Assert.IsFalse(
                _git.Files.Keys.Any(f => f.Contains("/L2/", StringComparison.Ordinal)),
                "every translated file goes");

            CollectionAssert.Contains(_git.Files.Keys.ToArray(), A + "_vocab.tex",
                "the vocabulary key stays");
            CollectionAssert.Contains(_git.Files.Keys.ToArray(), A + ".tex");
        }

        [TestMethod]
        public async Task PurgingEverythingTakesTheVocabularyToo()
        {
            CompleteThrough(A);

            var result = await _orchestrator.PurgeAsync(PurgeScope.TranslationsAndVocabulary);

            Assert.IsTrue(result.Removed);
            CollectionAssert.DoesNotContain(_git.Files.Keys.ToArray(), A + "_vocab.tex");
            CollectionAssert.Contains(_git.Files.Keys.ToArray(), A + ".tex",
                "the English sheet is never touched");
        }

        [TestMethod]
        public async Task PurgingWithNothingToRemoveIsNotAFailure()
        {
            _git.AddFile(A + ".tex", 1);

            var result = await _orchestrator.PurgeAsync(PurgeScope.TranslationsAndVocabulary);

            Assert.IsTrue(result.Removed);
            Assert.AreEqual(0, result.Files.Count);
            Assert.AreEqual(0, _git.RemoveFilesCalls.Count, "no commit for nothing");
        }

        [TestMethod]
        public async Task AConflictDuringAPurgeRemovesNothing()
        {
            CompleteThrough(A);

            _git.SimulateConflict = true;

            var result = await _orchestrator.PurgeAsync(PurgeScope.Translations);

            Assert.IsFalse(result.Removed);
            CollectionAssert.Contains(_git.Files.Keys.ToArray(), A + "/L2/pol/a_polishKey.tex");
        }
    }
}
