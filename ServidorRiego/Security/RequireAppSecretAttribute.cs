using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ServidorRiego.Security
{
    /// <summary>
    /// Exige que la petición incluya la cabecera "X-App-Secret" con el valor configurado en
    /// App:ClientSecret. Pensado para que solo la app Android (que lleva el secreto embebido)
    /// pueda llamar a ciertos endpoints públicos, como el registro de usuarios.
    ///
    /// No es una autenticación fuerte: un secreto embebido en un APK se puede extraer
    /// decompilando el paquete. Su objetivo es frenar bots y llamadas directas a la API
    /// (curl, Postman, scripts), no resistir a un atacante decidido a decompilar la app.
    ///
    /// En Development se omite por completo, para poder seguir probando desde Swagger sin
    /// tener que mandar la cabecera.
    /// </summary>
    public class RequireAppSecretAttribute : Attribute, IAsyncActionFilter
    {
        private const string HeaderName = "X-App-Secret";

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var env = context.HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
            if (!env.IsDevelopment())
            {
                var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                var expectedSecret = configuration["App:ClientSecret"];
                var providedSecret = context.HttpContext.Request.Headers[HeaderName].ToString();

                if (string.IsNullOrEmpty(expectedSecret) || !SecretsMatch(expectedSecret, providedSecret))
                {
                    context.Result = new ObjectResult(new { success = false, message = "Acceso no autorizado" })
                    {
                        StatusCode = StatusCodes.Status401Unauthorized
                    };
                    return;
                }
            }

            await next();
        }

        /// <summary>Comparación en tiempo constante para no filtrar el secreto por temporización</summary>
        private static bool SecretsMatch(string expected, string provided)
        {
            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            var providedBytes = Encoding.UTF8.GetBytes(provided);

            if (expectedBytes.Length != providedBytes.Length)
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
        }
    }
}
