namespace Durak.Models
{
    public class GamePlayState
    {
        public string attackerId { get; set; }
        public string defenderId { get; set; }
        public Card nuke { get; set; }

        //  This makes more sense to me than what I was going for before.
        //  The second card object needs to be nullable, because that value is only added once a defender covers the key value (attacking card)
        //  Key == attacking card | value == defending card.
        //  I'm also using a string for now as it's not liking the objects when converting to json.
        public Dictionary<string, string?>? cardsInPlay  {get; set;} 
        public List<string> playerOrder { get; set; }
        public List<string> tableOrder { get; set; }
        public int cardsRemaining { get; set; }
        public bool checkDurak;
    }
}

//  Game State will send be used t keep clients updated with who the current attacker is, who the current defender is,
//  what the nuke for the round is, and what has been played and their positions.
