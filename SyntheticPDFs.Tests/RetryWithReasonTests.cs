using SyntheticPDFs.Configuration;
using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Rendering;
using SyntheticPDFs.Tests.Fakes;

namespace SyntheticPDFs.Tests
{
    // A retry is told why the attempt before it was turned down, rather than sent the
    // identical prompt to pay again for what is likely the same mistake.
    [TestClass]
    public class RetryWithReasonTests
    {
        private const String Prompt = "Write the worked solutions.";

        private const String Marker = "--- EARLIER ATTEMPTS ---";

        private static readonly String Valid = FakeLLMService.ValidTex("Two plus two is four.");

        // each rejected for a reason of its own, which the retry is expected to repeat
        private static readonly String NeedsHyperref =
            FakeLLMService.ValidTex(@"\section{\texorpdfstring{$\frac{1}{a}$}{1/a}}");

        private static readonly String NeedsBeamer =
            FakeLLMService.ValidTex(@"The answer is \uncover<2->{7}.");

        #region A whole document

        [TestMethod]
        public async Task TheRetryIsToldWhatWasWrong()
        {
            var llm = new FakeLLMService()
                .When(p => p.Contains(Marker, StringComparison.Ordinal), Valid);

            llm.DefaultResponse = NeedsHyperref;

            String? tex = await SourceGenerator.TryGetValidTex(llm, Prompt);

            Assert.AreEqual(Valid, tex);
            Assert.AreEqual(2, llm.CallCount);
            StringAssert.Contains(llm.PromptsSeen[1], @"it uses \texorpdfstring");
        }

        [TestMethod]
        public async Task TheFirstAttemptIsSentThePromptAsItStands()
        {
            var llm = new FakeLLMService { DefaultResponse = Valid };

            await SourceGenerator.TryGetValidTex(llm, Prompt);

            Assert.AreEqual(Prompt, llm.PromptsSeen.Single());
        }

        [TestMethod]
        public async Task TheRetryBeginsWithExactlyWhatTheFirstAttemptWasSent()
        {
            // the part the API can serve from its cache, so the note goes on the end
            var llm = new FakeLLMService()
                .When(p => p.Contains(Marker, StringComparison.Ordinal), Valid);

            llm.DefaultResponse = NeedsHyperref;

            await SourceGenerator.TryGetValidTex(llm, Prompt);

            StringAssert.StartsWith(llm.PromptsSeen[1], llm.PromptsSeen[0]);
        }

        [TestMethod]
        public async Task EveryDistinctReasonIsKeptSoTheFirstMistakeIsNotMadeAgain()
        {
            // the first attempt needs hyperref, the second a deck, the third is fine
            var llm = new FakeLLMService()
                .When(p => p.Contains(@"\uncover", StringComparison.Ordinal), Valid)
                .When(p => p.Contains(@"\texorpdfstring", StringComparison.Ordinal), NeedsBeamer);

            llm.DefaultResponse = NeedsHyperref;

            String? tex = await SourceGenerator.TryGetValidTex(llm, Prompt);

            Assert.AreEqual(Valid, tex);
            StringAssert.Contains(llm.PromptsSeen[2], @"\texorpdfstring");
            StringAssert.Contains(llm.PromptsSeen[2], @"\uncover");
        }

        [TestMethod]
        public async Task TheSameReasonTwiceIsSaidOnce()
        {
            var llm = new FakeLLMService { DefaultResponse = NeedsHyperref };

            Assert.IsNull(await SourceGenerator.TryGetValidTex(llm, Prompt));

            String third = llm.PromptsSeen[2];

            Assert.AreEqual(1, Occurrences(third, @"it uses \texorpdfstring"), third);
        }

        [TestMethod]
        public async Task WhatTheFixupMendsIsNotReportedAsAReason()
        {
            // a code fence is stripped for nothing, so it is never worth a second call
            var llm = new FakeLLMService { DefaultResponse = "```latex\n" + Valid + "\n```" };

            Assert.IsNotNull(await SourceGenerator.TryGetValidTex(llm, Prompt));
            Assert.AreEqual(1, llm.CallCount);
        }

        #endregion

        #region A translated body

        private const String English = """
            \documentclass{article}
            \begin{document}
            Write the fraction.
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

        [TestMethod]
        public async Task ATranslatedBodyRetryIsToldWhatWasWrongToo()
        {
            const String good = "\\begin{document}\n\\ealpara{Zapisz ulamek}{Write the \\ealkey{fraction}}\n\\end{document}";
            const String invented = "\\begin{document}\n\\ealpara{Zapisz}{Write the \\ealgl{fraction}{ulamek}}\n\\end{document}";

            var llm = new FakeLLMService()
                .When(p => p.Contains(Marker, StringComparison.Ordinal), good);

            llm.DefaultResponse = invented;

            String body = await SourceGenerator.GenerateTranslatedBody(
                English, Terms, TexFixtures.Polish, new L2ColourOptions(),
                SheetForm.ParallelText, SheetArchetypes.Worksheet, llm);

            Assert.AreEqual(good, body);
            StringAssert.Contains(llm.PromptsSeen[1], @"it uses \ealgl,");
            StringAssert.StartsWith(llm.PromptsSeen[1], llm.PromptsSeen[0]);
        }

        #endregion

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
