# Instrukcja operatora

## Uruchomienie

### Lokalnie, bez publikacji na Discord

```powershell
dotnet restore GachaBot.slnx --configfile NuGet.Config
dotnet run --project src/GachaBot.Web
```

Otwórz `http://localhost:8791`. Konfiguracja Development pozwala wejść bez logowania. Przed startem ustaw `DatabaseStorage__ConnectionString` do PostgreSQL. Automatyczne workery są lokalnie wyłączone; pierwszy skan uruchom ze strony **Źródła**. Aby testować polling w tle, ustaw `Workers__Enabled=true` w konfiguracji uruchomieniowej.

W Riderze wybierz profil **GachaBot.Web: Development**. Uruchomienie przez konfigurację **.NET Project → GachaBot.Web** pomija `launchSettings.json` i bez dodatkowych zmiennych startuje jak środowisko produkcyjne. Alternatywnie ustaw w tej konfiguracji `ASPNETCORE_ENVIRONMENT=Development`.

### Przez Docker Compose, z Discordem

```powershell
Copy-Item .env.example .env
docker compose up --build -d
```

Uzupełnij `.env` przed startem. Potrzebujesz tokenu bota, identyfikatora serwera i kanału, danych OAuth oraz identyfikatora co najmniej jednego administratora. Panel działa pod `http://localhost:8791`.

## Odświeżanie źródeł

Otwórz stronę **Źródła**.

- **Odśwież to źródło** uruchamia tylko adapter z danej karty.
- **Odśwież wszystkie** uruchamia adaptery po kolei.
- Wynik nad kartami podaje liczbę odczytanych pozycji. Po nieudanej próbie karta pokazuje czas i komunikat błędu.
- **Zobacz wyciągnięte treści** otwiera indeks z filtrem `source` w adresie URL.

Pierwszy udany odczyt tworzy baseline. Dla źródeł oficjalnych bot zapisuje treści w PostgreSQL, ale nie publikuje starego katalogu. Źródło `ReviewRequired`, takie jak Game8, od razu tworzy kandydaturę do ręcznej akceptacji, lecz nigdy nie publikuje jej automatycznie. Przy kolejnych odczytach bot porównuje tożsamość wpisu, tytuł i hash dokumentu. Zmiana tworzy rewizję.

## Podgląd danych i Discord

Otwórz **Treści**, a potem kliknij tytuł wpisu.

Strona szczegółów ma dwie kolumny:

- **Dane wyciągnięte ze źródła** pokazują bloki po parsowaniu: nagłówki, tekst, linki, obrazy, galerie, pola key/value i kod.
- **Podgląd Discord** używa tego samego kompozytora co publikator. Pokazuje każdą wiadomość i każdy rich embed: tytuł, opis, pola, kolor, obraz oraz stopkę.

Tytuł embedu jest linkiem do strony źródłowej. Nagłówki, tekst, linki i bloki kodu trafiają do opisu; bloki key/value stają się polami, a grafiki obrazami. Pierwsza grafika jest częścią głównego embedu, a pozostałe dostają własne embedy ze stopką z podpisu lub tekstu alternatywnego.

Linki do konkretnych filmów YouTube z `youtube.com`, `youtu.be` i iframe `youtube-nocookie.com` są normalizowane i deduplikowane. Bot wysyła taki adres jako zwykłe pole `content`, ponieważ aplikacje Discord nie mogą bezpośrednio ustawiać pola `video` rich embeda. Discord tworzy natywny podgląd filmu, jeżeli bot ma uprawnienie **Embed Links**, a podglądy linków nie są wyłączone po stronie serwera lub klienta. Dashboard pokazuje adres i oznaczenie natywnego podglądu; tytuł, miniatura i odtwarzacz pojawią się dopiero po przetworzeniu wiadomości przez Discord.

