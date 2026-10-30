# Ritmo v1.2.0

Primera publicación conjunta de Ritmo para Android y Windows.

## Descargas

- **Ritmo-Android.apk**: APK de Android firmado, para teléfonos, tablets y equipos Android de automóvil. Requiere Android 7.0 o superior.
- **Ritmo-Windows-x64.exe**: aplicación autónoma de Windows 10/11 de 64 bits, desarrollada en C# con WPF. Incluye .NET y se abre directamente.
- **SHA256SUMS.txt**: sumas SHA-256 para verificar ambos archivos.

## Incluye

- Reproductor propio con portada, avance y retroceso, aleatorio estable y repetición.
- Favoritos, creación de playlists e importación y respaldo CSV.
- Descargas dentro de la aplicación para escuchar sin conexión.
- Letras sincronizadas cuando están disponibles, o letra completa.
- Escritorio: cola móvil con la canción actual y hasta 20 próximas canciones; se renueva al avanzar.
- Escritorio: anterior, reproducir/pausar, siguiente y favorito desde la vista previa de la barra de tareas.
- Escritorio: carga de imágenes y audio fuera de la interfaz, miniaturas con memoria limitada y paneles reutilizables, conservando las funciones del reproductor.
- Android: integración con servicio de audio en segundo plano y biblioteca de Android Auto.

## Instalación

En Android, descarga el APK, ábrelo y autoriza la instalación desde el origen que hayas utilizado. Para actualizar una instalación previa, conserva el APK firmado con la misma clave.

En Windows, descarga y ejecuta `Ritmo-Windows-x64.exe`. No requiere Node.js, Vite, Java ni instalar .NET por separado.

## Notas

- Las bibliotecas son locales e independientes en cada plataforma; puedes trasladar listas mediante CSV.
- La búsqueda y la primera carga de canciones necesitan internet. Las descargas completas pueden reproducirse sin conexión.
- Windows prepara el audio completo en caché antes de reproducirlo; la primera carga puede tardar unos segundos.
- La disponibilidad de canciones y letras depende del catálogo y sus proveedores.
- El ejecutable de Windows no tiene firma de editor comercial.
