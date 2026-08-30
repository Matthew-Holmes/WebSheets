using SyntheticPDFs.Models;
using static SyntheticPDFs.Logic.Orchestrator;

namespace SyntheticPDFs.Logic
{
    using RootName = String;

    using VariantInfo = HashSet<TrackedFileWithMetadata>;

    public partial class Orchestrator
    {
        // One state per root, holding every language and rendition of it together.
        //
        // This used to stratify by language before judging anything, which worked while
        // the only language was English. It cannot now: a translated key is derived from
        // the English vocabulary key and a parallel text from its English counterpart,
        // so splitting by language first would hide the very edges that decide whether
        // a translated file is still current.
        private Dictionary<RootName, RootPlanState> GetPlanStates(RepoModel repoModel, String ext)
        {
            Dictionary<RootName, VariantInfo> variantInfo = GetVariantInfo(repoModel, ext);

            MathsDictionary dictionary = LoadDictionary();

            Dictionary<RootName, RootPlanState> states = new();

            foreach (var kvp in variantInfo)
            {
                SourceArchetype archetype = ArchetypeOf(kvp.Key, kvp.Value);

                states[kvp.Key] = BuildPlanState(
                    kvp.Key,
                    archetype,
                    kvp.Value,
                    SourcePlan.For(archetype, Languages, L2Settings.GenerateVocabularyKeys),
                    Outdated(kvp.Value, dictionary));
            }

            return states;
        }

        // Files that exist and are correctly ordered but were built from something that
        // has since changed. Age cannot show this: rewording a definition does not touch
        // the sheet, and changing a colour touches nothing in the repository at all.
        //
        // Both are decided by reading what the file itself records, so an edit rebuilds
        // exactly what it affects - a dictionary change leaves alone every key whose
        // words it did not touch, however large the change was.
        private IReadOnlySet<SourceKey> Outdated(VariantInfo files, MathsDictionary dictionary)
        {
            HashSet<SourceKey> outdated = new();

            foreach (TrackedFileWithMetadata file in files)
            {
                SourceRendition rendition = file.SourceMetadata.Rendition;

                if (rendition == SourceRendition.Original) { continue; }

                String contents;

                try
                {
                    contents = RepoManager.GetContent(file.TrackedFile.FullPath).TexSource;
                }
                catch (Exception e)
                {
                    // one unreadable file must not stop the pass. leaving it alone is the
                    // cheap answer: rebuilding it would cost an API call on a guess
                    _logger.LogWarning(
                        "could not read {File} to check what it was built from: {Message}",
                        file.TrackedFile.FullPath, e.Message);

                    continue;
                }

                if (rendition == SourceRendition.VocabKey
                    && !L2VocabData.MatchesDictionary(contents, dictionary))
                {
                    _logger.LogInformation(
                        "{File} no longer agrees with the shared dictionary", file.TrackedFile.FullPath);

                    outdated.Add(KeyOf(file.SourceMetadata));
                    continue;
                }

                if (!L2Macros.MatchesSettings(contents, L2Settings.Colours))
                {
                    _logger.LogInformation(
                        "{File} was built from different settings", file.TrackedFile.FullPath);

                    outdated.Add(KeyOf(file.SourceMetadata));
                }
            }

            return outdated;
        }

        // Read fresh each pass, since it lives in the content repository and may have
        // been changed by anyone. A repository without one is not an error - it simply
        // has no shared definitions yet, and the model's own wording stands.
        private MathsDictionary LoadDictionary()
        {
            String path = ContentRepository.DictionaryPath;

            if (String.IsNullOrWhiteSpace(path)) { return MathsDictionary.Empty; }

            try
            {
                MathsDictionary dictionary = MathsDictionary.Parse(
                    RepoManager.GetContent(path).TexSource, _logger);

                _logger.LogInformation(
                    "read {Count} shared definition(s) from {Path}", dictionary.Count, path);

                return dictionary;
            }
            catch (Exception e)
            {
                _logger.LogWarning(
                    "no shared dictionary at {Path} ({Message}) - the definitions the model "
                    + "writes will stand as they are", path, e.Message);

                return MathsDictionary.Empty;
            }
        }

        // the archetype comes from the folder, so every file sharing a root name should
        // agree about it. one that does not is a naming accident rather than a reason to
        // stop, so take the root file's answer and say so
        private SourceArchetype ArchetypeOf(RootName root, VariantInfo files)
        {
            var archetypes = files.Select(f => f.SourceMetadata.Archetype).ToHashSet();

            if (archetypes.Count == 1) { return archetypes.First(); }

            SourceArchetype chosen = files
                .Where(f => f.SourceMetadata.Type == SourceType.Root
                         && f.SourceMetadata.Rendition == SourceRendition.Original)
                .Select(f => f.SourceMetadata.Archetype)
                .DefaultIfEmpty(archetypes.First())
                .First();

            _logger.LogWarning(
                "files under '{Root}' disagree about their archetype ({Seen}) - treating them "
                + "all as {Chosen}. Has one of them been moved to a different folder?",
                root, String.Join(", ", archetypes), chosen);

            return chosen;
        }
    }
}
