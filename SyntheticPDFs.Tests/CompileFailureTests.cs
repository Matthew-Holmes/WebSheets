using SyntheticPDFs.Configuration;
using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Rendering;
using SyntheticPDFs.Tests.Fakes;
using System.Text.RegularExpressions;

namespace SyntheticPDFs.Tests
{
    // The ten generated files that reached the repository and did not compile, each kept
    // here in the shape it actually had. A check that stops catching one fails in this
    // file, rather than in CI - where these sat behind thirty-five red runs in a row.
    [TestClass]
    public class CompileFailureTests
    {
        // a deck as the answer macro review leaves one, with helpers of its own
        private const String Deck = """
            \documentclass{beamer}
            \usepackage{tikz}
            \newcommand{\ablank}[1]{%
              \alt<2>{\textcolor{red}{#1}}{\underline{\phantom{#1}}}%
            }
            \newcommand{\ashow}[1]{\uncover<2->{\textcolor{red}{\small #1}}}
            \newenvironment{answers}{\par}{\par}
            \begin{document}
            \begin{frame}{Starter 1}Expand $2(x+3)$: \ablank{$2x+6$}\end{frame}
            \end{document}
            """;

        private const String Worksheet = """
            \documentclass{article}
            \begin{document}
            Find $\sqrt{16}$.
            \end{document}
            """;

        private static readonly VocabTerm[] Terms =
        {
            new VocabTerm
            {
                English = "fraction", Definition = "part of a whole",
                Translation = "ulamek", TranslatedDefinition = "czesc calosci",
            },
        };

        // a body the model might return, wrapped so that it passes every check but the
        // one under test
        private static String Body(String inside) =>
            "\\begin{document}\n\\ealkey{x}\n" + inside + "\n\\end{document}";

        #region Cause A - the sheet's own macros defined a second time (4 files)

        [TestMethod]
        public void ABodyThatDefinesTheSheetsOwnMacroAgainIsRejected()
        {
            // expandingBracketsStarter_polishParallelText: the model was shown the whole
            // file, and wrote the preamble's definitions out again after \begin{document}
            String body = Body("""
                \newcommand{\ablank}[1]{%
                  \alt<2>{\textcolor{red}{#1}}{\underline{\phantom{#1}}}%
                }
                \ealpara{Rozwin}{Expand}
                """);

            String? wrong = L2Document.WhatIsWrongWith(body, Deck);

            Assert.IsNotNull(wrong, "this is \"Command \\ablank already defined\"");
            StringAssert.Contains(wrong, @"\ablank");
            StringAssert.Contains(wrong, "already defines");
        }

        [TestMethod]
        public void AnEnvironmentTheSheetDefinesIsCaughtToo()
        {
            String? wrong = L2Document.WhatIsWrongWith(
                Body(@"\newenvironment{answers}{}{} \ealpara{a}{b}"), Deck);

            Assert.IsNotNull(wrong);
            StringAssert.Contains(wrong, "the answers environment");
        }

        [TestMethod]
        public void TheSameMacroDefinedTwiceInTheBodyIsRejected()
        {
            String? wrong = L2Document.WhatIsWrongWith(
                Body(@"\newcommand{\half}{one half} \newcommand{\half}{one half} \ealpara{a}{b}"), Deck);

            Assert.IsNotNull(wrong);
            StringAssert.Contains(wrong, @"\half");
        }

        [TestMethod]
        [DataRow(@"\newcommand{\half}{one half}", "a macro of its own, which nothing else defines")]
        [DataRow(@"\renewcommand{\ablank}[1]{#1}", "a redefinition on purpose, which is legal")]
        [DataRow(@"\providecommand{\ashow}[1]{#1}", "a definition that gives way to the existing one")]
        public void DefinitionsThatCompileAreLeftAlone(String definition, String what)
        {
            Assert.IsNull(
                L2Document.WhatIsWrongWith(Body(definition + @" \ealpara{a}{b}"), Deck), what);
        }

        [TestMethod]
        public void TheModelIsShownTheBodyAndNotThePreamble()
        {
            // it cannot repeat definitions it was never shown
            String prompt = SourceGenerator.GenerateParallelTextPrompt(
                Deck, Terms, TexFixtures.Polish, new L2ColourOptions(), SheetArchetypes.QuestionSlides);

            Assert.IsFalse(prompt.Contains(@"\newcommand{\ablank}", StringComparison.Ordinal));
            Assert.IsFalse(prompt.Contains(@"\usepackage{tikz}", StringComparison.Ordinal));
            StringAssert.Contains(prompt, @"\begin{frame}{Starter 1}");
        }

