# Architektura

Projekt jest modularnym monolitem zgodnym z Clean Architecture.

```text
HTTP sources ──> adapters ──> ingestion coordinator ──> PostgreSQL schemas + media archive
                                      │
                                      ├── baseline / review policy
                                      └── transactional outbox ──> Discord REST

Blazor dashboard ──> application ports ──> composite store ──> PostgreSQL
```

Zależności kodu biegną do środka: Domain nie zna żadnej warstwy zewnętrznej, Application zna tylko Domain, Infrastructure implementuje porty Application, a Web jest composition rootem. Test architektoniczny pilnuje tych granic.

## Dokument blokowy

Treść nie jest pojedynczym polem embed. `ContentDocument` przechowuje uporządkowane bloki z pozycją i walidacją:

- `HeadingBlock`, `TextBlock`, `LinkBlock`;
- `ImageBlock`, `GalleryBlock` z tekstem alternatywnym;
- `KeyValueBlock`;
- `CodeBlock`.

Canonical JSON bloku jest hashowany SHA-256. Dodanie następnego rodzaju wymaga implementacji serializacji i renderera, ale nie zmiany tabeli treści. PostgreSQL przechowuje dokument JSON i jego hash, a poprzednie wersje w `ContentRevisions`.

## Izolacja źródeł

Każdy adapter źródła ma osobny schemat PostgreSQL nazwany kluczem źródła; wpisy ręczne używają schematu `manual`. Composite store kieruje zapis do właściwego schematu, a odczyty dashboardu i kolejka publikacji agregują wszystkie schematy. Treść, rewizje, stan źródła i transactional outbox danego adaptera pozostają razem, dzięki czemu pojedynczy zapis nadal jest atomowy.

Adapter najpierw pobiera stan wszystkich identyfikatorów źródła w porcjach do 500, zamiast wykonywać `Exists` i odczyt dokumentu dla każdego elementu. Koordynator przekazuje zmiany do zapisu porcjami po 50. Pierwszy baseline korzysta z parametryzowanych wielowierszowych insertów, a kolejne niezmienione skany kończą się na ograniczonej liczbie odczytów. Indeks `(SourceKey, ExternalId)` jest tworzony również dla istniejących plików bazy.

## Publikacja

Utworzenie lub zmiana zaufanej treści zapisuje `PublicationRecord` w tej samej bazie co treść. Worker lease’uje rekord, przekłada neutralne bloki domenowe na plan Discord rich embedów, publikuje przez Discord REST, zapisuje message ID albo ustawia retry z opóźnieniem wykładniczym. Kompozytor dzieli duże dokumenty według limitów API: 10 embedów i 6000 znaków na wiadomość, 4096 znaków opisu i 25 pól na embed. Po ośmiu próbach rekord przechodzi w `Failed`.

Jeżeli publikacja zajmuje kilka wiadomości, `ProviderMessageId` przechowuje uporządkowaną listę snowflake ID. Aktualizacja edytuje istniejące części, tworzy brakujące i usuwa nadmiarowe. Dashboard preview mapuje ten sam plan na neutralne DTO, więc nie ma osobnej logiki formatującej. Konkretne adresy filmów YouTube są kanonizowane przez wspólną politykę linków i trafiają do `content`, aby Discord mógł utworzyć natywny video/link embed; pole `video` nie jest ustawiane przez aplikację.

Jest to gwarancja at-least-once: awaria procesu po wysłaniu do Discorda, lecz przed zapisem receipt, może spowodować powtórzenie. Discord nie udostępnia klucza idempotencji dla zwykłego `SendMessage`; zapis `ProviderMessageId` ogranicza powtórzenia po poprawnym receipt.

## Czas i archiwum

Warstwa aplikacji operuje w UTC. Dashboard przyjmuje i pokazuje czas Europe/Warsaw. PostgreSQL przechowuje znaczniki czasu z informacją o strefie i ma indeksy kolejki/statusu.

Wygasłe wpisy są oznaczane `Archived`, a oczekujące publikacje anulowane. Obrazy są archiwizowane oddzielnie na filesystemie według SHA-256; URL musi być HTTPS, DNS nie może wskazywać sieci prywatnej, typ MIME musi być obrazem, a rozmiar mieścić się w konfigurowalnym limicie (domyślnie 20 MiB). Świadome odrzucenie zbyt dużego pliku nie cofa zapisu treści i ma osobny komunikat informacyjny bez stosu wyjątku.
