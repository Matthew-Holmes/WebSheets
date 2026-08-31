# LaTeX for translated sheets

How the EAL / L2 sheets are typeset, why each constraint exists, and how to work
out what is wrong when a new language will not build.

Everything below was established by compiling, not by reading documentation.
Several of these failures are silent — the file builds, produces a PDF, and is
simply wrong — so the checks in [Reading the log](#reading-the-log) matter more
than the exit code.

## The engine is LuaLaTeX, always

Every generated L2 file starts with

```latex
% !TeX program = lualatex
```

The CI classifier (`005_classifyCompileEngine.sh` in the content repository)
reads that magic comment **before** anything else, so it decides the engine
outright. Two consequences:

- A generated L2 file must never inherit the magic comment of the English source
  it was built from. A sheet pinned to `pdflatex` would take its translation
  down with it, because pdfLaTeX cannot typeset any of these scripts.
- The generator writes this line itself. It is not something the model is asked
  to reproduce.

LuaLaTeX rather than XeLaTeX because:

- babel's `bidi=basic` coexists with `array`, `booktabs`, `tikz` and inline
  maths, which the sheets use freely.
- HarfBuzz shaping (`Renderer=Harfbuzz`) renders Indic conjuncts correctly.
  Bengali `ভগ্নাংশ` and `ক্ষেত্রফল` come out right; without it they do not.
- One engine for every language means one preamble shape, one set of checks and
  one thing to debug.

## The preamble the generator writes

The language-specific half is built from the language profile in configuration
and is never written by the model:

```latex
\directlua{%
  if luaotfload and luaotfload.add_fallback then
    luaotfload.add_fallback("eallatinfallback",
      { "Noto Serif:mode=harf;" })
  else
    tex.error("this luaotfload is too old to register a fallback font")
  end
}
\usepackage[english,bidi=basic]{babel}   % bidi=basic only when the language is RTL
\babelprovide[import]{urdu}
\babelfont[urdu]{rm}[Renderer=Harfbuzz,
  BoldFont={Arial}, ItalicFont={Arial}, BoldItalicFont={Arial},
  RawFeature={fallback=eallatinfallback}]{Arial}
\babelfont[urdu]{sf}[Renderer=Harfbuzz,
  BoldFont={Arial}, ItalicFont={Arial}, BoldItalicFont={Arial},
  RawFeature={fallback=eallatinfallback}]{Arial}
\newcommand{\ealtext}[1]{\foreignlanguage{urdu}{#1}}
\newcommand{\ealtextblock}[1]{{\raggedleft\foreignlanguage{urdu}{#1}\par}}
```

`\ealtextblock` uses `\raggedleft` for a right-to-left language and
`\raggedright` for a left-to-right one. That is the only difference between an
RTL and an LTR profile, besides the `bidi=basic` option.

### Six rules this preamble encodes

Each one is a real failure that was hit and fixed.

**`english` goes in the package options.** Writing
`\babelprovide[import,main]{english}` instead looks equivalent and is not: babel
reports *"You haven't specified a language as a class or package option. I'll
load 'nil'"*, falls back to `nil`, and the language font never switches. The
document still builds. It just has 189 missing glyphs in it.

**Attach the font to both `rm` and `sf`.** beamer sets sans, `article` sets
roman. A font attached to only one family leaves the other silently unshaped —
the give-away is that only `lmsans*.otf` appears in the log's font list.

**Name every face explicitly.** Many script fonts have no bold italic. When
fontspec cannot resolve one face it fails for the *whole family*, so the
language gets no font at all: *"Could not resolve font 'Nirmala UI/BI'"*.
Pointing all four faces at the same file is deliberate — a vocabulary gloss has
no need of real bold or italic.

**Never override the sheet's English font.** The sheet already chose one. The
preamble only ever *adds* a font for the new language.

**Right-to-left needs a paragraph-level wrapper.** `\foreignlanguage` inside an
otherwise left-to-right paragraph sets the run correctly but leaves the
paragraph flush left. Urdu and Arabic want flush right, which is what
`\ealtextblock` is for.

**A script font needs somewhere to borrow Latin from.** Several of the Noto
script families carry their script and essentially nothing else. Noto Sans
Bengali, Noto Sans Hebrew, Noto Sans Thai, Noto Sans Gujarati, Noto Sans
Ethiopic and Noto Naskh Arabic have **no full stop, no digits and no em dash**.
LuaTeX drops a character a font has not got rather than substituting one, and
the build stays green while it does it — so a Bengali sheet came out with holes
where every full stop, every digit and the em dash between a word and its
meaning should have been.

`luaotfload.add_fallback` registers a second font to be asked for anything the
first has not got, and `RawFeature={fallback=...}` attaches it. The script font
is still asked first, so nothing about how the script itself is shaped changes.
The family is `L2:FallbackFont` in configuration, and defaults to Noto Serif —
the same family the English half of a parallel text is set in.

Two things about it are worth knowing:

- **It works with `Renderer=Harfbuzz`.** That is not obvious, and was checked
  rather than assumed: a base font with no CJK, a fallback that has it, and the
  glyph appears with no `Missing character` line.
- **A fallback that was never registered is fatal, not a no-op.** Naming one
  that does not exist makes the *font itself* unloadable — fontspec reports
  *"metric data not found or bad"* and every translated file in the build dies.
  So the declaration stops with a readable `tex.error` on a luaotfload too old
  to have `add_fallback` (before 3.13, so before TeX Live 2021), rather than
  carrying on and failing later as a font error nobody can trace back to here.

`\iffontchar` still reports what a font has **of its own** — it does not see
what the fallback lends it — which is what `fontProbe.tex` uses to list the
languages that are relying on the fallback.

## The macros

Defined in `L2Macros`, pasted into every generated L2 file, and the only things
the model is asked to use in the body:

| Macro | For |
| --- | --- |
| `\ealkey{word}` | a tier 3 English word, wherever it appears |
| `\ealkeytr{word}` | the same word in the translation, also green, so the eye links the pair |
| `\ealgloss{english}{translation}` | one translated word sitting above one English word |
| `\ealpara{translation}{english}` | a whole translated sentence above its English counterpart |
| `ealglossed` | environment opening up line spacing around a block of glosses |

### Two rules the macros encode

**Names are letters only, and prefixed `eal`.** A TeX control sequence cannot
contain a digit, so `\l2text` parses as `\l` followed by the characters `2text`
— silently redefining the barred-l used by Polish. The `eal` prefix stops a
collision with a macro the sheet already defines.

**`\ealgloss` uses kernel primitives, never `tabular`.** Sheets load `array`,
`booktabs` and `tabularx` as they please, and a `tabular`-based gloss breaks
under `array` with *"Misplaced \noalign"* and a cascade of grouping errors.
`\sbox`, `\makebox` and `\raisebox` are immune.

The gloss box is as wide as the **wider** of the two words, so a long
translation pushes its neighbours aside instead of overlapping them, and it
claims its **full height**, so a table row or a TikZ node makes room for it.
Inside `ealglossed` the leading is fixed at 2.1× — about 30pt against the ~21pt
a gloss needs — so claiming that height costs nothing in running text.

## The vocabulary key

The key is the one generated file whose LaTeX is written entirely by
`L2VocabKeyRenderer` rather than by a model, so its layout lives there and not
in `L2Macros` — a parallel text sheet has no use for any of it, and pasting it
into every generated file would make restyling the key rebuild all of them.
That is also why the key records its own `key layout version` in the provenance
block, separate from `layout macros version`: only the keys go stale when the
key is restyled.

Three pages: the key, the match-up, and the answers to the match-up, in that
order so a teacher can print the first two. See
[glossaryStyles/](glossaryStyles/) for what they look like.

**`\ealpage` packs copies onto the page.** The body is typeset into a box so its
height can be measured, then repeated with a cut line between as many times as
it fits. `\ealfits` is a `\count` taking a dimension, which is how TeX coerces
scaled points to an integer for the division; `\divide` truncates, where
`\numexpr` would round up and overflow the page. A body taller than the page is
typeset directly instead, so it breaks across pages rather than overflowing one.
The body is therefore typeset twice — once to be measured and thrown away — so
nothing in it may have a side effect that would differ between copies.

**The match-up is a TikZ matrix, not a `tabular`.** The answer page has to draw
a line from each word to its meaning, and both pages have to agree on where the
rows are. Nothing uses `remember picture`, so the grid is placed in one pass and
a packed copy of it is simply drawn again — which is what makes packing and the
answer lines coexist.

The cell separator is `\ealnextcol` rather than the usual `\&`. A definition is
allowed to contain an ampersand, which `Escape` writes as `\&`, and that would
otherwise split the cell in half.

**Straight answer lines depend on the shuffle being bounded.**
`L2VocabData.MaxDisplacement` keeps every definition within five rows of its
word. Curves, or lines routed through lanes, were both tried at fifteen terms
and neither is readable; bounding the shuffle bounds the slope, and straight
lines then read cleanly. A definition left beside its own word is swapped out
afterwards — it would be a free answer, and draws a horizontal line saying so.

### Two rules a right-to-left key encodes

**The whole key is mirrored, not just the text alignment.** Setting the meaning
flush right is necessary and is not enough: with the word still on the left, the
dotted leader runs out in the middle of the line and the meaning starts
somewhere else entirely, and on the match-up an answer line stops at the far
side of the column with a gap before the text it points at. So for an RTL
profile the word goes to the right margin with the leader running leftwards, and
the match-up grid has its columns swapped — which also swaps which edge each
answer line is drawn from, `2.west -- 1.east` rather than `1.east -- 2.west`.
Both ends then meet the text, because both columns end against the gutter.

The translated word, the em dash and the definition go inside a **single**
`\ealtext`. Set as separate runs they are ordered left to right by the
surrounding paragraph, which lands the word at the far end of the line where it
is read last.

**The meaning needs a `\strut`.** A translated meaning opens with a language
switch and a colour, and both put a whatsit into the box ahead of its first
line. A `\parbox[t]` whose first item is not a box takes its height as zero, so
the whole meaning drops about half a line below the word it belongs to — on
every row, in every translated key, and not at all in an English one. Starting
the paragraph in horizontal mode puts them back on the same line. It is worth
roughly one extra copy per page, since the offset was costing a line an entry.

## Fonts in CI

> For the actual steps to take in the content repository, see
> [content-repo-translation-setup.md](content-repo-translation-setup.md). This
> section is the reasoning behind them.

The TeX Live image the content repository builds with does not carry fonts for
most of these scripts, so they have to be added. **Adding a separate
`apt-get` step to the workflow does not work**, and it is worth knowing why
before reaching for one.

`xu-cheng/latex-action` is a composite action, but it runs the compile inside a
Docker container it builds from its `os` and `texlive_version` inputs. Only the
workspace is mounted in. Anything installed on the runner host lands outside
that container and the compiler never sees it — the build fails exactly as if
nothing had been installed.

The action has two inputs for this, and one of them is the right answer.

### `extra_system_packages`, for scripts Alpine packages

The default `os` is `alpine`, so these are `apk` names, not Debian ones:

```yaml
- uses: xu-cheng/latex-action@v4
  with:
    root_file: ${{ steps.read-files.outputs.lualatex_files }}
    work_in_root_file_dir: true
    latexmk_use_lualatex: true
    extra_system_packages: >-
      font-noto font-noto-arabic font-noto-bengali
      font-noto-gurmukhi font-noto-devanagari
```

That covers Polish, Arabic, Bengali and Punjabi.

### `extra_fonts`, for anything Alpine does not package

Alpine has **no nastaliq package**. Urdu set with `font-noto-arabic` renders
correctly but in naskh, which is not the style Urdu readers expect. For that,
commit the font file to the content repository and point the action at it:

```yaml
    extra_fonts: ./fonts/*.ttf
```

Noto fonts are licensed under the SIL Open Font License, which permits
redistribution, so committing `NotoNastaliqUrdu-Regular.ttf` is legitimate.
Check the licence before committing any font that is not Noto.

`extra_fonts` is also the deterministic option in general: a committed font
cannot be renamed or dropped by a distribution update, and it works whichever
base OS the image uses.

### Font names still need one CI run to confirm

A package existing does not prove fontconfig exposes the family name the
language profile asks for. The names in `appsettings.json` are the expected
ones, but the first CI run against a new script is the check — a missing font
gives `Missing character` lines rather than an error, so read the log rather
than the exit code.

## Testing locally

A local TeX install with `lualatex` is enough; nothing here needs the content
repository or the generator running. On this machine MiKTeX provides it:

```bash
export PATH="/c/Users/Matt/AppData/Local/Programs/MiKTeX/miktex/bin/x64:$PATH"
lualatex --interaction=nonstopmode --enable-installer probe.tex
```

`--enable-installer` lets MiKTeX fetch packages it does not have rather than
stopping to ask. On a TeX Live machine drop that flag.

Local fonts will not be the CI fonts. That is fine for checking *layout and
shaping* — which is what these probes are for — but it means a probe passing
locally does not prove CI has the font. Those are separate questions, and the
font one is answered by the workflow step above.

### Reading the log

A zero exit code proves very little here. Check all four:

```bash
grep -c "Missing character" probe.log   # font does not cover the script
grep -c "^! "               probe.log   # hard errors
grep -c "Overfull"          probe.log   # content running off the page
grep -oiE "[a-z0-9_ -]+\.(ttf|otf)" probe.log | sort -u   # which fonts were really used
```

That last one is the most useful single check. If the font you named is not in
the list, it was never applied, whatever else the log says.

## Diagnosing a new language that fails in CI

Work through these in order. Each one distinguishes a different cause, and the
first three are all silent failures.

| Symptom | Cause | Fix |
| --- | --- | --- |
| Many `Missing character` lines, font list lacks your font | the font never applied | check `english` is a package option, and that `\babelfont` was set for **both** `rm` and `sf` |
| `Could not resolve font "X/BI"` | the font has no bold italic | name all four faces explicitly |
| Font list shows only `lmsans*` on a slide deck | font attached to `rm` only | add the `sf` line |
| Glyphs appear but in the wrong order, or punctuation at the wrong end | bidi not enabled | add `bidi=basic` and mark the language RTL in its profile |
| Conjuncts render as separate letters (Indic) | shaping not applied | add `Renderer=Harfbuzz` |
| `Misplaced \noalign`, cascading grouping errors | a `tabular`-based construct meeting `array` | use the kernel-primitive macros; do not reintroduce `tabular` |
| Glosses collide with table rules or a diagram | a raised box claiming no height | `\raisebox` must not be given `[0pt][0pt]` |
| Everything builds locally, fails in CI | the font is not in the container | add it via `extra_system_packages` or `extra_fonts` on the action - **not** a separate `apt-get` step, which installs outside the container |

### The probe

Copy this, substitute the language, and compile it. It exercises every construct
the generator emits, in the contexts that have actually broken: `array` and
`booktabs` loaded, inline maths inside a translated run, a gloss wider than its
English word, a gloss inside a table cell, and a gloss inside a TikZ node.

```latex
% !TeX program = lualatex
\documentclass[12pt]{article}
\usepackage[a4paper,margin=1in]{geometry}
\usepackage{amsmath,amssymb}
\usepackage{array}
\usepackage{booktabs}
\usepackage{tikz}

% ---- substitute the language, font and direction ----
\usepackage[english,bidi=basic]{babel}
\babelprovide[import]{urdu}
\babelfont[urdu]{rm}[Renderer=Harfbuzz,
  BoldFont={Arial}, ItalicFont={Arial}, BoldItalicFont={Arial}]{Arial}
\babelfont[urdu]{sf}[Renderer=Harfbuzz,
  BoldFont={Arial}, ItalicFont={Arial}, BoldItalicFont={Arial}]{Arial}
\newcommand{\ealtext}[1]{\foreignlanguage{urdu}{#1}}
\newcommand{\ealtextblock}[1]{{\raggedleft\foreignlanguage{urdu}{#1}\par}}
% -----------------------------------------------------

\input{macros.tex}   % the block L2Macros emits

\begin{document}
\pagenumbering{gobble}

\section*{Parallel text, with maths inside the translated run}
\ealpara{اس شکل کا \ealkeytr{رقبہ} معلوم کریں: $A = 3 \times 4$۔}%
        {Find the \ealkey{area} of this shape: $A = 3 \times 4$.}

\section*{Glosses, with array and booktabs loaded}
\begin{ealglossed}
Find the \ealgloss{numerator}{شمار کنندہ} and the \ealgloss{denominator}{مخرج}
of each \ealgloss{fraction}{کسر}, then simplify.
\end{ealglossed}

\section*{A gloss wider than its English word}
\begin{ealglossed}
The \ealgloss{area}{رقبہ اور احاطہ کی پیمائش} of the shape, and more text after it.
\end{ealglossed}

\section*{A gloss in a table cell}
\begin{tabular}{@{}ll@{}}
\toprule
Term & Meaning \\
\midrule
\ealgloss{numerator}{شمار کنندہ} & the number above the line \\
\bottomrule
\end{tabular}

\section*{A gloss in a TikZ node}
\begin{tikzpicture}
  \draw (0,0) rectangle (4,2);
  \node at (2,-0.6) {\ealgloss{width}{چوڑائی}};
\end{tikzpicture}

\end{document}
```

Run the same probe with `\documentclass{beamer}`, the body split across frames,
before declaring a language working. Beamer is where the `rm`-versus-`sf`
failure shows up, and it is silent on `article`.

Then **look at the PDF**. Three of the faults above produce a clean log and a
wrong page: colliding glosses, a flush-left RTL paragraph, and unshaped
conjuncts all compile perfectly happily.
