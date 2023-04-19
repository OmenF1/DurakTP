using Microsoft.AspNetCore.Identity;

namespace Durak.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? NickName { get; set; }
        public int? GamesPlayed { get; set; }
        public int? Escapes { get; set; }
        public int? DuraksInARow { get; set; }
        public int? UltimateDuraks { get; set; }
    }
}