        [TestMethod]
        [DataRow("ParallelText")]
        [DataRow("Tier3Only")]
        public void TheModelIsToldWhatThePreambleDefinesWithoutBeingShownHow(String form)
        {
            // the body is full of \ablank, so the model has to know it is there to use
            String prompt = form == "ParallelText"
                ? SourceGenerator.GenerateParallelTextPrompt(
                    Deck, Terms, TexFixtures.Polish, new L2ColourOptions(), SheetArchetypes.QuestionSlides)
                : SourceGenerator.GenerateTier3OnlyPrompt(
                    Deck, Terms, TexFixtures.Polish, new L2ColourOptions(), SheetArchetypes.QuestionSlides);

            StringAssert.Contains(prompt, @"already defines \ablank, \ashow, the answers environment.");
            StringAssert.Contains(prompt, "never define any of them again");
        }

        [TestMethod]
        public void ASheetWithNoMacrosOfItsOwnIsToldNothingAboutThem()
        {
            String prompt = SourceGenerator.GenerateParallelTextPrompt(
                Worksheet, Terms, TexFixtures.Polish, new L2ColourOptions(), SheetArchetypes.Worksheet);

            Assert.IsFalse(prompt.Contains("preamble is added for you as well", StringComparison.Ordinal));
        }

        [TestMethod]
        public void TheBodyStartsAtBeginDocument()
        {
            String body = L2Document.BodyOf(Deck);

            Assert.IsTrue(body.StartsWith(@"\begin{document}", StringComparison.Ordinal), body);
            StringAssert.Contains(body, @"\end{document}");
        }

        [TestMethod]
        public void ABeginDocumentInACommentIsNotWhereTheBodyStarts()
        {
            String source = """
                \documentclass{article}
                % the body starts at \begin{document}, below
                \newcommand{\mine}{x}
                \begin{document}
                Hello
                \end{document}
                """;

            String body = L2Document.BodyOf(source);

            Assert.IsFalse(body.Contains(@"\mine}", StringComparison.Ordinal), body);
            StringAssert.StartsWith(body, @"\begin{document}");
        }

        #endregion

        #region Cause B - a line break with no line to break (2 files)

        [TestMethod]
        [DataRow("""
            \begin{enumerate}
              \item \ealpara{Ile stopni?}{How many degrees? Answer:}
                    \vspace{0.6cm}\\
                    \hspace*{1.6em}\ansbox
            \end{enumerate}
            """, "pieChartsDrawing_polishParallelText - vertical space does not start a line")]
        [DataRow("""
            {\begin{ealglossed}
               \ealgloss{Common denominator}{Wspolny mianownik} $20$:
               \end{ealglossed}\\[0.4em]
               $\dfrac{12}{20}$}
            """, "fractionsQuickQuestions_polishTier3Only - ealglossed ends its paragraph")]
        [DataRow("\\begin{itemize}\\item a\\end{itemize}\\\\", "after a list, which ends its paragraph")]
        [DataRow("some words\n\n\\vspace{1em}\\\\", "after vertical space that follows a blank line")]
        [DataRow("some words\\newpage\\\\", "after a new page, which ends the paragraph")]
        public void ALineBreakTheCheckUsedToMissIsRejected(String fragment, String why)
        {
            String? wrong = L2Document.WhatIsWrongWith(Body(fragment));

            Assert.IsNotNull(wrong, why);
            StringAssert.Contains(wrong, "no line has been started");
        }

        [TestMethod]
        [DataRow("some words \\vspace{1em}\\\\ more words", "vertical space in the middle of a paragraph")]
        [DataRow("words\\medskip\\\\ more", "a skip in the middle of a paragraph")]
        [DataRow("\\begin{tikzpicture}\\draw (0,0)--(1,1);\\end{tikzpicture}\\\\ next", "a picture is a box in a line")]
        [DataRow("\\begin{minipage}{3cm}x\\end{minipage}\\\\ next", "and so is a minipage")]
        public void ALineBreakWithALineOpenIsLeftAlone(String fragment, String why)
        {
            Assert.IsNull(L2Document.WhatIsWrongWith(Body(fragment)), why);
        }

        #endregion

        #region Cause C - maths that lost its dollars (2 files)

        [TestMethod]
        public void MathsThatLostItsDollarsIsRejected()
        {
            // shapeAndAveragesStarters_polishParallelText: the English had
            // \ablank{$F=10+2\sqrt{13}$}, and the translation dropped the $
            String original = """
                \documentclass{beamer}
                \begin{document}
                Find the perimeter. \ablank{$D=24$},\ \ablank{$F=10+2\sqrt{13}$}.
                \end{document}
                """;

            String body = Body("""
                \ealpara{8. Oblicz obwod.}{8. Find the perimeter. \ablank{D=24},\ \ablank{F=10+2\sqrt{13}}.}
                """);

            String? wrong = L2Document.WhatIsWrongWith(body, original);

            Assert.IsNotNull(wrong, "this is \"Missing $ inserted\"");
            StringAssert.Contains(wrong, @"\sqrt");
            StringAssert.Contains(wrong, "$ around it");
        }

