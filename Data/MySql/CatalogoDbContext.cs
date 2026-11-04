using CasaApuestas.Models.Catalogo;
using Microsoft.EntityFrameworkCore;

namespace CasaApuestas.Data.MySql
{
    /// <summary>
    /// Contexto del gestor MySQL (base soccerbet_catalogo), SEPARADO del de SQL Server: cada gestor tiene su propio contexto.
    /// Es de SOLO LECTURA: el seguimiento de cambios esta desactivado por defecto y, ademas, cada consulta usa AsNoTracking().
    /// Los nombres de tablas y columnas se mapean a mano (minusculas con guion bajo) para coincidir exactamente con
    /// db/mysql/01_catalogo.sql. No se declaran relaciones ni claves foraneas: las uniones se hacen en la capa de servicios.
    /// </summary>
    public class CatalogoDbContext : DbContext
    {
        public CatalogoDbContext(DbContextOptions<CatalogoDbContext> options) : base(options)
        {
            ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        }

        public DbSet<Liga> Ligas => Set<Liga>();
        public DbSet<Equipo> Equipos => Set<Equipo>();
        public DbSet<Estadio> Estadios => Set<Estadio>();
        public DbSet<EquipoEstadio> EquiposEstadios => Set<EquipoEstadio>();
        public DbSet<Jugador> Jugadores => Set<Jugador>();

        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.Entity<Liga>(e =>
            {
                e.ToTable("liga");
                e.HasKey(x => x.IdLiga);
                e.Property(x => x.IdLiga).HasColumnName("id_liga");
                e.Property(x => x.IdEmpresa).HasColumnName("id_empresa");
                e.Property(x => x.IdDeporte).HasColumnName("id_deporte");
                e.Property(x => x.Nombre).HasColumnName("nombre");
                e.Property(x => x.Pais).HasColumnName("pais");
                e.Property(x => x.Temporada).HasColumnName("temporada");
                e.Property(x => x.Activa).HasColumnName("activa");
                e.Property(x => x.LogoUrl).HasColumnName("logo_url");
            });

            mb.Entity<Equipo>(e =>
            {
                e.ToTable("equipo");
                e.HasKey(x => x.IdEquipo);
                e.Property(x => x.IdEquipo).HasColumnName("id_equipo");
                e.Property(x => x.IdLiga).HasColumnName("id_liga");
                e.Property(x => x.Nombre).HasColumnName("nombre");
                e.Property(x => x.Ciudad).HasColumnName("ciudad");
                e.Property(x => x.Activo).HasColumnName("activo");
                e.Property(x => x.EscudoUrl).HasColumnName("escudo_url");
            });

            mb.Entity<Estadio>(e =>
            {
                e.ToTable("estadio");
                e.HasKey(x => x.IdEstadio);
                e.Property(x => x.IdEstadio).HasColumnName("id_estadio");
                e.Property(x => x.Nombre).HasColumnName("nombre");
                e.Property(x => x.Ciudad).HasColumnName("ciudad");
                e.Property(x => x.Pais).HasColumnName("pais");
                e.Property(x => x.Capacidad).HasColumnName("capacidad");
            });

            mb.Entity<EquipoEstadio>(e =>
            {
                e.ToTable("equipo_estadio");
                e.HasKey(x => x.IdEquipoEstadio);
                e.Property(x => x.IdEquipoEstadio).HasColumnName("id_equipo_estadio");
                e.Property(x => x.IdEquipo).HasColumnName("id_equipo");
                e.Property(x => x.IdEstadio).HasColumnName("id_estadio");
                e.Property(x => x.EsPrincipal).HasColumnName("es_principal");
            });

            mb.Entity<Jugador>(e =>
            {
                e.ToTable("jugador");
                e.HasKey(x => x.IdJugador);
                e.Property(x => x.IdJugador).HasColumnName("id_jugador");
                e.Property(x => x.IdEquipo).HasColumnName("id_equipo");
                e.Property(x => x.Nombres).HasColumnName("nombres");
                e.Property(x => x.Apellidos).HasColumnName("apellidos");
                e.Property(x => x.NumeroCamiseta).HasColumnName("numero_camiseta");
                e.Property(x => x.FechaNacimiento).HasColumnName("fecha_nacimiento");
                e.Property(x => x.Nacionalidad).HasColumnName("nacionalidad");
                e.Property(x => x.Posicion).HasColumnName("posicion");
                e.Property(x => x.FotoUrl).HasColumnName("foto_url");
            });
        }
    }
}
