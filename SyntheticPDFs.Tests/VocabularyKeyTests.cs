using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using SyntheticPDFs.Tests.Fakes;

namespace SyntheticPDFs.Tests
{
    // the vocabulary key is the seed for every translation, and the only generated file
    // whose LaTeX is written here rather than by a model
    [TestClass]
    public class VocabularyKeyTests
    {
        private const String Root   = "latex/worksheets/algebra/quadratics";
        private const String RootEx = Root + ".tex";
        private const String Worked = Root + "_workedSolutions.tex";
        private const String Answers = Root + "_solutions.tex";
        private const String Vocab  = Root + "_vocab.tex";
        private const String PolishKey =
            Root + "/L2/pol/quadratics_polishKey.tex";

        private FakeGitRepoManager _git = null!;
        private FakeLLMService _llm = null!;

        [TestInitialize]
        public void Setup()
        {
            _git = new FakeGitRepoManager();
            _llm = new FakeLLMService();
        }

        // where the orchestrator looks for the shared definitions by default
        private const String DictionaryPath = "latex/dictionary/mathematicalDictionary.tex";

        private void GiveTheRepoADictionary(String entries) =>
            _git.AddFile(DictionaryPath, ageCommits: 9, contents: entries);

        private static L2Options Settings(bool withPolish = false)
        {
            var options = new L2Options { GenerateVocabularyKeys = true };

            if (withPolish)
            {
                options.Languages["pol"] = new LanguageOptions
                    { Font = "Noto Serif", BabelName = "polish" };
                options.EagerLanguages.Add("pol");
            }

            return options;
        }

        private Orchestrator Build(L2Options options) => new Orchestrator(
            NullLogger<Orchestrator>.Instance,
            _git,
            _llm,
            Options.Create(new GenerationOptions { MaxFilesPerRun = 30 }),
            Options.Create(options));

        private void GiveTheRepoACompleteWorksheet()
        {
            _git.AddFile(RootEx, ageCommits: 3);
            _git.AddFile(Worked, ageCommits: 2);
            _git.AddFile(Answers, ageCommits: 1);
        }

        // ---- reading what the model returns ----

        [TestMethod]
        public void JsonWrappedInProseIsStillRead()
        {
            // models add explanation and code fences however firmly they are asked not to
            var terms = L2VocabData.TryParse(
                "Here you go:\n```json\n{\"terms\":[{\"en\":\"fraction\",\"def\":\"part of a whole\"}]}\n```");

            Assert.IsNotNull(terms);
            Assert.AreEqual("fraction", terms.Single().English);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("no json here at all")]
        [DataRow("{\"terms\":[]}")]
        [DataRow("{\"terms\":[{\"en\":\"\",\"def\":\"\"}]}")]
        [DataRow("{ this is not valid json")]
        public void UnusableResponsesAreRejectedRatherThanGuessedAt(String response)
        {
            Assert.IsNull(L2VocabData.TryParse(response));
        }

        [TestMethod]
        public void TheSameWordTwiceIsCollapsed()
        {
            // two identical rows would give the match-up a question with two answers
            var terms = L2VocabData.TryParse(
                "{\"terms\":[{\"en\":\"area\",\"def\":\"first\"},{\"en\":\"Area\",\"def\":\"second\"}]}");

            Assert.IsNotNull(terms);
            Assert.AreEqual(1, terms.Count);
            Assert.AreEqual("first", terms.Single().Definition, "the first definition wins");
        }

        // ---- carrying the data inside the .tex ----

        [TestMethod]
        public void TermsSurviveARoundTripThroughTheCommentBlock()
        {
            var original = new List<VocabTerm>
            {
                new() { English = "numerator", Definition = "above the line" },
                new() { English = "area", Definition = "space inside", Translation = "pole",
                        TranslatedDefinition = "przestrzen wewnatrz" },
            };

            var read = L2VocabData.ReadBlock("\\documentclass{article}\n" + L2VocabData.Block(original));

            Assert.IsNotNull(read);
            CollectionAssert.AreEqual(original, read);
        }

        [TestMethod]
        public void AFileWithNoDataBlockReadsAsNothing()
        {
            Assert.IsNull(L2VocabData.ReadBlock("\\documentclass{article}"));
        }

        // ---- the shared dictionary ----

        private static MathsDictionary Dictionary(String word, String definition) =>
            MathsDictionary.Parse($"\\dictentry{{{word}}}{{{definition}}}");

