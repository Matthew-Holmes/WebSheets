namespace SyntheticPDFs.Models.Content
{
    // A three letter ISO 639-3 code. This used to be an enum, which cannot carry the
    // 8000 codes the standard defines, nor the font and direction each one needs to be
    // typeset - both of those come from LanguageNames and the configured language table
    // instead.
    internal readonly record struct ISO639_3Code
    {
        internal static readonly ISO639_3Code eng = new("eng");

        internal String Code { get; }

        internal ISO639_3Code(String code)
        {
            Code = code;
        }

        public override String ToString() => Code;
    }
}
