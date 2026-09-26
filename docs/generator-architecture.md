# How the generator is put together

The generator does one thing on a loop: read the content repository, work out
what is missing or out of date, make one round of it, and commit. This describes
the layers that answer the middle question, and how to extend them.

## The layers

```
Git/            git, and nothing but git
  └ RepoModel               a list of paths, each with an age in commits

Models/Content/ what those paths mean
  └ ContentModel            sheets and dictionaries, keyed by root name
    └ SheetState            one sheet: which files exist, and which are stale
      └ SheetArchetype      what kind of thing it is, and everything that follows

Logic/          decisions
  └ Orchestrator            what to do about it

Rendering/      LaTeX
  └ L2Macros, renderers, prompts, dictionaries
```

The important boundary is between the first two. `RepoModel` exists to abstract
git and knows nothing else — a path and how many commits ago it was last
touched. `ContentModel` wraps it and attaches the semantics unpicked from the
names: which sheet a file belongs to, which part of it it is, which language, and
what kind of sheet it is.

Everything above that reasons about sheets. The orchestrator never takes a
filename apart to decide anything, and `RepoModel` never learns what a worksheet
is.

Building the model reads no files at all — it is string work over a list of
paths. Only the second half, judging what is stale, opens anything, and only the
few files that record what they were built from.

## The three axes

A file belonging to a sheet is identified by a `ContentKey`, which is three
independent things:

| Axis | Values |
| --- | --- |
| `Language` | `eng`, `pol`, `urd`, … (ISO 639-3) |
| `SheetPart` | `Root`, `WorkedSolutions`, `Solutions` |
| `SheetForm` | `Original`, `Glossary`, `TranslatedGlossary`, `ParallelText`, `Tier3Only`, `RetrieveAndConnect` |

None implies the others: "the Polish parallel text of the worked solutions" is a
language, a part and a form.

## Archetypes

An archetype is what kind of source a file is, and it is read off the **folder**:
anything under `latex/starters/` is a deck of question slides however it is
named. Each is a class in `Models/Content/Archetypes/`:

| Class | Folder | Has |
| --- | --- | --- |
| `Worksheet` | `worksheets` | root, worked solutions, answers |
| `QuestionSlides` | `starters` | root, worked solutions — the deck reveals its own answers |
| `TeachingSlides` | `slides` | root only — a deck that explains rather than asks |
| `QuickQuestions` | `quickQuestions` | root only — each question's second slide already shows its solution |
| `Poster` | `cheatSheets` | root only |
| `MathematicalDictionary` | `dictionary` | the shared definitions, and a translation of them per language |

They are found by reflection at startup rather than listed in a register, so
adding one is a matter of adding its file. Two archetypes claiming the same
folder is refused at startup rather than resolved by load order.

Each is a singleton, so the archetype carried on a piece of metadata compares by
reference and can sit inside a record without spoiling its equality.

### Adding one

Write a class in `Models/Content/Archetypes/` deriving from `SheetArchetype`. The
three abstract members are `Description`, `Folder` and `Parts`; everything else
has a sensible default:

```csharp
internal sealed class Investigation : SheetArchetype
{
    internal override String Description => "an investigation";

    internal override String Folder => "investigations";

    internal override IReadOnlyList<SheetPart> Parts { get; } = new[]
    {
        SheetPart.Root,
        SheetPart.WorkedSolutions,
    };
}
```

That is enough for the whole pipeline to handle it: naming, staleness, the plan,
the glossary and every translated form. The virtual members worth knowing about:

| Member | Default | Override when |
| --- | --- | --- |
| `HasGlossary` | `true` | it *is* a glossary, as the dictionary is |
| `TranslatedEagerly` | `false` | its glossary and translated sheet are worth paying for before anyone asks — true only of worksheets and starters |
| `RevealsItsOwnAnswers` | `false` | its answers live in the file itself, so the file has to be checked for the helpers that reveal them |
| `WorkedSolutionsInstructions` | `null` | its worked solutions are laid out unusually and the prompt has to say so |
| `TranslatedSheetInstructions` | `null` | translating it needs something said that the shared rules do not cover |
| `Variants` | none | some school wants this kind of file worded their way, or wants it on paper |
| `FileNameFor` / `Parse` | the shared convention | it is not one-file-per-sheet, as the dictionary is not |
| `Plan` | the standard chain | its files are not created-and-replaced, as the dictionary's are not |

