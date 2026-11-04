using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CasaApuestas.Infraestructura
{
    /// <summary>
    /// Hace que una accion SOLO exista en el entorno Development: en cualquier otro responde 404, como si no estuviera.
    /// Corre ANTES del antiforgery (Order muy bajo), asi un POST sin token en produccion tambien recibe 404 y no 400.
    /// Se usa para el modo demostracion (RF18), que no debe existir en produccion.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class SoloDevelopmentAttribute : Attribute, IAuthorizationFilter, IOrderedFilter
    {
        public int Order => -10_000;

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var entorno = context.HttpContext.RequestServices.GetRequiredService<IHostEnvironment>();
            if (!entorno.IsDevelopment()) context.Result = new NotFoundResult();
        }
    }
}
