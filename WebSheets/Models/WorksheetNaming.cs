namespace WebSheets.Models
{
    /// <summary>
    /// Shared naming convention for generated worksheet files: each file name
    /// (before its extension) ends with a git-hash suffix - a 12 character hash
    /// plus the separating underscore - appended by the content-generation
    /// pipeline (see the SyntheticPDFs branch). This is the single place that
    /// knows how to strip that suffix, so callers don't re-implement it with
    /// their own magic numbers.
    /// </summary>
    public static class WorksheetNaming
    {
        public const int GitHashLength = 12;
        public const int HashSuffixLength = GitHashLength + 1; // plus the separating underscore

        /// <summary>
        /// Removes the trailing git-hash suffix from a name that has already
        /// had its extension removed. Returns the input unchanged if it isn't
        /// long enough to contain the suffix.
        /// </summary>
        public static string StripHashSuffix(string nameWithoutExtension)
        {
            if (string.IsNullOrEmpty(nameWithoutExtension) || nameWithoutExtension.Length <= HashSuffixLength)
                return nameWithoutExtension;

            return nameWithoutExtension[..^HashSuffixLength];
        }
    }
}
