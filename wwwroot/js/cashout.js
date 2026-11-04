// Cash out en Mis apuestas (RF17). El navegador solo MUESTRA: el valor y el bloqueo los calcula el servidor (cuotas de PostgreSQL).
//  - Cada Vivo:IntervaloActualizacionSegundos (la MISMA configuracion del home, via <body data-vivo-intervalo>) pide los valores nuevos.
//  - "Cash out $ X" abre una confirmacion; al confirmar se envia el valor que el cliente VIO y el servidor recalcula. Si cambio, NO se
//    cobra: se muestra el nuevo valor y el cliente decide si lo acepta.
(function () {
    'use strict';

    var panel = document.getElementById('panelApuestas');
    if (!panel) return;
    var bloques = function () { return Array.prototype.slice.call(panel.querySelectorAll('.ap-co[data-apuesta-id]')); };
    if (!bloques().length) return;

    var urlValores = panel.dataset.urlValores, urlConfirmar = panel.dataset.urlConfirmar;
    var INTERVALO_MS = (parseInt(document.body.dataset.vivoIntervalo, 10) || 0) * 1000;
    var enviando = false;

    function pesos(n) { return '$ ' + Math.round(n).toLocaleString('es-CO'); }
    function token() { var i = document.querySelector('input[name="__RequestVerificationToken"]'); return i ? i.value : ''; }
    function el(tag, clase, texto) { var e = document.createElement(tag); if (clase) e.className = clase; if (texto != null) e.textContent = texto; return e; }

    // ---- Estado normal: texto + boton "Cash out $ X" (o bloqueado con el motivo) ------------------------------------------
    function pintarNormal(b) {
        var disponible = b.dataset.disponible === '1', valor = Number(b.dataset.valor), motivo = b.dataset.motivo;
        b.textContent = '';
        b.appendChild(el('span', 'fine ap-co-txt', disponible ? 'Valor actual según las cuotas. Se recalcula al confirmar.' : (motivo || 'El cash out no está disponible ahora.')));
        var btn = el('button', 'btn ap-co-btn', 'Cash out' + (disponible ? ' ' + pesos(valor) : ''));
        btn.type = 'button'; btn.disabled = !disponible;
        b.appendChild(btn);
        delete b.dataset.modo;
    }

    // ---- Confirmacion ----------------------------------------------------------------------------------------------------
    function pintarConfirmar(b, valor, aviso) {
        b.dataset.modo = 'confirmar';
        b.dataset.valorVisto = String(valor);
        b.textContent = '';
        var txt = el('span', 'ap-co-conf');
        if (aviso) txt.appendChild(el('b', null, aviso + ' '));
        txt.appendChild(document.createTextNode('Recibirás '));
        txt.appendChild(el('b', null, pesos(valor)));
        txt.appendChild(document.createTextNode(' ahora y la apuesta se cierra.'));
        b.appendChild(txt);
        var acc = el('span', 'ap-co-acc');
        var ok = el('button', 'btn', 'Confirmar cash out'); ok.type = 'button'; ok.dataset.accion = 'confirmar';
        var no = el('button', 'btn ghost', 'Cancelar'); no.type = 'button'; no.dataset.accion = 'cancelar';
        acc.appendChild(ok); acc.appendChild(document.createTextNode(' ')); acc.appendChild(no);
        b.appendChild(acc);
    }

    function mensaje(b, texto, clase) {
        var m = b.querySelector('.ap-co-msg');
        if (!m) { m = el('div', 'ap-msg'); m.classList.add('ap-co-msg'); b.appendChild(m); }
        m.className = 'ap-msg ap-co-msg ' + (clase || 'warn'); m.textContent = texto;
    }

    panel.addEventListener('click', async function (e) {
        var b = e.target.closest('.ap-co[data-apuesta-id]');
        if (!b) return;

        if (e.target.closest('.ap-co-btn') && !e.target.closest('.ap-co-btn').disabled) {
            pintarConfirmar(b, Number(b.dataset.valor));
            return;
        }
        var accion = e.target.closest('[data-accion]');
        if (!accion) return;
        if (accion.dataset.accion === 'cancelar') { pintarNormal(b); return; }

        if (enviando) return;
        enviando = true;
        accion.disabled = true;
        var visto = Number(b.dataset.valorVisto);
        try {
            var resp = await fetch(urlConfirmar, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest', 'RequestVerificationToken': token() },
                body: JSON.stringify({ idApuesta: Number(b.dataset.apuestaId), valorVisto: visto })
            });
            var data = null;
            if ((resp.headers.get('content-type') || '').indexOf('json') >= 0) data = await resp.json();

            if (resp.status === 401) { pintarConfirmar(b, visto); mensaje(b, 'Tu sesión caducó. Inicia sesión de nuevo.', 'warn'); }
            else if (resp.status === 400 && !data) { pintarConfirmar(b, visto); mensaje(b, 'La página caducó. Recárgala e inténtalo de nuevo.', 'warn'); }
            else if (data && data.exito) {
                if (data.saldoTexto) { var s = document.querySelector('.wallet .bal b'); if (s) s.textContent = data.saldoTexto; }
                b.textContent = '';
                b.appendChild(el('div', 'ap-msg ok', data.mensaje));
                setTimeout(function () { window.location.reload(); }, 1200);      // la tarjeta pasa a CASHOUT con su valor cobrado
            } else if (data && data.codigo === 'CAMBIO') {
                // NO se cobro. Se muestra el valor nuevo y el cliente decide: "Confirmar" ahora enviaria ESE valor.
                pintarConfirmar(b, data.nuevoValor, 'El valor cambió.');
            } else {
                pintarNormal(b);
                mensaje(b, (data && data.mensaje) || 'No pudimos hacer el cash out. Inténtalo de nuevo.', 'err');
            }
        } catch (err) {
            pintarConfirmar(b, visto); mensaje(b, 'No se pudo contactar al servidor. Revisa Mis apuestas antes de reintentar.', 'warn');
        } finally { enviando = false; }
    });

    // ---- Refresco periodico (mismo intervalo que el home) ---------------------------------------------------------------
    async function refrescar() {
        if (document.hidden || enviando || !urlValores) return;
        try {
            var resp = await fetch(urlValores, { headers: { 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' }, cache: 'no-store' });
            if (!resp.ok) return;
            var lista = await resp.json();
            lista.forEach(function (v) {
                var b = panel.querySelector('.ap-co[data-apuesta-id="' + v.idApuesta + '"]');
                // Mientras el cliente esta confirmando NO se le cambia el valor bajo los ojos: el servidor avisara si cambio.
                if (!b || b.dataset.modo === 'confirmar') return;
                b.dataset.valor = String(v.valor);
                b.dataset.disponible = v.disponible ? '1' : '0';
                b.dataset.motivo = v.motivo || '';
                pintarNormal(b);
            });
        } catch (err) { /* sin red: se reintenta */ }
    }
    if (INTERVALO_MS > 0) setInterval(refrescar, INTERVALO_MS);
})();
