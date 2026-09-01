using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Rendering;
using SyntheticPDFs.Tests.Fakes;

namespace SyntheticPDFs.Tests
{
    // The variant of a deck of starters for a school that calls them something else.
    //
    // It is a rename rather than a rewrite, and these are mostly about the difference:
    // that the titles change, that nothing else does, and that no model is asked to have
    // an opinion about a file it could damage.
    [TestClass]
    public class RetrieveAndConnectTests
    {
        private const String Deck = "latex/starters/KS3/circlesArea.tex";
        private const String DeckWorked = "latex/starters/KS3/circlesArea_workedSolutions.tex";

        private const String DeckVariant =
            "latex/starters/KS3/circlesArea_retrieveAndConnect.tex";

        private const String WorkedVariant =
            "latex/starters/KS3/circlesArea_workedSolutions_retrieveAndConnect.tex";

        #region One title at a time

        // The slide's own title, so the number goes: on the board it says what the
        // school calls it and nothing else.
        [TestMethod]
        [DataRow("Starter 1", "Retrieve and Connect")]
        [DataRow("Starter", "Retrieve and Connect")]
        [DataRow("Starter 12", "Retrieve and Connect")]
        [DataRow("Starter 3 --", "Retrieve and Connect")]
        [DataRow("Starter 4:", "Retrieve and Connect")]
        public void AStartersOwnTitleLosesItsNumber(String title, String expected)
        {
            Assert.AreEqual(expected, RetrieveAndConnect.Retitle(title, own: true));
        }

        // What a title says beyond naming the starter is the teacher's, not ours - an
        // answers slide retitled "Retrieve and Connect" would be a lie about itself.
        [TestMethod]
        [DataRow("Starter: Answers", "Retrieve and Connect: Answers")]
        [DataRow("Starter -- Solutions", "Retrieve and Connect -- Solutions")]
        [DataRow("Starter 3: Comparing Fractions", "Retrieve and Connect: Comparing Fractions")]
        [DataRow("Year 8 Algebra Starters", "Year 8 Algebra Retrieve and Connects")]
        public void TheRestOfATitleIsKept(String title, String expected)
        {
            Assert.AreEqual(expected, RetrieveAndConnect.Retitle(title, own: true));
        }

        // A title pointing at one question of one starter is only useful while it still
        // says which starter, and a contents slide has to tell them apart as well.
        [TestMethod]
        [DataRow(true, "Starter 3 -- Q2: Mean", "Retrieve and Connect 3 -- Q2: Mean")]
        [DataRow(true, "Starter 1 -- Question 4", "Retrieve and Connect 1 -- Question 4")]
        [DataRow(true, "Worked Solution: Starter 1, Question 1",
            "Worked Solution: Retrieve and Connect 1, Question 1")]
        [DataRow(false, "Starter 1", "Retrieve and Connect 1")]
        public void ATitleThatPointsAtOneStarterKeepsItsNumber(
            bool own, String title, String expected)
        {
            Assert.AreEqual(expected, RetrieveAndConnect.Retitle(title, own));
        }

        [TestMethod]
        [DataRow("Contents")]
        [DataRow("Answers")]
        [DataRow("Automated check: answers may need attention")]
        public void ATitleThatNamesNoStarterIsLeftExactlyAsItIs(String title)
        {
            Assert.AreEqual(title, RetrieveAndConnect.Retitle(title, own: true));
        }

        #endregion

        #region A whole deck

        private const String ADeck =
            "\\documentclass{beamer}\n"
            + "\\title{Year 8 Starters}\n"
            + "\\begin{document}\n"
            + "\\frame{\\titlepage}\n"
            + "\\begin{frame}{Contents}\n"
            + "\\item \\hyperlink{q-st1}{\\beamergotobutton{Starter 1}}\n"
            + "\\end{frame}\n"
            + "\\begin{frame}[label=starter1qs]{Starter 1}\n"
            + "\\hypertarget{q-st1}{}\n"
            + "What is $2+2$? A starter question about \\ashow{4}.\n"
            + "\\end{frame}\n"
            + "\\end{document}";

        [TestMethod]
        public void EveryKindOfTitleInADeckIsHandledAtOnce()
        {
            String rewritten = RetrieveAndConnect.Rewrite(ADeck, "x.tex")!;

            Assert.IsNotNull(rewritten);

            StringAssert.Contains(rewritten, @"\title{Year 8 Retrieve and Connects}");
            StringAssert.Contains(rewritten, @"\begin{frame}[label=starter1qs]{Retrieve and Connect}");
            StringAssert.Contains(rewritten, @"\beamergotobutton{Retrieve and Connect 1}",
                "the contents slide still has to tell them apart");
            StringAssert.Contains(rewritten, @"\begin{frame}{Contents}",
                "a title that names no starter is untouched");
        }

