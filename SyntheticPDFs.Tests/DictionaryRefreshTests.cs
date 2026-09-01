using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Rendering;
using SyntheticPDFs.Tests.Fakes;
using System.Text.RegularExpressions;

namespace SyntheticPDFs.Tests
{
    // The shared dictionary in each language, kept in step with the English one.
    //
    // The point of it is cost: a word defined once and translated once is reused by every
    // sheet that mentions it, instead of being sent to a model again with each sheet that
    // does. These tests pin the two halves of that - that the translation is made once,
    // and that the glossaries then take it rather than asking again.
    [TestClass]
    public class DictionaryRefreshTests
    {
        private const String DictionaryPath = "latex/dictionary/mathematicalDictionary.tex";

        private const String PolishDictionary =
            "latex/dictionary/L2/pol/mathematicalDictionary_polish.tex";

        private const String Root    = "latex/worksheets/algebra/quadratics";
        private const String RootEx  = Root + ".tex";
        private const String Worked  = Root + "_workedSolutions.tex";
        private const String Answers = Root + "_solutions.tex";
        private const String Vocab   = Root + "_vocab.tex";

        private const String PolishKey = Root + "/L2/pol/quadratics_polishKey.tex";

        private FakeGitRepoManager _git = null!;
        private FakeLLMService _llm = null!;

        [TestInitialize]
        public void Setup()
        {
            _git = new FakeGitRepoManager();
            _llm = new FakeLLMService();
        }

        private static L2Options Settings()
        {
            var options = new L2Options { GenerateVocabularyKeys = true };

            options.Languages["pol"] = new LanguageOptions
                { Font = "Noto Serif", BabelName = "polish" };

            options.EagerLanguages.Add("pol");

            return options;
        }

        private Orchestrator Build() => new Orchestrator(
            NullLogger<Orchestrator>.Instance,
            _git,
            _llm,
            Options.Create(new GenerationOptions { MaxFilesPerRun = 30 }),
            Options.Create(Settings()));

        private void GiveTheRepoADictionary(String entries) =>
            _git.AddFile(DictionaryPath, ageCommits: 9, contents: entries);

        private const String OneWord =
            @"\dictentry{numerator}{the number above the line in a fraction}";

        // a whole English sheet, already settled, so a pass gets straight to the L2 work
        private void GiveTheRepoASettledSheet()
        {
            _git.AddFile(RootEx, ageCommits: 8);
            _git.AddFile(Worked, ageCommits: 7);
            _git.AddFile(Answers, ageCommits: 6);
        }

        private void GiveTheRepoAnEnglishGlossary(params (String Word, String Meaning)[] terms)
        {
            _git.AddFile(Vocab, ageCommits: 5, contents: TexFixtures.VocabularyKey(
                Root,
                terms.Select(t => new VocabTerm { English = t.Word, Definition = t.Meaning })
                     .ToList()));
        }

        private void GiveTheRepoAPolishDictionary(params L2DictionaryEntry[] entries)
        {
            _git.AddFile(PolishDictionary, ageCommits: 4, contents: L2DictionaryRenderer.Render(
                L2Dictionary.Empty(new ISO639_3Code("pol")).With(entries),
                TexFixtures.Polish,
                new L2ColourOptions(),
                builtFrom: DictionaryPath,
                fallbackFont: TexFixtures.FallbackFont));
        }

        private static L2DictionaryEntry Entry(
            String headword, String english, String word, String definition) =>
            new()
            {
                Headword   = headword,
                English    = english,
                Word       = word,
                Definition = definition,
            };

        private String? Committed(String path) =>
            _git.CommitCalls
                .SelectMany(c => c)
                .LastOrDefault(f => f.FileNameFullPath == path)
                ?.TexSource;

        #region Making one

