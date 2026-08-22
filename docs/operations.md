# Operacje i wdrożenie

## Docker Compose

```powershell
Copy-Item .env.example .env
# uzupełnij sekrety w .env
docker compose up --build -d
docker compose logs -f gachabot
```

Panel działa na porcie `8791`, a `/health` zwraca stan hosta. Stan aplikacji znajduje się w PostgreSQL, media w skonfigurowanym buckecie S3, a klucze ochrony sesji w wolumenie `gachabot-data` pod `/app/data`.

## Backup

Przed zmianą wersji wykonaj backup PostgreSQL, bucketa S3 i wolumenu z kluczami ochrony sesji. Najprostsza bezpieczna procedura dla pojedynczej instancji:

1. `docker compose stop gachabot`;
2. wykonaj spójny backup bazy PostgreSQL i bucketa S3 oraz skopiuj wolumen `/app/data`;
3. `docker compose start gachabot`;
4. okresowo przetestuj odtworzenie kopii na osobnym wolumenie.

Każde źródło ma własny schemat PostgreSQL; wpisy ręczne używają schematu `manual`. Reset pojedynczego źródła wykonuj przez kontrolowaną operację na jego schemacie, po zatrzymaniu aplikacji i wykonaniu backupu.

## Aktualizacja

```powershell
docker compose build --pull
docker compose up -d
```

Pierwsza wersja tworzy schemat przez EF Core `EnsureCreated`. Przed wdrożeniem wersji zmieniającej model danych backup jest obowiązkowy; kolejnym krokiem ewolucji projektu powinno być przejście na numerowane migracje EF Core przed pierwszą niekompatybilną zmianą schematu.

## Diagnostyka

- `/sources`: baseline, ręczne odświeżenie pojedynczego adaptera, ostatni sukces i ostatni błąd;
- `/content/{id}`: zapisane bloki oraz podgląd wiadomości i grafik dla Discord;
- log `Source polling completed`: zbiorczy wynik skanu;
- tabela `Publications`: `Pending`, `Processing`, `Published`, `Failed`, `Cancelled`;
- brak postów po pierwszym starcie jest prawidłowy — pierwszy skan tylko buduje baseline;
- Game8 403 jest oczekiwanym trybem degradacji; oficjalne źródła działają niezależnie.

## Sekrety i dostęp

Produkcja nie startuje bez skonfigurowanego Discord OAuth, jeśli anonimowy dostęp deweloperski jest wyłączony. Administrator serwera konfiguruje bota przez komendy `gachabot-*` i wymaga uprawnienia **Manage Server**. Cookie jest HttpOnly, ma SameSite=Lax i ośmiogodzinny czas życia. Token bota, OAuth secret, baza i klucze data protection nie mogą trafić do repozytorium ani logów.
