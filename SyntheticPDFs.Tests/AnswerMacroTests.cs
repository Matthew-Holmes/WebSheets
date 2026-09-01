using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Rendering;
using SyntheticPDFs.Services;
using SyntheticPDFs.Tests.Fakes;

namespace SyntheticPDFs.Tests
{
    // the helpers themselves, the markers that record what has been done to a deck, and what
    // each prompt is told. the bounded review loop that uses all this lives in
    // AnswerMacroReviewTests
    [TestClass]
    public class AnswerMacroTests
    {
        private const String Deck = "latex/starters/KS3/circlesArea.tex";
        private const String DeckWorked = "latex/starters/KS3/circlesArea_workedSolutions.tex";

        private const String RewriteAsk = "Rewrite it so that";

        private FakeGitRepoManager _git = null!;
        private FakeLLMService _llm = null!;
        private Orchestrator _orchestrator = null!;

        [TestInitialize]
        public void Setup()
        {
            _git = new FakeGitRepoManager();
            _llm = new FakeLLMService();
            _orchestrator = new Orchestrator(
                NullLogger<Orchestrator>.Instance,
                _git,
                _llm,
                Options.Create(new GenerationOptions { MaxFilesPerRun = 30 }));

            _llm.ScriptedResponses.Add((RewriteAsk, TexFixtures.RewrittenSlideDeck()));
        }

        private static String NameOf(SyntheticPDFs.Models.TexSourceModel ts) => ts.FileNameFullPath;

        // ---- the definitions themselves ----

        [TestMethod]
        public void DefinitionsAreTheAgreedText()
        {
            // spelled out here rather than reused from the constant, so that changing the
            // constant has to be a deliberate act with this test updated alongside it
            String expected = String.Join('\n',
                "% ================================================================",
                "% Answer-overlay helpers",
                "% ================================================================",
                @"\newcommand{\ablank}[1]{%",
                @"  \alt<2>{\textcolor{red}{#1}}{\underline{\phantom{#1}}}%",
                "}",
                @"\newcommand{\ashow}[1]{\uncover<2->{\textcolor{red}{\small #1}}}",
                @"\newcommand{\ashowq}[1]{\alt<2->{\textcolor{red}{\small #1}}{?}}",
                "",
                @"% Answers drawn straight on to a TikZ diagram, revealed on overlay 2 like \ashow.",
                "% Space is reserved on overlay 1, so the diagram does not move between slides.",
                @"\usetikzlibrary{overlay-beamer-styles}",
                @"\tikzset{",
                @"  ans/.style     = {red, thick, visible on=<2->},",
                @"  ansfill/.style = {red!15, visible on=<2->},",
                @"  anslab/.style  = {red, font=\tiny, visible on=<2->},",
                "}",
                "",
                "% Used by the worked solutions rather than by this deck, so that each worked",
                "% solution is first a question with room to write in and then the answer.",
                @"\newlength{\aworkpad}",
                @"\setlength{\aworkpad}{8mm}",
                @"\newcommand{\awork}[1]{%",
                @"  \par\vspace{\aworkpad}%",
                @"  \uncover<2->{#1}%",
                @"  \par\vspace{\aworkpad}%",
                "}");

            Assert.AreEqual(expected, AnswerMacros.Definitions);
        }

        [TestMethod]
        public void ADeckDefiningTheHelpersPassesTheCheapCheck()
        {
            Assert.IsTrue(AnswerMacros.AreDefined(TexFixtures.SlideDeckDefiningAnswerMacros()));
        }

        [TestMethod]
        public void WindowsLineEndingsAreForgiven()
        {
            String crlf = TexFixtures.SlideDeckDefiningAnswerMacros().Replace("\n", "\r\n");

            Assert.IsTrue(AnswerMacros.AreDefined(crlf), "line endings are not a real difference");
        }

        [TestMethod]
        [DataRow(@"\newcommand{\ablank}[1]{%")]
        [DataRow(@"\newcommand{\ashow}[1]{\uncover<2->{\textcolor{red}{\small #1}}}")]
        [DataRow(@"\newcommand{\ashowq}[1]{\alt<2->{\textcolor{red}{\small #1}}{?}}")]
        public void ADeckMissingAnyOneHelperFailsTheCheapCheck(String helperStart)
        {
            String deck = TexFixtures.SlideDeckDefiningAnswerMacros();

            String withoutIt = String.Join('\n',
                deck.Split('\n').Where(l => !l.StartsWith(helperStart, StringComparison.Ordinal)));

            Assert.IsFalse(AnswerMacros.AreDefined(withoutIt));
        }

