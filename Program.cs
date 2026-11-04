using CasaApuestas.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Autenticacion por cookie (RF3). Al iniciar sesion el servidor emite una cookie cifrada y firmada
// con los claims del usuario; en cada peticion ASP.NET la lee y rellena HttpContext.User.
// No se guarda nada de sesion en el servidor, por eso no hace falta un almacen de sesiones.
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "SoccerBet.Auth";
        options.Cookie.HttpOnly = true;                              // el JavaScript de la pagina no puede leerla
        options.Cookie.SameSite = SameSiteMode.Lax;                  // no se envia en peticiones POST desde otros sitios
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // "Secure" cuando se navega por https (en desarrollo tambien se usa http)

        options.ExpireTimeSpan = TimeSpan.FromHours(8);              // 8 horas de vida
        options.SlidingExpiration = true;                            // cada peticion renueva el plazo mientras haya actividad

        options.LoginPath = "/Account/Login";                        // a donde se redirige a quien entra sin sesion ([Authorize])
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";                // no hay roles en este alcance; se reutiliza el login

        // Las llamadas del cupon (fetch con X-Requested-With) NO deben recibir un 302 al HTML del login: responden 401/403
        // para que el cupon pueda avisar "tu sesion caduco" sin romperse. Las paginas normales siguen redirigiendo al login.
        options.Events.OnRedirectToLogin = ctx =>
        {
            if (EsPeticionAjax(ctx.Request)) ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            else ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (EsPeticionAjax(ctx.Request)) ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            else ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
    });

// Antiforgery: los fetch (login y registro) envian el token en este encabezado, ademas de la cookie antiforgery.
// Asi un sitio externo no puede enviar esos POST en nombre del usuario.
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");

// Servicios de negocio (Services/): un caso de uso por clase, inyectados por interfaz.
builder.Services.AddScoped<CasaApuestas.Services.ISaldoService, CasaApuestas.Services.SaldoService>();
builder.Services.AddScoped<CasaApuestas.Services.IRecargaService, CasaApuestas.Services.RecargaService>();
builder.Services.AddScoped<CasaApuestas.Data.SqlServer.IAcreditacionRepository, CasaApuestas.Data.SqlServer.AcreditacionRepository>();

// Wompi (sandbox). Las llaves vienen de user-secrets; si falta alguna o no es de pruebas, la app NO arranca
// (mensaje sin valores). El HttpClient tipado enviara la llave privada solo en el encabezado Authorization.
builder.Services.Configure<CasaApuestas.Services.Wompi.WompiOptions>(
    builder.Configuration.GetSection(CasaApuestas.Services.Wompi.WompiOptions.Seccion));
builder.Configuration.GetSection(CasaApuestas.Services.Wompi.WompiOptions.Seccion)
    .Get<CasaApuestas.Services.Wompi.WompiOptions>()!.ValidarSandbox();
builder.Services.AddHttpClient<CasaApuestas.Services.Wompi.IWompiClient, CasaApuestas.Services.Wompi.WompiClient>();
builder.Services.AddSingleton<CasaApuestas.Services.Wompi.IWompiCheckoutBuilder, CasaApuestas.Services.Wompi.WompiCheckoutBuilder>();

// Firebase (Firestore): coleccion "pagos". FirestoreDb es seguro entre hilos y se recomienda UNA instancia
// por aplicacion, por eso es singleton. ProjectId y la ruta del JSON de la cuenta de servicio vienen de user-secrets.
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var projectId = cfg["Firebase:ProjectId"];
    var credencialesPath = cfg["Firebase:CredentialsPath"];
    if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(credencialesPath))
        throw new InvalidOperationException("Faltan Firebase:ProjectId o Firebase:CredentialsPath (configuralos con user-secrets).");
    if (!File.Exists(credencialesPath))
        throw new InvalidOperationException("El archivo de credenciales de Firebase no existe en la ruta configurada.");

    // CredentialsPath esta marcado como obsoleto (riesgo de seguridad: aceptaria cualquier tipo de credencial de un archivo).
    // Se carga explicitamente como CUENTA DE SERVICIO, que es lo unico que esperamos, y se entrega como GoogleCredential.
    var credencial = Google.Apis.Auth.OAuth2.CredentialFactory
        .FromFile<Google.Apis.Auth.OAuth2.ServiceAccountCredential>(credencialesPath)
        .ToGoogleCredential();

    return new Google.Cloud.Firestore.FirestoreDbBuilder
    {
        ProjectId = projectId,
        GoogleCredential = credencial
    }.Build();
});
builder.Services.AddSingleton<CasaApuestas.Data.Firebase.IPagoRepository, CasaApuestas.Data.Firebase.PagoRepository>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// MySQL (catalogo deportivo, solo lectura): contexto propio, separado del de SQL Server.
// La version del servidor es FIJA (contenedor mysql:8.4) en lugar de ServerVersion.AutoDetect: asi la app no abre una
// conexion al arrancar y, si MySQL esta apagado, solo falla el catalogo; el login y el resto del sitio siguen funcionando.
builder.Services.AddDbContext<CasaApuestas.Data.MySql.CatalogoDbContext>(options =>
    options.UseMySql(builder.Configuration.GetConnectionString("MySql"), new MySqlServerVersion(new Version(8, 4, 0))));
