// Filtros del home (RF5): pestanas y chips filtran EN EL NAVEGADOR las filas ya cargadas, sin recargar y sin tocar la base de datos.
// Cada fila trae data-envivo (1/0), data-dia (hoy|manana|otro), data-liga y data-tipos (tipos de mercado). Un partido se ve si cumple
// la pestana Y el chip activos. vivo.js solo cambia el TEXTO de las filas (minuto, marcador, cuotas), nunca las oculta ni las quita,
// asi que el filtro sigue vigente tras cada actualizacion; si vivo.js recarga la pagina (un partido cambio de estado), el filtro se
// restaura desde sessionStorage.
(function () {
    'use strict';

    var tabs = document.getElementById('tabsHome');
    var chips = document.getElementById('chipsHome');
    if (!tabs || !chips) return;
    var vacio = document.getElementById('sinResultadosFiltro');
    var CLAVE = 'homeFiltro';

    // ---- Reglas (puras): que filas deja pasar cada pestana y cada chip ---------------------------------------------------
    var reglasTab = {
        proximo: function (f) { return f.envivo || f.dia === 'hoy' || f.dia === 'manana'; },    // en vivo + lo que comienza pronto
        competicion: function (f) { return f.liga !== ''; },                                      // partidos de una competicion
        todo: function () { return true; }
    };
    function pasaChip(chip, f) {
        if (chip === 'todo') return true;
        if (chip === 'envivo') return f.envivo;
        return f.tipos.indexOf(chip) >= 0;     // futbol / jugadores / datos: segun los tipos que declara la fila
    }
    function leerFila(el) {
        return { envivo: el.dataset.envivo === '1', dia: el.dataset.dia || '', liga: el.dataset.liga || '', tipos: (el.dataset.tipos || '').split(' ') };
    }

    function guardado() {
        try { var v = JSON.parse(sessionStorage.getItem(CLAVE) || 'null'); if (v && reglasTab[v.tab] && v.chip) return v; } catch (e) { /* sin storage */ }
        return null;
    }
    var estado = guardado() || { tab: 'proximo', chip: 'todo' };

    function aplicar() {
        var filas = Array.prototype.slice.call(document.querySelectorAll('.match'));
        var visibles = 0;
        filas.forEach(function (el) {
            var f = leerFila(el);
            var ver = reglasTab[estado.tab](f) && pasaChip(estado.chip, f);
            el.hidden = !ver;
            if (ver) visibles++;
        });

        // Titulos de liga y secciones: se ocultan si se quedaron sin filas visibles.
        document.querySelectorAll('.sec').forEach(function (sec) {
            var hijos = Array.prototype.slice.call(sec.children), titulo = null, hay = false, algunaSeccion = false;
            function cerrar() { if (titulo) titulo.hidden = !hay; }
            hijos.forEach(function (h) {
                if (h.classList.contains('comp')) { cerrar(); titulo = h; hay = false; }
                else if (h.classList.contains('match')) { if (!h.hidden) { hay = true; algunaSeccion = true; } }
            });
            cerrar();
            sec.hidden = !algunaSeccion;
        });

        if (vacio) vacio.hidden = visibles > 0 || filas.length === 0;

        tabs.querySelectorAll('[data-tab]').forEach(function (b) {
            var on = b.dataset.tab === estado.tab; b.classList.toggle('on', on); b.setAttribute('aria-selected', on ? 'true' : 'false');
        });
        chips.querySelectorAll('[data-chip]').forEach(function (b) {
            var on = b.dataset.chip === estado.chip; b.classList.toggle('on', on); b.setAttribute('aria-pressed', on ? 'true' : 'false');
        });
        try { sessionStorage.setItem(CLAVE, JSON.stringify(estado)); } catch (e) { /* sin storage */ }
    }

    tabs.addEventListener('click', function (e) {
        var b = e.target.closest('[data-tab]'); if (!b) return;
        estado.tab = b.dataset.tab; aplicar();
    });
    chips.addEventListener('click', function (e) {
        var b = e.target.closest('[data-chip]'); if (!b) return;
        estado.chip = b.dataset.chip; aplicar();
    });

    aplicar();
})();
