using SyntheticPDFs.Configuration;
using SyntheticPDFs.Models.Content;
using System.Text;

namespace SyntheticPDFs.Rendering
{
    // Turns a list of terms into the key .tex.
    //
    // The model never writes this file. It picks the words and defines them; the order,
    // the layout, the shuffle and the answers are all done here, so every key in the
    // repository comes out identical in shape, the match-up is genuinely shuffled, and
    // there is no retry loop needed on a table the model may or may not close.
    internal static class L2VocabKeyRenderer
    {
        // A4. Three pages: the key, the match-up, and the answers to the match-up. Each
        // is repeated down its page as many times as it fits, with a cut line between,
        // so a key of five terms is printed four to a sheet rather than taking one.
        //
        // The answers are last so that a teacher can print the first two pages and keep
        // the third.
        internal static String Render(
            IReadOnlyList<VocabTerm> terms,
            L2Macros.SourceMetadataTitle metadata,
            L2ColourOptions colours,
            LanguageProfile? language,
            String builtFrom,
            String? vocabularyKey,
            String? fallbackFont = null)
        {
            String title = L2Macros.TitleFor(metadata, language);

            // a key is looked up by its English word, so that is the order it is in
            List<VocabTerm> ordered = L2VocabData.Alphabetical(terms);

            StringBuilder sb = new();

            sb.AppendLine(L2Macros.CompilerDirective);
            sb.AppendLine(L2Macros.ProvenanceBlock(
                title, colours, language, builtFrom, vocabularyKey,
                isKey: true, fallbackFont: fallbackFont));
            sb.AppendLine(L2VocabData.Block(ordered));
            sb.AppendLine();

            sb.AppendLine(@"\documentclass[11pt]{article}");
            sb.AppendLine(@"\usepackage[a4paper,margin=18mm]{geometry}");

            if (language is not null)
            {
                sb.AppendLine(L2Macros.LanguagePreamble(language, fallbackFont));
            }
            else
            {
                // an English key still uses \ealtext, so give it one that does nothing
                sb.AppendLine(@"\newcommand{\ealtext}[1]{#1}");
                sb.AppendLine(@"\newcommand{\ealtextblock}[1]{{\raggedright #1\par}}");
            }

            sb.AppendLine(L2Macros.Definitions(colours));
            sb.AppendLine();
            sb.AppendLine(KeyLayout(language?.RightToLeft ?? false));
            sb.AppendLine();
            sb.AppendLine(@"\pagestyle{empty}");
            sb.AppendLine(@"\setlength{\parindent}{0pt}");
            sb.AppendLine();

            sb.AppendLine(KeyBody(ordered, metadata, language));
            sb.AppendLine();
            sb.AppendLine(MatchBodies(ordered, metadata, language));
            sb.AppendLine();

            sb.AppendLine(@"\begin{document}");
            sb.AppendLine(@"\ealpage{\ealkeybody}");
            sb.AppendLine(@"\ealpage{\ealmatchbody}");
            sb.AppendLine(@"\ealpage{\ealanswerbody}");
            sb.AppendLine(@"\end{document}");

            return sb.ToString();
        }

        #region The layout

        // Everything the key needs and nothing any other L2 file does. Kept here rather
        // than in L2Macros because a parallel text sheet has no use for it, and pasting
        // it into every generated file would make restyling the key rebuild all of them.
        private static String KeyLayout(bool rightToLeft)
        {
            return String.Join('\n',
                "% ================================================================",
                "% Vocabulary key layout",
                "% ================================================================",
                @"\usepackage{tikz}",
                @"\usetikzlibrary{matrix}",
                "",
                @"\colorlet{ealrulecolour}{ealkeycolour!35!white}",
                "",
                @"\newlength{\ealwordcol}",
                @"\setlength{\ealwordcol}{40mm}",
                @"\newlength{\ealmeaningcol}",
                @"\setlength{\ealmeaningcol}{\dimexpr\textwidth-\ealwordcol-3mm\relax}",
                "",
                "% the gap between a word and the dots that lead away from it, and the",
                "% widest a word can be and still leave that gap",
                @"\newlength{\ealwordgap}",
                @"\setlength{\ealwordgap}{2mm}",
                @"\newlength{\ealwordfit}",
                @"\setlength{\ealwordfit}{\dimexpr\ealwordcol-\ealwordgap\relax}",
                @"\newsavebox{\ealwordbox}",
                "",
                Entry(rightToLeft),
                "",
                "% The translated word and then its meaning, all inside one \\ealtext",
                "% rather than two. A right to left language then orders the word, the",
                "% dash and the meaning itself; set as two runs, the word lands at the",
                "% far end of the line and is read last.",
                "%",
                "% The tie keeps the dash with the word, so a meaning that wraps breaks",
                "% after the dash rather than opening its second line with one.",
                @"\newcommand{\ealtrmeaning}[2]{%",
                @"  \ealtext{{\bfseries\color{ealkeycolour}#1}~\textemdash{} #2}}",
                "",
                "% how a heading and the match-up grid sit on the page",
                @"\newcommand{\ealstart}{" + (rightToLeft ? @"\hspace*{\fill}" : "") + "}",
                "",
                PackingMacros(),
                "",
                MatchGrid(rightToLeft));
        }