Kompozytor respektuje [oficjalne limity Discord](https://docs.discord.com/developers/resources/message#embed-limits): 256 znaków tytułu, 4096 opisu, 25 pól, 256 znaków nazwy pola, 1024 wartości pola, 2048 stopki, maksymalnie 10 embedów i 6000 znaków wszystkich embedów w jednej wiadomości. Zwykłe pole `content` ma limit 2000 znaków. Większy dokument jest dzielony na kilka wiadomości; bot zapisuje wszystkie ich ID, aby kolejna wersja mogła edytować, dodać albo usunąć odpowiednie części bez pozostawiania starych fragmentów.

Panel pokazuje stylizowaną symulację oraz rozwijany **Dokładny payload** w JSON. Discord może użyć innej czcionki lub szerokości, ale dane podglądu i publikacji pochodzą z jednego planu wiadomości.

Zwykły tekst pobrany ze strony nie jest traktowany jako Markdown. Znaki takie jak `**` w zamaskowanych identyfikatorach kont są escapowane przed publikacją, a granice paragrafów, `<br>` i wierszy tabeli pozostają zachowane. Pole **Data źródła** pokazuje datę publikacji odczytaną ze strony niezależnie od czasu importu i publikacji przez bota.

Nagłówki tabeli **Treści** są przyciskami sortowania. Dostępna jest również **Data źródła**, niezależna od czasu importu. Kolejne kliknięcie tej samej kolumny przełącza kierunek; aktywny kierunek pokazuje strzałka i atrybut dostępności `aria-sort`. Wpisy bez daty źródłowej pozostają na końcu w obu kierunkach.

## Zatwierdzanie treści

Źródła `Official` i `Trusted` mogą tworzyć automatyczne publikacje po baseline. Źródła `ReviewRequired`, w tym domyślne adaptery kodów Game8, zapisują kandydatów jako szkice do kontroli.

Na stronie szczegółów kliknij **Zatwierdź i publikuj**. System ustawi bieżący czas jako termin i doda rekord do transactional outbox. Worker pobierze rekord, wyśle wiadomość i zapisze identyfikator zwrócony przez Discord. Gdy agregat kodów zmieni się po kolejnych skanach, ponownie wymaga akceptacji, a publikator edytuje poprzednią wiadomość Discord zamiast tworzyć duplikat. Kody są renderowane jako bloki kodu, dla których klient Discord udostępnia kopiowanie. Data wygaśnięcia ma format `dd.MM.yyyy` oraz natywny znacznik względnego czasu Discord, więc opublikowana wiadomość pokazuje także, ile czasu zostało.

Game8 utrzymuje osobny wpis kodów bieżących i permanentnych. Jeśli sekcja bieżącej wersji jest pusta, bot nie uzupełnia jej kodami z ogólnej tabeli „All Active”; pusty agregat bieżący trafia do archiwum.
Przy aktualizacji ze starszej wersji wspólny rekord `active-codes` jest migrowany do agregatu permanentnego z zachowaniem `ContentId` i identyfikatora wiadomości Discord.

## Reset pojedynczego źródła

Każdy adapter ma własny schemat PostgreSQL, na przykład `official-neverness-to-everness` albo `game8-wuthering-waves-redeem-codes`. Aby wyczyścić tylko jedno źródło, zatrzymaj aplikację, wykonaj backup i wyczyść jego schemat na serwerze bazy. Po ponownym starcie aplikacja utworzy wymagane tabele, a ręczny refetch zbuduje nowy baseline.

## Wpis ręczny

Otwórz **Nowy wpis** i uzupełnij:

1. grę, typ i tytuł;
2. tekst oraz opcjonalny link i grafikę;
3. opcjonalną datę publikacji.

Pusta data zapisuje szkic. Data z formularza jest interpretowana w strefie `Europe/Warsaw`, a PostgreSQL przechowuje ją w UTC. Po zapisaniu otwórz wpis z indeksu, aby sprawdzić podgląd.

## Archiwum

Przycisk **Archiwizuj** przenosi wpis do archiwum, zapisuje powód `Manual` i anuluje oczekującą publikację. Ręczna decyzja operatora jest trwała: późniejszy refetch może odświeżyć dokument i jego rewizję, ale nie zmieni statusu ani nie utworzy publikacji. Worker oznacza wygasłe wpisy powodem `Expired`, a agregaty zastępujące wcześniejsze rekordy używają `Superseded`. Powód jest widoczny w indeksie i szczegółach treści.

Przy pierwszym uruchomieniu aplikacja tworzy wymagane schematy PostgreSQL. Istniejące wpisy zachowują zapisany powód archiwizacji.

## Diagnostyka

```powershell
docker compose ps
docker compose logs --tail 200 gachabot
```

Sprawdź też:

- `http://localhost:8791/health`, aby potwierdzić działanie hosta;
- stronę **Źródła**, aby odczytać ostatnią próbę i błąd adaptera;
- stronę **Treści**, aby sprawdzić status kolejki;
- wynik `/gachabot-status` oraz `/gachabot-configure`, jeśli publikacja nie trafia na kanał.

Brak publikacji po pierwszym skanie jest zgodny z mechanizmem baseline. Błąd jednego adaptera nie zatrzymuje pozostałych źródeł.

Archiwum mediów rozdziela limit wejściowy od wyjściowego. `MediaArchive__MaximumDownloadSizeMegabytes` (domyślnie 50 MiB) ogranicza plik pobierany ze strony. Następnie bot próbuje zoptymalizować obraz do `MediaArchive__MaximumStoredImageSizeMegabytes` (domyślnie 9 MiB) i `MediaArchive__MaximumImageDimension` (domyślnie 4096 px). Gdy bezpieczna kompresja nie jest możliwa, oryginał nadal trafia do archiwum, a tabela `MediaAssets` otrzymuje stan `Uncompressable`. Źródła oficjalne publikują URL CDN, natomiast pozostałe źródła używają lokalnego `attachment://...`, jeśli plik mieści się w limicie Discorda.

Istniejące kopie, również dla treści o statusie `Archived`, można odbudować bez resetowania bazy. Po zatrzymaniu bota `powershell -ExecutionPolicy Bypass -File .\scripts\Migrate-MediaArchive.ps1` pokazuje plan, a wariant z `-Apply` pobiera i kompresuje obrazy do nowej struktury. Parametr `-SourceKey` ogranicza migrację do jednego źródła. `-DeleteLegacy` jest opcjonalne i usuwa stary katalog haszowy tylko wtedy, gdy wszystkie bieżące grafiki wpisu zostały pomyślnie przeniesione.