        [TestMethod]
        public async Task TheSharedDictionaryIsTranslatedOnceForTheWholeRepository()
        {
            GiveTheRepoADictionary(OneWord);
            GiveTheRepoASettledSheet();
            GiveTheRepoAnEnglishGlossary(("numerator", "the number above the line in a fraction"));

            _llm.StructuredResponse = FakeLLMService.TranslatedVocabulary(
                ("numerator", "the number above the line in a fraction",
                 "licznik", "liczba nad kreska ulamka"));

            await Build().DoOnePassAsync();

            String? written = Committed(PolishDictionary);

            Assert.IsNotNull(written, "a dictionary in every language being produced");
            StringAssert.Contains(written, "licznik");
        }

        [TestMethod]
        public async Task ADictionaryIsNotWrittenForALanguageNothingIsProducedIn()
        {
            // nothing is eager here and no Polish file exists, so there is nothing for a
            // Polish dictionary to serve yet
            var options = new L2Options { GenerateVocabularyKeys = true };

            options.Languages["pol"] = new LanguageOptions
                { Font = "Noto Serif", BabelName = "polish" };

            GiveTheRepoADictionary(OneWord);
            GiveTheRepoASettledSheet();
            GiveTheRepoAnEnglishGlossary(("numerator", "the number above the line in a fraction"));

            var orchestrator = new Orchestrator(
                NullLogger<Orchestrator>.Instance,
                _git,
                _llm,
                Options.Create(new GenerationOptions { MaxFilesPerRun = 30 }),
                Options.Create(options));

            await orchestrator.DoOnePassAsync();

            Assert.IsNull(Committed(PolishDictionary));
        }

        #endregion

        #region Keeping one in step

        [TestMethod]
        public async Task RewordingAnEnglishDefinitionRetranslatesThatWordAndNoOther()
        {
            GiveTheRepoADictionary(
                @"\dictentry{numerator}{the top number of a fraction}" + "\n"
                + @"\dictentry{vertex}{a corner where two or more edges meet}");

            GiveTheRepoASettledSheet();
            GiveTheRepoAnEnglishGlossary(
                ("numerator", "the top number of a fraction"),
                ("vertex", "a corner where two or more edges meet"));

            // the Polish dictionary was made from the older English wording of numerator
            GiveTheRepoAPolishDictionary(
                Entry("numerator", "the number above the line in a fraction",
                    "licznik", "liczba nad kreska ulamka"),
                Entry("vertex", "a corner where two or more edges meet",
                    "wierzcholek", "rog, w ktorym spotykaja sie krawedzie"));

            _llm.StructuredResponse = FakeLLMService.TranslatedVocabulary(
                ("numerator", "the top number of a fraction", "licznik", "gorna liczba ulamka"));

            await Build().DoOnePassAsync();

            // the prompt lists the words it wants translating, one to a line
            String prompt = _llm.StructuredPromptsSeen.Single();

            StringAssert.Contains(prompt, "- numerator:", "the reworded word has to go again");
            Assert.IsFalse(
                prompt.Contains("- vertex:", StringComparison.Ordinal),
                "a word whose English wording did not change keeps the translation it has");

            String? written = Committed(PolishDictionary);

            Assert.IsNotNull(written);
            StringAssert.Contains(written, "gorna liczba ulamka", "the new translation is in");
            StringAssert.Contains(written, "wierzcholek", "and the untouched one is still there");
        }

        [TestMethod]
        public async Task AWordTakenOutOfTheSharedDictionaryLosesItsTranslation()
        {
            GiveTheRepoADictionary(OneWord);
            GiveTheRepoASettledSheet();
            GiveTheRepoAnEnglishGlossary(("numerator", "the number above the line in a fraction"));

            GiveTheRepoAPolishDictionary(
                Entry("numerator", "the number above the line in a fraction",
                    "licznik", "liczba nad kreska ulamka"),
                Entry("vertex", "a corner where two or more edges meet",
                    "wierzcholek", "rog, w ktorym spotykaja sie krawedzie"));

            await Build().DoOnePassAsync();

            String? written = Committed(PolishDictionary);

            Assert.IsNotNull(written);
            StringAssert.Contains(written, "licznik");
            Assert.IsFalse(
                written.Contains("wierzcholek", StringComparison.Ordinal),
                "a translation of a word nothing defines has nothing left to be current against");

            Assert.AreEqual(0, _llm.StructuredPromptsSeen.Count,
                "removing a word costs nothing at all");
        }

