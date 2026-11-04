using CasaApuestas.Data.Mongo;
using CasaApuestas.Models.Catalogo;

namespace CasaApuestas.Services.Simulador
{
    /// <summary>
    /// Elige los 11 titulares de un equipo a partir de su plantilla del catalogo (MySQL). Es PURO y determinista: la misma
    /// plantilla da siempre la misma alineacion (por eso repetirlo es seguro).
    ///   - Formacion fija 4-4-2: 1 portero, 4 defensas, 4 mediocampistas y 2 delanteros, tomando en cada posicion a los de
    ///     menor numero de camiseta (los que no tienen numero van al final; el id desempata).
    ///   - Si a una posicion le faltan jugadores, el hueco se llena con los que sobran (en el mismo orden).
    ///   - Si el equipo no tiene posiciones en el catalogo (ocurre con algunos del seed), se toman los 11 primeros y la
    ///     formacion queda NULL: no se inventa una formacion que no se puede cumplir.
    /// Solo titulares: no se generan suplentes.
    /// </summary>
    public static class GeneradorAlineaciones
    {
        public const int Titulares = 11;
        public const string FormacionBase = "4-4-2";

        private static readonly (string Posicion, int Cantidad)[] Esquema =
        {
            ("PORTERO", 1), ("DEFENSA", 4), ("MEDIOCAMPISTA", 4), ("DELANTERO", 2)
        };

        public static (string? Formacion, IReadOnlyList<Jugador> Titulares) Elegir(IReadOnlyList<Jugador> plantilla)
        {
            var orden = plantilla
                .OrderBy(j => j.NumeroCamiseta ?? int.MaxValue)
                .ThenBy(j => j.IdJugador)
                .ToList();

            if (orden.All(j => string.IsNullOrWhiteSpace(j.Posicion)))
                return (null, orden.Take(Titulares).ToList());

            var elegidos = new List<Jugador>();
            foreach (var (posicion, cantidad) in Esquema)
                elegidos.AddRange(orden.Where(j => string.Equals(j.Posicion, posicion, StringComparison.OrdinalIgnoreCase)).Take(cantidad));

            // Relleno de huecos con quienes no entraron (cualquier posicion), hasta completar 11.
            if (elegidos.Count < Titulares)
            {
                var ya = elegidos.Select(j => j.IdJugador).ToHashSet();
                elegidos.AddRange(orden.Where(j => !ya.Contains(j.IdJugador)).Take(Titulares - elegidos.Count));
            }
            return (FormacionBase, elegidos);
        }

        /// <summary>Documento listo para guardar en MongoDB.</summary>
        public static AlineacionDocumento CrearDocumento(int idPartido, int idEquipo, IReadOnlyList<Jugador> plantilla)
        {
            var (formacion, titulares) = Elegir(plantilla);
            return new AlineacionDocumento
            {
                IdPartido = idPartido,
                IdEquipo = idEquipo,
                Tipo = AlineacionDocumento.TipoInicial,
                Formacion = formacion,
                Jugadores = titulares.Select(j => new AlineacionJugadorDocumento
                {
                    IdJugador = j.IdJugador,
                    Titular = true,
                    NumeroCamiseta = j.NumeroCamiseta,
                    Posicion = j.Posicion
                }).ToList()
            };
        }
    }
}
