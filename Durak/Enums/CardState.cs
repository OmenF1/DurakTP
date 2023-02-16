public enum CardState
{
    InDeck,
    InHand,
    Open, 
    Covered, 
    Covering,
    Burnt
}

//  This can be confusing if you don't know the game, to sum it up
//  Open is a card that has been played to the defender, but has not been defended yet (covered)
//  Covered is a card that has been played to the defender and has successfully been defended by the defender.
//  Covering is a card that was played by the defender to cover a card played by an attacker