# Ritmo en iPhone y iPad

La web es instalable como una PWA: icono propio, ventana sin la barra de Safari y almacenamiento de audio y letras dentro del dispositivo. No es un IPA y no tiene una firma que renovar cada siete días.

## Instalar y escuchar sin conexión

1. Publica la versión compilada en una **dirección HTTPS estable**, con los servicios de música configurados como se explica abajo. `http://192.168…` y el `localhost` de otra computadora no habilitan el service worker en el iPhone.
2. Abre esa dirección en Safari → Compartir → Añadir a pantalla de inicio. Si aparece **Abrir como app**, actívalo.
3. Abre **el nuevo icono de Ritmo**. En Descargas → Instalar Ritmo / Uso sin conexión puedes consultar las instrucciones y el estado de preparación del arranque offline.
4. Descarga las canciones desde esa instalación, dejando Ritmo abierto hasta que aparezca el visto. Las letras disponibles se guardan también; el visto confirma el audio, no la existencia de una letra.
5. Activa modo avión, cierra y vuelve a abrir Ritmo desde su icono. Entra en Descargas: el audio guardado se reproduce y permite adelantar y retroceder sin contactar al servidor.

Se solicita almacenamiento persistente al descargar, pero Safari decide si lo concede. La cuota depende del dispositivo; borrar los datos del sitio elimina su biblioteca offline. No prometemos conservación ilimitada ante presión de almacenamiento. La instalación y la pestaña de Safari pueden tener almacenes separados: descarga desde el icono que utilizarás y conserva la misma dirección HTTPS. No uses navegación privada.

La app integra Media Session con título, artista, reproducción/pausa, anterior/siguiente y posición cuando Safari los admite. El comportamiento con pantalla bloqueada y las descargas en segundo plano dependen de iOS; no equivale a un servicio nativo de audio. La primera reproducción requiere una interacción del usuario. No incluye integración nativa con CarPlay.

## Alojar en Vercel

Selecciona `web` como **Root Directory**, framework Vite, compilación `npm run build` y salida `dist`. `web/vercel.json` configura la interfaz y las cabeceras del service worker. Mantén el dominio de producción para que las descargas sigan en el mismo origen; las URLs de preview tienen almacenes distintos.

**Esto publica la interfaz, no el servidor de música.** Los plugins de `vite.config.js` solo se ejecutan en el servidor local de desarrollo/preview, no dentro del `dist` de Vercel. Para buscar, reproducir canciones nuevas y descargar necesitas un backend HTTPS que implemente las rutas actuales:

- `/yt/*`, `/spotify/*`
- `/stream` (incluidos parámetros de consulta y cabecera Range / respuestas 206)
- `/lyrics/*`, `/letras/*`, `/lyrics-ovh/*`

Cuando tengas ese backend, añade `rewrites` al mismo `vercel.json`. Ejemplo con **un dominio de ejemplo que debes sustituir**, conservando las demás propiedades del archivo:

```json
"rewrites": [
  { "source": "/yt/:path*", "destination": "https://TU-BACKEND.example/yt/:path*" },
  { "source": "/spotify/:path*", "destination": "https://TU-BACKEND.example/spotify/:path*" },
  { "source": "/stream", "destination": "https://TU-BACKEND.example/stream" },
  { "source": "/lyrics/:path*", "destination": "https://TU-BACKEND.example/lyrics/:path*" },
  { "source": "/letras/:path*", "destination": "https://TU-BACKEND.example/letras/:path*" },
  { "source": "/lyrics-ovh/:path*", "destination": "https://TU-BACKEND.example/lyrics-ovh/:path*" }
]
```

No se ha contratado ni publicado un backend con este cambio. Antes de dar por operativo el despliegue, comprueba una descarga completa y una petición de audio con `Range`; los límites de tiempo y transferencia del alojamiento también se aplican. Publicar la app completa y su proxy bajo un mismo dominio HTTPS en un servidor es otra opción, sin separar la interfaz en Vercel.

## GitHub Pages

Pages solo sirve archivos estáticos y no puede ejecutar el proxy actual. Además, este proyecto usa rutas de API, iconos y service worker desde `/`: el subdirectorio predeterminado `/RitmoApp/` no está soportado por esta configuración. Para esta app conviene Vercel con el backend configurado, o un servidor HTTPS que sirva la app completa. Subir únicamente `dist` a Pages no basta para escuchar o descargar canciones nuevas.

## Verificación local

```powershell
cd web
npm ci
npm run build
npm run test:ui
```

Las pruebas de navegador comprueban instalación de la caché, manifiesto/iconos, apertura offline, audio local, letras y salto a más de 40 segundos. Una prueba automatizada con WebKit no sustituye la prueba física en Safari/iPhone (instalación desde Compartir, modo avión, pantalla bloqueada y cambio de canción).

Para probar también almacenamiento, peticiones offline por rangos y diseño con WebKit:

```powershell
npx playwright install webkit
$env:RITMO_TEST_BROWSER = 'webkit'
npm run test:ui
```

WebKit de Playwright en Windows tiene [limitaciones conocidas de audio](https://github.com/microsoft/playwright/issues/10892); en ese entorno se omite explícitamente la prueba completa de decodificación offline. Los bytes, rangos, persistencia, letras y caché de arranque se verifican por separado. En Edge sí se ejecuta la reproducción offline y el salto a 90 segundos. Falta comprobar la instalación y el audio en un iPhone físico antes de considerar validado Safari.

Referencias: [instalación desde Safari (Apple)](https://support.apple.com/en-in/guide/iphone/iphea86e5236/ios), [almacenamiento de WebKit](https://webkit.org/blog/14403/updates-to-storage-policy/), [rewrites externos de Vercel](https://vercel.com/docs/routing/rewrites), [GitHub Pages](https://docs.github.com/en/pages/getting-started-with-github-pages/what-is-github-pages).
