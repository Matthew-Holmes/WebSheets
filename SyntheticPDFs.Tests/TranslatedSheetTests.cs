using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Rendering;
using SyntheticPDFs.Tests.Fakes;

namespace SyntheticPDFs.Tests
{
    // the two translated forms of a sheet. the model writes only the body; everything
    // that has to be exactly right to compile is wrapped around it here
    [TestClass]
    public class TranslatedSheetTests
    {
        private const String Root    = "latex/worksheets/algebra/quadratics";
        private const String RootEx  = Root + ".tex";
        private const String Worked  = Root + "_workedSolutions.tex";
        private const String Answers = Root + "_solutions.tex";
        private const String Vocab   = Root + "_vocab.tex";
        private const String Dir     = Root + "/L2/pol/";
        private const String Key     = Dir + "quadratics_polishKey.tex";
        private const String Parallel = Dir + "quadratics_polishParallelText.tex";
        private const String Tier3    = Dir + "quadratics_polishTier3Only.tex";

        private FakeGitRepoManager _git = null!;
        private FakeLLMService _llm = null!;
        private Orchestrator _orchestrator = null!;

        // a body of the shape the prompt asks for: no preamble, no definitions, uses the
        // helpers it is given
        private const String GoodBody =
            "\\begin{document}\n"
            + "\\ealpara{Zapisz ulamek}{Write the \\ealkey{fraction}}\n"
            + "\\end{document}";

        [TestInitialize]
        public void Setup()
        {
            _git = new FakeGitRepoManager();
            _llm = new FakeLLMService { DefaultResponse = GoodBody };

            var options = new L2Options { GenerateVocabularyKeys = true };

            options.Languages["pol"] = new LanguageOptions
                { Font = "Noto Serif", BabelName = "polish" };
            options.EagerLanguages.Add("pol");

            _orchestrator = new Orchestrator(
                NullLogger<Orchestrator>.Instance,
                _git,
                _llm,
                Options.Create(new GenerationOptions { MaxFilesPerRun = 30 }),
                Options.Create(options));
        }

        // a repository already holding a complete English sheet, its vocabulary key and
        // the Polish key derived from it
        private void GiveTheRepoEverythingUpToTheKey(String rootContents = "\\documentclass{beamer}\n\\usepackage{tikz}")
        {
            var terms = new[]
            {
                new VocabTerm
                {
                    English = "fraction", Definition = "part of a whole",
                    Translation = "ulamek", TranslatedDefinition = "czesc calosci",
                },
            };

            _git.AddFile(RootEx, ageCommits: 5, contents: rootContents);
            _git.AddFile(Worked, ageCommits: 4);
            _git.AddFile(Answers, ageCommits: 3);
            _git.AddFile(Vocab, ageCommits: 2, contents: TexFixtures.VocabularyKey(Root, terms));
            _git.AddFile(Key, ageCommits: 1,
                contents: TexFixtures.VocabularyKey(Root, terms, TexFixtures.Polish));
        }

        private static String NameOf(SyntheticPDFs.Models.TexSourceModel ts) => ts.FileNameFullPath;

        // ---- what a pass produces ----

        [TestMethod]
        public async Task BothTranslatedFormsOfTheSheetAreMadeTogether()
        {
            // neither depends on the other, so they are free to run in one pass
            GiveTheRepoEverythingUpToTheKey();

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());

            CollectionAssert.AreEquivalent(
                new[] { Parallel, Tier3 },
                _git.LastCommit.Select(NameOf).ToArray());
        }

        [TestMethod]
        public async Task OnlyTheSheetIsTranslatedUnasked()
        {
            // the worked solutions and answers have translated forms in the plan, but
            // making them for every language unasked would cost far more than it is worth
            GiveTheRepoEverythingUpToTheKey();

            await _orchestrator.DoOnePassAsync();
            await _orchestrator.DoOnePassAsync();

            Assert.IsFalse(
                _git.Files.Keys.Any(f => f.Contains("workedSolutions_polish", StringComparison.Ordinal)),
                "the worked solutions are translated on request only");
        }