        [TestMethod]
        public void SharedDefinitionsReplaceTheModelsOwn()
        {
            // this is what makes them standard across worksheets rather than suggested
            var terms = L2VocabData.ApplyDictionary(
                new[] { new VocabTerm { English = "Area", Definition = "whatever the model said" } },
                Dictionary("area", "the agreed wording"));

            Assert.AreEqual("the agreed wording", terms.Single().Definition);
        }

        [TestMethod]
        public void AKeyIsOutOfDateOnceASharedDefinitionChanges()
        {
            String tex = L2VocabData.Block(new[]
            {
                new VocabTerm { English = "area", Definition = "the old wording" },
            });

            Assert.IsTrue(L2VocabData.MatchesDictionary(tex, Dictionary("area", "the old wording")));
            Assert.IsFalse(L2VocabData.MatchesDictionary(tex, Dictionary("area", "the new wording")));
        }

        [TestMethod]
        public void AWordNotInTheDictionaryDoesNotMakeAKeyOutOfDate()
        {
            // the point of comparing by content: an edit rebuilds only the keys it touches
            String tex = L2VocabData.Block(new[]
            {
                new VocabTerm { English = "hypotenuse", Definition = "the model's wording" },
            });

            Assert.IsTrue(L2VocabData.MatchesDictionary(
                tex, Dictionary("area", "something else entirely")));
        }

        // ---- the match-up ----

        [TestMethod]
        public void TheMatchUpIsShuffledButStable()
        {
            var terms = Enumerable.Range(0, 8)
                .Select(i => new VocabTerm { English = $"word{i}", Definition = $"def{i}" })
                .ToList();

            var once = L2VocabData.Shuffled(terms, Root);
            var again = L2VocabData.Shuffled(terms, Root);

            CollectionAssert.AreEqual(once, again, "the same sheet must shuffle the same way twice");
            CollectionAssert.AreEquivalent(terms, once, "every term must still be there");
            CollectionAssert.AreNotEqual(terms, once, "a match-up in key order gives itself away");
        }

        [TestMethod]
        public void ATwoTermMatchUpIsStillReordered()
        {
            // the case most likely to come back in its original order by chance
            var terms = new List<VocabTerm>
            {
                new() { English = "a", Definition = "one" },
                new() { English = "b", Definition = "two" },
            };

            CollectionAssert.AreNotEqual(terms, L2VocabData.Shuffled(terms, Root));
        }

        // ---- the provenance block ----

        [TestMethod]
        public void ProvenanceRecordsColoursAsPlainNumbersAndReadsThemBack()
        {
            var colours = new L2ColourOptions();

            String block = L2Macros.ProvenanceBlock(
                "Polish Parallel Text Version of Quadratics", colours, null, RootEx, null);

            StringAssert.Contains(block, "RGB 112 48 160", "colours are written out, not hashed");
            StringAssert.Contains(block, "purple", "and named, for whoever opens the file");
            StringAssert.Contains(block, "Polish Parallel Text Version of Quadratics");
            StringAssert.Contains(block, "feel free", "it must say what is safe to edit");

            Assert.IsTrue(L2Macros.MatchesSettings(block, colours));
        }

        [TestMethod]
        public void ChangingAColourMakesAFileOutOfDate()
        {
            String block = L2Macros.ProvenanceBlock(
                "title", new L2ColourOptions(), null, RootEx, null);

            var changed = new L2ColourOptions();
            changed.Translation = new RgbColourOptions { R = 1, G = 2, B = 3, Name = "blue" };

            Assert.IsFalse(L2Macros.MatchesSettings(block, changed));
        }

        [TestMethod]
        public void AFileWithNoProvenanceCannotBeShownToBeCurrent()
        {
            Assert.IsFalse(L2Macros.MatchesSettings("\\documentclass{article}", new L2ColourOptions()));
        }

        // ---- the rendered key ----

        [TestMethod]
        public void TheKeyRepeatsItselfWithoutRepeatingItsSource()
        {
            // A4 twice over, so a teacher printing two pages to a sheet gets two copies
            var terms = new List<VocabTerm>
            {
                new() { English = "numerator", Definition = "above the line" },
                new() { English = "denominator", Definition = "below the line" },
            };

            String tex = L2VocabKeyRenderer.Render(
                terms,
                new L2Macros.SourceMetadataTitle(Root, SourceType.Root, SourceRendition.VocabKey),
                new L2ColourOptions(), null, RootEx, null);

            Assert.AreEqual(1, Occurrences(tex, @"\newcommand{\ealkeybody}"),
                "the key body is defined once");
            Assert.AreEqual(1, Occurrences(tex, "key vocabulary"),
                "and its contents appear once in the source, not twice over");

            // both bodies invoked twice, which is what puts each on two pages
            StringAssert.Contains(tex, @"\ealkeybody\newpage\ealkeybody");
            StringAssert.Contains(tex, @"\ealmatchbody\newpage\ealmatchbody");
            StringAssert.Contains(tex, L2Macros.CompilerDirective);
        }

