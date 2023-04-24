﻿using Durak.Hubs;
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
        public int timerDuration = 15;
        private IHubContext<DurakHub> _hubContext;
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
                return true;
            }
        }

        public List<string> RemovePlayer(string name)
        {
            if (_players.Exists(i => i.Name == name))
            {
                var player = _players.FirstOrDefault(i => i.Name == name);
                _players.Remove(player);
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
                        return false;
            }

            //  This will change at a later stage to also account for TP edition where "bombing" is allowed.
            if (gamePlayState.attackerId != playerId && gamePlayState.defenderId != playerId  && gameType != GameType.TPEdition)
                return false;

            //  Defender can't play a defensive card before a card has even been played.
            if (playerId == gamePlayState.defenderId && gamePlayState.cardsInPlay.Count == 0)
                return false;

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
                    
                return true;
                
            }

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

                //  The card suites must be the same, however if it's the defending card is a nuke, then it may cover any other suite.
                if ((attackingCard.suite != defendingCard.suite) && (defendingCard.suite != nuke.suite))
                    return false;

                if (attackingCard.suite == nuke.suite)
                {
                    //  If the attacking card is a nuke then the defending card must be higher as per normal.
                    if (attackingCard.value > defendingCard.value)
                        return false;
                }

                if (attackingCard.value > defendingCard.value && defendingCard.suite != nuke.suite)
                    return false;

                //  The defending card is valid, cover the attacking card.
                playerHands[playerId].Remove(playerHands[playerId].Where(c => c.friendlyName == friendlyPlayedName).FirstOrDefault());
                gamePlayState.cardsInPlay[friendlyCoveredName] = friendlyPlayedName;

                if (playerHands[playerId].Count == 0)
                {
                    EndAttack();
                    gamePlayState.checkDurak = true;
                    return true;
                }    
                StartTurnTimer(timerDuration);
                return true;
            }



            return false;
        }

        public async void PickUp()
        {
            if (allowPickUpPass)
            {
                await _hubContext.Clients.Group(id).SendAsync("NotifyClientPickingUp");
                await _hubContext.Clients.Group(id).SendAsync("StartTimer", timerDuration);
                StartTurnTimer(timerDuration);
                allowPickUpPass = false;
                return;
            }
            else
            {
                allowPickUpPass = true;
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

        private void StartTurnTimer(int timerDuration)
        {
            if (playTimer != null)
            {
                playTimer.Stop();
            }

            playTimer = new System.Timers.Timer(timerDuration * 1000);
            playTimer.AutoReset = false;
            playTimer.Elapsed += OnPlayTimerElapsed;
            playTimer.Start();
        }

        private void OnPlayTimerElapsed(object sender, ElapsedEventArgs e)
        {
            callHub = true;
            if (gamePlayState.cardsInPlay.Values.Any(value => value == null))
            {
                PickUp();
            }
            else
            {
                EndAttack();
            }
        }

    }
}
