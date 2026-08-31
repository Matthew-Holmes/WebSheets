# What a vocabulary key looks like

Three PDFs to look at, compiled from the sample keys in
[../contentRepo/latex/test/eal/](../contentRepo/latex/test/eal/) — so they are
what `L2VocabKeyRenderer` actually writes, not a mock-up of it.

| File | |
| --- | --- |
| `englishKey.pdf` | `fractionsBasics_vocab.tex` |
| `polishKey.pdf` | the same key in Polish |
| `urduKey.pdf` | and in Urdu, right to left |

Each is three pages: the key, the match-up, and the answers to the match-up.

`*.pdf` is gitignored, so these are local. Regenerate them by compiling the
sample keys — see the bottom of this file.

> The standalone LaTeX these were designed in used to live here as well. It has
> gone: it was a second copy of the layout that had to be kept in step with the
> renderer, and once the layout moved into `L2VocabKeyRenderer` the better loop
> was to change the renderer, rewrite the sample keys through it, and compile
> those — which is what these PDFs are.

## What the layout is doing

**A dictionary list, not a table.** The word, a dotted leader across to the
meaning, then the meaning in a box of its own so a wrapped second line lines up
under the first rather than under the word. Ordered alphabetically by the
English word, which is what a key is looked up by.

**A word too long for its column takes a second line instead of the leader.**
The column is 40mm, and a term like "highest common factor" is wider than that.
It is measured first: one that fits is placed as before, and one that does not
is set over as many lines as it needs, breaking at its spaces. There is no
leader on a wrapped word — the dots are there to carry the eye across a gap, and
a word filling its column has not left one.

**In a translated key the left column is still only the English word.** The
translated word starts the meaning, an em dash between them — `ułamek — liczba
zapisana...` — so every translation begins at the same place down the page. Both
words are green, because a tier 3 word and its translation are the same word as
far as the sheet is concerned.

**A right-to-left key is the mirror image.** The word sits at the right margin,
the leader runs leftwards, and the meaning is flush right so it begins where the
leader ends. The match-up grid is mirrored with it — words on the right, where
an Urdu reader starts, meanings flush right in the column to their left, so that
each meaning ends against the gutter the answer lines cross.

**Copies are packed onto the page.** The body is measured and repeated with a
cut line between as many times as it fits: a five-term key is four or five to a
sheet. A body taller than a page is typeset directly instead, so it breaks
across pages rather than overflowing one.

**The answers are drawn as straight lines**, which works because the shuffle is
bounded — `L2VocabData.MaxDisplacement` keeps every meaning within five rows of
its word, so no line is steep enough to sweep across the others. They are the
last page, so a teacher can print the first two and keep the third.

Why each of those is written the way it is, and what it is protecting against,
is in [translated-sheet-latex.md](../translated-sheet-latex.md#the-vocabulary-key).

## Regenerating these

```bash
export PATH="/c/Users/Matt/AppData/Local/Programs/MiKTeX/miktex/bin/x64:$PATH"
lualatex --interaction=nonstopmode --enable-installer fractionsBasics_vocab.tex
```

CI installs the Noto fonts the language profiles name, and a generated key names
its font outright — fontspec stops rather than substituting, so a machine
without them cannot compile the translated keys at all. To look at one locally,
copy it and replace the font name with one that is installed. That changes the
Urdu from nastaliq to naskh, and nothing about the layout.
