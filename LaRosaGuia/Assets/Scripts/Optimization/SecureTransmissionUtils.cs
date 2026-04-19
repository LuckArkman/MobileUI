using UnityEngine;
using System.Text;
using System.Security.Cryptography;

namespace LuckArkman.XR.Optimization
{
    /// <summary>
    /// Fornece utilitários de segurança e integridade para a transmissão de metadados.
    /// </summary>
    public static class SecureTransmissionUtils
    {
        private static readonly string sharedSecret = "XR_SECURE_TOKEN_2026";

        /// <summary>
        /// Gera um hash SHA256 simples para validar a autenticidade dos comandos recebidos via Wi-Fi.
        /// </summary>
        public static string GenerateAuthToken(string payload)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(payload + sharedSecret));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        public static bool ValidateToken(string payload, string token)
        {
            return GenerateAuthToken(payload) == token;
        }
    }
}
