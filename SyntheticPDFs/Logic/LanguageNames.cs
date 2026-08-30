namespace SyntheticPDFs.Logic
{
    // ISO 639-3 codes we are prepared to see in a filename, with the English name
    // that goes into the name of a translated file.
    //
    // Deliberately curated rather than the full registry. The registry has close to
    // 8000 codes, and enough of them collide with ordinary English words that using
    // it would reclassify real worksheets as translations - "abc" is Ambala Ayta and
    // "set" is Sentani, so latex/worksheets/foo_abc and sheet_set would both stop
    // being root files. A language only needs adding here when a sheet is actually
    // wanted in it; whether it can be typeset is a separate question, answered by
    // the language table in configuration.
    internal static class LanguageNames
    {
        internal const String English = "english";

        private static readonly Dictionary<String, String> ByCode =
            new(StringComparer.Ordinal)
            {
                ["eng"] = English,

                // the eagerly generated set - the most common first languages after
                // English in UK schools
                ["pol"] = "polish",
                ["urd"] = "urdu",
                ["pan"] = "punjabi",
                ["ben"] = "bengali",
                ["ara"] = "arabic",

                // available on request
                ["ces"] = "czech",
                ["cym"] = "welsh",
                ["deu"] = "german",
                ["fas"] = "persian",
                ["fra"] = "french",
                ["guj"] = "gujarati",
                ["hin"] = "hindi",
                ["ita"] = "italian",
                ["kur"] = "kurdish",
                ["lit"] = "lithuanian",
                ["nld"] = "dutch",
                ["pes"] = "farsi",
                ["por"] = "portuguese",
                ["ron"] = "romanian",
                ["rus"] = "russian",
                ["slk"] = "slovak",
                ["som"] = "somali",
                ["spa"] = "spanish",
                ["sqi"] = "albanian",
                ["tam"] = "tamil",
                ["tur"] = "turkish",
                ["ukr"] = "ukrainian",
                ["vie"] = "vietnamese",
                ["zho"] = "chinese",
            };

        internal static bool IsKnown(String code) => ByCode.ContainsKey(code);

        // lower case, since it is joined on to a camelCase filename
        internal static String? EnglishNameOf(String code) =>
            ByCode.TryGetValue(code, out String? name) ? name : null;

        internal static IReadOnlyCollection<String> AllCodes => ByCode.Keys;
    }
}
