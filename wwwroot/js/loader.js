// Loader global. Uso:
//   Loader.show();   // muestra el circulo y bloquea toda la pagina (clics, teclado y botones)
//   Loader.hide();   // lo quita (llamarlo ANTES de mostrar un SweetAlert, que queda por debajo del loader)
//   Loader.isBusy(); // true mientras haya un proceso en curso (sirve para ignorar clics repetidos)
// Es un contador: si dos procesos lo piden a la vez, se oculta cuando termina el ultimo.
window.Loader = (function () {
    let overlay = null;
    let count = 0;

    function crear() {
        overlay = document.createElement('div');
        overlay.className = 'loader';
        overlay.hidden = true;
        overlay.setAttribute('role', 'status');
        overlay.setAttribute('aria-live', 'polite');
        overlay.innerHTML = '<div class="loader-spinner"></div><div class="loader-text">Cargando…</div>';
        document.body.appendChild(overlay);
    }

    // "inert" deshabilita clics, foco y teclado de todo el contenido que queda detras del loader.
    function bloquear(bloqueado) {
        Array.from(document.body.children).forEach(function (el) {
            if (el === overlay) return;
            if (bloqueado) {
                if (!el.inert) { el.inert = true; el.dataset.loaderInert = '1'; }
            } else if (el.dataset.loaderInert) {
                el.inert = false;
                delete el.dataset.loaderInert;
            }
        });
        document.body.setAttribute('aria-busy', bloqueado ? 'true' : 'false');
    }

    function show() {
        if (!overlay) crear();
        count++;
        if (count > 1) return;
        overlay.hidden = false;
        bloquear(true);
    }

    function hide() {
        if (count === 0) return;
        count--;
        if (count > 0) return;
        overlay.hidden = true;
        bloquear(false);
    }

    function isBusy() { return count > 0; }

    // Si el usuario vuelve con "Atras" a una pagina que quedo cargando, se restaura limpia.
    window.addEventListener('pageshow', function (e) {
        if (e.persisted && overlay) { count = 1; hide(); }
    });

    return { show: show, hide: hide, isBusy: isBusy };
})();
