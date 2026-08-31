using SyntheticPDFs.Configuration;
using SyntheticPDFs.Models;
using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Services;

namespace SyntheticPDFs.Rendering
{
    public static partial class SourceGenerator
    {

        internal static async Task<String?> TryGetValidTex(ILLMService LLM, String prompt, int retry = 3)
        {
            for (int i = 0; i != retry; i++)
            {
                String response = await LLM.GetResponse(prompt);

                if (IsValidTex(response)) { return response; }

                LLM.Log(LogLevel.Warning, "Failed to generate good source");

                response = TryFixupTex(response, LLM);

                if (IsValidTex(response)) { return response; }

                LLM.Log(LogLevel.Warning, "Failed to fixup bad tex source");

                LLM.Log(LogLevel.Warning, $"attemtp {i + 1} at getting valid Tex failed!");
            }

            LLM.Log(LogLevel.Error, "failed to generate valide Tex!, returning null");

            return null;
        }

        // same shape as TryGetValidTex - the model gets a few goes at reaching a verdict
        // before we give up, since a shrug is not a verdict either way
        internal static async Task<ReviewVerdict?> TryGetReview(ILLMService LLM, String question, int retry = 3)
        {
            for (int i = 0; i != retry; i++)
            {
                ReviewVerdict? verdict = await LLM.GetReviewResponse(question);

                if (verdict is not null) { return verdict; }

                LLM.Log(LogLevel.Warning, $"attempt {i + 1} at getting a review verdict failed!");
            }

            LLM.Log(LogLevel.Error, "failed to get a review verdict!, returning null");

            return null;
        }

        // same shape as TryGetValidTex - the model gets a few goes at returning readable
        // data before we give up. what comes back is a word list, not a document, so the
        // check is that it parses rather than that it would compile
        internal static async Task<List<VocabTerm>?> TryGetVocabulary(
            ILLMService LLM, String prompt, int retry = 3)
        {
            for (int i = 0; i != retry; i++)
            {
                String response = await LLM.GetStructuredResponse(prompt);

                List<VocabTerm>? terms = L2VocabData.TryParse(response);

                if (terms is not null) { return terms; }

                LLM.Log(LogLevel.Warning, $"attempt {i + 1} at reading a vocabulary list failed!");
            }

            LLM.Log(LogLevel.Error, "failed to read a vocabulary list!, returning null");

            return null;
        }

        internal static async Task<List<VocabTerm>> GenerateVocabularyTerms(
            String rootSource, String? workedSolutions, String? solutions, ILLMService LLM)
        {
            String prompt = GenerateVocabularyKeyPrompt(rootSource, workedSolutions, solutions);

            List<VocabTerm>? terms = await TryGetVocabulary(LLM, prompt);

            if (terms is null)
            {
                throw new Exception("failed to establish the tier 3 vocabulary!");
            }

            return terms;
        }

        internal static async Task<List<VocabTerm>> TranslateVocabularyTerms(
            IReadOnlyList<VocabTerm> terms, LanguageProfile language, ILLMService LLM)
        {
            String prompt = TranslateVocabularyKeyPrompt(terms, language);

            List<VocabTerm>? translated = await TryGetVocabulary(LLM, prompt);

            if (translated is null)
            {
                throw new Exception($"failed to translate the vocabulary into {language.TitleName}!");
            }

            return Reconcile(terms, translated, language, LLM);
        }

