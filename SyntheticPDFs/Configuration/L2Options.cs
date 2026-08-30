namespace SyntheticPDFs.Configuration
{
    // an RGB colour, written out in full rather than hashed, so that the provenance
    // block in a generated file is readable by whoever opens it and still comparable
    // by the generator
    public class RgbColourOptions
    {
        public int R { get; set; }
        public int G { get; set; }
        public int B { get; set; }

        // what a person calls it, for the provenance block - "green", "purple"
        public string Name { get; set; } = "";
    }

    public class L2ColourOptions
    {
        // tier 3 vocabulary, in English and in translation alike
        public RgbColourOptions Tier3 { get; set; } = new() { R = 0, G = 128, B = 0, Name = "green" };

        public RgbColourOptions English { get; set; } = new() { R = 0, G = 0, B = 0, Name = "black" };

        public RgbColourOptions Translation { get; set; } = new() { R = 112, G = 48, B = 160, Name = "purple" };
    }

    // what a language needs before it can be typeset at all. the ISO code and the
    // English name come from LanguageNames; this is the half that decides whether
    // the result compiles
    public class LanguageOptions
    {
        // font family name, resolved by fontspec. must exist in the CI container -
        // see docs/translated-sheet-latex.md
        public string Font { get; set; } = "";

        // babel's name for the language, passed to \babelprovide and \foreignlanguage
        public string BabelName { get; set; } = "";

        // right to left scripts need bidi=basic and a flush-right paragraph
        public bool RightToLeft { get; set; }
    }

    public class L2Options
    {
        public const string SectionName = "L2";

        public L2ColourOptions Colours { get; set; } = new();

        // keyed by ISO 639-3 code. a language absent from here cannot be generated,
        // whatever LanguageNames says about naming it
        public Dictionary<string, LanguageOptions> Languages { get; set; } = new();

        // generated for every root without being asked. deliberately roots only -
        // worked solutions and answer keys are generated on request
        public List<string> EagerLanguages { get; set; } = new();

        // definitions shared across worksheets, so that the same word is explained
        // the same way everywhere. a vocabulary key whose definition of a shared term
        // no longer matches this is stale, which is how an edit here propagates
        public Dictionary<string, string> Glossary { get; set; } = new();
    }
}