        // The whole reason this is code and not a prompt. A model asked to hand back a
        // beamer deck with one word changed will also reflow a picture or drop an
        // overlay somewhere in the middle of it.
        [TestMethod]
        public void NothingOutsideATitleIsTouched()
        {
            String rewritten = RetrieveAndConnect.Rewrite(ADeck, "x.tex")!;

            var titles = new Regex(
                @"\\begin\{frame\}(\[[^\]]*\])?\{[^}]*\}|\\title\{[^}]*\}|\\beamergotobutton\{[^}]*\}");

            Assert.AreEqual(
                titles.Replace(ADeck, "@"),
                titles.Replace(WithoutTheHeader(rewritten), "@"));
        }

        [TestMethod]
        public void ALabelIsNotRenamedWithTheTitleItSitsBeside()
        {
            String rewritten = RetrieveAndConnect.Rewrite(ADeck, "x.tex")!;

            StringAssert.Contains(rewritten, "label=starter1qs",
                "renaming a label would break every link that points at it");
            StringAssert.Contains(rewritten, @"\hypertarget{q-st1}{}");
            StringAssert.Contains(rewritten, "A starter question about",
                "and the questions are not titles");
        }

        // A frame with no title of its own may perfectly well open its body with a
        // group, and reading that as a title would rewrite the first line of the slide.
        [TestMethod]
        public void AFrameWithNoTitleIsLeftAlone()
        {
            String deck =
                "\\begin{document}\n"
                + "\\begin{frame}\n"
                + "{\\Large A starter to warm up with}\n"
                + "\\end{frame}\n"
                + "\\title{Starter}\n"
                + "\\end{document}";

            String rewritten = RetrieveAndConnect.Rewrite(deck, "x.tex")!;

            StringAssert.Contains(rewritten, @"{\Large A starter to warm up with}");
            StringAssert.Contains(rewritten, @"\title{Retrieve and Connect}",
                "the deck was still worth retitling for its own title");
        }

        [TestMethod]
        public void ADeckWithNoStarterTitleAtAllIsHandedBack()
        {
            String deck = "\\begin{document}\n\\begin{frame}{Contents}\n\\end{frame}\n\\end{document}";

            Assert.IsNull(RetrieveAndConnect.Rewrite(deck, "x.tex"),
                "so the caller knows to ask somebody else");
        }

        [TestMethod]
        public void AVariantSaysWhichRulesMadeIt()
        {
            String rewritten = RetrieveAndConnect.Rewrite(ADeck, "x.tex")!;

            Assert.IsTrue(RetrieveAndConnect.MatchesCurrentRules(rewritten));
            Assert.IsFalse(RetrieveAndConnect.MatchesCurrentRules(ADeck),
                "a file that says nothing about how it was made cannot be shown to be current");

            StringAssert.Contains(rewritten, "x.tex", "and what it was made from");
        }

        private static String WithoutTheHeader(String rewritten)
        {
            const String rule = "% ================================================================";

            int second = rewritten.IndexOf(rule, rule.Length, StringComparison.Ordinal);

            return rewritten[(rewritten.IndexOf('\n', second) + 1)..];
        }

        #endregion

        #region A whole pass

        private FakeGitRepoManager _git = null!;
        private FakeLLMService _llm = null!;

        [TestInitialize]
        public void Setup()
        {
            _git = new FakeGitRepoManager();
            _llm = new FakeLLMService();
        }

        private Orchestrator Build()
        {
            var options = new L2Options { GenerateVocabularyKeys = false };

            options.Languages["pol"] = new LanguageOptions
                { Font = "Noto Serif", BabelName = "polish" };

            options.EagerLanguages.Add("pol");

            return new Orchestrator(
                NullLogger<Orchestrator>.Instance,
                _git,
                _llm,
                Options.Create(new GenerationOptions { MaxFilesPerRun = 30 }),
                Options.Create(options));
        }

        private static String NameOf(SyntheticPDFs.Models.TexSourceModel ts) => ts.FileNameFullPath;

        [TestMethod]
        public async Task ADeckAndItsWorkedSolutionsBothGetOne()
        {
            _git.AddFile(Deck, ageCommits: 2, contents: TexFixtures.SlideDeckDefiningAnswerMacros());
            _git.AddFile(DeckWorked, ageCommits: 1,
                contents: TexFixtures.VerifiedSlideWorkedSolutions());

            Orchestrator orchestrator = Build();

            Assert.AreEqual(
                Orchestrator.PassOutcome.Generated, await orchestrator.DoOnePassAsync());

            CollectionAssert.AreEquivalent(
                new[] { DeckVariant, WorkedVariant },
                _git.LastCommit.Select(NameOf).ToArray());

            Assert.AreEqual(0, _llm.CallCount,
                "retitling is string work, so a repository full of decks costs nothing");

            StringAssert.Contains(_git.Contents[DeckVariant], "{Retrieve and Connect}");

            Assert.AreEqual(
                Orchestrator.PassOutcome.NothingToDo, await orchestrator.DoOnePassAsync());
        }

