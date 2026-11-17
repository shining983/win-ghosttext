# Third-party notices

## Embedded English frequency word list

File: `src/WinGhostText.Core/Data/english-freq.txt`

Derived from **[hermitdave/FrequencyWords](https://github.com/hermitdave/FrequencyWords)** `content/2018/en/en_full.txt`
(OpenSubtitles-based frequency data).

- Filtered alphabetic subset (ASCII `[a-z]`, length ≥ 2), top ~80k by source order.
- Source repo license: see upstream repository (historically treated as freely redistributable frequency data for NLP / spellcheck use).
- **Not** Oxford / Collins / New Oxford copyrighted dictionary text, definitions, or headword databases.

## Companion known-word allowlist

File: `src/WinGhostText.Core/Data/english-extra.txt`

Derived from **[dwyl/english-words](https://github.com/dwyl/english-words)** `words_alpha.txt` (**Unlicense**).

- ASCII `[a-z]` length 3–12, excluding words already present in `english-freq.txt`.
- Used only as a membership allowlist (do-not-flag), not as completion ranks.

## Proper nouns

File: `src/WinGhostText.Core/Data/proper-nouns.txt`

Small project-original compilation (PolyForm Noncommercial 1.0.0 with the rest of this repository).

## Common misspellings table

File: `src/WinGhostText.Core/Data/common-misspellings.txt`

A small hand-maintained list of frequent English typos → corrections (project original; PolyForm Noncommercial 1.0.0).

## .NET / WPF

Copyright Microsoft. Distributed under the .NET and Windows Desktop runtime licenses.

See also `ATTRIBUTION.md`.
