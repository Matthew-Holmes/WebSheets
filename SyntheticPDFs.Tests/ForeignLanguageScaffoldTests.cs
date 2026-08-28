using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using SyntheticPDFs.Tests.Fakes;

namespace SyntheticPDFs.Tests
{
    // the language axis is deliberately scaffolded but inert. these tests pin that:
    // if one starts failing, someone has begun implementing it and should say so.
    [TestClass]
    public class ForeignLanguageScaffoldTests
    {
        [TestMethod]
        public void OnlyEnglishIsDefined()
        {
            CollectionAssert.AreEqual(
                new[] { ISO639_3Code.eng },
                Enum.GetValues<ISO639_3Code>(),
                "adding a language means the foreign paths below need implementing first");
        }

        [TestMethod]
        public async Task GeneratingForeignSourceIsNotImplemented()
        {
            var orchestrator = new Orchestrator(
                NullLogger<Orchestrator>.Instance,
                new FakeGitRepoManager(),
                new FakeLLMService(),
                Options.Create(new GenerationOptions()));

            var metadata = new Orchestrator.SourceMetadata
            {
                RootName = "latex/worksheets/sheet",
                Type = SourceType.Root,
                Archetype = SourceArchetype.Worksheet,
                // cast past the enum, since only eng exists to name
                Language = (ISO639_3Code)999,
            };

            await Assert.ThrowsExceptionAsync<NotImplementedException>(
                () => orchestrator.GenerateSyntheticSource(metadata));
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

            var metadata = new Orchestrator.SourceMetadata
            {
                RootName = "latex/worksheets/sheet",
                Type = SourceType.Root,
                Archetype = SourceArchetype.Worksheet,
                Language = ISO639_3Code.eng,
            };

            Assert.ThrowsExceptionAsync<ArgumentException>(
                () => orchestrator.GenerateSyntheticSource(metadata)).Wait();
        }
    }
}
