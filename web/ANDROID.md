# Ritmo para Android con Capacitor

La app Android empaqueta la misma web que sirve Vite. El modo teléfono y las pantallas Android instaladas en un estéreo usan el diseño adaptable de `style.css`. Android Auto consume la biblioteca y los controles multimedia nativos de Media3.

## Abrir el proyecto

1. Desde esta carpeta ejecuta `npm install` si hace falta.
2. Ejecuta `npm run mobile:sync` para generar `dist` y sincronizar los archivos web con Android.
3. Abre `android/` en Android Studio.
4. Espera a que Gradle termine la sincronización y compila el APK.

También puedes abrir Android Studio desde la terminal con `npm run mobile:open`.

## Después de cambiar la web

Ejecuta `npm run mobile:sync` antes de compilar, para que el APK contenga la última versión web.

La app usa el servicio Media3 existente para reproducción en segundo plano, notificación y controles de Android Auto. Los favoritos y playlists de la web se sincronizan con la biblioteca local Android; las descargas se guardan en el almacenamiento privado de la app.