        [TestMethod]
        public async Task ADictionaryThatIsAlreadyInStepIsLeftAlone()
        {
            GiveTheRepoADictionary(OneWord);
            GiveTheRepoASettledSheet();
            GiveTheRepoAnEnglishGlossary(("numerator", "the number above the line in a fraction"));

            GiveTheRepoAPolishDictionary(
                Entry("numerator", "the number above the line in a fraction",
                    "licznik", "liczba nad kreska ulamka"));

            await Build().DoOnePassAsync();

            Assert.IsNull(Committed(PolishDictionary),
                "a settled dictionary must not be rewritten on every pass");
        }

        #endregion

        #region Spending the saving

        [TestMethod]
        public async Task ATranslatedGlossaryIsBuiltFromTheDictionaryWithoutAskingAModel()
        {
            GiveTheRepoADictionary(OneWord);
            GiveTheRepoASettledSheet();
            GiveTheRepoAnEnglishGlossary(("numerator", "the number above the line in a fraction"));

            GiveTheRepoAPolishDictionary(
                Entry("numerator", "the number above the line in a fraction",
                    "licznik", "liczba nad kreska ulamka"));

            await Build().DoOnePassAsync();

            String? written = Committed(PolishKey);

            Assert.IsNotNull(written, "the Polish glossary is what this pass had left to do");
            StringAssert.Contains(written, "licznik");

            Assert.AreEqual(0, _llm.StructuredPromptsSeen.Count,
                "every word was already translated, so nothing needed asking");
        }

        [TestMethod]
        public async Task AWordTheDictionaryDoesNotCoverIsStillAskedFor()
        {
            GiveTheRepoADictionary(OneWord);
            GiveTheRepoASettledSheet();
            GiveTheRepoAnEnglishGlossary(
                ("numerator", "the number above the line in a fraction"),
                ("hypotenuse", "the longest side of a right angled triangle"));

            GiveTheRepoAPolishDictionary(
                Entry("numerator", "the number above the line in a fraction",
                    "licznik", "liczba nad kreska ulamka"));

            _llm.StructuredResponse = FakeLLMService.TranslatedVocabulary(
                ("hypotenuse", "the longest side of a right angled triangle",
                 "przeciwprostokatna", "najdluzszy bok trojkata prostokatnego"));

            // it takes the repository a few passes to settle: the word the dictionary
            // has never heard of goes into the English one, then into the Polish one,
            // and only then onto the sheet's own glossary
            await Settle(Build());

            String prompt = _llm.StructuredPromptsSeen.Single();

            StringAssert.Contains(prompt, "- hypotenuse:");
            Assert.IsFalse(
                prompt.Contains("- numerator:", StringComparison.Ordinal),
                "only the words the dictionary could not answer are sent");
        }

        [TestMethod]
        public async Task AWordMetOnASheetIsTranslatedOnceAndThenBelongsToTheRepository()
        {
            GiveTheRepoADictionary(OneWord);
            GiveTheRepoASettledSheet();
            GiveTheRepoAnEnglishGlossary(
                ("numerator", "the number above the line in a fraction"),
                ("hypotenuse", "the longest side of a right angled triangle"));

            GiveTheRepoAPolishDictionary(
                Entry("numerator", "the number above the line in a fraction",
                    "licznik", "liczba nad kreska ulamka"));

            _llm.StructuredResponse = FakeLLMService.TranslatedVocabulary(
                ("hypotenuse", "the longest side of a right angled triangle",
                 "przeciwprostokatna", "najdluzszy bok trojkata prostokatnego"));

            await Settle(Build());

            StringAssert.Contains(
                _git.Contents[DictionaryPath], @"\dictentry{hypotenuse}",
                "the English definition is the repository's now, not one sheet's");

            StringAssert.Contains(
                _git.Contents[PolishDictionary], "przeciwprostokatna",
                "and so is its translation, so the next sheet to use the word pays nothing");

            Assert.AreEqual(1, _llm.StructuredPromptsSeen.Count,
                "which took exactly one call to a model, in the dictionary rather than "
                + "on the sheet");
        }

