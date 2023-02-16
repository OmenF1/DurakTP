using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Durak.Pages
{
    [Authorize]
    public class gameModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
