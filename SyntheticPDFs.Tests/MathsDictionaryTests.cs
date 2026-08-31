using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Rendering;
using SyntheticPDFs.Tests.Fakes;

namespace SyntheticPDFs.Tests
{
    // the shared definitions, read from the content repository so a wording can be
    // discussed and changed there like anything else
    [TestClass]
    public class MathsDictionaryTests
    {
        private const String DictionaryPath = "latex/dictionary/mathematicalDictionary.tex";

        private static MathsDictionary Parse(String source) =>
            MathsDictionary.Parse(source, NullLogger<Orchestrator>.Instance);

        // ---- reading the file ----

        [TestMethod]
        public void EntriesAreReadOffTheMacro()
        {
            var dictionary = Parse(
                "\\documentclass{article}\n"
                + @"\dictentry{numerator}{the number above the line in a fraction}" + "\n"
                + @"\dictentry{area}{the amount of space inside a flat shape}");

            Assert.AreEqual(2, dictionary.Count);
            Assert.AreEqual("the number above the line in a fraction", dictionary.Define("numerator"));
        }

        [TestMethod]
        public void ADefinitionMayContainBracesOfItsOwn()
        {
            // a balanced-group read rather than a regex, so maths in a definition survives
            var dictionary = Parse(@"\dictentry{half}{one part of \(\frac{1}{2}\) a whole}");

            Assert.AreEqual(@"one part of \(\frac{1}{2}\) a whole", dictionary.Define("half"));
        }

        [TestMethod]
        public void CommentedOutEntriesAreNotRead()
        {
            // someone trying a wording out without committing to it
            var dictionary = Parse(
                @"% \dictentry{area}{an old wording nobody wanted}" + "\n"
                + @"\dictentry{area}{the wording we agreed}");

            Assert.AreEqual(1, dictionary.Count);
            Assert.AreEqual("the wording we agreed", dictionary.Define("area"));
        }

        [TestMethod]
        public void TheMacroDefinitionItselfIsNotReadAsAnEntry()
        {
            // the file defines \dictentry before using it, and that definition names no
            // word - reading it would put a nonsense entry in the dictionary
            var dictionary = Parse(
                @"\newcommand{\dictentry}[3][]{\textbf{#2} #3}" + "\n"
                + @"\dictentry{area}{the amount of space inside a flat shape}");

            Assert.AreEqual(1, dictionary.Count);
            Assert.IsTrue(dictionary.Defines("area"));
        }

        [TestMethod]
        public void TheSameWordTwiceKeepsTheFirstAndWarns()
        {
            var logger = new RecordingLogger();

            var dictionary = MathsDictionary.Parse(
                @"\dictentry{area}{first}" + "\n" + @"\dictentry{area}{second}", logger);

            Assert.AreEqual("first", dictionary.Define("area"));
            StringAssert.Contains(logger.Warnings.Single(), "area");
        }

        [TestMethod]
        public void AFileWithNoEntriesIsSimplyEmpty()
        {
            Assert.AreEqual(0, Parse("\\documentclass{article}\n\\begin{document}\\end{document}").Count);
        }

        // ---- word variants ----

        [TestMethod]
        [DataRow("numerator")]
        [DataRow("Numerator")]
        [DataRow("NUMERATOR")]
        [DataRow("numerators")]
        [DataRow("Numerators")]
        [DataRow(" numerator ")]
        [DataRow("numerator,")]
        public void CaseAndRegularPluralsFindTheSameEntry(String word)
        {
            var dictionary = Parse(@"\dictentry{numerator}{above the line}");

            Assert.AreEqual("above the line", dictionary.Define(word));
        }

        [TestMethod]
        [DataRow("identities", "identity")]
        [DataRow("boxes", "box")]
        [DataRow("squares", "square")]
        [DataRow("matches", "match")]
        public void RegularPluralsOfEveryShapeAreFound(String plural, String headword)
        {
            var dictionary = Parse($"\\dictentry{{{headword}}}{{the agreed wording}}");

            Assert.AreEqual("the agreed wording", dictionary.Define(plural));
        }

