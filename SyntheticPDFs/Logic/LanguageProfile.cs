using SyntheticPDFs.Configuration;

namespace SyntheticPDFs.Logic
{
    // everything needed to name a translated file and to typeset it: the code and
    // English name from LanguageNames, the font and direction from configuration.
    // a language missing either half cannot be generated
    internal record LanguageProfile
    {
        internal required ISO639_3Code Code { get; init; }

        // lower case, since it is joined on to a camelCase filename
        internal required String EnglishName { get; init; }

        internal required String Font { get; init; }

        internal required String BabelName { get; init; }

        internal required bool RightToLeft { get; init; }

        // what the provenance block says about direction
        internal String DirectionDescription => RightToLeft ? "right to left" : "left to right";

        // capitalised for a title - "Polish Parallel Text Version of ..."
        internal String TitleName =>
            EnglishName.Length == 0
                ? EnglishName
                : Char.ToUpperInvariant(EnglishName[0]) + EnglishName[1..];
    }

    // resolves configured languages into profiles, complaining once about any that
    // cannot be used rather than failing a pass later on
    internal class LanguageTable
    {
        private readonly Dictionary<ISO639_3Code, LanguageProfile> _profiles = new();

        internal LanguageTable(L2Options options, ILogger? logger = null)
        {
            foreach (var kvp in options.Languages)
            {
                String code = kvp.Key;

                String? englishName = LanguageNames.EnglishNameOf(code);

                if (englishName is null)
                {
                    logger?.LogWarning(
                        "{Section}:Languages has '{Code}', which is not a code LanguageNames can "
                        + "name - it cannot be given a filename, so it is being ignored.",
                        L2Options.SectionName, code);

                    continue;
                }

                if (String.IsNullOrWhiteSpace(kvp.Value.Font)
                    || String.IsNullOrWhiteSpace(kvp.Value.BabelName))
                {
                    logger?.LogWarning(
                        "{Section}:Languages:{Code} needs both a Font and a BabelName to be "
                        + "typeset - it is being ignored.",
                        L2Options.SectionName, code);

                    continue;
                }

                _profiles[new ISO639_3Code(code)] = new LanguageProfile
                {
                    Code        = new ISO639_3Code(code),
                    EnglishName = englishName,
                    Font        = kvp.Value.Font,
                    BabelName   = kvp.Value.BabelName,
                    RightToLeft = kvp.Value.RightToLeft,
                };
            }

            EagerLanguages = options.EagerLanguages
                .Select(c => new ISO639_3Code(c))
                .Where(c =>
                {
                    if (_profiles.ContainsKey(c)) { return true; }

                    logger?.LogWarning(
                        "{Section}:EagerLanguages names '{Code}', which has no usable entry in "
                        + "{Section}:Languages - nothing will be generated for it.",
                        L2Options.SectionName, c.Code, L2Options.SectionName);

                    return false;
                })
                .ToList();
        }

        // in configured order, so the eager set is generated predictably
        internal IReadOnlyList<ISO639_3Code> EagerLanguages { get; }

        internal LanguageProfile? Get(ISO639_3Code code) =>
            _profiles.TryGetValue(code, out LanguageProfile? profile) ? profile : null;

        internal bool CanGenerate(ISO639_3Code code) => _profiles.ContainsKey(code);

        internal IReadOnlyCollection<ISO639_3Code> All => _profiles.Keys;
    }
}
