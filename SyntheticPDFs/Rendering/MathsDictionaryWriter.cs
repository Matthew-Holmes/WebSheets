using System.Text;

namespace SyntheticPDFs.Rendering
{
    // Adds words to the shared dictionary without disturbing what is already in it.
    //
    // The file is organised by hand - entries grouped by topic, each group alphabetical -
    // and that organisation is for whoever reads it rather than for the parser, which
    // does not care where an entry sits. So new words are appended in a section of their
    // own, marked as not yet read by anybody, and whoever tidies the file moves them up
    // into the topic they belong to.
    //
    // Nothing already in the file is rewritten, reordered or reformatted. This is the
    // one file in the repository a person owns outright, and a generator that reflowed
    // it would be a generator nobody trusted to touch it.
    internal static class MathsDictionaryWriter
    {
        // how the section is recognised on the next pass, so its header is written once
        internal const String SectionMarker =
            "% ---- Words met on a worksheet, not yet checked by anybody ----";

        private const String EndOfDocument = @"\end{document}";

        // how the shipped dictionary heads each of its topics
        private const String TopicMacro = @"\dicttopic";

        // Returns the file with these entries added, or unchanged when there are none.
        // Each is a headword and the definition the vocabulary key that met it was
        // written with.
        internal static String Add(
            String texSource, IReadOnlyList<KeyValuePair<String, String>> entries)
        {
            if (entries.Count == 0) { return texSource; }

            String source = texSource.Replace("\r\n", "\n");

            StringBuilder addition = new();

            if (!source.Contains(SectionMarker, StringComparison.Ordinal))
            {
                addition.Append(Header(source)).Append('\n');
            }

            foreach (var entry in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                addition.Append(Entry(entry.Key, entry.Value)).Append('\n');
            }

            return Insert(source, addition.ToString());
        }

        // The words are prose from a model, so anything LaTeX would read as markup is
        // neutralised on the way in and read back out by MathsDictionary.Parse. Without
        // it a definition mentioning a percentage would comment out the rest of its own
        // line, and take the entry with it.
        private static String Entry(String headword, String definition) =>
            $@"\dictentry{{{TexArguments.Escape(headword)}}}"
            + $@"{{{TexArguments.Escape(definition)}}}";

        // Plain words, since the person this is addressed to is a teacher opening a
        // LaTeX file to fix a wording rather than anybody who knows what the generator is.
        //
        // It gets a heading in the compiled dictionary as well as in the source, so the
        // words nobody has checked are visible to somebody reading the pdf rather than
        // only to somebody reading the LaTeX - but only where the file has a heading
        // macro to use, since inventing one it has not defined would stop it compiling.
        private static String Header(String source) =>
            String.Join('\n',
                "",
                source.Contains(TopicMacro, StringComparison.Ordinal)
                    ? TopicMacro + "{Words not yet checked}\n"
                    : "",
                SectionMarker,
                "%",
                "% The generator adds a word here the first time one of its vocabulary",
                "% keys uses a word this file does not define, along with the definition",
                "% that key was written with. Nobody has read these yet, so they are the",
                "% entries most worth checking.",
                "%",
                "% Rewording one here changes it everywhere: every vocabulary key that",
                "% uses that word is rebuilt to match, in English and in every language",
                "% the repository is translated into. So this is the place to fix an",
                "% explanation that reads badly, rather than the sheet it appeared on.",
                "%",
                "% Move an entry up into the topic it belongs to whenever you like. The",
                "% generator does not mind where in the file an entry lives - it reads",
                "% the word and the definition and nothing else.",
                "% -------------------------------------------------------------");

        // Straight after the last entry in the file, so that a new word is typeset by
        // whatever the file puts its entries inside. The shipped dictionary sets them in
        // three columns, and words dropped in at the end - after \end{multicols} but
        // before \end{document} - would come out across the whole page instead.
        private static String Insert(String source, String addition)
        {
            int at = AfterTheLastEntry(source);

            if (at >= 0) { return source[..at] + addition + source[at..]; }

            int end = source.LastIndexOf(EndOfDocument, StringComparison.Ordinal);

            // a dictionary with no entries and no \end{document} will not compile, but
            // it can still be read and added to, and losing the words as well would
            // help nobody
            if (end < 0) { return source.TrimEnd('\n') + "\n\n" + addition; }

            return source[..end] + addition + "\n" + source[end..];
        }

        // The end of the line the last entry finishes on. Counted rather than searched
        // for, so that an entry whose definition runs over two lines is not cut in half.
        private static int AfterTheLastEntry(String source)
        {
            int at = source.LastIndexOf(MathsDictionary.EntryMacro, StringComparison.Ordinal);

            if (at < 0) { return -1; }

            int depth = 0;

            for (int i = at; i < source.Length; i++)
            {
                switch (source[i])
                {
                    case '{': depth++; break;
                    case '}': depth--; break;
                    case '\n' when depth <= 0: return i + 1;
                }
            }

            return source.Length;
        }
    }
}