        [TestMethod]
        [DataRow("simplifies")]
        [DataRow("simplified")]
        [DataRow("simplifying")]
        public void RegularVerbEndingsFindTheirRoot(String form)
        {
            var dictionary = Parse(@"\dictentry{simplify}{write in its plainest form}");

            Assert.AreEqual("write in its plainest form", dictionary.Define(form));
        }

        [TestMethod]
        [DataRow("vertices", "vertex")]
        [DataRow("indices", "index")]
        [DataRow("radii", "radius")]
        [DataRow("axes", "axis")]
        [DataRow("formulae", "formula")]
        [DataRow("matrices", "matrix")]
        public void LatinAndGreekPluralsAreKnownWithoutBeingListed(String plural, String headword)
        {
            // mathematics has more of these than most subjects, so they are built in
            var dictionary = Parse($"\\dictentry{{{headword}}}{{the agreed wording}}");

            Assert.AreEqual("the agreed wording", dictionary.Define(plural));
        }

        [TestMethod]
        public void AnEntryMayListFormsOfItsOwn()
        {
            var dictionary = Parse(
                @"\dictentry[HCF, highest factor]{highest common factor}{the largest common divisor}");

            Assert.AreEqual("the largest common divisor", dictionary.Define("HCF"));
            Assert.AreEqual("the largest common divisor", dictionary.Define("hcf"));
            Assert.AreEqual("the largest common divisor", dictionary.Define("highest factor"));
            Assert.AreEqual("the largest common divisor", dictionary.Define("highest common factor"));
        }

        [TestMethod]
        public void AWordWeDoNotDefineIsLeftAlone()
        {
            var dictionary = Parse(@"\dictentry{numerator}{above the line}");

            Assert.IsNull(dictionary.Define("hypotenuse"));
        }

        [TestMethod]
        [DataRow("ring")]
        [DataRow("mass")]
        [DataRow("is")]
        [DataRow("sines")]
        public void AnAggressiveRuleCannotInventAMatch(String word)
        {
            // stripping "ing" from "ring" suggests "r", "s" from "is" suggests "i", and
            // "es" from "sines" suggests "sin". none of those is a headword, so nothing
            // matches - which is exactly what lets the rules be aggressive in the first
            // place, since a wrong stem costs nothing
            var dictionary = Parse(
                @"\dictentry{bracket}{a symbol that groups part of an expression}" + "\n"
                + @"\dictentry{area}{the amount of space inside a flat shape}");

            Assert.IsNull(dictionary.Define(word));
        }

        [TestMethod]
        public void AVerbEndingThatDoesReachAHeadwordIsMatched()
        {
            // the other side of the same rule - "bracketing" really is "bracket"
            var dictionary = Parse(@"\dictentry{bracket}{a symbol that groups part of an expression}");

            Assert.AreEqual(
                "a symbol that groups part of an expression", dictionary.Define("bracketing"));
        }

        [TestMethod]
        public void AHeadwordThatIsItselfAPluralWinsOverTheRule()
        {
            var dictionary = Parse(
                @"\dictentry{axes}{the lines a graph is measured against}" + "\n"
                + @"\dictentry{axis}{one such line}");

            Assert.AreEqual("the lines a graph is measured against", dictionary.Define("axes"));
        }

        // ---- the shipped dictionary ----

        // The copy under docs/contentRepo is the one committed here - the live dictionary
        // lives in the content repository and is free to move ahead of it. What this
        // pins is the format: if the convention the parser reads ever drifts from the
        // file people are told to copy, this is what says so.
        [TestMethod]
        public void TheShippedDictionaryParsesAndCoversItsOwnHeadwords()
        {
            String path = Path.Combine(
                RepositoryRoot(), "docs", "contentRepo",
                "latex", "dictionary", "mathematicalDictionary.tex");

            Assert.IsTrue(File.Exists(path), $"expected the dictionary at {path}");

            var dictionary = MathsDictionary.Parse(File.ReadAllText(path));

            Assert.IsTrue(dictionary.Count > 60,
                $"expected a useful number of entries, found {dictionary.Count}");

            // the words the pipeline is most likely to meet
            foreach (String word in new[]
            {
                "numerators", "Denominator", "fractions", "vertices", "simplifying",
                "perimeter", "hypotenuse", "coefficients", "indices",
            })
            {
                Assert.IsTrue(dictionary.Defines(word), $"the dictionary should cover '{word}'");
            }
        }