        // the repository settles over several passes, one commit at a time, and a test
        // that only ran the first would be testing the order rather than the outcome
        private static async Task Settle(Orchestrator orchestrator, int passes = 8)
        {
            for (int pass = 0; pass < passes; pass++)
            {
                if (await orchestrator.DoOnePassAsync()
                    == Orchestrator.PassOutcome.NothingToDo)
                {
                    return;
                }
            }
        }

        [TestMethod]
        public async Task CorrectingATranslationPutsTheGlossariesThatUseThatWordBackInStep()
        {
            GiveTheRepoADictionary(OneWord);
            GiveTheRepoASettledSheet();
            GiveTheRepoAnEnglishGlossary(("numerator", "the number above the line in a fraction"));

            GiveTheRepoAPolishDictionary(
                Entry("numerator", "the number above the line in a fraction",
                    "licznik", "liczba nad kreska ulamka"));

            // a Polish glossary made before somebody corrected the word in the dictionary
            _git.AddFile(PolishKey, ageCommits: 1, contents: TexFixtures.VocabularyKey(
                Root,
                new[]
                {
                    new VocabTerm
                    {
                        English              = "numerator",
                        Definition           = "the number above the line in a fraction",
                        Translation          = "licznikk",
                        TranslatedDefinition = "liczba nad kreska ulamka",
                    },
                },
                TexFixtures.Polish));

            var orchestrator = Build();

            Assert.AreEqual(
                Orchestrator.PassOutcome.Generated, await orchestrator.DoOnePassAsync(),
                "the glossary disagrees with the dictionary, so it is stated again");

            Assert.AreEqual(0, _git.RemoveFilesCalls.Count, "without being thrown away first");

            String? restated = Committed(PolishKey);

            Assert.IsNotNull(restated);
            StringAssert.Contains(restated, "licznik");
            Assert.IsFalse(restated.Contains("licznikk", StringComparison.Ordinal),
                "and the wording somebody corrected is gone");

            Assert.AreEqual(0, _llm.StructuredPromptsSeen.Count,
                "the correction is read out of the dictionary, never asked for again");
        }

        [TestMethod]
        public async Task AWordThePolishDictionaryHasNotKeepsTheTranslationItAlreadyHad()
        {
            // the English key has not moved, so a translation already in the file is a
            // translation of the same English wording - buying it again would pay twice
            GiveTheRepoADictionary(OneWord);
            GiveTheRepoASettledSheet();
            GiveTheRepoAnEnglishGlossary(("numerator", "the number above the line in a fraction"));

            GiveTheRepoAPolishDictionary(
                Entry("numerator", "the number above the line in a fraction",
                    "licznik", "liczba nad kreska ulamka"));

            _git.AddFile(PolishKey, ageCommits: 1, contents: TexFixtures.VocabularyKey(
                Root,
                new[]
                {
                    new VocabTerm
                    {
                        English              = "numerator",
                        Definition           = "the number above the line in a fraction",
                        Translation          = "licznikk",
                        TranslatedDefinition = "liczba nad kreska ulamka",
                    },
                    new VocabTerm
                    {
                        English              = "hypotenuse",
                        Definition           = "the long side",
                        Translation          = "przeciwprostokatna",
                        TranslatedDefinition = "najdluzszy bok",
                    },
                },
                TexFixtures.Polish));

            await Build().DoOnePassAsync();

            String? restated = Committed(PolishKey);

            Assert.IsNotNull(restated);
            StringAssert.Contains(restated, "przeciwprostokatna",
                "the word the dictionary says nothing about keeps what it had");

            Assert.AreEqual(0, _llm.StructuredPromptsSeen.Count, "and nothing was asked for");
        }