        // The English half is ours and must not come back changed, and a translation that
        // dropped or invented entries would put the key and the match-up out of step with
        // the sheet. So the English list is authoritative: entries are matched back on to
        // it by word, anything extra is discarded, and anything the model failed to
        // translate keeps the English so the key is still usable rather than missing rows.
        private static List<VocabTerm> Reconcile(
            IReadOnlyList<VocabTerm> english,
            IReadOnlyList<VocabTerm> translated,
            LanguageProfile language,
            ILLMService LLM)
        {
            var byWord = translated
                .GroupBy(t => t.English, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            List<VocabTerm> ret = new();

            int untranslated = 0;

            foreach (VocabTerm term in english)
            {
                if (byWord.TryGetValue(term.English, out VocabTerm? match)
                    && match.Translation.Length > 0)
                {
                    ret.Add(term with
                    {
                        Translation = match.Translation,

                        // a translated word with an untranslated definition is still
                        // worth having, so fall back rather than dropping the row
                        TranslatedDefinition = match.TranslatedDefinition.Length > 0
                            ? match.TranslatedDefinition
                            : term.Definition,
                    });

                    continue;
                }

                untranslated++;

                ret.Add(term with
                {
                    Translation = term.English,
                    TranslatedDefinition = term.Definition,
                });
            }

            if (untranslated > 0)
            {
                LLM.Log(LogLevel.Warning,
                    $"{untranslated} of {english.Count} terms came back untranslated into "
                    + $"{language.TitleName}, leaving the English in their place");
            }

            return ret;
        }

        // The body of a translated sheet. Same retry shape as TryGetValidTex, with the
        // extra checks that a body has to pass: it must be a body rather than a whole
        // document, it must not redefine the helpers it is given, and it must actually
        // use them - a "translation" that used none of them would be the English sheet
        // under a different name, which is worse than a failure because it looks fine.
        internal static async Task<String> GenerateTranslatedBody(
            String english,
            IReadOnlyList<VocabTerm> terms,
            LanguageProfile language,
            L2ColourOptions colours,
            SheetForm form,
            ILLMService LLM,
            int retry = 3)
        {
            String prompt = form == SheetForm.ParallelText
                ? GenerateParallelTextPrompt(english, terms, language, colours)
                : GenerateTier3OnlyPrompt(english, terms, language, colours);

            for (int i = 0; i != retry; i++)
            {
                String response = await LLM.GetResponse(prompt);

                String body = StripFences(response);

                String? wrong = L2Document.WhatIsWrongWith(body);

                if (wrong is null && BeginBalance(body) == 0) { return body; }

                LLM.Log(LogLevel.Warning,
                    $"attempt {i + 1} at a {form} body failed: "
                    + (wrong ?? "its begin and end statements do not balance"));
            }

            throw new Exception($"failed to generate a usable {form} body!");
        }

        // the body is not a whole document, so IsValidTex does not apply to it - but the
        // code fence it sometimes arrives in is the same nuisance either way
        private static String StripFences(String response)
        {
            String body = response.Trim();

            if (!OkFirstChar(body) && body.Split('\n').Length > 1)
            {
                body = RemoveFirstLine(body).Trim();
            }

            while (LastLineIsTicks(body) && body.Split('\n').Length > 1)
            {
                body = RemoveLastLine(body).TrimEnd();
            }

            return body;
        }

        internal static async Task<String> GenerateSyntheticEnglishWorkedSolutionsTexSource(TexSourceModel rootSource, SheetArchetype at, ILLMService LLM)
        {
            String prompt = GenerateEnglishWorkedSolutionsPrompt(rootSource.TexSource, at);

            String? texSource = await TryGetValidTex(LLM, prompt);

            if (texSource is null)
            {
                throw new Exception("failed to generate good source!");
            }

            return texSource;
        }



        internal static async Task<String> GenerateSyntheticEnglishSolutionsTexSource(TexSourceModel rootSource, TexSourceModel wsolSource, ILLMService LLM)
        {
            String prompt = GenerateEnglishSolutionsPrompt(rootSource.TexSource, wsolSource.TexSource);

            String? texSource = await TryGetValidTex(LLM, prompt);

            if (texSource is null)
            {
                throw new Exception("failed to generate good source!");
            }

            return texSource;
        }


        #region Answer overlay helpers

        // decks that reveal their own answers need no separate solutions pdf, so this is what
        // decides whether a deck is already doing that or needs the fixer run over it
        internal static async Task<ReviewVerdict> ReviewAnswerMacroUsage(TexSourceModel rootSource, ILLMService LLM)
        {
            // the definitions are free to check, and without them the verdict is settled
            if (!AnswerMacros.AreDefined(rootSource.TexSource))
            {
                return new ReviewVerdict
                {
                    Passed = false,
                    Reasons = "the deck does not define the answer overlay helpers verbatim in its preamble",
                };
            }

            ReviewVerdict? verdict = await TryGetReview(LLM, AnswerMacroUsageQuestion(rootSource.TexSource));

            if (verdict is null)
            {
                // treating a shrug as a pass ships an unchecked deck, as a fail it pays for a
                // rewrite that may not be needed, so do neither and let the next pass ask again
                throw new Exception("could not establish whether the answer overlay helpers are used!");
            }

            return verdict;
        }

        internal static async Task<String> GenerateSlidesWithAnswerMacrosTexSource(TexSourceModel rootSource, ILLMService LLM)
        {
            String prompt = GenerateAnswerMacroRewritePrompt(rootSource.TexSource);

            String? texSource = await TryGetValidTex(LLM, prompt);

            if (texSource is null)
            {
                throw new Exception("failed to generate good source!");
            }

            // refuse a rewrite that still fails the cheap check, otherwise it gets committed
            // and the next pass simply asks for the same rewrite again
            if (!AnswerMacros.AreDefined(texSource))
            {
                throw new Exception("rewritten slides still don't define the answer overlay helpers!");
            }

            return texSource;
        }

        internal static async Task<String> SummariseReviewReasons(IEnumerable<String> reasons, ILLMService LLM)
        {
            String summary = await LLM.GetSummaryResponse(SummariseReviewReasonsPrompt(reasons));

            if (String.IsNullOrWhiteSpace(summary))
            {
                throw new Exception("failed to summarise the review reasons!");
            }

            return summary;
        }

        #endregion
    }
}
