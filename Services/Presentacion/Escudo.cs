namespace CasaApuestas.Services.Presentacion
{
    /// <summary>Utilidades del escudo generico, compartidas por el catalogo y el home.</summary>
    public static class Escudo
    {
        private static readonly string[] PalabrasIgnoradas = { "de", "del", "la", "los" };

        /// <summary>
        /// Escudo generico provisional: primera letra de las dos primeras palabras, ignorando solo "de", "del", "la" y "los"
        /// ("Alianza FC" -> "AF", "Junior de Barranquilla" -> "JB"). Puede haber repetidas (Deportivo Pasto y Deportivo
        /// Pereira -> "DP"); los escudos reales las reemplazaran.
        /// </summary>
        public static string Iniciales(string nombre)
        {
            var palabras = nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !PalabrasIgnoradas.Contains(w, StringComparer.OrdinalIgnoreCase))
                .Take(2)
                .Select(w => char.ToUpperInvariant(w[0]))
                .ToArray();
            if (palabras.Length > 0) return new string(palabras);
            return nombre.Length >= 2 ? nombre[..2].ToUpperInvariant() : nombre.ToUpperInvariant();
        }
    }
}