        private static String RepositoryRoot()
        {
            DirectoryInfo? at = new DirectoryInfo(AppContext.BaseDirectory);

            while (at is not null && !File.Exists(Path.Combine(at.FullName, "WebSheets.sln")))
            {
                at = at.Parent;
            }

            Assert.IsNotNull(at, "could not find the repository root");

            return at.FullName;
        }

        // ---- what a change to it does ----

        private FakeGitRepoManager _git = null!;
        private FakeLLMService _llm = null!;

        private Orchestrator Build()
        {
            var options = new L2Options { GenerateVocabularyKeys = true };

            options.Languages["pol"] = new LanguageOptions
                { Font = "Noto Serif", BabelName = "polish" };

            return new Orchestrator(
                NullLogger<Orchestrator>.Instance,
                _git,
                _llm,
                Options.Create(new GenerationOptions { MaxFilesPerRun = 30 }),
                Options.Create(options));
        }

        private void Repo(String dictionary, String definitionInTheKey)
        {
            _git = new FakeGitRepoManager();
            _llm = new FakeLLMService();

            _git.AddFile(DictionaryPath, 9, dictionary);

            foreach (String root in new[] { "latex/worksheets/a", "latex/worksheets/b" })
            {
                _git.AddFile(root + ".tex", 4);
                _git.AddFile(root + "_workedSolutions.tex", 3);
                _git.AddFile(root + "_solutions.tex", 2);
            }

            // a uses "numerator"; b uses a word the dictionary says nothing about
            _git.AddFile("latex/worksheets/a_vocab.tex", 1, TexFixtures.VocabularyKey(
                "latex/worksheets/a",
                new[] { new VocabTerm { English = "numerator", Definition = definitionInTheKey } }));

            _git.AddFile("latex/worksheets/b_vocab.tex", 1, TexFixtures.VocabularyKey(
                "latex/worksheets/b",
                new[] { new VocabTerm { English = "hypotenuse", Definition = "the long side" } }));
        }

        [TestMethod]
        public async Task RewordingADefinitionRebuildsOnlyTheKeysThatUseThatWord()
        {
            // the whole point of comparing content rather than commit age: an edit to the
            // dictionary must not invalidate every key in the repository
            Repo(@"\dictentry{numerator}{a NEW wording}", "the old wording");

            Assert.AreEqual(Orchestrator.PassOutcome.RemovedStaleFiles, await Build().DoOnePassAsync());

            var removed = _git.RemoveFilesCalls.Single();

            CollectionAssert.AreEqual(new[] { "latex/worksheets/a_vocab.tex" }, removed.ToArray(),
                "only the key using the reworded word goes");
        }

        [TestMethod]
        public async Task AKeyThatAgreesWithTheDictionaryIsLeftAlone()
        {
            Repo(@"\dictentry{numerator}{the settled wording}", "the settled wording");

            Orchestrator orchestrator = Build();

            // there is one thing to do first: b's key uses a word the dictionary has
            // never heard of, and that word is taken into it
            Assert.AreEqual(
                Orchestrator.PassOutcome.Generated, await orchestrator.DoOnePassAsync());

            Assert.AreEqual(
                Orchestrator.PassOutcome.NothingToDo, await orchestrator.DoOnePassAsync());

            Assert.AreEqual(0, _git.RemoveFilesCalls.Count, "and no key was rebuilt to do it");
        }

        // ---- words the sheets know that the dictionary does not ----

        [TestMethod]
        public async Task AWordAKeyDefinesThatTheDictionaryHasNotIsAddedToIt()
        {
            Repo(@"\dictentry{numerator}{the settled wording}", "the settled wording");

            await Build().DoOnePassAsync();

            String dictionary = _git.Contents[DictionaryPath];

            StringAssert.Contains(dictionary, @"\dictentry{hypotenuse}{the long side}",
                "with the wording the key that met it was written with");

            StringAssert.Contains(dictionary, MathsDictionaryWriter.SectionMarker,
                "under a heading saying nobody has checked it yet");

            StringAssert.Contains(dictionary, @"\dictentry{numerator}{the settled wording}",
                "and what was already there is untouched");
        }

