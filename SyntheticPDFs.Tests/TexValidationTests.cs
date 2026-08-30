using SyntheticPDFs.Logic;
using SyntheticPDFs.Tests.Fakes;

namespace SyntheticPDFs.Tests
{
    [TestClass]
    public class TexValidationTests
    {
        private readonly FakeLLMService _llm = new();

        [TestMethod]
        public void WellFormedDocumentIsValid()
        {
            Assert.IsTrue(SourceGenerator.IsValidTex(FakeLLMService.ValidTex("Some workings.")));
        }

        [TestMethod]
        public void MarkdownFenceMakesItInvalid()
        {
            String fenced = "```latex\n" + FakeLLMService.ValidTex("x") + "\n```";

            Assert.IsFalse(SourceGenerator.IsValidTex(fenced));
        }

        [TestMethod]
        public void FixupStripsMarkdownFences()
        {
            String fenced = "```latex\n" + FakeLLMService.ValidTex("x") + "\n```";

            String fixedUp = SourceGenerator.TryFixupTex(fenced, _llm);

            Assert.IsTrue(SourceGenerator.IsValidTex(fixedUp), $"still invalid:\n{fixedUp}");
            Assert.IsFalse(fixedUp.Contains("```"));
        }

        [TestMethod]
        public void UnbalancedBeginIsInvalid()
        {
            String bad = "\\documentclass{article}\n\\begin{document}\n\\begin{enumerate}\n\\item hi\n\\end{document}";

            Assert.IsFalse(SourceGenerator.IsValidTex(bad));
        }

        [TestMethod]
        public void FixupClosesUnclosedEnvironments()
        {
            String bad = "\\documentclass{article}\n\\begin{document}\n\\begin{enumerate}\n\\item hi\n\\end{enumerate}";

            String fixedUp = SourceGenerator.CloseUnclosedBegins(bad, _llm);

            Assert.IsTrue(fixedUp.Contains("\\end{document}"), $"document was not closed:\n{fixedUp}");
        }

        [TestMethod]
        [DataRow('\u21D2')] // the implication arrow
        [DataRow('\u2248')] // approximately equal
        public void UntypesettableCharactersAreInvalidAndGetSubstituted(char bad)
        {
            String source = FakeLLMService.ValidTex($"so x {bad} y");

            Assert.IsFalse(SourceGenerator.IsValidTex(source), "should be rejected before fixup");

            String fixedUp = SourceGenerator.TryFixupTex(source, _llm);

            Assert.IsFalse(fixedUp.Contains(bad), "character should have been replaced");
            Assert.IsTrue(SourceGenerator.IsValidTex(fixedUp), $"still invalid:\n{fixedUp}");
        }

        [TestMethod]
        public void CommentedOutBeginDoesNotCountTowardsBalance()
        {
            String source = "\\documentclass{article}\n\\begin{document}\n% \\begin{enumerate}\nhi\n\\end{document}";

            Assert.IsTrue(SourceGenerator.IsValidTex(source));
        }

        [TestMethod]
        public async Task RetriesThenGivesUpWhenOutputNeverBecomesValid()
        {
            var llm = new FakeLLMService { DefaultResponse = "this is prose, not tex" };

            String? result = await SourceGenerator.TryGetValidTex(llm, "prompt", retry: 3);

            Assert.IsNull(result, "unusable output should come back as null, not throw");
            Assert.AreEqual(3, llm.CallCount, "should have used all its retries");
        }

        [TestMethod]
        public async Task ReturnsFirstValidResponseWithoutRetrying()
        {
            var llm = new FakeLLMService { DefaultResponse = FakeLLMService.ValidTex("fine") };

            String? result = await SourceGenerator.TryGetValidTex(llm, "prompt", retry: 3);

            Assert.IsNotNull(result);
            Assert.AreEqual(1, llm.CallCount);
        }
        // ---- math mode nested inside math mode ----

        // a $ inside \( ... \) closes the maths early and LaTeX stops with Missing $ inserted.
        // this came up for real: \(\frac{3}{8} = 3 \times 12.5\% = \ablank{$37.5\%$}\)
        private static String Doc(String body) => FakeLLMService.ValidTex(body);

