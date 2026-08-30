using System.Security.Cryptography;
using System.Text;

namespace ServidorRiego.Utils
{
    /// <summary>
    /// Utilidades para generar y verificar tokens secretos opacos (refresh tokens, tokens de dispositivo, etc.).
    /// El valor en claro solo se entrega al cliente una vez; en base de datos siempre se guarda su hash.
    /// </summary>
    public static class TokenUtils
    {
        /// <summary>Genera un token aleatorio criptográficamente seguro codificado en Base64</summary>
        public static string GenerateSecureToken(int byteLength = 64)
        {
            var randomBytes = new byte[byteLength];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomBytes);
            return Convert.ToBase64String(randomBytes);
        }

        /// <summary>Calcula el hash SHA-256 (en hexadecimal) de un token, para almacenarlo de forma segura</summary>
        public static string HashToken(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes);
        }
    }
}
