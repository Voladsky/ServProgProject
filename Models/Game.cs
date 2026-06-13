namespace ServProgProject.Models
{
    // Models/Game.cs
    public class Game
    {
        public Guid Id { get; } = Guid.NewGuid();
        public GameStatus Status { get; set; } = GameStatus.WaitingForPlayers;

        public string Player1Id { get; set; }   // token
        public string Player2Id { get; set; }
        public string Player1Connection { get; set; }
        public string Player2Connection { get; set; }

        public Board Board { get; set; } = new Board();
        public string CurrentTurn { get; set; }          // token of player who moves now
        public string LastJumper { get; set; }           // token of last player who made a jump
        public int ConsecutivePasses { get; set; } = 0;

        public bool Player1Removed { get; set; }         // has Player1 performed mandatory removal?
        public bool Player2Removed { get; set; }

        // Disconnect timeout handling
        public CancellationTokenSource DisconnectCts { get; set; }
        public bool IsPlayerDisconnected(string playerId) =>
            (playerId == Player1Id && Player1Connection == null) ||
            (playerId == Player2Id && Player2Connection == null);
    }
}