        [TestMethod]
        public void ARenderedKeyCarriesItsOwnDataForTheTranslationToUse()
        {
            var terms = new List<VocabTerm>
            {
                new() { English = "numerator", Definition = "above the line" },
            };

            String tex = L2VocabKeyRenderer.Render(
                terms,
                new L2Macros.SourceMetadataTitle(Root, SourceType.Root, SourceRendition.VocabKey),
                new L2ColourOptions(), null, RootEx, null);

            CollectionAssert.AreEqual(terms, L2VocabData.ReadBlock(tex));
        }

        [TestMethod]
        public void CharactersLatexWouldReadAsMarkupAreNeutralised()
        {
            // a stray % in a definition comments out the rest of the row
            var terms = new List<VocabTerm>
            {
                new() { English = "percentage", Definition = "50% of something & more" },
            };

            String tex = L2VocabKeyRenderer.Render(
                terms,
                new L2Macros.SourceMetadataTitle(Root, SourceType.Root, SourceRendition.VocabKey),
                new L2ColourOptions(), null, RootEx, null);

            StringAssert.Contains(tex, @"50\% of something \& more");
        }

        private static int Occurrences(String haystack, String needle)
        {
            int count = 0, at = 0;

            while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += needle.Length;
            }

            return count;
        }

        // ---- full passes ----

        [TestMethod]
        public async Task NoVocabularyKeyIsMadeUnlessItIsAskedFor()
        {
            GiveTheRepoACompleteWorksheet();

            var orchestrator = Build(new L2Options());

            Assert.AreEqual(Orchestrator.PassOutcome.NothingToDo, await orchestrator.DoOnePassAsync());
            Assert.AreEqual(0, _llm.StructuredPromptsSeen.Count);
        }

