// Actualizacion en vivo (RF13): cada ~5 s pide /Partidos/Estado y refresca minuto, marcador y cuotas de los partidos de la pagina.
// Solo MUESTRA datos: habilita o deshabilita cuotas segun lo que informa el servidor y avisa al cupon con el evento 'vivo:cuota' (el
// cupon nunca cambia una cuota por su cuenta). Si el estado de un partido cambia (empezo, termino, se anulo) la pagina se recarga para
// reacomodar las secciones (el cupon se conserva en sessionStorage).
(function () {
    'use strict';

    // El intervalo sale de appsettings.json (Vivo:IntervaloActualizacionSegundos) via <body data-vivo-intervalo>: no hay otro numero aqui.
    var INTERVALO_MS = (parseInt(document.body.dataset.vivoIntervalo, 10) || 0) * 1000;
    if (INTERVALO_MS <= 0) return;
    var filas = Array.prototype.slice.call(document.querySelectorAll('[data-vivo-id]'));
    if (!filas.length) return;

    var ids = filas.map(function (f) { return f.dataset.vivoId; }).filter(function (v, i, a) { return a.indexOf(v) === i; });
    var recargando = false;

    function formato(n) { return Number(n).toFixed(2); }

    // Cuotas: actualiza el valor, marca ▲ / ▼ si subio o bajo respecto a lo que mostraba la pagina y deja las no disponibles apagadas.
    function pintarCuota(boton, cuota, estado) {
        var anterior = parseFloat(boton.dataset.valor);
        var b = boton.querySelector('b');
        if (b) b.textContent = formato(cuota.cuota);
        boton.dataset.valor = String(cuota.cuota);
        boton.classList.remove('up', 'dn');
        if (isFinite(anterior) && cuota.cuota > anterior + 0.004) boton.classList.add('up');
        else if (isFinite(anterior) && cuota.cuota < anterior - 0.004) boton.classList.add('dn');
        // Se puede pulsar si el partido esta PROGRAMADO o EN_VIVO y la cuota esta disponible (mercado abierto). El servidor lo
        // vuelve a validar al confirmar; esto solo refleja lo que muestra la pagina.
        boton.disabled = !((estado === 'PROGRAMADO' || estado === 'EN_VIVO') && cuota.disponible);
        document.dispatchEvent(new CustomEvent('vivo:cuota', { detail: { idCuota: cuota.idCuota, cuota: cuota.cuota, disponible: cuota.disponible } }));
    }

    function pintar(fila, p) {
        if (fila.dataset.vivoEstado !== p.estado) {
            // El partido cambio de estado: se recarga una sola vez para que cambie de seccion y de aspecto.
            if (!recargando) { recargando = true; window.location.reload(); }
            return;
        }
        var min = fila.querySelector('.vivo-min');
        if (min && p.minuto != null) min.textContent = p.minuto + "'";
        var gl = fila.querySelector('.vivo-gl'), gv = fila.querySelector('.vivo-gv');
        if (gl && p.golesLocal != null) gl.textContent = p.golesLocal;
        if (gv && p.golesVisitante != null) gv.textContent = p.golesVisitante;

        (p.cuotas || []).forEach(function (c) {
            var boton = document.querySelector('.odd[data-cuota-id="' + c.idCuota + '"]');
            if (boton) pintarCuota(boton, c, p.estado);
        });
    }

    async function actualizar() {
        if (document.hidden || recargando) return;
        try {
            var resp = await fetch('/Partidos/Estado?ids=' + ids.join(','), { headers: { 'Accept': 'application/json' }, cache: 'no-store' });
            if (!resp.ok) return;                       // error momentaneo: se reintenta en el siguiente intervalo
            var datos = await resp.json();
            datos.forEach(function (p) {
                filas.filter(function (f) { return f.dataset.vivoId === String(p.id); }).forEach(function (f) { pintar(f, p); });
            });
        } catch (e) { /* sin red: se reintenta */ }
    }

    setInterval(actualizar, INTERVALO_MS);
    document.addEventListener('visibilitychange', function () { if (!document.hidden) actualizar(); });
})();