        // ---- what the generator guarantees, rather than the model ----

        [TestMethod]
        public async Task TheGeneratedFileCarriesThePreambleWhateverTheModelWrote()
        {
            GiveTheRepoEverythingUpToTheKey();

            await _orchestrator.DoOnePassAsync();

            String tex = _git.LastCommit.First(t => NameOf(t) == Parallel).TexSource;

            StringAssert.Contains(tex, L2Macros.CompilerDirective,
                "it must be pinned to lualatex, never to whatever the English sheet used");
            StringAssert.Contains(tex, @"\babelprovide[import]{polish}");
            StringAssert.Contains(tex, @"\babelfont[polish]{sf}",
                "both families, since a deck sets sans and a sheet sets roman");
            Assert.IsTrue(L2Macros.AreDefined(tex), "the helpers must be defined");
            Assert.IsTrue(L2Macros.HasCompilerDirective(tex));
        }

        [TestMethod]
        public async Task TheOriginalsClassAndPackagesAreKept()
        {
            // a deck has to stay a deck, and a sheet that draws diagrams must keep tikz
            GiveTheRepoEverythingUpToTheKey();

            await _orchestrator.DoOnePassAsync();

            String tex = _git.LastCommit.First(t => NameOf(t) == Parallel).TexSource;

            StringAssert.Contains(tex, @"\documentclass{beamer}");
            StringAssert.Contains(tex, @"\usepackage{tikz}");
        }

        [TestMethod]
        public async Task PackagesWeSupplyOurselvesAreNotLoadedTwice()
        {
            // loading xcolor or babel twice with different options is a hard error
            GiveTheRepoEverythingUpToTheKey(
                "\\documentclass{article}\n\\usepackage{xcolor}\n\\usepackage[english]{babel}\n\\usepackage{tikz}");

            await _orchestrator.DoOnePassAsync();

            String tex = _git.LastCommit.First(t => NameOf(t) == Parallel).TexSource;

            Assert.AreEqual(1, Occurrences(tex, @"\usepackage{xcolor}"));
            Assert.AreEqual(1, Occurrences(tex, "{babel}"));
            StringAssert.Contains(tex, @"\usepackage{tikz}", "packages that are not ours are kept");
        }

        [TestMethod]
        public async Task TheProvenanceBlockNamesWhatItWasBuiltFrom()
        {
            GiveTheRepoEverythingUpToTheKey();

            await _orchestrator.DoOnePassAsync();

            String tex = _git.LastCommit.First(t => NameOf(t) == Parallel).TexSource;

            StringAssert.Contains(tex, "Polish Parallel Text Version of Quadratics");
            StringAssert.Contains(tex, RootEx, "it says which English file it came from");
            StringAssert.Contains(tex, Key, "and which vocabulary key");
            StringAssert.Contains(tex, "feel free", "and what is safe to edit");
        }

        // ---- what the prompt is told ----

        [TestMethod]
        public async Task ThePromptCarriesBothTheSheetAndItsVocabulary()
        {
            GiveTheRepoEverythingUpToTheKey("\\documentclass{article} % THE ENGLISH SHEET");

            await _orchestrator.DoOnePassAsync();

            foreach (String prompt in _llm.PromptsSeen)
            {
                StringAssert.Contains(prompt, "THE ENGLISH SHEET");
                StringAssert.Contains(prompt, "fraction = ulamek",
                    "the translation to use is fixed by the key, not left to the model");
            }
        }

        [TestMethod]
        public async Task ThePromptsDifferByForm()
        {
            GiveTheRepoEverythingUpToTheKey();

            await _orchestrator.DoOnePassAsync();

            Assert.AreEqual(2, _llm.PromptsSeen.Count);

            Assert.IsTrue(
                _llm.PromptsSeen.Any(p => p.Contains(@"\ealpara{translation}{english}", StringComparison.Ordinal)),
                "the parallel text prompt asks for whole sentences");

            Assert.IsTrue(
                _llm.PromptsSeen.Any(p => p.Contains("Leave the English exactly as it is", StringComparison.Ordinal)),
                "the tier 3 prompt asks for the English to be left alone");
        }

