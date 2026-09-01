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
| `RevealsItsOwnAnswers` | `false` | its answers live in the file itself, so the file has to be checked for the helpers that reveal them |
| `WorkedSolutionsInstructions` | `null` | its worked solutions are laid out unusually and the prompt has to say so |
| `Variants` | none | some school wants this kind of file worded their way |
| `FileNameFor` / `Parse` | the shared convention | it is not one-file-per-sheet, as the dictionary is not |
| `Plan` | the standard chain | its files are not created-and-replaced, as the dictionary's are not |

The last two are why the dictionary is a class rather than a special case: it is
one file for the whole repository, named `latex/dictionary/L2/pol/…` rather than
under a sheet, and its translations are **refreshed** rather than rebuilt.

## Variants

A variant is the same English file with something about it changed, made **from
the file** rather than written again. There is one so far: some schools have
their own name for a starter and expect to see it on the board, so a deck and
its worked solutions each get a version whose slide titles say
`Retrieve and Connect` instead.

```
latex/starters/KS3/circlesArea_retrieveAndConnect.tex
latex/starters/KS3/circlesArea_workedSolutions_retrieveAndConnect.tex
```

It is a `SheetForm` like any other, so naming, planning and staleness need no
special case: it is derived from the part it is a variant of, it is rebuilt when
that part is edited, and it is not translated. Adding one is a value in the enum,
a rewriter, and a line on the archetype that wants it.

**No model is asked.** `RetrieveAndConnect.Rewrite` edits the file's titles in
place, so every byte outside a title is the byte that was there before. That is
the whole point: a model handed a beamer deck to reproduce with one word changed
will also reflow a TikZ picture or lose an overlay specification somewhere in the
middle of it, and the failure is a slide that comes out wrong in a lesson rather
than a build that goes red. A model is asked only when no title in the file
mentions a starter at all, and if it cannot answer safely the variant is a copy —
never an error to retry forever.

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

### Except the English vocabulary key

One derived file is never thrown away. An English key carries the words it is
made of inside itself, in its data block, so a key that has fallen out of step —
the shared dictionary now words one of its terms differently, or the colours or
the layout have changed — is **stated again** rather than removed: the dictionary
is applied afresh and the file rendered again, by the same method that would have
written it from scratch.

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
