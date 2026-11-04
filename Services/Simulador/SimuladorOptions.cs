namespace CasaApuestas.Services.Simulador
{
    /// <summary>
    /// Configuracion del simulador de partidos (seccion "Simulador" de appsettings.json). Los valores NO se escriben en el
    /// codigo: si falta alguno o es invalido, la app no arranca con un mensaje claro. En las pruebas se reducen los
    /// tiempos con variables de entorno (por ejemplo Simulador__DuracionRealPartidoSegundos=45).
    /// </summary>
    public class SimuladorOptions
    {
        public const string Seccion = "Simulador";

        /// <summary>false = el simulador no corre en esta instancia (el resto del sitio funciona igual).</summary>
        public bool Habilitado { get; set; }

        /// <summary>Cada cuantos segundos reales el simulador revisa y avanza los partidos.</summary>
        public int IntervaloCicloSegundos { get; set; }

        /// <summary>Segundos reales que dura un partido completo (240 = 90 minutos de juego en 4 minutos).</summary>
        public int DuracionRealPartidoSegundos { get; set; }

        /// <summary>Minutos de juego de un partido (90).</summary>
        public int MinutosPartido { get; set; }

        /// <summary>Maximo de partidos EN_VIVO al mismo tiempo.</summary>
        public int MaxPartidosEnVivo { get; set; }

        /// <summary>Segundos reales minimos entre dos inicios de partido (arranque escalonado de los vencidos).</summary>
        public int EscalonInicioSegundos { get; set; }

        /// <summary>Goles esperados por partido, entre los dos equipos (promedio de la liga).</summary>
        public double GolesEsperadosPorPartido { get; set; }

        /// <summary>
        /// Cuanto pesan las cuotas en el reparto de goles entre local y visitante (0 = 50/50, 1 = muy ligado a las cuotas).
        /// Con 0,35 el favorito marca algo mas, sin volverse un resultado seguro.
        /// </summary>
        public double SesgoCuotas { get; set; }

        /// <summary>Tarjetas amarillas esperadas por partido (entre los dos equipos).</summary>
        public double TarjetasAmarillasEsperadas { get; set; }

        /// <summary>Probabilidad de que haya una tarjeta roja directa en un partido.</summary>
        public double ProbTarjetaRojaPorPartido { get; set; }

        /// <summary>Minuto que se asume para un partido EN_VIVO que no tiene minuto guardado (el seed deja el minuto en NULL).</summary>
        public int MinutoSinDato { get; set; }

        /// <summary>Semilla del generador aleatorio (para repetir una prueba con los mismos goles). null = aleatorio.</summary>
        public int? Semilla { get; set; }

        /// <summary>
        /// Si es true, cuando hay lugar para mas partidos en vivo y no hay ninguno vencido, el simulador adelanta a "ahora" la
        /// hora del siguiente partido PROGRAMADO para que la demo no quede quieta. Apagado por defecto; se enciende en
        /// appsettings.Development.json.
        /// </summary>
        public bool AdelantarPartidosProgramados { get; set; }

        public static bool EsValida(SimuladorOptions o) =>
            o.IntervaloCicloSegundos > 0 && o.DuracionRealPartidoSegundos > 0 && o.MinutosPartido > 0
            && o.MaxPartidosEnVivo > 0 && o.EscalonInicioSegundos >= 0 && o.GolesEsperadosPorPartido > 0
            && o.SesgoCuotas is >= 0 and <= 1 && o.TarjetasAmarillasEsperadas >= 0
            && o.ProbTarjetaRojaPorPartido is >= 0 and <= 1 && o.MinutoSinDato >= 0 && o.MinutoSinDato < o.MinutosPartido;
    }
}
