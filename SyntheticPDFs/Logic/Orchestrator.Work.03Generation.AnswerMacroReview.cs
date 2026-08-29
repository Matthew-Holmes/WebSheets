using SyntheticPDFs.Models;
using SyntheticPDFs.Services;

namespace SyntheticPDFs.Logic
{
    public partial class Orchestrator
    {
        // where an unresolved review puts its note. a deck the fixer keeps changing may have
        // something more fundamental wrong with it, so that one is said up front
        internal enum ReviewNotePlacement
        {
            End,
            Start,
        }

        internal record AnswerMacroReviewOutcome
        {
            // the deck as it should be committed, note slide and all
            internal required TexSourceModel Deck { get; init; }

            // the same deck without any note slide, since worked solutions are derived from
            // the questions and shouldn't pick up a note about themselves
            internal required TexSourceModel Questions { get; init; }

            internal required bool DeckChanged { get; init; }

            internal required bool Passed { get; init; }
        }

        // The review and the fixer are two models that can disagree forever, so this bounds
        // them: at most two rounds of fixing, and every path ends with the deck settled and
        // the worked solutions marked, so a deck is never reviewed twice over the same bytes.
        //
        // The fixer is deliberately NOT told what the review objected to. It is given a fixed
        // brief and judged on it - if the review is inventing work, that stays the review's
        // problem and doesn't get to drag the deck around.
        private static async Task<AnswerMacroReviewOutcome> ReviewAndFixAnswerMacros(TexSourceModel deck, ILLMService LLM)
        {
            List<String> reasons = new();

            ReviewVerdict verdict = await Review(deck, round: 0, LLM);

            if (verdict.Passed) { return Settled(deck, changed: false); }

            reasons.Add(verdict.Reasons);

            TexSourceModel first = await TryFix(deck, LLM) ?? deck;

            ReviewVerdict firstVerdict = await Review(first, round: 1, LLM);

            if (firstVerdict.Passed) { return Settled(first, changed: Differs(first, deck)); }

            reasons.Add(firstVerdict.Reasons);

            // case 1 - the fixer read the same deck and left it alone, so the two of them
            // disagree about something fundamental and no amount of rounds will settle it
            if (!Differs(first, deck))
            {
                LLM.Log(LogLevel.Warning,
                    $"answer macro review and fixer disagree on {deck.FileNameFullPath}, the fixer changed nothing");

                return await Unresolved(deck, changed: false, ReviewNotePlacement.End, reasons, LLM);
            }

            // case 2 - the fixer did something, so it gets one more go before we stop
            TexSourceModel second = await TryFix(first, LLM) ?? first;

            ReviewVerdict secondVerdict = await Review(second, round: 2, LLM);

            if (secondVerdict.Passed) { return Settled(second, changed: true); }

            reasons.Add(secondVerdict.Reasons);

            if (!Differs(second, first))
            {
                // deadlocked the same way as case 1, just a round later
                LLM.Log(LogLevel.Warning,
                    $"answer macro fixer stopped changing {deck.FileNameFullPath} without satisfying the review");

                return await Unresolved(first, changed: true, ReviewNotePlacement.End, reasons, LLM);
            }

            // still moving after two rounds. stopping here rather than going round again is
            // the whole point, but a deck that keeps changing is worth saying up front
            LLM.Log(LogLevel.Warning,
                $"answer macro review still failing {deck.FileNameFullPath} after two rounds, and the deck is "
                + "still changing - stopping and noting it at the front");

            return await Unresolved(second, changed: true, ReviewNotePlacement.Start, reasons, LLM);
        }


        private static async Task<ReviewVerdict> Review(TexSourceModel deck, int round, ILLMService LLM)
        {
            ReviewVerdict verdict = await SourceGenerator.ReviewAnswerMacroUsage(deck, LLM);

            // the reasons are the whole point of logging this - without them a rejection is a
            // single bit and nobody can tell whether the review is right
            if (verdict.Passed)
            {
                LLM.Log(LogLevel.Information,
                    $"answer macro review round {round} PASS for {deck.FileNameFullPath}");
            }
            else
            {
                LLM.Log(LogLevel.Warning,
                    $"answer macro review round {round} FAIL for {deck.FileNameFullPath}, reasons: {verdict.Reasons}");
            }

            return verdict;
        }

        // a fixer that can't produce usable source counts as one that changed nothing, which
        // lands the deck in case 1 and gets a person told, rather than failing the whole job
        // and leaving it to be retried forever
        private static async Task<TexSourceModel?> TryFix(TexSourceModel deck, ILLMService LLM)
        {
            try
            {
                String texSource = await SourceGenerator.GenerateSlidesWithAnswerMacrosTexSource(deck, LLM);

                return deck with { TexSource = texSource };
            }
            catch (Exception e)
            {
                LLM.Log(LogLevel.Warning,
                    $"answer macro fixer produced nothing usable for {deck.FileNameFullPath}: {e.Message}");

                return null;
            }
        }

        private static bool Differs(TexSourceModel a, TexSourceModel b)
        {
            return !String.Equals(a.TexSource, b.TexSource, StringComparison.Ordinal);
        }

        private static AnswerMacroReviewOutcome Settled(TexSourceModel deck, bool changed)
        {
            return new AnswerMacroReviewOutcome
            {
                Deck = deck,
                Questions = deck,
                DeckChanged = changed,
                Passed = true,
            };
        }

        // the deck gets marked either way, so the pipeline moves on - what a person gets is a
        // slide saying the answers may not be right and what the review made of them
        private static async Task<AnswerMacroReviewOutcome> Unresolved(
            TexSourceModel deck,
            bool changed,
            ReviewNotePlacement placement,
            List<String> reasons,
            ILLMService LLM)
        {
            AnswerMacroReviewOutcome withoutNote = new AnswerMacroReviewOutcome
            {
                Deck = deck,
                Questions = deck,
                DeckChanged = changed,
                Passed = false,
            };

            if (AnswerMacros.HasReviewNote(deck.TexSource))
            {
                LLM.Log(LogLevel.Information,
                    $"{deck.FileNameFullPath} already carries a review note, not adding another");

                return withoutNote;
            }

            String summary = await Summarise(reasons, LLM);

            String? withNote = AnswerMacros.AddReviewNote(
                deck.TexSource, summary, atStart: placement == ReviewNotePlacement.Start);

            if (withNote is null)
            {
                // no document body to put a frame in, so the deck could not have compiled
                // anyway - nothing to do but say so
                LLM.Log(LogLevel.Error,
                    $"found nowhere in {deck.FileNameFullPath} to put a review note");

                return withoutNote;
            }

            LLM.Log(LogLevel.Information,
                $"noted unresolved answer macro review on {deck.FileNameFullPath} at the "
                + $"{placement.ToString().ToLowerInvariant()}: {summary}");

            return withoutNote with
            {
                Deck = deck with { TexSource = withNote },
                DeckChanged = true,
            };
        }

        // the reasons are the thing a person actually needs, so a summariser that falls over
        // doesn't get to stop them arriving - they just arrive unpolished
        private static async Task<String> Summarise(List<String> reasons, ILLMService LLM)
        {
            try
            {
                return await SourceGenerator.SummariseReviewReasons(reasons, LLM);
            }
            catch (Exception e)
            {
                LLM.Log(LogLevel.Warning,
                    $"failed to summarise the review reasons, using them as they are: {e.Message}");

                return String.Join(" ", reasons);
            }
        }
    }
}
