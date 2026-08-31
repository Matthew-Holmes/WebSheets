using SyntheticPDFs.Rendering;

namespace SyntheticPDFs.Models.Content
{
    // The shared dictionary and its translations, held apart from the sheets because
    // almost nothing that is true of a sheet is true of it.
    //
    // The English one is written by a person and read by the pipeline. Each translated
    // one is written by the pipeline and read by it again on the next pass: it is a
    // cache, and the reason a word is only ever translated once however many sheets go
    // on to use it.
    //
    // The definitions are parsed and kept here rather than left as source. There is one
    // dictionary per language and they are read on every pass, so the cost is bounded by
    // the number of languages rather than by the size of the repository - which is the
    // one place in this model where holding parsed content in memory pays for itself.
    internal sealed record DictionaryState
    {
        // "latex/dictionary/mathematicalDictionary" - the English file, without extension
        internal required String RootName { get; init; }

        internal ContentFile? English { get; init; }

        internal required IReadOnlyDictionary<ISO639_3Code, ContentFile> Translations { get; init; }

        // A repository need not have one. Without it the model's own wording stands, so
        // this is a fact about the repository rather than a fault.
        internal bool Exists => English is not null;

        #region What is actually in it

        // empty until the orchestrator has read the files - see LoadDictionaries
        internal MathsDictionary Definitions { get; init; } = MathsDictionary.Empty;

        internal IReadOnlyDictionary<ISO639_3Code, L2Dictionary> Translated { get; init; }
            = new Dictionary<ISO639_3Code, L2Dictionary>();

        internal L2Dictionary? In(ISO639_3Code language) =>
            Translated.TryGetValue(language, out L2Dictionary? dictionary) ? dictionary : null;

        // The translation to use for a word as a sheet spells it, or null if there is
        // nothing current to use.
        //
        // Both halves of the lookup live here because both are needed and neither is
        // enough. The shared dictionary knows that "numerators" is filed under
        // "numerator"; the translated one knows what "numerator" is in Polish and which
        // English wording that translation was made from. Asking either on its own would
        // miss every word a sheet happens to use in the plural.
        internal L2DictionaryEntry? Lookup(
            ISO639_3Code language, String word, String englishDefinition)
        {
            String? headword = Definitions.HeadwordFor(word);

            return headword is null ? null : In(language)?.Current(headword, englishDefinition);
        }

        // Languages whose file was built from settings that have since changed - a colour,
        // or the layout version. Re-rendering one of these costs a commit and nothing
        // else, since every translation in it is kept.
        internal IReadOnlySet<ISO639_3Code> BuiltFromOldSettings { get; init; }
            = new HashSet<ISO639_3Code>();

        #endregion

        internal static DictionaryState Empty(String rootName) => new()
        {
            RootName     = rootName,
            Translations = new Dictionary<ISO639_3Code, ContentFile>(),
        };
    }
}