        [TestMethod]
        public async Task AddingAWordDoesNotMakeTheKeyThatSuppliedItStale()
        {
            // the definition that goes into the dictionary is the one the key already
            // shows, so the key still agrees with it and costs nothing to keep
            Repo(@"\dictentry{numerator}{the settled wording}", "the settled wording");

            Orchestrator orchestrator = Build();

            await orchestrator.DoOnePassAsync();

            Assert.AreEqual(
                Orchestrator.PassOutcome.NothingToDo, await orchestrator.DoOnePassAsync());

            Assert.AreEqual(0, _git.RemoveFilesCalls.Count);
        }

        [TestMethod]
        public async Task AWordTwoSheetsDefineDifferentlyIsSettledTheSameWayEveryTime()
        {
            // a word can arrive with two wordings, and which one the repository ends up
            // agreeing on must not depend on the order it happened to be walked in
            _git = new FakeGitRepoManager();
            _llm = new FakeLLMService();

            _git.AddFile(DictionaryPath, 9, String.Empty);

            foreach (String root in new[] { "latex/worksheets/b", "latex/worksheets/a" })
            {
                _git.AddFile(root + ".tex", 4);
                _git.AddFile(root + "_workedSolutions.tex", 3);
                _git.AddFile(root + "_solutions.tex", 2);

                _git.AddFile(root + "_vocab.tex", 1, TexFixtures.VocabularyKey(
                    root,
                    new[] { new VocabTerm { English = "vertex", Definition = "as " + root } }));
            }

            await Build().DoOnePassAsync();

            StringAssert.Contains(
                _git.Contents[DictionaryPath],
                @"\dictentry{vertex}{as latex/worksheets/a}",
                "the sheet that comes first alphabetically supplies the wording");
        }

        // ---- writing them into the file ----

        private const String HandWritten =
            "% a comment somebody wrote\n"
            + "\\documentclass{article}\n"
            + "\\begin{document}\n"
            + "\\dictentry{numerator}{the number above the line}\n"
            + "\\end{document}\n";

        [TestMethod]
        public void AWordGoesInsideTheDocumentRatherThanAfterIt()
        {
            String written = MathsDictionaryWriter.Add(HandWritten, Words(("vertex", "a corner")));

            int entry = written.IndexOf(@"\dictentry{vertex}", StringComparison.Ordinal);
            int end = written.IndexOf(@"\end{document}", StringComparison.Ordinal);

            Assert.IsTrue(entry > 0 && entry < end, "or the file would stop compiling");

            StringAssert.Contains(written, "% a comment somebody wrote",
                "and nothing already in the file is disturbed");
        }

        [TestMethod]
        public void TheHeadingIsWrittenOnceHoweverManyWordsArriveLater()
        {
            String once = MathsDictionaryWriter.Add(HandWritten, Words(("vertex", "a corner")));
            String twice = MathsDictionaryWriter.Add(once, Words(("edge", "a side")));

            Assert.AreEqual(1, Occurrences(twice, MathsDictionaryWriter.SectionMarker));

            StringAssert.Contains(twice, @"\dictentry{vertex}");
            StringAssert.Contains(twice, @"\dictentry{edge}");
        }

        [TestMethod]
        public void AWordJoinsTheOtherEntriesRatherThanTrailingTheFile()
        {
            // the shipped dictionary sets its entries in three columns. a word added
            // after \end{multicols} would compile, and would come out across the whole
            // width of the page looking like a mistake
            String shipped = File.ReadAllText(Path.Combine(
                RepositoryRoot(), "docs", "contentRepo",
                "latex", "dictionary", "mathematicalDictionary.tex"));

            String written = MathsDictionaryWriter.Add(shipped, Words(("googol", "a big number")));

            int entry = written.IndexOf(@"\dictentry{googol}", StringComparison.Ordinal);
            int columns = written.IndexOf(@"\end{multicols}", StringComparison.Ordinal);

            Assert.IsTrue(entry > 0, "the word was written");
            Assert.IsTrue(entry < columns, "and it is inside the columns the rest are in");

            StringAssert.Contains(written, @"\dicttopic{Words not yet checked}",
                "under a heading a reader of the pdf can see, since this file has them");
        }

