namespace SyntheticPDFs.Models.Content
{
    // Which part of a sheet a file is: the questions, the workings derived from them, or
    // the answers derived from those.
    internal enum SheetPart
    {
        Root,
        WorkedSolutions,
        Solutions,
    }

    // Which version of that part a file is. The part says whether it is the questions,
    // the workings or the answers; the form says whether it is the English original, the
    // glossary derived from it, or one of the translated versions.
    internal enum SheetForm
    {
        Original,           // English only - the file as written or derived
        Glossary,           // English only, Root - the tier 3 key for the whole sheet
        TranslatedGlossary, // translated only, Root - the translation of that key
        ParallelText,       // translated only - the whole text above the English
        Tier3Only,          // translated only - only the tier 3 words glossed

        // English only - a variant: the same file with something about it changed for a
        // school that wants it differently. Named on the archetype rather than assumed
        // to apply everywhere, so a new one is a form here and a line there.
        RetrieveAndConnect, // starters titled the way some schools insist on
    }

    // Identifies one file belonging to a root, without naming it. The three axes are
    // independent: "the Polish parallel text of the worked solutions" is a language, a
    // part and a form, and none of them implies the others.
    internal readonly record struct ContentKey(
        ISO639_3Code Language,
        SheetPart Part,
        SheetForm Form);
}
