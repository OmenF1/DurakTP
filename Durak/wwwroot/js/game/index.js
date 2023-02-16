"use strict";

var groupCreate = makeid(10);
var connection = new signalR.HubConnectionBuilder().withUrl("/durak").build();

//Disable the send button until connection is established.
document.getElementById("joinButton").disabled = true;

connection.start().then(function () {
    document.getElementById("joinButton").disabled = false;
    document.getElementById("groupName").value = groupCreate;
    connection.invoke("GetGamesList").catch(function (err) {
        return console.error(err.toString());
    });
}).catch(function (err) {
    return console.error(err.toString());
});

document.getElementById("joinButton").addEventListener("click", function (event) {
    var groupName = document.getElementById("groupName").value;
    var gType;
    var dType;
    if (document.getElementById("taditional").checked) {
        gType = "Traditional"
    }
    else {
        gType = "TPEdition";
    }
    if (document.getElementById("smallDeck").checked) {
        dType = "Small"
    }
    else {
        dType = "Full"
    }

    connection.invoke("JoinGroup", groupName, gType, dType)
    window.location.href = "/game?id=" + groupName;
    
});

function makeid(length) {
    var result = '';
    var characters = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
    var charactersLength = characters.length;
    for (var i = 0; i < length; i++) {
        result += characters.charAt(Math.floor(Math.random() * charactersLength));
    }
    return result;
}

connection.on("ReceiveLobbyList", function (lobbies) {
    const lobbyData = JSON.parse(lobbies);
    UpdateLobbies(lobbyData);
});

function UpdateLobbies(lobbies) {
    var Table = document.getElementById("lobbyRows");
    while (Table.firstChild) Table.removeChild(Table.firstChild)
    for (var i in lobbies) {
        console.log(lobbies[i].id);
        var row = document.createElement("tr");
        var rowId = document.createElement("td");
        rowId.textContent = lobbies[i].id;
        row.appendChild(rowId);
        var rowGType = document.createElement("td");
        if (lobbies[i].gameType == 1) {
            rowGType.textContent = "TP Edition"
        }
        else {
            rowGType.textContent = "Traditional"
        }
        row.appendChild(rowGType);
        var rowDType = document.createElement("td");
        if (lobbies[i].deckType == 1) {
            rowDType.textContent = "Full Deck";
        }
        else {
            rowDType.textContent = "Small Deck";
        }
        row.appendChild(rowDType);
        var rowCount = document.createElement("td");
        rowCount.textContent = lobbies[i].Count + " / 8";
        row.appendChild(rowCount);
        var joinButton = document.createElement("a");
        joinButton.href = "game?id=" + lobbies[i].id;
        joinButton.text = "Join";
        row.appendChild(joinButton);
        Table.appendChild(row);
    }
}

function RefreshLobbies() {
    connection.invoke("GetGamesList").catch(function (err) {
        return console.error(err.toString());
    });
}