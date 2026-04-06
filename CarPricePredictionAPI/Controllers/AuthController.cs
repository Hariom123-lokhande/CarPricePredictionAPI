using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace CarPricePredictionAPI.Controllers
{
    public class AuthController : Controller
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IConfiguration _configuration;

        public AuthController(SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> userManager, IConfiguration configuration)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _configuration = configuration;
        }

        [HttpGet("Auth/Login")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public IActionResult Login()
        {
            return View();
        }

        [HttpGet("Auth/Signup")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public IActionResult Signup()
        {
            return View();
        }

        public class LoginDto { public string Username { get; set; } = string.Empty; public string Password { get; set; } = string.Empty; }
        
        [HttpPost("api/auth/login")]
        public async Task<IActionResult> ApiLogin([FromBody] LoginDto model)
        {
            var result = await _signInManager.PasswordSignInAsync(model.Username, model.Password, false, false);
            if (result.Succeeded)
            {
                var user = await _userManager.FindByNameAsync(model.Username);
                if(user != null) {
                    var token = GenerateJwtToken(user);
                    return Ok(new { token });
                }
            }
            return Unauthorized(new { message = "Invalid login attempt." });
        }

        [HttpPost("api/auth/signup")]
        public async Task<IActionResult> ApiSignup([FromBody] LoginDto model)
        {
            var user = new IdentityUser { UserName = model.Username, Email = model.Username };
            var result = await _userManager.CreateAsync(user, model.Password);

            if (result.Succeeded)
            {
                await _signInManager.SignInAsync(user, isPersistent: false);
                var token = GenerateJwtToken(user);
                return Ok(new { token });
            }

            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        }

        private string GenerateJwtToken(IdentityUser user)
        {
            var jwtKey = _configuration["Jwt:Key"] ?? "CarPriceAI_Secure_Ultra_Secret_Key_2024";
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.UserName ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Name, user.UserName ?? string.Empty)
            };

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddHours(2),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
