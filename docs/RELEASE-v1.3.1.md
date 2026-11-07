# Ritmo 1.3.1

La barra de progreso de Windows conserva el punto elegido al hacer clic, y el cambio automático o manual de canción utiliza una nueva instancia de audio para evitar interferencias de eventos anteriores. La carga controlada de portadas y la virtualización de bibliotecas de la versión de escritorio quedan incluidas en esta publicación.

## Funciones compartidas

| Función | Windows | Android | Web |
| --- | --- | --- | --- |
| Favoritos, playlists locales, importación y respaldo CSV | Sí | Sí | Sí |
| Letra sincronizada o completa, con caché cuando está disponible | Sí | Sí | Sí |
| Cola actual + próximas 20, según el orden aleatorio real | Sí | Sí | Sí |
| Abrir canción actual sin reiniciar ni quitar la pausa | Sí | Sí | Sí |
| Descargas dentro de Ritmo y acceso offline | Archivos locales | Almacenamiento privado | IndexedDB |
| Historial reciente y acceso directo a letras/cola | Sí | Sí | Sí |

Android conserva sus protocolos de descarga, seek y reproducción nativa. La interfaz compartida incorpora Descargas, historial reciente y la cola limitada. Se mantiene la integración Android Auto. Windows conserva los controles de la barra de tareas.

En web, el arranque offline requiere abrir una vez la versión de producción (`npm run preview`) y mantener los datos del sitio. El catálogo online y las nuevas descargas necesitan el servidor de proxies. Los datos no se sincronizan automáticamente entre dispositivos.

## Verificación

- Pruebas Windows de lógica, reproducción y clics de progreso; apertura/cierre de paneles con 320 portadas reales.
- Transiciones naturales y manuales entre archivos PCM y M4A reales en carpetas aisladas; se comprueba el avance del reloj del reproductor.
- Pruebas JavaScript de aleatorio, cola, selección actual, CSV y ranking.
- Pruebas Edge de reproducción, pausa, descargas persistentes, recarga sin conexión, letras offline y seek a 90 segundos.
- Interfaz compartida verificada a 360 × 800, 1024 × 600 y 1440 × 900.
- APK release compilado y firmado con la clave existente; comprobación de firma y pruebas unitarias Gradle. No se certifica una prueba física de Android Auto en esta publicación.

## Archivos

- `Ritmo-Windows-x64.exe`: ejecutable portable con .NET incluido.
- `Ritmo-Android.apk`: actualización Android, versión 1.3.1, código 5.
- `Ritmo-Web.zip`: web compilada, fuentes y configuración del servidor.
- `SHA256SUMS.txt`: integridad de los paquetes.