        [TestMethod]
        public void MathsWrittenStraightIntoATranslationIsRejected()
        {
            // pieChartsDrawing_urduParallelText: the English half kept its maths, the
            // translated half was written with none
            String body = Body("""
                \ealpara{کلیدی فارمولا: \dfrac{مقدار}{کل} \times 360°}{\textbf{Key formula:}\quad $\displaystyle\text{\ealkey{sector}} = \dfrac{\text{amount}}{\text{total}} \times 360°$}
                """);

            String? wrong = L2Document.WhatIsWrongWith(body);

            Assert.IsNotNull(wrong);
            StringAssert.Contains(wrong, @"\dfrac");
        }

        [TestMethod]
        [DataRow(@"$\sqrt{13}$", "between dollars")]
        [DataRow(@"\(\frac{1}{2}\)", "between \\( and \\)")]
        [DataRow(@"\[\frac{1}{2}\]", "between \\[ and \\]")]
        [DataRow(@"$$\frac{1}{2}$$", "between double dollars")]
        [DataRow("\\begin{align*}x &= \\frac{1}{2}\\end{align*}", "in a maths environment")]
        [DataRow("% \\frac{1}{2} is in a comment", "in a comment")]
        [DataRow(@"costs \$5, or $\frac{1}{2}$ of it", "after an escaped dollar, which opens nothing")]
        [DataRow(@"\ablank{$2x+6$}", "inside a helper that kept its dollars")]
        public void MathsInsideMathsIsLeftAlone(String fragment, String where)
        {
            Assert.IsNull(L2Document.WhatIsWrongWith(Body(fragment)), where);
        }

        [TestMethod]
        public void AnEscapedDollarDoesNotOpenMaths()
        {
            Assert.IsNotNull(L2Document.WhatIsWrongWith(Body(@"costs \$5, which is \sqrt{25}")));
        }

        [TestMethod]
        public void AnInlineSpanLeftOpenStopsAtTheParagraph()
        {
            // otherwise one stray $ would hide everything after it from the check
            Assert.IsNotNull(L2Document.WhatIsWrongWith(Body("a stray $ here\n\nthen \\frac{1}{2}")));
        }

        [TestMethod]
        public void ASheetWhoseEnglishDoesTheSameIsNotHeldToIt()
        {
            // \sqfrac opens maths for its own arguments, so \sqrt outside any dollars is
            // correct here - and nothing short of expanding \sqfrac could tell
            String original = """
                \documentclass{article}
                \newcommand{\sqfrac}[2]{\(\frac{#1}{#2}\)}
                \begin{document}
                Simplify \sqfrac{\sqrt 2}{3}.
                \end{document}
                """;

            Assert.IsNull(L2Document.WhatIsWrongWith(
                Body(@"\ealpara{Uprosc \sqfrac{\sqrt 2}{3}.}{Simplify \sqfrac{\sqrt 2}{3}.}"), original));
        }

        #endregion

        #region Cause D1 - a helper that does not exist (1 file)

        [TestMethod]
        public void AHelperTheModelInventedIsRejected()
        {
            // recurringDecimalsToFractions_polishTier3Only: \ealgloss, shortened
            String body = Body("""
                \item \begin{ealglossed}\textbf{Sevenths.} One seventh has a repeating block of six \ealgl{digits}{cyfry}:
                $\dfrac{1}{7}=0.\dot{1}4285\dot{7}$.\end{ealglossed}
                """);

            String? wrong = L2Document.WhatIsWrongWith(body);

            Assert.IsNotNull(wrong, "this is \"Undefined control sequence\"");
            StringAssert.Contains(wrong, @"\ealgl,");
        }

        [TestMethod]
        public void AnEnvironmentTheModelInventedIsRejected()
        {
            String? wrong = L2Document.WhatIsWrongWith(Body(@"\begin{ealgloss}x\end{ealgloss}"));

            Assert.IsNotNull(wrong);
            StringAssert.Contains(wrong, "an environment called ealgloss");
        }

        [TestMethod]
        public void TheHelperNamesAreExactlyTheOnesTheBlocksDefine()
        {
            // held against the definitions themselves, so a helper added there and not
            // here is found now, rather than as every body using it being rejected
            String blocks = L2Macros.Definitions(new L2ColourOptions())
                + L2Macros.LanguagePreamble(TexFixtures.Polish, TexFixtures.FallbackFont);

            String[] commands = Regex.Matches(blocks, @"\\new(?:command|savebox|length)\{\\(eal[A-Za-z]+)\}")
                .Select(m => m.Groups[1].Value)
                .ToArray();

            String[] environments = Regex.Matches(blocks, @"\\newenvironment\{(eal[A-Za-z]+)\}")
                .Select(m => m.Groups[1].Value)
                .ToArray();

            CollectionAssert.AreEquivalent(commands, L2Macros.HelperNames.ToArray());
            CollectionAssert.AreEquivalent(environments, L2Macros.HelperEnvironments.ToArray());
        }

