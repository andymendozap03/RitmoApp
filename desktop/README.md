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

Las listas usan un `ListBox` con `VirtualizingStackPanel` y reciclaje: conservan todas las canciones como datos, pero solo crean controles cerca de la zona visible. El control administra su propio desplazamiento para mantener activa la [virtualización de WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls). Se conservan los botones de cada fila y la posición al volver de letras o portada. La biblioteca y el reproductor se ocultan sin descartar sus medidas al alternar entre ellos.

Solo se solicitan las portadas dentro de la zona visible. Hay dos trabajos de miniaturas y uno reservado para la portada actual; se cancelan solicitudes al ocultar o retirar una vista, sin cancelar imágenes compartidas por otra vista visible. Las filas, la cola y el reproductor inferior comparten tamaños de miniatura, y se evita descargar el mismo original simultáneamente.

La decodificación JPEG/PNG y la conversión de píxeles se completan en segundo plano antes de entregar la imagen a WPF. Se presentan como máximo dos imágenes nuevas cada 16 ms para evitar una ráfaga de actualizaciones. La caché de imágenes decodificadas tiene un límite de 32 MB; la portada grande conserva su resolución independiente y el archivo original permanece en disco. Las imágenes ya mostradas se reutilizan al regresar a la página principal.

Los controles del reproductor y de la cola se preparan en segundo plano del dispatcher al seleccionar una canción, antes del primer clic, y se reutilizan durante la misma canción. La letra se añade por lotes después de pintar el panel. La preparación del audio, las transferencias y el análisis del catálogo trabajan fuera del hilo de interfaz; el progreso de descarga se comunica como máximo unas diez veces por segundo y al completar. Se mantiene la consulta de letras al abrir su vista, sin precarga de proveedores.

`--panel-test` mide la apertura y el regreso hasta un turno de dibujo con 320 canciones de prueba, verifica las filas recicladas, el desplazamiento, la prioridad de portada y la cancelación de cargas. Al añadir `--real-art-test`, copia 320 portadas existentes de la caché del usuario a una carpeta aislada, espera a que las imágenes visibles estén cargadas y genera capturas `real-art-*.png`. No modifica la biblioteca ni sus imágenes originales. Estas medidas no garantizan la latencia de presentación física del monitor.

`%LOCALAPPDATA%\RitmoDesktop\performance.log` registra la versión, el nivel de renderizado, las transiciones y pausas del dispatcher superiores a 750 ms. Es local, se escribe fuera de la interfaz, rota al superar aproximadamente 64 KB y no incluye títulos de canciones ni contenido de la biblioteca.

Se conserva la canción actual y hasta 20 próximas canciones en la cola. Al avanzar, la anterior desaparece de la vista y entra otra al final; el historial interno permite retroceder. También se conservan el logo de alta resolución, los botones de letras y cola en el reproductor inferior y la apertura de la portada sin reiniciar la canción actual.
## Límites conocidos

El avance por clic observa también los eventos que WPF marca como atendidos, para que el temporizador no sustituya el destino antes de soltar el ratón. Al cambiar de canción se crea una nueva instancia de `MediaPlayer`, se ignoran los eventos de instancias anteriores y el cambio automático se realiza después del evento de finalización. Si el contador no arranca, se intenta reabrir el archivo local una sola vez conservando la intención de pausa; un fallo persistente permite reintentar sin saltarse la canción.

`Ritmo.exe --transition-test` verifica finales naturales consecutivos, siguiente/anterior, pausa y pulsaciones rápidas. También copia al menos dos M4A existentes de la biblioteca local a una carpeta de prueba para verificar el cambio entre archivos comprimidos. No modifica los originales; esta comprobación requiere tener esas canciones guardadas. El registro local incluye eventos de reproducción por generación e índice, sin títulos ni URLs.

- El rango mínimo de ventana es 760 × 540; las letras y la cola tienen desplazamiento propio y los controles permanecen visibles.
- El catálogo no siempre proporciona insignias de autenticidad; se priorizan las coincidencias oficiales identificables, sin garantizar que cada resultado esté verificado.
- No hay sincronización automática entre web, teléfono y escritorio. Usa CSV para trasladar listas.
- Este ejecutable aún no tiene firma de editor comercial de Windows.
