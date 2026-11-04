using CasaApuestas.Models.Catalogo;

namespace CasaApuestas.Services.Simulador
{
    /// <summary>
    /// Modelo sencillo (y explicable) de goles. Es PURO: no toca ninguna base ni el reloj, solo recibe numeros y un generador
    /// aleatorio; por eso es facil de probar y de repetir con una semilla.
    ///
    /// Idea: un partido tiene "goles esperados" (por defecto 2,6 entre los dos equipos). Las cuotas 1X2 INICIALES se convierten
    /// en probabilidades implicitas; el equipo con mas probabilidad se lleva una parte algo mayor de esos goles
    /// ("levemente ligada a las cuotas"). Despues, en cada minuto simulado, cada equipo marca con probabilidad
    /// goles_esperados_del_equipo / minutos_del_partido (un gol por minuto como maximo).
    /// </summary>
    public static class ModeloGoles
    {
        /// <summary>Reparte los goles esperados del partido entre local y visitante segun las cuotas 1X2 iniciales.</summary>
        public static (double Local, double Visitante) GolesEsperados(decimal? cuotaLocal, decimal? cuotaEmpate, decimal? cuotaVisitante,
            double golesDelPartido, double sesgo)
        {
            if (cuotaLocal is not > 1 || cuotaEmpate is not > 1 || cuotaVisitante is not > 1)
                return (golesDelPartido / 2, golesDelPartido / 2);

            // Probabilidad implicita = 1 / cuota; se normaliza para quitar el margen de la casa.
            double pl = 1.0 / (double)cuotaLocal.Value, pe = 1.0 / (double)cuotaEmpate.Value, pv = 1.0 / (double)cuotaVisitante.Value;
            var suma = pl + pe + pv;
            pl /= suma; pv /= suma;

            // Parte de los goles que le toca al local: 50 % +/- sesgo x (ventaja del favorito), siempre entre 20 % y 80 %.
            var parteLocal = Math.Clamp(0.5 + sesgo * (pl - pv), 0.2, 0.8);
            return (golesDelPartido * parteLocal, golesDelPartido * (1 - parteLocal));
        }

        /// <summary>true si en este minuto ocurre el suceso, con "esperados" sucesos repartidos en los minutos del partido.</summary>
        public static bool OcurreEnUnMinuto(double esperadosEnElPartido, int minutosPartido, Random rng) =>
            rng.NextDouble() < Math.Min(1.0, esperadosEnElPartido / minutosPartido);

        // ---- Quien protagoniza el suceso: se elige al azar con pesos segun la posicion del jugador ----

        public static double PesoGol(string? posicion) => (posicion ?? "").ToUpperInvariant() switch
        {
            "DELANTERO" => 6.0,
            "MEDIOCAMPISTA" => 3.0,
            "DEFENSA" => 1.0,
            "PORTERO" => 0.05,
            _ => 2.0                      // posicion desconocida (algunos equipos del seed no la tienen)
        };

        public static double PesoTarjeta(string? posicion) => (posicion ?? "").ToUpperInvariant() switch
        {
            "DEFENSA" => 3.0,
            "MEDIOCAMPISTA" => 3.0,
            "DELANTERO" => 1.5,
            "PORTERO" => 0.5,
            _ => 2.0
        };

        /// <summary>Elige un jugador al azar con probabilidad proporcional a su peso. null si no hay jugadores elegibles.</summary>
        public static Jugador? Elegir(IReadOnlyList<Jugador> jugadores, Func<Jugador, double> peso, ISet<int> excluidos, Random rng)
        {
            var candidatos = jugadores.Where(j => !excluidos.Contains(j.IdJugador)).ToList();
            if (candidatos.Count == 0) return null;
            var total = candidatos.Sum(peso);
            var punto = rng.NextDouble() * total;
            foreach (var j in candidatos)
            {
                punto -= peso(j);
                if (punto <= 0) return j;
            }
            return candidatos[^1];
        }
    }
}
