using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using SyntheticPDFs.Tests.Fakes;

namespace SyntheticPDFs.Tests
{
    // the naming and staleness layers understand translated source now, but nothing
    // generates it yet. these tests pin that boundary: if one starts failing, someone
    // has begun implementing generation and should say so.
    [TestClass]
    public class ForeignLanguageScaffoldTests
    {
        [TestMethod]
        public void TheEagerlyGeneratedLanguagesAreAllNameable()
        {
            // a language that cannot be named cannot have a filename built for it, so
            // this is the cheap check that the eager set is actually producible
            foreach (String code in new[] { "pol", "urd", "pan", "ben", "ara" })
            {
                Assert.IsTrue(
                    LanguageNames.IsKnown(code),
                    $"{code} is eagerly generated, so it must be in LanguageNames");
            }
        }

        [TestMethod]
        public async Task ALanguageWithNoConfiguredEntryIsRefusedClearly()
        {
            // it can be named, so a filename could be built for it, but nothing says
            // which font or script it needs - generating it would produce a file that
            // cannot compile, so refuse rather than write one
            var orchestrator = new Orchestrator(
                NullLogger<Orchestrator>.Instance,
                new FakeGitRepoManager(),
                new FakeLLMService(),
                Options.Create(new GenerationOptions()));

            var request = new Orchestrator.GenerationRequest
            {
                Target = new Orchestrator.SourceMetadata
                {
                    RootName = "latex/worksheets/sheet",
                    Type = SourceType.Root,
                    Archetype = SourceArchetype.Worksheet,
                    Language = new ISO639_3Code("pol"),
                    Rendition = SourceRendition.ParallelText,
                },
                Job = Orchestrator.GenerationJob.CreateSource,
            };

            var thrown = await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => orchestrator.GenerateSyntheticSource(request));

            StringAssert.Contains(thrown.Message, "pol", "the error must name the language");
        }

        [TestMethod]
        public void EnglishRootGenerationIsRejected()
        {
            // question sheets are written by a person; the pipeline only ever derives from them
            var orchestrator = new Orchestrator(
                NullLogger<Orchestrator>.Instance,
                new FakeGitRepoManager(),
                new FakeLLMService(),
                Options.Create(new GenerationOptions()));

            var request = new Orchestrator.GenerationRequest
            {
                Target = new Orchestrator.SourceMetadata
                {
                    RootName = "latex/worksheets/sheet",
                    Type = SourceType.Root,
                    Archetype = SourceArchetype.Worksheet,
                    Language = ISO639_3Code.eng,
                },
                Job = Orchestrator.GenerationJob.CreateSource,
            };

            Assert.ThrowsExceptionAsync<ArgumentException>(
                () => orchestrator.GenerateSyntheticSource(request)).Wait();
        }
    }
}
