namespace CasaApuestas.Services.Presentacion
{
    /// <summary>
    /// Hora de Colombia. Los partidos guardan fecha_hora como hora LOCAL de Colombia (timestamp sin zona), asi que "ahora"
    /// tambien debe ser hora de Colombia; comparar con DateTime.UtcNow desfasaria 5 horas.
    /// Colombia es UTC-5 todo el ano (no tiene horario de verano), por eso se usa un desfase fijo en lugar de buscar la zona
    /// por nombre ("SA Pacific Standard Time" en Windows, "America/Bogota" en Linux).
    /// </summary>
    public static class FechaColombia
    {
        private static readonly TimeSpan Desfase = TimeSpan.FromHours(-5);

        // Nombres propios (no CultureInfo) para que el resultado sea siempre el mismo, sin puntos ni cambios entre sistemas.
        private static readonly string[] Dias = { "dom", "lun", "mar", "mié", "jue", "vie", "sáb" };
        private static readonly string[] Meses = { "ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic" };

        /// <summary>Hora actual de Colombia (sin zona, comparable con fecha_hora).</summary>
        public static DateTime Ahora(TimeProvider reloj) =>
            DateTime.SpecifyKind(reloj.GetUtcNow().UtcDateTime + Desfase, DateTimeKind.Unspecified);

        /// <summary>"Hoy 21:00", "Mañana 20:00" o, para otras fechas, "jue 05 nov 20:00".</summary>
        public static string Formatear(DateTime fechaHora, DateTime ahora)
        {
            var hora = fechaHora.ToString("HH:mm");
            var dias = (fechaHora.Date - ahora.Date).Days;
            if (dias == 0) return $"Hoy {hora}";
            if (dias == 1) return $"Mañana {hora}";
            return $"{Dias[(int)fechaHora.DayOfWeek]} {fechaHora.Day:00} {Meses[fechaHora.Month - 1]} {hora}";
        }
    }
}
