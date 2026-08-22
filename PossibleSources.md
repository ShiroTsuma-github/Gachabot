# Candidate data sources

This file keeps research candidates that are not part of the trusted production registry. Implemented sources and their exact trust policy are documented in [docs/sources.md](docs/sources.md).

## Wuthering Waves candidates

- [Wuwa Tracker timeline](https://wuwatracker.com/pl/timeline) — community timeline; implemented as a `Trusted` event source.
- [Gengamer countdown](https://wuthering-countdown.gengamer.in/) — community countdown; useful as a comparison signal, not a source of truth.
- [Game8 event overview](https://game8.co/games/Wuthering-Waves/archives/453303) — community editorial content; keep `ReviewRequired` if implemented.

## Neverness to Everness candidates

- [Gengamer countdown](https://nte-countdown.gengamer.in/) — community countdown; not currently ingested.
- [Game8 overview](https://game8.co/games/Neverness-to-Everness/archives/592073) — community editorial content; implemented as a `Trusted` event source.
