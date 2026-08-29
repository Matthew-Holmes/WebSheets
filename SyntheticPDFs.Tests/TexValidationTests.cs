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