        [TestMethod]
        public async Task ThePromptDescribesTheColoursRatherThanLeavingThemToChance()
        {
            GiveTheRepoEverythingUpToTheKey();

            await _orchestrator.DoOnePassAsync();

            StringAssert.Contains(_llm.PromptsSeen[0], "green");
            StringAssert.Contains(_llm.PromptsSeen[0], "purple");
            StringAssert.Contains(_llm.PromptsSeen[0], "never write a colour command of your own");
        }

        [TestMethod]
        public async Task ThePromptAllowsAKeyWordToBeInflected()
        {
            // Most of these languages inflect, so the dictionary form of a word is often
            // not the form a sentence needs - Polish "ulamek" becomes "ulamka" after
            // "each". Demanding the given translation verbatim produces sentences that
            // read as wrong, and leaves the inflected word looking like it was never a
            // key word at all.
            GiveTheRepoEverythingUpToTheKey();

            await _orchestrator.DoOnePassAsync();

            foreach (String prompt in _llm.PromptsSeen)
            {
                StringAssert.Contains(prompt, "dictionary form");
                StringAssert.Contains(prompt, "still that word and is still marked");

                Assert.IsFalse(
                    prompt.Contains("use exactly the translation given", StringComparison.Ordinal),
                    "asking for the translation verbatim breaks any language that inflects");
            }
        }

        [TestMethod]
        public async Task ThePromptAllowsTheSheetToGrowButNotToOverlap()
        {
            GiveTheRepoEverythingUpToTheKey();

            await _orchestrator.DoOnePassAsync();

            StringAssert.Contains(_llm.PromptsSeen[0], "more pages than the");
            StringAssert.Contains(_llm.PromptsSeen[0], "Never place translated text on top of a diagram");
        }

        // ---- bodies that are not usable ----

        [TestMethod]
        [DataRow("\\begin{document}\\ealkey{x}", @"\end{document}")]
        [DataRow("\\documentclass{article}\n\\begin{document}\\ealkey{x}\\end{document}", "document class")]
        [DataRow("\\begin{document}\\newcommand{\\ealkey}[1]{#1}\\ealpara{a}{b}\\end{document}", "clash")]
        [DataRow("\\begin{document}Just the English, unchanged.\\end{document}", "none of the eal helpers")]
        public void ABodyThatCouldNotBeUsedIsDescribedRatherThanCommitted(String body, String because)
        {
            String? wrong = L2Document.WhatIsWrongWith(body);

            Assert.IsNotNull(wrong, "this body should have been rejected");
            StringAssert.Contains(wrong, because);
        }

        [TestMethod]
        public void AGoodBodyPassesItsChecks()
        {
            Assert.IsNull(L2Document.WhatIsWrongWith(GoodBody));
        }

        // ---- the preamble the original wrote for itself ----

        // a slide deck as the answer macro review leaves one: packages, a colour, a tikz
        // style and the three helpers that reveal its answers
        private const String DeckWithItsOwnPreamble =
            "% !TeX program = lualatex\n"
            + "\\documentclass{beamer}\n"
            + "\\usepackage{amsmath}\n"
            + "\\usepackage{xcolor}\n"
            + "\\usepackage{tikz}\n"
            + "\\usetikzlibrary{overlay-beamer-styles}\n"
            + "\\definecolor{MainBlue}{HTML}{1A6FA8}\n"
            + "\\tikzset{ans/.style={text=red}}\n"
            + "\\newcommand{\\ablank}[1]{%\n"
            + "  \\uncover<2->{\\textcolor{red}{#1}}%\n"
            + "}\n"
            + "\\newcommand{\\ashow}[1]{\\uncover<2->{\\textcolor{red}{\\small #1}}}\n"
            + "\\begin{document}\n"
            + "\\begin{frame}{Starter 1}$3+4=\\ablank{7}$\\end{frame}\n"
            + "\\end{document}\n";

