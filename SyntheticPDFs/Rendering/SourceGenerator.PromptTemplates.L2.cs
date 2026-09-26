using SyntheticPDFs.Configuration;
using SyntheticPDFs.Models.Content;

namespace SyntheticPDFs.Rendering
{
    public static partial class SourceGenerator
    {
        #region The vocabulary key

        // What counts as tier 3, said the same way every time. The value of these sheets
        // is in isolating the words a learner cannot infer, so the brief is deliberately
        // narrow - a list padded with ordinary English is worse than a short one.
        private static String Tier3Brief => String.Join(' ',
            "Tier 3 vocabulary means the subject specific words a mathematics lesson depends on -",
            "words like numerator, perpendicular, coefficient or hypotenuse, which a learner will",
            "not work out from everyday English and which carry a precise mathematical meaning.",
            "Include a word whose everyday meaning differs from its mathematical one, such as product,",
            "power, mean or similar, because those are the ones that mislead.",
            "Do not include ordinary English that happens to appear in the question, do not include",
            "numbers or symbols, and do not include instruction words such as write, find or calculate",
            "unless they carry a specific mathematical sense here.",
            "A short accurate list is worth far more than a long one.");

        private static String DefinitionBrief => String.Join(' ',
            "Define each word as it is used in this sheet, in plain English a learner of about eleven",
            "to sixteen could read, in one sentence and at most about twenty words.",
            "Do not use the word itself in its own definition, and do not use another tier 3 word in a",
            "definition unless that word is also in your list.");