builder.Services.AddScoped<CasaApuestas.Data.MySql.ICatalogoRepository, CasaApuestas.Data.MySql.CatalogoRepository>();
builder.Services.AddScoped<CasaApuestas.Services.ICatalogoService, CasaApuestas.Services.CatalogoService>();

// PostgreSQL (partidos y cuotas, solo lectura por ahora): tercer contexto, separado de los otros dos.
builder.Services.AddDbContext<CasaApuestas.Data.Postgres.PartidosDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddScoped<CasaApuestas.Data.Postgres.IPartidoRepository, CasaApuestas.Data.Postgres.PartidoRepository>();

// Home: combina PostgreSQL (partidos) con MySQL (nombres y escudos) en la capa de servicios. El cache en memoria guarda
// unos minutos los equipos del catalogo. TimeProvider permite obtener "ahora" (y probarlo) sin llamar a DateTime directamente.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CasaApuestas.Services.IEquiposCache, CasaApuestas.Services.EquiposCache>();
builder.Services.AddScoped<CasaApuestas.Services.IPartidoService, CasaApuestas.Services.PartidoService>();
builder.Services.AddScoped<CasaApuestas.Services.IPosicionesService, CasaApuestas.Services.PosicionesService>();
builder.Services.AddScoped<CasaApuestas.Services.IDetallePartidoService, CasaApuestas.Services.DetallePartidoService>();

// Apuestas (RF9): limites desde appsettings.json ("Apuestas"); si faltan o son invalidos la app no arranca.
builder.Services.AddOptions<CasaApuestas.Services.ApuestasOptions>()
    .Bind(builder.Configuration.GetSection(CasaApuestas.Services.ApuestasOptions.Seccion))
    .Validate(CasaApuestas.Services.ApuestasOptions.EsValida,
        "Falta o es invalida la seccion 'Apuestas' de appsettings.json (ImporteMinimo, ImporteMaximo, MaxSelecciones, PremioMaximo).")
    .ValidateOnStart();
builder.Services.AddScoped<CasaApuestas.Data.SqlServer.IApuestaRepository, CasaApuestas.Data.SqlServer.ApuestaRepository>();

// Retiros simulados (RF16): el minimo viene de appsettings.json ("Retiros"); si falta o es invalido la app no arranca.
builder.Services.AddOptions<CasaApuestas.Services.RetirosOptions>()
    .Bind(builder.Configuration.GetSection(CasaApuestas.Services.RetirosOptions.Seccion))
    .Validate(CasaApuestas.Services.RetirosOptions.EsValida, "Falta o es invalida la seccion 'Retiros' de appsettings.json (ImporteMinimo).")
    .ValidateOnStart();
builder.Services.AddScoped<CasaApuestas.Data.SqlServer.IRetiroRepository, CasaApuestas.Data.SqlServer.RetiroRepository>();
builder.Services.AddScoped<CasaApuestas.Services.IRetiroService, CasaApuestas.Services.RetiroService>();
builder.Services.AddScoped<CasaApuestas.Services.IContextoCuotasLoader, CasaApuestas.Services.ContextoCuotasLoader>();
builder.Services.AddScoped<CasaApuestas.Services.IApuestaService, CasaApuestas.Services.ApuestaService>();
builder.Services.AddScoped<CasaApuestas.Services.IMisApuestasService, CasaApuestas.Services.MisApuestasService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// El orden importa: primero se identifica al usuario (cookie) y despues se autoriza.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

// fetch/XHR del cupon: el JavaScript envia X-Requested-With: XMLHttpRequest.
static bool EsPeticionAjax(HttpRequest request) =>
    string.Equals(request.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
