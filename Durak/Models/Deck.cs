using Microsoft.IdentityModel.Tokens;

namespace Durak.Models
{
    public class Deck
    {
        public List<Card> Cards { get; set; }
        private Random rng = new Random();
        private DeckType deckType;
        private int startingValue;

        public Deck(DeckType _deckType)
        {
            Cards = new List<Card>();
            deckType = _deckType;
            if (deckType == DeckType.Small)
            {
                startingValue = 6;
            }
            else
            {
                startingValue = 2;
            }
            Init();
        }

        private void Init()
        {
            foreach (Suites _suite in Enum.GetValues(typeof(Suites)))
            {
                for (int i = startingValue; i <= 14; i++)
                {
                    Cards.Add(new Card() { suite = _suite, value = i, state = CardState.InDeck, friendlyName = $"{i}_{_suite}"});
                }
            }

            Cards = Cards.OrderBy(a => rng.Next()).ToList();
        }

        public List<Card> DrawCard(int numberOfCards)
        {
            List<Card> cards = new List<Card>();
            for (int i = 1; i <= numberOfCards; i++ )
            {
                if (Cards.Count > 0)
                {
                    Card card = Cards.First();
                    Cards.Remove(card);
                    cards.Add(card);
                }
            }

            return cards;
        }

        public int DeckCount()
        {
            return Cards.Count();
        }

        public Card GetCardFromFriendlyName(string _friendlyName)
        {
            var values = _friendlyName.Split("_");
            return new Card() {suite = Enum.Parse<Suites>(values[1]), value = Int32.Parse(values[0]), friendlyName = _friendlyName };
        }

    }
}
