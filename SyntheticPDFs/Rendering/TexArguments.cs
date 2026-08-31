namespace SyntheticPDFs.Rendering
{
    // Reading the arguments of a macro out of a .tex file.
    //
    // Both dictionaries are parsed rather than merely written, because both are meant to
    // be edited by hand: the shared one is where a wording that reads badly to a teacher
    // gets changed, and a translated one is where a teacher who speaks the language
    // corrects a translation. That means the reader has to cope with what a person's
    // editor does to a file - reflowed lines, a comment added, braces inside a
    // definition - rather than with only what the generator wrote.
    internal static class TexArguments
    {
        // Reads one balanced {...} group, skipping any whitespace in front of it, and
        // leaves `at` after the closing brace. Returns null when the next thing is not a
        // group, which is how a caller finds out a macro is missing an argument.
        internal static String? ReadGroup(String source, ref int at)
        {
            at = SkipSpace(source, at);

            if (at >= source.Length || source[at] != '{') { return null; }

            int depth = 0;
            int start = at + 1;

            for (int i = at; i < source.Length; i++)
            {
                char c = source[i];

                // an escaped brace is a character, not a delimiter
                if (c == '\\') { i++; continue; }

                if (c == '{') { depth++; }
                else if (c == '}')
                {
                    depth--;

                    if (depth == 0)
                    {
                        String value = source[start..i];
                        at = i + 1;
                        return value;
                    }
                }
            }

            return null;
        }

        // Whether what follows is actually an argument. A .tex file defines its own macros
        // before it uses them, so the name of a macro occurs once with nothing after it -
        // inside \newcommand{\dictentry}[2]{...} - and that occurrence has to be told
        // apart from a real entry rather than reported as a broken one.
        internal static bool OpensAGroup(String source, int at)
        {
            at = SkipSpace(source, at);

            return at < source.Length && (source[at] == '{' || source[at] == '[');
        }

        internal static int SkipSpace(String source, int at)
        {
            while (at < source.Length && Char.IsWhiteSpace(source[at])) { at++; }

            return at;
        }

        // Prose from a model, made safe to put in a .tex file. A stray % comments out the
        // rest of the line and an & looks like a table cell, so the six characters LaTeX
        // reads as markup are escaped.
        //
        // The backslash is deliberately left alone: a definition may legitimately carry
        // maths, and escaping it would turn \frac into the literal word.
        internal static String Escape(String text)
        {
            System.Text.StringBuilder sb = new(text.Length);

            foreach (char c in text)
            {
                switch (c)
                {
                    case '&': sb.Append(@"\&"); break;
                    case '#': sb.Append(@"\#"); break;
                    case '%': sb.Append(@"\%"); break;
                    case '_': sb.Append(@"\_"); break;
                    case '~': sb.Append(@"\textasciitilde{}"); break;
                    case '^': sb.Append(@"\textasciicircum{}"); break;
                    default: sb.Append(c); break;
                }
            }

            return sb.ToString();
        }

        // The exact inverse, so that text written into a dictionary file and read back on
        // the next pass is the text that went in. Without it a definition would pick up
        // another backslash every time it was rewritten.
        internal static String Unescape(String text)
        {
            return text
                .Replace(@"\textasciitilde{}", "~")
                .Replace(@"\textasciicircum{}", "^")
                .Replace(@"\&", "&")
                .Replace(@"\#", "#")
                .Replace(@"\%", "%")
                .Replace(@"\_", "_");
        }

        // A commented out entry is not an entry. This matters most for the line-ending
        // comment the generator writes after each entry, which would otherwise be read as
        // part of the following one.
        internal static String StripComments(String source)
        {
            return String.Join('\n', source
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(line =>
                {
                    int at = line.IndexOf('%');

                    while (at > 0 && line[at - 1] == '\\')
                    {
                        at = line.IndexOf('%', at + 1);
                    }

                    return at < 0 ? line : line[..at];
                }));
        }
    }
}
