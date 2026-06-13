// Models/Game.cs
using ServProgProject.Models;

public class Game
{
    public Guid Id { get; } = Guid.NewGuid();
    public GameStatus Status { get; set; } = GameStatus.WaitingForPlayers;

    public string Player1Id { get; set; }
    public string Player2Id { get; set; }
    public string Player1Connection { get; set; }
    public string Player2Connection { get; set; }

    public Board Board { get; set; } = new Board();
    public string CurrentTurn { get; set; }
    public string LastJumper { get; set; }
    public int ConsecutivePasses { get; set; } = 0;

    public bool Player1Removed { get; set; }         // выполнил ли обязательное удаление
    public bool Player2Removed { get; set; }

    // Новые флаги: был ли уже первый ход (прыжок или пас)
    public bool Player1FirstTurnDone { get; set; } = false;
    public bool Player2FirstTurnDone { get; set; } = false;

    public long Version { get; set; } = 0;
    public CancellationTokenSource DisconnectCts { get; set; }

    public bool IsPlayerDisconnected(string playerId) =>
        (playerId == Player1Id && Player1Connection == null) ||
        (playerId == Player2Id && Player2Connection == null);
}