        // One entry: the word, a dotted leader across to the meaning, and the meaning
        // in a box of its own so that a wrapped line lines up under the first rather
        // than under the word.
        //
        // A right to left language gets the mirror image of it - the word at the right
        // margin, the leader running leftwards, the meaning flush right so that it
        // starts where the leader ends. Laid out the other way round the leader points
        // into a gap, because the meaning is set flush right whatever else happens.
        //
        // The word is measured first, because a \makebox does not clip what it cannot
        // fit: a term as long as "highest common factor" simply ran on out of its
        // column and into the meaning beside it. One that does not fit is set over as
        // many lines as it needs instead, and gets no leader - the dots are there to
        // carry the eye across a gap, and a word filling its column has not left one.
        //
        // The \strut is not decoration. A translated meaning opens with a language
        // switch and a colour, both of which put a whatsit into the box before its
        // first line - and a \parbox[t] whose first item is not a box takes its height
        // as zero, which drops the whole meaning half a line below the word it belongs
        // to. Starting the paragraph in horizontal mode puts them where they belong.
        private static String Entry(bool rightToLeft) =>
            String.Join('\n',
                WordLines(),
                "",
                rightToLeft
                    ? String.Join('\n',
                        @"\newcommand{\ealentry}[2]{%",
                        @"  \par\addvspace{1.6mm}%",
                        @"  \noindent",
                        @"  \parbox[t]{\ealmeaningcol}{\raggedleft\strut #2}%",
                        @"  \hspace{3mm}%",
                        @"  \sbox{\ealwordbox}{\textbf{\ealkey{#1}}}%",
                        @"  \ifdim\wd\ealwordbox>\ealwordfit",
                        @"    \ealwordlines{\raggedleft}{#1}%",
                        @"  \else",
                        @"    \makebox[\ealwordcol][l]{\textcolor{ealrulecolour}{\dotfill}%",
                        @"      \hspace{\ealwordgap}\usebox{\ealwordbox}}%",
                        @"  \fi",
                        @"  \par",
                        @"}")
                    : String.Join('\n',
                        @"\newcommand{\ealentry}[2]{%",
                        @"  \par\addvspace{1.6mm}%",
                        @"  \noindent",
                        @"  \sbox{\ealwordbox}{\textbf{\ealkey{#1}}}%",
                        @"  \ifdim\wd\ealwordbox>\ealwordfit",
                        @"    \ealwordlines{\raggedright}{#1}%",
                        @"  \else",
                        @"    \makebox[\ealwordcol][l]{\usebox{\ealwordbox}%",
                        @"      \hspace{\ealwordgap}\textcolor{ealrulecolour}{\dotfill}}%",
                        @"  \fi",
                        @"  \hspace{3mm}%",
                        @"  \parbox[t]{\ealmeaningcol}{\raggedright\strut #2}%",
                        @"  \par",
                        @"}"));

        // A word too long for one line, set over as many as it needs and aligned to
        // whichever margin its language reads from.
        //
        // Hyphenation is all but forbidden inside it, so "common denominator" breaks
        // at its space rather than as "common denomina-tor" - a hyphenated word is a
        // poor thing to put in front of somebody learning it. 9999 rather than 10000,
        // so a single word wider than the whole column is still hyphenated rather than
        // left to run over the edge.
        private static String WordLines() =>
            String.Join('\n',
                @"\newcommand{\ealwordlines}[2]{%",
                @"  \parbox[t]{\ealwordcol}{#1\hyphenpenalty=9999\relax",
                @"    \strut\textbf{\ealkey{#2}}}%",
                @"}");

