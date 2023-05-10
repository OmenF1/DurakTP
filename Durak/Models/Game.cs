using Durak.Hubs;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Timers;

namespace Durak.Models
{

    // I really want to rewrite this whole thing, but for now this will do.
    public class Game
    {
        public const int cardsNeeded = 6;
        public const int minPlayers = 2;
        public const int maxPlayers = 8;
        public Deck? deck;
        public Card? nuke;
        public GameType gameType;
        public DeckType deckType;
        public string attacker;
        public string defender;
        public bool refreshPlayerCards = false;
        public string LobbyOwner; //    The person who created the lobby.

        public GamePlayState? gamePlayState { get; set; }

        public List<Player> _players;
        public Dictionary<string, List<Card>>? playerHands;
        public GameState state { get; set; }
        public string id { get; set; }
        private System.Timers.Timer playTimer;
        public int timerDuration = 30;
        public int pickUpDuration = 10;
        private IHubContext<DurakHub> _hubContext;
        private ILogger<Game> _logger;
        private bool callHub = false;
        public bool notifyNewTurn = true;
        public bool allowPickUpPass = true;

        public Game(string _id, GameType _gameType, DeckType _deckType, IHubContext<DurakHub> hubContext)
        {
            _players = new List<Player>();
            state = GameState.Pending;
            id = _id;
            gameType = _gameType;
            deckType = _deckType;
            _hubContext = hubContext;
            _logger = LoggerFactory.Create(options => { }).CreateLogger<Game>();
        }
        public bool AddPlayer(string id, string name, string userId)
        {
            if(_players.Exists(i => i.Name == name))
            {
                _players.FirstOrDefault(i => i.Name == name).Connections.Add(id);
                return false;
            }
            else
            {
                _players.Add(new Player(name, id, userId));
                _logger.LogInformation($"{DateTime.Now} - game id: {id} - added player {name}");
                return true;
            }
        }

        public List<string> RemovePlayer(string name)
        {
            if (_players.Exists(i => i.Name == name))
            {
                var player = _players.FirstOrDefault(i => i.Name == name);
                _players.Remove(player);
                _logger.LogInformation($"{DateTime.Now} - game id: {id} - removed player {name}");
                return player.Connections;
            }
            return null;
        }

        public List<Player> GetPlayers()
        {
            return _players;
        }

        public int GetPlayerCount()
        {
            return _players.Count;
        }

        public void StartGame()
        {

            deck = new Deck(deckType);
            state = GameState.InProgress;
            nuke = deck.Cards.Last();
            playerHands = new Dictionary<string, List<Card>>();
            foreach(Player player in _players)
            {
                playerHands.Add(player.Name, deck.DrawCard(cardsNeeded));
            }
            List<Player> tempL = new List<Player>(_players);
            string _startingAttacker = GetStartingAttacker(nuke.suite);
            string _startingDefender = GetStartingDefender(_startingAttacker, _players.Select(p => p.Name).ToList());



            gamePlayState = new GamePlayState()
            {
                cardsInPlay = new Dictionary<string, string?>(),
                nuke = nuke,
                playerOrder = _players.Select(p => p.Name).ToList(),
                tableOrder = _players.Select(p => p.Name).ToList(),
                attackerId = _startingAttacker,
                defenderId = _startingDefender,
                cardsRemaining = deck.DeckCount()
            };
            refreshPlayerCards = true;
        }

