namespace SyntheticPDFs.Services
{
    // a review answer. the reasons matter as much as the verdict - they are what gets logged
    // when a deck is rejected, and what a person eventually reads on the note slide
    public record ReviewVerdict
    {
        public required bool Passed { get; init; }

        // empty when it passed
        public required String Reasons { get; init; }
    }
}
