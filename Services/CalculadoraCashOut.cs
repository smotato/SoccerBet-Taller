using CasaApuestas.Data.SqlServer;

namespace CasaApuestas.Services
{
    /// <summary>Opciones del cash out (seccion "CashOut" de appsettings.json). Factor = 0,95: el 5 % queda como margen de la casa.</summary>
    public class CashOutOptions
    {
        public const string Seccion = "CashOut";

        public decimal Factor { get; set; }

        public static bool EsValida(CashOutOptions o) => o.Factor > 0 && o.Factor <= 1;
    }

    /// <summary>Una seleccion tal como la necesita el calculo. CuotaActual / Disponible solo importan si Estado = PENDIENTE.</summary>
    public sealed record SeleccionCashOut(string Estado, decimal CuotaGuardada, decimal? CuotaActual, bool Disponible, string Descripcion, bool PartidoTerminado = false);

    /// <summary>Valor entero en pesos. Si Disponible es false, Motivo explica por que (se muestra al cliente).</summary>
    public sealed record ResultadoCashOut(bool Disponible, long Valor, string? Motivo);

    /// <summary>
    /// Cash out (RF17). Es PURO: solo recibe datos y calcula, asi se puede probar con estados forzados.
    ///
    ///   valor = importe x PRODUCTO de (cuota guardada / cuota actual) de cada seleccion x factor (0,95)
    ///     - seleccion PENDIENTE: cuota actual = la vigente en PostgreSQL.
    ///     - seleccion GANADA: cuota actual = 1,00 (ya es un acierto: cuenta su cuota guardada completa).
    ///     - seleccion ANULADA: no cuenta (factor 1).
    ///     - alguna seleccion PERDIDA: no hay cash out.
    ///   Se limita a la ganancia potencial y se redondea a pesos enteros (nunca mas que la parte entera de la ganancia potencial).
    ///
    /// No esta disponible si: una seleccion perdio; alguna seleccion pendiente tiene su mercado cerrado (cuota no disponible); no se pudo
    /// leer su cuota actual; o el valor es menor a $ 1.
    /// </summary>
    public static class CalculadoraCashOut
    {
        public static ResultadoCashOut Calcular(decimal importe, decimal posibleGanancia, IReadOnlyList<SeleccionCashOut> selecciones, decimal factor)
        {
            if (selecciones.Any(s => s.Estado == "PERDIDA"))
                return new(false, 0, "Una selección ya perdió: no hay cash out.");

            var activas = selecciones.Where(s => s.Estado != "ANULADA").ToList();
            if (activas.Count == 0)
                return new(false, 0, "No quedan selecciones activas.");

            decimal producto = 1m;
            string? motivo = null;
            foreach (var s in activas)
            {
                if (s.Estado == "GANADA")
                {
                    producto *= s.CuotaGuardada / 1.00m;
                    continue;
                }

                // PENDIENTE
                if (s.CuotaActual is not decimal actual || actual <= 0)
                {
                    motivo ??= "No pudimos leer las cuotas actuales. Inténtalo en unos segundos.";
                    continue;
                }
                producto *= s.CuotaGuardada / actual;
                if (s.PartidoTerminado) motivo ??= $"El partido de «{s.Descripcion}» terminó: la apuesta se liquidará en breve.";
                else if (!s.Disponible) motivo ??= $"Mercado cerrado: «{s.Descripcion}».";
            }

            var bruto = importe * producto * factor;
            var valor = Math.Min(Math.Round(bruto, 0, MidpointRounding.AwayFromZero), Math.Floor(posibleGanancia));
            var entero = (long)Math.Max(valor, 0);

            if (motivo is not null) return new(false, entero, motivo);
            if (entero < 1) return new(false, entero, "El valor de cash out es menor a $ 1.");
            return new(true, entero, null);
        }

        /// <summary>
        /// Evalua UNA apuesta a partir de sus filas de SQL Server (una por seleccion) y de las cuotas actuales de PostgreSQL.
        /// Solo tiene sentido para apuestas PENDIENTE.
        /// </summary>
        public static ResultadoCashOut Evaluar(IReadOnlyList<FilaApuesta> filas, ContextoCuotas ctx, decimal factor)
        {
            var primera = filas[0];
            var selecciones = filas.Select(f =>
            {
                if (f.EstadoSeleccion != "PENDIENTE")
                    return new SeleccionCashOut(f.EstadoSeleccion, f.Cuota, null, true, "");
                if (!ctx.Cuotas.TryGetValue(f.IdCuota, out var c))
                    return new SeleccionCashOut("PENDIENTE", f.Cuota, null, false, "");
                var t = ctx.Textos(c);
                var terminado = ctx.Partidos.TryGetValue(c.IdPartido, out var p) && p.Estado is not ("PROGRAMADO" or "EN_VIVO");
                return new SeleccionCashOut("PENDIENTE", f.Cuota, c.Valor, c.Disponible, terminado ? t.Partido : $"{t.Seleccion} · {t.Partido}", terminado);
            }).ToList();

            return Calcular(primera.Importe, primera.PosibleGanancia, selecciones, factor);
        }
    }
}
