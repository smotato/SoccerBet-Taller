using CasaApuestas.Models;
using Microsoft.EntityFrameworkCore;

namespace CasaApuestas.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Usuario> Usuarios { get; set; } = null!;
        public DbSet<UsuarioEstado> UsuarioEstados { get; set; } = null!;
        public DbSet<Login> Logins { get; set; } = null!;

        // Solo lectura: consultar siempre con AsNoTracking() y nunca escribir desde C# (ver comentarios en las entidades).
        public DbSet<Cliente> Clientes { get; set; } = null!;
        public DbSet<Saldo> Saldos { get; set; } = null!;
    }
}
