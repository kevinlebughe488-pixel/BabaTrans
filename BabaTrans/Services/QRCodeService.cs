using QRCoder;
using System.Security.Cryptography;
using System.Text;

namespace BabaTrans.Services
{
    /// <summary>
    /// Service de génération de QR-Codes pour les colis.
    /// </summary>
    public class QRCodeService
    {
        private readonly byte[] _signingKey;

        public QRCodeService(IConfiguration configuration, IWebHostEnvironment environment)
        {
            var configuredKey = configuration["QRCode:SigningKey"]
                ?? Environment.GetEnvironmentVariable("BABATRANS_QR_SIGNING_KEY");

            if (string.IsNullOrWhiteSpace(configuredKey) && environment.IsDevelopment())
                configuredKey = "BabaTrans-development-only-QR-key-change-me";

            if (string.IsNullOrWhiteSpace(configuredKey))
                throw new InvalidOperationException("La clé de signature QRCode:SigningKey doit être configurée en production.");

            _signingKey = Encoding.UTF8.GetBytes(configuredKey);
        }

        /// <summary>
        /// Génère un QR-Code en base64 à partir d'un texte.
        /// </summary>
        public string GenererQRCode(string contenu)
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(contenu, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            // 10 pixels par module suffisent pour une impression nette (image environ 4 fois plus légère qu'avec 20).
            var qrCodeBytes = qrCode.GetGraphic(10);
            return Convert.ToBase64String(qrCodeBytes);
        }

        /// <summary>
        /// Génère le contenu du QR-Code pour un colis donné.
        /// </summary>
        /// <param name="dateEnregistrement">Date d'enregistrement du colis (fixe, ne change pas à la régénération)</param>
        public string GenererContenuColis(int colisId, string codeSuivi, string description, DateTime? dateEnregistrement = null)
        {
            var dateRef = dateEnregistrement ?? DateTime.Now;
            var payload = $"BABATRANS|COLIS:{colisId}|CODE:{codeSuivi}|DESC:{description}|DATE:{dateRef:yyyy-MM-dd HH:mm}";
            using var hmac = new HMACSHA256(_signingKey);
            var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return $"{payload}|SIG:{signature}";
        }

        public bool VerifierContenuColis(string contenu)
        {
            var separator = contenu.LastIndexOf("|SIG:", StringComparison.Ordinal);
            if (separator <= 0)
                return false;

            var payload = contenu[..separator];
            var signature = contenu[(separator + 5)..].Replace('-', '+').Replace('_', '/');
            signature = signature.PadRight((signature.Length + 3) / 4 * 4, '=');

            try
            {
                var expected = Convert.FromBase64String(signature);
                using var hmac = new HMACSHA256(_signingKey);
                var actual = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