        [TestMethod]
        public void AHeadingIsOnlyUsedWhereTheFileAlreadyHasThem()
        {
            // inventing a macro the file has not defined would stop it compiling
            String written = MathsDictionaryWriter.Add(HandWritten, Words(("vertex", "a corner")));

            Assert.IsFalse(written.Contains(@"\dicttopic", StringComparison.Ordinal));
            StringAssert.Contains(written, MathsDictionaryWriter.SectionMarker);
        }

        [TestMethod]
        public void AddingNothingLeavesTheFileExactlyAsItWas()
        {
            Assert.AreEqual(
                HandWritten,
                MathsDictionaryWriter.Add(
                    HandWritten, Array.Empty<KeyValuePair<String, String>>()));
        }

        [TestMethod]
        public void ADefinitionSurvivesBeingWrittenAndReadBack()
        {
            // a model writes prose, and prose contains characters LaTeX reads as markup.
            // a stray per cent sign would comment out the rest of its own entry
            const String awkward = "50% of a whole, & the # of parts it is cut into";

            String written = MathsDictionaryWriter.Add(HandWritten, Words(("share", awkward)));

            MathsDictionary read = MathsDictionary.Parse(written);

            Assert.AreEqual(awkward, read.Define("share"));

            // and again, so that a rewritten entry does not collect another backslash
            String rewritten = MathsDictionaryWriter.Add(written, Words(("part", awkward)));

            Assert.AreEqual(awkward, MathsDictionary.Parse(rewritten).Define("share"));
        }

        private static IReadOnlyList<KeyValuePair<String, String>> Words(
            params (String Word, String Meaning)[] words) =>
            words
                .Select(w => new KeyValuePair<String, String>(w.Word, w.Meaning))
                .ToList();

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

        [TestMethod]
        public async Task AKeyUsingAVariantOfARewordedWordIsAlsoRebuilt()
        {
            // the key says "Numerators", the dictionary says "numerator" - the same word
            _git = new FakeGitRepoManager();
            _llm = new FakeLLMService();

            _git.AddFile(DictionaryPath, 9, @"\dictentry{numerator}{a NEW wording}");
            _git.AddFile("latex/worksheets/a.tex", 4);
            _git.AddFile("latex/worksheets/a_workedSolutions.tex", 3);
            _git.AddFile("latex/worksheets/a_solutions.tex", 2);
            _git.AddFile("latex/worksheets/a_vocab.tex", 1, TexFixtures.VocabularyKey(
                "latex/worksheets/a",
                new[] { new VocabTerm { English = "Numerators", Definition = "the old wording" } }));

            Assert.AreEqual(Orchestrator.PassOutcome.RemovedStaleFiles, await Build().DoOnePassAsync());

            CollectionAssert.Contains(_git.RemoveFilesCalls.Single(), "latex/worksheets/a_vocab.tex");
        }

        [TestMethod]
        public async Task ARewordingCarriesThroughToTheTranslationsOfThatKey()
        {
            // staleness is transitive, so a translated key built from a reworded English
            // one goes with it
            Repo(@"\dictentry{numerator}{a NEW wording}", "the old wording");

            _git.AddFile("latex/worksheets/a/L2/pol/a_polishKey.tex", 0, TexFixtures.VocabularyKey(
                "latex/worksheets/a",
                new[] { new VocabTerm { English = "numerator", Definition = "the old wording" } },
                TexFixtures.Polish));

            await Build().DoOnePassAsync();

            var removed = _git.RemoveFilesCalls.Single();

            CollectionAssert.Contains(removed, "latex/worksheets/a_vocab.tex");
            CollectionAssert.Contains(removed, "latex/worksheets/a/L2/pol/a_polishKey.tex");
        }
    }
}