        public bool CardPlayed(string playerId, string friendlyPlayedName, string? friendlyCoveredName = null)
        {
            
            //  This just stops players from trying to cover the same card more than once.
            if (!string.IsNullOrEmpty(friendlyCoveredName))
            {
                if (gamePlayState.cardsInPlay.ContainsKey(friendlyCoveredName))
                    if (gamePlayState.cardsInPlay[friendlyCoveredName] != null)
                        _logger.LogInformation($"{DateTime.Now} - game id: {id} - {playerId} tried to cover a card that was already covered");
                        return false;
            }

            //  This will change at a later stage to also account for TP edition where "bombing" is allowed.
            if (gamePlayState.attackerId != playerId && gamePlayState.defenderId != playerId  && gameType != GameType.TPEdition)
                return false;

            //  Defender can't play a defensive card before a card has even been played.
            if (playerId == gamePlayState.defenderId && gamePlayState.cardsInPlay.Count == 0)
            {
                _logger.LogInformation($"{DateTime.Now} - game id: {id} - {playerId} tried to defend without any cards been attacked");
                return false;
            }
            

            //  This will stop a player from getting attacked further even though they're out of cards.
            //  It doesn't fully solve the problem though, but I can't impliment this logic properly until I've got timers.
            if (playerId != gamePlayState.defenderId && playerHands[gamePlayState.defenderId].Count == 0)
                return false;

            //  On the opening attack card for the round there's no further processing that needs be done.
            if (playerId != gamePlayState.defenderId && gamePlayState.cardsInPlay.Count == 0)
            {
                // check this o,0
                playerHands[playerId].Remove(playerHands[playerId].Where(c => c.friendlyName == friendlyPlayedName).FirstOrDefault());
                gamePlayState.cardsInPlay.Add(friendlyPlayedName, null);
                StartTurnTimer(timerDuration);
                _logger.LogInformation($"{DateTime.Now} - game id: {id} - {playerId} opened the attack with {friendlyCoveredName}");
                return true;
                
            }

            //  prevent a defender from being attacked with more cards than what is in their hand.
            if (playerId != gamePlayState.defenderId && playerHands[gamePlayState.defenderId].Count == gamePlayState.cardsInPlay.Count(kvp => kvp.Value == null) && allowPickUpPass)
                return false;

            //  This method makes me want to hurl, but it's just temporary.
            if (playerId != gamePlayState.defenderId)
            {
                var cardVal = friendlyPlayedName.Split("_")[0];

                foreach (KeyValuePair<string, string> kvp  in gamePlayState.cardsInPlay)
                {
                    if (kvp.Key.Split("_")[0] == cardVal || kvp.Value.Split("_")[0] == cardVal)
                    {
                        playerHands[playerId].Remove(playerHands[playerId].Where(c => c.friendlyName == friendlyPlayedName).FirstOrDefault());
                        gamePlayState.cardsInPlay.Add(friendlyPlayedName, null);
                        if (!allowPickUpPass)
                            StartTurnTimer(timerDuration);
                        return true;
                    }
                        
                }
                return false;
            }

            //  Here we're checking passing on.  I'm doing this very loosely for now and we will refine this at a later stage, I just want to
            //  get its basic functionality in for now.
            if (playerId == gamePlayState.defenderId && !string.IsNullOrEmpty(friendlyPlayedName) && string.IsNullOrEmpty(friendlyCoveredName) && gamePlayState.cardsInPlay.Count > 0 && gameType == GameType.TPEdition)
            {
                Card firstCard = deck.GetCardFromFriendlyName(gamePlayState.cardsInPlay.FirstOrDefault().Key);
                Card playedCard = deck.GetCardFromFriendlyName(friendlyPlayedName);
                if (firstCard.value != playedCard.value)
                    return false;

                var valid = gamePlayState.cardsInPlay.Count(x => !string.IsNullOrEmpty(x.Value));
                if (valid > 0)
                    return false;

                var nextPlayerID = GetStartingDefender(playerId, _players.Select(p => p.Name).ToList());
                if (playerHands[nextPlayerID].Count < gamePlayState.cardsInPlay.Count + 1)
                    return false;

                playerHands[playerId].Remove(playerHands[playerId].Where(c => c.friendlyName == friendlyPlayedName).FirstOrDefault());
                gamePlayState.cardsInPlay.Add(friendlyPlayedName, null);

                gamePlayState.defenderId = nextPlayerID;
                if (gamePlayState.attackerId == nextPlayerID)
                    gamePlayState.attackerId = playerId;

                StartTurnTimer(timerDuration);
                return true;
            }

            //  Check if defender has made a valid defensive move.
            if (playerId == gamePlayState.defenderId && !string.IsNullOrEmpty(friendlyCoveredName) && !string.IsNullOrEmpty(friendlyPlayedName) && allowPickUpPass)
            {
                Card attackingCard = deck.GetCardFromFriendlyName(friendlyCoveredName);
                Card defendingCard = deck.GetCardFromFriendlyName(friendlyPlayedName);
                _logger.LogInformation($"{DateTime.Now} - {playerId} tried to defend {friendlyCoveredName} with {friendlyPlayedName}");

                //  The card suites must be the same, however if it's the defending card is a nuke, then it may cover any other suite.
                if ((attackingCard.suite != defendingCard.suite) && (defendingCard.suite != nuke.suite))
                {
                    _logger.LogInformation($"{DateTime.Now} - game id: {id} - {playerId} defense declined because suites did not match.");
                    return false; 
                }

                if (attackingCard.suite == nuke.suite)
                {
                    //  If the attacking card is a nuke then the defending card must be higher as per normal.
                    if (attackingCard.value > defendingCard.value)
                    {
                        _logger.LogInformation($"{DateTime.Now} - game id: {id} - {playerId} defense declined because attacking card is nuke and defending card is lower value");
                        return false;
                    }
                }

                if (attackingCard.value > defendingCard.value && defendingCard.suite != nuke.suite)
                {
                    _logger.LogInformation($"{DateTime.Now} - game id: {id} - {playerId} defending card not valid");
                    return false;
                }

                //  The defending card is valid, cover the attacking card.
                playerHands[playerId].Remove(playerHands[playerId].Where(c => c.friendlyName == friendlyPlayedName).FirstOrDefault());
                gamePlayState.cardsInPlay[friendlyCoveredName] = friendlyPlayedName;

                if (playerHands[playerId].Count == 0)
                {
                    EndAttack();
                    gamePlayState.checkDurak = true;
                    return true;
                }
                _logger.LogInformation($"{DateTime.Now} - game id: {id} - {playerId} defense allowed.");
                StartTurnTimer(timerDuration);
                return true;

            }


            _logger.LogInformation($"{DateTime.Now} - game id: {id} - {playerId} card played {friendlyPlayedName} went into catchall return false");
            return false;
        }