        [TestMethod]
        [DataRow(@"\newcommand{\ablank}", "the answer overlay helpers a deck defines for itself")]
        [DataRow(@"\tikzset{ans/.style", "a tikz style it set up")]
        [DataRow(@"\definecolor{MainBlue}", "a colour it defined")]
        [DataRow(@"\usepackage{tikz}", "a package it loaded")]
        [DataRow(@"\usetikzlibrary{overlay-beamer-styles}", "a tikz library it loaded")]
        public void TheOriginalsPreambleIsCarriedIntoTheTranslation(String expected, String what)
        {
            String preamble = L2Document.PreambleOf(DeckWithItsOwnPreamble);

            StringAssert.Contains(preamble, expected, what + " has to come across");
        }

        [TestMethod]
        public void AMacroDefinedOverSeveralLinesComesAcrossWhole()
        {
            // the reason this reads a region rather than picking out lines it likes: a
            // definition that spans lines would be cut in half by anything line by line
            String preamble = L2Document.PreambleOf(DeckWithItsOwnPreamble);

            StringAssert.Contains(preamble, "\\newcommand{\\ablank}[1]{%\n  \\uncover<2->");
        }

        [TestMethod]
        [DataRow(@"\documentclass", "the class is written out separately")]
        [DataRow(@"\usepackage{xcolor}", "xcolor comes from our own block, and loading it twice clashes")]
        [DataRow(@"\begin{document}", "the preamble stops there")]
        [DataRow("Starter 1", "and the body is the model's, not the original's")]
        public void WhatWeProvideOurselvesIsNotCarriedTwice(String unwanted, String why)
        {
            String preamble = L2Document.PreambleOf(DeckWithItsOwnPreamble);

            Assert.IsFalse(preamble.Contains(unwanted, StringComparison.Ordinal), why);
        }

        [TestMethod]
        public void APreambleThatRedefinesAnEalHelperIsNotCarried()
        {
            String source = "\\documentclass{article}\n"
                + "\\newcommand{\\ealkey}[1]{#1}\n"
                + "\\newcommand{\\mine}[1]{#1}\n"
                + "\\begin{document}\\end{document}";

            String preamble = L2Document.PreambleOf(source);

            Assert.IsFalse(preamble.Contains(@"\ealkey", StringComparison.Ordinal),
                "it would clash with the block that defines the helpers");

            StringAssert.Contains(preamble, @"\mine", "but its own macros still come across");
        }

        [TestMethod]
        public void ATranslationMissingAMacroTheOriginalDefinedIsDescribed()
        {
            // what nine of the ten parallel texts in the repository did: kept using
            // \ablank and \ashow, and no longer defined either
            String assembled = "\\documentclass{beamer}\n"
                + "\\begin{document}\\ealpara{a}{$3+4=\\ablank{7}$}\\end{document}";

            String? missing = L2Document.WhatIsMissingFrom(assembled, DeckWithItsOwnPreamble);

            Assert.IsNotNull(missing);
            StringAssert.Contains(missing, @"\ablank");
        }

        [TestMethod]
        public void ATranslationThatCarriedThePreambleIsNotReportedAsMissingAnything()
        {
            String assembled = L2Document.Assemble(
                "\\begin{document}\\ealpara{a}{$3+4=\\ablank{7}$}\\end{document}",
                L2Document.DocumentClassOf(DeckWithItsOwnPreamble),
                L2Document.PreambleOf(DeckWithItsOwnPreamble),
                "Polish Parallel Text Version of Starters",
                new L2ColourOptions(),
                TexFixtures.Polish,
                builtFrom: "latex/starters/a.tex",
                vocabularyKey: "latex/starters/a/L2/pol/a_polishKey.tex",
                fallbackFont: TexFixtures.FallbackFont);

            Assert.IsNull(L2Document.WhatIsMissingFrom(assembled, DeckWithItsOwnPreamble));
        }

        // ---- line breaks with no line to break ----