        [TestMethod]
        public void ADollarInsideInlineMathIsInvalid()
        {
            Assert.IsFalse(SourceGenerator.IsValidTex(
                Doc(@"\(\frac{3}{8} = 3 \times 12.5\% = \ablank{$37.5\%$}\).")));
        }

        [TestMethod]
        public void ADollarInsideDisplayMathIsInvalid()
        {
            Assert.IsFalse(SourceGenerator.IsValidTex(Doc(@"\[ x = \ashow{$5$} \]")));
        }

        [TestMethod]
        public void FixupDropsTheInnerDelimitersAndKeepsTheAnswer()
        {
            String bad = Doc(@"\(\frac{3}{8} = 3 \times 12.5\% = \ablank{$37.5\%$}\).");

            String fixedUp = SourceGenerator.TryFixupTex(bad, _llm);

            Assert.IsTrue(SourceGenerator.IsValidTex(fixedUp), $"still invalid:\n{fixedUp}");
            StringAssert.Contains(fixedUp, @"\ablank{37.5\%}", "the answer survives, only the $ go");
            Assert.IsFalse(fixedUp.Contains("$"), "and nothing else is left behind");
        }

        [TestMethod]
        public void PlainInlineMathIsFine()
        {
            Assert.IsTrue(SourceGenerator.IsValidTex(Doc(@"\(\frac{3}{8} = \ablank{37.5\%}\).")));
        }

        [TestMethod]
        public void DollarMathOutsideAnyParensIsFine()
        {
            // the helpers are used this way in ordinary text and must not be flagged
            Assert.IsTrue(SourceGenerator.IsValidTex(Doc(@"What is $2+2$? \ablank{$4$}")));
        }

        [TestMethod]
        public void AnEscapedPercentIsNotAComment()
        {
            // a naive comment strip would cut the line at \% and never see the closing \)
            Assert.IsFalse(SourceGenerator.IsValidTex(Doc(@"\(12.5\% = \ablank{$x$}\)")));
        }

        [TestMethod]
        public void AnEscapedDollarIsNotADelimiter()
        {
            // \$ is a literal dollar sign and is legal inside maths
            Assert.IsTrue(SourceGenerator.IsValidTex(Doc(@"\(\text{costs } \$5\)")));
        }

        [TestMethod]
        public void ADollarInACommentInsideMathIsIgnored()
        {
            // the comment sits inside the span, so this only passes if comments are skipped
            String tex = Doc("\\(x = 5 % a note about $money$\n + 2\\)");

            // guard: the span really is inline maths, not a line break followed by a bracket
            StringAssert.Contains(tex, @"\(x = 5");

            Assert.IsFalse(SourceGenerator.HasNestedMathMode(tex));
        }

        [TestMethod]
        public void AnUnclosedSpanIsLeftAlone()
        {
            // broken for a different reason, so reporting it as this one would mislead
            Assert.IsFalse(SourceGenerator.HasNestedMathMode(Doc(@"\(x = 5 and later $2+2$")));
        }

        // ---- correct versions of the sequence line must survive untouched ----

