using SyntheticPDFs.Services;
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


        // As I see errors I can update these methods, or even find a library that will e.g. identify valid tex
        internal static bool IsValidTex(String response)
        {
            bool okFirstLast = OkFirstChar(response) && !LastLineIsTicks(response);

            bool allBeginsAreClosed = BeginBalance(response) == 0;

            bool noBadChars = !HasBadImplicationCharacter(response) && !HasBadEquivEqualCharacter(response);

            return okFirstLast && allBeginsAreClosed && noBadChars;
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
