-- ============================================================
-- ScotTrip — schema Supabase
-- Da eseguire una volta sola nel SQL Editor del progetto Supabase.
-- Idempotente: rilanciarlo non fa danni.
-- ============================================================

-- ---------- tabelle ----------
-- Nota di progetto: gli id sono generati dal client (offline-first),
-- updated_at governa i conflitti (last-write-wins),
-- deleted=true è una cancellazione "soft" che si propaga tra i telefoni.

create table if not exists public.ratings (
    id          uuid primary key,
    target_kind text        not null check (target_kind in ('Stop', 'Meal', 'Stay')),
    target_id   text        not null,
    rater       text        not null,
    category    text        not null default 'Generale'
                            check (category in ('Generale', 'Location', 'Prezzo', 'Qualita', 'Personale')),
    stars       int         not null check (stars between 1 and 5),
    note        text,
    deleted     boolean     not null default false,
    updated_at  timestamptz not null default now()
);

create table if not exists public.meals (
    id         uuid primary key,
    name       text        not null,
    place      text        not null default '',
    day_date   date        not null,
    meal_type  text        not null check (meal_type in ('Colazione', 'Pranzo', 'Cena', 'Spuntino')),
    cost       numeric(8, 2),
    dishes     text,
    note       text,
    deleted    boolean     not null default false,
    updated_at timestamptz not null default now()
);

create table if not exists public.stays (
    id         uuid primary key,
    name       text        not null,
    location   text        not null default '',
    check_in   date        not null,
    check_out  date        not null,
    note       text,
    deleted    boolean     not null default false,
    updated_at timestamptz not null default now()
);

create table if not exists public.photos (
    id           uuid primary key,
    target_kind  text        not null default 'Stop' check (target_kind in ('Stop', 'Meal', 'Stay')),
    target_id    text        not null,
    taken_at     timestamptz not null default now(),
    storage_path text,
    caption      text,
    author       text,
    deleted      boolean     not null default false,
    updated_at   timestamptz not null default now()
);

-- Un solo voto per persona per ogni cosa votata E per categoria
-- (l'upsert del client, con id deterministico, aggiorna quello esistente).
create unique index if not exists ratings_unique_vote
    on public.ratings (target_kind, target_id, rater, category);

create index if not exists photos_by_target on public.photos (target_kind, target_id);
create index if not exists meals_by_date on public.meals (day_date);

-- ---------- last-write-wins lato server ----------
-- Se arriva un upsert con updated_at PIÙ VECCHIO della riga esistente
-- (es. un telefono rimasto offline a lungo), la riga sul server vince.
create or replace function public.lww_guard()
returns trigger
language plpgsql
as $$
begin
    if tg_op = 'UPDATE' and new.updated_at <= old.updated_at then
        return old; -- ignora silenziosamente la scrittura più vecchia
    end if;
    return new;
end;
$$;

drop trigger if exists lww_ratings on public.ratings;
create trigger lww_ratings before update on public.ratings
    for each row execute function public.lww_guard();

drop trigger if exists lww_meals on public.meals;
create trigger lww_meals before update on public.meals
    for each row execute function public.lww_guard();

drop trigger if exists lww_stays on public.stays;
create trigger lww_stays before update on public.stays
    for each row execute function public.lww_guard();

drop trigger if exists lww_photos on public.photos;
create trigger lww_photos before update on public.photos
    for each row execute function public.lww_guard();

-- ---------- Row Level Security ----------
-- L'app è pubblicata su GitHub Pages, quindi la anon key è visibile a chiunque:
-- per questo TUTTO richiede un utente autenticato (i vostri due account).
alter table public.ratings enable row level security;
alter table public.meals   enable row level security;
alter table public.stays   enable row level security;
alter table public.photos  enable row level security;

drop policy if exists "coppia_ratings" on public.ratings;
create policy "coppia_ratings" on public.ratings
    for all to authenticated using (true) with check (true);

drop policy if exists "coppia_meals" on public.meals;
create policy "coppia_meals" on public.meals
    for all to authenticated using (true) with check (true);

drop policy if exists "coppia_stays" on public.stays;
create policy "coppia_stays" on public.stays
    for all to authenticated using (true) with check (true);

drop policy if exists "coppia_photos" on public.photos;
create policy "coppia_photos" on public.photos
    for all to authenticated using (true) with check (true);

-- ---------- Storage: bucket privato per le foto ----------
insert into storage.buckets (id, name, public)
values ('trip-photos', 'trip-photos', false)
on conflict (id) do nothing;

drop policy if exists "coppia_foto_lettura" on storage.objects;
create policy "coppia_foto_lettura" on storage.objects
    for select to authenticated using (bucket_id = 'trip-photos');

drop policy if exists "coppia_foto_scrittura" on storage.objects;
create policy "coppia_foto_scrittura" on storage.objects
    for insert to authenticated with check (bucket_id = 'trip-photos');

drop policy if exists "coppia_foto_aggiornamento" on storage.objects;
create policy "coppia_foto_aggiornamento" on storage.objects
    for update to authenticated using (bucket_id = 'trip-photos');

drop policy if exists "coppia_foto_cancellazione" on storage.objects;
create policy "coppia_foto_cancellazione" on storage.objects
    for delete to authenticated using (bucket_id = 'trip-photos');
