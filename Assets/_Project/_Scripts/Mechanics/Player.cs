using System;
using System.Collections.Generic;
using Unity.Services.Lobbies.Models;

public class GamePlayer
{
    public int id;
    public string nombre;
    public string carta;
    public string palabra;

    public Dictionary<string, PlayerDataObject> Data { get; internal set; }

    public static implicit operator Unity.Services.Lobbies.Models.Player(GamePlayer v)
    {
        throw new NotImplementedException();
    }
}