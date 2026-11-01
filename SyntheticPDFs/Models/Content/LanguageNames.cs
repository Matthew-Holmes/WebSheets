namespace SyntheticPDFs.Models.Content
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
    //
    // The list below is roughly the languages a UK school is most likely to need,
    // taken as far as the fifty or so that come up in practice. It is not a ranking
    // and the order does not matter: the site sorts by name, and only the languages
    // in L2:EagerLanguages are generated without being asked for.
    //
    // One collision is worth knowing about in a mathematics repository: "sin" is
    // Sinhala. A sheet called something_sin.tex is still treated as an English root,
    // but it logs a warning saying so on every pass.
    //
    // One code here is not ISO: "zht". ISO 639-3 has a language code for Chinese and
    // nothing for how it is written, so "zho" means simplified characters, which is
    // what every file already under it uses, and "zht" is our own for traditional.
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
                ["ron"] = "romanian",

                // ---- available on request ----

                // Latin script
                ["ces"] = "czech",
                ["cym"] = "welsh",
                ["deu"] = "german",
                ["est"] = "estonian",
                ["fra"] = "french",
                ["hau"] = "hausa",
                ["hrv"] = "croatian",
                ["hun"] = "hungarian",
                ["ibo"] = "igbo",
                ["ind"] = "indonesian",
                ["ita"] = "italian",
                ["kur"] = "kurdish",
                ["lav"] = "latvian",
                ["lit"] = "lithuanian",
                ["nld"] = "dutch",
                ["por"] = "portuguese",
                ["slk"] = "slovak",
                ["sna"] = "shona",
                ["som"] = "somali",
                ["spa"] = "spanish",
                ["sqi"] = "albanian",
                ["swa"] = "swahili",
                ["tgl"] = "tagalog",
                ["tur"] = "turkish",
                ["twi"] = "twi",
                ["vie"] = "vietnamese",
                ["yor"] = "yoruba",
                ["zul"] = "zulu",

                // Cyrillic and Greek
                ["bul"] = "bulgarian",
                ["ell"] = "greek",
                ["rus"] = "russian",
                ["srp"] = "serbian",
                ["ukr"] = "ukrainian",

                // right to left
                ["fas"] = "persian",
                ["heb"] = "hebrew",
                ["pes"] = "farsi",
                ["pus"] = "pashto",

                // Indic
                ["guj"] = "gujarati",
                ["hin"] = "hindi",
                ["kan"] = "kannada",
                ["mal"] = "malayalam",
                ["mar"] = "marathi",
                ["nep"] = "nepali",
                ["sin"] = "sinhala",
                ["tam"] = "tamil",
                ["tel"] = "telugu",

                // everything else
                ["amh"] = "amharic",
                ["jpn"] = "japanese",
                ["kor"] = "korean",
                ["tha"] = "thai",
                ["tir"] = "tigrinya",
                ["zho"] = "chinese",
                ["zht"] = "chinesetraditional",
            };

        // the few languages whose filename name does not read properly once capitalised
        private static readonly Dictionary<String, String> TitleNames =
            new(StringComparer.Ordinal)
            {
                ["zho"] = "Chinese (Simplified)",
                ["zht"] = "Chinese (Traditional)",
            };

        // as a reader would say it - on the site, in a title and in a prompt to the model
        internal static String TitleNameOf(String code, String englishName)
        {
            if (TitleNames.TryGetValue(code, out String? name)) { return name; }

            return englishName.Length == 0
                ? englishName
                : Char.ToUpperInvariant(englishName[0]) + englishName[1..];
        }

        internal static bool IsKnown(String code) => ByCode.ContainsKey(code);

        // lower case, since it is joined on to a camelCase filename
        internal static String? EnglishNameOf(String code) =>
            ByCode.TryGetValue(code, out String? name) ? name : null;

        internal static IReadOnlyCollection<String> AllCodes => ByCode.Keys;
    }
}
