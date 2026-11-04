namespace CasaApuestas.Services.Recuperacion
{
    /// <summary>Seccion "Recuperacion" de appsettings.json (RF15). BaseUrl se usa para construir el enlace del correo.</summary>
    public class RecuperacionOptions
    {
        public const string Seccion = "Recuperacion";

        /// <summary>Direccion publica del sitio (por ejemplo https://localhost:7124). Se configura, NO se toma del encabezado Host (que el cliente puede falsear).</summary>
        public string BaseUrl { get; set; } = "";
        public int VigenciaMinutos { get; set; }
        public int MaxSolicitudesPorCorreoPorHora { get; set; }
        public int MaxSolicitudesPorIp { get; set; }
        public int VentanaIpMinutos { get; set; }
        /// <summary>Tiempo maximo de un envio de correo; pasado este tiempo se abandona (la pagina nunca espera mas).</summary>
        public int EnvioTimeoutSegundos { get; set; }

        public static bool EsValida(RecuperacionOptions o) =>
            Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
            && o.VigenciaMinutos > 0 && o.MaxSolicitudesPorCorreoPorHora > 0 && o.MaxSolicitudesPorIp > 0
            && o.VentanaIpMinutos > 0 && o.EnvioTimeoutSegundos > 0;
    }

    /// <summary>
    /// Seccion "Smtp". Usuario y Clave van SOLO en user-secrets (Smtp:Usuario, Smtp:Clave); el resto en appsettings.json.
    /// </summary>
    public class SmtpOptions
    {
        public const string Seccion = "Smtp";

        public bool Habilitado { get; set; }
        public string Servidor { get; set; } = "";
        public int Puerto { get; set; }
        public string NombreRemitente { get; set; } = "";
        /// <summary>Si queda vacio se usa Smtp:Usuario (Gmail exige que el remitente sea la cuenta autenticada o un alias suyo).</summary>
        public string CorreoRemitente { get; set; } = "";
        public string Usuario { get; set; } = "";
        public string Clave { get; set; } = "";

        public static bool EsValida(SmtpOptions o) =>
            !o.Habilitado || (!string.IsNullOrWhiteSpace(o.Servidor) && o.Puerto > 0 && !string.IsNullOrWhiteSpace(o.Usuario) && !string.IsNullOrWhiteSpace(o.Clave));
    }
}