        [TestMethod]
        public async Task TheKeyIsMadeOnlyOnceTheWholeSheetExists()
        {
            _git.AddFile(RootEx, ageCommits: 1);

            var orchestrator = Build(Settings());

            // worked solutions, then answers, and only then the vocabulary
            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await orchestrator.DoOnePassAsync());
            CollectionAssert.DoesNotContain(_git.LastCommit.Select(t => t.FileNameFullPath).ToArray(), Vocab);

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await orchestrator.DoOnePassAsync());
            CollectionAssert.DoesNotContain(_git.LastCommit.Select(t => t.FileNameFullPath).ToArray(), Vocab);

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await orchestrator.DoOnePassAsync());
            CollectionAssert.AreEqual(
                new[] { Vocab },
                _git.LastCommit.Select(t => t.FileNameFullPath).ToArray());
        }

        [TestMethod]
        public async Task ThePromptSeesTheAnswersAsWellAsTheQuestions()
        {
            // a word a pupil needs may appear only in the workings
            _git.AddFile(RootEx, ageCommits: 3, contents: "\\documentclass{article} % THE QUESTIONS");
            _git.AddFile(Worked, ageCommits: 2, contents: "\\documentclass{article} % THE WORKINGS");
            _git.AddFile(Answers, ageCommits: 1, contents: "\\documentclass{article} % THE ANSWERS");

            await Build(Settings()).DoOnePassAsync();

            String prompt = _llm.StructuredPromptsSeen.Single();

            StringAssert.Contains(prompt, "THE QUESTIONS");
            StringAssert.Contains(prompt, "THE WORKINGS");
            StringAssert.Contains(prompt, "THE ANSWERS");
        }

        [TestMethod]
        public async Task APosterGetsAKeyWithoutWaitingForWorkingsItWillNeverHave()
        {
            // the caveat in the specification: only the files that archetype actually has
            _git.AddFile("latex/cheatSheets/trigIdentities.tex", ageCommits: 1);

            var orchestrator = Build(Settings());

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await orchestrator.DoOnePassAsync());
            CollectionAssert.AreEqual(
                new[] { "latex/cheatSheets/trigIdentities_vocab.tex" },
                _git.LastCommit.Select(t => t.FileNameFullPath).ToArray());
        }

        [TestMethod]
        public async Task SharedDefinitionsReachTheGeneratedKey()
        {
            GiveTheRepoACompleteWorksheet();
            GiveTheRepoADictionary(
                @"\dictentry{numerator}{the number above the line in a fraction}");

            _llm.StructuredResponse = FakeLLMService.Vocabulary(
                ("numerator", "whatever the model came up with"));

            await Build(Settings()).DoOnePassAsync();

            String tex = _git.LastCommit.Single().TexSource;

            StringAssert.Contains(tex, "the number above the line in a fraction");
            Assert.IsFalse(tex.Contains("whatever the model came up with", StringComparison.Ordinal));
        }

        [TestMethod]
        public async Task ASheetUsingAWordVariantStillGetsTheSharedDefinition()
        {
            // the sheet says "Numerators"; the dictionary files it under "numerator"
            GiveTheRepoACompleteWorksheet();
            GiveTheRepoADictionary(
                @"\dictentry{numerator}{the number above the line in a fraction}");

            _llm.StructuredResponse = FakeLLMService.Vocabulary(
                ("Numerators", "whatever the model came up with"));

            await Build(Settings()).DoOnePassAsync();

            StringAssert.Contains(
                _git.LastCommit.Single().TexSource, "the number above the line in a fraction");
        }

        [TestMethod]
        public async Task ARepositoryWithNoDictionaryStillGeneratesKeys()
        {
            // the dictionary lives in the content repository, so it may simply not be
            // there yet - the model's own wording stands rather than the pass failing
            GiveTheRepoACompleteWorksheet();

            _llm.StructuredResponse = FakeLLMService.Vocabulary(
                ("numerator", "the model's own wording"));

            Assert.AreEqual(
                Orchestrator.PassOutcome.Generated, await Build(Settings()).DoOnePassAsync());

            StringAssert.Contains(_git.LastCommit.Single().TexSource, "the model's own wording");
        }

        [TestMethod]
        public async Task TheDictionaryIsNotTreatedAsAWorksheet()
        {
            // it lives under latex/, so without an exception the pipeline would try to
            // write worked solutions for a list of words
            GiveTheRepoADictionary(@"\dictentry{numerator}{above the line}");

            Assert.AreEqual(
                Orchestrator.PassOutcome.NothingToDo, await Build(Settings()).DoOnePassAsync());

            Assert.AreEqual(0, _git.CommitCalls.Count);
        }

        // ---- the translated key ----

        [TestMethod]
        public async Task TheTranslatedKeyIsBuiltFromTheEnglishKeysOwnData()
        {
            GiveTheRepoACompleteWorksheet();

            var orchestrator = Build(Settings(withPolish: true));

            _llm.StructuredResponse = FakeLLMService.Vocabulary(("numerator", "above the line"));

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await orchestrator.DoOnePassAsync());

            _llm.StructuredResponse = FakeLLMService.TranslatedVocabulary(
                ("numerator", "above the line", "licznik", "liczba nad kreska"));

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await orchestrator.DoOnePassAsync());

            var committed = _git.LastCommit.Single();

            Assert.AreEqual(PolishKey, committed.FileNameFullPath);
            StringAssert.Contains(committed.TexSource, "licznik");
            StringAssert.Contains(committed.TexSource, @"\babelprovide[import]{polish}",
                "the language preamble is written for it, not by the model");
        }

        [TestMethod]
        public async Task ATermTheModelFailsToTranslateKeepsItsEnglish()
        {
            // a missing row would put the key and the match-up out of step with each other
            GiveTheRepoACompleteWorksheet();

            var orchestrator = Build(Settings(withPolish: true));

            _llm.StructuredResponse = FakeLLMService.Vocabulary(
                ("numerator", "above the line"), ("denominator", "below the line"));

            await orchestrator.DoOnePassAsync();

            // only one of the two comes back translated
            _llm.StructuredResponse = FakeLLMService.TranslatedVocabulary(
                ("numerator", "above the line", "licznik", "liczba nad kreska"));

            await orchestrator.DoOnePassAsync();

            var terms = L2VocabData.ReadBlock(_git.LastCommit.Single().TexSource);

            Assert.IsNotNull(terms);
            Assert.AreEqual(2, terms.Count, "no term may be dropped");
            Assert.AreEqual("denominator", terms[1].Translation,
                "an untranslated term keeps its English rather than going missing");
        }

        [TestMethod]
        public async Task NoTranslationIsAttemptedWithoutAKeyToTranslate()
        {
            // the whole point of the ordering: nothing in a language starts until the
            // English vocabulary exists
            _git.AddFile(RootEx, ageCommits: 1);

            var orchestrator = Build(Settings(withPolish: true));

            await orchestrator.DoOnePassAsync();

            Assert.IsFalse(
                _git.Files.Keys.Any(f => f.Contains("/L2/", StringComparison.Ordinal)),
                "no translated file may appear before the key it comes from");
        }
    }
}
