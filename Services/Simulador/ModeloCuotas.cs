namespace CasaApuestas.Services.Simulador
{
    /// <summary>
    /// Cuotas a partir de goles esperados. Es PURO (solo matematicas). Se supone que los goles de cada equipo siguen una
    /// distribucion de Poisson independiente (el modelo clasico mas sencillo del futbol); de ahi salen las probabilidades de
    /// cada resultado, y la cuota es  (1 - margen) / probabilidad,  que deja el margen de la casa.
    /// Hoy lo usa el generador de jornadas (cuotas de antes del partido); las cuotas en vivo (E3) reutilizaran las mismas funciones.
    /// </summary>
    public static class ModeloCuotas
    {
        private const int MaxGoles = 12;          // la cola de Poisson mas alla de 12 goles es despreciable

        /// <summary>Probabilidad de exactamente k goles con media lambda.</summary>
        public static double Poisson(int k, double lambda)
        {
            if (lambda <= 0) return k == 0 ? 1.0 : 0.0;
            double p = Math.Exp(-lambda);
            for (var i = 1; i <= k; i++) p *= lambda / i;
            return p;
        }

        public static double[] DistribucionGoles(double lambda)
        {
            var d = new double[MaxGoles + 1];
            for (var k = 0; k <= MaxGoles; k++) d[k] = Poisson(k, lambda);
            return d;
        }

        public sealed record Probabilidades(double Local, double Empate, double Visitante, double Mas25, double Menos25, double AmbosSi, double AmbosNo);

        /// <summary>Probabilidades de los 7 resultados de los 3 mercados para un partido que empieza 0-0.</summary>
        public static Probabilidades Prepartido(double lambdaLocal, double lambdaVisitante) =>
            DesdeMarcador(0, 0, lambdaLocal, lambdaVisitante);

        /// <summary>
        /// Probabilidades con un marcador ya dado y los goles que aun se esperan (lo que queda de partido).
        /// Sirve igual al inicio (0-0 y todos los goles esperados) que en vivo (marcador actual y menos goles esperados).
        /// </summary>
        public static Probabilidades DesdeMarcador(int golesLocal, int golesVisitante, double restanLocal, double restanVisitante)
        {
            var dl = DistribucionGoles(restanLocal);
            var dv = DistribucionGoles(restanVisitante);
            double local = 0, empate = 0, visitante = 0, mas25 = 0, ambosSi = 0;

            for (var i = 0; i <= MaxGoles; i++)
            for (var j = 0; j <= MaxGoles; j++)
            {
                var p = dl[i] * dv[j];
                int fl = golesLocal + i, fv = golesVisitante + j;
                if (fl > fv) local += p; else if (fl == fv) empate += p; else visitante += p;
                if (fl + fv >= 3) mas25 += p;
                if (fl >= 1 && fv >= 1) ambosSi += p;
            }

            // Se normaliza para absorber lo que quedo fuera de la tabla (MaxGoles).
            var total = local + empate + visitante;
            return new Probabilidades(local / total, empate / total, visitante / total, mas25 / total, 1 - mas25 / total, ambosSi / total, 1 - ambosSi / total);
        }

        /// <summary>Cuota con margen de la casa, redondeada a 2 decimales y nunca menor que el minimo.</summary>
        public static decimal Cuota(double probabilidad, double margen, decimal minima)
        {
            if (probabilidad <= 0) return 99m;
            var c = (decimal)Math.Round((1 - margen) / probabilidad, 2, MidpointRounding.AwayFromZero);
            return Math.Max(c, minima);
        }

        /// <summary>Fuerza de un equipo entre 0,75 y 1,25, estable (el mismo equipo siempre tiene la misma). Es lo que hace que unos equipos sean favoritos.</summary>
        public static double Fuerza(int idEquipo) => 0.75 + new Random(idEquipo * 7919 + 13).NextDouble() * 0.5;

        /// <summary>
        /// Reparte los goles esperados entre local y visitante segun la fuerza de cada equipo, con una pequeña ventaja de local (10 %).
        /// </summary>
        public static (double Local, double Visitante) GolesEsperadosPorFuerza(int idLocal, int idVisitante, double golesDelPartido)
        {
            double fl = 1.10 * Fuerza(idLocal), fv = Fuerza(idVisitante);
            var parte = fl / (fl + fv);
            return (golesDelPartido * parte, golesDelPartido * (1 - parte));
        }
    }
}
