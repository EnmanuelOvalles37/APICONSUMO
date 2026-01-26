namespace Consumo_App.Servicios
{
    public class PasswordPolicy
    {
        
        public static bool IsValid(string pwd, out string? error)
        {
            if (string.IsNullOrWhiteSpace(pwd)) { error = "Contraseña vacía."; return false; }
            if (pwd.Length < 8) { error = "Mínimo 8 caracteres."; return false; }
            if (!pwd.Any(char.IsUpper)) { error = "Debe incluir mayúsculas."; return false; }
            if (!pwd.Any(char.IsLower)) { error = "Debe incluir minúsculas."; return false; }
            //if (!pwd.Any(char.IsDigit)) { error = "Debe incluir números."; return false; }
            error = null; return true;
        }
    }
}
