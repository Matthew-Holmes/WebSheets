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

            Assert.AreEqual(Orchestrator.PassOutcome.NothingToDo, await Build().DoOnePassAsync());
            Assert.AreEqual(0, _git.RemoveFilesCalls.Count);
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