        [TestMethod]
        public void ARetypedHelperFailsTheCheapCheck()
        {
            // being wrong this way round only costs a rewrite, the other way ships a deck
            // whose overlays don't behave as the rest of the pipeline assumes
            String altered = TexFixtures.SlideDeckDefiningAnswerMacros()
                .Replace(@"\textcolor{red}{\small #1}", @"\textcolor{blue}{\small #1}");

            Assert.IsFalse(AnswerMacros.AreDefined(altered));
        }

        [TestMethod]
        public void ADeckWithNoHelpersAtAllFailsTheCheapCheck()
        {
            Assert.IsFalse(AnswerMacros.AreDefined(TexFixtures.SlideDeckWithoutAnswerMacros()));
        }

        [TestMethod]
        public async Task TheCheapCheckCostsNothingBeforeTheFirstFix()
        {
            // a deck with no definitions is settled without asking anyone, so the only review
            // is the one that looks at what the fixer produced
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckWithoutAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            Assert.AreEqual(1, _llm.ReviewCallCount, "round 0 was settled for free");
            StringAssert.Contains(_llm.QuestionsSeen.Single(), TexFixtures.DefaultBody,
                "the one review that ran looked at the rewritten deck");
        }

        // ---- the markers ----

        [TestMethod]
        public void MarkingIsIdempotentAndLeavesValidTex()
        {
            String once = AnswerMacros.AddVerifiedMarker(TexFixtures.WorkedSolutions());
            String twice = AnswerMacros.AddVerifiedMarker(once);

            Assert.IsTrue(AnswerMacros.IsMarkedVerified(once));
            Assert.AreEqual(once, twice, "marking an already marked file must change nothing");
            Assert.IsTrue(SourceGenerator.IsValidTex(once), $"marker broke the tex:\n{once}");
            Assert.IsTrue(once.StartsWith("%"), "the marker goes first so an append can't lose it");
        }

        [TestMethod]
        public void UnmarkedWorkedSolutionsAreRecognisedAsSuch()
        {
            Assert.IsFalse(AnswerMacros.IsMarkedVerified(TexFixtures.WorkedSolutions()));
        }

        [TestMethod]
        public void TheMarkerIsNotMatchedInsideASentence()
        {
            // it is a whole line or it is nothing
            String prose = TexFixtures.WorkedSolutions(
                "We note that the Root file used ashow macros here and there.");

            Assert.IsFalse(AnswerMacros.IsMarkedVerified(prose));
        }

        [TestMethod]
        public void AReviewNoteIsRecognisedByItsMarkerComment()
        {
            Assert.IsTrue(AnswerMacros.HasReviewNote(TexFixtures.SlideDeckWithReviewNote()));
            Assert.IsFalse(AnswerMacros.HasReviewNote(TexFixtures.SlideDeckDefiningAnswerMacros()));
        }

        // ---- worked solutions that carry no record of a review ----

        [TestMethod]
        public async Task UnmarkedWorkedSolutionsAreStampedRatherThanRegenerated()
        {
            String handWritten = TexFixtures.SlideWorkedSolutions("a person wrote these");

            _git.AddFile(Deck, ageCommits: 2, contents: TexFixtures.SlideDeckDefiningAnswerMacros());
            _git.AddFile(DeckWorked, ageCommits: 1, contents: handWritten);

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());

            CollectionAssert.AreEqual(new[] { DeckWorked }, _git.LastCommit.Select(NameOf).ToArray());

            Assert.AreEqual(0, _llm.CallCount, "regenerating them would be paying twice for the same content");
            Assert.AreEqual(1, _llm.ReviewCallCount);

            Assert.IsTrue(AnswerMacros.IsMarkedVerified(_git.Contents[DeckWorked]));
            StringAssert.Contains(_git.Contents[DeckWorked], "a person wrote these", "the content must survive");

