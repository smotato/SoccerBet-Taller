// Cupon de apuesta (RF9). Solo maneja la interfaz: el SERVIDOR valida todo al confirmar (cuota, partido, mercado, saldo).
// Lo que se guarda aqui (sessionStorage) puede estar viejo o alterado; el servidor siempre decide y, si algo difiere,
// responde con el motivo y las cuotas vigentes. Los limites vienen de appsettings.json (data-* del <aside id="cupon">).
(function () {
    'use strict';

    var raiz = document.getElementById('cupon');
    if (!raiz) return;

    var cuerpo = document.getElementById('cuponCuerpo');
    var tipoEl = document.getElementById('cuponTipo');
    var cfg = {
        autenticado: raiz.dataset.autenticado === '1',
        loginUrl: raiz.dataset.loginUrl,
        confirmarUrl: raiz.dataset.confirmarUrl,
        misApuestasUrl: raiz.dataset.misApuestasUrl,
        min: Number(raiz.dataset.min),
        max: Number(raiz.dataset.max),
        maxSel: Number(raiz.dataset.maxSel),
        premioMax: Number(raiz.dataset.premioMax)
    };
    var CLAVE_STORAGE = 'soccerbet.cupon.v1';
    var IMPORTES_RAPIDOS = [5000, 10000, 20000, 50000];

    // ---- Estado ------------------------------------------------------------------------------------------------
    // estado (persistente): clave de idempotencia, selecciones e importe.
    // ui (solo en memoria): avisos, errores por seleccion, cambios de cuota, envio en curso, ultimo resultado.
    var estado = cargar();
    var ui = { avisos: [], errores: {}, cambios: [], enviando: false, resultado: null };

    function cargar() {
        var vacio = { clave: null, picks: [], importe: '' };
        try {
            var d = JSON.parse(sessionStorage.getItem(CLAVE_STORAGE) || 'null');
            if (!d || !Array.isArray(d.picks)) return vacio;
            d.picks = d.picks.filter(function (p) { return p && Number.isInteger(p.idCuota) && isFinite(p.cuota); });
            return { clave: typeof d.clave === 'string' ? d.clave : null, picks: d.picks, importe: String(d.importe || '').replace(/\D/g, '') };
        } catch (e) { return vacio; }
    }
    function guardar() {
        try { sessionStorage.setItem(CLAVE_STORAGE, JSON.stringify(estado)); } catch (e) { /* sin almacenamiento: el cupon vive solo en esta pagina */ }
    }
    // Una clave (GUID) por cupon: el servidor la usa para no cobrar dos veces el mismo cupon.
    function nuevaClave() {
        if (window.crypto && crypto.randomUUID) return crypto.randomUUID();
        var b = new Uint8Array(16); crypto.getRandomValues(b);
        b[6] = (b[6] & 0x0f) | 0x40; b[8] = (b[8] & 0x3f) | 0x80;
        var h = Array.prototype.map.call(b, function (x) { return ('0' + x.toString(16)).slice(-2); }).join('');
        return h.slice(0, 8) + '-' + h.slice(8, 12) + '-' + h.slice(12, 16) + '-' + h.slice(16, 20) + '-' + h.slice(20);
    }

    // ---- Utilidades ------------------------------------------------------------------------------------------
    function el(tag, clase, texto) {
        var e = document.createElement(tag);
        if (clase) e.className = clase;
        if (texto !== undefined && texto !== null) e.textContent = texto;   // textContent: nunca se interpreta HTML
        return e;
    }
    function pesos(n) { return '$ ' + Math.round(n).toLocaleString('es-CO'); }
    function cuotaTxt(n) { return Number(n).toFixed(2); }
    function importeNum() { return parseInt(estado.importe, 10) || 0; }
    function cuotaTotal() {
        var t = estado.picks.reduce(function (a, p) { return a * p.cuota; }, 1);
        return Math.round(t * 10000) / 10000;
    }
    function tokenAntiforgery() {
        var i = raiz.querySelector('input[name="__RequestVerificationToken"]');
        return i ? i.value : '';
    }
    function limpiarAvisos() { ui.avisos = []; ui.errores = {}; ui.cambios = []; }

    // ---- Seleccionar cuotas en la pagina ------------------------------------------------------------------------
    function leerPick(b) {
        var d = b.dataset;
        return {
            idCuota: parseInt(d.cuotaId, 10), cuota: parseFloat(d.valor),
            partidoId: parseInt(d.partidoId, 10), mercadoId: parseInt(d.mercadoId, 10),
            partido: d.partidoTxt, mercado: d.mercadoTxt, seleccion: d.selTxt
        };
    }

    document.addEventListener('click', function (e) {
        var b = e.target.closest('.odd[data-cuota-id]');
        if (!b || b.disabled) return;
        var p = leerPick(b);
        if (!Number.isInteger(p.idCuota) || !isFinite(p.cuota)) return;
        alternar(p);
    });

    function alternar(p) {
        ui.resultado = null;          // si se acababa de registrar una apuesta, esto empieza un cupon nuevo
        limpiarAvisos();
        var i = estado.picks.findIndex(function (x) { return x.idCuota === p.idCuota; });
        if (i >= 0) {
            estado.picks.splice(i, 1);                                   // pulsar de nuevo una cuota elegida la quita
        } else {
            // Regla: como maximo UNA seleccion por mercado en cada partido; otra del mismo mercado reemplaza a la anterior.
            var otra = estado.picks.findIndex(function (x) { return x.partidoId === p.partidoId && x.mercadoId === p.mercadoId; });
            if (otra >= 0) {
                estado.picks.splice(otra, 1);
                ui.avisos.push('Solo cabe una selección por mercado en cada partido: reemplazamos la anterior.');
            }
            if (estado.picks.length >= cfg.maxSel) {
                ui.avisos.push('Máximo ' + cfg.maxSel + ' selecciones por cupón.');
            } else {
                if (!estado.clave) estado.clave = nuevaClave();           // se genera al abrir el cupon
                estado.picks.push(p);
            }
        }
        if (estado.picks.length === 0) { estado.clave = null; estado.importe = ''; }
        guardar();

        // Quien no tiene sesion ve las cuotas, pero al elegir se le pide iniciar sesion. El cupon se conserva (sessionStorage).
        if (!cfg.autenticado && estado.picks.length > 0) { irALogin(); return; }
        render(); marcarCuotas();
    }

    function irALogin() {
        window.location.href = cfg.loginUrl + '?ReturnUrl=' + encodeURIComponent(window.location.pathname + window.location.search);
    }

    // Resalta en la pagina las cuotas que estan en el cupon (y sincroniza su valor con lo que la pagina muestra ahora).
    function marcarCuotas() {
        var ids = {};
        estado.picks.forEach(function (p) { ids[p.idCuota] = true; });
        document.querySelectorAll('.odd[data-cuota-id]').forEach(function (b) {
            var sel = !!ids[b.dataset.cuotaId];
            b.classList.toggle('sel', sel);
            b.setAttribute('aria-pressed', sel ? 'true' : 'false');
        });
    }
    function sincronizarConPagina() {
        var cambios = [];
        estado.picks.forEach(function (p) {
            var b = document.querySelector('.odd[data-cuota-id="' + p.idCuota + '"]');
            if (!b || b.disabled) return;
            var v = parseFloat(b.dataset.valor);
            if (isFinite(v) && Math.abs(v - p.cuota) > 0.00005) {
                cambios.push(p.seleccion + ' (' + p.partido + '): ' + cuotaTxt(p.cuota) + ' → ' + cuotaTxt(v));
                p.cuota = v;
            }
        });
        if (cambios.length) { ui.avisos.push('Algunas cuotas de tu cupón se actualizaron con las de esta página: ' + cambios.join('; ') + '.'); guardar(); }
    }

    // ---- Dibujo del cupon --------------------------------------------------------------------------------------
    function render() {
        cuerpo.textContent = '';
        tipoEl.textContent = '';

        if (ui.resultado) { renderExito(ui.resultado); return; }

        if (estado.picks.length === 0) {
            var vacio = el('div', 'ap-empty');
            vacio.appendChild(el('p', null, 'Elige una cuota para empezar.'));
            vacio.appendChild(el('small', 'fine', '1 selección = sencilla · 2 o más = combinada (máx. ' + cfg.maxSel + ').'));
            ui.avisos.forEach(function (a) { cuerpo.appendChild(el('div', 'ap-msg warn', a)); });
            cuerpo.appendChild(vacio);
            return;
        }

        tipoEl.textContent = estado.picks.length > 1 ? '· Combinada de ' + estado.picks.length : '· Sencilla';

        var lista = el('div', 'ap-picks');
        estado.picks.forEach(function (p, i) {
            var err = ui.errores[p.idCuota];
            var d = el('div', 'ap-pick' + (err ? ' bad' : ''));
            var x = el('button', 'ap-x', '×');
            x.type = 'button'; x.setAttribute('aria-label', 'Quitar ' + p.seleccion + ' del cupón');
            x.dataset.quitar = String(i);
            d.appendChild(x);
            d.appendChild(el('small', null, p.partido));
            var fila = el('b');
            fila.appendChild(el('span', null, p.seleccion));
            fila.appendChild(el('span', null, cuotaTxt(p.cuota)));
            d.appendChild(fila);
            d.appendChild(el('small', null, p.mercado));
            if (err) {
                d.appendChild(el('div', 'ap-pick-err', err));
                var q = el('button', 'ap-link', 'Quitar esta selección');
                q.type = 'button'; q.dataset.quitar = String(i);
                d.appendChild(q);
            }
            lista.appendChild(d);
        });
        cuerpo.appendChild(lista);

        ui.avisos.forEach(function (a) { cuerpo.appendChild(el('div', 'ap-msg warn', a)); });
        if (ui.cambios.length) {
            var c = el('div', 'ap-msg warn');
            c.appendChild(el('b', null, 'Las cuotas cambiaron. '));
            c.appendChild(document.createTextNode('Revisa las nuevas y confirma de nuevo si quieres apostar:'));
            var ul = el('ul', 'ap-cambios');
            ui.cambios.forEach(function (k) {
                ul.appendChild(el('li', null, k.seleccion + ' (' + k.partido + '): ' + cuotaTxt(k.anterior) + ' → ' + cuotaTxt(k.actual)));
            });
            c.appendChild(ul);
            cuerpo.appendChild(c);
        }

        // Importe
        var bloque = el('div', 'ap-importe');
        var lb = el('label', 'ap-label', 'Importe'); lb.htmlFor = 'cuponImporte';
        bloque.appendChild(lb);
        var money = el('div', 'ap-money'); money.id = 'cuponMoney';
        money.appendChild(el('span', null, '$'));
        var inp = el('input'); inp.id = 'cuponImporte'; inp.type = 'text'; inp.inputMode = 'numeric'; inp.placeholder = '0'; inp.autocomplete = 'off';
        inp.value = estado.importe ? Number(estado.importe).toLocaleString('es-CO') : '';
        money.appendChild(inp);
        bloque.appendChild(money);
        var rapidos = el('div', 'ap-quick');
        IMPORTES_RAPIDOS.forEach(function (v) {
            var q = el('button', null, pesos(v)); q.type = 'button'; q.dataset.rapido = String(v);
            rapidos.appendChild(q);
        });
        bloque.appendChild(rapidos);
        bloque.appendChild(el('div', 'fine', 'Mínimo ' + pesos(cfg.min) + ' · máximo ' + pesos(cfg.max)));
        cuerpo.appendChild(bloque);

        var resumen = el('div'); resumen.id = 'cuponResumen';
        cuerpo.appendChild(resumen);
        renderResumen();
    }

    // Resumen, validacion rapida y boton: se redibuja al escribir el importe sin perder el foco del campo.
    function renderResumen() {
        var r = document.getElementById('cuponResumen');
        if (!r) return;
        r.textContent = '';
        var v = importeNum(), total = cuotaTotal();
        var err = '';
        if (v && v < cfg.min) err = 'El importe mínimo es ' + pesos(cfg.min) + '.';
        else if (v > cfg.max) err = 'El importe máximo es ' + pesos(cfg.max) + '.';
        var money = document.getElementById('cuponMoney');
        if (money) money.classList.toggle('bad', !!err);
        if (err) r.appendChild(el('div', 'ap-msg err', err));

        var sum = el('div', 'ap-sum');
        function fila(a, b, clase) { var d = el('div', clase); d.appendChild(el('span', null, a)); d.appendChild(el('b', null, b)); sum.appendChild(d); }
        fila('Cuota total', cuotaTxt(total));
        fila('Importe', v ? pesos(v) : '–');
        var potencial = Math.min(v * total, cfg.premioMax);
        fila('Ganancia potencial', v ? pesos(potencial) : '–', 'big');
        r.appendChild(sum);
        if (v && v * total > cfg.premioMax)
            r.appendChild(el('div', 'ap-msg warn', 'El premio máximo por apuesta es ' + pesos(cfg.premioMax) + '.'));
        else
            r.appendChild(el('div', 'fine', 'Incluye tu importe. Se paga solo si aciertas todas las selecciones.'));

        var btn = el('button', 'ap-btn', ui.enviando ? 'Enviando…' : (!cfg.autenticado ? 'Iniciar sesión para apostar'
            : (ui.cambios.length ? 'Aceptar nuevas cuotas y apostar' : 'Confirmar apuesta')));
        btn.type = 'button'; btn.id = 'cuponConfirmar';
        btn.disabled = cfg.autenticado && (ui.enviando || !v || !!err || estado.picks.length === 0);
        r.appendChild(btn);
        r.appendChild(el('div', 'fine', 'Dinero de prueba, sin retiros.'));
    }

    function renderExito(res) {
        var a = res.apuesta;
        tipoEl.textContent = '';
        cuerpo.appendChild(el('div', 'ap-msg ok', res.mensaje));
        a.selecciones.forEach(function (s) {
            var d = el('div', 'ap-pick done');
            d.appendChild(el('small', null, s.partido));
            var f = el('b'); f.appendChild(el('span', null, s.seleccion)); f.appendChild(el('span', null, cuotaTxt(s.cuota)));
            d.appendChild(f);
            d.appendChild(el('small', null, s.mercado));
            cuerpo.appendChild(d);
        });
        var sum = el('div', 'ap-sum');
        function fila(x, y, clase) { var d = el('div', clase); d.appendChild(el('span', null, x)); d.appendChild(el('b', null, y)); sum.appendChild(d); }
        fila('Apuesta #' + a.idApuesta + ' · ' + (a.selecciones.length > 1 ? 'combinada de ' + a.selecciones.length : 'sencilla'), '');
        fila('Cuota total', cuotaTxt(a.cuotaTotal));
        fila('Importe', pesos(a.importe));
        fila('Ganancia potencial', pesos(a.posibleGanancia), 'big');
        cuerpo.appendChild(sum);
        if (a.importe * a.cuotaTotal > a.posibleGanancia + 1)
            cuerpo.appendChild(el('div', 'ap-msg warn', 'El premio se limitó al máximo por apuesta.'));
        var otra = el('button', 'ap-btn', 'Seguir apostando'); otra.type = 'button'; otra.id = 'cuponNuevo';
        cuerpo.appendChild(otra);
        var ver = el('a', 'ap-btn ap-btn-sec', 'Ver Mis apuestas'); ver.href = cfg.misApuestasUrl;
        cuerpo.appendChild(ver);
    }

    // ---- Eventos del cupon ------------------------------------------------------------------------------------
    raiz.addEventListener('click', function (e) {
        var q = e.target.closest('[data-quitar]');
        if (q) {
            estado.picks.splice(parseInt(q.dataset.quitar, 10), 1);
            limpiarAvisos();
            if (estado.picks.length === 0) { estado.clave = null; estado.importe = ''; }
            guardar(); render(); marcarCuotas();
            return;
        }
        var r = e.target.closest('[data-rapido]');
        if (r) { estado.importe = r.dataset.rapido; guardar(); var i = document.getElementById('cuponImporte'); if (i) i.value = Number(estado.importe).toLocaleString('es-CO'); renderResumen(); return; }
        if (e.target.closest('#cuponNuevo')) { ui.resultado = null; limpiarAvisos(); render(); return; }
        if (e.target.closest('#cuponConfirmar')) confirmar();
    });

    raiz.addEventListener('input', function (e) {
        if (e.target.id !== 'cuponImporte') return;
        estado.importe = e.target.value.replace(/\D/g, '').slice(0, 9);
        e.target.value = estado.importe ? Number(estado.importe).toLocaleString('es-CO') : '';
        guardar(); renderResumen();
    });

    // ---- Confirmar ---------------------------------------------------------------------------------------------
    async function confirmar() {
        if (ui.enviando || estado.picks.length === 0) return;
        if (!cfg.autenticado) { irALogin(); return; }
        ui.enviando = true; limpiarAvisos(); renderResumen();

        var cuerpoPeticion = {
            clave: estado.clave,
            importe: importeNum(),
            selecciones: estado.picks.map(function (p) { return { idCuota: p.idCuota, cuota: p.cuota }; })
        };
        var resp, data = null;
        try {
            resp = await fetch(cfg.confirmarUrl, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Accept': 'application/json',
                    'X-Requested-With': 'XMLHttpRequest',               // asi la sesion caducada responde 401 y no un 302
                    'RequestVerificationToken': tokenAntiforgery()
                },
                body: JSON.stringify(cuerpoPeticion)
            });
            if (resp.headers.get('content-type') && resp.headers.get('content-type').indexOf('json') >= 0) data = await resp.json();
        } catch (e) {
            ui.enviando = false;
            ui.avisos.push('No se pudo contactar al servidor. Si reintentas con este mismo cupón no se cobrará dos veces.');
            render(); return;
        }
        ui.enviando = false;

        if (resp.status === 401) {
            ui.avisos.push('Tu sesión caducó. Inicia sesión de nuevo; tu cupón se conserva.');
            render();
            var l = el('button', 'ap-btn', 'Iniciar sesión'); l.type = 'button'; l.addEventListener('click', irALogin); cuerpo.appendChild(l);
            return;
        }
        if (resp.status === 400 && !data) {
            ui.avisos.push('La página caducó. Recárgala e inténtalo de nuevo (tu cupón se conserva).');
            render(); return;
        }
        if (!data) { ui.avisos.push('Respuesta inesperada del servidor. Inténtalo de nuevo.'); render(); return; }

        // El servidor manda las cuotas vigentes: se reemplazan las del cupon (el cupon guardado pudo estar viejo).
        (data.cuotasActuales || []).forEach(function (c) {
            var p = estado.picks.find(function (x) { return x.idCuota === c.idCuota; });
            if (p) p.cuota = c.cuota;
        });

        if (data.exito) {
            ui.resultado = data;
            if (data.saldoTexto) {
                var saldo = document.querySelector('.wallet .bal b');
                if (saldo) saldo.textContent = data.saldoTexto;
            }
            estado = { clave: null, picks: [], importe: '' };            // cupon nuevo: nueva clave cuando se elija otra cuota
            guardar(); render(); marcarCuotas();
            return;
        }

        if (data.codigo === 'CUOTAS_CAMBIADAS') {
            ui.cambios = data.cambios || [];
        } else {
            // Errores por seleccion: se marcan en la fila correspondiente (con opcion de quitarla); el resto, como avisos.
            (data.errores || []).forEach(function (er) {
                if (er.idCuota != null && estado.picks.some(function (p) { return p.idCuota === er.idCuota; })) ui.errores[er.idCuota] = er.mensaje;
                else ui.avisos.push(er.mensaje);
            });
            if (data.mensaje) ui.avisos.unshift(data.mensaje);
        }
        guardar(); render(); marcarCuotas();
    }

    // ---- Arranque ----------------------------------------------------------------------------------------------
    if (estado.picks.length) sincronizarConPagina();
    render(); marcarCuotas();
})();