        // The measure-and-repeat that fills a page with copies. Written with kernel
        // primitives because the height has to be known before anything is placed:
        // \ealfits is a count taking a dimension coerced to scaled points, which is how
        // TeX does the division.
        private static String PackingMacros() =>
            String.Join('\n',
                "% As many copies of a body as fit go on one page, with a cut line",
                "% between them, so a key of five terms is printed four to a sheet",
                "% rather than taking one to itself. A body too tall to fit is typeset",
                "% directly instead, so it breaks across pages rather than overflowing.",
                @"\newsavebox{\ealcopybox}",
                @"\newlength{\ealcopyht}",
                @"\newlength{\ealcutgap}",
                @"\setlength{\ealcutgap}{10mm}",
                @"\newcount\ealfits",
                @"\newcount\ealdone",
                "",
                @"\newcommand{\ealcut}{%",
                @"  \par\vspace{0.5\ealcutgap}%",
                @"  \noindent\hbox to \textwidth{%",
                @"    \color{ealrulecolour}%",
                @"    \leaders\hbox to 6pt{\hss\rule[0.55ex]{3pt}{0.4pt}\hss}\hfill}%",
                @"  \par\vspace{0.5\ealcutgap}%",
                @"}",
                "",
                @"\newcommand{\ealpage}[1]{%",
                @"  \sbox{\ealcopybox}{\parbox{\textwidth}{#1}}%",
                @"  \setlength{\ealcopyht}{\dimexpr\ht\ealcopybox+\dp\ealcopybox\relax}%",
                @"  \ifdim\ealcopyht>\textheight",
                @"    #1%",
                @"  \else",
                @"    \ealfits=\dimexpr\textheight+\ealcutgap\relax",
                @"    \ealdone=\dimexpr\ealcopyht+\ealcutgap\relax",
                @"    \divide\ealfits by \ealdone",
                @"    \ifnum\ealfits<1 \ealfits=1 \fi",
                @"    \ealdone=0",
                @"    \loop\ifnum\ealdone<\ealfits",
                @"      \ifnum\ealdone>0 \ealcut\fi",
                @"      \noindent\usebox{\ealcopybox}\par",
                @"      \advance\ealdone by 1",
                @"    \repeat",
                @"  \fi",
                @"  \newpage",
                @"}");

        // A TikZ matrix rather than a tabular, because the answer page has to draw a
        // line from each word to its meaning and the two pages must agree on where the
        // rows are. Nothing uses "remember picture", so the grid is placed in one pass
        // and a packed copy of it is simply drawn again.
        //
        // The cell separator is \ealnextcol rather than the usual \&, which would
        // collide with the escaped ampersand a definition is allowed to contain.
        //
        // A right to left language has the columns the other way round: the words on
        // the right, where its reader starts, and the meanings flush right in the
        // column to their left, so that each meaning ends against the gutter the
        // answer lines cross. Left as it is for English, a line would stop at the far
        // side of the column and leave a gap before the text it points at.
        // A word too long for its column wraps here rather than overflowing, since a
        // node with a text width is a paragraph. What it must not do is hyphenate, for
        // the same reason as on the key page - so the words are given the same near
        // ban on it. It has to be set from execute at begin node rather than from
        // inside the cell: a cell's own braces are a group, and the setting would be
        // restored before the node's paragraph was ever broken into lines.
        private static String MatchGrid(bool rightToLeft)
        {
            String words = "nodes={text width=38mm, align=left, "
                + @"execute at begin node={\hyphenpenalty=9999\relax}}";
            String meanings = "nodes={text width=82mm, align="
                + (rightToLeft ? "right" : "left") + "}";

            return String.Join('\n',
                @"\tikzset{",
                @"  ealmatchgrid/.style={",
                @"    matrix of nodes, ampersand replacement=\ealnextcol,",
                @"    row sep=4mm, column sep=24mm,",
                @"    nodes={anchor=west, inner sep=1pt},",
                @"    column 1/.style={" + (rightToLeft ? meanings : words) + "},",
                @"    column 2/.style={" + (rightToLeft ? words : meanings) + "},",
                @"  },",
                @"  ealanswerline/.style={ealkeycolour!70!white, line width=0.7pt},",
                @"}");
        }