        #endregion

        #region Cause D2 - a deck's command in a document that is not a deck (1 file)

        // indicesQuickQuestions_workedSolutions: an article written from a beamer deck,
        // which carried \texorpdfstring across from it
        private const String ArticleWithDeckHabits = """
            \documentclass[11pt]{article}
            \usepackage[margin=1in]{geometry}
            \usepackage{amsmath,amssymb}
            \usepackage{xcolor}
            \usepackage{enumitem}
            \usepackage{parskip}
            \begin{document}
            \section{Indices of the form \texorpdfstring{$\frac{1}{a}$}{1/a}}
            \end{document}
            """;

        [TestMethod]
        public void AHyperrefCommandInADocumentWithoutHyperrefIsRejected()
        {
            String? wrong = SourceGenerator.WhatIsWrongWithTex(ArticleWithDeckHabits);

            Assert.IsNotNull(wrong, "this is \"Undefined control sequence\"");
            StringAssert.Contains(wrong, @"\texorpdfstring");
            StringAssert.Contains(wrong, "hyperref");
        }

        [TestMethod]
        [DataRow(@"\usepackage{hyperref}", "loaded on its own")]
        [DataRow(@"\usepackage[colorlinks=true]{hyperref}", "loaded with options")]
        [DataRow(@"\usepackage{amsmath,hyperref}", "loaded in a list")]
        public void AHyperrefCommandIsFineOnceHyperrefIsLoaded(String load, String how)
        {
            String tex = ArticleWithDeckHabits.Replace(@"\usepackage{parskip}", load);

            Assert.IsNull(SourceGenerator.WhatIsWrongWithTex(tex), how);
        }

        [TestMethod]
        public void ADeckHasEveryOneOfThemAlready()
        {
            // beamer loads hyperref for itself
            String deck = """
                \documentclass{beamer}
                \begin{document}
                \begin{frame}{\texorpdfstring{$\frac{1}{a}$}{1/a}}\uncover<2->{x}\end{frame}
                \end{document}
                """;

            Assert.IsNull(SourceGenerator.WhatIsWrongWithTex(deck));
        }

        [TestMethod]
        public void AnOverlayCommandOutsideADeckIsRejected()
        {
            String tex = FakeLLMService.ValidTex(@"The answer is \uncover<2->{7}.");

            String? wrong = SourceGenerator.WhatIsWrongWithTex(tex);

            Assert.IsNotNull(wrong);
            StringAssert.Contains(wrong, @"\uncover");
            StringAssert.Contains(wrong, "beamer");
        }

        [TestMethod]
        public void ADocumentMayDefineOneOfTheseForItself()
        {
            // \alert is beamer's, but an article is free to have one of its own
            String tex = """
                \documentclass{article}
                \usepackage{xcolor}
                \newcommand{\alert}[1]{\textcolor{red}{#1}}
                \begin{document}
                \alert{Careful} with the signs.
                \end{document}
                """;

            Assert.IsNull(SourceGenerator.WhatIsWrongWithTex(tex));
        }

        #endregion

        #region The maths delimiter checks, on a translated body as well

        [TestMethod]
        public async Task ATranslatedBodyWithUnpairedMathsIsRejected()
        {
            var llm = new FakeLLMService { DefaultResponse = Body(@"\ealpara{Oblicz \(x}{Find \(x\)}") };

            await Assert.ThrowsExceptionAsync<Exception>(() => SourceGenerator.GenerateTranslatedBody(
                Worksheet, Terms, TexFixtures.Polish, new L2ColourOptions(),
                SheetForm.ParallelText, SheetArchetypes.Worksheet, llm));

            Assert.IsTrue(llm.Logged.Any(m => m.Contains("do not pair up", StringComparison.Ordinal)),
                String.Join("\n", llm.Logged));
        }

        [TestMethod]
        public async Task ADollarInsideMathsInATranslatedBodyIsMendedRatherThanPaidForAgain()
        {
            var llm = new FakeLLMService
            {
                DefaultResponse = Body(@"\ealpara{Oblicz \(x = \ablank{$5$}\)}{Find \(x = \ablank{$5$}\)}"),
            };

            String body = await SourceGenerator.GenerateTranslatedBody(
                Worksheet, Terms, TexFixtures.Polish, new L2ColourOptions(),
                SheetForm.ParallelText, SheetArchetypes.Worksheet, llm);

            Assert.AreEqual(1, llm.CallCount, "dropping the inner dollars costs nothing");
            StringAssert.Contains(body, @"\ablank{5}");
        }

        #endregion
    }
}
