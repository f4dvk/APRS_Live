# APRS_Live

Logiciel APRS de bureau, multiplateforme (Linux, Windows x64, Windows x86) :
connexion TNC KISS (série ou TCP) ou TNC audio (modem AFSK1200 maison, sans
dépendance externe), possibilité de connecter 2 TNC simultanément, carte
hors-ligne (tuiles `.mbtiles` raster) ou en ligne (OpenStreetMap), traces des
stations filtrables par fenêtre de temps, onglet Messages avec gestion des
accusés de réception, onglet Couverture (portée moyenne et maximale sur 24 h
glissantes de votre station, d'après ce que vos TNC reçoivent en direct, et des
digipeaters choisis ; un clic sur une ligne liste les stations reçues en
direct, avec l'heure de dernière réception ; en option, les liaisons entre
digis lues dans le chemin des trames), thème clair/sombre sélectionnable.

Développé en .NET 8 + [Avalonia](https://avaloniaui.net/) (même socle que le
projet [PCT_Report](https://github.com/f4dvk/PCT_Report), dont la couche
KISS/AX.25/APRS est réutilisée et généralisée ici) et [Mapsui](https://mapsui.com/)
pour la carte.

## État du projet

Le projet est développé par phases (voir le détail dans les notes de
conception). **Phases 0 et 1 faites.** L'application a trois onglets — Carte
(en ligne ou un `.mbtiles` chargé manuellement), Station (indicatif, icône
APRS sélectionnable, chemin, destination, texte de balise) et Paramètres
(thème clair/sombre/système) — et la couche protocolaire (KISS série/TCP,
AX.25, décodage APRS) est portée et testée dans `AprsLive.Core`. Rien n'est
encore branché bout en bout : pas de connexion TNC active depuis l'UI, pas de
traces, pas de messagerie.

Prochaines phases : gestion double-TNC et traces de stations, messagerie avec
accusés de réception, E/S audio et modem AFSK1200.

## Compiler et lancer

```sh
dotnet restore
dotnet build
dotnet run --project src/AprsLive.App
```

## Tests

```sh
dotnet test
```

## Publier un exécutable autonome

```sh
./publish.sh              # les trois cibles (linux-x64, win-x64, win-x86)
./publish.sh win-x86      # une seule cible
```

Chaque cible produit un exécutable autonome unique (self-contained,
single-file) dans `publish/<rid>/`.

## Structure

- `src/AprsLive.Core` — logique APRS/KISS/réglages, sans dépendance UI.
- `src/AprsLive.Modem` — modem AFSK1200 (à venir), sans dépendance externe.
- `src/AprsLive.Audio` — E/S audio et PTT matériel (à venir).
- `src/AprsLive.App` — application Avalonia (carte, onglets, réglages).
- `tests/AprsLive.Core.Tests` — tests unitaires du cœur applicatif.

## Crédits

- Icônes de station : planches de symboles APRS
  [hessu/aprs-symbols](https://github.com/hessu/aprs-symbols) (OH7LZB,
  d'après le jeu WA8LMF ; usage libre sur le système APRS).
- Cartographie [Mapsui](https://mapsui.com/) + tuiles
  [OpenStreetMap](https://www.openstreetmap.org/copyright).

## Licence

[MIT](LICENSE).
