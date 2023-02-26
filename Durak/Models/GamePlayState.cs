namespace Durak.Models
{
    public class GamePlayState
    {
        public string attackerId { get; set; }
        public string defenderId { get; set; }
        public Card nuke { get; set; }
        public Dictionary<string, string?>? cardsInPlay  {get; set;} 
        public List<string> playerOrder { get; set; }
        public List<string> tableOrder { get; set; }
        public int cardsRemaining { get; set; }
        public bool checkDurak;
    }
}

//  Game State will send be used t keep clients updated with who the current attacker is, who the current defender is,
//  what the nuke for the round is, and what has been played and their positions.
