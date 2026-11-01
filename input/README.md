# Formato de entrada: el volcado de un emisor

El constructor del dataset no habla con las plataformas. Lee un **CSV** con la lista de programas de un
emisor y lo cruza con Wikidata; producir ese CSV es lo único que tiene que hacer un emisor.

El fichero va en `input/<emisor>.csv` (es lo que busca `IdsBuilder` por defecto) y se pasa con `-i <csv>`
si está en otro sitio.

## Dialecto

- Separador `;`. Es el que Excel espera en español y el que usan los volcados que ya existen.
- Todas las celdas entre comillas dobles, que se escapan duplicándolas (`"Crims; el jurat"` no se parte).
- UTF-8 con BOM y saltos de línea `\r\n`, para que Excel abra los acentos bien.
- La primera fila es la cabecera con los nombres de las columnas; el orden no importa.

## Columnas

Obligatorias:

| Columna | Contenido |
| --- | --- |
| `id` | Identificador del programa en el catálogo del emisor (`programatv_id` en 3Cat). Es el que acaba en `emitterId`. |
| `titol` | Título del programa en el idioma del catálogo (`title` en el dataset). |
| `kind` | `TV` para los programas de televisión; cualquier otra cosa se considera web o radio y se salta, salvo con `--include-web`. |
| `category` | Categoría del catálogo («Sèrie», «Animació»...), sin traducir. De aquí sale `kind` en el dataset. |

Opcionales, pero son las que hacen que el emparejamiento funcione:

| Columna | Contenido |
| --- | --- |
| `detail_title` | Título de la ficha del programa, que suele ser el del artículo de la Wikipedia. Manda sobre `titol` para buscar. |
| `detail_original_title` | Título original («Dragon Ball Super»). Se busca antes que el título local en la segunda Wikipedia y es lo que encuentra las series dobladas. |
| `detail_countries` | Países de origen publicados por la ficha. Se usa como pista. |
| `primera_emissio` | Fecha (`AAAA-MM-DD`) del vídeo más antiguo disponible. Ojo: es la fecha del catálogo, no la del estreno. |
| `url` | Página del programa en el catálogo del emisor (`https://www.3cat.cat/3cat/polonia/`). Es el enlace que abre quien revisa a mano para comprobar que los identificadores son de la obra. |
| `slug` | Identificador de la página (`els-barrufets-3d`): el valor que Wikidata declara en `P10356` («TV3 show ID»). Si falta, se lee del final de `url`, así que un volcado sin esta columna también funciona. |

Cualquier otra columna se ignora, así que un volcado puede llevar más información sin romper nada.

`slug` y `url` no son decorativos: antes de buscar ningún título, el build lee de Wikidata todos los valores de
`P10356` y casa los programas cuyo slug está ahí. Un volcado que no publique ninguno de los dos sigue
funcionando, pero deja ese camino sin usar.

## El volcado de 3Cat

`input/3cat.csv` se genera con `CatalogDump`, la herramienta del repositorio del plugin de Jellyfin
(<https://github.com/Lowitle/Jellyfin-3CAT>), que lee los endpoints públicos de 3Cat con el mismo cliente
que usa el plugin:

```powershell
dotnet run --project tools/CatalogDump
# escribe catalogo-3cat-programas.csv en la raíz del repositorio del plugin
```

Se copia a `input/3cat.csv` y se versiona: así el dataset es reproducible y se sabe exactamente qué
catálogo describe cada publicación.

## Emisores pendientes

- **rtve**: falta confirmar el endpoint público de programas de RTVE y decidir qué campos publica
  (`idImdb`, artículo de Wikipedia...). Cuando exista, basta con que escriba `input/rtve.csv` con las
  columnas de arriba; el constructor ya acepta `--emitter rtve`.