The last two are why the dictionary is a class rather than a special case: it is
one file for the whole repository, named `latex/dictionary/L2/pol/…` rather than
under a sheet, and its translations are **refreshed** rather than rebuilt.

### What a kind of source says about its own translations

Every translated sheet follows the same rules — the helpers, the colours, the
fixed vocabulary, the body-only structure. `TranslatedSheetInstructions` is where
an archetype adds what those rules cannot cover, and both translated forms get
it: the parallel text and the tier 3 only version.

A deck of starters is the one that needs it. Both forms put a great deal more on
the page than the English did — a whole second language in one, a translation
raised above every subject word in the other — and the general answer to that is
to let the sheet run on to another page. A slide cannot: whatever will not fit on
it is off the board rather than below it. So a deck is told to **split each
starter across three slides**, sharing its questions between them as evenly as
their number allows, keeping each question's own number, and putting a diagram on
whichever of the three holds the question that refers to it. Three is a rule of
thumb — roughly what the extra text costs — and a rule that is followed the same
way every time is worth more here than asking a model to judge how much will fit,
which it cannot do without typesetting the slide.

Those rules live in a prompt, so nothing in the finished file can be read to tell
whether they were followed. What is recorded instead is which rules the file was
made under: a translated file whose archetype asks for something of its own
carries a `sheet layout version` line in its provenance block, and a deck
translated before the split has none, so it is out of date and is made again. It
is versioned apart from `layout macros version` for the same reason the key
layout is — changing what a deck needs should not rebuild every translated
worksheet to come back identical.

## Variants

A variant is the same English file with something about it changed, made **from
the file** rather than written again. There are two so far, both on a deck of
starters.

**Retrieve and Connect.** Some schools have their own name for a starter and
expect to see it on the board, so a deck and its worked solutions each get a
version whose slide titles say `Retrieve and Connect` instead.

**For printing.** A deck is written to be projected, which is no use to a pupil
who was away or who cannot see the board. The printable version shows every slide
as it stands before any answer is revealed, four copies of each on a page of A4
in a two by two grid, so a printed page cuts into four question sheets. Only the
questions get one: the worked solutions are a deck to talk through rather than a
sheet to hand out. The retitled deck gets its own, since a school that renames
its starters prints them under that name.

Every slide is repeated, the title page included, so **one page is one slide** —
page 5 of the pdf is slide 5 of the deck, and printing two starters means asking
for two pages. That is why nothing is dropped for being furniture rather than a
question: leaving a slide out would be harmless, but letting one reach the page
without being repeated puts every page after it half a starter out of step. Both
ways beamer lets a deck open a slide have to be found for that to hold —
`\begin{frame}` and the `\frame{...}` command every template uses for its title
page.

```
latex/starters/KS3/circlesArea_retrieveAndConnect.tex
latex/starters/KS3/circlesArea_workedSolutions_retrieveAndConnect.tex
latex/starters/KS3/circlesArea_forPrinting.tex
latex/starters/KS3/circlesArea_retrieveAndConnect_forPrinting.tex
```

Each is a `SheetForm` like any other, so naming, planning and staleness need no
special case: it is derived from one file, it is rebuilt when that file is edited,
and it is not translated. What it is derived from, and which parts of a sheet it
exists for, are stated on a `SheetVariant` beside the form — which is what lets
the printable version of the retitled deck be made from the retitled deck rather
than from the deck. Adding a variant is a value in the enum, a rewriter, and a
line on the archetype that wants it.

**No model is asked.** `RetrieveAndConnect.Rewrite` edits the file's titles in
place and `ForPrinting.Render` adds a step to each `egin{frame}` and repeats it,
so in both cases every byte outside the change is the byte that was there before.
That is the whole point: a model handed a beamer deck to reproduce with one word
changed will also reflow a TikZ picture or lose an overlay specification somewhere
in the middle of it, and the failure is a slide that comes out wrong in a lesson
rather than a build that goes red. A model is asked only for the retitling, and
only when no title in the file mentions a starter at all; the printable version
has no such route, and if there are no slides it can lay out the result is a copy
— never an error to retry forever.

