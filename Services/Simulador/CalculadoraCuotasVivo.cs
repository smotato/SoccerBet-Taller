using CasaApuestas.Data.Postgres;

namespace CasaApuestas.Services.Simulador
{
    /// <summary>Cuota ya recalculada para un instante del partido.</summary>
    public sealed record CuotaVivaCalculada(int IdCuota, decimal Cuota, bool Disponible);

    /// <summary>
    /// Cuotas EN VIVO (RF13). Es PURA (solo matematicas): recibe el estado del partido y devuelve las cuotas nuevas.
    ///
    /// Modelo: igual que las cuotas de antes del partido (Poisson con los goles esperados de cada equipo), pero con el marcador
    /// actual y SOLO los goles que se esperan en el tiempo que queda. Para no dar un salto al empezar, la cuota viva parte de
    /// la cuota INICIAL y la mueve en proporcion a como cambio la probabilidad:
    ///
    ///        cuota_viva = cuota_inicial x (probabilidad_al_inicio / probabilidad_ahora)
    ///
    /// Asi, al minuto 0 con 0-0 la cuota es exactamente la inicial (con su margen de la casa); un gol del local baja la cuota
    /// del local y sube la del visitante; y con el paso del tiempo y el mismo marcador, el favorito se acerca a 1,01.
    /// Nunca baja de la cuota minima (1,01) ni pasa de 99.
    ///
    /// Una cuota NO esta disponible cuando: (a) hay una pausa tras un gol, (b) ya se alcanzo el minuto de cierre (85) o
    /// (c) el resultado esta casi decidido: probabilidad >= ProbCasiSegura (casi seguro) o <= 1 - ProbCasiSegura (casi imposible).
    /// </summary>
    public static class CalculadoraCuotasVivo
    {
        private const decimal CuotaMaxima = 99m;

        public static IReadOnlyList<CuotaVivaCalculada> Calcular(IReadOnlyList<CuotaPartidoSim> cuotas,
            double esperadosLocal, double esperadosVisitante, int minuto, int golesLocal, int golesVisitante,
            int minutosPartido, int minutoCierre, bool enPausa, decimal cuotaMinima, double probCasiSegura)
        {
            var inicio = ModeloCuotas.Prepartido(esperadosLocal, esperadosVisitante);

            var fraccionRestante = Math.Max(0, minutosPartido - minuto) / (double)minutosPartido;
            var ahora = ModeloCuotas.DesdeMarcador(golesLocal, golesVisitante,
                esperadosLocal * fraccionRestante, esperadosVisitante * fraccionRestante);

            var resultado = new List<CuotaVivaCalculada>();
            foreach (var c in cuotas)
            {
                var p0 = Probabilidad(inicio, c.CodigoSeleccion);
                var p = Probabilidad(ahora, c.CodigoSeleccion);
                if (p0 is null || p is null) continue;                 // codigo desconocido: no se toca

                decimal cuota = p.Value <= 0 ? CuotaMaxima : c.CuotaInicial * (decimal)(p0.Value / p.Value);
                cuota = Math.Round(Math.Clamp(cuota, cuotaMinima, CuotaMaxima), 2, MidpointRounding.AwayFromZero);

                var decidida = p.Value >= probCasiSegura || p.Value <= 1 - probCasiSegura;
                resultado.Add(new CuotaVivaCalculada(c.IdCuota, cuota, !enPausa && minuto < minutoCierre && !decidida));
            }
            return resultado;
        }

        private static double? Probabilidad(ModeloCuotas.Probabilidades p, string codigo) => codigo switch
        {
            "LOCAL" => p.Local,
            "EMPATE" => p.Empate,
            "VISITANTE" => p.Visitante,
            "MAS_2_5" => p.Mas25,
            "MENOS_2_5" => p.Menos25,
            "SI" => p.AmbosSi,
            "NO" => p.AmbosNo,
            _ => null
        };
    }
}
