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
        public void NoDefinitionSitsFarFromTheWordItBelongsTo()
        {
            // what keeps the answer page readable: a bounded shuffle means a bounded
            // slope, and a straight line that never sweeps across the others
            for (int count = 2; count <= 40; count++)
            {
                var terms = Enumerable.Range(0, count)
                    .Select(i => new VocabTerm { English = $"word{i}", Definition = $"def{i}" })
                    .ToList();

                for (int seed = 0; seed < 25; seed++)
                {
                    var shuffled = L2VocabData.Shuffled(terms, $"sheet{seed}");

                    CollectionAssert.AreEquivalent(terms, shuffled,
                        $"{count} terms, seed {seed}: every term must still be there");

                    for (int row = 0; row < count; row++)
                    {
                        int from = terms.IndexOf(shuffled[row]);

                        Assert.IsTrue(
                            Math.Abs(from - row) <= L2VocabData.MaxDisplacement,
                            $"{count} terms, seed {seed}: {shuffled[row].English} moved "
                            + $"from row {from} to row {row}");
                    }
                }
            }
        }

        [TestMethod]
        public void ADefinitionIsNotLeftBesideItsOwnWord()
        {
            // it would be a free answer, and a bounded shuffle leaves one in place far
            // more often than an unbounded one did
            var terms = Enumerable.Range(0, 12)
                .Select(i => new VocabTerm { English = $"word{i}", Definition = $"def{i}" })
                .ToList();

            for (int seed = 0; seed < 25; seed++)
            {
                var shuffled = L2VocabData.Shuffled(terms, $"sheet{seed}");

                for (int row = 0; row < terms.Count; row++)
                {
                    Assert.AreNotEqual(terms[row], shuffled[row],
                        $"seed {seed}: row {row} was left in place");
                }
            }
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

        [TestMethod]
        public void RestylingTheKeyDatesTheKeysAndNothingElse()
        {
            // the key layout is versioned apart from the shared macros so that
            // restyling it does not rebuild every parallel text sheet as well - each
            // of those costs an API call and would come back identical
            var colours = new L2ColourOptions();

            String sheet = L2Macros.ProvenanceBlock("title", colours, null, RootEx, null);
            String key = L2Macros.ProvenanceBlock(
                "title", colours, null, RootEx, null, isKey: true);

            Assert.IsTrue(L2Macros.MatchesSettings(sheet, colours),
                "a sheet says nothing about the key layout and does not need to");
            Assert.IsTrue(L2Macros.MatchesSettings(key, colours, isKey: true));

            Assert.IsFalse(L2Macros.MatchesSettings(sheet, colours, isKey: true),
                "a key written before the layout was versioned cannot be shown current");
        }

        // ---- the rendered key ----

        [TestMethod]
        public void TheKeyRepeatsItselfWithoutRepeatingItsSource()
        {
            // how many copies fit on a page is decided when the file is compiled, so
            // the source says each body once and \ealpage does the repeating
            var terms = new List<VocabTerm>
            {
                new() { English = "numerator", Definition = "above the line" },
                new() { English = "denominator", Definition = "below the line" },
            };

            String tex = L2VocabKeyRenderer.Render(
                terms,
                new L2Macros.SourceMetadataTitle(Root, SheetPart.Root, SheetForm.Glossary),
                new L2ColourOptions(), null, RootEx, null);

            Assert.AreEqual(1, Occurrences(tex, @"\newcommand{\ealkeybody}"),
                "the key body is defined once");
            Assert.AreEqual(1, Occurrences(tex, "key vocabulary"),
                "and its contents appear once in the source, not twice over");
            Assert.AreEqual(1, Occurrences(tex, @"\newcommand{\ealmatchrows}"),
                "the match-up rows are written once and used by both of its pages");

            StringAssert.Contains(tex, @"\ealpage{\ealkeybody}");
            StringAssert.Contains(tex, @"\ealpage{\ealmatchbody}");
            StringAssert.Contains(tex, @"\ealpage{\ealanswerbody}");
            StringAssert.Contains(tex, L2Macros.CompilerDirective);
        }

        [TestMethod]
        public void TheKeyIsInAlphabeticalOrder()
        {
            // a key is looked up by its English word, so that is what it is ordered by
            var terms = new List<VocabTerm>
            {
                new() { English = "numerator", Definition = "above the line" },
                new() { English = "denominator", Definition = "below the line" },
                new() { English = "fraction", Definition = "parts of a whole" },
            };

            String tex = L2VocabKeyRenderer.Render(
                terms,
                new L2Macros.SourceMetadataTitle(Root, SheetPart.Root, SheetForm.Glossary),
                new L2ColourOptions(), null, RootEx, null);

            var rows = Regex.Matches(tex, @"\\ealentry\{(\w+)\}")
                .Select(m => m.Groups[1].Value)
                .ToList();

            CollectionAssert.AreEqual(
                new[] { "denominator", "fraction", "numerator" }, rows);

            var carried = L2VocabData.ReadBlock(tex);

            Assert.IsNotNull(carried);
            CollectionAssert.AreEqual(
                new[] { "denominator", "fraction", "numerator" },
                carried.Select(t => t.English).ToList(),
                "the data block is in the same order as the table it was written with");
        }

        [TestMethod]
        public void EveryAnswerLineJoinsAWordToItsOwnMeaning()
        {
            // the answers are drawn from the shuffle rather than read back off it, so
            // an off-by-one here would ship a page of confidently wrong answers
            AssertTheAnswersAreRight(language: null, word: 1, meaning: 2);
        }

        [TestMethod]
        public void AMirroredAnswerLineStillJoinsAWordToItsOwnMeaning()
        {
            // a right to left key has the columns the other way round, so the lines
            // are drawn between the other two edges. getting one of the pair and not
            // the other would draw them across the text instead of across the gutter.
            AssertTheAnswersAreRight(Urdu, word: 2, meaning: 1);
        }

        private static readonly LanguageProfile Urdu = new()
        {
            Code        = new ISO639_3Code("urd"),
            EnglishName = "urdu",
            Font        = "Noto Nastaliq Urdu",
            BabelName   = "urdu",
            RightToLeft = true,
        };

        private static void AssertTheAnswersAreRight(
            LanguageProfile? language, int word, int meaning)
        {
            var terms = Enumerable.Range(0, 9)
                .Select(i => new VocabTerm
                {
                    English              = $"word{i}",
                    Definition           = $"the meaning of word{i}",
                    Translation          = $"tr{i}",
                    TranslatedDefinition = $"the meaning of word{i}",
                })
                .ToList();

            String tex = L2VocabKeyRenderer.Render(
                terms,
                new L2Macros.SourceMetadataTitle(Root, SheetPart.Root, SheetForm.Glossary),
                new L2ColourOptions(), language, RootEx, null);

            List<String> rows = MatchUpRows(tex);

            Assert.AreEqual(terms.Count, rows.Count);

            // both ends face the gutter, whichever side of it each column is on
            String leavingTheWord = word == 1 ? "east" : "west";
            String reachingTheMeaning = word == 1 ? "west" : "east";

            var lines = Regex.Matches(
                    tex,
                    $@"\(m-(\d+)-{word}\.{leavingTheWord}\) -- "
                    + $@"\(m-(\d+)-{meaning}\.{reachingTheMeaning}\)")
                .Select(m => (Word: int.Parse(m.Groups[1].Value),
                              Meaning: int.Parse(m.Groups[2].Value)))
                .ToList();

            Assert.AreEqual(terms.Count, lines.Count, "one line per word");
            Assert.AreEqual(terms.Count, lines.Select(l => l.Meaning).Distinct().Count(),
                "and no two words pointing at the same meaning");

            foreach (var line in lines)
            {
                String english = Regex.Match(
                    rows[line.Word - 1], @"ealkey\{(\w+)\}").Groups[1].Value;

                StringAssert.Contains(rows[line.Meaning - 1], $"the meaning of {english}",
                    $"the line from row {line.Word} points at the wrong meaning");
            }
        }

        // the rows of the match-up grid, which both of its pages are drawn from
        private static List<String> MatchUpRows(String tex)
        {
            int open = tex.IndexOf(@"\newcommand{\ealmatchrows}", StringComparison.Ordinal);

            String block = tex[open..tex.IndexOf(@"\newcommand{\ealmatchbody}", StringComparison.Ordinal)];

            return block.Split(@"\\", StringSplitOptions.TrimEntries)
                .Where(line => line.Contains(@"\ealnextcol", StringComparison.Ordinal))
                .ToList();
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
                new L2Macros.SourceMetadataTitle(Root, SheetPart.Root, SheetForm.Glossary),
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
                new L2Macros.SourceMetadataTitle(Root, SheetPart.Root, SheetForm.Glossary),
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

            VocabTerm missed = terms.Single(t => t.English == "denominator");

            Assert.AreEqual("denominator", missed.Translation,
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