        #endregion

        #region The bodies

        private static String KeyBody(
            IReadOnlyList<VocabTerm> terms,
            L2Macros.SourceMetadataTitle metadata,
            LanguageProfile? language)
        {
            StringBuilder sb = new();

            sb.AppendLine(@"\newcommand{\ealkeybody}{%");
            sb.AppendLine(Title($"{Readable(metadata.RootName)}: key vocabulary"));

            foreach (VocabTerm term in terms)
            {
                sb.AppendLine($@"  \ealentry{{{Escape(term.English)}}}{{{Meaning(term, language)}}}");
            }

            sb.AppendLine("}");

            return sb.ToString();
        }

        // The English words stay in the order of the key and the meanings are shuffled.
        // In a translated key the meaning is the translated word and its definition
        // together, since what is being tested there is the translation rather than the
        // meaning.
        private static String MatchBodies(
            IReadOnlyList<VocabTerm> terms,
            L2Macros.SourceMetadataTitle metadata,
            LanguageProfile? language)
        {
            List<VocabTerm> shuffled = L2VocabData.Shuffled(terms, metadata.RootName);

            bool mirrored = language?.RightToLeft ?? false;

            String instruction = language is null
                ? "Match the word to its meaning"
                : $"Match the English word to its {language.TitleName} meaning";

            StringBuilder sb = new();

            // the rows are written once and used by both pages, so the answer lines
            // cannot end up pointing at a grid that has moved
            sb.AppendLine(@"\newcommand{\ealmatchrows}{%");

            for (int i = 0; i < terms.Count; i++)
            {
                String word = $@"{{\textbf{{\ealkey{{{Escape(terms[i].English)}}}}}}}";
                String meaning = $"{{{Meaning(shuffled[i], language)}}}";

                sb.AppendLine(mirrored
                    ? $@"  {meaning} \ealnextcol {word} \\"
                    : $@"  {word} \ealnextcol {meaning} \\");
            }

            sb.AppendLine("}");
            sb.AppendLine();

            sb.AppendLine(@"\newcommand{\ealmatchbody}{%");
            sb.AppendLine(Title(instruction));
            sb.AppendLine(@"  \ealstart\begin{tikzpicture}");
            sb.AppendLine(@"    \matrix (m) [ealmatchgrid] {\ealmatchrows};");
            sb.AppendLine(@"  \end{tikzpicture}}");
            sb.AppendLine();

            sb.AppendLine(@"\newcommand{\ealanswerbody}{%");
            sb.AppendLine(Title($"{instruction}: answers"));
            sb.AppendLine(@"  \ealstart\begin{tikzpicture}");
            sb.AppendLine(@"    \matrix (m) [ealmatchgrid] {\ealmatchrows};");

            // One straight line per pair, drawn from the edge of the word to the edge
            // of the meaning - which are opposite sides of the gutter, and so the other
            // way round when the grid is mirrored. That this reads cleanly at all is
            // down to the shuffle being bounded: no meaning sits more than a few rows
            // from its word, so no line is steep enough to sweep across the others.
            String from = mirrored ? "2.west" : "1.east";
            String to = mirrored ? "1.east" : "2.west";

            for (int i = 0; i < terms.Count; i++)
            {
                int row = shuffled.FindIndex(t => t.English == terms[i].English);

                sb.AppendLine(
                    $@"    \draw[ealanswerline] (m-{i + 1}-{from}) -- (m-{row + 1}-{to});");
            }

            sb.AppendLine(@"  \end{tikzpicture}}");

            return sb.ToString();
        }

        private static String Meaning(VocabTerm term, LanguageProfile? language) =>
            language is null
                ? Escape(term.Definition)
                : $@"\ealtrmeaning{{{Escape(term.Translation)}}}"
                  + $@"{{{Escape(term.TranslatedDefinition)}}}";

        // \ealstart puts the heading at the margin its language reads from. The
        // heading is in English even on a translated key, but a left aligned one over
        // a mirrored body reads as a mistake rather than as a choice.
        private static String Title(String text) =>
            $@"  \ealstart{{\large\bfseries {Escape(text)}}}\par\medskip";

        #endregion

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
