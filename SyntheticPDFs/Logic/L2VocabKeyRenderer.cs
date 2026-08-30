using SyntheticPDFs.Configuration;
using System.Text;

namespace SyntheticPDFs.Logic
{
    // Turns a list of terms into the key .tex.
    //
    // The model never writes this file. It picks the words and defines them; the layout,
    // the shuffle and the two-page repeat are done here, so every key in the repository
    // comes out identical in shape, the match-up is genuinely shuffled, and there is no
    // retry loop needed on a table the model may or may not close.
    internal static class L2VocabKeyRenderer
    {
        // A4, with the key on the first two pages and the match-up on the next two, so a
        // teacher printing two pages to a sheet gets two copies of each. The bodies are
        // written once and invoked twice rather than repeated verbatim.
        internal static String Render(
            IReadOnlyList<VocabTerm> terms,
            L2Macros.SourceMetadataTitle metadata,
            L2ColourOptions colours,
            LanguageProfile? language,
            String builtFrom,
            String? vocabularyKey)
        {
            String title = L2Macros.TitleFor(metadata, language);

            StringBuilder sb = new();

            sb.AppendLine(L2Macros.CompilerDirective);
            sb.AppendLine(L2Macros.ProvenanceBlock(title, colours, language, builtFrom, vocabularyKey));
            sb.AppendLine(L2VocabData.Block(terms));
            sb.AppendLine();

            sb.AppendLine(@"\documentclass[11pt]{article}");
            sb.AppendLine(@"\usepackage[a4paper,margin=18mm]{geometry}");
            sb.AppendLine(@"\usepackage{array}");

            if (language is not null)
            {
                sb.AppendLine(L2Macros.LanguagePreamble(language));
            }
            else
            {
                // an English key still uses \ealtext, so give it one that does nothing
                sb.AppendLine(@"\newcommand{\ealtext}[1]{#1}");
                sb.AppendLine(@"\newcommand{\ealtextblock}[1]{{\raggedright #1\par}}");
            }

            sb.AppendLine(L2Macros.Definitions(colours));
            sb.AppendLine();
            sb.AppendLine(@"\pagestyle{empty}");
            sb.AppendLine(@"\setlength{\parindent}{0pt}");
            sb.AppendLine();

            sb.AppendLine(KeyBody(terms, metadata, language));
            sb.AppendLine();
            sb.AppendLine(MatchBody(terms, metadata, language));
            sb.AppendLine();

            sb.AppendLine(@"\begin{document}");
            sb.AppendLine(@"\ealkeybody\newpage\ealkeybody\newpage");
            sb.AppendLine(@"\ealmatchbody\newpage\ealmatchbody");
            sb.AppendLine(@"\end{document}");

            return sb.ToString();
        }

        private static String KeyBody(
            IReadOnlyList<VocabTerm> terms,
            L2Macros.SourceMetadataTitle metadata,
            LanguageProfile? language)
        {
            String sheet = Escape(Readable(metadata.RootName));

            StringBuilder sb = new();

            sb.AppendLine(@"\newcommand{\ealkeybody}{%");
            sb.AppendLine($@"  {{\large\textbf{{{sheet}: key vocabulary}}}}\par\medskip");

            if (language is null)
            {
                sb.AppendLine(@"  \begin{tabular}{@{}p{40mm}p{112mm}@{}}");
                sb.AppendLine(@"    \textbf{Word} & \textbf{Meaning} \\[2pt]");
                sb.AppendLine(@"    \hline\\[-6pt]");

                foreach (VocabTerm term in terms)
                {
                    sb.AppendLine(
                        $@"    \ealkey{{{Escape(term.English)}}} & {Escape(term.Definition)} \\[4pt]");
                }
            }
            else
            {
                // three columns: the English word, the same word in the language, and
                // the meaning in that language
                sb.AppendLine(@"  \begin{tabular}{@{}p{32mm}p{34mm}p{86mm}@{}}");
                sb.AppendLine(
                    $@"    \textbf{{English}} & \textbf{{{language.TitleName}}} & \textbf{{Meaning}} \\[2pt]");
                sb.AppendLine(@"    \hline\\[-6pt]");

                foreach (VocabTerm term in terms)
                {
                    sb.AppendLine(
                        $@"    \ealkey{{{Escape(term.English)}}} & \ealkeytr{{{Escape(term.Translation)}}} "
                        + $@"& \ealtext{{{Escape(term.TranslatedDefinition)}}} \\[4pt]");
                }
            }

            sb.AppendLine(@"  \end{tabular}}");

            return sb.ToString();
        }

        // The English words stay in the order of the key and the right hand column is
        // shuffled. In a translated key the right hand column is the translated word and
        // its meaning together, since what is being tested there is the translation
        // rather than the meaning.
        private static String MatchBody(
            IReadOnlyList<VocabTerm> terms,
            L2Macros.SourceMetadataTitle metadata,
            LanguageProfile? language)
        {
            List<VocabTerm> shuffled = L2VocabData.Shuffled(terms, metadata.RootName);

            String instruction = language is null
                ? "Match the word to its meaning"
                : $"Match the English word to its {language.TitleName} meaning";

            StringBuilder sb = new();

            sb.AppendLine(@"\newcommand{\ealmatchbody}{%");
            sb.AppendLine($@"  {{\large\textbf{{{Escape(instruction)}}}}}\par\medskip");
            sb.AppendLine(@"  \begin{tabular}{@{}p{50mm}@{\hspace{16mm}}p{86mm}@{}}");

            for (int i = 0; i < terms.Count; i++)
            {
                String left = $@"\ealkey{{{Escape(terms[i].English)}}}";

                String right = language is null
                    ? Escape(shuffled[i].Definition)
                    : $@"\ealkeytr{{{Escape(shuffled[i].Translation)}}} "
                      + $@"\ealtext{{{Escape(shuffled[i].TranslatedDefinition)}}}";

                sb.AppendLine($@"    {left} & {right} \\[10pt]");
            }

            sb.AppendLine(@"  \end{tabular}}");

            return sb.ToString();
        }

        private static String Readable(String rootName)
        {
            String name = rootName.Split('/').Last();

            return name.Length == 0 ? name : Char.ToUpperInvariant(name[0]) + name[1..];
        }

        // definitions are prose from a model, so anything LaTeX would read as markup has
        // to be neutralised - a stray % comments out the rest of the line. the backslash
        // is left alone deliberately, since a definition may legitimately carry maths
        private static String Escape(String text)
        {
            StringBuilder sb = new(text.Length);

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
    }
}