        // A variant is made from one part of a sheet and from nothing else, so editing
        // that part is what invalidates it - and editing another part does not.
        [TestMethod]
        public async Task EditingOnePartOnlyRebuildsThatPartsVariant()
        {
            _git.AddFile(Deck, ageCommits: 2, contents: TexFixtures.SlideDeckDefiningAnswerMacros());
            _git.AddFile(DeckWorked, ageCommits: 1,
                contents: TexFixtures.VerifiedSlideWorkedSolutions());

            Orchestrator orchestrator = Build();

            await orchestrator.DoOnePassAsync();

            // somebody corrects a working, which ages everything else by a commit
            _git.Files[DeckVariant] = 1;
            _git.Files[WorkedVariant] = 1;

            _git.AddFile(DeckWorked, ageCommits: 0,
                contents: TexFixtures.VerifiedSlideWorkedSolutions("a corrected working"));

            Assert.AreEqual(
                Orchestrator.PassOutcome.RemovedStaleFiles, await orchestrator.DoOnePassAsync());

            CollectionAssert.AreEqual(
                new[] { WorkedVariant },
                _git.RemoveFilesCalls.Single().ToArray(),
                "the deck was not touched, so the version of it made for the school stands");
        }

        [TestMethod]
        public async Task OnlyADeckOfStartersGetsOne()
        {
            _git.AddFile("latex/worksheets/quadratics.tex", ageCommits: 1);
            _git.AddFile("latex/cheatSheets/trigIdentities.tex", ageCommits: 1);

            Orchestrator orchestrator = Build();

            for (int pass = 0; pass < 4; pass++) { await orchestrator.DoOnePassAsync(); }

            Assert.IsFalse(
                _git.Files.Keys.Any(f => f.Contains("retrieveAndConnect", StringComparison.Ordinal)),
                "a worksheet is not a starter and a poster is not either");
        }

        [TestMethod]
        public async Task APurgeOfTheTranslationsLeavesItAlone()
        {
            _git.AddFile(Deck, ageCommits: 2, contents: TexFixtures.SlideDeckDefiningAnswerMacros());
            _git.AddFile(DeckWorked, ageCommits: 1,
                contents: TexFixtures.VerifiedSlideWorkedSolutions());

            Orchestrator orchestrator = Build();

            await orchestrator.DoOnePassAsync();

            await orchestrator.PurgeAsync(Shared.PurgeScope.TranslationsAndVocabulary);

            CollectionAssert.DoesNotContain(
                _git.RemoveFilesCalls.SelectMany(c => c).ToArray(), DeckVariant,
                "it is English, and clearing it would only mean writing it again");
        }

        [TestMethod]
        public async Task NoTranslationOfAVariantIsEverAskedFor()
        {
            // a translated deck is printed for a handful of pupils, where what the school
            // calls it on the board is not what is being read
            _git.AddFile(Deck, ageCommits: 2, contents: TexFixtures.SlideDeckDefiningAnswerMacros());
            _git.AddFile(DeckWorked, ageCommits: 1,
                contents: TexFixtures.VerifiedSlideWorkedSolutions());

            Orchestrator orchestrator = Build();

            for (int pass = 0; pass < 6; pass++) { await orchestrator.DoOnePassAsync(); }

            Assert.IsFalse(
                _git.Files.Keys.Any(f =>
                    f.Contains("/L2/", StringComparison.Ordinal)
                    && f.Contains("retrieveAndConnect", StringComparison.Ordinal)));
        }

        #endregion

        #region What it is called

        [TestMethod]
        [DataRow("Root", "latex/starters/circlesArea_retrieveAndConnect")]
        [DataRow("WorkedSolutions",
            "latex/starters/circlesArea_workedSolutions_retrieveAndConnect")]
        public void AVariantIsNamedAfterThePartItIsAVariantOf(String partName, String expected)
        {
            SheetPart part = Enum.Parse<SheetPart>(partName);

            SourceMetadata metadata = new()
            {
                RootName  = "latex/starters/circlesArea",
                Language  = ISO639_3Code.eng,
                Part      = part,
                Form      = SheetForm.RetrieveAndConnect,
                Archetype = SheetArchetypes.QuestionSlides,
            };

            Assert.AreEqual(expected, metadata.PathNoExtension);

            // and it reads back as the same thing, which is what stops it being made
            // again on every pass
            SourceMetadata parsed = SheetArchetypes.Parse(expected);

            Assert.AreEqual(metadata.RootName, parsed.RootName);
            Assert.AreEqual(part, parsed.Part);
            Assert.AreEqual(SheetForm.RetrieveAndConnect, parsed.Form);
        }

        #endregion
    }
}