The printable version leans on beamer's own `egin{frame}<1>`, which asks for the
first step of a frame and nothing after it. That is why it does not matter how a
deck hides its answers: an answer helper, a `\pause` or a picture that draws itself
in has not happened yet on the first step, whichever it is.

## Plans, and what stale means

An archetype's `Plan` says which files a sheet may have and what each is derived
from. Both staleness and batch selection read off it, so an archetype rule is
stated once rather than in a staleness check and three selection methods that
have to agree.

A file is stale when:

- it is older than something it was derived from (arithmetic on commit ages), or
- something it was derived from has gone, or
- something it was derived from is stale — staleness is transitive, so worked
  solutions older than the root take the answer key with them, or
- it records having been built from settings that have since changed, or
- the plan does not allow it at all: a poster given worked solutions before
  posters stopped getting them, or a translation into a language no longer
  configured.

Stale files are removed, and the pass ends there. The next pass rebuilds what was
eager and leaves the rest — a file made on request is maintained only while it
lasts.

### Except a vocabulary key

A vocabulary key is not thrown away. A key carries the words it is made of inside
itself, in its data block, so one that has fallen out of step — a dictionary now
words one of its terms differently, or the colours or the layout have changed — is
**stated again** rather than removed: the dictionary is applied afresh and the file
rendered again, by the same method that would have written it from scratch.

That is not merely a saving. Rebuilding a key means asking a model for the
sheet's vocabulary a second time, and a model asked twice picks a different set of
words. Those new words go into the shared dictionary, which puts the next set of
keys out of step, which rebuilds those, which adds more words. It is a loop rather
than a settling, and it was one: the content repository churned through rounds of
`Update/Add …_vocab.tex` and `removed stale files: …_vocab.tex`, paying for each
one. Restating fixes the word list, so the same repository settles in four passes
and no model is called at all.

A key with no readable data block is the exception to the exception — there is
nothing to state it again from, so that one is rebuilt like anything else.

The same holds in every language. A translated key carries the English word, the
English definition, the word a teacher in that language would use and the
definition translated, so it is stated again from the dictionary in its own
language, and a word that dictionary does not cover keeps the translation the file
already had. Correcting a translation, or changing a colour, no longer throws away
every translated key in the repository and buys the uncovered words back.

**One case stays a rebuild**: a translated key older than the English key it was
made from. Then the words themselves may have changed, so there is a genuine
translation to buy, and a translation of wording the sheet no longer shows would
be wrong to keep. It also has to stay a rebuild for a duller reason. Both of the
reasons that *are* restated guarantee the file comes out different; being merely
older does not, and a restatement that landed on the bytes already there would be
a commit git had nothing to record — leaving the file exactly as stale as it was
and asking again every pass, for ever. Staleness that only age can see is left to
the walk over the plan, which removes and rebuilds, and which resets the age.

## Immutability

`ContentModel`, `SheetState`, `SourceMetadata`, `ContentFile` and `PlannedFile`
are all records, changed with `with` rather than in place. That is not
decoration: `SheetState` arrives from `ContentModel` knowing only which files
exist and `Judged()` returns the same state with the plan applied, which is what
lets the cheap half (reading names) be separated from the half that has to open
files.

The same idea does most of the work in the naming layer, where nearly every use
is "the same file but the worked solutions", or "the same file but in English":

```csharp
String wsol = (metadata with { Part = SheetPart.WorkedSolutions }).FilePath;
```

## What it costs

The model is rebuilt from scratch on every pass, and every sheet is judged
against a plan with an entry per language per form. That is a lot of small
objects, so it is measured rather than assumed —
`ContentModelTests.BuildingAndJudgingTheWholeRepositoryIsFastEnoughToDoEveryPass`
builds and judges 2000 sheets against 50 languages and prints the time. It runs
in about a second, against a pass that spends tens of seconds waiting on a model,
so the readable version costs nothing worth having back.

The test's own budget is deliberately loose. It is there to catch an accident
that makes this quadratic, not to police milliseconds.
