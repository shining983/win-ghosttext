# Attribution

WinGhostText ships only **redistributable** English word data — no Collins, Oxford, or other copyrighted dictionary text.

## english-freq.txt

- **Source:** [hermitdave/FrequencyWords](https://github.com/hermitdave/FrequencyWords) `content/2018/en/en_full.txt` (OpenSubtitles-based frequency lists).
- **Use:** Top ~80k alphabetic (`[a-z]`, length ≥ 2) tokens, re-ranked by source order for Trie completion and spelling edit-distance candidates.
- **License notes:** Upstream frequency data is widely treated as freely redistributable for NLP / spellcheck; see the upstream repository. This project does **not** claim ownership of the underlying subtitle-derived counts.

## english-extra.txt

- **Source:** [dwyl/english-words](https://github.com/dwyl/english-words) `words_alpha.txt`.
- **Use:** Companion **membership-only** allowlist (~40k additional ASCII words, length 3–12) so uncommon but real words are less often flagged as typos. These entries are **not** used as completion frequency ranks or as the primary edit-distance pool.
- **License:** [Unlicense](https://github.com/dwyl/english-words/blob/master/LICENSE) (public domain dedication).

## proper-nouns.txt

- Small project-original list of common days/months, places, brands, and given names (canonical Title Case).
- License: same as this repository ([PolyForm Noncommercial 1.0.0](LICENSE)).

## common-misspellings.txt

- Hand-maintained typo → correction table (project original; same as this repository — PolyForm Noncommercial 1.0.0).

## Grammar

Local grammar is **rule-level** (a/an, light agreement / double modal, capitalization). It is **not** a full proofreading engine and does not call any cloud grammar API.
