# Preparing the content repository for translated sheets

Two changes are needed in
[Matthews_Mathematics](https://github.com/Matthew-Holmes/Matthews_Mathematics)
before any translated (EAL) sheet will build. Neither belongs in this
repository, and neither can be made from here.

**Do not make these edits in `SyntheticPDFs/Matthews_Mathematics/`.** That
directory is a gitignored working clone, and the generator **deletes and
re-clones it on startup**, so anything changed there is lost. Clone the content
repository somewhere of its own:

```bash
git clone git@github.com:Matthew-Holmes/Matthews_Mathematics.git
cd Matthews_Mathematics
git switch -c translated-sheet-fonts
```

Work on a branch. Pushing to `main` starts a build that recompiles and
re-uploads PDFs, so merge it when you are ready for that rather than as a side
effect of committing.

---

## 1. Give the compiler its fonts

### Why the obvious approach does not work

`xu-cheng/latex-action` runs the compile **inside a Docker container** it builds
itself, with only the workspace mounted in. A separate `apt-get` step in the
workflow installs on the runner host, outside that container, where the
compiler never sees it. Worse, it fails quietly: a missing font produces
`Missing character` lines in the log and a PDF full of blanks, not an error.

The action has two inputs that reach inside the container. Use those.

### The edit

In `.github/workflows/build-latex.yml`, find the LuaLaTeX compile step:

```yaml
      - name: Compile LaTeX files from list (LuaLaTeX)
        if: steps.read-files.outputs.lualatex_files != ''
        uses: xu-cheng/latex-action@v4
        with:
          root_file: ${{ steps.read-files.outputs.lualatex_files }}
          work_in_root_file_dir: true
          latexmk_use_lualatex: true
```

and add the last three lines:

```yaml
      - name: Compile LaTeX files from list (LuaLaTeX)
        if: steps.read-files.outputs.lualatex_files != ''
        uses: xu-cheng/latex-action@v4
        with:
          root_file: ${{ steps.read-files.outputs.lualatex_files }}
          work_in_root_file_dir: true
          latexmk_use_lualatex: true
          extra_system_packages: >-
            font-noto font-noto-all font-noto-cjk
          extra_fonts: ./fonts/*.ttf
```

Those are `apk` package names, because the action's `os` input defaults to
`alpine`. Debian names such as `fonts-noto-core` will not resolve.

**`font-noto-all` rather than a list of scripts.** Alpine packages Noto one
script at a time — `font-noto-arabic`, `font-noto-bengali`,
`font-noto-devanagari` and so on — and naming them individually means this list
has to be kept in step with `L2:Languages` in the generator's configuration.
It does not fail loudly when it drifts: fontspec stops with *"The font ... cannot
be found"* the first time somebody asks for a language whose script was left
out, and it takes the whole build down with it. There are fifty-odd languages
configured across eighteen font families, so `font-noto-all` is both shorter and
the only version that cannot go stale. `font-noto-cjk` is named as well because
it is packaged separately and is what Chinese, Japanese and Korean need.

Only the LuaLaTeX step needs this. Every generated translation carries
`% !TeX program = lualatex`, so the classifier always routes it here. If a
hand-written translation ever needs XeLaTeX, add the same two inputs to that
step as well.

`extra_fonts` is harmless while `fonts/` is empty, so it can go in now.

### Checking it took

`latex/test/eal/fontProbe.tex` asks for the font of every configured language
and prints one letter of that language's script in it, so one run says whether
the container has them all. Push it with this change: it either passes, or names
every family it could not find — a line in the log per language, then one error
listing each family once. Without it, a missing script stays hidden until the
first person asks for a sheet in that language, and then breaks the whole build.

Each language also prints a full stop, a digit and an em dash. Six of the Noto
script packages carry their script and no Latin at all, and LuaTeX drops a
character a font has not got rather than substituting one, so those three come
from a fallback font instead — and printing them here is what proves the
fallback is working. A `Missing character` line naming one of them means it is
not, and that every translated sheet in that language is losing its punctuation.

The same run also reports, **without** failing, which fonts have none of those
three of their own. That list is which languages are relying on the fallback,
rather than a sign that anything is wrong; see
[contentRepo/README.md](contentRepo/README.md).

---

## 2. Get the nastaliq font for Urdu

Alpine has no nastaliq package. `font-noto-arabic` will render Urdu, but in
**naskh** — legible, and not the style Urdu readers expect. Urdu is normally set
in nastaliq, and to a reader the difference is closer to typeface versus
handwriting than to a font preference.

The font has to be committed to the repository, which is what `extra_fonts`
above picks up.

### Download it

```bash
mkdir -p fonts
cd fonts

curl -LO https://raw.githubusercontent.com/notofonts/notofonts.github.io/main/fonts/NotoNastaliqUrdu/hinted/ttf/NotoNastaliqUrdu-Regular.ttf
curl -LO https://raw.githubusercontent.com/notofonts/notofonts.github.io/main/fonts/NotoNastaliqUrdu/hinted/ttf/NotoNastaliqUrdu-Bold.ttf

# the licence has to travel with the font
curl -L -o OFL.txt https://raw.githubusercontent.com/notofonts/notofonts.github.io/main/LICENSE

cd ..
```

Check you got fonts and not HTML error pages before committing — a redirect or
a 404 saved under the right name is an easy mistake to miss:

```bash
file fonts/*.ttf     # each should say "TrueType Font data"
ls -l  fonts/        # each should be some hundreds of kB, not a few hundred bytes
```

### Licensing

Noto fonts are under the **SIL Open Font License 1.1**, which permits
redistribution, including inside another repository. The one condition that
matters here is that the licence text accompanies the font — hence `OFL.txt`
above. Do not rename the font files: OFL forbids redistribution under a
reserved name, and keeping the original names avoids the question entirely.

Any font that is **not** Noto needs its licence checked before it goes in.

### Commit

```bash
git add fonts .github/workflows/build-latex.yml
git commit -m "add fonts for translated sheets"
git push -u origin translated-sheet-fonts
```

---

## 3. Add the shared dictionary

The definitions used across every worksheet live in the content repository rather
than in the generator's settings, so a wording that reads badly to a teacher can
be changed by a commit that can be discussed. The same file compiles to a
dictionary worth having on its own.

Copy it in from this repository:

```bash
mkdir -p latex/dictionary
cp <path-to-WebSheets>/docs/contentRepo/latex/dictionary/mathematicalDictionary.tex \
   latex/dictionary/
```

Once it is committed there, **that** copy is the authoritative one — it is what the
generator reads and what people edit to change a wording. The copy under
`docs/contentRepo` is a starting point, kept so the format stays pinned by the
test suite; see [contentRepo/README.md](contentRepo/README.md).

It builds under pdfLaTeX like any other sheet, and the generator never derives a
worksheet's worth of files from it — no worked solutions, no answer key. What it
does derive is a translation of the dictionary itself, in each language; see the
next section.

### Editing it

One entry per line:

```latex
\dictentry{numerator}{the number above the line in a fraction}
\dictentry[vertices]{vertex}{a corner where two or more edges meet}
```

Write the definition as you would say it to a pupil who has not met the word:
plain words, one sentence, no symbols, and never the word itself inside its own
definition.

Other forms are found automatically — `numerators`, `Numerator`, `simplifying`
and `vertices` all reach their headword — so the optional argument is only for
forms no rule would reach, such as an abbreviation.

Changing a wording rebuilds the vocabulary keys that use **that word**, and only
those, on the next run. A key whose words the edit did not touch is left alone,
however large the edit was. If the file is absent the generator carries on and
the model's own wording stands, so it can be added at any point.

## 4. The dictionary in each language

Once the English dictionary is there, the generator writes a translation of it
for each language it is producing sheets in:

```
latex/dictionary/mathematicalDictionary.tex          written by you
latex/dictionary/L2/pol/mathematicalDictionary_polish.tex   written by the server
latex/dictionary/L2/urd/mathematicalDictionary_urdu.tex
```

Nothing needs setting up for this. It is worth understanding, though, because it
is where most of the cost of translation goes and where a bad translation is
best corrected.

### Why it exists

A word is translated **once**, in this file, and every vocabulary key in that
language then takes the translation from here rather than asking the model
again. A repository whose dictionary covers its vocabulary produces its
translated keys for nothing at all.

It also means the same word reads the same way on every sheet a pupil is given,
which is the part that matters in a classroom.

### Correcting a translation

If a translation reads wrongly to somebody who speaks the language, fix it in
this file. The next run rebuilds the keys that use that word — and only those —
and does it **without asking a model anything**, because the corrected wording is
read straight out of the dictionary.

Each entry has four arguments:

```latex
\dictentrytr{numerator}{the number above the line in a fraction}%
            {licznik}{liczba nad kreską ułamka}
```

The last two are yours to correct. The first two are the English word and the
English wording the translation was made from, and are how the server knows the
translation is still current — change the English **in the shared dictionary**,
not here. The file says all of this in a comment at the top of itself, which is
where somebody editing it will actually look.

### Keeping it in step

Rewording an English definition retranslates that one word. Removing a word from
the shared dictionary removes its translation, and costs nothing. Adding words
is done a few dozen at a time, so a first run against a full dictionary takes
several passes rather than one enormous request.

### If somebody breaks one

An entry the parser cannot read — a missing brace, usually — is reported rather
than guessed at. The entries either side of it keep working, and the whole
dictionary does not stop being applied.

`/ping` answers **422** rather than 200 while any such problem stands, and names
the file and what is wrong with it, so the workflow that pings after a push
fails instead of leaving the breakage to be noticed months later. It describes
the last completed pass, so a problem introduced by one push is surfaced by the
next announcement.

## 5. Check it worked

The fonts change nothing until a translated sheet exists, so the first real
check is the first build that compiles one. When that runs, open the LuaLaTeX
step's log in Actions and look for:

- **`Missing character` lines.** Any at all means a font did not apply. The
  build will still be green and the PDF will have blanks in it, so this is the
  thing to look for, not the exit code.
- **the font list**, printed near the end of the step. Every font named in
  `L2:Languages` in `SyntheticPDFs/appsettings.json` should appear. A font
  that is not listed was never applied, whatever else the log says.

A package existing does not prove fontconfig exposes the family name the
language profile asks for, so this first run is the real confirmation of the
names in `appsettings.json`.

If a script fails, work through the symptom table in
[translated-sheet-latex.md](translated-sheet-latex.md#diagnosing-a-new-language-that-fails-in-ci),
which distinguishes a missing font from a missing face, missing shaping and
missing bidi — they look similar in the log and have different fixes.

## Rolling back

Both changes are additive: the two action inputs and a new `fonts/` directory.
Nothing existing depends on either, so reverting the commit restores the
previous behaviour exactly, and English sheets are unaffected throughout.
