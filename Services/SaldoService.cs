using CasaApuestas.Data;
using Microsoft.EntityFrameworkCore;

namespace CasaApuestas.Services
{
    /// <summary>Caso de uso: consultar el saldo actual de un cliente (RF11). Solo lectura.</summary>
    public interface ISaldoService
    {
        Task<decimal> ObtenerSaldoAsync(int idCliente, CancellationToken cancellationToken = default);
    }

    public class SaldoService : ISaldoService
    {
        private readonly ApplicationDbContext _context;

        public SaldoService(ApplicationDbContext context)
        {
            _context = context;
        }

        // El saldo vive en SQL Server (tabla Saldo). Aqui solo se lee, con AsNoTracking(): el dinero se mueve
        // unicamente con los procedimientos almacenados (sp_RegistrarApuesta, sp_AcreditarSaldo).
        public async Task<decimal> ObtenerSaldoAsync(int idCliente, CancellationToken cancellationToken = default)
        {
            var saldo = await _context.Saldos.AsNoTracking()
                .Where(s => s.IdCliente == idCliente)
                .Select(s => (decimal?)s.SaldoActual)
                .FirstOrDefaultAsync(cancellationToken);

            // Sin fila de saldo se considera 0.
            return saldo ?? 0m;
        }
    }
}
