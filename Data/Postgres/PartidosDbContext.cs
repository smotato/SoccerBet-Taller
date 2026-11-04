using CasaApuestas.Models.Partidos;
using Microsoft.EntityFrameworkCore;

namespace CasaApuestas.Data.Postgres
{
    /// <summary>
    /// Contexto del gestor PostgreSQL (base soccerbet_partidos), separado del de SQL Server y del de MySQL.
    /// En esta fase es de SOLO LECTURA: sin seguimiento de cambios y sin SaveChanges. Los nombres de tablas y columnas se
    /// mapean a mano para coincidir exactamente con db/postgres/01_partidos.sql. Sin relaciones ni llaves foraneas en el modelo.
    /// Npgsql abre la conexion al ejecutar la primera consulta, no al arrancar la app.
    /// </summary>
    public class PartidosDbContext : DbContext
    {
        public PartidosDbContext(DbContextOptions<PartidosDbContext> options) : base(options)
        {
            ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        }

        public DbSet<Calendario> Calendarios => Set<Calendario>();
        public DbSet<Partido> Partidos => Set<Partido>();
        public DbSet<Mercado> Mercados => Set<Mercado>();
        public DbSet<Cuota> Cuotas => Set<Cuota>();

        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.Entity<Calendario>(e =>
            {
                e.ToTable("calendario");
                e.HasKey(x => x.IdCalendario);
                e.Property(x => x.IdCalendario).HasColumnName("id_calendario");
                e.Property(x => x.IdLiga).HasColumnName("id_liga");
                e.Property(x => x.NumeroJornada).HasColumnName("numero_jornada");
                e.Property(x => x.Temporada).HasColumnName("temporada");
                e.Property(x => x.FechaInicio).HasColumnName("fecha_inicio");
                e.Property(x => x.FechaFin).HasColumnName("fecha_fin");
                e.Property(x => x.Estado).HasColumnName("estado");
            });

            mb.Entity<Partido>(e =>
            {
                e.ToTable("partido");
                e.HasKey(x => x.IdPartido);
                e.Property(x => x.IdPartido).HasColumnName("id_partido");
                e.Property(x => x.IdCalendario).HasColumnName("id_calendario");
                e.Property(x => x.IdEstadio).HasColumnName("id_estadio");
                e.Property(x => x.IdEquipoLocal).HasColumnName("id_equipo_local");
                e.Property(x => x.IdEquipoVisitante).HasColumnName("id_equipo_visitante");
                e.Property(x => x.FechaHora).HasColumnName("fecha_hora");
                e.Property(x => x.GolesLocal).HasColumnName("goles_local");
                e.Property(x => x.GolesVisitante).HasColumnName("goles_visitante");
                e.Property(x => x.Estado).HasColumnName("estado");
                e.Property(x => x.FechaFinalizacion).HasColumnName("fecha_finalizacion");
            });

            mb.Entity<Mercado>(e =>
            {
                e.ToTable("mercado");
                e.HasKey(x => x.IdMercado);
                e.Property(x => x.IdMercado).HasColumnName("id_mercado");
                e.Property(x => x.Nombre).HasColumnName("nombre");
                e.Property(x => x.Descripcion).HasColumnName("descripcion");
                e.Property(x => x.Activo).HasColumnName("activo");
            });

            mb.Entity<Cuota>(e =>
            {
                e.ToTable("cuota");
                e.HasKey(x => x.IdCuota);
                e.Property(x => x.IdCuota).HasColumnName("id_cuota");
                e.Property(x => x.IdPartido).HasColumnName("id_partido");
                e.Property(x => x.IdMercado).HasColumnName("id_mercado");
                e.Property(x => x.Seleccion).HasColumnName("seleccion");
                e.Property(x => x.CodigoSeleccion).HasColumnName("codigo_seleccion");
                e.Property(x => x.Valor).HasColumnName("cuota");
                e.Property(x => x.Disponible).HasColumnName("disponible");
            });
        }
    }
}
