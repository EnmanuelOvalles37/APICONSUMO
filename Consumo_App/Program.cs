// Program.cs
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Consumo_App.Data.Repositories;
using Consumo_App.Data.Sql;
using Consumo_App.Seguridad;
using Consumo_App.Servicios;
using Consumo_App.Servicios.Sql;
using Consumo_App.Services;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// =======================
// CONFIGURACIÓN
// =======================
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

// =======================
// CONTROLLERS
// =======================
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

builder.Services.AddEndpointsApiExplorer();

// =======================
// SWAGGER (SOLO DEV)
// =======================
builder.Services.AddSwaggerGen(c =>
{
    c.CustomSchemaIds(t => t.FullName!
        .Replace("+", "_")
        .Replace(".", "_"));

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Bearer {token}"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// =======================
// SQL CONNECTION (DAPPER)
// =======================
builder.Services.AddSingleton<SqlConnectionFactory>();

// =======================
// JWT AUTH
// =======================
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection.GetValue<string>("Key");

if (string.IsNullOrWhiteSpace(jwtKey))
    throw new Exception("JWT Key no configurada");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero
        };
    });

// =======================
// AUTHORIZATION (RBAC)
// =======================
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

// =======================
// CORS
// =======================

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowVercel", policy =>
    {
        policy
            .WithOrigins(
               "https://front-test-wbpl.vercel.app"
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

//http://enmanuelovalles-001-site1.stempurl.com
//https://enmanuelovalles-001-site1.stempurl.com


// =======================
// SERVICES / DI
// =======================
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IClienteService, ClienteService>();
builder.Services.AddScoped<IConsumoService, ConsumoService>();
builder.Services.AddScoped<IPermisosRepository, PermisosRepository>();
builder.Services.AddScoped<ISeguridadService, SeguridadService>();
builder.Services.AddScoped<IPasswordHasher, Pbkdf2Hasher>();
builder.Services.AddScoped<IUserContext, UserContext>();
builder.Services.AddScoped<AuthSqlService>();
builder.Services.AddScoped<ProveedorAsignacionSqlService>();

builder.Services.AddHostedService<CorteAutomaticoService>();
builder.Services.AddHttpContextAccessor();

var app = builder.Build();



// =======================
// PIPELINE
// =======================
if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}



app.UseCors("AllowVercel");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
  
app.Run();