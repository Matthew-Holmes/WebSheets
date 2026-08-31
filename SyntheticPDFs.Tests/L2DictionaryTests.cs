using SyntheticPDFs.Configuration;
using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Rendering;
using SyntheticPDFs.Tests.Fakes;

namespace SyntheticPDFs.Tests
{
    // The translated dictionary is a cache, so what matters is that it survives a round
    // trip through a .tex file exactly. A word whose English definition comes back even
    // slightly changed looks reworded, and is then translated again for nothing.
    [TestClass]
    public class L2DictionaryTests
    {
        private static readonly ISO639_3Code Pol = new("pol");

        private const String File = "latex/dictionary/L2/pol/mathematicalDictionary_polish.tex";

        private static L2Dictionary Parse(String tex, out List<DictionaryProblem> problems)
        {
            problems = new List<DictionaryProblem>();

            return L2Dictionary.Parse(tex, Pol, File, problems);
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

        private static String Rendered(params L2DictionaryEntry[] entries) =>
            L2DictionaryRenderer.Render(
                L2Dictionary.Empty(Pol).With(entries),
                TexFixtures.Polish,
                new L2ColourOptions(),
                builtFrom: "latex/dictionary/mathematicalDictionary.tex");

        #region Round tripping

        [TestMethod]
        public void AnEntrySurvivesBeingWrittenAndReadBack()
        {
            var entry = Entry("numerator", "the number above the line in a fraction",
                "licznik", "liczba nad kreska ulamka");

            var read = Parse(Rendered(entry), out var problems);

            Assert.AreEqual(0, problems.Count);

            var back = read.Find("numerator");

            Assert.IsNotNull(back);
            Assert.AreEqual(entry.English, back.English);
            Assert.AreEqual(entry.Word, back.Word);
            Assert.AreEqual(entry.Definition, back.Definition);
        }

        [TestMethod]
        public void CharactersLatexWouldReadAsMarkupSurviveTheRoundTrip()
        {
            // without an exact inverse of the escaping, a definition mentioning a
            // percentage picks up another backslash every time the file is rewritten,
            // and looks reworded every time as well
            var entry = Entry("percentage", "a number out of 100, written with a % sign",
                "procent", "liczba na 100, ze znakiem % i podkresleniem _");

            var read = Parse(Rendered(entry), out var problems);

            Assert.AreEqual(0, problems.Count);
            Assert.AreEqual(entry.English, read.Find("percentage")!.English);
            Assert.AreEqual(entry.Definition, read.Find("percentage")!.Definition);
        }

        [TestMethod]
        public void RewritingWhatWasReadChangesNothing()
        {
            // the pass rewrites a dictionary whenever anything about it changes, so a
            // rewrite that changed the file on its own would commit on every pass forever
            var entries = new[]
            {
                Entry("numerator", "the number above the line in a fraction",
                    "licznik", "liczba nad kreska ulamka"),
                Entry("vertex", "a corner where two or more edges meet",
                    "wierzcholek", "rog, w ktorym spotykaja sie krawedzie"),
            };

            String first = Rendered(entries);

            var read = Parse(first, out _);

            String second = L2DictionaryRenderer.Render(
                read, TexFixtures.Polish, new L2ColourOptions(),
                builtFrom: "latex/dictionary/mathematicalDictionary.tex");

            Assert.AreEqual(first, second);
        }

        [TestMethod]
        public void TheFileSaysWhichHalfOfItIsSafeToEdit()
        {
            // it is the only place somebody editing it will look
            String tex = Rendered(Entry("numerator", "a definition", "licznik", "definicja"));

            StringAssert.Contains(tex, "What is safe to edit in this file");
            StringAssert.Contains(tex, "Not safe:");
        }

        #endregion

        #region Deciding what is still current

        [TestMethod]
        public void ATranslationIsCurrentOnlyWhileItsEnglishIsUnchanged()
        {
            var dictionary = L2Dictionary.Empty(Pol).With(new[]
            {
                Entry("numerator", "the number above the line in a fraction",
                    "licznik", "liczba nad kreska ulamka"),
            });

            Assert.IsNotNull(
                dictionary.Current("numerator", "the number above the line in a fraction"),
                "unchanged English means the translation still says the same thing");

            Assert.IsNull(
                dictionary.Current("numerator", "the top number of a fraction"),
                "reworded English means the translation is of something else now");
        }

        [TestMethod]
        public void AWordTheSharedDictionaryHasDroppedIsRemovedRatherThanKept()
        {
            var dictionary = L2Dictionary.Empty(Pol)
                .With(new[]
                {
                    Entry("numerator", "a definition", "licznik", "definicja"),
                    Entry("vertex", "a definition", "wierzcholek", "definicja"),
                })
                .Without(new[] { "vertex" });

            Assert.IsTrue(dictionary.Has("numerator"));
            Assert.IsFalse(dictionary.Has("vertex"));
        }

        [TestMethod]
        public void APluralInASheetStillReachesItsHeadword()
        {
            // the shared dictionary knows the forms; the translated one only ever holds
            // headwords, so a lookup that skipped the first would miss every plural
            var state = DictionaryState.Empty("latex/dictionary/mathematicalDictionary") with
            {
                Definitions = MathsDictionary.Parse(
                    @"\dictentry{numerator}{the number above the line in a fraction}"),
                Translated  = new Dictionary<ISO639_3Code, L2Dictionary>
                {
                    [Pol] = L2Dictionary.Empty(Pol).With(new[]
                    {
                        Entry("numerator", "the number above the line in a fraction",
                            "licznik", "liczba nad kreska ulamka"),
                    }),
                },
            };

            var found = state.Lookup(
                Pol, "numerators", "the number above the line in a fraction");

            Assert.IsNotNull(found);
            Assert.AreEqual("licznik", found.Word);
        }

        #endregion

        #region What happens when somebody breaks it

        [TestMethod]
        public void AnEntryMissingAnArgumentIsReportedRatherThanGuessedAt()
        {
            String tex = @"\dictentrytr{numerator}{a definition}{licznik}" + "\n"
                + @"\dictentrytr{vertex}{a definition}{wierzcholek}{definicja}";

            var read = Parse(tex, out var problems);

            Assert.AreEqual(1, problems.Count, "the broken one is named");
            StringAssert.Contains(problems.Single().Message, "four arguments");

            Assert.IsTrue(read.Has("vertex"), "the entries either side of it still work");
        }

        [TestMethod]
        public void TheSameWordTwiceIsReportedAndTheFirstWins()
        {
            String tex = @"\dictentrytr{numerator}{a definition}{licznik}{pierwsza}" + "\n"
                + @"\dictentrytr{numerator}{a definition}{licznik}{druga}";

            var read = Parse(tex, out var problems);

            Assert.AreEqual(1, problems.Count);
            Assert.AreEqual("pierwsza", read.Find("numerator")!.Definition);
        }

        [TestMethod]
        public void ACommentedOutEntryIsNotAnEntry()
        {
            String tex = @"% \dictentrytr{numerator}{a definition}{licznik}{definicja}";

            var read = Parse(tex, out var problems);

            Assert.AreEqual(0, read.Count);
            Assert.AreEqual(0, problems.Count);
        }

        #endregion
    }
}
