let shellState = 'preparing';
let deferredInstall;
let installButton;
let status;
let offlineController;
export const supportsOfflineAudioRoute = () => Boolean(offlineController && offlineController === navigator.serviceWorker?.controller);
function checkOfflineRoute() {
  const controller = navigator.serviceWorker.controller;
  offlineController = null;
  if (!controller) return;
  const channel = new MessageChannel();
  const timeout = setTimeout(() => channel.port1.close(), 2000);
  channel.port1.onmessage = event => {
    if (event.data?.offlineAudio === true && controller === navigator.serviceWorker.controller) offlineController = controller;
    clearTimeout(timeout); channel.port1.close();
  };
  controller.postMessage({type:'ritmo-capabilities'}, [channel.port2]);
}
const standalone = () => matchMedia('(display-mode: standalone)').matches || navigator.standalone === true;

export async function protectOfflineStorage() {
  try { return await navigator.storage?.persist?.() || false; }
  catch { return false; }
}

function updateStatus() {
  if (!status) return;
  status.textContent = shellState === 'ready'
    ? 'La aplicación está lista para abrirse sin conexión. Descarga canciones desde este icono de Ritmo y espera el visto antes de desconectarte.'
    : shellState === 'insecure'
      ? 'Abre Ritmo desde una dirección HTTPS para habilitar la instalación y el arranque sin conexión en iPhone.'
      : shellState === 'unavailable'
        ? 'No se pudo preparar el arranque sin conexión. Abre de nuevo Ritmo con internet y revisa que el navegador permita guardar datos.'
        : shellState === 'development'
          ? 'El modo de desarrollo no guarda la aplicación sin conexión. Usa la versión publicada o npm run build y npm run preview.'
          : 'Preparando la aplicación para abrirla sin conexión…';
}

export function installHelpButton() {
  const button = document.createElement('button');
  button.className = 'btn ghost pwa-help';
  button.textContent = standalone() ? 'Uso sin conexión' : 'Instalar Ritmo';
  button.addEventListener('click', () => {
    const dialog = document.createElement('dialog');
    dialog.className = 'playlist-dialog pwa-dialog';
    dialog.setAttribute('aria-labelledby', 'pwa-title');
    dialog.innerHTML = `<h2 id="pwa-title">Instalar Ritmo</h2>
      <p>En iPhone: abre esta dirección en Safari, pulsa Compartir y Añadir a pantalla de inicio. Activa Abrir como app si aparece y pulsa Añadir.</p>
      <p>Abre Ritmo desde el nuevo icono y descarga allí tus canciones. Deja la app abierta hasta ver el visto. Las letras disponibles se guardan también.</p>
      <p>Las descargas ocupan espacio en el iPhone. No borres los datos del sitio si quieres conservarlas.</p>
      <p class="pwa-status" role="status"></p>
      <div class="pwa-actions"><button type="button" class="btn pwa-install" hidden>Instalar</button><button type="button" class="btn ghost pwa-close">Entendido</button></div>`;
    status = dialog.querySelector('.pwa-status');
    installButton = dialog.querySelector('.pwa-install');
    installButton.hidden = !deferredInstall || standalone();
    installButton.addEventListener('click', async () => {
      if (!deferredInstall) return;
      const prompt = deferredInstall;
      deferredInstall = null;
      installButton.hidden = true;
      await prompt.prompt();
    });
    dialog.querySelector('.pwa-close').addEventListener('click', () => dialog.close());
    dialog.addEventListener('close', () => { status = null; installButton = null; dialog.remove(); });
    document.body.append(dialog);
    updateStatus();
    dialog.showModal();
  });
  return button;
}

export function initializePwa() {
  window.addEventListener('beforeinstallprompt', event => {
    event.preventDefault(); deferredInstall = event;
    if (installButton) installButton.hidden = false;
  });
  window.addEventListener('appinstalled', () => {
    deferredInstall = null;
    if (installButton) installButton.hidden = true;
  });
  if (!isSecureContext) shellState = 'insecure';
  else if (!import.meta.env.PROD) shellState = 'development';
  else if (!('serviceWorker' in navigator)) shellState = 'unavailable';
  else {
    navigator.serviceWorker.addEventListener('controllerchange', checkOfflineRoute);
    checkOfflineRoute();
    navigator.serviceWorker.register('/sw.js').then(() => navigator.serviceWorker.ready).then(() => {
      checkOfflineRoute();
      shellState = 'ready'; updateStatus();
    }).catch(() => { shellState = 'unavailable'; updateStatus(); });
  }
  updateStatus();
}