        //  Defender is picking up or conceding the round.
        public async void PickUp()
        {
            if (allowPickUpPass)
            {
                await _hubContext.Clients.Group(id).SendAsync("NotifyClientPickingUp");
                await _hubContext.Clients.Group(id).SendAsync("StartTimer", pickUpDuration);
                StartTurnTimer(pickUpDuration);
                _logger.LogInformation($"{DateTime.Now} - game id: {id} - defender picking up");
                allowPickUpPass = false;
                return;
            }
            else
            {
                allowPickUpPass = true;
                _logger.LogInformation($"{DateTime.Now} - game id: {id} - defender picked up");
            }


            foreach (KeyValuePair<string, string> kvp in gamePlayState.cardsInPlay)
            {
                playerHands[gamePlayState.defenderId].Add(deck.GetCardFromFriendlyName(kvp.Key));
                if (kvp.Value != null)
                    playerHands[gamePlayState.defenderId].Add(deck.GetCardFromFriendlyName(kvp.Value));

            }
            gamePlayState.cardsInPlay.Clear();
            foreach (KeyValuePair<string, List<Card>> kvp in playerHands)
            {
                if (kvp.Value.Count < cardsNeeded && deck.DeckCount() > 0)
                {
                    kvp.Value.AddRange(deck.DrawCard(cardsNeeded - kvp.Value.Count));
                }

            }
            gamePlayState.cardsRemaining = deck.DeckCount();
            refreshPlayerCards = true;
            UpdateGameState(false);
        }

        //  Defender has successfully defended the attack.
        public bool EndAttack()
        {
            foreach (KeyValuePair<string, string> kvp in gamePlayState.cardsInPlay)
            {
                //  This just prevents the cards from being wiped from the game by mistake by the attacker.
                if (kvp.Value == null)
                    return false;
            }

            gamePlayState.cardsInPlay.Clear();
            foreach (KeyValuePair<string, List<Card>> kvp in playerHands)
            { 
                if (kvp.Value.Count < cardsNeeded && deck.DeckCount() > 0)
                {
                    kvp.Value.AddRange(deck.DrawCard(cardsNeeded - kvp.Value.Count));
                }
            }
            gamePlayState.cardsRemaining = deck.DeckCount();
            refreshPlayerCards = true;
            UpdateGameState(true);
            return true;
        }

