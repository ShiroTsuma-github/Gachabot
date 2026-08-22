# Source regression fixtures

Snapshoty w tym katalogu pochodzą z rzeczywistych odpowiedzi serwisów i są używane wyłącznie przez testy offline parserów. Dzięki temu zmiana selektora albo formatu danych nie jest maskowana przez ręcznie uproszczony HTML.

Stan pobrania: 2026-08-13 (Europe/Warsaw).

| Plik | Oryginalny URL | Uwagi |
|---|---|---|
| `wuthering-waves/article-menu-3657.json` | `https://hw-media-cdn-mingchao.kurogame.com/akiwebsite/website2.0/json/G152/en/ArticleMenu.json` | rzeczywisty rekord wycięty z dużej listy; pokazuje ucięte `articleContent` |
| `wuthering-waves/article-3657.json` | `https://hw-media-cdn-mingchao.kurogame.com/akiwebsite/website2.0/json/G152/en/article/3657.json` | pełna odpowiedź detalu |
| `neverness-to-everness/index.html` | `https://nte.perfectworld.com/en/article/news/index.html` | pełny HTML pierwszej strony Latest |
| `neverness-to-everness/index1.html` | `https://nte.perfectworld.com/en/article/news/index1.html` | pełny HTML drugiej strony Latest |
| `neverness-to-everness/article-263505.html` | `https://nte.perfectworld.com/en/article/news/gamebroad/20260810/263505.html` | pełny HTML detalu |
| `neverness-to-everness/article-262479.html` | `https://nte.perfectworld.com/en/article/news/gamebroad/20260602/262479.html` | pełny HTML detalu z datą `yyyy.MM.dd` w `.articleDate` |
| `neverness-to-everness/article-261969.html` | `https://nte.perfectworld.com/en/article/news/gamebroad/20260428/261969.html` | pełny HTML detalu z poprawnymi obrazami CDN i technicznym duplikatem `docs.oa.wanmei.net` |
| `neverness-to-everness/article-257051-video.html` | `https://nte.perfectworld.com/en/article/news/gamenews/20250625/257051.html` | rzeczywisty fragment detalu z produkcyjnym iframe YouTube |
| `game8/nte-rendered-fragment.html` | `https://game8.co/games/Neverness-to-Everness/archives/593718` | fragment DOM po przejściu wyzwania JS; selektor i wartość potwierdzone w lokalnym Chrome |
| `game8/wuwa-rendered-fragment.html` | `https://game8.co/games/Wuthering-Waves/archives/453149` | fragment DOM tabel skróconej i szczegółowej; obejmuje nagrody, datę tekstową i kod bez daty |

Fixture’y nie są pobierane podczas `dotnet test`. Ich świadome odświeżenie powinno być osobnym review, ponieważ zmiana snapshotu może wymagać zmiany reguł w `source-definitions.json`.

Dynamiczny kontrakt Game8 można dodatkowo sprawdzić na żądanie. Test ładuje dokładnie produkcyjny `source-definitions.json`, uruchamia Chromium i dlatego nie jest częścią domyślnego przebiegu offline:

```powershell
dotnet test tests/GachaBot.Infrastructure.IntegrationTests/GachaBot.Infrastructure.IntegrationTests.csproj --configuration Release --no-restore -- --explicit only --filter-class 'GachaBot.Infrastructure.IntegrationTests.Game8LiveContractTests'
```
