using System.Net.Security;

namespace Durak.Models
{
    public class Card
    {
        public Suites suite { get; set; }
        public int value { get; set; }
        public string friendlyName { get; set; }
        public CardState? state { get; set; }
    }
}