        // the reported line was \(3, 6, 9, 12, \_, \_, \_. with no closing \). these are the
        // ways of writing it correctly, and the fixup machinery must not alter any of them.
        // the \_ in particular is read as one unit by the scanner, so it is inert
        [TestMethod]
        [DataRow(@"Complete the sequence \(3, 6, 9, 12, \_, \_, \_\).", "closed before the full stop")]
        [DataRow(@"Complete the sequence \(3, 6, 9, 12, \_, \_, \_.\)", "closed after it")]
        [DataRow(@"Complete the sequence $3, 6, 9, 12, \_, \_, \_$.", "written in dollars instead")]
        [DataRow(@"Complete the sequence \(3, 6, 9, \ablank{12}, \ablank{15}\).", "with the blanks filled by helpers")]
        [DataRow(@"\(3, 6, 9\) then separately $12, 15$ and \(18\).", "several spans on one line")]
        [DataRow(@"\(\_\)", "nothing but an escaped underscore")]
        [DataRow(@"\_ \_ \_ with no maths at all", "underscores outside any span")]
        [DataRow(@"\(a \_ b", "an underscore in a span that never closes")]
        [DataRow(@"\[ 3, 6, 9, \_, \_ \]", "display maths")]
        [DataRow(@"\(x = 1 % a note about \_ and $money$", "a comment after the maths opens")]
        [DataRow(@"\(12.5\% + \_\)", "an escaped percent next to an underscore")]
        [DataRow(@"\(\text{cost } \$5 \_\)", "an escaped dollar next to one")]
        public void AValidSequenceLineIsLeftExactlyAsItIs(String line, String why)
        {
            String tex = FakeLLMService.ValidTex(line);

            Assert.AreEqual(tex, SourceGenerator.TryFixupTex(tex, _llm), $"fixup altered it: {why}");
            Assert.IsFalse(SourceGenerator.HasNestedMathMode(tex), why);
        }

        [TestMethod]
        [DataRow(@"Complete the sequence \(3, 6, 9, 12, \_, \_, \_\).")]
        [DataRow(@"Complete the sequence $3, 6, 9, 12, \_, \_, \_$.")]
        [DataRow(@"\(3, 6, 9\) then separately $12, 15$ and \(18\).")]
        [DataRow(@"\[ 3, 6, 9, \_, \_ \]")]
        public void ACorrectlyClosedSequenceLineIsValid(String line)
        {
            Assert.IsTrue(SourceGenerator.IsValidTex(FakeLLMService.ValidTex(line)));
            Assert.IsFalse(SourceGenerator.HasUnbalancedMathDelimiters(FakeLLMService.ValidTex(line)));
        }

        [TestMethod]
        public void AMultiLineSpanWithUnderscoresIsLeftAlone()
        {
            // inline maths may run over a line break as long as there is no blank line
            String tex = FakeLLMService.ValidTex(String.Join("\n",
                @"Complete the sequence \(3, 6, 9,",
                @"12, \_, \_, \_\).",
                @"Then answer $2+2$."));

            Assert.AreEqual(tex, SourceGenerator.TryFixupTex(tex, _llm));
        }

        // ---- the repair can only ever remove dollar signs ----

        private static String WithoutDollars(String tex) =>
            new String(tex.Where(c => c != '$').ToArray());

        // the reported line lost its \), so the first question was whether this machinery
        // could have taken it. it cannot: the only indices it drops are ones it recorded as a
        // Dollar token, so every other character survives whatever the input
        [TestMethod]
        [DataRow(@"\(\frac{3}{8} = \ablank{$37.5\%$}\)")]
        [DataRow(@"Complete the sequence \(3, 6, 9, 12, \_, \_, \_.")]
        [DataRow(@"\(a\) $b$ \(c\) $d$")]
        [DataRow(@"\[ x = \ashow{$5$} \]")]
        [DataRow(@"\(x = 1 \_ \% \$ \) $2+2$")]
        public void TheRepairNeverTouchesAnythingButDollars(String line)
        {
            String tex = FakeLLMService.ValidTex(line);

            String repaired = SourceGenerator.RemoveNestedMathDelimiters(tex);

            Assert.AreEqual(
                WithoutDollars(tex),
                WithoutDollars(repaired),
                "a closing delimiter, an underscore or anything else must be impossible to lose");
        }

        [TestMethod]
        public void AnEscapedDollarSurvivesTheRepair()
        {
            // \$ is a literal dollar sign, not a delimiter, so it is not a candidate either
            String tex = FakeLLMService.ValidTex(@"\(\text{cost } \$5 = \ablank{$5$}\)");

            String repaired = SourceGenerator.RemoveNestedMathDelimiters(tex);

            StringAssert.Contains(repaired, @"\$5", "the escaped one stays");
            StringAssert.Contains(repaired, @"\ablank{5}", "only the nested pair goes");
        }

        // ---- an unclosed \( must not reach across the file ----

