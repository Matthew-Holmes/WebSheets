using SyntheticPDFs.Services;
using System.Text;
using System.Text.RegularExpressions;


namespace SyntheticPDFs.Logic
{
    public partial class SourceGenerator
    {
        // stuff to do with whent the source gets wrapped in ```latex .. ```

        private static bool OkFirstChar(string response)
        {
            if (string.IsNullOrEmpty(response))
                return false;

            char c = response[0];
            return c == ' ' || c == '\\' || c == '%';
        }


        private static bool LastLineIsTicks(String response)
        {
            return response.Split("\n").Last().Contains("```");
        }

        private static String RemoveFirstLine(String badTex)
        {
            var lines = badTex.Split("\n");

            if (lines.Length <= 1) return string.Empty;

            return String.Join('\n', lines.Skip(1));
        }

        private static String RemoveLastLine(String badTex)
        {
            var lines = badTex.Split("\n");

            if (lines.Length <= 1) return string.Empty;

            int nLines = lines.Count();

            return String.Join('\n', lines.Take(nLines - 1));
        }

        // handle unended \begin{statements}

        private static string StripComments(string input)
        {
            var lines = input.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int idx = lines[i].IndexOf('%');
                if (idx >= 0)
                    lines[i] = lines[i].Substring(0, idx);
            }
            return string.Join("\n", lines);
        }


        private static int BeginBalance(string response)
        {
            response = StripComments(response);

            var beginMatches = Regex.Matches(response, @"\\begin\s*\{");
            var endMatches   = Regex.Matches(response, @"\\end\s*\{");

            return beginMatches.Count - endMatches.Count;
        }


        private static bool HasBadImplicationCharacter(String response)
        {
            return response.Contains('⇒');

            // replace it with \( \implies \) but what if in a math environment!!!
        }

        private static bool HasBadEquivEqualCharacter(String response)
        {
            return response.Contains('≈');
        }


        // nested math mode

        // a $ inside \( ... \) or \[ ... \] closes the maths early, and LaTeX stops with
        // "Missing $ inserted". it is always an error whatever put it there, and the answer
        // helpers make it an easy one to write by accident: \(x = \ablank{$5$}\)

        private enum MathToken { OpenInline, CloseInline, OpenDisplay, CloseDisplay, Dollar }

        // one pass over the source, skipping comments and reading a backslash plus the
        // character after it as a single unit, so \\ and \$ can't be mistaken for a delimiter
        // and \% can't be mistaken for the start of a comment
        private static List<(int Index, MathToken Token)> ScanMathTokens(String tex)
        {
            List<(int Index, MathToken Token)> tokens = new();

            int i = 0;

            while (i < tex.Length)
            {
                char c = tex[i];

                if (c == '%')
                {
                    int newline = tex.IndexOf('\n', i);
                    i = newline < 0 ? tex.Length : newline + 1;
                    continue;
                }

                if (c == '\\' && i + 1 < tex.Length)
                {
                    switch (tex[i + 1])
                    {
                        case '(': tokens.Add((i, MathToken.OpenInline)); break;
                        case ')': tokens.Add((i, MathToken.CloseInline)); break;
                        case '[': tokens.Add((i, MathToken.OpenDisplay)); break;
                        case ']': tokens.Add((i, MathToken.CloseDisplay)); break;
                    }

                    i += 2;
                    continue;
                }

                if (c == '$') { tokens.Add((i, MathToken.Dollar)); }

                i++;
            }

            return tokens;
        }

        // only dollars inside a span that actually closes count - an unclosed \( is broken
        // for a different reason, and reporting it as this one would just be misleading
        private static List<int> NestedMathDollars(String tex)
        {
            List<int> nested = new();

            var tokens = ScanMathTokens(tex);

            for (int i = 0; i < tokens.Count; i++)
            {
                MathToken open = tokens[i].Token;

                if (open != MathToken.OpenInline && open != MathToken.OpenDisplay) { continue; }

                MathToken wanted = open == MathToken.OpenInline
                    ? MathToken.CloseInline
                    : MathToken.CloseDisplay;

                List<int> inside = new();

                int j = i + 1;

                for (; j < tokens.Count && tokens[j].Token != wanted; j++)
                {
                    if (tokens[j].Token == MathToken.Dollar) { inside.Add(tokens[j].Index); }
                }

                if (j < tokens.Count)
                {
                    nested.AddRange(inside);
                    i = j;
                }
            }

            return nested;
        }

        internal static bool HasNestedMathMode(String response)
        {
            return NestedMathDollars(response).Count > 0;
        }

