<p align="center"><img src="docs/logo.svg" width="100" alt="Logo de Ritmo"></p>

# Ritmo

Tu música, siempre a mano. Reproductor para **Windows, Android y web**, con interfaz propia, búsqueda mediante InnerTube, playlists, letras y descargas dentro de la aplicación.

## Descargar

| Plataforma | Descarga | Requisitos |
| --- | --- | --- |
| Windows | [Ritmo-Windows-x64.exe](https://github.com/andymendozap03/RitmoApp/releases/latest/download/Ritmo-Windows-x64.exe) | Windows 10/11 de 64 bits |
| Android | [Ritmo-Android.apk](https://github.com/andymendozap03/RitmoApp/releases/latest/download/Ritmo-Android.apk) | Android 7.0 o superior |

[Todas las versiones](https://github.com/andymendozap03/RitmoApp/releases) · [Sumas SHA-256](https://github.com/andymendozap03/RitmoApp/releases/latest/download/SHA256SUMS.txt)

En Windows, descarga y abre el ejecutable: incluye .NET y no requiere instalar un servidor. En Android, abre el APK y permite la instalación desde el origen elegido. Las actualizaciones deben estar firmadas con la misma clave para conservar la instalación existente.

## Funciones

- Búsqueda de canciones, artistas y playlists, con prioridad para coincidencias oficiales identificables.
- Reproductor con portada, controles, barra de progreso, aleatorio y repetición.
- Favoritos, creación de playlists y selección de lista al añadir canciones.
- Importación y respaldo CSV de favoritos y playlists individuales.
- Descargas para escuchar sin conexión dentro de Ritmo, con las letras disponibles.
- Letras sincronizadas cuando existan, o letra completa como alternativa.
- Letras y cola independientes. En escritorio, la cola muestra la canción actual y hasta 20 próximas canciones y se actualiza al avanzar.
- Controles Windows desde la vista previa de la barra de tareas.
- Android para teléfono, tablet y estéreo Android, con servicio de audio y biblioteca para Android Auto.

## Estructura

```text
desktop/       Aplicación nativa de Windows: C#, WPF y MediaPlayer
web/           Aplicación web: JavaScript y Vite
web/android/   Android: Capacitor y servicio nativo Kotlin/Media3
docs/          Logo y notas de publicación
```

La versión Windows es nativa WPF. Android utiliza la interfaz web y componentes nativos para reproducción, descargas e integración multimedia.

## Ejecutar la web

Requiere Node.js 22.12 o superior.

```powershell
cd web
npm ci
npm run dev
```

Abre la dirección que muestra Vite, normalmente `http://localhost:8099`. Mantén el servidor activo para los proxies de catálogo, letras y audio. `npm run build` genera la interfaz; sus servicios de proxy siguen necesitando servidor. También está disponible `web/servir.bat`, con Java 17 o superior.

## Compilar Windows

Instala el SDK de .NET 10 y ejecuta:

```powershell
cd desktop
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o publish
```

El resultado es `desktop/publish/Ritmo.exe`. También puedes usar `desktop/compilar.bat`. [Detalles de escritorio y pruebas](desktop/README.md).

## Compilar Android

Requiere Node.js 22.12+, Android Studio, JDK 21 y Android SDK 36. Configura el SDK local con Android Studio o `web/android/local.properties`.

```powershell
cd web
npm ci
npm run mobile:sync
cd android
.\gradlew.bat :app:assembleDebug
```

El APK de desarrollo queda en `web/android/app/build/outputs/apk/debug/`. Para una release firmada, crea `web/android/keystore.properties` usando el archivo `.example`, configura tu propia clave y ejecuta `:app:assembleRelease`. Las claves y los ajustes locales están excluidos de Git. [Guía Android](web/ANDROID.md).

## Datos y funcionamiento offline

Cada plataforma mantiene su propia biblioteca; los CSV permiten trasladar listas, pero no sincronizan automáticamente las descargas. En Windows, los datos se guardan en `%LOCALAPPDATA%\RitmoDesktop`, fuera del directorio del programa.

La búsqueda y la primera carga de audio requieren conexión. Windows valida y prepara el archivo completo en caché antes de reproducirlo para permitir saltos a cualquier posición; esa preparación depende de la conexión y del proveedor. Las descargas completas se conservan para uso offline.

La disponibilidad de canciones y letras depende de los proveedores. La prioridad de resultados oficiales usa los metadatos disponibles y no garantiza que todos los resultados estén verificados. El ejecutable Windows todavía no tiene firma de editor comercial.

## Desarrollo y publicación

El repositorio contiene fuentes y recursos. Dependencias, cachés, claves de firma y binarios generados se excluyen mediante `.gitignore`; los instalables se publican en [Releases](https://github.com/andymendozap03/RitmoApp/releases).

Las pruebas de escritorio cubren reproducción local, pausa y avance, cola, CSV, persistencia, letras, descargas por rangos y reutilización de paneles e imágenes. Consulta los comandos en [desktop/README.md](desktop/README.md).