        // a real one: the model wrote \(3, 6, 9, 12, \_, \_, \_. and never closed it. the scan
        // for the closing \) then ran on to the next one anywhere in the document, so every $
        // in between was read as nested maths and stripped out - silently corrupting content
        // that had nothing wrong with it
        private static String UnclosedThenLaterMaths() => FakeLLMService.ValidTex(String.Join("\n",
            @"\textbf{Question:} Complete the sequence \(3, 6, 9, 12, \_, \_, \_.",
            @"\end{frame}",
            @"\begin{frame}",
            @"Later maths: $2+2=4$ and \(x = 1\)."));

        [TestMethod]
        public void AnUnclosedSpanDoesNotSwallowLaterMaths()
        {
            String tex = UnclosedThenLaterMaths();

            Assert.IsFalse(
                SourceGenerator.HasNestedMathMode(tex),
                "the later $2+2=4$ is not inside anything");
        }

        [TestMethod]
        public void FixupLeavesLaterMathsAloneWhenASpanIsUnclosed()
        {
            String tex = UnclosedThenLaterMaths();

            String after = SourceGenerator.RemoveNestedMathDelimiters(tex);

            Assert.AreEqual(tex, after, "not one character may be removed");
            StringAssert.Contains(after, "$2+2=4$", "the dollars used to be deleted from here");
        }

        [TestMethod]
        public void AnUnclosedSpanIsReportedAsUnbalanced()
        {
            // it is a hard LaTeX error, so the file is rejected and generated again rather
            // than being patched up by guesswork
            Assert.IsTrue(SourceGenerator.HasUnbalancedMathDelimiters(UnclosedThenLaterMaths()));
            Assert.IsFalse(SourceGenerator.IsValidTex(UnclosedThenLaterMaths()));
        }

        [TestMethod]
        [DataRow(@"\(x = 1\) and \(y = 2\)", false)]
        [DataRow(@"\[ x = 1 \] then \(y\)", false)]
        [DataRow(@"\(x = 1", true)]
        [DataRow(@"x = 1\)", true)]
        [DataRow(@"\[ x = 1", true)]
        [DataRow(@"\(a\) \(b", true)]
        public void BalanceIsCountedBothWays(String body, bool unbalanced)
        {
            Assert.AreEqual(unbalanced, SourceGenerator.HasUnbalancedMathDelimiters(FakeLLMService.ValidTex(body)));
        }

        [TestMethod]
        public void ASpanIsNotCarriedAcrossAParagraphBreak()
        {
            // neither kind of maths may contain a blank line, so a $ after one is not nested
            String tex = FakeLLMService.ValidTex(String.Join("\n",
                @"open here \(x = 1",
                "",
                @"a later $2+2$ line \)"));

            Assert.IsFalse(SourceGenerator.HasNestedMathMode(tex));
        }

        [TestMethod]
        public void ASecondOpenerEndsTheSearchForACloser()
        {
            // \( twice with only one \) means the first never closed
            String tex = FakeLLMService.ValidTex(@"\(a = 1 then $2+2$ then \(b = 2\) and \(c\)");

            Assert.IsFalse(SourceGenerator.HasNestedMathMode(tex));
        }

        [TestMethod]
        public void RealNestingIsStillCaughtWhenTheFileIsBalanced()
        {
            // the fix must not have blunted the original check
            Assert.IsTrue(SourceGenerator.HasNestedMathMode(
                FakeLLMService.ValidTex(@"\(\frac{3}{8} = \ablank{$37.5\%$}\)")));
        }

        [TestMethod]
        public void ALineBreakIsNotAnOpener()
        {
            // \\ followed by ( is a break then a bracket, not the start of inline maths
            Assert.IsFalse(SourceGenerator.HasNestedMathMode(Doc(@"row one \\(a) then $2+2$")));
        }

        [TestMethod]
        public void SeveralSpansAreAllChecked()
        {
            Assert.IsTrue(SourceGenerator.HasNestedMathMode(
                Doc(@"\(a = 1\) then \(b = \ablank{$2$}\) then \(c = 3\)")));
        }
    }
}
