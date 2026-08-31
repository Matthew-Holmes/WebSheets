# Files destined for the content repository

These are not part of any project here. They are files that belong in
[Matthews_Mathematics](https://github.com/Matthew-Holmes/Matthews_Mathematics),
kept alongside the code that reads them so the two can be changed together and so
there is something to copy from when setting an instance up.

The paths below this folder mirror the paths they take there, so copying is a
straight `cp` — see
[content-repo-translation-setup.md](../content-repo-translation-setup.md).

| Here | There |
| --- | --- |
| `latex/dictionary/mathematicalDictionary.tex` | `latex/dictionary/mathematicalDictionary.tex` |
| `latex/test/eal/` | `latex/test/eal/` |

## `latex/test/eal` — sample content for trying the site out

One worksheet with everything the pipeline can derive from it, so the browse
menus and the EAL page have something real to show before the generator has
worked its way round to producing any:

```
fractionsBasics.tex                     the sheet
fractionsBasics_workedSolutions.tex
fractionsBasics_solutions.tex
fractionsBasics_vocab.tex               the tier 3 vocabulary key
fractionsBasics/L2/pol/                 Polish: key, parallel text, key words only
fractionsBasics/L2/urd/                 Urdu: the same three, right to left
fontProbe.tex                           every configured language's font, loaded
```

Two languages rather than one so the language picker has something to pick
between, and Urdu specifically because it is right-to-left — the case most
likely to look wrong.

**These were produced by the generator's own renderers, not written by hand.**
That matters: a file whose provenance block does not match the current settings,
or whose definitions disagree with the shared dictionary, is treated as out of
date and removed on the next pass. Hand-written samples would have disappeared.
The definitions are copied from the dictionary for the same reason.

All ten compile with no errors and no missing glyphs. The two Noto fonts they
name are the ones the workflow installs, so they build in CI but not necessarily
on a machine without them.

## `fontProbe.tex` — proving the fonts are installed

The site offers around fifty languages, and each is set in a Noto family that
has to be present in the build container. A family that is not there is a hard
fontspec error that takes the whole build down — and it would otherwise surface
one language at a time, whenever somebody first asked for a sheet in it.

It also loads one more font that every translated file loads: the fallback the
script fonts borrow their Latin from. A name that does not resolve there breaks
every translation rather than one, so the probe fails on it outright.

The probe asks for every one of them by name before loading it, and prints one
letter of each language's own script in the font that language would be set in.
Push it once with the `extra_system_packages` change from
[content-repo-translation-setup.md](../content-repo-translation-setup.md) and
the run either passes, or names **every** family it could not find: one line in
the log per language as it is found, and one error at the end listing each
family once.

Asking first is the point. Loading a font that is not installed is a fatal
error, so a probe that simply used all fifty-odd would stop at the first one
missing and say nothing about the rest — which is the drip-feed it exists to
avoid. If you would rather it reported without failing the build, change
`\PackageError` at the end of the file to `\PackageWarning`.

**It has to print the script, not a full stop.** Several of Alpine's Noto script
packages carry their script and very little else. An earlier version of this
file printed `.` in every language; five of them had no full stop to print, and
the run then died — a font that is selected and then prints nothing leaves
LuaTeX subsetting an empty font, which is a fatal backend error rather than a
warning. A letter the font is certain to have avoids both.

### The Latin characters beside each script letter

Each language also prints a full stop, a digit and an em dash — the three Latin
characters a translated sheet mixes into its own script. Those are there because
six of the Noto script families (Bengali, Hebrew, Thai, Gujarati, Ethiopic and
the naskh Arabic) carry their script and **no Latin at all**, and LuaTeX drops a
character a font has not got rather than substituting one. A Bengali sheet came
out with holes where every full stop and every digit should have been, and the
build was green throughout.

Those three now come from a fallback font, declared at the top of the probe and
in every translated sheet. So on this page they are a live test of it: a line
that is missing one of them, or a `Missing character` line in the log naming
one, means the fallback is not reaching that language and its sheets are losing
their punctuation.

### The note at the foot of the page

Separately, each font is asked — silently, so it produces no warning — whether it
has those three characters **of its own**. The ones that do not are named at the
foot of the page and in the log. That does **not** fail the build: it is a fact
about the font, and the list is simply which languages are relying on the
fallback rather than a sign that anything is wrong.

### Regenerating it

It is generated from `SyntheticPDFs/appsettings.json` by `SyntheticPDFs.Tests`
and pinned by a test there, so adding a language fails the suite until the probe
is rewritten:

```bash
WEBSHEETS_WRITE_FONTPROBE=1 dotnet test SyntheticPDFs.Tests
```

A language in a script none of the configured fonts covers yet fails a different
test first: every font needs a sample letter in `FontProbe.Samples` before it can
be used, which is what stops the empty-subset crash coming back.

It is worth keeping in the content repository afterwards rather than deleting: it
is a few seconds of compile, and it turns a change to the build image into a
failure with a name on it rather than a mystery months later.

## The dictionary in each language is not here

`latex/dictionary/L2/<code>/mathematicalDictionary_<language>.tex` is written by
the generator once the English dictionary is committed, so there is nothing to
copy. It is worth knowing it exists, because it is where a bad translation is
corrected and because it is what stops each sheet paying to translate the same
words over again — see
[content-repo-translation-setup.md](../content-repo-translation-setup.md).

## What is not here yet

The generator will still fill in what is missing here — the other three eager
languages, and translations of the worked solutions and answers if anyone asks
for them. What is absent is deliberate: it leaves the EAL page with greyed-out
entries to click, which is the part of it worth testing.

## Which copy is authoritative

**The content repository is**, once the file has been committed there. That is
the copy the generator reads and the one people edit to change a wording, and it
is free to move ahead of this one.

What is here is a starting point: the format, and a first pass at the content
worth having on day one. It is checked by the test suite, which is what stops the
format drifting away from the parser that reads it — so if the convention changes,
change it here too.
