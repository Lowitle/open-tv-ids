# open-tv-ids

[![build](https://github.com/Lowitle/open-tv-ids/actions/workflows/build.yaml/badge.svg)](https://github.com/Lowitle/open-tv-ids/actions/workflows/build.yaml)

Dataset abierto de identificadores cruzados entre los catálogos de las plataformas de televisión a la
carta y los catálogos internacionales de las obras. Un fichero por catálogo (`emitter`) y, dentro, una
línea JSON por programa con los identificadores del mismo trabajo en **Wikidata**, **IMDb**, **TMDB** y
**TVDB**:

```json
{"id":"3cat:73476","emitter":"3cat","emitterId":"73476","kind":"anime","category":"Anime","title":"Bola de Drac Súper","originalTitle":"Dragon Ball Super","availableSince":"2025-06-06","ids":{"wikidata":"Q19839121","imdb":"tt4644488","tmdb":"62715","tvdb":"295068"},"sources":["wikidata:Q19839121","wikipedia:ca:Bola de Drac Súper"],"match":{"method":"wikidata","confidence":0.9,"verified":false},"firstSeen":"2026-10-30","lastSeen":"2026-11-01"}
{"id":"3cat:74796","emitter":"3cat","emitterId":"74796","kind":"series","category":"Sèrie","title":"Els barrufets 3D","originalTitle":"The smurfs","availableSince":"2025-12-21","ids":{"wikidata":"Q69652877","imdb":"tt14670820","tmdb":"123591","tvdb":"392426"},"sources":["wikidata:Q69652877","wikidata:P10356:els-barrufets-3d"],"match":{"method":"wikidata-id","confidence":1,"verified":false},"firstSeen":"2026-10-30","lastSeen":"2026-11-01"}
```

El segundo no tiene artículo: Wikidata ya declara el identificador de su ficha (`P10356`), así que el elemento
sale de ahí y no se busca ningún título.

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
│       └── revision.csv           # todos los programas con estado, evidencia, enlaces y sugerencia (generado)
├── mixnmatch/
│   └── 3cat.tsv                   # el catálogo entero para Mix'n'match, una fila por programa (generado, versionado)
├── schema/
│   └── series.schema.json         # JSON Schema 2020-12 de cada línea
├── overrides/
│   └── 3cat.json                  # correcciones manuales, que mandan sobre el matcher (a mano o con --check)
├── input/
│   ├── 3cat.csv                   # volcado del catálogo del emisor (ver input/README.md)
│   └── README.md                  # contrato del volcado que tiene que producir un emisor
├── src/IdsBuilder/                # el constructor: lee el volcado y cruza con Wikidata
├── tests/IdsBuilder.Tests/        # pruebas del normalizador, scorer, lector, escritor y revisión
└── .github/workflows/             # build.yaml (compila y prueba) y refresh.yaml (rehace el dataset)
```

`data/`, `report/`, `mixnmatch/` e `input/` son datos; `schema/`, `overrides/` y el código son lo que se
mantiene a mano.

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
`--review`, `--concurrency`, `--delay`, `--cache <dir>`, `--fresh`, `--offline`, `--check`, `--all`,
`--again`, `-v`. `--help` las lista.

La caché de las respuestas de Wikipedia y Wikidata vive en la carpeta temporal del usuario (cambiable con
`--cache`) y no se versiona: una segunda pasada solo pregunta por los programas cuyo título cambió. Si el
endpoint devolvió un 429 a media pasada, el hueco no se guarda: **repetir el volcado lo rellena** y es barato,
porque solo pregunta por lo que falta (la última pasada completa, más una segunda de minuto y medio, recuperó
44 programas que se habían quedado sin artículo por eso).

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

1. Antes de buscar nada, se lee el **identificador de la ficha que Wikidata ya declara**: la propiedad
   `P10356` («TV3 show ID»), en un solo SPARQL para todo Wikidata. Su valor es el *slug* de la página
   (`https://www.3cat.cat/3cat/els-barrufets-3d/` → `els-barrufets-3d`), así que el programa cuyo slug está
   ahí ya tiene elemento y no hay nada que puntuar: se publica con `"method": "wikidata-id"` y confianza 1.
   Es la única vía para las obras **sin artículo** en ninguna Wikipedia, y hoy resuelve 344 de las 1.349
   fichas (207 de ellas no las encontraba el título).
2. Se busca el título del resto (y el original, si el catálogo lo publica) en las Wikipedias catalana,
   española e inglesa. El artículo devuelve el elemento de Wikidata en la misma respuesta.
3. Se leen los identificadores de los elementos cincuenta a la vez: IMDb (`P345`), TMDB (`P4983`/`P4947`)
   y TVDB (`P4835`).
4. Se puntúa cada candidato:

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

5. Se queda el candidato con más puntos. Desde **0,90** el registro se publica; entre **0,70 y 0,89** queda
   en `report/<emisor>/revision.csv` para que lo mire una persona; por debajo aparece también en el CSV, con
   estado `sin_match`.

Una búsqueda no pasa nunca de 0,99: solo el identificador de la ficha y una corrección a mano llegan a 1. Y el
identificador no es un atajo, es la evidencia más fuerte que hay: los dos emparejamientos que corrigió en el
primer volcado llevaban el mismo título que el bueno («Els barrufets 3D» apuntaba a la serie de 1981 y es la
de 2021; «Les Tortugues Ninja» apuntaba a la de 1987 y es la de 2012, la de los 124 capítulos).

Un año que no coincide **no resta**: la fecha del catálogo es la de alta en la plataforma, no la del
estreno («Bola de Drac Súper» figura en 2025 y la serie es de 2015), así que solo puede confirmar un
emparejamiento.

## Cómo se corrige

La revisión de lo que el matcher no tiene claro es manual, y hay dos maneras de hacerla: **pregunta a
pregunta** en el terminal o **abriendo el CSV** en una hoja de cálculo. Las dos acaban en el mismo sitio,
`overrides/<emisor>.json`.

### Pregunta a pregunta

```powershell
dotnet run --project src/IdsBuilder -- --check
```

Recorre `report/<emisor>/revision.csv` de mayor a menor confianza y, para cada programa, enseña el título, el
artículo que encontró y **los enlaces para comprobarlo de un vistazo**: la ficha del programa en el emisor,
el elemento de Wikidata, el artículo de la Wikipedia y, si lo tiene, la ficha de IMDb.

```
[3/61] 3cat:61340 · Sèrie · revisar · confianza 0,85 · título exacto
   Título:   Polònia
   Artículo: ca:Polònia (Exact) · Q4887621
   Ids:      wikidata Q4887621 · imdb tt0891508
   Abrir:    https://www.3cat.cat/3cat/polonia/
             https://www.wikidata.org/wiki/Q4887621
             https://ca.wikipedia.org/wiki/Pol%C3%B2nia
   ¿Es la misma obra? [s/v/n/o/d/q/?] >
```

| Tecla | Qué hace |
| --- | --- |
| `s` | Sí, es la misma obra: se publican esos identificadores. |
| `v` | Sí, y además lo has comprobado en la ficha: el registro sale `"verified": true`. |
| `n` | No es esta obra: el programa queda fuera del dataset. |
| `o` | Otra ficha: pide el `Q…` (o pega el enlace de Wikidata) y **lee sus identificadores**. |
| `d` o `Enter` | Dejarlo para luego. |
| `q` | Salir. `?` repite la ayuda. |

**Cada respuesta se guarda al momento** en `overrides/<emisor>.json`, así que la revisión se puede dejar a
medias y retomar otro día: los programas ya decididos se saltan solos. Con `--all` entra también en los
828 programas sin coincidencia (la segunda pasada, para las obras que existen pero que el matcher no
encontró) y con `--again` vuelve a los que ya tienen corrección.

Al terminar, se aplica al dataset sin tocar la red:

```powershell
dotnet run --project src/IdsBuilder -- --offline
```

### En la hoja de cálculo

`report/<emisor>/revision.csv` lleva una columna `revisado` (`no`/`sí`), la `nota` de la corrección si ya
existe y los enlaces `url_<emisor>` (`url_3cat`), `url_wikidata` y `url_suggerencia`, que la hoja abre con un
clic. Las columnas `wikidata`/`imdb`/`tmdb`/`tvdb` muestran **lo que se publicaría** también en los programas
en revisión, que es justo lo que hay que aceptar o rechazar. La hoja sirve para leer y filtrar; para escribir,
`--check` o el JSON.

### El fichero de correcciones

`overrides/<emisor>.json` manda sobre el matcher y sobrevive a todos los volcados:

```json
[
  {
    "id": "3cat:60336",
    "ids": { "wikidata": "Q82990385", "tmdb": "100000", "imdb": "tt1234567" },
    "note": "Confirmado en la ficha de TMDB; la Wikipedia catalana no tiene artículo.",
    "verified": true,
    "reject": false
  },
  {
    "id": "3cat:76716",
    "ids": {},
    "note": "descartado a mano: el emparejamiento no era la obra",
    "verified": false,
    "reject": true
  }
]
```

Los ids se validan igual que los de Wikidata (un `imdb` que no empiece por `tt` se descarta) y el registro
sale con `"match": {"method": "manual", "confidence": 1}` y el `verified` de la corrección. Tres detalles que
importan:

- Los `ids` **sustituyen** a los del matcher, no se mezclan: al corregir un falso positivo («Polònia», el
  país, en lugar de «Polònia», el programa) no se queda ningún identificador del elemento equivocado. Por eso
  la tecla `o` de la revisión lee el elemento que le dices y escribe **todos** sus identificadores; si editas
  el JSON a mano y solo pones el `Q…`, el registro publica solo Wikidata.
- `"reject": true` deja el programa fuera del dataset sin borrar la fila del informe: no vuelve a aparecer en
  la revisión y en el CSV queda como `descartado`.
- Las correcciones que hace `--check` llevan nota automática («revisado a mano», «descartado a mano: …»), que
  sale en la columna `nota` del CSV y en la línea `Nota:` de la tarjeta la próxima vez que abras ese programa:
  así se recuerda por qué se decidió.

### En Mix'n'match (en abierto)

`mixnmatch/<emisor>.tsv` es el catálogo entero en el formato que importa
[Mix'n'match](https://meta.wikimedia.org/wiki/Mix%27n%27match/Import), para que la cola de revisión no dependa
de una sola persona: una fila por programa, con el *slug* de su ficha como `id`, la categoría y el título
original en `desc`, y el elemento en `q` o en `autoq`.

| Columna | Qué lleva |
| --- | --- |
| `id` | El *slug* de la ficha (`els-barrufets-3d`), que es el valor de `P10356`: el catálogo se importa con esa propiedad y no necesita ninguna columna de propiedad. |
| `name` | El título del programa en el catálogo del emisor. |
| `desc` | La categoría y el título original («Anime · Dragon Ball Super»), que es lo que ayuda a encontrar la obra. |
| `url` | La ficha del programa en el catálogo del emisor. |
| `q` | El elemento que **sabemos** que es: lo declara Wikidata (`wikidata-id`) o lo confirmó una persona (`manual`). Mix'n'match lo da por casado. |
| `autoq` | El elemento que **parece**: lo encontró una búsqueda por título, y el voluntario lo confirma o lo corrige. |

El circuito se cierra solo: Mix'n'match escribe `P10356 = <slug>` en el elemento que casa un voluntario y el
siguiente volcado lo lee como identificador, sin buscar el título (paso 1 de «Cómo se empareja»). Y sirve
también para lo contrario, que es como se encontraron los dos emparejamientos que el título confundía: una
ficha con el `autoq` mal apuntado se ve de un vistazo en el catálogo y se arregla con `--check` o editando
`overrides/`.

Hoy son 1.349 fichas: 345 casadas con certeza, 115 por confirmar y 889 para la comunidad.

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

1.349 programas de televisión procesados: **460 publicados** (34,1 %), 61 esperando revisión y 828 sin
coincidencia. De los publicados, 385 llevan IMDb, 388 TMDB y 329 TVDB; los 460 llevan Wikidata. **344 salen
del identificador de la ficha** (`P10356`) y no de una búsqueda por título; 37 publican solo el elemento
porque su elemento de Wikidata no tiene IMDb, TMDB ni TVDB todavía (están listados en
[`report/3cat/cobertura.md`](report/3cat/cobertura.md): añadirlos en Wikidata los completa).

| Categoría | Procesados | Publicados | Con TMDB |
| --- | ---: | ---: | ---: |
| Sèrie | 245 | 178 (72,7 %) | 167 |
| Anime | 29 | 18 (62,1 %) | 21 |
| Animació | 146 | 80 (54,8 %) | 80 |
| Humor | 27 | 9 (33,3 %) | 8 |
| Concurs | 53 | 17 (32,1 %) | 20 |
| Documental | 244 | 72 (29,5 %) | 66 |
| Magazín | 38 | 9 (23,7 %) | 7 |
| Actualitat | 186 | 36 (19,4 %) | 25 |
| (sin categoría) | 267 | 31 (11,6 %) | 35 |
| Esports | 34 | 2 (5,9 %) | 1 |

La ficción es donde el cruce funciona: casi tres cuartas partes de las series y más de la mitad de la
animación ya tienen ficha. Los programas propios de la plataforma (documentales, informativos, deportes,
magacines) siguen lejos, porque ni tienen artículo ni están dados de alta en Wikidata. Programa a programa,
con la evidencia y la sugerencia de cada uno, está en [`report/3cat/cobertura.md`](report/3cat/cobertura.md) y
[`report/3cat/revision.csv`](report/3cat/revision.csv). Las **889** fichas que siguen sin elemento están en
[`mixnmatch/3cat.tsv`](mixnmatch/3cat.tsv), el catálogo que puede casar la comunidad.

## Límites

- **No es exhaustivo.** Un programa sin artículo en ninguna de las tres Wikipedias **ni** identificador de su
  ficha en Wikidata no se puede casar. Las franquicias con artículo propio (la de «Bola de Drac», la del manga
  de «Naruto») se quedan fuera porque no publican ids de serie.
- **Los registros salen con `"verified": false`** porque la única fuente es Wikidata. Una segunda fuente
  independiente (TMDB `/find`, TVmaze `/lookup/shows?imdb=`) es lo que los marcará como verificados.
- **Cortesía con los endpoints.** `IdsBuilder` se presenta con un `User-Agent` identificable y deja una
  pausa entre peticiones. Si vas a hacer volcados en bucle, sube `--delay`.

## Licencia

Los datos (`data/`, `report/`, `overrides/`) son **CC0 1.0** y el código es **MIT**; los detalles están en
[`LICENSE`](LICENSE). Los identificadores vienen de Wikidata, que también es CC0; de Wikipedia solo se
guarda el título del artículo citado en `sources`.

## Hoja de ruta

- [x] Leer el identificador de la ficha que Wikidata ya declara (`P10356`): 207 registros que el título no
      encontraba y dos emparejamientos corregidos.
- [ ] Revisar con `--check` los 61 programas que quedan a medias y pasarlos a `overrides/3cat.json`.
- [x] Llevar el catálogo a **Mix'n'match** (`mixnmatch/3cat.tsv`, con `P10356` como identificador de la fila
      y el candidato en `autoq`): 1.349 fichas, 345 ya casadas, 115 por confirmar y 889 para la comunidad.
- [ ] Que la comunidad case las 889 que quedan en el catálogo de Mix'n'match: lo que rellene aparece en
      `P10356` y el siguiente volcado lo publica solo, sin tocar una línea de código.
- [ ] Verificar con una segunda fuente (TMDB `/find`, TVmaze) los registros publicados, para poder escribir
      `"verified": true`.
- [ ] Segundo emisor (**rtve**): confirmar su endpoint público de programas, escribir su volcado y añadirlo
      al refresco.
- [ ] Publicar el dataset como release con fecha, además de en `data/`.

