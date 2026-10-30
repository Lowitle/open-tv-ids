# open-tv-ids

[![build](https://github.com/Lowitle/open-tv-ids/actions/workflows/build.yaml/badge.svg)](https://github.com/Lowitle/open-tv-ids/actions/workflows/build.yaml)

Dataset abierto de identificadores cruzados entre los catálogos de las plataformas de televisión a la
carta y los catálogos internacionales de las obras. Un fichero por catálogo (`emitter`) y, dentro, una
línea JSON por programa con los identificadores del mismo trabajo en **Wikidata**, **IMDb**, **TMDB** y
**TVDB**:

```json
{"id":"3cat:73476","emitter":"3cat","emitterId":"73476","kind":"anime","category":"Anime","title":"Bola de Drac Súper","originalTitle":"Dragon Ball Super","availableSince":"2025-06-06","ids":{"wikidata":"Q19839121","imdb":"tt4644488","tmdb":"62715","tvdb":"295068"},"sources":["wikidata:Q19839121","wikipedia:ca:Bola de Drac Súper"],"match":{"method":"wikidata","confidence":0.9,"verified":false},"firstSeen":"2026-10-29","lastSeen":"2026-10-30"}
```

El catálogo de 3Cat identifica cada programa con un `programatv_id` que no significa nada fuera de 3Cat.
Los agregadores de metadata de Jellyfin (TheMovieDb, TheTVDB, OMDb) trabajan con sus propios ids, y
ninguno sabe que el programa `73476` de 3Cat es la serie que ellos llaman `62715`. Este repositorio es esa
traducción, y la mantiene fuera del plugin que la consume: es un dataset, no una parte de un cliente.

## Para qué sirve

- **Resolver la metadata de una biblioteca de Jellyfin con los proveedores nativos.** La ficha
  `tvshow.nfo` de una serie puede llevar tres `uniqueid` (tmdb, tvdb, imdb) y Jellyfin los usa para
  descargar carátulas, sinopsis y episodios sin plugins adicionales. Con una línea del dataset, una
  biblioteca montada desde el catálogo de 3Cat deja de depender de que la ficha del programa publique el
  id de IMDb, que casi nunca lo hace.
- **Cruzar dos catálogos sin escribir un matcher.** Cualquier herramienta puede hacer un `join` por id con
  TMDB, TVDB, IMDb o Wikidata.
- **Es dato, no copia.** No hay sinopsis, ni carátulas, ni episodios: solo la identidad del trabajo, la
  evidencia del emparejamiento y su procedencia, para que se pueda comprobar.

## Ficheros

```
open-tv-ids/
├── data/
│   └── 3cat/series.jsonl          # el dataset: una línea JSON por programa (generado, versionado)
├── report/
│   └── 3cat/
│       ├── cobertura.md           # cobertura por categoría y qué queda pendiente (generado, versionado)
│       └── revision.csv           # todos los programas con estado, evidencia y sugerencia (generado)
├── schema/
│   └── series.schema.json         # JSON Schema 2020-12 de cada línea
├── overrides/
│   └── 3cat.json                  # correcciones manuales, que mandan sobre el matcher (a mano)
├── input/
│   ├── 3cat.csv                   # volcado del catálogo del emisor (ver input/README.md)
│   └── README.md                  # contrato del volcado que tiene que producir un emisor
├── src/IdsBuilder/                # el constructor: lee el volcado y cruza con Wikidata
├── tests/IdsBuilder.Tests/        # pruebas del normalizador, del scorer, del lector y del escritor
└── .github/workflows/             # build.yaml (compila y prueba) y refresh.yaml (rehace el dataset)
```

`data/`, `report/` e `input/` son datos; `schema/`, `overrides/` y el código son lo que se mantiene a mano.

## Cómo se genera

```powershell
# 1. El volcado del emisor (una fila por programa). Ver input/README.md.
#    Para 3Cat lo escribe CatalogDump, del repositorio del plugin de Jellyfin.

# 2. El cruce con Wikidata y la escritura del dataset
dotnet run --project src/IdsBuilder

# Sin red: rehace los informes a partir de lo que ya está en la caché
dotnet run --project src/IdsBuilder -- --offline

# Pruebas
dotnet test open-tv-ids.sln
```

Escribe `data/<emisor>/series.jsonl`, `report/<emisor>/revision.csv` y `report/<emisor>/cobertura.md`.
Opciones: `--emitter <slug>`, `-i <csv>`, `-o <raíz>`, `--limit`, `--skip`, `--include-web`, `--accept`,
`--review`, `--concurrency`, `--delay`, `--cache <dir>`, `--fresh`, `--offline`, `-v`. `--help` las lista.

La caché de las respuestas de Wikipedia y Wikidata vive en la carpeta temporal del usuario (cambiable con
`--cache`) y no se versiona: una segunda pasada solo pregunta por los programas cuyo título cambió.

## Cómo se añade un emisor

Un emisor es un catálogo con sus propios ids. El constructor no habla con la plataforma: lee un CSV con la
lista de programas y lo cruza con Wikidata, así que **añadir un emisor es producir su volcado**, no escribir
un cliente. El contrato de las columnas está en [`input/README.md`](input/README.md).

```powershell
dotnet run --project src/IdsBuilder -- --emitter rtve
# lee input/rtve.csv y escribe data/rtve/, report/rtve/ y overrides/rtve.json
```

En el código, la pieza es `IProgramCatalog` (`src/IdsBuilder/Emitters/ProgramCatalog.cs`): la
implementación que hay, `CsvCatalog`, lee el volcado; una que hablara directamente con la API del emisor
tendría el mismo contrato.

## Cómo se empareja

1. Se busca el título del programa (y el original, si el catálogo lo publica) en las Wikipedias catalana,
   española e inglesa. El artículo devuelve el elemento de Wikidata en la misma respuesta.
2. Se leen los identificadores de los elementos cincuenta a la vez: IMDb (`P345`), TMDB (`P4983`/`P4947`)
   y TVDB (`P4835`).
3. Se puntúa cada candidato:

| Evidencia | Puntos |
| --- | ---: |
| Título idéntico al del artículo | 0,60 |
| Artículo con el título más un calificador («Merlí (sèrie de televisió)») | 0,55 |
| Un título contiene al otro («Crims» y «Crims a Barcelona») | 0,40 |
| El calificador nombra una obra o un año («(programa)», «(2005 TV series)») | +0,05 |
| El elemento publica un id de TMDB o TVDB | +0,20 |
| Solo publica un id de IMDb (lo comparten personas, películas y series) | +0,10 |
| El elemento es una serie de televisión o un subtipo suyo (`P31/P279*` de Q5398426) | +0,10 |
| El año de inicio (`P580`) coincide con el del catálogo (±1) | +0,10 |

4. Se queda el candidato con más puntos. Desde **0,90** el registro se publica; entre **0,70 y 0,89** queda
   en `report/<emisor>/revision.csv` para que lo mire una persona; por debajo aparece también en el CSV, con
   estado `sin_match`.

Un año que no coincide **no resta**: la fecha del catálogo es la de alta en la plataforma, no la del
estreno («Bola de Drac Súper» figura en 2025 y la serie es de 2015), así que solo puede confirmar un
emparejamiento.

## Cómo se corrige

`overrides/<emisor>.json` manda sobre el matcher y sobrevive a todos los volcados:

```json
[
  {
    "id": "3cat:60336",
    "ids": { "wikidata": "Q82990385", "tmdb": "100000", "imdb": "tt1234567" },
    "note": "Confirmado en la ficha de TMDB; la Wikipedia catalana no tiene artículo.",
    "verified": true
  }
]
```

Los ids se validan igual que los de Wikidata (un `imdb` que no empiece por `tt` se descarta) y el registro
sale con `"match": {"method": "manual", "confidence": 1, "verified": true}`.

## Cómo se consume

```csharp
foreach (var line in File.ReadLines("data/3cat/series.jsonl"))
{
    var record = JsonSerializer.Deserialize<CrossIdRecord>(line, options);

    if (record is not null && record.Ids.TryGetValue("tmdb", out var tmdb))
    {
        series.ProviderIds["Tmdb"] = tmdb;   // Jellyfin descarga la metadata con su proveedor nativo
    }
}
```

El `tvshow.nfo` de una serie quedaría así, que es lo que hace que Jellyfin resuelva la serie con sus
proveedores nativos:

```xml
<uniqueid type="threecat" default="true">73476</uniqueid>
<uniqueid type="tmdb">62715</uniqueid>
<uniqueid type="tvdb">295068</uniqueid>
<uniqueid type="imdb">tt4644488</uniqueid>
```

## Cobertura del último volcado (3cat)

1.349 programas de televisión procesados: **249 publicados** (18,5 %), 91 esperando revisión y 1.009 sin
coincidencia. De los publicados, 242 llevan IMDb, 232 TMDB y 221 TVDB; los 249 llevan Wikidata.

| Categoría | Procesados | Publicados | Con TMDB |
| --- | ---: | ---: | ---: |
| Sèrie | 245 | 122 (49,8 %) | 131 |
| Animació | 146 | 63 (43,2 %) | 74 |
| Anime | 29 | 9 (31,0 %) | 15 |
| Humor | 27 | 5 (18,5 %) | 6 |
| Concurs | 53 | 7 (13,2 %) | 11 |
| Magazín | 38 | 5 (13,2 %) | 6 |
| Documental | 244 | 16 (6,6 %) | 28 |
| Actualitat | 186 | 5 (2,7 %) | 13 |

La ficción es donde el cruce funciona: la mitad de las series y el 43 % de la animación tienen artículo en
alguna Wikipedia. Los programas propios de la plataforma (documentales, informativos, deportes, magacines)
casi nunca lo tienen, y ahí el dataset no puede llegar sin trabajo manual. Programa a programa, con la
evidencia y la sugerencia de cada uno, está en [`report/3cat/cobertura.md`](report/3cat/cobertura.md) y
[`report/3cat/revision.csv`](report/3cat/revision.csv).

## Límites

- **No es exhaustivo.** Un programa sin artículo en ninguna de las tres Wikipedias no se puede casar. Las
  franquicias con artículo propio (la de «Bola de Drac», la del manga de «Naruto») se quedan fuera porque no
  publican ids de serie.
- **Los registros salen con `"verified": false`** porque la única fuente es Wikidata. Una segunda fuente
  independiente (TMDB `/find`, TVmaze `/lookup/shows?imdb=`) es lo que los marcará como verificados.
- **Cortesía con los endpoints.** `IdsBuilder` se presenta con un `User-Agent` identificable y deja una
  pausa entre peticiones. Si vas a hacer volcados en bucle, sube `--delay`.

## Licencia

Los datos (`data/`, `report/`, `overrides/`) son **CC0 1.0** y el código es **MIT**; los detalles están en
[`LICENSE`](LICENSE). Los identificadores vienen de Wikidata, que también es CC0; de Wikipedia solo se
guarda el título del artículo citado en `sources`.

## Hoja de ruta

- [ ] Revisar `report/3cat/revision.csv` y pasar a `overrides/3cat.json` los 91 programas que quedan a
      medias.
- [ ] Verificar con una segunda fuente (TMDB `/find`, TVmaze) los registros publicados, para poder escribir
      `"verified": true`.
- [ ] Segundo emisor (**rtve**): confirmar su endpoint público de programas, escribir su volcado y añadirlo
      al refresco.
- [ ] Publicar el dataset como release con fecha, además de en `data/`.