            // the retitled versions follow, once the files they are made from are settled
            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());
            Assert.AreEqual(Orchestrator.PassOutcome.NothingToDo, await _orchestrator.DoOnePassAsync());
        }

        [TestMethod]
        public async Task MarkedWorkedSolutionsAreNotCheckedAgain()
        {
            _git.AddFile(Deck, ageCommits: 2, contents: TexFixtures.SlideDeckDefiningAnswerMacros());
            _git.AddFile(DeckWorked, ageCommits: 1, contents: TexFixtures.VerifiedSlideWorkedSolutions());

            // the retitled versions are still owed, and cost nothing to make
            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());
            Assert.AreEqual(Orchestrator.PassOutcome.NothingToDo, await _orchestrator.DoOnePassAsync());

            Assert.AreEqual(0, _llm.CallCount);
            Assert.AreEqual(0, _llm.ReviewCallCount, "the marker is there so we must not pay to ask again");
        }

        [TestMethod]
        public async Task StaleWorkedSolutionsBringTheReviewBack()
        {
            // this is the case where a person edits the deck and takes the helpers back out:
            // the worked solutions go stale, are removed, and the review comes back with them
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckDefiningAnswerMacros());
            _git.AddFile(DeckWorked, ageCommits: 5, contents: TexFixtures.VerifiedSlideWorkedSolutions());

            Assert.AreEqual(Orchestrator.PassOutcome.RemovedStaleFiles, await _orchestrator.DoOnePassAsync());
            Assert.AreEqual(0, _llm.ReviewCallCount, "removal happens before anything is asked");

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());
            Assert.AreEqual(1, _llm.ReviewCallCount, "a fresh set of worked solutions means a fresh review");
        }

        [TestMethod]
        public async Task AnIndeterminateVerdictChangesNothing()
        {
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckDefiningAnswerMacros());
            _llm.DefaultVerdict = null;

            await _orchestrator.DoOnePassAsync();

            // the retitled deck needs no verdict and is written, but nothing that does
            // was: no worked solutions, and no change to the deck itself
            CollectionAssert.DoesNotContain(
                _git.CommitCalls.SelectMany(c => c).Select(NameOf).ToArray(), DeckWorked,
                "a shrug must not be read as either verdict");

            Assert.AreEqual(3, _llm.ReviewCallCount, "the model gets a few goes before we give up");
        }

        // ---- other archetypes are untouched by any of this ----

        [TestMethod]
        public async Task WorksheetsAreNeverReviewedForAnswerMacros()
        {
            _git.AddFile("latex/worksheets/quadratics.tex", ageCommits: 1);

            await _orchestrator.DoOnePassAsync();

            Assert.AreEqual(0, _llm.ReviewCallCount);
            Assert.IsFalse(
                AnswerMacros.IsMarkedVerified(_git.Contents["latex/worksheets/quadratics_workedSolutions.tex"]),
                "the marker is a statement about a slide deck, not a worksheet");
        }

        // ---- what each prompt is told ----

        private const String MathModeRule = @"\ablank{$3x^2$}";

        [TestMethod]
        public async Task TheRewriteIsToldToPutMathematicalAnswersInMathMode()
        {
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckWithoutAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            StringAssert.Contains(_llm.PromptsSeen.First(p => p.Contains(RewriteAsk)), MathModeRule);
        }

        // the worked solutions used to be handed the overlay rules wholesale, and the model
        // applied them to the workings too - a second overlay on every solution with nothing
        // new on it, and invented macros to go with it
        [TestMethod]
        [DataRow("A worked solution frame is not a question slide", "the workings are the answer")]
        [DataRow(@"It uses \awork and nothing else", "and it has one way of revealing them")]
        [DataRow(@"Do not use \ablank, \ashow or \ashowq on a worked solution frame", "and none of the deck's helpers")]
        [DataRow("ans, ansfill or anslab TikZ", "nor the diagram styles")]
        [DataRow(@"\pause, \onslide, \alt", "nor any other overlay command")]
        [DataRow("exactly two slides in the compiled pdf", "the blank one to write on, then the working")]
        [DataRow(@"there is no \blank, no \answer and no \soln", "and nothing may be invented")]
        [DataRow("Copy each of them exactly as it is, keeping the helpers it already uses", "only the copied starters keep theirs")]
        public async Task TheWorkedSolutionsPromptKeepsTheHelpersOffTheWorkings(String required, String why)
        {
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckDefiningAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            StringAssert.Contains(_llm.PromptsSeen.Single(), required, why);
        }

        [TestMethod]
        public async Task TheWorkedSolutionsPromptIsNotToldHowToUseTheHelpers()
        {
            // being told how to use them is what made it want to use them everywhere
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckDefiningAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            String prompt = _llm.PromptsSeen.Single();

            Assert.IsFalse(prompt.Contains(MathModeRule, StringComparison.Ordinal));
            Assert.IsFalse(
                prompt.Contains("makes the answer clearest", StringComparison.Ordinal),
                "choosing between helpers is a question for the deck, not for the workings");
        }

        [TestMethod]
        public async Task AWorksheetPromptIsNotBurdenedWithSlideRules()
        {
            _git.AddFile("latex/worksheets/quadratics.tex", ageCommits: 1);

            await _orchestrator.DoOnePassAsync();

            Assert.IsFalse(_llm.PromptsSeen.Single().Contains(MathModeRule, StringComparison.Ordinal));
        }

        [TestMethod]
        public async Task TheReviewOnlyFailsAnAnswerThatWouldNotCompile()
        {
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckDefiningAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            StringAssert.Contains(_llm.QuestionsSeen.Single(), "would not compile");
        }

        // every one of these was a way the review could reject a deck that was fine, and each
        // false rejection used to cost a full rewrite
        [TestMethod]
        [DataRow("commented out")]
        [DataRow("Only questions count")]
        [DataRow("drawn on to a diagram")]
        [DataRow("whose whole job is to list the answers")]
        [DataRow("Never fail a deck because such a slide reveals")]
        [DataRow("again in full on a later answers slide")]
        [DataRow("where you would have chosen another")]
        [DataRow("ans, ansfill or anslab")]
        [DataRow("several parts at once")]
        [DataRow("inside existing math mode")]
        [DataRow("Numbering questions by hand")]
        [DataRow("style, wording, spacing, layout")]
        public async Task TheReviewIsToldNotToRejectForThis(String exclusion)
        {
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckDefiningAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            StringAssert.Contains(_llm.QuestionsSeen.Single(), exclusion);
        }

        [TestMethod]
        public void ADeckWithTheCommandsButNoDiagramStylesFailsTheCheapCheck()
        {
            // the diagram styles are part of the agreed set, so a deck that predates them has
            // to be brought up to date like any other
            String deck = TexFixtures.SlideDeckDefiningAnswerMacros()
                .Replace(AnswerMacros.DiagramStyles, "");

            Assert.IsFalse(AnswerMacros.AreDefined(deck));
        }

        [TestMethod]
        public void TheEarlierAblankNoLongerCounts()
        {
            // it used to underline the revealed answer as well as the gap - a deck still on
            // that version is out of date and gets rewritten
            String deck = TexFixtures.SlideDeckDefiningAnswerMacros()
                .Replace(@"\alt<2>{\textcolor{red}{#1}}", @"\alt<2>{\textcolor{red}{\underline{#1}}}");

            Assert.IsFalse(AnswerMacros.AreDefined(deck));
        }

        [TestMethod]
        public async Task ThePromptsExplainWhenToDrawAnAnswerOnADiagram()
        {
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckWithoutAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            String rewrite = _llm.PromptsSeen.First(p => p.Contains(RewriteAsk));

            StringAssert.Contains(rewrite, "ansfill for a shaded region");
            StringAssert.Contains(rewrite, @"\usetikzlibrary{overlay-beamer-styles}", "the styles are handed over too");
        }

        [TestMethod]
        public async Task TheReviewAsksOnlyThatAnswersRevealOnOverlayTwo()
        {
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckDefiningAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            StringAssert.Contains(_llm.QuestionsSeen.Single(), "reveal it on overlay 2");
        }

        [TestMethod]
        public async Task TheRewriteSaysToPickWhicheverHelperReadsBest()
        {
            // no helper is the right one - whichever reads best on the slide wins
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckWithoutAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            String rewrite = _llm.PromptsSeen.First(p => p.Contains(RewriteAsk));

            StringAssert.Contains(rewrite, "whichever of these makes the answer clearest");
        }

        [TestMethod]
        public async Task TheRewriteScopesTheOverlayRuleToQuestionSlides()
        {
            // it used to say every answer, full stop, which contradicted the instruction two
            // sentences later to leave a dedicated answers slide alone
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckWithoutAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            String rewrite = _llm.PromptsSeen.First(p => p.Contains(RewriteAsk));

            StringAssert.Contains(rewrite, "On a slide that asks questions, every answer must appear on overlay 2");
            StringAssert.Contains(rewrite, "is not a question slide. Leave it showing");
            StringAssert.Contains(rewrite, @"there is no \blank", "and it may not invent helpers either");
        }

        [TestMethod]
        public async Task OnlyAQuestionSlideGivingItselfAwayCountsAsALeak()
        {
            // a deck may end with a slide that lists every answer at once - the fixer is told
            // to keep those, so the review must not fail the deck for them
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckDefiningAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            String question = _llm.QuestionsSeen.Single();

            StringAssert.Contains(question, "a question slide gives its own answer away on overlay 1");
            Assert.IsFalse(
                question.Contains("already visible on overlay 1 in the part of the file", StringComparison.Ordinal),
                "the old wording condemned a deliberate answers slide");
        }

        // a helper sitting inside \( ... \) whose argument opens math mode again closes the
        // maths early and takes the build down with Missing $ inserted. the rule for this
        // used to trail an unconditional "answers go in math mode", and lost to it
        [TestMethod]
        [DataRow("decide whether the macro itself is already inside math", "the decision comes first")]
        [DataRow(@"\( ... \), \[ ... \], or an equation, align", "and says what counts as already in maths")]
        [DataRow("put no math delimiters in the braces at all", "then one branch")]
        [DataRow(@"never \(\frac{3}{8} = \ablank{$37.5\%$}\)", "with the failing case spelled out")]
        [DataRow("Missing $", "and the error it causes")]
        [DataRow("If instead the macro is in ordinary text", "then the other branch")]
        [DataRow(@"write \ablank{$3x^2$}, never \ablank{3x^2}", "which still needs its delimiters")]
        public async Task TheRewriteIsToldNotToNestMathMode(String required, String why)
        {
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckWithoutAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            StringAssert.Contains(_llm.PromptsSeen.First(p => p.Contains(RewriteAsk)), required, why);
        }

        [TestMethod]
        public async Task TheReviewFailsBothWaysOfGettingMathModeWrong()
        {
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckDefiningAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            String question = _llm.QuestionsSeen.Single();

            StringAssert.Contains(question, @"\ablank{3x^2} in ordinary text", "needs maths and has none");
            StringAssert.Contains(question, @"\(x = \ablank{$5$}\)", "or opens maths inside maths");
        }

        // ---- the compiler magic comment ----

        // the hosting repo routes each file to pdflatex, xelatex or lualatex, and a
        // "% !TeX program" comment in the first few lines overrides that. every file we
        // derive from another has to carry it across or it gets built by a different engine
        private const String DirectiveRule = @"% !TeX program = xelatex, copy that line across verbatim";

        [TestMethod]
        public async Task TheSlidesWorkedSolutionsPromptCarriesTheCompilerDirectiveAcross()
        {
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckDefiningAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            StringAssert.Contains(_llm.PromptsSeen.Single(), DirectiveRule);
        }

        [TestMethod]
        public async Task TheWorksheetWorkedSolutionsPromptCarriesItToo()
        {
            _git.AddFile("latex/worksheets/quadratics.tex", ageCommits: 1);

            await _orchestrator.DoOnePassAsync();

            StringAssert.Contains(_llm.PromptsSeen.Single(), DirectiveRule);
        }

        [TestMethod]
        public async Task TheAnswerKeyPromptCarriesItToo()
        {
            _git.AddFile("latex/worksheets/quadratics.tex", ageCommits: 2);
            _git.AddFile("latex/worksheets/quadratics_workedSolutions.tex", ageCommits: 1);

            await _orchestrator.DoOnePassAsync();

            StringAssert.Contains(_llm.PromptsSeen.Single(), DirectiveRule);
        }

        [TestMethod]
        public async Task TheRewriteCarriesItToo()
        {
            // this one overwrites the deck itself, so losing the directive would change how
            // the teacher's own file builds
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckWithoutAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            StringAssert.Contains(_llm.PromptsSeen.First(p => p.Contains(RewriteAsk)), DirectiveRule);
        }

        [TestMethod]
        public void MarkingWorkedSolutionsLeavesAMagicCommentWhereItCanStillBeFound()
        {
            // the marker goes in above it, which is fine: the hosting repo scans the first
            // 4096 bytes for the comment at any line start, not just line one
            String directive = "% !TeX program = xelatex";

            String marked = AnswerMacros.AddVerifiedMarker(directive + "\n" + TexFixtures.WorkedSolutions());

            String[] lines = marked.Split('\n');

            Assert.AreEqual(AnswerMacros.VerifiedMarker, lines[0]);
            Assert.AreEqual(directive, lines[1], "the directive survives, one line further down");
            Assert.IsTrue(marked.IndexOf(directive, StringComparison.Ordinal) < 4096);
        }

        // ---- the house style for a set of worked solutions ----

        // modelled on introductoryFractionsStarters_workedSolutions.tex. these are what stops
        // the layout drifting between decks, since nothing downstream checks the shape of
        // what comes back
        [TestMethod]
        [DataRow(@"\frame{\titlepage}", "opens with a title page")]
        [DataRow("contents frame titled Contents", "then one contents frame")]
        [DataRow(@"\begin{columns}[T]", "laid out in two columns")]
        [DataRow(@"\textbf{Questions and short answers}", "headed on the left")]
        [DataRow(@"\textbf{Worked solutions}", "and on the right")]
        [DataRow(@"\hyperlink{q-st1}{\beamergotobutton{Starter 1}}", "linking the questions")]
        [DataRow(@"\hyperlink{work-st1}{\beamergotobutton{Starter 1}}", "and the workings")]
        [DataRow(@"\hypertarget{q-stN}{}", "with matching targets on the question slides")]
        [DataRow(@"\hypertarget{work-stN}{}", "and on the first worked solution of each starter")]
        [DataRow("Worked Solution: Starter N, Question M", "and a title format for the workings")]
        [DataRow(@"\textbf{Question:}", "restating the question")]
        [DataRow(@"\awork{Solution: ...}", "with the working labelled inside the reveal")]
        public async Task TheWorkedSolutionsPromptSpecifiesTheHouseStyle(String required, String why)
        {
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckDefiningAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            StringAssert.Contains(_llm.PromptsSeen.Single(), required, why);
        }

        [TestMethod]
        public async Task TheWorkedSolutionsPromptAsksForStartersAndWorkingsInterleaved()
        {
            // a teacher works down the deck in the order they teach it, so each starter is
            // finished before the next one begins
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckDefiningAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            String prompt = _llm.PromptsSeen.Single();

            StringAssert.Contains(prompt, "finish each one completely before starting the next");
            StringAssert.Contains(
                prompt,
                "starter 1, starter 1 worked solutions, starter 2, starter 2 worked solutions",
                "the wanted reading order is spelled out");
            StringAssert.Contains(
                prompt,
                "Do not group all the question slides together and all the worked solutions together",
                "and the old layout is ruled out explicitly");
        }

        [TestMethod]
        public async Task AWorksheetIsNotGivenTheSlideHouseStyle()
        {
            _git.AddFile("latex/worksheets/quadratics.tex", ageCommits: 1);

            await _orchestrator.DoOnePassAsync();

            Assert.IsFalse(
                _llm.PromptsSeen.Single().Contains(@"\beamergotobutton", StringComparison.Ordinal));
        }

        // ---- splicing the note in ----

        private const String Body = "What is $2 + 2$? \ashow{$4$}";

        private static String DeckWithNote(String summary = "the review was not happy", bool atStart = false) =>
            AnswerMacros.AddReviewNote(TexFixtures.SlideDeckDefiningAnswerMacros(Body), summary, atStart)!;

        [TestMethod]
        public void ANoteAtTheEndSitsAfterTheQuestionsAndInsideTheDocument()
        {
            String deck = DeckWithNote();

            int note = deck.IndexOf(AnswerMacros.ReviewNoteMarker, StringComparison.Ordinal);

            Assert.IsTrue(note > deck.IndexOf(Body, StringComparison.Ordinal), "after the questions");
            Assert.IsTrue(note < deck.IndexOf(@"\end{document}", StringComparison.Ordinal), "but still inside the body");
        }

        [TestMethod]
        public void ANoteAtTheStartSitsBeforeTheQuestionsAndInsideTheDocument()
        {
            String deck = DeckWithNote(atStart: true);

            int note = deck.IndexOf(AnswerMacros.ReviewNoteMarker, StringComparison.Ordinal);

            Assert.IsTrue(note > deck.IndexOf(@"\begin{document}", StringComparison.Ordinal), "inside the body");
            Assert.IsTrue(note < deck.IndexOf(Body, StringComparison.Ordinal), "before the questions");
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void SplicingANoteLosesNothing(bool atStart)
        {
            String original = TexFixtures.SlideDeckDefiningAnswerMacros(Body);
            String deck = AnswerMacros.AddReviewNote(original, "a summary", atStart)!;

            StringAssert.Contains(deck, Body, "the questions are untouched");
            Assert.IsTrue(AnswerMacros.AreDefined(deck), "so is the preamble");
            Assert.IsTrue(SourceGenerator.IsValidTex(deck), $"the deck must still be valid:\n{deck}");
            Assert.IsTrue(deck.Length > original.Length);
        }

        [TestMethod]
        public void TheSummaryReachesTheSlide()
        {
            StringAssert.Contains(DeckWithNote("SOMETHING THE TEACHER SHOULD READ"), "SOMETHING THE TEACHER SHOULD READ");
        }

        [TestMethod]
        public void LatexSpecialsInTheSummaryAreNeutralised()
        {
            // a stray % would comment out the rest of the line, and an & look like a cell
            String deck = DeckWithNote(@"100% of & _ #$ ^~ {x} \y");

            Assert.IsTrue(SourceGenerator.IsValidTex(deck), $"specials broke the deck:\n{deck}");
            StringAssert.Contains(deck, @"100\% of \& \_ \#\$");
            StringAssert.Contains(deck, @"\textbackslash{}y");
            StringAssert.Contains(deck, @"\{x\}");
        }

        [TestMethod]
        public void SplicingANoteTwiceChangesNothingTheSecondTime()
        {
            String once = DeckWithNote();
            String twice = AnswerMacros.AddReviewNote(once, "a different summary", atStart: false)!;

            Assert.AreEqual(once, twice, "a deck that already carries a note must be left alone");
        }

        [TestMethod]
        public void ADeckWithNoDocumentBodyCannotBeNoted()
        {
            Assert.IsNull(AnswerMacros.AddReviewNote(@"\documentclass{beamer}", "a summary", atStart: false));
            Assert.IsNull(AnswerMacros.AddReviewNote(@"\documentclass{beamer}", "a summary", atStart: true));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void TheDecksOwnLineEndingsAreKept(bool atStart)
        {
            String crlf = TexFixtures.SlideDeckDefiningAnswerMacros(Body).Replace("\n", "\r\n");

            String deck = AnswerMacros.AddReviewNote(crlf, "a summary\nover two lines", atStart)!;

            for (int i = 0; i < deck.Length; i++)
            {
                if (deck[i] == '\n')
                {
                    Assert.IsTrue(i > 0 && deck[i - 1] == '\r', $"a bare newline crept in at {i}");
                }
            }
        }

        // ---- reading a verdict out of a model that likes to talk ----

        [TestMethod]
        [DataRow("PASS", true)]
        [DataRow("pass", true)]
        [DataRow("Pass.", true)]
        [DataRow("**PASS**", true)]
        [DataRow("  \nYES\n", true)]
        [DataRow("FAIL", false)]
        [DataRow("fail.", false)]
        [DataRow("- NO", false)]
        public void AVerdictIsReadOutOfTheFirstWord(String response, bool expected)
        {
            Assert.AreEqual(expected, LLMService.ParseVerdict(response));
        }

        [TestMethod]
        [DataRow("maybe")]
        [DataRow("I think so")]
        [DataRow("")]
        [DataRow("1234")]
        public void AnythingElseIsNotAVerdict(String response)
        {
            Assert.IsNull(LLMService.ParseVerdict(response), "a non answer must not be read as a failure");
        }

        [TestMethod]
        public void TheReasonsAreEverythingAfterTheVerdict()
        {
            ReviewVerdict? verdict = LLMService.ParseReview("FAIL\nquestion 2 has no answer\nquestion 5 either");

            Assert.IsNotNull(verdict);
            Assert.IsFalse(verdict!.Passed);
            StringAssert.Contains(verdict.Reasons, "question 2 has no answer");
            StringAssert.Contains(verdict.Reasons, "question 5 either");
        }

        [TestMethod]
        public void APassCarriesNoReasons()
        {
            ReviewVerdict? verdict = LLMService.ParseReview("PASS\nlooks good to me");

            Assert.IsNotNull(verdict);
            Assert.IsTrue(verdict!.Passed);
            Assert.AreEqual(String.Empty, verdict.Reasons);
        }

        [TestMethod]
        public void ABareFailStillCountsAsAVerdict()
        {
            ReviewVerdict? verdict = LLMService.ParseReview("FAIL");

            Assert.IsNotNull(verdict);
            Assert.IsFalse(verdict!.Passed);
            Assert.AreNotEqual(String.Empty, verdict.Reasons, "the log needs something to show");
        }
    }
}
