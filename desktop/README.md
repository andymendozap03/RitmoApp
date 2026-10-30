# Ritmo para Windows — C# / WPF

Aplicación nativa para Windows 10/11 x64, con interfaz oscura basada en la web. No usa WebView, navegador, Node, Vite, Java ni un proxy local.

## Abrir

Ejecuta `publish\Ritmo.exe`. La publicación autónoma incluye .NET; puedes copiar ese ejecutable a otro equipo Windows x64. La primera apertura puede tardar unos segundos mientras Windows extrae los componentes nativos de .NET.

## Funciones

- Inicio con recomendaciones variables y recientes; búsqueda de canciones, artistas y playlists con carga adicional al desplazarse.
- Reproductor con portada, avance/retroceso, pausa, aleatorio estable, repetición, favoritos, letras y cola independientes.
- Vista previa de la barra de tareas con Me gusta, Anterior, Reproducir/Pausar y Siguiente. También admite teclas multimedia y espacio fuera de los campos de texto.
- Pulsar la canción que ya está seleccionada abre su portada conservando posición, cola y estado de reproducción o pausa.
- Descargas internas con visto de disponibilidad sin conexión. Los archivos parciales nunca se marcan como completos. La sección Descargas funciona sin internet.
- Creación de playlists y selector de lista desde el botón + de cada canción.
- Importación de CSV y enlaces de listas compartidas. Respaldo CSV por playlist y de Me gusta; compatible con los respaldos de la web.
- Letras sincronizadas cuando el proveedor las ofrece, o letra completa. Se guardan localmente al descargarlas; no todas las canciones tienen letra disponible.

La biblioteca del escritorio es independiente de la del navegador y Android. Para trasladar tus listas, exporta sus CSV desde la web y usa **Importar CSV** aquí. El CSV conserva la lista, no incluye los archivos de audio.

## Audio y almacenamiento

El catálogo y los resolutores se consultan desde C#. Se utilizan los perfiles de InnerTube de la aplicación existente, con alternativas de origen. La disponibilidad depende de la canción y del proveedor; si falla una canción, se muestra el error sin saltar a otra automáticamente.

Antes de reproducir una canción remota, se prepara un archivo completo en caché. Esto añade una espera inicial, pero permite adelantar a cualquier posición sin depender de rangos incompletos del servidor. Descargar conserva ese archivo permanentemente dentro de la biblioteca, junto con la letra disponible. Las descargas no se eliminan al limpiar la caché temporal.

Datos: `%LOCALAPPDATA%\RitmoDesktop`

- `library.json` y su copia `.bak`: favoritos, listas, recientes y registro de descargas.
- `audio`: descargas permanentes.
- `cache`: audio temporal; se limita a aproximadamente 512 MB al abrir la aplicación.
- `lyrics` y `art`: letras y portadas para uso offline.
- `errors.log`: errores de interfaz no controlados.

## Compilar

Requiere el SDK de .NET 10 en Windows. Desde esta carpeta:

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o publish
```

También puedes usar `compilar.bat`. El logo está en `Assets/ritmo.ico`, con tamaños de 16 a 256 px; `Generate-Icon.ps1` lo regenera.

## Verificación

```powershell
Start-Process .\bin\Release\net10.0-windows\Ritmo.exe -ArgumentList '--self-test' -Wait
Get-Content .\bin\Release\net10.0-windows\self-test.txt
```

Las pruebas verifican cola sin duplicados, CSV, persistencia, letras, extracción del catálogo y reconstrucción de descargas por rangos con un servidor local controlado. `--network-test` comprueba búsqueda, audio real, salto a 90 segundos y descarga; necesita internet y silencia la reproducción de prueba. Se verificaron «Virtual Diva» (Don Omar), «LUNA» (Feid y ATL Jacob) y «PROVENZA» (KAROL G), con avance confirmado a los 92 segundos y descargas persistentes. Esto no garantiza la disponibilidad de todo el catálogo. `--ui-test` genera capturas de Inicio y del reproductor en dos tamaños de ventana, con datos aislados de la biblioteca del usuario.

## Comportamiento de la interfaz

Las listas conservan sus controles WPF y se crean por lotes para permitir interacción durante la carga. Las portadas se decodifican fuera del hilo de interfaz, con cuatro trabajos simultáneos como máximo y una caché de imágenes limitada a 48 MB. Las miniaturas se ajustan al tamaño de pantalla; la portada grande usa su propia resolución y el archivo original se conserva en disco.

Los paneles de letras y cola se reutilizan durante la misma canción, sin reconstruirlos al alternar su visibilidad. La preparación del audio, las transferencias y el análisis del catálogo trabajan fuera del hilo de interfaz; el progreso de descarga se comunica como máximo unas diez veces por segundo y al completar. Se mantiene la consulta de letras al abrir su vista, sin precarga de proveedores.

Se conserva la canción actual y hasta 20 próximas canciones en la cola. Al avanzar, la anterior desaparece de la vista y entra otra al final; el historial interno permite retroceder. También se conservan el logo de alta resolución, los botones de letras y cola en el reproductor inferior y la apertura de la portada sin reiniciar la canción actual.
## Límites conocidos

- El rango mínimo de ventana es 760 × 540; las letras y la cola tienen desplazamiento propio y los controles permanecen visibles.
- El catálogo no siempre proporciona insignias de autenticidad; se priorizan las coincidencias oficiales identificables, sin garantizar que cada resultado esté verificado.
- No hay sincronización automática entre web, teléfono y escritorio. Usa CSV para trasladar listas.
- Este ejecutable aún no tiene firma de editor comercial de Windows.
