using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ProcessVisualizing
{
    public class JwtService
    {
        private readonly string _secretKey;
        private readonly int _expirationMinutes;

        public JwtService(IConfiguration config)
        {
            _secretKey = config["Jwt:Key"];
            _expirationMinutes = int.Parse(config["Jwt:ExpireMinutes"] ?? "60");
        }

        public string GenerateToken(int userId)
        {
            var claims = new[]
            {
            new Claim("uid", userId.ToString())
        };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_expirationMinutes),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public ClaimsPrincipal? ValidateToken(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_secretKey);

            try
            {
                return tokenHandler.ValidateToken(token,
                    new TokenValidationParameters
                    {
                        ValidateIssuer = false,
                        ValidateAudience = false,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(key),
                        ClockSkew = TimeSpan.Zero
                    },
                    out _);
            }
            catch
            {
                return null;
            }
        }
    }

}