        //  This is really just another way of saying the round is over, set up the next round.  Bad naming?
        public async void UpdateGameState(bool defended)
        {
            playTimer.Stop();
            SetNextPlayers(gamePlayState.attackerId, gamePlayState.defenderId, defended);
            notifyNewTurn = true;
            //  Fuck this is bad, but I actually don't know how else to do it, I will research more into this later.
            if (callHub)
            {
                await _hubContext.Clients.Group(id).SendAsync("TimerEnd");
                callHub = false;
            }
        }

        //  Find the player who will start the game.
        //  This is the player who has the lowest nuke.
        private string GetStartingAttacker(Suites nuke)
        {
            string startingPlayer = "";
            int startingPlayerValue = 15;
            foreach (KeyValuePair<string, List<Card>> kvp in playerHands)
            {
                foreach(Card card in kvp.Value)
                {
                    if (card.suite == nuke)
                    {
                        if (startingPlayerValue > card.value)
                        {
                            startingPlayer = kvp.Key;
                            startingPlayerValue = card.value;
                        }
                    }
                }
            }
            return startingPlayer;
        }

        //  This is the person to the attackers left.
        //  In this implimentation this is just attackerIndex  + 1
        private string GetStartingDefender(string attackerId, List<string> players)
        {
            int index = players.FindIndex(a => a == attackerId);
            if (index == players.Count -1)
            {
                return players[0];
            }
            return players[index + 1];
        }

        //  bool defended indicates if the defending player was able to successfully defend the previous round, if not they forfeight their turn to attack.
        private void SetNextPlayers(string attackerId, string defenderId, bool defended)
        {
            foreach(string player in gamePlayState.playerOrder.ToList())
            {
                if (playerHands[player].Count == 0)
                {
                    gamePlayState.playerOrder.Remove(player);
                }
            }
            if (gamePlayState.playerOrder.Count < 2)
                return;

            if (defended && playerHands[defenderId].Count > 0)
            {
                gamePlayState.attackerId = defenderId;
                if (gamePlayState.playerOrder.FindIndex(a => a == defenderId) == gamePlayState.playerOrder.Count - 1)
                {
                    gamePlayState.defenderId = gamePlayState.playerOrder[0];
                    return;
                }
                gamePlayState.defenderId = gamePlayState.playerOrder[gamePlayState.playerOrder.FindIndex(a => a == defenderId) + 1];
                return;
            }
            else
            {
                if (gamePlayState.playerOrder.FindIndex(a => a == defenderId) == gamePlayState.playerOrder.Count - 1)
                {
                    gamePlayState.attackerId = gamePlayState.playerOrder[0];
                    gamePlayState.defenderId = gamePlayState.playerOrder[1];
                    return;
                }
                gamePlayState.attackerId = gamePlayState.playerOrder[gamePlayState.playerOrder.FindIndex(a => a == defenderId) + 1];
                if (gamePlayState.playerOrder.FindIndex(a => a == gamePlayState.attackerId) == gamePlayState.playerOrder.Count - 1)
                {
                    gamePlayState.defenderId = gamePlayState.playerOrder[0];
                    return;
                }
                gamePlayState.defenderId = gamePlayState.playerOrder[gamePlayState.playerOrder.FindIndex(a => a == gamePlayState.attackerId) + 1];
                return;

            }
        }

        //  Timer for the round so that the game doesn't just stall if somebody is not playing.
        private void StartTurnTimer(int timerDuration)
        {
            if (playTimer != null)
            {
                playTimer.Stop();
            }
            _logger.LogInformation($"{DateTime.Now} - game id: {id} - turn timer started.");
            playTimer = new System.Timers.Timer(timerDuration * 1000);
            playTimer.AutoReset = false;
            playTimer.Elapsed += OnPlayTimerElapsed;
            playTimer.Start();
        }

        //  The specified time for the round / event has elapsed.
        private void OnPlayTimerElapsed(object sender, ElapsedEventArgs e)
        {
            callHub = true;
            if (gamePlayState.cardsInPlay.Values.Any(value => value == null))
            {
                _logger.LogInformation($"{DateTime.Now} - game id: {id} - timer elapsed calling pickup");
                PickUp();

            }
            else
            {
                _logger.LogInformation($"{DateTime.Now} - game id: {id} - timer elapsed calling end attack");
                EndAttack();
            }
        }

    }
}
