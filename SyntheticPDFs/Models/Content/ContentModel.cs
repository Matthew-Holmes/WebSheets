namespace SyntheticPDFs.Models.Content
{
    // What the repository holds, in the pipeline's own terms.
    //
    // RepoModel knows only what git knows: a list of paths and how many commits ago each
    // was last touched. This layer wraps that and attaches the semantics unpicked from
    // the names - which sheet a file belongs to, which part of it it is, which language,
    // and what kind of thing the sheet is. Nothing here reads a file; building the whole
    // model is string work over a list of paths.
    //
    // The split is deliberate. RepoModel exists to abstract git and is left alone; the
    // orchestrator then reasons about sheets rather than about paths, and never has to
    // take a filename apart to decide anything.
    internal sealed record ContentModel
    {
        internal required String LastCommitHash { get; init; }

        // by root name - "latex/worksheets/algebra/quadratics"
        internal required IReadOnlyDictionary<String, SheetState> Sheets { get; init; }

        // by root name as well, though a repository normally has exactly one
        internal required IReadOnlyDictionary<String, DictionaryState> Dictionaries { get; init; }

        internal IEnumerable<ContentFile> AllFiles =>
            Sheets.Values.SelectMany(s => s.Files.Values);

        // The dictionary at a configured path, or an empty state standing in for it, so a
        // caller never has to decide what to do about a repository that has not got one.
        internal DictionaryState DictionaryAt(String path)
        {
            String rootName = WithoutExtension(path);

            return Dictionaries.TryGetValue(rootName, out DictionaryState? state)
                ? state
                : DictionaryState.Empty(rootName);
        }

        #region Building it

        // a repository with nothing in it, which is what an empty clone reads as
        internal static ContentModel Empty { get; } = new()
        {
            LastCommitHash = String.Empty,
            Sheets         = new Dictionary<String, SheetState>(),
            Dictionaries   = new Dictionary<String, DictionaryState>(),
        };

        internal static ContentModel From(
            RepoModel repo, ILogger? logger = null, String extension = "tex")
        {
            String suffix = '.' + extension;

            Dictionary<String, List<ContentFile>> byRoot = new(StringComparer.Ordinal);

            foreach (TrackedFile tracked in repo.Contents)
            {
                if (!tracked.FullPath.EndsWith(suffix, StringComparison.Ordinal)) { continue; }

                String pathNoExt = tracked.FullPath[..^suffix.Length];

                SourceMetadata metadata = SheetArchetypes.Parse(pathNoExt, logger);

                ContentFile file = new()
                {
                    TrackedFile    = tracked,
                    SourceMetadata = metadata,
                };

                if (!byRoot.TryGetValue(metadata.RootName, out List<ContentFile>? files))
                {
                    files = new List<ContentFile>();
                    byRoot[metadata.RootName] = files;
                }

                files.Add(file);
            }

            Dictionary<String, SheetState> sheets = new(StringComparer.Ordinal);
            Dictionary<String, DictionaryState> dictionaries = new(StringComparer.Ordinal);

            foreach (var (rootName, files) in byRoot)
            {
                SheetArchetype archetype = ArchetypeOf(rootName, files, logger);

                if (archetype == SheetArchetypes.SharedDictionary)
                {
                    dictionaries[rootName] = DictionaryFrom(rootName, files, logger);
                    continue;
                }

                sheets[rootName] = new SheetState
                {
                    RootName  = rootName,
                    Archetype = archetype,
                    Files     = Keyed(rootName, files, logger),
                };
            }

            return new ContentModel
            {
                LastCommitHash = repo.LastCommitHash,
                Sheets         = sheets,
                Dictionaries   = dictionaries,
            };
        }

        // Two files claiming the same key would mean one of them is silently dropped, so
        // whichever is younger wins and the collision is said out loud. It takes a rename
        // to produce one - two spellings of a language name, say - and it would otherwise
        // show up as a file that is regenerated on every pass.
        private static IReadOnlyDictionary<ContentKey, ContentFile> Keyed(
            String rootName, List<ContentFile> files, ILogger? logger)
        {
            Dictionary<ContentKey, ContentFile> keyed = new();

            foreach (ContentFile file in files)
            {
                if (keyed.TryGetValue(file.Key, out ContentFile? existing))
                {
                    logger?.LogWarning(
                        "'{First}' and '{Second}' are both the same file of {Root} - keeping the "
                        + "younger. One of them wants renaming.",
                        existing.FullPath, file.FullPath, rootName);

                    if (existing.AgeCommits <= file.AgeCommits) { continue; }
                }

                keyed[file.Key] = file;
            }

            return keyed;
        }

        private static DictionaryState DictionaryFrom(
            String rootName, List<ContentFile> files, ILogger? logger)
        {
            Dictionary<ISO639_3Code, ContentFile> translations = new();

            ContentFile? english = null;

            foreach (ContentFile file in files)
            {
                if (file.SourceMetadata.Language == ISO639_3Code.eng)
                {
                    english = file;
                    continue;
                }

                translations[file.SourceMetadata.Language] = file;
            }

            return new DictionaryState
            {
                RootName     = rootName,
                English      = english,
                Translations = translations,
            };
        }

        // The archetype comes from the folder, so every file sharing a root name should
        // agree about it. One that does not is a naming accident rather than a reason to
        // stop, so take the root file's answer and say so.
        private static SheetArchetype ArchetypeOf(
            String rootName, List<ContentFile> files, ILogger? logger)
        {
            var archetypes = files.Select(f => f.SourceMetadata.Archetype).ToHashSet();

            if (archetypes.Count == 1) { return archetypes.First(); }

            SheetArchetype chosen = files
                .Where(f => f.SourceMetadata.Part == SheetPart.Root
                         && f.SourceMetadata.Form == SheetForm.Original)
                .Select(f => f.SourceMetadata.Archetype)
                .DefaultIfEmpty(archetypes.First())
                .First();

            logger?.LogWarning(
                "files under '{Root}' disagree about their archetype ({Seen}) - treating them "
                + "all as {Chosen}. Has one of them been moved to a different folder?",
                rootName, String.Join(", ", archetypes.Select(a => a.Name)), chosen.Name);

            return chosen;
        }

        private static String WithoutExtension(String path)
        {
            int dot = path.LastIndexOf('.');

            return dot < 0 ? path : path[..dot];
        }

        #endregion

        #region Judging it

        // The plans applied, so that every sheet knows what it is missing and what has
        // gone stale. Separate from building because this is the half that needs files
        // opening - `outdated` is decided by reading what a file records about itself.
        internal ContentModel Judged(
            LanguageTable languages,
            bool includeGlossaries,
            Func<SheetState, IReadOnlySet<ContentKey>>? outdated = null)
        {
            Dictionary<String, SheetState> judged = new(StringComparer.Ordinal);

            foreach (var (rootName, sheet) in Sheets)
            {
                judged[rootName] = sheet.Judged(
                    languages, includeGlossaries, outdated?.Invoke(sheet));
            }

            return this with { Sheets = judged };
        }

        #endregion
    }
}
