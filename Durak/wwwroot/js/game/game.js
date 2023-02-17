"use strict";

var connection = new signalR.HubConnectionBuilder().withUrl("/durak").build();
const urlParams = new URLSearchParams(window.location.search);
const groupId = urlParams.get('id');
const cardsLocation = "/images/cards/";
const tableLocation = document.getElementById("cardsInPlayArea");
var playerId = "";
var canPlayCards = false;
var playMode = 0; // 0 = play muted, 1 = attacking, 2 = defending, 3 = bombing.


//  ----------------- SignalR Messaging Start ----------------//

//  Send Chat Message.
document.getElementById("message")
    .addEventListener("keyup", function (event) {
        event.preventDefault();
        if (event.keyCode === 13) {
            var message = document.getElementById("message").value;

            connection.invoke("sendMessage", message, groupId).catch(function (err) {
                return console.error(err.toString());
            });
            document.getElementById("message").value = "";
        }
});

//  Start Game button.
document.getElementById("startGame").addEventListener("click", function (event) {
    connection.invoke("StartGame", groupId).catch(function (err) {
        return console.error(err.toString());
    });
})

//  Pickup cards button.
document.getElementById("PickUp").addEventListener("click", function (event) {
    connection.invoke("PickUp", groupId).catch(function (err) {
        return console.error(error.toString());
    });
    event.preventDefault();
});

//  Done attacking button

document.getElementById("FinishAttacking").addEventListener("click", function (event) {
    connection.invoke("FinishAttack", groupId).catch(function (err) {
        return console.error(err.toString());
    });
    event.preventDefault();
});

document.getElementById("btn-leave").addEventListener("click", function (event) {
    connection.invoke("LeaveGroup", groupId).catch(function (err) {
        return console.error(err.toString());
    });
    window.location.href = "/index"
});

//  Join Group.
connection.start().then(function () {
    connection.invoke("JoinGroup", groupId, null, null)
        .catch(function (err) {
            return console.error(err.toString());
        });
}).catch(function (err) {
    return console.error(err.toString());
});

//  Messages receveid from Hub for the chat window only.
connection.on("ReceiveMessage", function (user, message) {
    var li = document.createElement("li");
    document.getElementById("chatView").appendChild(li);
    li.textContent = `${user}: ${message}`;
});

//  Start preparing the game setting before receiving game state updates.
connection.on("StartGame", function (message) {
    document.getElementById("startWindow").style.display = "none";
    document.getElementById("playingTable").style.display = "block";
    var li = document.createElement("li");
    document.getElementById("chatView").appendChild(li);
    li.textContent = "Server: Game is starting";
});

connection.on("EndGame", function (message) {
    document.getElementById("startWindow").style.display = "block";
    document.getElementById("playingTable").style.display = "none";
});

//  This section works along side game state updates
//  I don't want game state updates to broadcast to all players what each player has in hand.
//  We therefor send each player their current hands and use this function to update the frontend.
connection.on("ReceiveCards", function (message) {
    var cardsArray = JSON.parse(message);
    UpdatePlayerHand(cardsArray);
});

//  Enable attack mode.
connection.on("EnableAttackMode", function (messaage) {
    canPlayCards = true;
    playMode = 1;
    document.getElementById("FinishAttacking").removeAttribute("disabled");
    document.getElementById("PickUp").disabled = true;
});

//  Enable defense mode.
connection.on("EnableDefenseMode", function (messaage) {
    canPlayCards = true;
    playMode = 2;
    document.getElementById("PickUp").removeAttribute("disabled");
    document.getElementById("FinishAttacking").disabled = true;
});

//  Enable defense mode.
connection.on("EnableBombMode", function (messaage) {
    canPlayCards = true;
    playMode = 3;
    document.getElementById("PickUp").disabled = true;
    document.getElementById("FinishAttacking").disabled = true;
});

//  Get current game state during play.
connection.on("ReceiveGamePlayState", function (state) {
    const data = JSON.parse(state);
    UpdateBoardAndGameState(data);
})

connection.on("RemoveCardFromHand", function (card) {
    var playerCard = document.getElementById(card);
    playerCard.remove();
})



//  ----------------- SignalR Messaging end - ----------------//

//  ----------------- Game Functions Start -------------------//
function GetCardPath(friendlyName) {
    let card = friendlyName.split("_");
    if (card[0] > 10) {
        switch (card[0]) {
            case "11":
                card[0] = "jack";
                break;
            case "12":
                card[0] = "queen";
                break;
            case "13":
                card[0] = "king";
                break;
            case "14":
                card[0] = "ace";
                break;
        }
    }
    return cardsLocation + card[0] + "_of_" + card[1] + ".png";
}

function UpdatePlayerHand(cards) {
    var playerCardsLocation = document.getElementById("playerCards");
    while (playerCardsLocation.firstChild) playerCardsLocation.removeChild(playerCardsLocation.firstChild);
    console.log(cards);
    for (var i in cards) {
        var link = document.createElement("a");
        link.href = '#';
        var img = document.createElement("img");
        img.src = GetCardPath(cards[i]);
        img.draggable = true;
        img.id = cards[i];
        img.addEventListener('dragstart', HandleDragStart);
        link.appendChild(img);
        playerCardsLocation.appendChild(link);
    }
}

function allowDrop(e) {
    e.preventDefault();
}

function HandleDragStart(ev) {
    ev.dataTransfer.setData("text/plain", ev.target.id);
}

function HandleDragDrop(ev) {
    ev.stopPropagation();
    if (playMode == 0) {
        return;
    }
    else {
        var data = ev.dataTransfer.getData("text");
        console.log(ev.target.id);
        connection.invoke("CardPlayed", data, groupId, ev.target.id).catch(function (err) {
            return console.error(err.toString());
        });
        event.preventDefault();
    }
    
}

function UpdateBoardAndGameState(data) {
    var exists = document.getElementById("nuke-img");
    if (typeof (exists) == "undefined" || exists == null) {
        var nukeImage = document.createElement("img");
        nukeImage.id = "nuke-img"
        nukeImage.src = GetCardPath(data.nuke.friendlyName);
        var deckAreaLocation = document.getElementById("deckArea");
        deckAreaLocation.appendChild(nukeImage);
    }
    ClearCardsFromTable();
    if (Object.keys(data.cardsInPlay).length > 0) {
        AddCardsToTable(data.cardsInPlay);
    }
}

function AddCardsToTable(cardsInPlay) {

    for (const [key, value] of Object.entries(cardsInPlay)) {

        var imgContainer = document.createElement("div");
        imgContainer.className = "col-3 cardInPlayDiv";

        var cardAttacking = document.createElement("img");
        cardAttacking.id = key;
        cardAttacking.className = "under";
        cardAttacking.src = GetCardPath(key);
        imgContainer.appendChild(cardAttacking);

        if (value != null) {
            var cardDefending = document.createElement("img");
            cardDefending.className = "over";
            cardDefending.src = GetCardPath(value);
            imgContainer.appendChild(cardDefending);
        }
        else {
            cardAttacking.ondrop = "HandleDragDrop(event)";
            cardAttacking.ondragover = "allowDrop(event)";
        }
        tableLocation.appendChild(imgContainer);
    }
}

function ClearCardsFromTable() {
    while (tableLocation.firstChild) tableLocation.removeChild(tableLocation.firstChild);
}
//  ----------------- Game Functions End ---------------------//