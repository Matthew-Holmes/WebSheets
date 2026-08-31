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
