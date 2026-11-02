// Formularios con atributo data-loader (p. ej. cerrar sesion): muestran el loader mientras se envian.
document.querySelectorAll('form[data-loader]').forEach(function (f) {
    f.addEventListener('submit', function () { Loader.show(); });
});

// Menu lateral ocultable (boton de 3 rayitas del _Layout).
// Abierto por defecto en pantallas anchas; cerrado en moviles (<= 900px), donde se superpone al contenido.
(function () {
    const shell = document.querySelector('.shell');
    const burger = document.getElementById('burger');
    if (!shell || !burger) return;

    function setMenu(open) {
        shell.classList.toggle('collapsed', !open);
        burger.setAttribute('aria-expanded', open);
    }

    burger.addEventListener('click', function () {
        setMenu(shell.classList.contains('collapsed'));
    });

    setMenu(window.innerWidth > 900);
})();