        [TestMethod]
        [DataRow("\\ealpara{a}{b}\n\\newline\nmore", "straight after a block level helper")]
        [DataRow("\\ealpara{a}{b}\n\\\\\nmore", "the same with a double backslash")]
        [DataRow("text\n\n\\newline\nmore", "after a blank line")]
        [DataRow("text \\par \\newline more", "after \\par")]
        [DataRow("\\begin{itemize}\n\\newline\n\\item x\n\\end{itemize}", "at the top of an environment")]
        // the shape the repository actually had: two arguments, each with maths and
        // braces of its own, and the break on the line after the closing brace
        [DataRow("\\ealpara{Oblicz: \\[ \\dfrac{1}{2} \\]}{Calculate: \\[ \\dfrac{1}{2} \\]}\n  \\newline\nmore",
                 "after a helper whose arguments contain maths")]
        public void ALineBreakWithNothingToBreakIsRejected(String fragment, String where)
        {
            String body = "\\begin{document}\\ealkey{x}\n" + fragment + "\n\\end{document}";

            String? wrong = L2Document.WhatIsWrongWith(body);

            Assert.IsNotNull(wrong, "a line break " + where + " does not compile");
            StringAssert.Contains(wrong, "no line has been started");
        }

        [TestMethod]
        public void ALineBreakAtTheVeryTopOfTheBodyIsRejected()
        {
            String? wrong = L2Document.WhatIsWrongWith(
                "\\begin{document}\n\\\\[0.3em]\n\\ealkey{x}\n\\end{document}");

            Assert.IsNotNull(wrong);
            StringAssert.Contains(wrong, "no line has been started");
        }

        [TestMethod]
        [DataRow("some words\\\\[0.3em]\nand more", "in the middle of a paragraph")]
        [DataRow("a line\n\\newline\nthe next", "on its own line but inside a paragraph")]
        [DataRow("\\ealkey{word}\\\\\nnext", "after an inline helper, which does not end a paragraph")]
        [DataRow("$x = 1$ \\newline $y = 2$", "after maths")]
        public void AnOrdinaryLineBreakIsLeftAlone(String fragment, String where)
        {
            String body = "\\begin{document}\\ealkey{x} " + fragment + "\n\\end{document}";

            Assert.IsNull(L2Document.WhatIsWrongWith(body),
                "a line break " + where + " is how anybody would write one");
        }

        [TestMethod]
        public async Task AModelThatKeepsReturningAnUnusableBodyFailsThatFileOnly()
        {
            // a translation that used none of the helpers would be the English sheet under
            // another name - worse than a failure, because it looks finished
            GiveTheRepoEverythingUpToTheKey();

            _llm.DefaultResponse = "\\begin{document}Nothing translated here.\\end{document}";

            Assert.AreEqual(
                Orchestrator.PassOutcome.GenerationFailed, await _orchestrator.DoOnePassAsync());

            Assert.AreEqual(0, _git.CommitCalls.Count, "nothing should be pushed");
        }

        [TestMethod]
        public async Task ABodyWrappedInACodeFenceIsStillUsed()
        {
            GiveTheRepoEverythingUpToTheKey();

            _llm.DefaultResponse = "```latex\n" + GoodBody + "\n```";

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());

            String tex = _git.LastCommit.First(t => NameOf(t) == Parallel).TexSource;

            Assert.IsFalse(tex.Contains("```", StringComparison.Ordinal), "the fence must not survive");
        }

        // ---- staleness reaches the translations ----

        [TestMethod]
        public async Task EditingTheEnglishSheetRemovesItsTranslations()
        {
            GiveTheRepoEverythingUpToTheKey();

            await _orchestrator.DoOnePassAsync();

            // a person edits the English sheet, so it becomes the youngest file
            _git.AddFile(RootEx, ageCommits: 0, contents: "\\documentclass{beamer} % EDITED");

            Assert.AreEqual(
                Orchestrator.PassOutcome.RemovedStaleFiles, await _orchestrator.DoOnePassAsync());

            var removed = _git.RemoveFilesCalls.Last();

            CollectionAssert.Contains(removed, Parallel, "a translation of an edited sheet is stale");
            CollectionAssert.Contains(removed, Key, "and so is the key it came from");
            CollectionAssert.Contains(removed, Vocab);
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
    }
}
