using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
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
        public async Task ThePromptsDifferByRendition()
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
