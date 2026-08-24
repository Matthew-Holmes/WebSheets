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
    }
}