        [TestMethod]
        public async Task ChangingTheLayoutStatesThePolishKeyAgainRatherThanBuyingItAgain()
        {
            GiveTheRepoADictionary(OneWord);
            GiveTheRepoASettledSheet();
            GiveTheRepoAnEnglishGlossary(("numerator", "the number above the line in a fraction"));

            GiveTheRepoAPolishDictionary(
                Entry("numerator", "the number above the line in a fraction",
                    "licznik", "liczba nad kreska ulamka"));

            _git.AddFile(PolishKey, ageCommits: 1, contents: Regex.Replace(
                TexFixtures.VocabularyKey(
                    Root,
                    new[]
                    {
                        new VocabTerm
                        {
                            English              = "numerator",
                            Definition           = "the number above the line in a fraction",
                            Translation          = "licznik",
                            TranslatedDefinition = "liczba nad kreska ulamka",
                        },
                    },
                    TexFixtures.Polish),
                @"(key layout\s+version )\d+", "${1}1"));

            await Build().DoOnePassAsync();

            Assert.AreEqual(0, _git.RemoveFilesCalls.Count, "nothing was thrown away");

            Assert.AreEqual(0, _llm.StructuredPromptsSeen.Count, "and nothing was paid for");

            String? restated = Committed(PolishKey);

            Assert.IsNotNull(restated);
            Assert.IsTrue(
                L2Macros.MatchesSettings(
                    restated, new L2ColourOptions(),
                    isKey: true, fallbackFont: TexFixtures.FallbackFont),
                "the key is now built to the current layout");
        }

        [TestMethod]
        public async Task APolishKeyOlderThanTheEnglishOneIsRebuiltRatherThanStatedAgain()
        {
            // The one case that must stay a rebuild. The English key has moved, so the
            // words themselves may have changed and there is a genuine translation to
            // buy - and stating it again could land on the bytes already there, which is
            // a commit git has nothing to record, leaving the file exactly as stale as it
            // was and asking again every pass for ever.
            GiveTheRepoADictionary(OneWord);
            GiveTheRepoASettledSheet();

            GiveTheRepoAPolishDictionary(
                Entry("numerator", "the number above the line in a fraction",
                    "licznik", "liczba nad kreska ulamka"));

            var terms = new[]
            {
                new VocabTerm
                {
                    English              = "numerator",
                    Definition           = "the number above the line in a fraction",
                    Translation          = "licznik",
                    TranslatedDefinition = "liczba nad kreska ulamka",
                },
            };

            // the English key is younger than the Polish one made from it
            _git.AddFile(Vocab, ageCommits: 0, contents: TexFixtures.VocabularyKey(
                Root,
                terms.Select(t => new VocabTerm { English = t.English, Definition = t.Definition })
                     .ToList()));

            _git.AddFile(PolishKey, ageCommits: 3,
                contents: TexFixtures.VocabularyKey(Root, terms, TexFixtures.Polish));

            Assert.AreEqual(
                Orchestrator.PassOutcome.RemovedStaleFiles, await Build().DoOnePassAsync());

            CollectionAssert.Contains(_git.RemoveFilesCalls.Single().ToArray(), PolishKey);
        }

        #endregion

        #region When somebody breaks one

        [TestMethod]
        public async Task AnUnreadableEntryIsReportedThroughPingRatherThanSwallowed()
        {
            GiveTheRepoADictionary(OneWord);
            GiveTheRepoASettledSheet();

            _git.AddFile(PolishDictionary, ageCommits: 4,
                contents: @"\dictentrytr{numerator}{a definition}{licznik}");

            var orchestrator = Build();

            await orchestrator.DoOnePassAsync();

            var problems = orchestrator.Ping().Problems;

            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains(problems.Single(), PolishDictionary);
            StringAssert.Contains(problems.Single(), "four arguments");
        }

        [TestMethod]
        public async Task ADictionaryThatReadsCleanlyReportsNothing()
        {
            GiveTheRepoADictionary(OneWord);
            GiveTheRepoASettledSheet();

            GiveTheRepoAPolishDictionary(
                Entry("numerator", "the number above the line in a fraction",
                    "licznik", "liczba nad kreska ulamka"));

            var orchestrator = Build();

            await orchestrator.DoOnePassAsync();

            Assert.AreEqual(0, orchestrator.Ping().Problems.Count);
        }

        #endregion
    }
}