        // dropping the inner delimiters is what a person would do by hand: \ablank{$37.5\%$}
        // inside \( ... \) becomes \ablank{37.5\%}, which is valid maths either way
        internal static String RemoveNestedMathDelimiters(String badTex)
        {
            HashSet<int> drop = NestedMathDollars(badTex).ToHashSet();

            if (drop.Count == 0) { return badTex; }

            StringBuilder sb = new StringBuilder(badTex.Length);

            for (int i = 0; i < badTex.Length; i++)
            {
                if (!drop.Contains(i)) { sb.Append(badTex[i]); }
            }

            return sb.ToString();
        }


        // As I see errors I can update these methods, or even find a library that will e.g. identify valid tex
        internal static bool IsValidTex(String response)
        {
            bool okFirstLast = OkFirstChar(response) && !LastLineIsTicks(response);

            bool allBeginsAreClosed = BeginBalance(response) == 0;

            bool noBadChars = !HasBadImplicationCharacter(response) && !HasBadEquivEqualCharacter(response);

            bool noNestedMath = !HasNestedMathMode(response);

            return okFirstLast && allBeginsAreClosed && noBadChars && noNestedMath;
        }

        internal static String TryFixupTex(String badTex, ILLMService LLM)
        {
            // occasionally is wrapped in ```latex from the LLM, so strip the first and last and see if that helps!

            if (!OkFirstChar(badTex))
            {
                LLM.Log(LogLevel.Warning, "first character was bad, removing first line");
                badTex = RemoveFirstLine(badTex);
            }

            if (LastLineIsTicks(badTex))
            {
                LLM.Log(LogLevel.Warning, "last line had ticks, removing last line");
                badTex = RemoveLastLine(badTex);
            }

            // check \begin lines match \end lines

            if (BeginBalance(badTex) != 0)
            {
                LLM.Log(LogLevel.Warning, $"begin/end balance was {BeginBalance(badTex)}, attempting fixup");
                badTex = CloseUnclosedBegins(badTex, LLM);
            }
            
            // deal with untypesettable characters

            if (HasBadImplicationCharacter(badTex))
            {
                LLM.Log(LogLevel.Warning, "LLM returned response containing (U+21D2), replacing with latex version");
                badTex = badTex.Replace("⇒", @"\( \implies \)"); // this will break if already in maths env, but better than nothing
            }

            if (HasBadEquivEqualCharacter(badTex))
            {
                LLM.Log(LogLevel.Warning, "LLM returned response containig (U+2248), replacing with latex version");
                badTex = badTex.Replace("≈", @"\( \approx \)");
            }

            if (HasNestedMathMode(badTex))
            {
                LLM.Log(LogLevel.Warning, @"found $ inside \( \), removing the inner delimiters");
                badTex = RemoveNestedMathDelimiters(badTex);
            }

            return badTex;
        }

        internal static string CloseUnclosedBegins(String badTex, ILLMService LLM)
        {
            Stack<String> stack = new();

            var lines = badTex.Split("\n").ToList();

            // This is not able to deal with all the edge cases, such as inline \begin \end statements
            // but will try to do a better job than stuff we definitely know is broken!

            foreach(var line in lines)
            {
                var clean = line.TrimStart();

                if (clean.StartsWith(@"\item"))
                {
                    clean = clean.Substring(5).TrimStart();
                }

                if (clean.StartsWith(@"\begin{"))
                {
                    int open = line.IndexOf('{');
                    int close = line.IndexOf('}', open + 1);
                    if (open < 0 || close < 0)
                    {
                        LLM.Log(LogLevel.Error, @"broken \begin in line: " + line);
                        return badTex;
                    }

                    String substance = line.Substring(open + 1, close - open - 1);


                    stack.Push(substance);
                    continue;
                }

                if (clean.StartsWith(@"\end{"))
                {
                    int open = line.IndexOf('{');
                    int close = line.IndexOf('}', open + 1);
                    if (open < 0 || close < 0)
                    {
                        LLM.Log(LogLevel.Error, @"broken \end in line: " + line);
                        return badTex;
                    }

                    String substance = line.Substring(open + 1, close - open - 1);

                    bool stackNotEmpty = stack.TryPop(out String? top);

                    if (!stackNotEmpty)
                    {
                        // stack went bad
                        LLM.Log(LogLevel.Error, "more ends than begins, fixup failed!");
                        return badTex;
                    }

                    if (top! != substance)
                    {
                        // stack went bad
                        LLM.Log(LogLevel.Error, $"environment mismatch, begun: {top!}, ended {substance} !, fixup failed");
                        return badTex;
                    }
                }
            }

            while (stack.TryPop(out String? unclosed))
            {
                LLM.Log(LogLevel.Warning, @"unclosed \begin, adding: " + @"\end{" + unclosed + "}");
                lines.Add(@"\end{" + unclosed + "}");
            }

            return String.Join('\n', lines);
        }
    }
}