        private static String VocabJsonShape => String.Join(' ',
            @"Return a single JSON object of the form {""terms"":[{""en"":""numerator"",",
            @"""def"":""the number above the line in a fraction""}]}.",
            @"Use the key ""en"" for the English word and ""def"" for its definition.",
            "List the terms in the order they first appear in the sheet.",
            "Return nothing but that object - no explanation, no markdown fence.");

        // the key covers the whole sheet, answers included, because a word a learner
        // needs may appear only in the workings
        internal static String GenerateVocabularyKeyPrompt(
            String rootSource, String? workedSolutions, String? solutions)
        {
            String parts = "Below is a mathematics worksheet.";

            if (workedSolutions is not null || solutions is not null)
            {
                parts = "Below are the parts of one mathematics worksheet - the questions, and the "
                      + "worked solutions and answers derived from them.";
            }

            String sources = "\n\n--- QUESTIONS ---\n\n" + rootSource;

            if (workedSolutions is not null)
            {
                sources += "\n\n--- WORKED SOLUTIONS ---\n\n" + workedSolutions;
            }

            if (solutions is not null)
            {
                sources += "\n\n--- ANSWERS ---\n\n" + solutions;
            }

            return $"{parts} It is going to be used by pupils who are learning English as an "
                + "additional language, so the subject specific vocabulary in it needs isolating and "
                + "explaining. "
                + $"{Tier3Brief} "
                + "Read all the parts below and list the tier 3 vocabulary used anywhere in them, "
                + "including in the workings and answers - a word a pupil needs may appear only there. "
                + $"{DefinitionBrief} "
                + $"{VocabJsonShape}"
                + sources;
        }

        #endregion

        #region Translating the key

        internal static String TranslateVocabularyKeyPrompt(
            IReadOnlyList<VocabTerm> terms, LanguageProfile language)
        {
            String list = String.Join('\n', terms.Select(t => $"- {t.English}: {t.Definition}"));

            return $"Below is a list of mathematics vocabulary in English, each word with its "
                + "definition. Translate it for a pupil whose first language is "
                + $"{language.TitleName} and who is learning mathematics in English. "
                + $"For each entry give the {language.TitleName} word a mathematics teacher would "
                + $"use for it, and a translation of the definition into {language.TitleName}. "
                + "Translate the definition rather than writing a new one, so that the English and "
                + "the translation say the same thing. "
                + $"Where mathematics in {language.TitleName} normally keeps the English term, give "
                + "the term as it is actually used rather than inventing a translation. "
                + "Keep every entry, keep them in the order given, and do not add any. "
                + @"Return a single JSON object of the form {""terms"":[{""en"":""numerator"","
                + @"""def"":""the number above the line in a fraction"",""tr"":""..."","
                + @"""trdef"":""...""}]}, "
                + @"where ""en"" and ""def"" are copied unchanged from the input, ""tr"" is the "
                + $@"{language.TitleName} word and ""trdef"" is the definition in {language.TitleName}. "
                + "Return nothing but that object.\n\n"
                + list;
        }

        #endregion

        #region Translated sheets

        // The preamble, the macros and the provenance are all added afterwards by the
        // generator, so the model must not write any of them. Saying so plainly is
        // cheaper than trying to strip a duplicate \newcommand back out, and a duplicate
        // definition is a hard LaTeX error rather than something that merely looks wrong.
        private static String L2StructureRules => String.Join(' ',
            "The document class, the packages, the language setup and the helper macros below are all",
            "added for you afterwards. Do not write a preamble, do not load any package, and above all",
            @"do not define \ealkey, \ealkeytr, \ealgloss, \ealpara, \ealtext or the ealglossed",
            "environment - defining one of them a second time stops the file compiling.",
            @"Give back the document body only: everything from \begin{document} to \end{document}",
            "inclusive, and nothing before or after it.");

        private static String L2MacroRules => String.Join(' ',
            @"\ealkey{word} marks a tier 3 word in the English text.",
            @"\ealgloss{english}{translation} puts one translated word directly above one English word,",
            "and already colours both, so never wrap it in anything.",
            @"\ealpara{translation}{english} puts a whole translated sentence above its English",
            "counterpart. It starts a new paragraph, so it must never go inside a table cell or a",
            "TikZ node.",
            @"\ealkeytr{word} marks a tier 3 word inside translated text.",
            "The ealglossed environment opens up the line spacing around a block that contains",
            @"\ealgloss, so every paragraph using \ealgloss must sit inside one.",
            "These are the only helpers there are. Never invent another.");

        // The colours are described rather than set, because the macros apply them. A
        // model told to colour something itself will reach for \textcolor and get the
        // shade wrong, or nest it inside a helper that is already colouring.
        private static String L2ColourRules(L2ColourOptions colours) => String.Join(' ',
            $"Tier 3 vocabulary is shown in {colours.Tier3.Name}, the English text in",
            $"{colours.English.Name}, and the translation in {colours.Translation.Name}.",
            "The helpers apply all of that themselves, so never write a colour command of your own",
            "and never change the colour of anything.");

        private static String L2LayoutRules => String.Join(' ',
            "There is more text on the page than there was, so give it room.",
            "Adding whitespace is welcome, and so is letting the sheet run on to more pages than the",
            "original used - a worksheet that fitted on one page may take two, and one slide may",
            "become several. That is expected and is better than a crowded page.",
            "Where the original places something deliberately - a diagram, a TikZ picture, anything at",
            "coordinates, anything in a box or a table - leave its placement exactly as it is and put",
            "the translation somewhere it cannot overlap. If there is no room beside such a thing, put",
            "the translation above or below it, or move the whole thing to a page of its own.",
            "Never place translated text on top of a diagram.");

        // The sheet's own preamble goes back on afterwards along with ours, so the model is
        // shown only the body - but the body is full of what that preamble defines, so it
        // still needs telling that those exist. Named rather than shown, leaving it nothing
        // to copy.
        private static String SheetMacroRules(String source)
        {
            IReadOnlyList<String> itsOwn = L2Document.MacrosDefinedBy(L2Document.PreambleOf(source));

            if (itsOwn.Count == 0) { return String.Empty; }

            return " The sheet's own preamble is added for you as well, and it already defines "
                + String.Join(", ", itsOwn)
                + ". Use them just as the English does, and never define any of them again - "
                + "defining one a second time stops the file compiling.";
        }

        private static String TermsList(IReadOnlyList<VocabTerm> terms, LanguageProfile language) =>
            String.Join('\n', terms.Select(t =>
                $"- {t.English} = {t.Translation} ({language.TitleName} for: {t.TranslatedDefinition})"));

        // the vocabulary is fixed by the key, so that the same word is picked out and
        // translated the same way on every sheet a pupil sees
        private static String VocabularyRules(IReadOnlyList<VocabTerm> terms, LanguageProfile language) =>
            String.Join(' ',
                "These are the tier 3 words for this sheet, with the translation to use for each.",
                "This list is fixed: mark these words and only these words, and use the translation",
                "given rather than one of your own, so the same word reads the same way on every",
                "sheet a pupil sees.",
                "Do not mark any other word, and do not translate a listed word differently.",
                "A word not in the list is ordinary English however technical it looks.",

                // The list gives each word in its dictionary form. Most of these languages
                // inflect, so a sentence will often need a different form of it - Polish
                // "ulamek" becomes "ulamka" after "each". Demanding the dictionary form
                // verbatim would produce translations that read as wrong to the pupil the
                // sheet is for, which is a worse failure than an inconsistent ending.
                $"The list gives each word in its dictionary form. Put it into whatever form the",
                $"sentence needs - {language.TitleName} grammar comes first, and a word that has to",
                "change its ending to fit is still that word and is still marked.",
                "What must not change is which word you have chosen for it.",
                $"\n\n{TermsList(terms, language)}\n\n");

        #region What a deck of slides needs as well

        // A translated slide carries two languages where the English one carried one, and
        // a glossed slide lifts a translation above every tier 3 word on it. Either way
        // there is far more on the slide than there was, and a slide is the one thing
        // here that cannot run on: a worksheet that no longer fits takes another page,
        // but whatever will not fit on a slide is off the board rather than below it.
        //
        // So a starter is split across three slides. Three is a rule of thumb rather than
        // a measurement - roughly what the extra text costs - and a rule the model can
        // follow the same way every time is worth more here than asking it to judge how
        // much will fit, which it cannot do without typesetting the slide.
        internal static String SplitStartersAcrossSlides => String.Join(' ',
            "This is a deck of slides, and a slide cannot run on to another page: whatever does not fit",
            "on it is off the board rather than further down. What you are adding takes a good deal more",
            "room than the English it is added to, so a starter that filled a slide in English will not",
            "fit on one now.",

            "So split it. Every slide that asks more than one question becomes three slides, sharing its",
            "questions as evenly between them as their number allows and keeping them in the order they",
            "are already in: six questions become three slides of two, five become two, two and one, and",
            "four become two, one and one.",
            "A slide asking two questions becomes two slides of one, and a slide asking a single question",
            "stays as it is - an empty slide is worse than a full one.",
            "A slide that asks nothing at all - a title page, a contents slide, a slide whose whole job is",
            "to list the answers - is left as the one slide it is.",

            "Moving a question must not change it. Every question appears on exactly one of the new",
            "slides, in the order it was already in, keeping the number it already had: question 5 is",
            "still numbered 5 on the slide it lands on, and is never renumbered to 1 for being the first",
            "question there. Do not drop a question, do not repeat one, and do not write a new one.",

            "A diagram, picture or table goes on the slide holding the question that refers to it. Where",
            "two questions that have ended up on different slides both refer to it, put a copy on each -",
            "a question whose diagram is on another slide cannot be answered.",
            "Anything on the slide that belongs to no particular question - an instruction to the class, a",
            "formula they are being given, a heading - goes on all of the new slides.",

            "Each new slide keeps the answers to the questions on it, revealed exactly the way they were",
            "revealed before, so that it still shows its questions first and their answers on its second",
            "overlay. Never leave an answer on a slide its question is not on.",

            "Title each new slide the way the original was titled, saying which of them it is and how many",
            "there are: a slide titled Starter 3 that becomes three slides gives Starter 3 (1 of 3),",
            "Starter 3 (2 of 3) and Starter 3 (3 of 3).",
            @"Anything the original slide carried for the rest of the deck to link to, such as a",
            @"\hypertarget, goes on the first of the new slides and is not repeated, so that a link still",
            "lands on the starter it names.");

        // What this kind of source needs saying beyond the rules every translated sheet
        // follows. Named on the archetype rather than chosen by a switch here, so that
        // adding a kind of source stays a matter of writing one file.
        private static String ArchetypeRules(SheetArchetype archetype) =>
            archetype.TranslatedSheetInstructions is String instructions
                ? instructions + " "
                : String.Empty;

        #endregion

        internal static String GenerateParallelTextPrompt(
            String source, IReadOnlyList<VocabTerm> terms,
            LanguageProfile language, L2ColourOptions colours,
            SheetArchetype archetype)
        {
            return "Below is the body of a mathematics .tex file. Produce a parallel text version of "
                + $"it for a pupil whose first language is {language.TitleName} and who is learning "
                + "mathematics in English. "
                + $"Above every question, instruction and piece of explanation, put a {language.TitleName} "
                + @"translation of it, using \ealpara{translation}{english}. "
                + "Translate the words, never the mathematics: numbers, algebra, equations and diagram "
                + "labels stay exactly as they are. "
                + "Keep every question, in the same order, with the same numbering, and do not change, "
                + "add or remove any mathematics. "
                + $"{VocabularyRules(terms, language)}"
                + $"Mark each of those words with \\ealkey in the English, and with \\ealkeytr where it "
                + $"appears in the {language.TitleName}. "
                + $"{L2ColourRules(colours)} "
                + $"{L2MacroRules} "
                + $"{L2LayoutRules} "
                + $"{ArchetypeRules(archetype)}"
                + $"{L2StructureRules}"
                + $"{SheetMacroRules(source)}"
                + "\n\n--- SOURCE ---\n\n" + L2Document.BodyOf(source);
        }

        internal static String GenerateTier3OnlyPrompt(
            String source, IReadOnlyList<VocabTerm> terms,
            LanguageProfile language, L2ColourOptions colours,
            SheetArchetype archetype)
        {
            return "Below is the body of a mathematics .tex file. Produce a version of it for a pupil "
                + $"whose first language is {language.TitleName}, whose English is good enough to read "
                + "the questions but who may not know the subject specific vocabulary. "
                + "Leave the English exactly as it is - do not translate the sentences. "
                + $"{VocabularyRules(terms, language)}"
                + @"Wherever one of those words appears in the English text, replace it with "
                + @"\ealgloss{english word}{translation}, which keeps the English word where it is and "
                + "puts the translation directly above it. "
                + "Do this at every occurrence, including inside question text and diagram labels, but "
                + "not inside mathematics. "
                + @"Any paragraph containing an \ealgloss must sit inside an ealglossed environment, so "
                + "the raised translations have room. "
                + $"{L2ColourRules(colours)} "
                + $"{L2MacroRules} "
                + $"{L2LayoutRules} "
                + $"{ArchetypeRules(archetype)}"
                + $"{L2StructureRules}"
                + $"{SheetMacroRules(source)}"
                + "\n\n--- SOURCE ---\n\n" + L2Document.BodyOf(source);
        }

        #endregion
    }
}
